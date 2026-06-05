using System;
using System.Collections;
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

    public enum CurveClipTransformSpace : byte
    {
        Local,
        World
    }

    public enum CurveClipValueMode : byte
    {
        Absolute,
        Relative
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
        internal Coroutine Coroutine;
        internal Transform Target;
        internal CurveClip Clip;

        public bool IsPlaying => Coroutine != null;

        public void Stop()
        {
            if (Coroutine == null)
                return;

            CurveClipRunner.Instance.StopCoroutine(Coroutine);
            Coroutine = null;
        }
    }

    [CreateAssetMenu(menuName = "Less3/Curve Clip", fileName = "New Curve Clip")]
    public class CurveClip : ScriptableObject
    {
        [Min(0.0001f)]
        public float duration = 1f;

        public CurveClipUpdateMode updateMode = CurveClipUpdateMode.DeltaTime;
        public CurveClipTransformSpace transformSpace = CurveClipTransformSpace.Local;
        public CurveClipValueMode valueMode = CurveClipValueMode.Relative;
        public bool applyPosition = true;
        public bool applyRotation = true;
        public bool applyScale = true;
        public bool sampleEndOnComplete = true;

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
            return Play(target, null, null);
        }

        public CurveClipPlayback Play(
            Transform target,
            Action<CurveClipSample> onCustomCurvesSampled,
            Action onComplete = null)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            var playback = new CurveClipPlayback
            {
                Target = target,
                Clip = this
            };

            playback.Coroutine = CurveClipRunner.Instance.StartCoroutine(PlayRoutine(
                playback,
                onCustomCurvesSampled,
                onComplete));

            return playback;
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
                        customCurve.curve.Evaluate(clampedTime)));
                }
            }

            return new CurveClipSample(
                clampedTime,
                normalizedTime,
                new Vector3(Evaluate(posX, clampedTime), Evaluate(posY, clampedTime), Evaluate(posZ, clampedTime)),
                new Vector3(Evaluate(rotX, clampedTime), Evaluate(rotY, clampedTime), Evaluate(rotZ, clampedTime)),
                new Vector3(Evaluate(scaleX, clampedTime), Evaluate(scaleY, clampedTime), Evaluate(scaleZ, clampedTime)),
                samples);
        }

        public void Sample(Transform target, float time)
        {
            if (target == null)
                return;

            ApplySample(target, Evaluate(time), CaptureBaseTransform(target));
        }

        private IEnumerator PlayRoutine(
            CurveClipPlayback playback,
            Action<CurveClipSample> onCustomCurvesSampled,
            Action onComplete)
        {
            Transform target = playback.Target;
            CurveClipBaseTransform baseTransform = CaptureBaseTransform(target);
            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.0001f, duration);

            while (target != null && elapsed < safeDuration)
            {
                ApplyAndNotify(target, elapsed, baseTransform, onCustomCurvesSampled);

                if (updateMode == CurveClipUpdateMode.FixedDeltaTime)
                    yield return new WaitForFixedUpdate();
                else
                    yield return null;

                elapsed += GetDeltaTime();
            }

            if (target != null && sampleEndOnComplete)
                ApplyAndNotify(target, safeDuration, baseTransform, onCustomCurvesSampled);

            playback.Coroutine = null;
            onComplete?.Invoke();
            Completed?.Invoke(target);
        }

        private void ApplyAndNotify(
            Transform target,
            float time,
            CurveClipBaseTransform baseTransform,
            Action<CurveClipSample> onCustomCurvesSampled)
        {
            CurveClipSample sample = Evaluate(time);
            ApplySample(target, sample, baseTransform);

            if (sample.CustomCurves.Count > 0)
            {
                onCustomCurvesSampled?.Invoke(sample);
                CustomCurvesSampled?.Invoke(sample);
            }
        }

        private void ApplySample(Transform target, CurveClipSample sample, CurveClipBaseTransform baseTransform)
        {
            Vector3 position = valueMode == CurveClipValueMode.Relative
                ? baseTransform.Position + sample.Position
                : sample.Position;
            Quaternion rotation = valueMode == CurveClipValueMode.Relative
                ? baseTransform.Rotation * Quaternion.Euler(sample.RotationEuler)
                : Quaternion.Euler(sample.RotationEuler);
            Vector3 scale = valueMode == CurveClipValueMode.Relative
                ? Vector3.Scale(baseTransform.Scale, sample.Scale)
                : sample.Scale;

            if (transformSpace == CurveClipTransformSpace.Local)
            {
                if (applyPosition)
                    target.localPosition = position;
                if (applyRotation)
                    target.localRotation = rotation;
            }
            else
            {
                if (applyPosition)
                    target.position = position;
                if (applyRotation)
                    target.rotation = rotation;
            }

            if (applyScale)
                target.localScale = scale;
        }

        private CurveClipBaseTransform CaptureBaseTransform(Transform target)
        {
            if (transformSpace == CurveClipTransformSpace.Local)
            {
                return new CurveClipBaseTransform(
                    target.localPosition,
                    target.localRotation,
                    target.localScale);
            }

            return new CurveClipBaseTransform(
                target.position,
                target.rotation,
                target.localScale);
        }

        private float GetDeltaTime()
        {
            switch (updateMode)
            {
                case CurveClipUpdateMode.UnscaledDeltaTime:
                    return Time.unscaledDeltaTime;
                case CurveClipUpdateMode.FixedDeltaTime:
                    return Time.fixedDeltaTime;
                default:
                    return Time.deltaTime;
            }
        }

        private static float Evaluate(AnimationCurve curve, float time)
        {
            return curve != null ? curve.Evaluate(time) : 0f;
        }

        private readonly struct CurveClipBaseTransform
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public CurveClipBaseTransform(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }
    }

    internal sealed class CurveClipRunner : MonoBehaviour
    {
        private static CurveClipRunner instance;

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
    }
}
