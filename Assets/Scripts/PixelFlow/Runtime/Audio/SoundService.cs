using System;
using UnityEngine;
using VContainer.Unity;

namespace PixelFlow.Runtime.Audio
{
    public sealed class SoundService : ISoundService, IInitializable, IDisposable
    {
        private const string AudioResourceRoot = "Audio/";
        private const string HostObjectName = "__SoundService";
        private const float PopMinInterval = 0.035f;
        private const float JumpMinInterval = 0.06f;
        private const float ShootMinInterval = 0.025f;
        private const float OutcomeMinInterval = 0.2f;

        private GameObject hostObject;
        private AudioSource sfxSource;
        private AudioSource musicSource;
        private AudioClip jumpClip;
        private AudioClip loseClip;
        private AudioClip popClip;
        private AudioClip shootClip;
        private AudioClip winClip;
        private float lastJumpPlayTime = float.NegativeInfinity;
        private float lastLosePlayTime = float.NegativeInfinity;
        private float lastPopPlayTime = float.NegativeInfinity;
        private float lastShootPlayTime = float.NegativeInfinity;
        private float lastWinPlayTime = float.NegativeInfinity;

        public void Initialize()
        {
            EnsureInitialized();
        }

        public void Dispose()
        {
            if (hostObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(hostObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(hostObject);
            }

            hostObject = null;
            sfxSource = null;
            musicSource = null;
        }

        public void PlayClick()
        {
            PlaySfx(popClip, 1f, PopMinInterval, ref lastPopPlayTime);
        }

        public void PlayPopupOpen()
        {
            PlaySfx(popClip, 1f, PopMinInterval, ref lastPopPlayTime);
        }

        public void PlayWin()
        {
            PlaySfx(winClip, 1f, OutcomeMinInterval, ref lastWinPlayTime);
        }

        public void PlayLose()
        {
            PlaySfx(loseClip, 1f, OutcomeMinInterval, ref lastLosePlayTime);
        }

        public void PlayJump()
        {
            PlaySfx(jumpClip, 1f, JumpMinInterval, ref lastJumpPlayTime);
        }

        public void PlayShoot()
        {
            PlaySfx(shootClip, 0.6f, ShootMinInterval, ref lastShootPlayTime);
        }

        public void PlayPigSelect()
        {
            PlaySfx(popClip, 1f, PopMinInterval, ref lastPopPlayTime);
        }

        public void PlayPop()
        {
            PlaySfx(popClip, 1f, PopMinInterval, ref lastPopPlayTime);
        }

        private void PlaySfx(AudioClip clip, float volume, float minInterval, ref float lastPlayTime)
        {
            EnsureInitialized();
            if (sfxSource == null || clip == null)
            {
                return;
            }

            var currentTime = Application.isPlaying ? Time.unscaledTime : Time.realtimeSinceStartup;
            if (currentTime - lastPlayTime < minInterval)
            {
                return;
            }

            lastPlayTime = currentTime;
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void EnsureInitialized()
        {
            if (hostObject == null)
            {
                hostObject = new GameObject(HostObjectName);
                UnityEngine.Object.DontDestroyOnLoad(hostObject);
            }

            if (sfxSource == null)
            {
                sfxSource = hostObject.GetComponent<AudioSource>();
                if (sfxSource == null)
                {
                    sfxSource = hostObject.AddComponent<AudioSource>();
                }

                sfxSource.playOnAwake = false;
                sfxSource.loop = false;
                sfxSource.spatialBlend = 0f;
                sfxSource.dopplerLevel = 0f;
                sfxSource.reverbZoneMix = 0f;
            }

            if (musicSource == null)
            {
                musicSource = hostObject.AddComponent<AudioSource>();
                musicSource.playOnAwake = false;
                musicSource.loop = true;
                musicSource.spatialBlend = 0f;
            }

            if (jumpClip == null)
            {
                LoadClips();
            }
        }

        private void LoadClips()
        {
            jumpClip = LoadClip("jump");
            loseClip = LoadClip("lose");
            popClip = LoadClip("pop");
            shootClip = LoadClip("shoot");
            winClip = LoadClip("win");
        }

        private static AudioClip LoadClip(string clipName)
        {
            return Resources.Load<AudioClip>($"{AudioResourceRoot}{clipName}");
        }
    }
}
