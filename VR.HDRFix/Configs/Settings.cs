namespace VR.HDRFix.Configs;

public class Settings
{
    /// <summary>Folders watched (recursively) for new *.jxr screenshots.</summary>
    public string[] WatchFolders { get; set; } = [];

    /// <summary>Root folder for converted images (sub-folder structure is mirrored). Empty = next to the source file.</summary>
    public string? OutputPath { get; set; }

    /// <summary>How many times a file is retried when it can't be read or converted yet (e.g. still being written).</summary>
    public int Retries { get; set; } = 10;

    /// <summary>Delay between retries, in milliseconds.</summary>
    public int DelayMs { get; set; } = 500;

    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// On startup (and after a watcher failure), convert screenshots modified within this many hours that have no output yet.
    /// 0 = disabled.
    /// </summary>
    public int ScanMaxAgeHours { get; set; } = 24;

    /// <summary>How many files are converted at the same time.</summary>
    public int MaxConcurrentConversions { get; set; } = 1;

    /// <summary>CPU threads used per conversion. 0 = half of the logical processors (leaves room for the game).</summary>
    public int MaxThreadsPerConversion { get; set; } = 0;

    public HdrFixSettings HdrFixSettings { get; set; } = new();
}