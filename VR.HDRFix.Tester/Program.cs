using System.IO;

namespace VR.HDRFix.Tester
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Starting HDR to SDR Test...");

            // Задай свої тестові шляхи
            string inputPath = @"D:\Videos\NVIDIA\Cyberpunk 2077\Cyberpunk 2077 Screenshot 2026.03.27 - 18.56.52.51.jxr";
            string outputPath = @"D:\Videos\NVIDIA\test_image-sdr.jpg";

            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file not found at {inputPath}");
                return;
            }

            // Задаємо налаштування вручну, імітуючи те, що робить сервіс через appsettings.json
            var options = new HdrFixOptions
            {
                PreGamma = 2.0f,
                Exposure = -4.0f,
                Saturation = 1.5f,
                PostGamma = 0.5f,
                ToneMap = "hable",
                ColorMap = "clip"
            };

            try
            {
                Console.WriteLine($"Processing {inputPath}...");

                // Викликаємо наш статичний метод напряму
                HdrPipeline.Process(inputPath, outputPath, options);

                Console.WriteLine($"Success! Saved to {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}