using System.IO;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Serilog;

using VR.HDRFix.Configs;
using VR.HDRFix.Helpers;

namespace VR.HDRFix
{
    public class HdrWorker : BackgroundService
    {
        private readonly IOptionsMonitor<Settings> _optionsMonitor;
        private readonly List<FileSystemWatcher> _watchers = [];
        private IDisposable _configChangeToken;

        public HdrWorker(IOptionsMonitor<Settings> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            SetupWatchers(_optionsMonitor.CurrentValue);

            _configChangeToken = _optionsMonitor.OnChange(newConfig =>
            {
                Log.Information("appsettings.json changed! Re-initializing watchers...");
                SetupWatchers(newConfig);
            });

            return Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private void SetupWatchers(Settings config)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;

                watcher.Created -= OnFileCreated;
                watcher.Changed -= OnFileCreated;

                watcher.Dispose();
            }

            _watchers.Clear();

            foreach (var folder in config.WatchFolders)
            {
                if (!Directory.Exists(folder))
                {
                    Log.Warning("Directory does not exist, skipping: {Folder}", folder);
                    continue;
                }

                var watcher = new FileSystemWatcher(folder)
                {
                    Filter = StringConstants.JxrFilter,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileCreated;
                watcher.Changed += OnFileCreated;

                _watchers.Add(watcher);
                Log.Information("Watching folder: {Folder} and its subdirectories", folder);
            }
        }

        private void OnFileCreated(object sender, FileSystemEventArgs eventArgs)
        {
            if (eventArgs.Name != null && !eventArgs.Name.EndsWith(StringConstants.Jxr, StringComparison.OrdinalIgnoreCase))
                return;

            var currentOptions = _optionsMonitor.CurrentValue;

            string relativeDir = Path.GetDirectoryName(eventArgs.Name) ?? string.Empty;
            string outputFileName = Path.GetFileNameWithoutExtension(eventArgs.Name) + StringConstants.SdrJpg;

            string targetDirectory;

            if (string.IsNullOrWhiteSpace(currentOptions.OutputPath))
            {
                targetDirectory = Path.GetDirectoryName(eventArgs.FullPath)!;
            }
            else
            {
                targetDirectory = Path.Combine(currentOptions.OutputPath, relativeDir);
            }

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            string outputPath = Path.Combine(targetDirectory, outputFileName);

            if (File.Exists(outputPath))
                return;

            Task.Run(() => ProcessFileSafe(eventArgs.FullPath, outputPath, currentOptions));
        }

        private void ProcessFileSafe(string inputPath, string outputPath, Settings options)
        {
            int maxRetries = options.Retries > 0 ? options.Retries : 10;
            int delayMs = options.DelayMs;

            string inputFileName = Path.GetFileName(inputPath);
            string outputFileName = Path.GetFileName(outputPath);

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (options.EnableLogging)
                        Log.Information("Processing {File}...", inputFileName);

                    using (var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        HdrPipeline.Process(stream, outputPath, options.HdrFixSettings);
                    }

                    if (options.EnableLogging)
                        Log.Information("Successfully converted: {File}", outputFileName);

                    return;
                }
                catch (IOException)
                {
                    if (options.EnableLogging)
                        Log.Warning("File locked, retrying {Retry}/{Max}...", i + 1, maxRetries);

                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Fatal error processing file: {File}", inputFileName);
                    return;
                }
            }

            Log.Warning("Failed to access {File} after {Retries} retries.", inputFileName, maxRetries);
        }

        public override void Dispose()
        {
            GC.SuppressFinalize(this);

            _configChangeToken?.Dispose();

            foreach (var watcher in _watchers)
                watcher.Dispose();

            base.Dispose();
        }
    }
}