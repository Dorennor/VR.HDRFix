using System.Numerics;

using VR.HDRFix.Helpers;

namespace VR.HDRFix.Tests;

public class HdrMathTests
{
    [Theory]
    [InlineData(0.0f, 0.0f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(0.18f, 0.4614f)]       // 18% grey
    [InlineData(0.0031308f, 0.04045f)] // linear/gamma segment boundary
    public void LinearToSrgb_MatchesStandard(float linear, float expected)
    {
        Assert.Equal(expected, HdrMath.LinearToSrgb(linear), 3);
    }

    [Fact]
    public void LinearToSrgbByte_FullRangeAndRounding()
    {
        Assert.Equal(0, HdrMath.LinearToSrgbByte(0.0f));
        Assert.Equal(0, HdrMath.LinearToSrgbByte(-1.0f));
        Assert.Equal(0, HdrMath.LinearToSrgbByte(float.NaN));
        Assert.Equal(255, HdrMath.LinearToSrgbByte(1.0f));  // the old code produced 246 here
        Assert.Equal(255, HdrMath.LinearToSrgbByte(5.0f));
        Assert.Equal(118, HdrMath.LinearToSrgbByte(0.18f)); // round(0.4614 * 255) = 118
    }

    [Fact]
    public void LinearToSrgbByte_IsMonotonic()
    {
        int previous = 0;

        for (int i = 0; i <= 10000; i++)
        {
            int value = HdrMath.LinearToSrgbByte(i / 10000.0f);
            Assert.True(value >= previous);
            previous = value;
        }
    }

    [Theory]
    [InlineData(0.5f, 0.2f, 0.1f)]
    [InlineData(1.0f, 1.0f, 1.0f)]
    [InlineData(-0.1f, 0.8f, 0.3f)] // out of gamut (negative channel)
    [InlineData(4.0f, 2.0f, 0.5f)]  // HDR value
    public void Oklab_RoundTrips(float r, float g, float b)
    {
        var c = new Vector3(r, g, b);
        var back = HdrMath.OklabToLinear(HdrMath.LinearToOklab(c));

        Assert.Equal(c.X, back.X, 3);
        Assert.Equal(c.Y, back.Y, 3);
        Assert.Equal(c.Z, back.Z, 3);
    }

    [Fact]
    public void Oklab_WhiteHasLightnessOne()
    {
        var lab = HdrMath.LinearToOklab(Vector3.One);

        Assert.Equal(1.0f, lab.X, 3);
        Assert.Equal(0.0f, lab.Y, 3);
        Assert.Equal(0.0f, lab.Z, 3);
    }

    [Fact]
    public void Shoulder_IsIdentityBelowKnee_AndHitsOneAtPeak()
    {
        Assert.Equal(0.5f, HdrMath.Shoulder(0.5f, 0.7f, 4.0f));
        Assert.Equal(0.7f, HdrMath.Shoulder(0.7f, 0.7f, 4.0f));
        Assert.Equal(1.0f, HdrMath.Shoulder(4.0f, 0.7f, 4.0f), 4);
    }

    [Fact]
    public void Shoulder_IsMonotonicAndSmoothAtKnee()
    {
        const float knee = 0.7f;
        const float peak = 6.0f;
        float previous = 0.0f;

        for (float x = 0.0f; x <= peak; x += 0.001f)
        {
            float y = HdrMath.Shoulder(x, knee, peak);
            Assert.True(y >= previous - 1e-6f, $"not monotonic at {x}");
            previous = y;
        }

        // Slope just above the knee is ~1, so there is no visible kink.
        float slope = (HdrMath.Shoulder(knee + 0.001f, knee, peak) - knee) / 0.001f;
        Assert.InRange(slope, 0.99f, 1.001f);
    }

    [Fact]
    public void Shoulder_NoCompressionWhenPeakFitsInSdr()
    {
        Assert.Equal(0.9f, HdrMath.Shoulder(0.9f, 0.7f, 1.0f));
    }

    [Fact]
    public void GamutMap_LeavesInGamutColorsAlone()
    {
        var c = new Vector3(0.2f, 0.5f, 0.9f);
        Assert.Equal(c, HdrMath.GamutMap(c));
    }

    [Fact]
    public void GamutMap_KeepsLightnessAndHueOfOutOfGamutColors()
    {
        var c = new Vector3(-0.15f, 0.6f, 0.9f); // wide-gamut cyan, as found in Cyberpunk captures
        var mapped = HdrMath.GamutMap(c);

        Assert.True(HdrMath.IsInGamut(mapped));

        var labIn = HdrMath.LinearToOklab(c);
        var labOut = HdrMath.LinearToOklab(mapped);

        Assert.Equal(labIn.X, labOut.X, 2);
        Assert.Equal(MathF.Atan2(labIn.Z, labIn.Y), MathF.Atan2(labOut.Z, labOut.Y), 2);
    }

    [Fact]
    public void AdjustSaturation_KeepsGreysGrey()
    {
        var result = HdrMath.AdjustSaturation(new Vector3(0.4f), 1.5f);

        Assert.Equal(0.4f, result.X, 3);
        Assert.Equal(0.4f, result.Y, 3);
        Assert.Equal(0.4f, result.Z, 3);
    }
}