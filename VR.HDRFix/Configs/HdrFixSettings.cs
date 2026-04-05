using VR.HDRFix.Enums;

namespace VR.HDRFix.Configs
{
    public class HdrFixSettings
    {
        public float PreGamma { get; set; }
        public float Exposure { get; set; }
        public float Saturation { get; set; }
        public float PostGamma { get; set; }
        public ToneMap ToneMap { get; set; }
        public ColorMap ColorMap { get; set; }
    }
}