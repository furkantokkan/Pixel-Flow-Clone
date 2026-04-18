using System;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [Serializable]
    public struct WaitingSlotData
    {
        [Range(0f, 1f)]
        [SerializeField] private float splinePercent;
        [SerializeField] private Vector2 localOffset;

        public WaitingSlotData(float splinePercent, Vector2 localOffset)
        {
            this.splinePercent = splinePercent;
            this.localOffset = localOffset;
        }

        public float SplinePercent => splinePercent;
        public Vector2 LocalOffset => localOffset;
    }
}
