using System;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    public enum PigDirection
    {
        None = 0,
        Left = 1,
        Right = 2,
        Up = 3,
        Down = 4,
    }

    [Serializable]
    public struct PigQueueEntry
    {
        [SerializeField] private PigColor color;
        [SerializeField] private int ammo;
        [SerializeField] private int slotIndex;
        [SerializeField] private PigDirection direction;

        public PigQueueEntry(PigColor color, int ammo, int slotIndex, PigDirection direction = PigDirection.None)
        {
            this.color = color;
            this.ammo = ammo;
            this.slotIndex = slotIndex;
            this.direction = direction;
        }

        public PigColor Color => color;
        public int Ammo => ammo;
        public int SlotIndex => slotIndex;
        public PigDirection Direction => direction;
    }
}
