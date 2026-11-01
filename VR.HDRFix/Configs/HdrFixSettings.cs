using VR.HDRFix.Enums;

namespace VR.HDRFix.Configs;

/// <summary>
/// Conversion settings. Every property has a sensible default, so a missing key in
/// appsettings.json never produces a broken image.
/// </summary>
public class HdrFixSettings
{
    /// <summary>
    /// HDR brightness (nits) that becomes SDR white. 0 = detect automatically for every image (recommended).
    /// Typical game "paper white" values are 200–400 nits.
    /// </summary>
    public float WhitePointNits { get; set; } = 0.0f;

    /// <summary>Auto mode: luminance percentile of the image used as the white reference.</summary>
    public float AutoWhitePercentile { get; set; } = 99.0f;

    /// <summary>Auto mode: extra brightening in stops applied on top of the detected white (0.25 = slightly brighter midtones).</summary>
    public float AutoExposureBias { get; set; } = 0.25f;

    /// <summary>
    /// Auto mode: highest allowed scene key (log-average luminance relative to SDR white). Bright scenes
    /// above it get a higher white point so they don't look bleached. 0 = off.
    /// </summary>
    public float MaxSceneKey { get; set; } = 0.1f;

    /// <summary>Auto mode: lower limit for the detected white point, keeps dark scenes from being over-brightened.</summary>
    public float MinWhitePointNits { get; set; } = 80.0f;

    /// <summary>Auto mode: upper limit for the detected white point, keeps bright scenes from being too dark.</summary>
    public float MaxWhitePointNits { get; set; } = 400.0f;

    /// <summary>Manual exposure adjustment in stops, applied in both auto and fixed mode. +1 = twice as bright.</summary>
    public float Exposure { get; set; } = 0.0f;

    /// <summary>Oklab chroma multiplier. 1.0 = unchanged; 1.25 compensates for SDR looking less vivid than HDR (colors look stronger at HDR brightness).</summary>
    public float Saturation { get; set; } = 1.25f;

    /// <summary>
    /// Filmic contrast around mid-grey (1.0 = linear, as the game rendered it in HDR). Values above 1 deepen
    /// shadows and give back the punch that HDR gets from its extra brightness range.
    /// </summary>
    public float Contrast { get; set; } = 1.2f;

    /// <summary>Fraction of SDR white where highlight compression starts (0.5–0.95). Below it, tones are kept linear.</summary>
    public float ShoulderStart { get; set; } = 0.7f;

    /// <summary>Brightness percentile (of max RGB channel) treated as the image peak, mapped exactly to SDR white.</summary>
    public float HighlightPercentile { get; set; } = 99.9f;

    /// <summary>How strongly very bright highlights fade toward white (0 = keep full color, 0.05 = only the brightest light sources).</summary>
    public float HighlightDesaturation { get; set; } = 0.05f;

    public OutputFormat OutputFormat { get; set; } = OutputFormat.Jpeg;

    /// <summary>JPEG quality 1–100.</summary>
    public int JpegQuality { get; set; } = 95;

    private static readonly HdrFixSettings Defaults = new();

    /// <summary>Returns a copy with all values clamped to safe ranges.</summary>
    public HdrFixSettings Sanitized()
    {
        float minWhite = Math.Clamp(Finite(MinWhitePointNits, Defaults.MinWhitePointNits), 1.0f, 10000.0f);
        float maxWhite = Math.Clamp(Finite(MaxWhitePointNits, Defaults.MaxWhitePointNits), minWhite, 10000.0f);

        return new HdrFixSettings
        {
            WhitePointNits = Math.Clamp(Finite(WhitePointNits, Defaults.WhitePointNits), 0.0f, 10000.0f),
            AutoWhitePercentile = Math.Clamp(Finite(AutoWhitePercentile, Defaults.AutoWhitePercentile), 50.0f, 100.0f),
            AutoExposureBias = Math.Clamp(Finite(AutoExposureBias, Defaults.AutoExposureBias), -4.0f, 4.0f),
            MaxSceneKey = Math.Clamp(Finite(MaxSceneKey, Defaults.MaxSceneKey), 0.0f, 1.0f),
            MinWhitePointNits = minWhite,
            MaxWhitePointNits = maxWhite,
            Exposure = Math.Clamp(Finite(Exposure, Defaults.Exposure), -8.0f, 8.0f),
            Saturation = Math.Clamp(Finite(Saturation, Defaults.Saturation), 0.0f, 3.0f),
            Contrast = Math.Clamp(Finite(Contrast, Defaults.Contrast), 0.5f, 2.0f),
            ShoulderStart = Math.Clamp(Finite(ShoulderStart, Defaults.ShoulderStart), 0.3f, 0.99f),
            HighlightPercentile = Math.Clamp(Finite(HighlightPercentile, Defaults.HighlightPercentile), 90.0f, 100.0f),
            HighlightDesaturation = Math.Clamp(Finite(HighlightDesaturation, Defaults.HighlightDesaturation), 0.0f, 5.0f),
            OutputFormat = Enum.IsDefined(OutputFormat) ? OutputFormat : OutputFormat.Jpeg,
            JpegQuality = Math.Clamp(JpegQuality, 1, 100)
        };
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
}