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
        private readonly List<FileSystemWatcher> _rootFoldersWatchers = [];
        private readonly List<FileSystemWatcher> _watchers = [];
        private readonly ConcurrentDictionary<string, byte> _processingFiles = new();
        private readonly Lock _watchersLock = new();
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
                lock (_watchersLock)
                {
                    SetupWatchers(_optionsMonitor.CurrentValue);
                }

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
            ClearRootWatchers();
            ClearInputWatchers();

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
                    Directory.CreateDirectory(folder);
                }

                var rootWatcher = InitializeRootWatcher(folder);
                var watcher = InitializeInputWatcher(folder);

                _rootFoldersWatchers.Add(rootWatcher);
                _watchers.Add(watcher);

                Log.Information("Successfully attached watcher to: {Folder}", folder);
            }
        }

        private FileSystemWatcher InitializeRootWatcher(string folder)
        {
            string parentDir = Path.GetDirectoryName(folder);
            string folderName = Path.GetFileName(folder);

            var rootWatcher = new FileSystemWatcher(parentDir, folderName)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            rootWatcher.Deleted += OnFolderDeletion;

            return rootWatcher;
        }

        private FileSystemWatcher InitializeInputWatcher(string folder)
        {
            var watcher = new FileSystemWatcher(folder)
            {
                Filter = StringConstants.JxrFilter,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += OnFileEvent;
            watcher.Changed += OnFileEvent;

            return watcher;
        }

        private void ClearRootWatchers()
        {
            foreach (var rootWatcher in _rootFoldersWatchers)
            {
                rootWatcher.EnableRaisingEvents = false;
                rootWatcher.Deleted -= OnFolderDeletion;
                rootWatcher.Dispose();
            }

            _rootFoldersWatchers.Clear();
        }

        private void ClearInputWatchers()
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;

                watcher.Created -= OnFileEvent;
                watcher.Changed -= OnFileEvent;

                watcher.Dispose();
            }

            _watchers.Clear();
        }

        private void OnFileEvent(object sender, FileSystemEventArgs eventArgs)
        {
            Task.Run(() => HandleNewFileAsync(eventArgs.FullPath, eventArgs.Name ?? string.Empty));
        }

        private void OnFolderDeletion(object sender, FileSystemEventArgs eventArgs)
        {
            if (!Directory.Exists(eventArgs.FullPath))
                Directory.CreateDirectory(eventArgs.FullPath);

            lock (_watchersLock)
            {
                SetupWatchers(_optionsMonitor.CurrentValue);
            }
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

                string targetDir;

                if (string.IsNullOrWhiteSpace(settings.OutputPath))
                    targetDir = Path.GetDirectoryName(fullPath);
                else
                    targetDir = Path.Combine(settings.OutputPath, relativeDir);

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

                    Log.Information("Converting: {FileName}", Path.GetFileName(inputPath));

                    await using (var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        HdrPipeline.Process(stream, outputPath, options.HdrFixSettings);
                    }

                    Log.Information("Conversion successful: {FileName}", Path.GetFileName(outputPath));

                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
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

            foreach (var rootWatcher in _rootFoldersWatchers)
                rootWatcher.Dispose();

            foreach (var watcher in _watchers)
                watcher.Dispose();

            _concurrencySemaphore.Dispose();

            base.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}