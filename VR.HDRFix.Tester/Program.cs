using System.Diagnostics;
using System.IO;

using VR.HDRFix.Configs;
using VR.HDRFix.Enums;
using VR.HDRFix.Helpers;

namespace VR.HDRFix.Tester;

/// <summary>
/// Manual converter using the same pipeline as the service.
/// Usage: VR.HDRFix.Tester [input file or folder] [output folder] [--force] [--png] [--white=NITS] [--exposure=STOPS] [--saturation=X] [--contrast=X]
/// </summary>
internal static class Program
{
    private const string DefaultInput = @"D:\Videos\NVIDIA";
    private const string DefaultOutput = @"D:\Videos\TEST\NVIDIA Screenshots";

    private static int Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        bool force = args.Contains("--force");

        string input = positional.Length > 0 ? positional[0] : DefaultInput;
        string? output = positional.Length > 1 ? positional[1] : DefaultOutput;

        var settings = new HdrFixSettings
        {
            OutputFormat = args.Contains("--png") ? OutputFormat.Png : OutputFormat.Jpeg,
            WhitePointNits = Option(args, "--white", 0.0f),
            Exposure = Option(args, "--exposure", 0.0f),
            Saturation = Option(args, "--saturation", new HdrFixSettings().Saturation),
            Contrast = Option(args, "--contrast", new HdrFixSettings().Contrast)
        };

        string root;
        string[] files;

        if (File.Exists(input))
        {
            root = Path.GetDirectoryName(Path.GetFullPath(input))!;
            files = [input];
        }
        else if (Directory.Exists(input))
        {
            root = input;
            files = Directory.GetFiles(input, StringConstants.JxrFilter, SearchOption.AllDirectories);
        }
        else
        {
            Console.WriteLine($"Input not found: {input}");
            return 1;
        }

        Console.WriteLine($"Converting {files.Length} file(s)...");
        int failed = 0;

        foreach (var file in files)
        {
            string target = OutputPaths.GetOutputPath(file, root, output, settings.OutputFormat);

            if (!force && File.Exists(target))
            {
                Console.WriteLine($"  skip (exists)  {target}");
                continue;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var r = HdrPipeline.ConvertFile(file, target, settings);
                Console.WriteLine($"  ok  {sw.ElapsedMilliseconds,5} ms  white {r.WhitePointNits,4:F0} nits  peak {r.PeakNits,5:F0} nits  {target}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAILED  {file}: {ex.Message}");
            }
        }

        return failed == 0 ? 0 : 2;
    }

    private static float Option(string[] args, string name, float fallback)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal));
        return arg != null && float.TryParse(arg[(name.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}