using System;
using PixelFlow.Runtime.Audio;
using PixelFlow.Runtime.Levels;
using UnityEngine;
using VContainer.Unity;

namespace PixelFlow.Runtime.UI
{
    public sealed class GameSceneHudPresenter : IInitializable, IDisposable
    {
        private const float OutcomeButtonLockDuration = 0.35f;

        private readonly GameSceneHudView view;
        private readonly LevelSessionController levelSessionController;
        private readonly ISoundService soundService;

        private LevelSessionController subscribedController;
        private float buttonsLockedUntilUnscaledTime;

        public GameSceneHudPresenter(
            GameSceneHudView view,
            LevelSessionController levelSessionController,
            ISoundService soundService)
        {
            this.view = view;
            this.levelSessionController = levelSessionController;
            this.soundService = soundService;
        }

        public void Initialize()
        {
            BindButtons();
            BindController();
            RefreshPresentation();
        }

        public void Dispose()
        {
            UnbindController();
            UnbindButtons();
        }

        private void HandleLevelChanged(int currentLevelIndex)
        {
            RefreshLevelText(currentLevelIndex);
            buttonsLockedUntilUnscaledTime = 0f;
            view?.ShowPlayingState();
        }

        private void HandleLevelWon(int currentLevelIndex)
        {
            RefreshLevelText(currentLevelIndex);
            LockOutcomeButtonsTemporarily();
            soundService?.PlayWin();
            view?.ShowWinState();
        }

        private void HandleLevelLost(int currentLevelIndex)
        {
            RefreshLevelText(currentLevelIndex);
            buttonsLockedUntilUnscaledTime = 0f;
            soundService?.PlayLose();
            view?.ShowLoseState();
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

            soundService?.PlayClick();
            levelSessionController.RestartCurrentLevel();
        }

        private void HandleNextLevelClicked()
        {
            if (levelSessionController == null || AreOutcomeButtonsLocked())
            {
                return;
            }

            soundService?.PlayClick();
            levelSessionController.LoadNextLevel();
        }

        private void RefreshPresentation()
        {
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
                    LockOutcomeButtonsTemporarily();
                    view.ShowWinState();
                    break;
                case LevelRunState.Lost:
                    buttonsLockedUntilUnscaledTime = 0f;
                    view.ShowLoseState();
                    break;
                default:
                    buttonsLockedUntilUnscaledTime = 0f;
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

        private void LockOutcomeButtonsTemporarily()
        {
            buttonsLockedUntilUnscaledTime = Time.unscaledTime + OutcomeButtonLockDuration;
        }

        private bool AreOutcomeButtonsLocked()
        {
            return Time.unscaledTime < buttonsLockedUntilUnscaledTime;
        }
    }
}
