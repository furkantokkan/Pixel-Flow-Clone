using TMPro;
using PixelFlow.Runtime.Visuals;
using UnityEngine;

namespace PixelFlow.Runtime.Pigs
{
    [DisallowMultipleComponent]
    public sealed class PigView : MonoBehaviour
    {
        [SerializeField] private AtlasColorTarget atlasColorTarget;
        [SerializeField] private TMP_Text ammoText;
        [SerializeField] private Transform trayRoot;

        private void Awake()
        {
            EnsureReferences();
        }

        private void Reset()
        {
            EnsureReferences();
        }

        private void OnValidate()
        {
            EnsureReferences();
        }

        public void Render(PigModel model)
        {
            EnsureReferences();
            atlasColorTarget?.SetColor(model.Color);

            if (ammoText != null)
            {
                ammoText.text = model.Ammo > 0
                    ? model.Ammo.ToString()
                    : string.Empty;
            }

            if (trayRoot != null)
            {
                trayRoot.gameObject.SetActive(model.TrayVisible);
            }
        }

        public void Clear()
        {
            if (ammoText != null)
            {
                ammoText.text = string.Empty;
            }

            if (trayRoot != null)
            {
                trayRoot.gameObject.SetActive(false);
            }
        }

        private void EnsureReferences()
        {
            atlasColorTarget ??= GetComponent<AtlasColorTarget>();
            atlasColorTarget ??= GetComponentInChildren<AtlasColorTarget>(true);
            ammoText ??= GetComponentInChildren<TMP_Text>(true);
            trayRoot ??= ResolveTrayRoot();
        }

        private Transform ResolveTrayRoot()
        {
            Transform directChild = transform.Find("Tray");
            if (directChild != null)
            {
                return directChild;
            }

            var children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i] != transform && children[i].name == "Tray")
                {
                    return children[i];
                }
            }

            return null;
        }
    }
}
