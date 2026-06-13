using System;
using System.Collections.Generic;
using UnityEngine;

namespace Less3.CurveClips
{
    public enum CurveClipUpdateMode : byte
    {
        DeltaTime,
        UnscaledDeltaTime,
        FixedDeltaTime
    }

    public enum CurveClipCurveGroup : byte
    {
        Position,
        Rotation,
        Scale,
        Custom
    }

    [Serializable]
    public class CustomCurve
    {
        public string name = "Custom";
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    [Serializable]
    public class CurveClipCurveVisibility
    {
        public string key;
        public bool visible;
    }

    public readonly struct CustomCurveSample
    {
        public readonly string Name;
        public readonly float Value;

        public CustomCurveSample(string name, float value)
        {
            Name = name;
            Value = value;
        }
    }

    public readonly struct CurveClipSample
    {
        public readonly float Time;
        public readonly float NormalizedTime;
        public readonly Vector3 Position;
        public readonly Vector3 RotationEuler;
        public readonly Vector3 Scale;
        public readonly IReadOnlyList<CustomCurveSample> CustomCurves;

        public CurveClipSample(
            float time,
            float normalizedTime,
            Vector3 position,
            Vector3 rotationEuler,
            Vector3 scale,
            IReadOnlyList<CustomCurveSample> customCurves)
        {
            Time = time;
            NormalizedTime = normalizedTime;
            Position = position;
            RotationEuler = rotationEuler;
            Scale = scale;
            CustomCurves = customCurves;
        }
    }

    public sealed class CurveClipPlayback
    {
        internal CurveClipRunner.PlaybackState State;
        internal Transform Target;
        internal CurveClip Clip;

        public bool IsPlaying => State != null && State.IsPlaying;

        public void Cancel()
        {
            CurveClipRunner.Instance.Cancel(this);
        }

        [Obsolete("Use Cancel instead.")]
        public void Stop() => Cancel();
    }

    [CreateAssetMenu(menuName = "Less3/Curve Clip", fileName = "New Curve Clip")]
    public class CurveClip : ScriptableObject
    {
        [Min(0.0001f)]
        public float duration = 1f;

        public CurveClipUpdateMode updateMode = CurveClipUpdateMode.DeltaTime;

        public AnimationCurve posX = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve posY = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve posZ = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve rotX = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve rotY = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve rotZ = AnimationCurve.Linear(0f, 0f, 1f, 0f);
        public AnimationCurve scaleX = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        public AnimationCurve scaleY = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        public AnimationCurve scaleZ = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        public List<CustomCurve> customCurves = new List<CustomCurve>();

        [HideInInspector]
        public List<CurveClipCurveVisibility> editorCurveVisibility = new List<CurveClipCurveVisibility>();

        public event Action<CurveClipSample> CustomCurvesSampled;
        public event Action<Transform> Completed;

        public CurveClipPlayback Play(Transform target)
        {
            return Play(target, Vector3.one, null, null);
        }

        public CurveClipPlayback Play(Transform target, float positionScale)
        {
            return Play(target, Vector3.one * positionScale, null, null);
        }

        public CurveClipPlayback Play(Transform target, Vector3 positionScale)
        {
            return Play(target, positionScale, null, null);
        }

        public CurveClipPlayback Play(Transform target, Renderer positionScaleRenderer)
        {
            return Play(target, GetRendererPositionScale(target, positionScaleRenderer), null, null);
        }

        public CurveClipPlayback Play(Transform target, GameObject positionScaleGameObject)
        {
            return Play(target, GetGameObjectPositionScale(target, positionScaleGameObject), null, null);
        }

        public CurveClipPlayback Play(
            Transform target,
            Action<CurveClipSample> onCustomCurvesSampled,
            Action onComplete = null)
        {
            return Play(target, Vector3.one, onCustomCurvesSampled, onComplete);
        }

        public CurveClipPlayback Play(
            Transform target,
            float positionScale,
            Action<CurveClipSample> onCustomCurvesSampled,
            Action onComplete = null)
        {
            return Play(target, Vector3.one * positionScale, onCustomCurvesSampled, onComplete);
        }

        public CurveClipPlayback Play(
            Transform target,
            Renderer positionScaleRenderer,
            Action<CurveClipSample> onCustomCurvesSampled,
            Action onComplete = null)
        {
            return Play(target, GetRendererPositionScale(target, positionScaleRenderer), onCustomCurvesSampled, onComplete);
        }

        public CurveClipPlayback Play(
            Transform target,
            GameObject positionScaleGameObject,
            Action<CurveClipSample> onCustomCurvesSampled,
            Action onComplete = null)
        {
            return Play(target, GetGameObjectPositionScale(target, positionScaleGameObject), onCustomCurvesSampled, onComplete);
        }

        public CurveClipPlayback Play(
            Transform target,
            Vector3 positionScale,
            Action<CurveClipSample> onCustomCurvesSampled,
            Action onComplete = null)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            return CurveClipRunner.Instance.Play(this, target, positionScale, onCustomCurvesSampled, onComplete);
        }

        public static void Cancel(Transform target)
        {
            if (target == null)
                return;

            CurveClipRunner.Instance.Cancel(target);
        }

        public static Vector3 GetRendererPositionScale(Transform target, Renderer renderer)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));

            TryGetRendererPositionBounds(target, renderer, out Bounds bounds);
            return bounds.size;
        }

        public static Vector3 GetGameObjectPositionScale(Transform target, GameObject gameObject)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (gameObject == null)
                throw new ArgumentNullException(nameof(gameObject));

            return TryGetGameObjectPositionBounds(target, gameObject, out Bounds bounds)
                ? bounds.size
                : Vector3.one;
        }

        public static bool TryGetGameObjectPositionBounds(Transform target, GameObject gameObject, out Bounds bounds)
        {
            bounds = default;
            if (target == null || gameObject == null)
                return false;

            Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!ShouldIncludeRenderer(renderer))
                    continue;

                if (!TryGetRendererPositionBounds(target, renderer, out Bounds rendererBounds))
                    continue;

                if (!hasBounds)
                {
                    bounds = rendererBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }

            return hasBounds;
        }

        public static bool TryGetRendererPositionBounds(Transform target, Renderer renderer, out Bounds bounds)
        {
            bounds = default;
            if (target == null || renderer == null)
                return false;

            Bounds rendererLocalBounds = renderer.localBounds;
            Transform rendererTransform = renderer.transform;
            Transform positionSpace = target.parent;
            Vector3 localMin = rendererLocalBounds.min;
            Vector3 localMax = rendererLocalBounds.max;

            Vector3 firstPoint = ToPositionSpace(
                new Vector3(localMin.x, localMin.y, localMin.z),
                rendererTransform,
                positionSpace);
            bounds = new Bounds(firstPoint, Vector3.zero);

            bounds.Encapsulate(ToPositionSpace(new Vector3(localMin.x, localMin.y, localMax.z), rendererTransform, positionSpace));
            bounds.Encapsulate(ToPositionSpace(new Vector3(localMin.x, localMax.y, localMin.z), rendererTransform, positionSpace));
            bounds.Encapsulate(ToPositionSpace(new Vector3(localMin.x, localMax.y, localMax.z), rendererTransform, positionSpace));
            bounds.Encapsulate(ToPositionSpace(new Vector3(localMax.x, localMin.y, localMin.z), rendererTransform, positionSpace));
            bounds.Encapsulate(ToPositionSpace(new Vector3(localMax.x, localMin.y, localMax.z), rendererTransform, positionSpace));
            bounds.Encapsulate(ToPositionSpace(new Vector3(localMax.x, localMax.y, localMin.z), rendererTransform, positionSpace));
            bounds.Encapsulate(ToPositionSpace(new Vector3(localMax.x, localMax.y, localMax.z), rendererTransform, positionSpace));

            return true;
        }

        public CurveClipSample Evaluate(float time)
        {
            float safeDuration = Mathf.Max(0.0001f, duration);
            float clampedTime = Mathf.Clamp(time, 0f, safeDuration);
            float normalizedTime = Mathf.Clamp01(clampedTime / safeDuration);

            var samples = new List<CustomCurveSample>(customCurves != null ? customCurves.Count : 0);
            if (customCurves != null)
            {
                for (int i = 0; i < customCurves.Count; i++)
                {
                    CustomCurve customCurve = customCurves[i];
                    if (customCurve == null || customCurve.curve == null)
                        continue;

                    samples.Add(new CustomCurveSample(
                        customCurve.name,
                        customCurve.curve.Evaluate(normalizedTime)));
                }
            }

            return new CurveClipSample(
                clampedTime,
                normalizedTime,
                new Vector3(Evaluate(posX, normalizedTime), Evaluate(posY, normalizedTime), Evaluate(posZ, normalizedTime)),
                new Vector3(Evaluate(rotX, normalizedTime), Evaluate(rotY, normalizedTime), Evaluate(rotZ, normalizedTime)),
                new Vector3(Evaluate(scaleX, normalizedTime), Evaluate(scaleY, normalizedTime), Evaluate(scaleZ, normalizedTime)),
                samples);
        }

        public void Sample(Transform target, float time)
        {
            if (target == null)
                return;

            ApplyRelativeLocalSample(target, Evaluate(time), TransformState.Capture(target));
        }

        internal void NotifyCustomCurvesSampled(CurveClipSample sample)
        {
            CustomCurvesSampled?.Invoke(sample);
        }

        internal void NotifyCompleted(Transform target)
        {
            Completed?.Invoke(target);
        }

        internal static void ApplyRelativeLocalSample(Transform target, CurveClipSample sample, TransformState baseTransform)
        {
            target.localPosition = baseTransform.LocalPosition + sample.Position;
            target.localRotation = baseTransform.LocalRotation * Quaternion.Euler(sample.RotationEuler);
            target.localScale = Vector3.Scale(baseTransform.LocalScale, sample.Scale);
        }

        private static float Evaluate(AnimationCurve curve, float time)
        {
            return curve != null ? curve.Evaluate(time) : 0f;
        }

        private static Vector3 ToPositionSpace(Vector3 rendererLocalPoint, Transform rendererTransform, Transform positionSpace)
        {
            Vector3 worldPoint = rendererTransform.TransformPoint(rendererLocalPoint);
            return positionSpace != null ? positionSpace.InverseTransformPoint(worldPoint) : worldPoint;
        }

        private static bool ShouldIncludeRenderer(Renderer renderer)
        {
            return renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
        }

        internal readonly struct TransformState
        {
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;
            public readonly Vector3 LocalScale;

            public TransformState(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
            {
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
            }

            public static TransformState Capture(Transform target)
            {
                return new TransformState(target.localPosition, target.localRotation, target.localScale);
            }
        }
    }

}
