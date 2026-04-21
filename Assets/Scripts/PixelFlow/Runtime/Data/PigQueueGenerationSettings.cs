using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace PixelFlow.Runtime.Data
{
    [Serializable]
    public sealed class PigQueueGenerationSettings
    {
        public const int MinAmmoStep = 1;
        public const int MaxAmmoStep = 10;

        [Min(1)]
        [SerializeField] private int holdingSlotCount = 5;

        [Min(1)]
        [SerializeField] private int minAmmoPerPig = 4;

        [Min(1)]
        [SerializeField] private int targetAmmoPerPig = 6;

        [Min(1)]
        [SerializeField] private int maxAmmoPerPig = 8;

        [Min(1)]
        [FormerlySerializedAs("maxPigsPerColor")]
        [SerializeField] private int minPigsPerColor = 4;

        [Min(1)]
        [SerializeField] private int maxDesiredPigsPerColor = 8;

        [Range(MinAmmoStep, MaxAmmoStep)]
        [SerializeField] private int ammoStep = MinAmmoStep;

        [Min(1f)]
        [SerializeField] private float ammoMultiplier = 1f;

        [SerializeField] private bool autoGenerateOnSave = true;
        [SerializeField] private bool autoGenerateOnImport = true;

        public int HoldingSlotCount
        {
            get => holdingSlotCount;
            set => holdingSlotCount = Mathf.Max(1, value);
        }

        public int MinAmmoPerPig
        {
            get => minAmmoPerPig;
            set => minAmmoPerPig = Mathf.Max(1, value);
        }

        public int TargetAmmoPerPig
        {
            get => targetAmmoPerPig;
            set => targetAmmoPerPig = Mathf.Max(1, value);
        }

        public int MaxAmmoPerPig
        {
            get => maxAmmoPerPig;
            set => maxAmmoPerPig = Mathf.Max(1, value);
        }

        public int MinPigsPerColor
        {
            get => minPigsPerColor;
            set => minPigsPerColor = Mathf.Max(1, value);
        }

        public int MaxPigsPerColor
        {
            get => maxDesiredPigsPerColor;
            set => maxDesiredPigsPerColor = Mathf.Max(MinPigsPerColor, value);
        }

        public int TargetPigsPerColor
        {
            get => Mathf.Clamp(
                Mathf.RoundToInt((MinPigsPerColor + MaxPigsPerColor) * 0.5f),
                MinPigsPerColor,
                MaxPigsPerColor);
            set
            {
                var clampedValue = Mathf.Max(1, value);
                minPigsPerColor = clampedValue;
                maxDesiredPigsPerColor = clampedValue;
            }
        }

        public int AmmoStep
        {
            get => ammoStep;
            set => ammoStep = ClampAmmoStep(value);
        }

        public float AmmoMultiplier
        {
            get => ammoMultiplier;
            set => ammoMultiplier = Mathf.Max(1f, value);
        }

        public bool AutoGenerateOnSave
        {
            get => autoGenerateOnSave;
            set => autoGenerateOnSave = value;
        }

        public bool AutoGenerateOnImport
        {
            get => autoGenerateOnImport;
            set => autoGenerateOnImport = value;
        }

        public void NormalizeAmmoDistributionSettings()
        {
            ammoStep = ClampAmmoStep(ammoStep);
            targetAmmoPerPig = Mathf.Max(1, targetAmmoPerPig);
            if (minAmmoPerPig <= 0)
            {
                minAmmoPerPig = Mathf.Max(ammoStep, targetAmmoPerPig - (ammoStep * 2));
            }

            if (maxAmmoPerPig <= 0)
            {
                maxAmmoPerPig = Mathf.Max(targetAmmoPerPig + (ammoStep * 2), minAmmoPerPig);
            }

            minAmmoPerPig = AmmoStepUtility.SnapUpToStep(Mathf.Max(1, minAmmoPerPig), ammoStep);
            maxAmmoPerPig = Mathf.Max(minAmmoPerPig, AmmoStepUtility.SnapUpToStep(Mathf.Max(1, maxAmmoPerPig), ammoStep));
            targetAmmoPerPig = AmmoStepUtility.SnapToNearestStep(Mathf.Clamp(targetAmmoPerPig, minAmmoPerPig, maxAmmoPerPig), ammoStep);
            targetAmmoPerPig = Mathf.Clamp(targetAmmoPerPig, minAmmoPerPig, maxAmmoPerPig);
            minPigsPerColor = Mathf.Max(1, minPigsPerColor);
            maxDesiredPigsPerColor = Mathf.Max(minPigsPerColor, maxDesiredPigsPerColor);
            ammoMultiplier = Mathf.Max(1f, ammoMultiplier);
        }

        public PigQueueGenerationSettings Clone()
        {
            var clone = new PigQueueGenerationSettings
            {
                holdingSlotCount = holdingSlotCount,
                minAmmoPerPig = minAmmoPerPig,
                targetAmmoPerPig = targetAmmoPerPig,
                maxAmmoPerPig = maxAmmoPerPig,
                minPigsPerColor = minPigsPerColor,
                maxDesiredPigsPerColor = maxDesiredPigsPerColor,
                ammoStep = ammoStep,
                ammoMultiplier = ammoMultiplier,
                autoGenerateOnSave = autoGenerateOnSave,
                autoGenerateOnImport = autoGenerateOnImport,
            };

            clone.NormalizeAmmoDistributionSettings();
            return clone;
        }

        public static int ClampAmmoStep(int value)
        {
            return Mathf.Clamp(value, MinAmmoStep, MaxAmmoStep);
        }
    }
}
