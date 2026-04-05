using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using VR.HDRFix.Configs;
using VR.HDRFix.Enums;
using VR.HDRFix.Models;

namespace VR.HDRFix.Helpers;

public static class HdrPipeline
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Process(FileStream inputFileStream, string outputPath, HdrFixSettings settings)
    {
        var decoder = new WmpBitmapDecoder(inputFileStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        int width = frame.PixelWidth;
        int height = frame.PixelHeight;

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Rgba128Float, null, 0);

        int stride = width * 16;
        float[] rawPixels = new float[width * height * 4];
        converted.CopyPixels(rawPixels, stride, 0);

        var pixels = new Vector3[width * height];

        Parallel.For(0, pixels.Length, i =>
        {
            int offset = i * 4;
            pixels[i] = new Vector3(rawPixels[offset], rawPixels[offset + 1], rawPixels[offset + 2]);
        });

        float hdrMax = (10000.0f / HdrMath.SdrWhite);

        var histogram = new HdrHistogram(pixels);

        float exposureScale = (float)Math.Pow(2.0, settings.Exposure);
        float scale = exposureScale * 0.5f / 0.5f;

        hdrMax *= scale;

        const float preLevelsMin = 0.0f;
        const float preLevelsMax = 1.0f;
        const float postLevelsMin = 0.0f;
        const float postLevelsMax = 1.0f;

        byte[] outPixels = new byte[width * height * 3];

        Parallel.For(0, pixels.Length, i =>
        {
            var pixel = pixels[i];

            pixel = HdrMath.ApplyLevels(pixel, preLevelsMin, preLevelsMax, settings.PreGamma);
            pixel *= scale;
            pixel = ApplyToneMap(pixel, settings, hdrMax);
            pixel = HdrMath.ApplyLevels(pixel, postLevelsMin, postLevelsMax, settings.PostGamma);
            pixel = ApplyColorMap(pixel, settings.ColorMap);
            pixel = HdrMath.LinearToSrgb(pixel);
            pixel = HdrMath.Clip(pixel);

            int offset = i * 3;

            outPixels[offset] = (byte)(pixel.X * 255.0f);
            outPixels[offset + 1] = (byte)(pixel.Y * 255.0f);
            outPixels[offset + 2] = (byte)(pixel.Z * 255.0f);
        });

        var outBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, outPixels, width * 3);
        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = 95
        };

        encoder.Frames.Add(BitmapFrame.Create(outBitmap));

        using (var outputFileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            encoder.Save(outputFileStream);
        }
    }

    private static Vector3 ApplyToneMap(Vector3 pixel, HdrFixSettings settings, float hdrMax)
    {
        switch (settings.ToneMap)
        {
            case ToneMap.Linear:
                return pixel;

            case ToneMap.Reinhard:
                return HdrMath.TonemapReinhardOklab(pixel, hdrMax, settings.Saturation);

            case ToneMap.ReinhardRgb:
                return HdrMath.TonemapReinhardRgb(pixel, hdrMax);

            case ToneMap.Aces:
                return HdrMath.TonemapAces(pixel);

            case ToneMap.Uncharted2:
                return HdrMath.TonemapUncharted2(pixel);

            case ToneMap.Hable:
            default:
                return HdrMath.TonemapHable(pixel);
        }
    }

    private static Vector3 ApplyColorMap(Vector3 pixel, ColorMap colorMapMode)
    {
        switch (colorMapMode)
        {
            case ColorMap.Darken:
                return HdrMath.ColorDarkenOklab(pixel);

            case ColorMap.Desaturate:
            case ColorMap.DesaturateOklab:
                return HdrMath.ColorDesatOklab(pixel);

            case ColorMap.Clip:
            default:
                return HdrMath.Clip(pixel);
        }
    }
}