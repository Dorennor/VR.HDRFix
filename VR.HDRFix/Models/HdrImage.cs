namespace VR.HDRFix.Models;

/// <summary>Decoded HDR image: linear scRGB, 4 floats per pixel (R, G, B, A), row-major, 1.0 = 80 nits.</summary>
public sealed class HdrImage
{
    public HdrImage(int width, int height, float[] pixels)
    {
        if (pixels.Length < (long)width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public float[] Pixels { get; }
    public int PixelCount => Width * Height;
}