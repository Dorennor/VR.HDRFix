namespace VR.HDRFix
{
    public class HdrFixOptions
    {
        public string WatchFolder { get; set; } = string.Empty;
        public float PreGamma { get; set; } = 1.0f;
        public float Exposure { get; set; } = 0.0f;
        public float Saturation { get; set; } = 1.0f;
        public float PostGamma { get; set; } = 1.0f;
        public string ToneMap { get; set; } = "hable";
        public string ColorMap { get; set; } = "clip";
    }
}