using System.IO;

using VR.HDRFix.Configs;
using VR.HDRFix.Enums;
using VR.HDRFix.Helpers;

namespace VR.HDRFix.Tester
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Starting HDR to SDR Test...");

            string watchFolder = @"D:\Videos\NVIDIA";
            string outputPath = @"D:\Pictures\NVIDIA Screenshots";

            if (!Directory.Exists(watchFolder))
            {
                Console.WriteLine($"Error: Watch folder not found at {watchFolder}");
                return;
            }

            var options = new HdrFixSettings
            {
                PreGamma = 1.0f,
                Exposure = 0.0f,
                Saturation = 1.0f,
                PostGamma = 1.0f,
                ToneMap = ToneMap.Hable,
                ColorMap = ColorMap.Clip
            };

            string[] files = Directory.GetFiles(watchFolder, "*.jxr", SearchOption.AllDirectories);

            if (files.Length == 0)
            {
                Console.WriteLine("No .jxr files found in watch folder or its subdirectories.");
                return;
            }

            foreach (string fileFullPath in files)
            {
                try
                {
                    Console.WriteLine($"Processing {fileFullPath}...");

                    string fileDir = Path.GetDirectoryName(fileFullPath)!;
                    string relativeDir = Path.GetRelativePath(watchFolder, fileDir);

                    if (relativeDir == ".")
                        relativeDir = string.Empty;

                    string targetDirectory;

                    if (string.IsNullOrWhiteSpace(outputPath))
                    {
                        targetDirectory = fileDir;
                    }
                    else
                    {
                        targetDirectory = Path.Combine(outputPath, relativeDir);
                    }

                    if (!Directory.Exists(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    string outputFileName = Path.GetFileNameWithoutExtension(fileFullPath) + "-sdr.jpg";
                    string targetFilePath = Path.Combine(targetDirectory, outputFileName);

                    if (File.Exists(targetFilePath))
                    {
                        Console.WriteLine($"File already exists, skipping: {targetFilePath}");
                        continue;
                    }

                    using (var stream = new FileStream(fileFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        HdrPipeline.Process(stream, targetFilePath, options);
                    }

                    Console.WriteLine($"Success! Saved to {targetFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {fileFullPath}: {ex.Message}");
                }
            }

            Console.WriteLine("Finished processing all files.");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}