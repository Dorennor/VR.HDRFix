using System.Numerics;

using VR.HDRFix.Configs;
using VR.HDRFix.Helpers;
using VR.HDRFix.Models;

namespace VR.HDRFix.Tests;

public class ToneMapperTests
{
    private static readonly HdrFixSettings Defaults = new HdrFixSettings().Sanitized();

    private static Vector3 Nits(float r, float g, float b) => new Vector3(r, g, b) / HdrMath.ScRgbWhiteNits;

    private static float Hue(Vector3 c)
    {
        var lab = HdrMath.LinearToOklab(c);
        return MathF.Atan2(lab.Z, lab.Y);
    }

    [Fact]
    public void AutoWhitePoint_UsesPercentilePlusBias()
    {
        var mapper = ToneMapper.Create(new ImageStats(WhiteReferenceNits: 400, HighlightPeakNits: 1000), Defaults);

        Assert.Equal(400.0f / MathF.Pow(2.0f, Defaults.AutoExposureBias), mapper.WhitePointNits, 1);
    }

    [Theory]
    [InlineData(20.0f, 80.0f)]    // dark scene: limited by MinWhitePointNits
    [InlineData(5000.0f, 400.0f)] // very bright scene: limited by MaxWhitePointNits
    public void AutoWhitePoint_IsClamped(float reference, float expected)
    {
        var mapper = ToneMapper.Create(new ImageStats(reference, reference), Defaults);
        Assert.Equal(expected, mapper.WhitePointNits, 1);
    }

    [Fact]
    public void FixedWhitePoint_OverridesAuto()
    {
        var settings = new HdrFixSettings { WhitePointNits = 250 }.Sanitized();
        var mapper = ToneMapper.Create(new ImageStats(1000, 2000), settings);

        Assert.Equal(250.0f, mapper.WhitePointNits);
    }

    [Fact]
    public void Midtones_AreLinear_GreyStaysGrey()
    {
        var settings = new HdrFixSettings { WhitePointNits = 200, Contrast = 1 }.Sanitized();
        var mapper = ToneMapper.Create(new ImageStats(200, 1000), settings);

        // 50 nits with a 200-nit white = 0.25 linear, untouched by the shoulder.
        var result = mapper.Map(Nits(50, 50, 50));

        Assert.Equal(0.25f, result.X, 3);
        Assert.Equal(0.25f, result.Y, 3);
        Assert.Equal(0.25f, result.Z, 3);
    }

    [Fact]
    public void PeakHighlight_MapsToWhite()
    {
        var settings = new HdrFixSettings { WhitePointNits = 200 }.Sanitized();
        var mapper = ToneMapper.Create(new ImageStats(200, 1000), settings);

        var result = mapper.Map(Nits(1000, 1000, 1000));

        Assert.Equal(1.0f, result.X, 3);
        Assert.Equal(1.0f, result.Y, 3);
        Assert.Equal(1.0f, result.Z, 3);
    }

    [Fact]
    public void Output_IsAlwaysInGamut()
    {
        var mapper = ToneMapper.Create(new ImageStats(300, 1000), Defaults);
        var random = new Random(42);

        for (int i = 0; i < 20000; i++)
        {
            // scRGB including negative (wide-gamut) and very bright values
            var c = new Vector3(
                (float)(random.NextDouble() * 20 - 1),
                (float)(random.NextDouble() * 20 - 1),
                (float)(random.NextDouble() * 20 - 1));

            var result = mapper.Map(c);
            Assert.True(HdrMath.IsInGamut(result, 0.0f), $"{c} -> {result}");
        }
    }

    [Fact]
    public void InvalidPixels_BecomeBlack()
    {
        var mapper = ToneMapper.Create(new ImageStats(300, 1000), Defaults);

        Assert.Equal(Vector3.Zero, mapper.Map(new Vector3(float.NaN, 1, 1)));
        Assert.Equal(Vector3.Zero, mapper.Map(new Vector3(float.PositiveInfinity, 1, 1)));
    }

    [Fact]
    public void SaturatedHighlight_KeepsHue()
    {
        var settings = new HdrFixSettings { WhitePointNits = 200, HighlightDesaturation = 0, Saturation = 1 }.Sanitized();
        var mapper = ToneMapper.Create(new ImageStats(200, 1000), settings);

        // Bright orange (fire / neon). Per-channel tone mapping would shift this toward yellow.
        var input = Nits(800, 300, 40);
        var output = mapper.Map(input);

        Assert.Equal(Hue(input), Hue(output), 1);
    }

    [Fact]
    public void BrightScene_WhitePointRaisedBySceneKey()
    {
        // Average 30 nits with P99 at 200 nits: without the cap white would be ~168 nits (key 0.18).
        var mapper = ToneMapper.Create(new ImageStats(200, 400, AverageNits: 30), Defaults);

        Assert.Equal(30.0f / Defaults.MaxSceneKey, mapper.WhitePointNits, 1);
    }

    [Fact]
    public void NormalScene_SceneKeyHasNoEffect()
    {
        var withAverage = ToneMapper.Create(new ImageStats(300, 1000, AverageNits: 10), Defaults);
        var without = ToneMapper.Create(new ImageStats(300, 1000), Defaults);

        Assert.Equal(without.WhitePointNits, withAverage.WhitePointNits);
    }

    [Fact]
    public void Contrast_KeepsMidGrey_DeepensShadows_KeepsGreysNeutral()
    {
        var settings = new HdrFixSettings { WhitePointNits = 200, Contrast = 1.2f, Saturation = 1 }.Sanitized();
        var mapper = ToneMapper.Create(new ImageStats(200, 1000), settings);

        var mid = mapper.Map(Nits(36, 36, 36));      // 0.18 of white
        var shadow = mapper.Map(Nits(4, 4, 4));      // 0.02 of white

        Assert.Equal(HdrMath.MidGrey, mid.Y, 3);
        Assert.True(shadow.Y < 0.02f);
        Assert.Equal(shadow.X, shadow.Z, 5);
    }
}