using System.Numerics;
using System.Runtime.CompilerServices;

using VR.HDRFix.Helpers;

namespace VR.HDRFix.Models
{
    public class HdrHistogram
    {
        private readonly float[] _lumaVals;

        public HdrHistogram(Vector3[] pixels)
        {
            _lumaVals = pixels.Select(p => HdrMath.LumaRgb(p)).ToArray();

            Array.Sort(_lumaVals);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Percentile(float targetPercent)
        {
            if (_lumaVals.Length == 0)
                return 0f;

            int maxIndex = _lumaVals.Length - 1;
            int targetIndex = (int)(maxIndex * (targetPercent / 100.0));

            targetIndex = Math.Clamp(targetIndex, 0, maxIndex);

            return _lumaVals[targetIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float AverageBelowPercentile(float percent)
        {
            float maxLuma = Percentile(percent);
            float sum = 0f;
            int count = 0;

            foreach (var luma in _lumaVals)
            {
                if (luma > maxLuma)
                    continue;

                sum += luma;
                count++;
            }

            return count == 0 ? 0f : sum / count;
        }
    }
}