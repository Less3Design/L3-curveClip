using System;
using System.Collections.Generic;
using UnityEngine;

namespace Less3.CurveClips
{
    internal sealed class CurveClipRunner : MonoBehaviour
    {
        private static CurveClipRunner instance;
        private readonly List<PlaybackState> playbacks = new List<PlaybackState>();
        private readonly Dictionary<Transform, TargetState> targets = new Dictionary<Transform, TargetState>();
        private readonly List<TargetState> dirtyTargets = new List<TargetState>();
        private readonly List<PlaybackState> completionBuffer = new List<PlaybackState>();
        private bool applyingDirtyTargets;

        public static CurveClipRunner Instance
        {
            get
            {
                if (instance != null)
                    return instance;

                var gameObject = new GameObject("L3 Curve Clip Runner")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                instance = gameObject.AddComponent<CurveClipRunner>();
                DontDestroyOnLoad(gameObject);
                return instance;
            }
        }

        internal CurveClipPlayback Play(
            CurveClip clip,
            Transform target,
            Action<CurveClipSample> onCustomCurvesSampled,
            Action onComplete)
        {
            TargetState targetState = GetOrCreateTargetState(target);
            var playback = new CurveClipPlayback
            {
                Target = target,
                Clip = clip
            };

            var state = new PlaybackState(playback, clip, targetState, onCustomCurvesSampled, onComplete);
            playback.State = state;
            playbacks.Add(state);
            targetState.Playbacks.Add(state);

            Sample(state, 0f);
            MarkDirty(targetState);
            ApplyDirtyTargets();
            return playback;
        }

        internal void Cancel(CurveClipPlayback playback)
        {
            PlaybackState state = playback != null ? playback.State : null;
            if (state == null)
                return;

            RemovePlayback(state);
            ApplyTarget(state.TargetState);
        }

        internal void Cancel(Transform target)
        {
            if (target == null || !targets.TryGetValue(target, out TargetState targetState))
                return;

            for (int i = targetState.Playbacks.Count - 1; i >= 0; i--)
                RemovePlayback(targetState.Playbacks[i], false);

            targetState.Playbacks.Clear();
            RestoreAndRemoveTarget(targetState);
        }

        private void Update()
        {
            Advance(CurveClipUpdateMode.DeltaTime, Time.deltaTime);
            Advance(CurveClipUpdateMode.UnscaledDeltaTime, Time.unscaledDeltaTime);
            ApplyDirtyTargets();
            InvokeCompletions();
        }

        private void FixedUpdate()
        {
            Advance(CurveClipUpdateMode.FixedDeltaTime, Time.fixedDeltaTime);
            ApplyDirtyTargets();
            InvokeCompletions();
        }

        private void Advance(CurveClipUpdateMode updateMode, float deltaTime)
        {
            for (int i = playbacks.Count - 1; i >= 0; i--)
            {
                PlaybackState state = playbacks[i];
                if (state.Clip.updateMode != updateMode)
                    continue;

                if (state.TargetState.Target == null)
                {
                    RemovePlayback(state);
                    if (state.TargetState.Playbacks.Count == 0)
                        RemoveTargetState(state.TargetState);
                    continue;
                }

                state.Elapsed += deltaTime;
                float safeDuration = Mathf.Max(0.0001f, state.Clip.duration);
                bool completed = state.Elapsed >= safeDuration;
                Sample(state, completed ? safeDuration : state.Elapsed);
                MarkDirty(state.TargetState);

                if (completed)
                {
                    completionBuffer.Add(state);
                    RemovePlayback(state);
                }
            }
        }

        private void Sample(PlaybackState state, float time)
        {
            state.CurrentSample = state.Clip.Evaluate(time);
            if (state.CurrentSample.CustomCurves.Count == 0)
                return;

            state.OnCustomCurvesSampled?.Invoke(state.CurrentSample);
            state.Clip.NotifyCustomCurvesSampled(state.CurrentSample);
        }

        private void ApplyDirtyTargets()
        {
            applyingDirtyTargets = true;
            try
            {
                for (int i = 0; i < dirtyTargets.Count; i++)
                {
                    TargetState targetState = dirtyTargets[i];
                    targetState.IsDirty = false;
                    ApplyTarget(targetState);
                }

                dirtyTargets.Clear();
            }
            finally
            {
                applyingDirtyTargets = false;
            }
        }

        private void ApplyTarget(TargetState targetState)
        {
            Transform target = targetState.Target;
            if (target == null)
            {
                RemoveTargetState(targetState);
                return;
            }

            if (targetState.Playbacks.Count == 0)
            {
                RestoreAndRemoveTarget(targetState);
                return;
            }

            Vector3 positionOffset = Vector3.zero;
            Vector3 rotationOffset = Vector3.zero;
            Vector3 scaleMultiplier = Vector3.one;

            for (int i = 0; i < targetState.Playbacks.Count; i++)
            {
                CurveClipSample sample = targetState.Playbacks[i].CurrentSample;
                positionOffset += sample.Position;
                rotationOffset += sample.RotationEuler;
                scaleMultiplier = Vector3.Scale(scaleMultiplier, sample.Scale);
            }

            CurveClipSample combinedSample = new CurveClipSample(
                0f,
                0f,
                positionOffset,
                rotationOffset,
                scaleMultiplier,
                Array.Empty<CustomCurveSample>());
            CurveClip.ApplyRelativeLocalSample(target, combinedSample, targetState.OriginalTransform);
        }

        private TargetState GetOrCreateTargetState(Transform target)
        {
            if (targets.TryGetValue(target, out TargetState targetState))
                return targetState;

            targetState = new TargetState(target, CurveClip.TransformState.Capture(target));
            targets.Add(target, targetState);
            return targetState;
        }

        private void MarkDirty(TargetState targetState)
        {
            if (targetState.IsDirty)
                return;

            targetState.IsDirty = true;
            dirtyTargets.Add(targetState);
        }

        private void RemovePlayback(PlaybackState state, bool removeFromTarget = true)
        {
            state.IsPlaying = false;
            state.Playback.State = null;
            playbacks.Remove(state);

            if (removeFromTarget)
                state.TargetState.Playbacks.Remove(state);
        }

        private void RestoreAndRemoveTarget(TargetState targetState)
        {
            Transform target = targetState.Target;
            if (target != null)
            {
                target.localPosition = targetState.OriginalTransform.LocalPosition;
                target.localRotation = targetState.OriginalTransform.LocalRotation;
                target.localScale = targetState.OriginalTransform.LocalScale;
            }

            RemoveTargetState(targetState);
        }

        private void RemoveTargetState(TargetState targetState)
        {
            if (!ReferenceEquals(targetState.Target, null))
                targets.Remove(targetState.Target);

            targetState.IsDirty = false;
            if (!applyingDirtyTargets)
                dirtyTargets.Remove(targetState);
        }

        private void InvokeCompletions()
        {
            for (int i = 0; i < completionBuffer.Count; i++)
            {
                PlaybackState state = completionBuffer[i];
                state.OnComplete?.Invoke();
                state.Clip.NotifyCompleted(state.TargetState.Target);
            }

            completionBuffer.Clear();
        }

        internal sealed class PlaybackState
        {
            public readonly CurveClipPlayback Playback;
            public readonly CurveClip Clip;
            public readonly TargetState TargetState;
            public readonly Action<CurveClipSample> OnCustomCurvesSampled;
            public readonly Action OnComplete;
            public CurveClipSample CurrentSample;
            public float Elapsed;
            public bool IsPlaying = true;

            public PlaybackState(
                CurveClipPlayback playback,
                CurveClip clip,
                TargetState targetState,
                Action<CurveClipSample> onCustomCurvesSampled,
                Action onComplete)
            {
                Playback = playback;
                Clip = clip;
                TargetState = targetState;
                OnCustomCurvesSampled = onCustomCurvesSampled;
                OnComplete = onComplete;
            }
        }

        internal sealed class TargetState
        {
            public readonly Transform Target;
            public readonly CurveClip.TransformState OriginalTransform;
            public readonly List<PlaybackState> Playbacks = new List<PlaybackState>();
            public bool IsDirty;

            public TargetState(Transform target, CurveClip.TransformState originalTransform)
            {
                Target = target;
                OriginalTransform = originalTransform;
            }
        }
    }
}
