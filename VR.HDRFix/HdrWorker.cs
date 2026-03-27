using System.IO;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VR.HDRFix
{
    public class HdrWorker : BackgroundService
    {
        private readonly ILogger<HdrWorker> _logger;
        private readonly IOptionsMonitor<HdrFixOptions> _options;
        private FileSystemWatcher? _watcher;

        public HdrWorker(ILogger<HdrWorker> logger, IOptionsMonitor<HdrFixOptions> options)
        {
            _logger = logger;
            _options = options;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var config = _options.CurrentValue;

            if (!Directory.Exists(config.WatchFolder))
            {
                _logger.LogError("Directory {WatchFolder} does not exist. Waiting...", config.WatchFolder);
                // Тут можна додати логіку очікування створення папки, якщо потрібно
                return Task.CompletedTask;
            }

            _watcher = new FileSystemWatcher(config.WatchFolder)
            {
                Filter = "*.jxr",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreated;
            _watcher.Changed += OnFileCreated; // Для файлів, які дописуються

            _logger.LogInformation("HDRFix Service started. Watching folder: {Folder}", config.WatchFolder);

            // Тримаємо сервіс живим, поки не прийде сигнал зупинки
            return Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            if (!e.Name!.EndsWith(".jxr", StringComparison.OrdinalIgnoreCase)) return;

            string outputPath = Path.ChangeExtension(e.FullPath, "-sdr.jpg");
            if (File.Exists(outputPath)) return;

            // Запускаємо обробку у фоні, щоб не блокувати FileSystemWatcher
            Task.Run(() => ProcessFileSafe(e.FullPath, outputPath));
        }

        private void ProcessFileSafe(string inputPath, string outputPath)
        {
            int maxRetries = 10;
            int delayMs = 500;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // Перевіряємо, чи NVIDIA вже відпустила файл
                    using (var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }

                    // !!! МАГІЯ ТУТ !!!
                    // Беремо найсвіжіші налаштування на момент конвертації
                    var currentSettings = _options.CurrentValue;

                    _logger.LogInformation("Processing {File} with Exposure: {Exp}, Saturation: {Sat}",
                        Path.GetFileName(inputPath), currentSettings.Exposure, currentSettings.Saturation);

                    // Виклик пайплайну конвертації, передаємо туди актуальні налаштування
                    // HdrPipeline.Process(inputPath, outputPath, currentSettings);

                    _logger.LogInformation("Successfully converted: {File}", Path.GetFileName(outputPath));
                    return;
                }
                catch (IOException)
                {
                    // Файл ще заблокований, чекаємо
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file: {File}", inputPath);
                    return;
                }
            }

            _logger.LogWarning("Failed to access {File} after {Retries} retries.", inputPath, maxRetries);
        }

        public override void Dispose()
        {
            _watcher?.Dispose();
            base.Dispose();
        }
    }
}