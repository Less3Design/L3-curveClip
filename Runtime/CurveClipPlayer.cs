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
        public bool resetTransformBeforePlay = true;
        public bool resetOnComplete;

        private CurveClipPlayback playback;
        private CurveClip activeClip;
        private TransformState originalTransform;
        private bool hasOriginalTransform;

        public CurveClip ActiveClip => activeClip;
        public bool IsPlaying => playback != null && playback.IsPlaying;

        private void Awake()
        {
            if (target == null)
                target = transform;
        }

        private void OnEnable()
        {
            CaptureOriginalTransform();
        }

        private void OnDisable()
        {
            Stop();
            RestoreOriginalTransform(activeClip);
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

            if (!hasOriginalTransform)
                CaptureOriginalTransform();

            if (IsPlaying)
            {
                if (!restartIfAlreadyPlaying)
                    return false;

                Stop();
            }

            activeClip = clip;

            if (resetTransformBeforePlay)
                RestoreOriginalTransform(clip);

            playback = clip.Play(playTarget, null, OnPlaybackComplete);
            return true;
        }

        public void Stop()
        {
            if (playback != null && playback.IsPlaying)
                playback.Stop();

            playback = null;
        }

        public void ResetTransform()
        {
            RestoreOriginalTransform(activeClip);
        }

        public void RecaptureOriginalTransform()
        {
            CaptureOriginalTransform();
        }

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

            int pick = Random.Range(0, validCount);
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

        private void CaptureOriginalTransform()
        {
            Transform captureTarget = GetTarget();
            if (captureTarget == null)
            {
                hasOriginalTransform = false;
                return;
            }

            originalTransform = new TransformState(
                captureTarget.localPosition,
                captureTarget.localRotation,
                captureTarget.position,
                captureTarget.rotation,
                captureTarget.localScale);
            hasOriginalTransform = true;
        }

        private void RestoreOriginalTransform(CurveClip clip)
        {
            if (!hasOriginalTransform)
                return;

            Transform restoreTarget = GetTarget();
            if (restoreTarget == null)
                return;

            if (clip != null && clip.transformSpace == CurveClipTransformSpace.World)
            {
                restoreTarget.position = originalTransform.WorldPosition;
                restoreTarget.rotation = originalTransform.WorldRotation;
            }
            else
            {
                restoreTarget.localPosition = originalTransform.LocalPosition;
                restoreTarget.localRotation = originalTransform.LocalRotation;
            }

            restoreTarget.localScale = originalTransform.LocalScale;
        }

        private void OnPlaybackComplete()
        {
            playback = null;
            if (resetOnComplete)
                RestoreOriginalTransform(activeClip);
        }

        private Transform GetTarget()
        {
            return target != null ? target : transform;
        }

        private readonly struct TransformState
        {
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;
            public readonly Vector3 WorldPosition;
            public readonly Quaternion WorldRotation;
            public readonly Vector3 LocalScale;

            public TransformState(
                Vector3 localPosition,
                Quaternion localRotation,
                Vector3 worldPosition,
                Quaternion worldRotation,
                Vector3 localScale)
            {
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                WorldPosition = worldPosition;
                WorldRotation = worldRotation;
                LocalScale = localScale;
            }
        }
    }
}
