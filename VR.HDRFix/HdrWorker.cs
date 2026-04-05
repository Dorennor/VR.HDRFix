using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<string, byte> _processingFiles = new();
        private readonly SemaphoreSlim _concurrencySemaphore = new(2);
        private IDisposable _configChangeToken;

        public HdrWorker(IOptionsMonitor<Settings> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Log.Information("HDR Fix Service is starting up...");

            try
            {
                SetupWatchers(_optionsMonitor.CurrentValue);

                _configChangeToken = _optionsMonitor.OnChange(newConfig =>
                {
                    Log.Information("Configuration changed. Re-initializing watchers...");
                    SetupWatchers(newConfig);
                });

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                Log.Information("Service stop requested.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Service encountered an unrecoverable error in the main loop.");
            }
        }

        private void SetupWatchers(Settings config)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= OnFileEvent;
                watcher.Changed -= OnFileEvent;
                watcher.Dispose();
            }
            _watchers.Clear();

            if (config.WatchFolders == null || config.WatchFolders.Length == 0)
            {
                Log.Warning("No watch folders configured in appsettings.json.");
                return;
            }

            foreach (var folder in config.WatchFolders)
            {
                if (!Directory.Exists(folder))
                {
                    Log.Warning("Target folder does not exist, skipping: {Folder}", folder);
                    continue;
                }

                var watcher = new FileSystemWatcher(folder)
                {
                    Filter = StringConstants.JxrFilter,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;

                _watchers.Add(watcher);
                Log.Information("Successfully attached watcher to: {Folder}", folder);
            }
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            Task.Run(() => HandleNewFileAsync(e.FullPath, e.Name ?? string.Empty));
        }

        private async Task HandleNewFileAsync(string fullPath, string relativeName)
        {
            if (!_processingFiles.TryAdd(fullPath, 0))
                return;

            try
            {
                var settings = _optionsMonitor.CurrentValue;

                string relativeDir = Path.GetDirectoryName(relativeName) ?? string.Empty;
                string outputFileName = Path.GetFileNameWithoutExtension(relativeName) + StringConstants.SdrJpg;

                string targetDir = string.IsNullOrWhiteSpace(settings.OutputPath)
                    ? Path.GetDirectoryName(fullPath)!
                    : Path.Combine(settings.OutputPath, relativeDir);

                string outputPath = Path.Combine(targetDir, outputFileName);

                if (File.Exists(outputPath))
                    return;

                await _concurrencySemaphore.WaitAsync();

                try
                {
                    await ProcessFileWithRetriesAsync(fullPath, outputPath, settings);
                }
                finally
                {
                    _concurrencySemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error handling file: {File}", fullPath);
            }
            finally
            {
                _processingFiles.TryRemove(fullPath, out _);
            }
        }

        private static async Task ProcessFileWithRetriesAsync(string inputPath, string outputPath, Settings options)
        {
            int maxRetries = Math.Max(1, options.Retries);
            int delayMs = Math.Max(100, options.DelayMs);

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (!File.Exists(inputPath))
                        return;

                    string dir = Path.GetDirectoryName(outputPath);

                    if (dir != null && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (options.EnableLogging)
                        Log.Information("Converting: {FileName}", Path.GetFileName(inputPath));

                    await using (var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        HdrPipeline.Process(stream, outputPath, options.HdrFixSettings);
                    }

                    if (options.EnableLogging)
                        Log.Information("Conversion successful: {FileName}", Path.GetFileName(outputPath));

                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    if (options.EnableLogging)
                        Log.Warning("File locked or being written. Retry {Current}/{Total} for {File}", i + 1, maxRetries, Path.GetFileName(inputPath));

                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Fatal error processing {File}", inputPath);
                    return;
                }
            }

            Log.Error("Failed to process {File} after {Count} attempts.", inputPath, maxRetries);
        }

        public override void Dispose()
        {
            _configChangeToken?.Dispose();

            foreach (var watcher in _watchers)
                watcher.Dispose();

            _concurrencySemaphore.Dispose();

            base.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}