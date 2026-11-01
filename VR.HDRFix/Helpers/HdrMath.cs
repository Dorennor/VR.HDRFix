using System.Numerics;
using System.Runtime.CompilerServices;

namespace VR.HDRFix.Helpers;

/// <summary>
/// Color math used by the HDR → SDR pipeline.
/// All RGB values are linear, with Rec.709 / sRGB primaries (same primaries as scRGB).
/// Oklab vectors are stored as (L, a, b) in X, Y, Z.
/// </summary>
public static class HdrMath
{
    /// <summary>scRGB 1.0 equals 80 nits by definition.</summary>
    public const float ScRgbWhiteNits = 80.0f;

    private const int SrgbLutSize = 16384;
    private static readonly byte[] SrgbLut = BuildSrgbLut();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Luma(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float MaxComponent(Vector3 c) => MathF.Max(c.X, MathF.Max(c.Y, c.Z));

    #region Oklab

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 LinearToOklab(Vector3 c)
    {
        // Signed cube root keeps out-of-gamut (negative) colors meaningful instead of clamping them.
        float l = MathF.Cbrt(0.4122214708f * c.X + 0.5363325363f * c.Y + 0.0514459929f * c.Z);
        float m = MathF.Cbrt(0.2119034982f * c.X + 0.6806995451f * c.Y + 0.1073969566f * c.Z);
        float s = MathF.Cbrt(0.0883024619f * c.X + 0.2817188376f * c.Y + 0.6299787005f * c.Z);

        return new Vector3(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 OklabToLinear(Vector3 lab)
    {
        float l = lab.X + 0.3963377774f * lab.Y + 0.2158037573f * lab.Z;
        float m = lab.X - 0.1055613458f * lab.Y - 0.0638541728f * lab.Z;
        float s = lab.X - 0.0894841775f * lab.Y - 1.2914855480f * lab.Z;

        l = l * l * l;
        m = m * m * m;
        s = s * s * s;

        return new Vector3(
             4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
            -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
            -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
    }

    /// <summary>Scales chroma in Oklab, keeping lightness and hue.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 AdjustSaturation(Vector3 c, float saturation)
    {
        if (saturation == 1.0f)
            return c;

        var lab = LinearToOklab(c);
        return OklabToLinear(new Vector3(lab.X, lab.Y * saturation, lab.Z * saturation));
    }

    #endregion Oklab

    #region Tone curve & gamut

    /// <summary>Scene mid-grey relative to SDR white; the pivot that <see cref="ApplyContrast"/> keeps fixed.</summary>
    public const float MidGrey = 0.18f;

    /// <summary>
    /// Filmic contrast: a power curve in log space around <see cref="MidGrey"/>, applied to luminance
    /// and transferred to RGB as a common factor (hue and RGB ratios are kept).
    /// Above 1 it deepens shadows and lifts highlights (which the shoulder then rolls off).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ApplyContrast(Vector3 c, float contrast)
    {
        if (contrast == 1.0f)
            return c;

        float y = Luma(c);

        if (!(y > 0.0f))
            return c;

        float mapped = MidGrey * MathF.Pow(y / MidGrey, contrast);
        return c * (mapped / y);
    }

    /// <summary>
    /// Highlight shoulder: identity below <paramref name="knee"/>, then a smooth (C1-continuous)
    /// extended-Reinhard roll-off that reaches exactly 1.0 at <paramref name="peak"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Shoulder(float x, float knee, float peak)
    {
        if (x <= knee || peak <= 1.0f)
            return x;

        float range = 1.0f - knee;
        float u = (x - knee) / range;
        float w = (peak - knee) / range;
        float g = u * (1.0f + u / (w * w)) / (1.0f + u);

        return knee + range * g;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInGamut(Vector3 c, float epsilon = 1e-4f)
    {
        return c.X >= -epsilon && c.Y >= -epsilon && c.Z >= -epsilon
            && c.X <= 1.0f + epsilon && c.Y <= 1.0f + epsilon && c.Z <= 1.0f + epsilon;
    }

    /// <summary>
    /// Brings a color into the [0, 1] sRGB cube by reducing Oklab chroma at constant lightness and hue,
    /// instead of clipping channels (which shifts hue and flattens saturated colors).
    /// </summary>
    public static Vector3 GamutMap(Vector3 c)
    {
        if (IsInGamut(c))
            return Vector3.Clamp(c, Vector3.Zero, Vector3.One);

        var lab = LinearToOklab(c);

        if (lab.X >= 1.0f)
            return Vector3.One;

        if (lab.X <= 0.0f)
            return Vector3.Zero;

        float lo = 0.0f;
        float hi = 1.0f;

        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) * 0.5f;
            var candidate = OklabToLinear(new Vector3(lab.X, lab.Y * mid, lab.Z * mid));

            if (IsInGamut(candidate))
                lo = mid;
            else
                hi = mid;
        }

        var result = OklabToLinear(new Vector3(lab.X, lab.Y * lo, lab.Z * lo));
        return Vector3.Clamp(result, Vector3.Zero, Vector3.One);
    }

    #endregion Tone curve & gamut

    #region sRGB encoding

    /// <summary>Exact sRGB transfer function (IEC 61966-2-1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LinearToSrgb(float v)
    {
        if (v <= 0.0031308f)
            return v * 12.92f;

        return 1.055f * MathF.Pow(v, 1.0f / 2.4f) - 0.055f;
    }

    /// <summary>Linear [0, 1] → 8-bit sRGB, rounded, via lookup table.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte LinearToSrgbByte(float v)
    {
        if (!(v > 0.0f))
            return 0;

        if (v >= 1.0f)
            return 255;

        return SrgbLut[(int)(v * (SrgbLutSize - 1) + 0.5f)];
    }

    private static byte[] BuildSrgbLut()
    {
        var lut = new byte[SrgbLutSize];

        for (int i = 0; i < SrgbLutSize; i++)
        {
            float encoded = LinearToSrgb(i / (float)(SrgbLutSize - 1));
            lut[i] = (byte)Math.Clamp((int)MathF.Round(encoded * 255.0f), 0, 255);
        }

        return lut;
    }

    #endregion sRGB encoding
}