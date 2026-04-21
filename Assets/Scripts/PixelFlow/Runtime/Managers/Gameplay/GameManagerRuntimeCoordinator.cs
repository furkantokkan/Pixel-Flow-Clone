using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Levels;
using PrimeTween;
using UnityEngine;

namespace PixelFlow.Runtime.Managers
{
    internal sealed class GameManagerRuntimeCoordinator
    {
        private const int PrimeTweenTweensCapacity = 2048;
        private static bool primeTweenCapacityConfigured;

        private readonly Func<int> targetFrameRateProvider;
        private readonly Func<int> playSessionVersionProvider;
        private readonly Func<GameSceneContext> sceneContextProvider;
        private readonly Func<LevelSessionController> levelSessionControllerProvider;
        private readonly Func<EnvironmentContext> currentEnvironmentProvider;
        private readonly Action<EnvironmentContext> constructEnvironment;
        private readonly Action resetForPlaySession;
        private readonly Action prewarmDispatchRuntime;

        private int observedPlaySessionVersion = -1;
        private CancellationTokenSource dispatchWarmupCts;

        public GameManagerRuntimeCoordinator(
            Func<int> targetFrameRateProvider,
            Func<int> playSessionVersionProvider,
            Func<GameSceneContext> sceneContextProvider,
            Func<LevelSessionController> levelSessionControllerProvider,
            Func<EnvironmentContext> currentEnvironmentProvider,
            Action<EnvironmentContext> constructEnvironment,
            Action resetForPlaySession,
            Action prewarmDispatchRuntime)
        {
            this.targetFrameRateProvider = targetFrameRateProvider;
            this.playSessionVersionProvider = playSessionVersionProvider;
            this.sceneContextProvider = sceneContextProvider;
            this.levelSessionControllerProvider = levelSessionControllerProvider;
            this.currentEnvironmentProvider = currentEnvironmentProvider;
            this.constructEnvironment = constructEnvironment;
            this.resetForPlaySession = resetForPlaySession;
            this.prewarmDispatchRuntime = prewarmDispatchRuntime;
        }

        public void Update()
        {
            ApplyFrameRateCap();
            BootstrapRuntimeIfNeeded();
        }

        public void ApplyFrameRateCap()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var resolvedTargetFrameRate = Mathf.Max(30, targetFrameRateProvider());
            if (ShouldDisableVSyncForCurrentPlatform()
                && QualitySettings.vSyncCount != 0)
            {
                QualitySettings.vSyncCount = 0;
            }

            if (Application.targetFrameRate != resolvedTargetFrameRate)
            {
                Application.targetFrameRate = resolvedTargetFrameRate;
            }
        }

        public void EnsurePrimeTweenCapacity()
        {
            if (primeTweenCapacityConfigured)
            {
                return;
            }

            PrimeTweenConfig.SetTweensCapacity(PrimeTweenTweensCapacity);
            primeTweenCapacityConfigured = true;
        }

        public void ScheduleDispatchWarmup()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CancelDispatchWarmup();
            dispatchWarmupCts = new CancellationTokenSource();
            RunDispatchWarmupAsync(dispatchWarmupCts.Token).Forget();
        }

        public void CancelDispatchWarmup()
        {
            if (dispatchWarmupCts == null)
            {
                return;
            }

            dispatchWarmupCts.Cancel();
            dispatchWarmupCts.Dispose();
            dispatchWarmupCts = null;
        }

        private void BootstrapRuntimeIfNeeded()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var sceneContext = sceneContextProvider();
            var resolvedLevelSessionController = levelSessionControllerProvider();
            if (sceneContext == null || resolvedLevelSessionController == null)
            {
                return;
            }

            var playSessionVersion = playSessionVersionProvider();
            if (observedPlaySessionVersion < 0)
            {
                observedPlaySessionVersion = playSessionVersion;
            }
            else if (observedPlaySessionVersion != playSessionVersion)
            {
                resetForPlaySession?.Invoke();
                observedPlaySessionVersion = playSessionVersion;
            }

            sceneContext.InitializeRuntimeSessionIfNeeded();
            if (!sceneContext.RuntimeSessionInitialized)
            {
                return;
            }

            var currentEnvironment = currentEnvironmentProvider();
            var resolvedEnvironment = currentEnvironment != null && currentEnvironment.gameObject != null && currentEnvironment.gameObject.activeInHierarchy
                ? currentEnvironment
                : sceneContext.EnvironmentInstance != null && sceneContext.EnvironmentInstance.gameObject != null && sceneContext.EnvironmentInstance.gameObject.activeInHierarchy
                    ? sceneContext.EnvironmentInstance
                    : sceneContext.EnsureEnvironment();

            if (resolvedEnvironment == null)
            {
                return;
            }

            if (currentEnvironment != resolvedEnvironment)
            {
                constructEnvironment?.Invoke(resolvedEnvironment);
            }

            if (!resolvedLevelSessionController.HasLoadedInitialLevel)
            {
                resolvedLevelSessionController.LoadInitialLevelIfNeeded();
                return;
            }

            if (resolvedLevelSessionController.CurrentRunState == LevelRunState.Won
                || resolvedLevelSessionController.CurrentRunState == LevelRunState.Lost)
            {
                return;
            }

            resolvedLevelSessionController.EnsureCurrentLevelLoaded();
        }

        private async UniTaskVoid RunDispatchWarmupAsync(CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                prewarmDispatchRuntime?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static bool ShouldDisableVSyncForCurrentPlatform()
        {
            return Application.isMobilePlatform
                || Application.isEditor
                || Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.OSXPlayer
                || Application.platform == RuntimePlatform.LinuxPlayer;
        }
    }
}
