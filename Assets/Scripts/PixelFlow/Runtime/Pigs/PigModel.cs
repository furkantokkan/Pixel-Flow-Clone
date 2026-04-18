using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Runtime.Pigs
{
    public sealed class PigModel
    {
        public PigColor Color { get; private set; } = PigColor.Pink;
        public int Ammo { get; private set; }
        public PigDirection Direction { get; private set; }
        public bool TrayVisible { get; private set; }
        public bool Queued { get; private set; }

        public void Configure(PigColor color, int ammo, PigDirection direction)
        {
            Color = color;
            Ammo = Mathf.Max(0, ammo);
            Direction = direction;
            TrayVisible = false;
            Queued = false;
        }

        public void SetAmmo(int ammo)
        {
            Ammo = Mathf.Max(0, ammo);
        }

        public bool TryConsumeAmmo(int amount = 1)
        {
            var clampedAmount = Mathf.Max(1, amount);
            if (Ammo < clampedAmount)
            {
                return false;
            }

            Ammo -= clampedAmount;
            return true;
        }

        public void SetDirection(PigDirection direction)
        {
            Direction = direction;
        }

        public void SetTrayVisible(bool visible)
        {
            TrayVisible = visible;
        }

        public void SetQueued(bool queued)
        {
            Queued = queued;
        }

        public void Reset()
        {
            Color = PigColor.Pink;
            Ammo = 0;
            Direction = PigDirection.None;
            TrayVisible = false;
            Queued = false;
        }
    }
}
