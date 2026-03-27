using System.Numerics;

namespace VR.HDRFix
{
    public class HdrHistogram
    {
        private readonly float[] _lumaVals;

        public HdrHistogram(Vector3[] pixels)
        {
            // Рахуємо яскравість (Luma) для кожного пікселя
            _lumaVals = pixels.Select(p => HdrMath.LumaRgb(p)).ToArray();

            // Сортуємо для обчислення перцентилів (еквівалент par_sort_unstable_by)
            Array.Sort(_lumaVals);
        }

        public float Percentile(float targetPercent)
        {
            if (_lumaVals.Length == 0) return 0f;

            int maxIndex = _lumaVals.Length - 1;
            int targetIndex = (int)(maxIndex * (targetPercent / 100.0));

            // Захист від виходу за межі
            targetIndex = Math.Clamp(targetIndex, 0, maxIndex);

            return _lumaVals[targetIndex];
        }

        public float AverageBelowPercentile(float percent)
        {
            float maxLuma = Percentile(percent);
            float sum = 0f;
            int count = 0;

            foreach (var luma in _lumaVals)
            {
                if (luma > maxLuma) continue;
                sum += luma;
                count++;
            }

            return count == 0 ? 0f : sum / count;
        }
    }
}