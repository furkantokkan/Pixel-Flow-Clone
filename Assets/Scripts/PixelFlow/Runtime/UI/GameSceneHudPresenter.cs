using PixelFlow.Runtime.Levels;
using UnityEngine;
using VContainer;

namespace PixelFlow.Runtime.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameSceneHudView))]
    public sealed class GameSceneHudPresenter : MonoBehaviour
    {
        private GameSceneHudView view;
        private LevelSessionController levelSessionController;

        private LevelSessionController subscribedController;

        private void Reset()
        {
            ResolveView();
        }

        private void Awake()
        {
            ResolveView();
        }

        private void OnEnable()
        {
            ResolveView();
            BindButtons();
            BindController();
            RefreshPresentation();
        }

        private void OnDisable()
        {
            UnbindController();
            UnbindButtons();
        }

        private void OnDestroy()
        {
            UnbindController();
            UnbindButtons();
        }

        [Inject]
        public void Construct(LevelSessionController injectedLevelSessionController)
        {
            levelSessionController = injectedLevelSessionController;
            if (!isActiveAndEnabled)
            {
                return;
            }

            BindController();
            RefreshPresentation();
        }

        private void HandleLevelChanged(int currentLevelIndex)
        {
            RefreshLevelText(currentLevelIndex);
            view?.ShowPlayingState();
        }

        private void HandleLevelWon(int currentLevelIndex)
        {
            RefreshLevelText(currentLevelIndex);
            view?.ShowWinState();
        }

        private void HandleLevelLost(int currentLevelIndex)
        {
            RefreshLevelText(currentLevelIndex);
            view?.ShowLoseState();
        }

        private void ResolveView()
        {
            view ??= GetComponent<GameSceneHudView>();
        }

        private void BindController()
        {
            if (levelSessionController == null || ReferenceEquals(subscribedController, levelSessionController))
            {
                return;
            }

            UnbindController();
            subscribedController = levelSessionController;
            subscribedController.LevelChanged += HandleLevelChanged;
            subscribedController.LevelWon += HandleLevelWon;
            subscribedController.LevelLost += HandleLevelLost;
        }

        private void UnbindController()
        {
            if (subscribedController == null)
            {
                return;
            }

            subscribedController.LevelChanged -= HandleLevelChanged;
            subscribedController.LevelWon -= HandleLevelWon;
            subscribedController.LevelLost -= HandleLevelLost;
            subscribedController = null;
        }

        private void BindButtons()
        {
            ResolveView();
            if (view == null)
            {
                return;
            }

            view.RemoveRestartListener(HandleRestartClicked);
            view.RemoveNextLevelListener(HandleNextLevelClicked);
            view.AddRestartListener(HandleRestartClicked);
            view.AddNextLevelListener(HandleNextLevelClicked);
        }

        private void UnbindButtons()
        {
            if (view == null)
            {
                return;
            }

            view.RemoveRestartListener(HandleRestartClicked);
            view.RemoveNextLevelListener(HandleNextLevelClicked);
        }

        private void HandleRestartClicked()
        {
            if (levelSessionController == null)
            {
                return;
            }

            levelSessionController.RestartCurrentLevel();
        }

        private void HandleNextLevelClicked()
        {
            if (levelSessionController == null)
            {
                return;
            }

            levelSessionController.LoadNextLevel();
        }

        private void RefreshPresentation()
        {
            ResolveView();
            if (view == null)
            {
                return;
            }

            RefreshLevelText();

            if (levelSessionController == null)
            {
                view.ShowPlayingState();
                return;
            }

            switch (levelSessionController.CurrentRunState)
            {
                case LevelRunState.Won:
                    view.ShowWinState();
                    break;
                case LevelRunState.Lost:
                    view.ShowLoseState();
                    break;
                default:
                    view.ShowPlayingState();
                    break;
            }
        }

        private void RefreshLevelText()
        {
            if (view == null)
            {
                return;
            }

            var displayLevelNumber = levelSessionController != null
                ? levelSessionController.DisplayLevelIndex + 1
                : 1;
            view.SetLevelNumber(displayLevelNumber);
        }

        private void RefreshLevelText(int currentLevelIndex)
        {
            if (view == null)
            {
                return;
            }

            view.SetLevelNumber(currentLevelIndex + 1);
        }
    }
}
