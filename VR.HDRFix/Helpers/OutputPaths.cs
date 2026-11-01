using System.IO;

using VR.HDRFix.Enums;

namespace VR.HDRFix.Helpers;

public static class OutputPaths
{
    public static string Extension(OutputFormat format) => format == OutputFormat.Png ? ".png" : ".jpg";

    /// <summary>
    /// Builds the output path for <paramref name="inputPath"/>.
    /// With an <paramref name="outputRoot"/>, the folder structure below <paramref name="watchRoot"/> is mirrored there;
    /// otherwise the output goes next to the input.
    /// </summary>
    public static string GetOutputPath(string inputPath, string watchRoot, string? outputRoot, OutputFormat format)
    {
        string fileName = Path.GetFileNameWithoutExtension(inputPath) + StringConstants.SdrSuffix + Extension(format);
        string inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;

        if (string.IsNullOrWhiteSpace(outputRoot))
            return Path.Combine(inputDir, fileName);

        string relativeDir = Path.GetRelativePath(NormalizeFolder(watchRoot), inputDir);

        // Input outside the watch root: don't recreate an odd "..\.." structure, use the output root directly.
        if (relativeDir == "." || relativeDir.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeDir))
            relativeDir = string.Empty;

        return Path.Combine(NormalizeFolder(outputRoot), relativeDir, fileName);
    }

    /// <summary>Full path without a trailing separator (but keeps "D:\" for drive roots).</summary>
    public static string NormalizeFolder(string folder)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
    }
}