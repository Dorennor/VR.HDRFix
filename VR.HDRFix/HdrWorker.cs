using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Threading.Channels;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VR.HDRFix.Configs;
using VR.HDRFix.Helpers;

namespace VR.HDRFix;

/// <summary>
/// Watches folders for new HDR screenshots and converts them through a queue.
/// <list type="bullet">
/// <item>File events only enqueue work; a fixed number of consumers do the conversions.</item>
/// <item>Watchers are rebuilt on config change, watcher error (buffer overflow, folder deleted) or a missing folder.</item>
/// <item>Recent unconverted files are picked up on startup and after every rebuild, so missed events are recovered.</item>
/// </list>
/// </summary>
public sealed class HdrWorker : BackgroundService
{
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FolderCheckInterval = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<Settings> _options;
    private readonly ILogger<HdrWorker> _logger;
    private readonly Channel<WorkItem> _queue = Channel.CreateUnbounded<WorkItem>();
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Lock _watchersLock = new();

    private IDisposable? _configChangeToken;
    private int _rebuildRequested;

    public HdrWorker(IOptionsMonitor<Settings> options, ILogger<HdrWorker> logger)
    {
        _options = options;
        _logger = logger;
    }

    private readonly record struct WorkItem(string InputPath, string WatchRoot);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _options.CurrentValue;

        RebuildWatchers(settings);
        ScanRecentFiles(settings);

        // Config reloads often fire twice per save; the monitor loop collapses them into one rebuild.
        _configChangeToken = _options.OnChange(_ => RequestRebuild("configuration changed"));

        int consumers = Math.Clamp(settings.MaxConcurrentConversions, 1, 8);

        var tasks = Enumerable.Range(0, consumers)
            .Select(_ => ConsumeQueueAsync(stoppingToken))
            .Append(MonitorAsync(stoppingToken))
            .ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Service stop requested.");
        }
    }

    #region Watchers

    private void RequestRebuild(string reason)
    {
        if (Interlocked.Exchange(ref _rebuildRequested, 1) == 0)
            _logger.LogInformation("Re-initializing watchers: {Reason}.", reason);
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(MonitorInterval);
        var lastFolderCheck = Stopwatch.StartNew();

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                if (lastFolderCheck.Elapsed >= FolderCheckInterval)
                {
                    lastFolderCheck.Restart();

                    if (AnyWatchedFolderMissing())
                        RequestRebuild("a watched folder is missing");
                }

                if (Interlocked.Exchange(ref _rebuildRequested, 0) == 1)
                {
                    var settings = _options.CurrentValue;
                    RebuildWatchers(settings);
                    ScanRecentFiles(settings);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watcher maintenance failed; will retry.");
                Interlocked.Exchange(ref _rebuildRequested, 1);
            }
        }
    }

    private bool AnyWatchedFolderMissing()
    {
        lock (_watchersLock)
        {
            return _watchers.Any(w => !Directory.Exists(w.Path));
        }
    }

    private void RebuildWatchers(Settings settings)
    {
        lock (_watchersLock)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();

            var folders = GetWatchFolders(settings);

            if (folders.Count == 0)
            {
                _logger.LogWarning("No watch folders configured in appsettings.json.");
                return;
            }

            foreach (var folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder))
                    {
                        _logger.LogWarning("Watch folder does not exist, creating it: {Folder}", folder);
                        Directory.CreateDirectory(folder);
                    }

                    _watchers.Add(CreateWatcher(folder));
                    _logger.LogInformation("Watching: {Folder}", folder);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not watch folder {Folder}.", folder);
                }
            }
        }
    }

    private FileSystemWatcher CreateWatcher(string folder)
    {
        var watcher = new FileSystemWatcher(folder, StringConstants.JxrFilter)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024
        };

        watcher.Created += (_, e) => Enqueue(e.FullPath, folder);
        watcher.Changed += (_, e) => Enqueue(e.FullPath, folder);
        watcher.Renamed += (_, e) => Enqueue(e.FullPath, folder);
        watcher.Error += (_, e) =>
        {
            _logger.LogWarning(e.GetException(), "File watcher error on {Folder}.", folder);
            RequestRebuild("watcher error");
        };

        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static List<string> GetWatchFolders(Settings settings)
    {
        return (settings.WatchFolders ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(OutputPaths.NormalizeFolder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ScanRecentFiles(Settings settings)
    {
        if (settings.ScanMaxAgeHours <= 0)
            return;

        var since = DateTime.Now.AddHours(-settings.ScanMaxAgeHours);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        int found = 0;

        foreach (var folder in GetWatchFolders(settings))
        {
            if (!Directory.Exists(folder))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, StringConstants.JxrFilter, options))
                {
                    if (File.GetLastWriteTime(file) >= since && Enqueue(file, folder))
                        found++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scanning {Folder} failed.", folder);
            }
        }

        if (found > 0)
            _logger.LogInformation("Found {Count} unconverted screenshot(s) from the last {Hours} h.", found, settings.ScanMaxAgeHours);
    }

    #endregion Watchers

    #region Queue

    /// <returns>True when the file was queued.</returns>
    private bool Enqueue(string inputPath, string watchRoot)
    {
        if (!inputPath.EndsWith(StringConstants.Jxr, StringComparison.OrdinalIgnoreCase))
            return false;

        var settings = _options.CurrentValue;
        string outputPath = OutputPaths.GetOutputPath(inputPath, watchRoot, settings.OutputPath, settings.HdrFixSettings.OutputFormat);

        if (File.Exists(outputPath))
            return false;

        if (!_pending.TryAdd(inputPath, 0))
            return false;

        return _queue.Writer.TryWrite(new WorkItem(inputPath, watchRoot));
    }

    private async Task ConsumeQueueAsync(CancellationToken ct)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ConvertWithRetriesAsync(item, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error handling {File}.", item.InputPath);
            }
            finally
            {
                _pending.TryRemove(item.InputPath, out _);
            }

            if (_queue.Reader.Count == 0)
                ReleaseMemory();
        }
    }

    private async Task ConvertWithRetriesAsync(WorkItem item, CancellationToken ct)
    {
        var settings = _options.CurrentValue;
        int attempts = Math.Max(1, settings.Retries);
        int delayMs = Math.Max(100, settings.DelayMs);
        string fileName = Path.GetFileName(item.InputPath);

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            if (!File.Exists(item.InputPath))
                return;

            string outputPath = OutputPaths.GetOutputPath(item.InputPath, item.WatchRoot, settings.OutputPath, settings.HdrFixSettings.OutputFormat);

            if (File.Exists(outputPath))
                return;

            try
            {
                await WaitUntilStableAsync(item.InputPath, delayMs, ct);

                var stopwatch = Stopwatch.StartNew();
                var result = await Task.Run(
                    () => HdrPipeline.ConvertFile(item.InputPath, outputPath, settings.HdrFixSettings, settings.MaxThreadsPerConversion),
                    ct);

                _logger.LogInformation(
                    "Converted {File} ({Width}x{Height}, white {White:F0} nits, peak {Peak:F0} nits) in {Ms} ms -> {Output}",
                    fileName, result.Width, result.Height, result.WhitePointNits, result.PeakNits, stopwatch.ElapsedMilliseconds, outputPath);

                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                // Locked, still being written (truncated data fails to decode), etc. All worth a retry.
                _logger.LogWarning("Attempt {Attempt}/{Total} failed for {File}: {Error}", attempt, attempts, fileName, ex.Message);
                await Task.Delay(delayMs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Giving up on {File} after {Total} attempts.", fileName, attempts);
            }
        }
    }

    /// <summary>Waits until the file size stops changing (the capture tool has finished writing).</summary>
    private static async Task WaitUntilStableAsync(string path, int delayMs, CancellationToken ct)
    {
        long lastSize = -1;

        for (int i = 0; i < 20; i++)
        {
            long size = new FileInfo(path).Length;

            if (size > 0 && size == lastSize)
                return;

            lastSize = size;
            await Task.Delay(Math.Min(delayMs, 250), ct);
        }
    }

    private static void ReleaseMemory()
    {
        // Conversions allocate large buffers on the LOH; give the memory back while the service is idle.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
    }

    #endregion Queue

    public override void Dispose()
    {
        _configChangeToken?.Dispose();

        lock (_watchersLock)
        {
            foreach (var watcher in _watchers)
                watcher.Dispose();

            _watchers.Clear();
        }

        base.Dispose();
    }
}