namespace VR.HDRFix.Configs
{
    public class Settings
    {
        public string[] WatchFolders { get; set; }
        public string OutputPath { get; set; }
        public int Retries { get; set; }
        public int DelayMs { get; set; }
        public bool EnableLogging { get; set; }
        public HdrFixSettings HdrFixSettings { get; set; }
    }
}