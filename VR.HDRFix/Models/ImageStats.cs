using System.Numerics;

using VR.HDRFix.Helpers;

namespace VR.HDRFix.Models;

/// <summary>Brightness statistics of an HDR image, in nits.</summary>
/// <param name="WhiteReferenceNits">Luminance percentile used as the auto white reference.</param>
/// <param name="HighlightPeakNits">Max-RGB percentile treated as the image peak.</param>
/// <param name="AverageNits">Log-average (geometric mean) luminance, the scene "key". 0 = unknown.</param>
public readonly record struct ImageStats(float WhiteReferenceNits, float HighlightPeakNits, float AverageNits = 0.0f)
{
    /// <summary>Upper bound on sampled pixels; percentiles are stable well below this.</summary>
    private const int MaxSamples = 500_000;

    public static ImageStats Analyze(HdrImage image, float whitePercentile, float highlightPercentile)
    {
        int count = image.PixelCount;

        if (count == 0)
            return new ImageStats(HdrMath.ScRgbWhiteNits, HdrMath.ScRgbWhiteNits);

        int step = Math.Max(1, count / MaxSamples);
        int samples = (count + step - 1) / step;

        var luma = new float[samples];
        var peak = new float[samples];
        var px = image.Pixels;
        double logSum = 0.0;

        for (int s = 0, i = 0; s < samples; s++, i += step)
        {
            int o = i * 4;
            var c = new Vector3(px[o], px[o + 1], px[o + 2]) * HdrMath.ScRgbWhiteNits;

            luma[s] = HdrMath.Luma(c);
            peak[s] = HdrMath.MaxComponent(c);

            // Floor at 0.01 nits so black pixels (HUD, letterbox) don't drag the average toward zero.
            float y = luma[s];
            logSum += Math.Log(float.IsFinite(y) ? Math.Max(y, 0.01f) : 0.01f);
        }

        Array.Sort(luma);
        Array.Sort(peak);

        float average = (float)Math.Exp(logSum / samples);

        return new ImageStats(Percentile(luma, whitePercentile), Percentile(peak, highlightPercentile), average);
    }

    private static float Percentile(float[] sorted, float percent)
    {
        int index = (int)Math.Round((sorted.Length - 1) * Math.Clamp(percent, 0.0f, 100.0f) / 100.0);
        float value = sorted[index];

        return float.IsFinite(value) ? value : 0.0f;
    }
}