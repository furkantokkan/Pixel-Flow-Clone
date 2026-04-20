using UnityEngine;

namespace PixelFlow.Runtime.Tray
{
    [DisallowMultipleComponent]
    public sealed class TrayController : MonoBehaviour
    {
        [SerializeField] private TrayView view;

        private readonly TrayModel model = new();

        private void Awake()
        {
            EnsureReferences();
            Render();
        }

        private void Reset()
        {
            EnsureReferences();
        }

        private void OnValidate()
        {
            EnsureReferences();
            Render();
        }

        public void Configure(bool visible, bool occupied)
        {
            model.Configure(visible, occupied);
            Render();
        }

        public void SetVisible(bool visible)
        {
            model.SetVisible(visible);
            Render();
        }

        public void SetOccupied(bool occupied)
        {
            model.SetOccupied(occupied);
            Render();
        }

        public void ResetTray()
        {
            model.Reset();
            Render();
        }

        private void Render()
        {
            if (!Application.isPlaying)
            {
                if (model.Visible)
                {
                    view?.Render(model);
                }

                return;
            }

            gameObject.SetActive(model.Visible);
            if (model.Visible)
            {
                view?.Render(model);
            }
        }

        private void EnsureReferences()
        {
            view ??= GetComponent<TrayView>();
        }
    }
}
