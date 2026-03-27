using System.Numerics;

namespace VR.HDRFix
{
    public struct Oklab
    {
        public float L, A, B;

        public Oklab(float l, float a, float b)
        { L = l; A = a; B = b; }
    }

    public static class HdrMath
    {
        public const float SDR_WHITE = 200.0f;
        public const float REC2100_MAX = 10000.0f;

        public static Vector3 Pow(Vector3 v, float power)
        {
            return new Vector3(
                (float)Math.Pow(Math.Max(0, v.X), power),
                (float)Math.Pow(Math.Max(0, v.Y), power),
                (float)Math.Pow(Math.Max(0, v.Z), power)
            );
        }

        #region Basic Color & Brightness Math

        public static float LumaRgb(Vector3 val)
        {
            return val.X * 0.2126f + val.Y * 0.7152f + val.Z * 0.0722f;
        }

        public static Vector3 ScaleRgb(Vector3 val, float lumaOut)
        {
            float lumaIn = LumaRgb(val);
            if (lumaIn < 1e-6f) return val;
            float scale = lumaOut / lumaIn;
            return val * scale;
        }

        public static Vector3 Clip(Vector3 input)
        {
            return Vector3.Clamp(input, Vector3.Zero, Vector3.One);
        }

        public static Vector3 LinearToSrgb(Vector3 val)
        {
            var min = new Vector3(0.0031308f);
            var linear = val * 12.92f;
            var gamma = Pow(val * 1.055f, 1.0f / 2.4f) - new Vector3(0.055f);

            return new Vector3(
                val.X <= min.X ? linear.X : gamma.X,
                val.Y <= min.Y ? linear.Y : gamma.Y,
                val.Z <= min.Z ? linear.Z : gamma.Z
            );
        }

        #endregion Basic Color & Brightness Math

        #region Oklab Color Space Conversions

        public static Oklab LinearSrgbToOklab(Vector3 c)
        {
            float l = 0.4122214708f * c.X + 0.5363325363f * c.Y + 0.0514459929f * c.Z;
            float m = 0.2119034982f * c.X + 0.6806995451f * c.Y + 0.1073969566f * c.Z;
            float s = 0.0883024619f * c.X + 0.2817188376f * c.Y + 0.6299787005f * c.Z;

            float l_ = MathF.Cbrt(Math.Max(0, l));
            float m_ = MathF.Cbrt(Math.Max(0, m));
            float s_ = MathF.Cbrt(Math.Max(0, s));

            return new Oklab(
                0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
                1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
                0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_
            );
        }

        public static Vector3 OklabToLinearSrgb(Oklab c)
        {
            float l_ = c.L + 0.3963377774f * c.A + 0.2158037573f * c.B;
            float m_ = c.L - 0.1055613458f * c.A - 0.0638541728f * c.B;
            float s_ = c.L - 0.0894841775f * c.A - 1.2914855480f * c.B;

            float l = l_ * l_ * l_;
            float m = m_ * m_ * m_;
            float s = s_ * s_ * s_;

            return new Vector3(
                 4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s
            );
        }

        public static float LumaOklab(Oklab val)
        {
            var oklabGray = new Oklab(val.L, 0.0f, 0.0f);
            var rgbGray = OklabToLinearSrgb(oklabGray);
            return rgbGray.X;
        }

        public static float OklabLForLuma(float luma)
        {
            var grayRgb = new Vector3(luma, luma, luma);
            var grayOklab = LinearSrgbToOklab(grayRgb);
            return grayOklab.L;
        }

        public static Oklab ScaleOklab(Oklab oklabIn, float lumaOut)
        {
            if (oklabIn.L == 0.0f) return oklabIn;
            float grayL = OklabLForLuma(lumaOut);
            float ratio = grayL / oklabIn.L;
            return new Oklab(grayL, oklabIn.A * ratio, oklabIn.B * ratio);
        }

        public static Oklab ScaleOklabDesat(Oklab oklabIn, float lumaOut, float saturation)
        {
            float lIn = oklabIn.L;
            if (lIn == 0.0f) return oklabIn;

            float lOut = OklabLForLuma(lumaOut);
            float ratio = MathF.Pow(lOut / lIn, 3.0f / saturation);
            return new Oklab(lOut, oklabIn.A * ratio, oklabIn.B * ratio);
        }

        #endregion Oklab Color Space Conversions

        #region Color Mapping (Binary Search)

        private static Vector3 BinarySearchColorMap(Vector3 input, Func<Oklab, float, Vector3> modifier)
        {
            var oklabIn = LinearSrgbToOklab(input);
            float min = 0.0f;
            float max = 1.0f;
            Vector3 result = input;

            // Ітеративний бінарний пошук (аналог rust binary_search)
            for (int i = 0; i < 32; i++)
            {
                float mid = (min + max) / 2.0f;
                result = modifier(oklabIn, mid);

                float maxElement = Math.Max(Math.Max(result.X, result.Y), result.Z);
                float delta = min - max;

                if (Math.Abs(delta) < 0.001f || Math.Abs(maxElement - 1.0f) < 0.001f) break;

                if (maxElement < 1.0f) min = mid; // занадто темно, треба вище значення
                else max = mid;                   // все ще виходить за 1.0, треба нижче
            }

            return Clip(result);
        }

        public static Vector3 ColorDarkenOklab(Vector3 cIn)
        {
            float max = Math.Max(Math.Max(cIn.X, cIn.Y), cIn.Z);
            if (max <= 1.0f) return cIn;

            return BinarySearchColorMap(cIn, (oklab, brightness) =>
            {
                var cOut = new Oklab(oklab.L * brightness, oklab.A * brightness, oklab.B * brightness);
                return OklabToLinearSrgb(cOut);
            });
        }

        public static Vector3 ColorDesatOklab(Vector3 cIn)
        {
            float max = Math.Max(Math.Max(cIn.X, cIn.Y), cIn.Z);
            if (max <= 1.0f) return cIn;

            return BinarySearchColorMap(cIn, (oklab, saturation) =>
            {
                var cOut = new Oklab(oklab.L, oklab.A * saturation, oklab.B * saturation);
                return OklabToLinearSrgb(cOut);
            });
        }

        #endregion Color Mapping (Binary Search)

        #region Tone Mapping Algorithms

        // Uncharted 2 / Hable
        private static float Uncharted2TonemapPartial(float x)
        {
            const float A = 0.15f, B = 0.50f, C = 0.10f, D = 0.20f, E = 0.02f, F = 0.30f;
            return ((x * (A * x + (C * B)) + (D * E)) / (x * (A * x + B) + (D * F))) - (E / F);
        }

        public static Vector3 TonemapUncharted2(Vector3 val)
        {
            float exposureBias = 2.0f;
            float curr = Uncharted2TonemapPartial(LumaRgb(val) * exposureBias);
            float whiteScale = 1.0f / Uncharted2TonemapPartial(11.2f);
            return ScaleRgb(val, curr * whiteScale);
        }

        public static Vector3 TonemapHable(Vector3 val)
        {
            float luma = LumaRgb(val);
            float overbright = Math.Max(luma - 2.0f, 1e-6f) / Math.Max(luma, 1e-6f);
            var rgbOut = val * (1.0f - overbright) + new Vector3(luma) * overbright;

            float sigOrig = Math.Max(Math.Max(rgbOut.X, rgbOut.Y), Math.Max(rgbOut.Z, 1e-6f));
            float curr = Uncharted2TonemapPartial(sigOrig * 2.0f);
            float whiteScale = 1.0f / Uncharted2TonemapPartial(11.2f);

            return rgbOut * ((curr * whiteScale) / sigOrig);
        }

        // ACES
        private static Vector3 AcesMul(float[] m, Vector3 v)
        {
            return new Vector3(
                m[0] * v.X + m[1] * v.Y + m[2] * v.Z,
                m[3] * v.X + m[4] * v.Y + m[5] * v.Z,
                m[6] * v.X + m[7] * v.Y + m[8] * v.Z
            );
        }

        public static Vector3 TonemapAces(Vector3 cIn)
        {
            var inputMatrix = new[] { 0.59719f, 0.35458f, 0.04823f, 0.07600f, 0.90834f, 0.01566f, 0.02840f, 0.13383f, 0.83777f };
            var outputMatrix = new[] { 1.60475f, -0.53108f, -0.07367f, -0.10208f, 1.10813f, -0.00605f, -0.00327f, -0.07276f, 1.07602f };

            var v = AcesMul(inputMatrix, cIn);

            var a = v * (v + new Vector3(0.0245786f)) - new Vector3(0.000090537f);
            var b = v * (new Vector3(0.983729f) * v + new Vector3(0.432951f)) + new Vector3(0.238081f);
            v = a / b;

            return AcesMul(outputMatrix, v);
        }

        // Reinhard
        public static Vector3 TonemapReinhardRgb(Vector3 cIn, float hdrMax)
        {
            float white2 = hdrMax * hdrMax;
            return cIn * (Vector3.One + cIn / new Vector3(white2)) / (Vector3.One + cIn);
        }

        public static Vector3 TonemapReinhardOklab(Vector3 cIn, float hdrMax, float saturation)
        {
            float white2 = hdrMax * hdrMax;
            var oklabIn = LinearSrgbToOklab(cIn);
            float lumaIn = LumaOklab(oklabIn);

            float lumaOut = lumaIn * (1.0f + lumaIn / white2) / (1.0f + lumaIn);
            var oklabOut = ScaleOklabDesat(oklabIn, lumaOut, saturation);
            return OklabToLinearSrgb(oklabOut);
        }

        #endregion Tone Mapping Algorithms

        #region Levels Processing

        public static Vector3 ApplyLevels(Vector3 cIn, float levelMin, float levelMax, float gamma)
        {
            float scale = levelMax - levelMin;
            if (scale == 0f) return cIn; // Захист від ділення на нуль

            var oklabIn = LinearSrgbToOklab(cIn);
            float lumaIn = LumaOklab(oklabIn);

            float lumaOut = MathF.Pow(Math.Max(0, (lumaIn - levelMin) / scale), gamma);
            var oklabOut = ScaleOklab(oklabIn, lumaOut);

            return OklabToLinearSrgb(oklabOut);
        }

        #endregion Levels Processing
    }
}