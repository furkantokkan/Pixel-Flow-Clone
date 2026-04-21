using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    internal static class AmmoStepUtility
    {
        public static int SnapUpToStep(int value, int step)
        {
            var positiveStep = Mathf.Max(1, step);
            var positiveValue = Mathf.Max(1, value);
            return ((positiveValue + positiveStep - 1) / positiveStep) * positiveStep;
        }

        public static int SnapToNearestStep(int value, int step)
        {
            var positiveStep = Mathf.Max(1, step);
            var positiveValue = Mathf.Max(1, value);
            var lower = Mathf.Max(positiveStep, (positiveValue / positiveStep) * positiveStep);
            var upper = SnapUpToStep(positiveValue, positiveStep);
            return positiveValue - lower <= upper - positiveValue ? lower : upper;
        }
    }
}
