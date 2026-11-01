using VR.HDRFix.Configs;
using VR.HDRFix.Enums;
using VR.HDRFix.Helpers;

namespace VR.HDRFix.Tests;

public class SettingsAndPathsTests
{
    [Fact]
    public void Sanitized_FixesDangerousValues()
    {
        var s = new HdrFixSettings
        {
            Saturation = float.NaN,
            ShoulderStart = 5,
            JpegQuality = 0,
            MinWhitePointNits = 500,
            MaxWhitePointNits = 100
        }.Sanitized();

        Assert.Equal(new HdrFixSettings().Saturation, s.Saturation);
        Assert.Equal(0.99f, s.ShoulderStart);
        Assert.Equal(1, s.JpegQuality);
        Assert.True(s.MaxWhitePointNits >= s.MinWhitePointNits);
    }

    [Theory]
    [InlineData(@"D:\Videos\NVIDIA\Game\shot.jxr", @"D:\Videos\NVIDIA", @"D:\Out", OutputFormat.Jpeg, @"D:\Out\Game\shot-sdr.jpg")]
    [InlineData(@"D:\Videos\NVIDIA\shot.jxr", @"D:\Videos\NVIDIA\", @"D:\Out\", OutputFormat.Png, @"D:\Out\shot-sdr.png")]
    [InlineData(@"D:\Videos\NVIDIA\Game\shot.jxr", @"D:\Videos\NVIDIA", "", OutputFormat.Jpeg, @"D:\Videos\NVIDIA\Game\shot-sdr.jpg")]
    [InlineData(@"E:\Other\shot.jxr", @"D:\Videos\NVIDIA", @"D:\Out", OutputFormat.Jpeg, @"D:\Out\shot-sdr.jpg")]
    public void GetOutputPath_MirrorsFolderStructure(string input, string root, string output, OutputFormat format, string expected)
    {
        Assert.Equal(expected, OutputPaths.GetOutputPath(input, root, output, format));
    }

    [Theory]
    [InlineData(@"D:\Videos\NVIDIA\", @"D:\Videos\NVIDIA")]
    [InlineData(@"D:\", @"D:\")]
    public void NormalizeFolder_HandlesTrailingSeparatorAndDriveRoot(string input, string expected)
    {
        Assert.Equal(expected, OutputPaths.NormalizeFolder(input));
    }
}