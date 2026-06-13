using System;
using System.Collections.Generic;
using UnityEngine;

namespace Less3.CurveClips
{
    [AddComponentMenu("Less3/Curve Clip Player")]
    public sealed class CurveClipPlayer : MonoBehaviour
    {
        public Transform target;
        public List<CurveClip> clips = new List<CurveClip>();

        [Space]
        public bool restartIfAlreadyPlaying = true;

        private CurveClipPlayback playback;
        private CurveClip activeClip;

        public CurveClip ActiveClip => activeClip;
        public bool IsPlaying => playback != null && playback.IsPlaying;

        public event Action<CurveClip> OnClipStarted;
        public event Action<CurveClip> OnClipCompleted;

        private void Awake()
        {
            if (target == null)
                target = transform;
        }

        private void OnDisable()
        {
            Stop();
        }

        public bool Play()
        {
            return Play(PickRandomClip());
        }

        public bool Play(CurveClip clip)
        {
            if (clip == null)
                return false;

            Transform playTarget = GetTarget();
            if (playTarget == null)
                return false;

            if (IsPlaying)
            {
                if (!restartIfAlreadyPlaying)
                    return false;

                Stop();
            }

            activeClip = clip;

            playback = clip.Play(playTarget, null, () => OnPlaybackComplete(clip));
            OnClipStarted?.Invoke(clip);
            return true;
        }

        public void Stop()
        {
            if (playback != null && playback.IsPlaying)
                playback.Cancel();

            playback = null;
        }

        public void ResetTransform()
        {
            CurveClip.Cancel(GetTarget());
        }

        [Obsolete("Original transforms are captured automatically when the first clip starts.")]
        public void RecaptureOriginalTransform() { }

        private CurveClip PickRandomClip()
        {
            int validCount = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null)
                    validCount++;
            }

            if (validCount == 0)
                return null;

            int pick = UnityEngine.Random.Range(0, validCount);
            for (int i = 0; i < clips.Count; i++)
            {
                CurveClip clip = clips[i];
                if (clip == null)
                    continue;

                if (pick == 0)
                    return clip;

                pick--;
            }

            return null;
        }

        private void OnPlaybackComplete(CurveClip completedClip)
        {
            playback = null;
            OnClipCompleted?.Invoke(completedClip);
        }

        private Transform GetTarget()
        {
            return target != null ? target : transform;
        }
    }
}
