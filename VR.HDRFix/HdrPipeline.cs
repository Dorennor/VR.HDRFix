using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VR.HDRFix;

public static class HdrPipeline
{
    public static void Process(string inputPath, string outputPath, HdrFixOptions options)
    {
        // 1. Читаємо JXR через Windows Imaging Component (нативно, без милиць)
        using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = new WmpBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        int width = frame.PixelWidth;
        int height = frame.PixelHeight;

        // WIC автоматично приводить HDR формат (Half-Float) до нормального 32-bit Float
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Rgba128Float, null, 0);

        // Копіюємо сирі байти (4 канали RGBA * 4 байти на один float = 16 байт на піксель)
        int stride = width * 16;
        float[] rawPixels = new float[width * height * 4];
        converted.CopyPixels(rawPixels, stride, 0);

        var pixels = new Vector3[width * height];

        Parallel.For(0, pixels.Length, i =>
        {
            int offset = i * 4;
            // RGB дані (A-канал [offset+3] нам для тонмаппінгу не потрібен)
            pixels[i] = new Vector3(rawPixels[offset], rawPixels[offset + 1], rawPixels[offset + 2]);
        });

        // 2. Вся наша математика (без змін!)
        float hdrMax = (10000.0f / HdrMath.SDR_WHITE);
        var histogram = new HdrHistogram(pixels);
        float exposureScale = (float)Math.Pow(2.0, options.Exposure);
        float scale = exposureScale * 0.5f / 0.5f;
        hdrMax *= scale;

        float preLevelsMin = 0.0f, preLevelsMax = 1.0f;
        float postLevelsMin = 0.0f, postLevelsMax = 1.0f;

        // Одразу готуємо байтовий масив для вихідного JPEG (RGB24, по 3 байти на піксель)
        byte[] outPixels = new byte[width * height * 3];

        Parallel.For(0, pixels.Length, i =>
        {
            var p = pixels[i];

            p = HdrMath.ApplyLevels(p, preLevelsMin, preLevelsMax, options.PreGamma);
            p *= scale;
            p = ApplyToneMap(p, options, hdrMax);
            p = HdrMath.ApplyLevels(p, postLevelsMin, postLevelsMax, options.PostGamma);
            p = ApplyColorMap(p, options.ColorMap);
            p = HdrMath.LinearToSrgb(p);
            p = HdrMath.Clip(p);

            // Переводимо 0.0-1.0 у 0-255 і пишемо в цільовий масив
            int offset = i * 3;
            outPixels[offset] = (byte)(p.X * 255.0f);     // R
            outPixels[offset + 1] = (byte)(p.Y * 255.0f); // G
            outPixels[offset + 2] = (byte)(p.Z * 255.0f); // B
        });

        // 3. Зберігаємо SDR JPEG через WIC
        var outBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, outPixels, width * 3);
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(outBitmap));

        using var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        encoder.Save(outFs);
    }

    private static Vector3 ApplyToneMap(Vector3 pixel, HdrFixOptions options, float hdrMax)
    {
        return options.ToneMap.ToLower() switch
        {
            "linear" => pixel,
            "reinhard" => HdrMath.TonemapReinhardOklab(pixel, hdrMax, options.Saturation),
            "reinhard-rgb" => HdrMath.TonemapReinhardRgb(pixel, hdrMax),
            "aces" => HdrMath.TonemapAces(pixel),
            "uncharted2" => HdrMath.TonemapUncharted2(pixel),
            "hable" => HdrMath.TonemapHable(pixel),
            _ => HdrMath.TonemapHable(pixel)
        };
    }

    private static Vector3 ApplyColorMap(Vector3 pixel, string colorMapMode)
    {
        return colorMapMode.ToLower() switch
        {
            "clip" => HdrMath.Clip(pixel),
            "darken" => HdrMath.ColorDarkenOklab(pixel),
            "desaturate" => HdrMath.ColorDesatOklab(pixel),
            "desaturate-oklab" => HdrMath.ColorDesatOklab(pixel),
            _ => HdrMath.Clip(pixel)
        };
    }
}