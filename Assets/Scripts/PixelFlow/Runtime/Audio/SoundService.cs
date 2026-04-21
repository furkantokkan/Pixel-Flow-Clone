using System;
using UnityEngine;
using VContainer.Unity;

namespace PixelFlow.Runtime.Audio
{
    public sealed class SoundService : ISoundService, IInitializable, IDisposable
    {
        private const string AudioResourceRoot = "Audio/";
        private const string HostObjectName = "__SoundService";

        private GameObject hostObject;
        private AudioSource sfxSource;
        private AudioSource musicSource;
        private AudioClip jumpClip;
        private AudioClip loseClip;
        private AudioClip popClip;
        private AudioClip shootClip;
        private AudioClip winClip;

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
            PlaySfx(popClip);
        }

        public void PlayPopupOpen()
        {
            PlaySfx(popClip);
        }

        public void PlayWin()
        {
            PlaySfx(winClip);
        }

        public void PlayLose()
        {
            PlaySfx(loseClip);
        }

        public void PlayJump()
        {
            PlaySfx(jumpClip);
        }

        public void PlayShoot()
        {
            PlaySfx(shootClip, 0.6f);
        }

        public void PlayPigSelect()
        {
            PlaySfx(popClip);
        }

        public void PlayPop()
        {
            PlaySfx(popClip);
        }

        private void PlaySfx(AudioClip clip, float volume = 1f)
        {
            EnsureInitialized();
            if (sfxSource == null || clip == null)
            {
                return;
            }

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
