using System.Numerics;
using System.Runtime.CompilerServices;

using VR.HDRFix.Configs;
using VR.HDRFix.Models;

namespace VR.HDRFix.Helpers;

/// <summary>
/// Per-image HDR → SDR mapping:
/// <list type="number">
/// <item>Exposure: scale so that the image's white reference becomes SDR white (1.0).</item>
/// <item>Saturation: optional Oklab chroma boost.</item>
/// <item>Highlights: hue-preserving shoulder on the max RGB channel, peak → exactly 1.0;
/// strongly compressed highlights blend toward white (like light sources on a real display).</item>
/// <item>Gamut: wide-gamut / overflowing colors are pulled into sRGB by reducing chroma, not by clipping.</item>
/// </list>
/// Midtones below the shoulder are left linear, so colors and contrast stay as the game rendered them.
/// </summary>
public sealed class ToneMapper
{
    private readonly float _scale;
    private readonly float _knee;
    private readonly float _peak;
    private readonly float _saturation;
    private readonly float _contrast;
    private readonly float _highlightDesaturation;

    private ToneMapper(float whitePointNits, float peakNits, HdrFixSettings settings)
    {
        WhitePointNits = whitePointNits;
        PeakNits = peakNits;

        float exposure = MathF.Pow(2.0f, settings.Exposure);

        // Input is scRGB (1.0 = 80 nits); output is relative to SDR white.
        _scale = HdrMath.ScRgbWhiteNits / whitePointNits * exposure;
        // The peak goes through the same contrast curve as the pixels, so it still lands exactly on 1.0.
        float peak = peakNits / whitePointNits * exposure;
        peak = HdrMath.MidGrey * MathF.Pow(peak / HdrMath.MidGrey, settings.Contrast);
        _peak = MathF.Max(peak, 1.0f);
        _knee = settings.ShoulderStart;
        _saturation = settings.Saturation;
        _contrast = settings.Contrast;
        _highlightDesaturation = settings.HighlightDesaturation;
    }

    /// <summary>HDR brightness (nits) mapped to SDR white for this image.</summary>
    public float WhitePointNits { get; }

    /// <summary>Detected highlight peak (nits) mapped exactly to the top of the SDR range.</summary>
    public float PeakNits { get; }

    /// <param name="settings">Must already be sanitized.</param>
    public static ToneMapper Create(HdrImage image, HdrFixSettings settings)
    {
        var stats = ImageStats.Analyze(image, settings.AutoWhitePercentile, settings.HighlightPercentile);
        return Create(stats, settings);
    }

    /// <param name="settings">Must already be sanitized.</param>
    public static ToneMapper Create(ImageStats stats, HdrFixSettings settings)
    {
        float white;

        if (settings.WhitePointNits > 0.0f)
        {
            white = settings.WhitePointNits;
        }
        else
        {
            white = stats.WhiteReferenceNits / MathF.Pow(2.0f, settings.AutoExposureBias);

            // Bright scenes (sunlit sand, snow, sky): don't let the average land too close to white,
            // otherwise most of the image ends up in the shoulder and looks bleached.
            if (settings.MaxSceneKey > 0.0f && stats.AverageNits > 0.0f)
                white = MathF.Max(white, stats.AverageNits / settings.MaxSceneKey);

            white = Math.Clamp(white, settings.MinWhitePointNits, settings.MaxWhitePointNits);
        }

        return new ToneMapper(white, MathF.Max(stats.HighlightPeakNits, white), settings);
    }

    /// <summary>Maps one linear scRGB pixel to linear SDR [0, 1] (sRGB primaries, not yet gamma-encoded).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 Map(Vector3 scRgb)
    {
        if (!float.IsFinite(scRgb.X) || !float.IsFinite(scRgb.Y) || !float.IsFinite(scRgb.Z))
            return Vector3.Zero;

        var c = HdrMath.ApplyContrast(scRgb * _scale, _contrast);
        c = HdrMath.AdjustSaturation(c, _saturation);
        float max = HdrMath.MaxComponent(c);

        if (max > _knee)
        {
            float mapped = HdrMath.Shoulder(max, _knee, _peak);
            c *= mapped / max;

            // Path to white: the more a highlight was compressed, the more it fades toward neutral.
            float compression = max - mapped;
            float blend = 1.0f - 1.0f / (_highlightDesaturation * compression + 1.0f);
            c = Vector3.Lerp(c, new Vector3(mapped), blend);
        }

        return HdrMath.GamutMap(c);
    }
}