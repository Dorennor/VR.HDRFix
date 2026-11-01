using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using VR.HDRFix.Configs;
using VR.HDRFix.Enums;
using VR.HDRFix.Models;

namespace VR.HDRFix.Helpers;

public readonly record struct ConversionResult(int Width, int Height, float WhitePointNits, float PeakNits);

public static class HdrPipeline
{
    /// <summary>
    /// Converts an HDR file to SDR. The output is written to a temp file and moved into place,
    /// so a partially written image never exists under the final name.
    /// </summary>
    public static ConversionResult ConvertFile(string inputPath, string outputPath, HdrFixSettings settings, int maxThreads = 0)
    {
        settings = settings.Sanitized();

        HdrImage image;

        using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            image = Decode(input);
        }

        var mapper = ToneMapper.Create(image, settings);
        int width = image.Width;
        int height = image.Height;
        byte[] rgb = Render(image, mapper, maxThreads);

        string? directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = outputPath + ".tmp";

        try
        {
            using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Encode(rgb, width, height, output, settings);
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        CopyTimestamps(inputPath, outputPath);

        return new ConversionResult(width, height, mapper.WhitePointNits, mapper.PeakNits);
    }

    /// <summary>Decodes a JPEG XR (or any WIC-supported) image into linear scRGB floats.</summary>
    public static HdrImage Decode(Stream input)
    {
        var decoder = new WmpBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        BitmapSource frame = decoder.Frames[0];

        int width = frame.PixelWidth;
        int height = frame.PixelHeight;

        // WIC treats float formats as linear scRGB, so half-float, float and 8-bit sources all land in the same space.
        BitmapSource source = frame.Format == PixelFormats.Rgba128Float
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Rgba128Float, null, 0);

        var pixels = new float[(long)width * height * 4];
        source.CopyPixels(pixels, width * 16, 0);

        return new HdrImage(width, height, pixels);
    }

    /// <summary>Tone maps the image into 8-bit sRGB (RGB24).</summary>
    public static byte[] Render(HdrImage image, ToneMapper mapper, int maxThreads = 0)
    {
        int width = image.Width;
        float[] src = image.Pixels;
        byte[] dst = new byte[image.PixelCount * 3];

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxThreads > 0 ? maxThreads : Math.Max(1, Environment.ProcessorCount / 2)
        };

        Parallel.For(0, image.Height, options, y =>
        {
            int s = y * width * 4;
            int d = y * width * 3;

            for (int x = 0; x < width; x++, s += 4, d += 3)
            {
                var c = mapper.Map(new Vector3(src[s], src[s + 1], src[s + 2]));

                dst[d] = HdrMath.LinearToSrgbByte(c.X);
                dst[d + 1] = HdrMath.LinearToSrgbByte(c.Y);
                dst[d + 2] = HdrMath.LinearToSrgbByte(c.Z);
            }
        });

        return dst;
    }

    public static void Encode(byte[] rgb, int width, int height, Stream output, HdrFixSettings settings)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, rgb, width * 3);

        BitmapEncoder encoder = settings.OutputFormat switch
        {
            OutputFormat.Png => new PngBitmapEncoder(),
            _ => new JpegBitmapEncoder { QualityLevel = settings.JpegQuality }
        };

        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(output);
    }

    private static void CopyTimestamps(string from, string to)
    {
        try
        {
            File.SetCreationTimeUtc(to, File.GetCreationTimeUtc(from));
            File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
        }
        catch
        {
            // Not important enough to fail the conversion.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }
}