using System;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [Serializable]
    public struct PixelCellData
    {
        [SerializeField] private Vector2Int position;
        [SerializeField] private PigColor color;

        public PixelCellData(Vector2Int position, PigColor color)
        {
            this.position = position;
            this.color = color;
        }

        public Vector2Int Position => position;
        public PigColor Color => color;
    }
}
