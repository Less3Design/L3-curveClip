using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Less3.CurveClips.Editor
{
    [CustomEditor(typeof(CurveClip))]
    public sealed class CurveClipEditor : UnityEditor.Editor
    {
        private const string CombinedGraphEditorPrefKey = "Less3.CurveClips.Editor.ShowCombinedGraph";
        private const string PreviewLoopDelayEditorPrefKey = "Less3.CurveClips.Editor.PreviewLoopDelay";
        private const float GraphSectionHorizontalScrollbarGuard = 36f;
        private const float GraphSectionVerticalScrollbarGuard = 20f;
        private const float GraphToolbarFallbackHeight = 22f;
        private const float MinSeparateGraphHeight = 100f;
        private const float MinCombinedGraphHeight = 200f;
        private const float PreviewLoopDelayControlWidth = 112f;
        private const float PreviewLoopDelayControlHeight = 22f;
        private const float PreviewPrefabControlWidth = 190f;
        private const float PreviewControlSpacing = 4f;

        private VisualElement inspectorRoot;
        private VisualElement curveList;
        private VisualElement graphSection;
        private VisualElement graphToolbar;
        private CurveClipGraphElement positionGraph;
        private CurveClipGraphElement rotationGraph;
        private CurveClipGraphElement scaleGraph;
        private CurveClipGraphElement customGraph;
        private CurveClipGraphElement combinedGraph;
        private VisualElement separateGraphsContainer;
        private VisualElement separateGraphsTopRow;
        private VisualElement separateGraphsBottomRow;
        private VisualElement combinedGraphContainer;
        private ToolbarToggle separateModeToggle;
        private ToolbarToggle combinedModeToggle;
        private ScrollView inspectorScrollView;
        private VisualElement inspectorViewport;
        private PreviewRenderUtility previewUtility;
        private Mesh previewCubeMesh;
        private Material previewCubeMaterial;
        private Material previewAxisXMaterial;
        private Material previewAxisYMaterial;
        private Material previewAxisZMaterial;
        private GameObject previewPrefab;
        private GameObject previewPrefabInstance;
        private GameObject previewPrefabInstanceSource;
        private Vector3 previewPrefabBasePosition;
        private Quaternion previewPrefabBaseRotation;
        private Vector3 previewPrefabBaseScale;
        private double previewStartTime;
        private float previewOrbitYaw = 140f;
        private float previewOrbitPitch = -22f;
        private float previewLoopDelay;
        private bool showCombinedGraph;
        private bool graphFitScheduled;
        private bool fittingGraphSection;
        private bool graphSectionSized;

        private void OnEnable()
        {
            previewStartTime = EditorApplication.timeSinceStartup;
            previewLoopDelay = Mathf.Max(0f, EditorPrefs.GetFloat(PreviewLoopDelayEditorPrefKey, 0f));
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            CleanupPreview();
        }

        private void OnUndoRedoPerformed()
        {
            serializedObject.Update();
            RebuildCurveList();
            RepaintGraphs();
            ScheduleGraphSectionFit();
            Repaint();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            inspectorRoot = root;
            root.style.flexGrow = 1;
            root.RegisterCallback<AttachToPanelEvent>(OnInspectorRootAttached);
            root.RegisterCallback<DetachFromPanelEvent>(OnInspectorRootDetached);
            root.RegisterCallback<GeometryChangedEvent>(OnGraphSizingGeometryChanged);

            if (serializedObject.isEditingMultipleObjects)
            {
                root.Add(new HelpBox("Curve Clip graph editing supports one asset at a time.", HelpBoxMessageType.Info));
                InspectorElement.FillDefaultInspector(root, serializedObject, this);
                return root;
            }

            VisualElement settings = BuildSettingsSection();
            settings.style.marginLeft = 2;
            settings.style.marginRight = 2;
            root.Add(settings);
            VisualElement customCurves = BuildCustomCurvesSection();
            customCurves.style.marginLeft = 2;
            customCurves.style.marginRight = 2;
            root.Add(customCurves);
            graphSection = BuildGraphSection();
            root.Add(graphSection);
            root.Bind(serializedObject);
            ScheduleGraphSectionFit();
            return root;
        }

        public override bool HasPreviewGUI()
        {
            return !serializedObject.isEditingMultipleObjects && target is CurveClip;
        }

        public override GUIContent GetPreviewTitle()
        {
            return new GUIContent("3D Preview");
        }

        public override bool RequiresConstantRepaint()
        {
            return HasPreviewGUI();
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            Rect loopDelayRect = GetPreviewLoopDelayControlRect(rect);
            Rect prefabRect = GetPreviewPrefabControlRect(rect);
            HandlePreviewInput(rect, loopDelayRect, prefabRect);

            CurveClip clip = target as CurveClip;
            if (Event.current.type == EventType.Repaint && clip != null)
            {
                EnsurePreviewResources();
                RenderCurvePreview(rect, background, clip);
            }

            DrawPreviewLoopDelayControl(loopDelayRect);
            DrawPreviewPrefabControl(prefabRect);
        }

        private void HandlePreviewInput(Rect rect, Rect loopDelayRect, Rect prefabRect)
        {
            Event evt = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);

            if (evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
            {
                if (loopDelayRect.Contains(evt.mousePosition) || prefabRect.Contains(evt.mousePosition))
                    return;

                GUIUtility.hotControl = controlId;
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && evt.button == 0 && GUIUtility.hotControl == controlId)
            {
                previewOrbitYaw += evt.delta.x * 0.45f;
                previewOrbitPitch = Mathf.Clamp(previewOrbitPitch + evt.delta.y * 0.45f, -85f, 85f);
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0 && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }

        private Rect GetPreviewLoopDelayControlRect(Rect previewRect)
        {
            return new Rect(
                previewRect.xMax - PreviewLoopDelayControlWidth - 6f,
                previewRect.yMin + 6f,
                PreviewLoopDelayControlWidth,
                PreviewLoopDelayControlHeight);
        }

        private Rect GetPreviewPrefabControlRect(Rect previewRect)
        {
            return new Rect(
                previewRect.xMax - PreviewPrefabControlWidth - 6f,
                previewRect.yMin + 6f + PreviewLoopDelayControlHeight + PreviewControlSpacing,
                PreviewPrefabControlWidth,
                PreviewLoopDelayControlHeight);
        }

        private void DrawPreviewLoopDelayControl(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.55f));

            Rect labelRect = new Rect(rect.x + 6f, rect.y + 3f, 38f, 16f);
            Rect fieldRect = new Rect(rect.x + 47f, rect.y + 2f, rect.width - 53f, 17f);

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.78f);
            GUI.Label(labelRect, "Delay", EditorStyles.miniLabel);
            GUI.color = previousColor;

            EditorGUI.BeginChangeCheck();
            float delay = EditorGUI.FloatField(fieldRect, GUIContent.none, previewLoopDelay, EditorStyles.miniTextField);
            if (EditorGUI.EndChangeCheck())
            {
                previewLoopDelay = Mathf.Max(0f, delay);
                EditorPrefs.SetFloat(PreviewLoopDelayEditorPrefKey, previewLoopDelay);
                Repaint();
            }
        }

        private void DrawPreviewPrefabControl(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.55f));

            Rect labelRect = new Rect(rect.x + 6f, rect.y + 3f, 42f, 16f);
            Rect fieldRect = new Rect(rect.x + 52f, rect.y + 2f, rect.width - 58f, 17f);

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.78f);
            GUI.Label(labelRect, "Prefab", EditorStyles.miniLabel);
            GUI.color = previousColor;

            EditorGUI.BeginChangeCheck();
            GameObject selectedPrefab = EditorGUI.ObjectField(
                fieldRect,
                GUIContent.none,
                previewPrefab,
                typeof(GameObject),
                false) as GameObject;
            if (EditorGUI.EndChangeCheck())
            {
                previewPrefab = selectedPrefab;
                CleanupPreviewPrefabInstance();
                Repaint();
            }
        }

        private void EnsurePreviewResources()
        {
            if (previewUtility == null)
            {
                previewUtility = new PreviewRenderUtility();
                previewUtility.cameraFieldOfView = 30f;
                previewUtility.lights[0].intensity = 1.15f;
                previewUtility.lights[0].transform.rotation = Quaternion.Euler(45f, 35f, 0f);
                previewUtility.lights[1].intensity = 0.65f;
            }

            if (previewCubeMesh == null)
            {
                previewCubeMesh = CreatePreviewCubeMesh();
                previewCubeMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            if (previewCubeMaterial == null)
            {
                previewCubeMaterial = CreatePreviewMaterial(Color.white);
            }

            if (previewAxisXMaterial == null)
            {
                previewAxisXMaterial = CreatePreviewMaterial(new Color(0.94f, 0.28f, 0.30f, 1f));
                previewAxisYMaterial = CreatePreviewMaterial(new Color(0.32f, 0.78f, 0.38f, 1f));
                previewAxisZMaterial = CreatePreviewMaterial(new Color(0.30f, 0.55f, 1f, 1f));
            }
        }

        private static Material CreatePreviewMaterial(Color color)
        {
            var material = new Material(FindPreviewShader());
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            return material;
        }

        private static Shader FindPreviewShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                return shader;

            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
                return shader;

            shader = Shader.Find("Standard");
            if (shader != null)
                return shader;

            return Shader.Find("Diffuse");
        }

        private void RenderCurvePreview(Rect rect, GUIStyle background, CurveClip clip)
        {
            float duration = Mathf.Max(0.0001f, clip.duration);
            float time = GetPreviewPlaybackTime(duration);
            CurveClipSample sample = clip.Evaluate(time);
            bool usePrefabPreview = EnsurePreviewPrefabInstance();
            Vector3 prefabPositionScale = usePrefabPreview ? GetPreviewPrefabPositionScale() : Vector3.one;
            Bounds bounds = usePrefabPreview
                ? CalculatePreviewBounds(clip, duration, previewPrefabInstance, prefabPositionScale)
                : CalculatePreviewBounds(clip, duration);

            previewUtility.BeginPreview(rect, background);
            ConfigurePreviewCamera(bounds);
            DrawPreviewAxes(bounds);
            if (usePrefabPreview)
            {
                ApplyPreviewSampleToPrefab(sample, prefabPositionScale);
            }
            else
            {
                previewUtility.DrawMesh(
                    previewCubeMesh,
                    Matrix4x4.TRS(sample.Position, Quaternion.Euler(sample.RotationEuler), sample.Scale),
                    previewCubeMaterial,
                    0);
            }

            previewUtility.camera.Render();
            previewUtility.EndAndDrawPreview(rect);
        }

        private bool EnsurePreviewPrefabInstance()
        {
            if (previewPrefab == null)
            {
                CleanupPreviewPrefabInstance();
                return false;
            }

            if (previewPrefabInstance != null && previewPrefabInstanceSource == previewPrefab)
                return true;

            CleanupPreviewPrefabInstance();

            previewPrefabInstance = PrefabUtility.InstantiatePrefab(previewPrefab) as GameObject;
            if (previewPrefabInstance == null)
                previewPrefabInstance = Instantiate(previewPrefab);

            if (previewPrefabInstance == null)
                return false;

            previewPrefabInstance.name = previewPrefab.name + " Preview";
            previewPrefabInstance.hideFlags = HideFlags.HideAndDontSave;
            SetPreviewHideFlags(previewPrefabInstance);
            previewPrefabInstanceSource = previewPrefab;

            Transform instanceTransform = previewPrefabInstance.transform;
            previewPrefabBasePosition = instanceTransform.localPosition;
            previewPrefabBaseRotation = instanceTransform.localRotation;
            previewPrefabBaseScale = instanceTransform.localScale;

            previewUtility.AddSingleGO(previewPrefabInstance);
            return true;
        }

        private static void SetPreviewHideFlags(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        private Vector3 GetPreviewPrefabPositionScale()
        {
            if (previewPrefabInstance == null)
                return Vector3.one;

            ResetPreviewPrefabTransform();
            return CurveClip.GetGameObjectPositionScale(previewPrefabInstance.transform, previewPrefabInstance);
        }

        private void ResetPreviewPrefabTransform()
        {
            if (previewPrefabInstance == null)
                return;

            Transform instanceTransform = previewPrefabInstance.transform;
            instanceTransform.localPosition = previewPrefabBasePosition;
            instanceTransform.localRotation = previewPrefabBaseRotation;
            instanceTransform.localScale = previewPrefabBaseScale;
        }

        private void ApplyPreviewSampleToPrefab(CurveClipSample sample, Vector3 positionScale)
        {
            if (previewPrefabInstance == null)
                return;

            Transform instanceTransform = previewPrefabInstance.transform;
            instanceTransform.localPosition = previewPrefabBasePosition + Vector3.Scale(sample.Position, positionScale);
            instanceTransform.localRotation = previewPrefabBaseRotation * Quaternion.Euler(sample.RotationEuler);
            instanceTransform.localScale = Vector3.Scale(previewPrefabBaseScale, sample.Scale);
        }

        private float GetPreviewPlaybackTime(float duration)
        {
            float delay = Mathf.Max(0f, previewLoopDelay);
            float cycleDuration = duration + delay;
            float cycleTime = Mathf.Repeat((float)(EditorApplication.timeSinceStartup - previewStartTime), cycleDuration);
            return Mathf.Min(cycleTime, duration);
        }

        private void DrawPreviewAxes(Bounds bounds)
        {
            float axisLength = PreviewAxisLength(bounds);
            float axisThickness = Mathf.Max(0.025f, axisLength * 0.035f);

            previewUtility.DrawMesh(
                previewCubeMesh,
                Matrix4x4.TRS(new Vector3(axisLength * 0.5f, 0f, 0f), Quaternion.identity, new Vector3(axisLength, axisThickness, axisThickness)),
                previewAxisXMaterial,
                0);
            previewUtility.DrawMesh(
                previewCubeMesh,
                Matrix4x4.TRS(new Vector3(0f, axisLength * 0.5f, 0f), Quaternion.identity, new Vector3(axisThickness, axisLength, axisThickness)),
                previewAxisYMaterial,
                0);
            previewUtility.DrawMesh(
                previewCubeMesh,
                Matrix4x4.TRS(new Vector3(0f, 0f, axisLength * 0.5f), Quaternion.identity, new Vector3(axisThickness, axisThickness, axisLength)),
                previewAxisZMaterial,
                0);
        }

        private void ConfigurePreviewCamera(Bounds bounds)
        {
            Vector3 direction = Quaternion.Euler(previewOrbitPitch, previewOrbitYaw, 0f) * Vector3.forward;
            float radius = Mathf.Max(1f, bounds.extents.magnitude);
            float distance = radius / Mathf.Sin(previewUtility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            Transform cameraTransform = previewUtility.camera.transform;
            cameraTransform.position = bounds.center - direction * Mathf.Max(3.5f, distance * 1.15f);
            cameraTransform.rotation = Quaternion.LookRotation(bounds.center - cameraTransform.position, Vector3.up);
            previewUtility.camera.nearClipPlane = 0.01f;
            previewUtility.camera.farClipPlane = Mathf.Max(100f, distance * 6f);
            previewUtility.camera.clearFlags = CameraClearFlags.Color;
            previewUtility.camera.backgroundColor = GraphSectionBackgroundColor();
        }

        private static Bounds CalculatePreviewBounds(CurveClip clip, float duration)
        {
            CurveClipSample first = clip.Evaluate(0f);
            Bounds bounds = new Bounds(first.Position, PreviewSampleSize(first.Scale));
            const int steps = 48;
            for (int i = 1; i <= steps; i++)
            {
                float time = duration * i / steps;
                CurveClipSample sample = clip.Evaluate(time);
                bounds.Encapsulate(new Bounds(sample.Position, PreviewSampleSize(sample.Scale)));
            }

            float axisLength = PreviewAxisLength(bounds);
            bounds.Encapsulate(Vector3.zero);
            bounds.Encapsulate(Vector3.right * axisLength);
            bounds.Encapsulate(Vector3.up * axisLength);
            bounds.Encapsulate(Vector3.forward * axisLength);
            return bounds;
        }

        private Bounds CalculatePreviewBounds(CurveClip clip, float duration, GameObject prefabInstance, Vector3 positionScale)
        {
            CurveClipSample first = clip.Evaluate(0f);
            ApplyPreviewSampleToPrefab(first, positionScale);
            if (!TryCalculateRendererBounds(prefabInstance, out Bounds bounds))
                return CalculatePreviewBounds(clip, duration);

            const int steps = 48;
            for (int i = 1; i <= steps; i++)
            {
                float time = duration * i / steps;
                CurveClipSample sample = clip.Evaluate(time);
                ApplyPreviewSampleToPrefab(sample, positionScale);
                if (TryCalculateRendererBounds(prefabInstance, out Bounds sampleBounds))
                    bounds.Encapsulate(sampleBounds);
            }

            float axisLength = PreviewAxisLength(bounds);
            bounds.Encapsulate(Vector3.zero);
            bounds.Encapsulate(Vector3.right * axisLength);
            bounds.Encapsulate(Vector3.up * axisLength);
            bounds.Encapsulate(Vector3.forward * axisLength);
            return bounds;
        }

        private static bool TryCalculateRendererBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            return root != null && CurveClip.TryGetGameObjectPositionBounds(root.transform, root, out bounds);
        }

        private static float PreviewAxisLength(Bounds bounds)
        {
            return Mathf.Max(1f, bounds.extents.magnitude * 0.65f);
        }

        private static Vector3 PreviewSampleSize(Vector3 scale)
        {
            return new Vector3(
                Mathf.Max(0.05f, Mathf.Abs(scale.x)),
                Mathf.Max(0.05f, Mathf.Abs(scale.y)),
                Mathf.Max(0.05f, Mathf.Abs(scale.z)));
        }

        private void CleanupPreview()
        {
            CleanupPreviewPrefabInstance();

            if (previewUtility != null)
            {
                previewUtility.Cleanup();
                previewUtility = null;
            }

            if (previewCubeMesh != null)
            {
                DestroyImmediate(previewCubeMesh);
                previewCubeMesh = null;
            }

            if (previewCubeMaterial != null)
            {
                DestroyImmediate(previewCubeMaterial);
                previewCubeMaterial = null;
            }

            if (previewAxisXMaterial != null)
            {
                DestroyImmediate(previewAxisXMaterial);
                previewAxisXMaterial = null;
            }

            if (previewAxisYMaterial != null)
            {
                DestroyImmediate(previewAxisYMaterial);
                previewAxisYMaterial = null;
            }

            if (previewAxisZMaterial != null)
            {
                DestroyImmediate(previewAxisZMaterial);
                previewAxisZMaterial = null;
            }
        }

        private void CleanupPreviewPrefabInstance()
        {
            if (previewPrefabInstance != null)
            {
                DestroyImmediate(previewPrefabInstance);
                previewPrefabInstance = null;
            }

            previewPrefabInstanceSource = null;
        }

        private static Mesh CreatePreviewCubeMesh()
        {
            var mesh = new Mesh { name = "Curve Clip Preview Cube" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f)
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private VisualElement BuildSettingsSection()
        {
            var foldout = new Foldout { text = "Settings", value = true };
            foldout.style.marginBottom = 3;

            foldout.Add(CreateProperty("duration"));
            foldout.Add(CreateProperty("updateMode"));

            return foldout;
        }

        private PropertyField CreateProperty(string propertyName)
        {
            return new PropertyField(serializedObject.FindProperty(propertyName));
        }

        private void OnInspectorRootAttached(AttachToPanelEvent evt)
        {
            inspectorScrollView = inspectorRoot?.GetFirstAncestorOfType<ScrollView>();
            inspectorViewport = inspectorScrollView != null ? inspectorScrollView.contentViewport : inspectorRoot?.panel?.visualTree;
            if (inspectorViewport != null)
                inspectorViewport.RegisterCallback<GeometryChangedEvent>(OnGraphSizingGeometryChanged);

            ScheduleGraphSectionFit();
        }

        private void OnInspectorRootDetached(DetachFromPanelEvent evt)
        {
            if (inspectorViewport != null)
                inspectorViewport.UnregisterCallback<GeometryChangedEvent>(OnGraphSizingGeometryChanged);

            inspectorScrollView = null;
            inspectorViewport = null;
            graphFitScheduled = false;
            fittingGraphSection = false;
            graphSectionSized = false;
        }

        private void OnGraphSizingGeometryChanged(GeometryChangedEvent evt)
        {
            ScheduleGraphSectionFit();
        }

        private void ScheduleGraphSectionFit()
        {
            if (graphSection == null)
                return;

            if (TryFitGraphSectionToInspector())
                return;

            if (graphFitScheduled)
                return;

            graphFitScheduled = true;
            graphSection.schedule.Execute(() =>
            {
                graphFitScheduled = false;
                TryFitGraphSectionToInspector();
            }).ExecuteLater(0);
        }

        private bool TryFitGraphSectionToInspector()
        {
            if (!HasValidGraphSizingBounds())
                return false;

            FitGraphSectionToInspector();
            return true;
        }

        private bool HasValidGraphSizingBounds()
        {
            if (graphSection == null || graphSection.panel == null || graphSection.parent == null)
                return false;

            VisualElement viewport = GetInspectorViewport();
            if (viewport == null)
                return false;

            return viewport.worldBound.width > 1f
                && viewport.worldBound.height > 1f
                && graphSection.parent.worldBound.width > 1f;
        }

        private void FitGraphSectionToInspector()
        {
            if (fittingGraphSection || graphSection == null || graphSection.panel == null)
                return;

            fittingGraphSection = true;
            try
            {
                FitGraphSectionHorizontally();

                float availableHeight = CalculateAvailableGraphSectionHeight();
                float toolbarHeight = GetOuterHeight(graphToolbar, GraphToolbarFallbackHeight);
                float graphAreaHeight = Mathf.Max(MinCombinedGraphHeight, availableHeight - toolbarHeight);
                if (showCombinedGraph)
                {
                    SetSeparateGraphHeight(MinSeparateGraphHeight);
                    SetCombinedGraphHeight(graphAreaHeight);
                    graphSection.style.height = toolbarHeight + graphAreaHeight;
                }
                else
                {
                    float rowHeight = Mathf.Max(MinSeparateGraphHeight, Mathf.Floor(graphAreaHeight * 0.5f));
                    SetSeparateGraphHeight(rowHeight);
                    SetCombinedGraphHeight(MinCombinedGraphHeight);
                    graphSection.style.height = toolbarHeight + rowHeight * 2f;
                }

                if (!graphSectionSized)
                {
                    graphSection.style.visibility = Visibility.Visible;
                    graphSectionSized = true;
                }

                RepaintGraphs();
            }
            finally
            {
                fittingGraphSection = false;
            }
        }

        private void FitGraphSectionHorizontally()
        {
            VisualElement viewport = GetInspectorViewport();
            VisualElement parent = graphSection.parent;
            if (viewport == null || parent == null)
                return;

            float parentWidth = parent.resolvedStyle.width;
            if (float.IsNaN(parentWidth) || parentWidth <= 0f)
                parentWidth = parent.worldBound.width;

            float viewportWidth = viewport.resolvedStyle.width;
            if (float.IsNaN(viewportWidth) || viewportWidth <= 0f)
                viewportWidth = viewport.worldBound.width;

            float desiredWidth = Mathf.Max(1f, Mathf.Min(parentWidth, viewportWidth) - GraphSectionHorizontalScrollbarGuard);

            graphSection.style.marginLeft = 0;
            graphSection.style.marginRight = 0;
            graphSection.style.minWidth = 0;
            graphSection.style.width = desiredWidth;
            graphSection.style.maxWidth = desiredWidth;
        }

        private float CalculateAvailableGraphSectionHeight()
        {
            VisualElement viewport = GetInspectorViewport();
            if (viewport == null)
                return showCombinedGraph ? MinCombinedGraphHeight + GraphToolbarFallbackHeight : MinSeparateGraphHeight * 2f + GraphToolbarFallbackHeight;

            float viewportHeight = viewport.resolvedStyle.height;
            if (float.IsNaN(viewportHeight) || viewportHeight <= 0f)
                viewportHeight = viewport.worldBound.height;

            float graphTop = graphSection.worldBound.yMin - viewport.worldBound.yMin;
            if (inspectorScrollView != null && inspectorScrollView.contentContainer != null)
                graphTop = graphSection.worldBound.yMin - inspectorScrollView.contentContainer.worldBound.yMin;

            float minimumHeight = showCombinedGraph ? MinCombinedGraphHeight + GraphToolbarFallbackHeight : MinSeparateGraphHeight * 2f + GraphToolbarFallbackHeight;
            float remainingHeight = viewportHeight - Mathf.Max(0f, graphTop) - GraphSectionVerticalScrollbarGuard;
            return Mathf.Max(minimumHeight, remainingHeight);
        }

        private VisualElement GetInspectorViewport()
        {
            if (inspectorViewport != null)
                return inspectorViewport;

            inspectorScrollView = inspectorRoot?.GetFirstAncestorOfType<ScrollView>();
            inspectorViewport = inspectorScrollView != null ? inspectorScrollView.contentViewport : inspectorRoot?.panel?.visualTree;
            return inspectorViewport;
        }

        private static float GetOuterHeight(VisualElement element, float fallback)
        {
            if (element == null)
                return fallback;

            float height = element.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 0f)
                height = fallback;

            return height + element.resolvedStyle.marginTop + element.resolvedStyle.marginBottom;
        }

        private void SetSeparateGraphHeight(float height)
        {
            if (separateGraphsContainer != null)
                separateGraphsContainer.style.height = height * 2f;
            if (separateGraphsTopRow != null)
                separateGraphsTopRow.style.height = height;
            if (separateGraphsBottomRow != null)
                separateGraphsBottomRow.style.height = height;

            positionGraph.style.height = height;
            rotationGraph.style.height = height;
            scaleGraph.style.height = height;
            customGraph.style.height = height;
        }

        private void SetCombinedGraphHeight(float height)
        {
            if (combinedGraphContainer != null)
                combinedGraphContainer.style.height = height;

            combinedGraph.style.height = height;
        }

        private VisualElement BuildCustomCurvesSection()
        {
            var foldout = new Foldout { text = "Custom Curves", value = false };
            foldout.style.marginBottom = 3;

            curveList = new VisualElement();
            curveList.style.marginLeft = 2;
            foldout.Add(curveList);

            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginTop = 3;

            var addCustom = new Button(AddCustomCurve) { text = "Add Custom Curve" };
            addCustom.style.flexGrow = 1;
            toolbar.Add(addCustom);
            foldout.Add(toolbar);

            RebuildCurveList();
            ScheduleGraphSectionFit();
            return foldout;
        }

        private VisualElement BuildGraphSection()
        {
            var root = new VisualElement();
            root.style.marginTop = 0;
            root.style.marginLeft = 0;
            root.style.marginRight = 0;
            root.style.paddingLeft = 0;
            root.style.paddingRight = 0;
            root.style.paddingTop = 0;
            root.style.paddingBottom = 0;
            root.style.minWidth = 0;
            root.style.backgroundColor = GraphSectionBackgroundColor();
            root.style.visibility = Visibility.Hidden;
            showCombinedGraph = EditorPrefs.GetBool(CombinedGraphEditorPrefKey, false);

            positionGraph = CreateGraph("Position", CurveClipCurveGroup.Position, new Rect(-.1f, -1f, 1.2f, 2f));
            rotationGraph = CreateGraph("Rotation", CurveClipCurveGroup.Rotation, new Rect(-.1f, -90f, 1.2f, 180f));
            scaleGraph = CreateGraph("Scale", CurveClipCurveGroup.Scale, new Rect(-.1f, 0f, 1.2f, 2f));
            customGraph = CreateGraph("Custom", CurveClipCurveGroup.Custom, new Rect(-.1f, 0f, 1.2f, 1f));
            combinedGraph = CreateCombinedGraph();

            graphToolbar = CreateGraphModeSwitcher();
            root.Add(graphToolbar);

            separateGraphsContainer = new VisualElement();
            separateGraphsContainer.style.minWidth = 0;
            separateGraphsTopRow = CreateGraphRow(positionGraph, rotationGraph, true);
            separateGraphsBottomRow = CreateGraphRow(scaleGraph, customGraph, false);
            separateGraphsContainer.Add(separateGraphsTopRow);
            separateGraphsContainer.Add(separateGraphsBottomRow);
            root.Add(separateGraphsContainer);

            combinedGraphContainer = new VisualElement();
            combinedGraphContainer.style.minWidth = 0;
            combinedGraphContainer.Add(combinedGraph);
            root.Add(combinedGraphContainer);

            UpdateGraphMode();
            return root;
        }

        private VisualElement CreateGraphModeSwitcher()
        {
            var toolbar = new Toolbar();
            toolbar.style.marginBottom = 0;

            separateModeToggle = new ToolbarToggle { text = "Separate" };
            separateModeToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    showCombinedGraph = false;
                    EditorPrefs.SetBool(CombinedGraphEditorPrefKey, showCombinedGraph);
                }

                UpdateGraphMode();
            });

            combinedModeToggle = new ToolbarToggle { text = "Combined" };
            combinedModeToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    showCombinedGraph = true;
                    EditorPrefs.SetBool(CombinedGraphEditorPrefKey, showCombinedGraph);
                }

                UpdateGraphMode();
            });

            toolbar.Add(separateModeToggle);
            toolbar.Add(combinedModeToggle);
            return toolbar;
        }

        private void UpdateGraphMode()
        {
            if (separateGraphsContainer != null)
                separateGraphsContainer.style.display = showCombinedGraph ? DisplayStyle.None : DisplayStyle.Flex;
            if (combinedGraphContainer != null)
                combinedGraphContainer.style.display = showCombinedGraph ? DisplayStyle.Flex : DisplayStyle.None;

            separateModeToggle?.SetValueWithoutNotify(!showCombinedGraph);
            combinedModeToggle?.SetValueWithoutNotify(showCombinedGraph);
            ScheduleGraphSectionFit();
            RepaintGraphs();
        }

        private VisualElement CreateGraphRow(CurveClipGraphElement left, CurveClipGraphElement right, bool addBottomMargin)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 0;
            row.style.minWidth = 0;

            left.style.flexGrow = 1;
            left.style.flexShrink = 1;
            left.style.flexBasis = 0;
            left.style.minWidth = 0;
            left.style.marginRight = 0;

            right.style.flexGrow = 1;
            right.style.flexShrink = 1;
            right.style.flexBasis = 0;
            right.style.minWidth = 0;
            right.style.marginLeft = 0;

            row.Add(left);
            row.Add(right);
            return row;
        }

        private static Color GraphSectionBackgroundColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.13f, 0.13f, 0.13f)
                : new Color(0.70f, 0.70f, 0.70f);
        }

        private CurveClipGraphElement CreateGraph(string title, CurveClipCurveGroup group, Rect defaultView)
        {
            var graph = new CurveClipGraphElement(
                title,
                serializedObject,
                serializedObject.FindProperty("duration"),
                () => GetChannels(group),
                () => GetChannels(group, false),
                IsVisible,
                SetCurveVisible,
                RepaintGraphs,
                defaultView);
            graph.style.height = MinSeparateGraphHeight;
            graph.style.marginBottom = 0;

            return graph;
        }

        private CurveClipGraphElement CreateCombinedGraph()
        {
            var graph = new CurveClipGraphElement(
                "All Curves",
                serializedObject,
                serializedObject.FindProperty("duration"),
                () => GetCombinedChannels(true),
                () => GetCombinedChannels(false),
                IsVisible,
                SetCurveVisible,
                RepaintGraphs,
                new Rect(-.1f, -90f, 1.2f, 180f));
            graph.style.height = MinCombinedGraphHeight;
            graph.style.marginBottom = 0;
            return graph;
        }

        private void RebuildCurveList()
        {
            if (curveList == null)
                return;

            curveList.Clear();
            SerializedProperty customCurves = serializedObject.FindProperty("customCurves");
            if (customCurves == null)
                return;

            for (int i = 0; i < customCurves.arraySize; i++)
                curveList.Add(CreateCustomCurveNameRow(i));
        }

        private VisualElement CreateCustomCurveNameRow(int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 22;
            row.style.marginBottom = 1;

            SerializedProperty nameProperty = serializedObject.FindProperty("customCurves.Array.data[" + index + "].name");
            var nameField = new TextField();
            nameField.BindProperty(nameProperty);
            nameField.style.flexGrow = 1;
            nameField.style.marginRight = 4;
            nameField.RegisterValueChangedCallback(_ => RepaintGraphs());
            row.Add(nameField);

            var remove = new Button(() => RemoveCustomCurve(index)) { text = "-" };
            remove.tooltip = "Remove custom curve";
            remove.style.width = 22;
            remove.style.marginLeft = 3;
            row.Add(remove);

            return row;
        }

        private void SetAllVisible(bool visible)
        {
            serializedObject.Update();
            foreach (CurveClipCurveGroup group in Enum.GetValues(typeof(CurveClipCurveGroup)))
            {
                IReadOnlyList<CurveChannel> channels = GetChannels(group, false);
                for (int i = 0; i < channels.Count; i++)
                    SetCurveVisibility(channels[i].VisibilityKey, visible);
            }

            ApplyVisibilityChange();
            RebuildCurveList();
            ScheduleGraphSectionFit();
            RepaintGraphs();
        }

        private void SetCurveVisible(CurveChannel channel, bool visible)
        {
            serializedObject.Update();
            SetCurveVisibility(channel.VisibilityKey, visible);
            ApplyVisibilityChange();
            RebuildCurveList();
            FrameGraphs();
        }

        private void AddCustomCurve()
        {
            serializedObject.Update();
            SerializedProperty customCurves = serializedObject.FindProperty("customCurves");
            int index = customCurves.arraySize;
            customCurves.InsertArrayElementAtIndex(index);

            SerializedProperty element = customCurves.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("name").stringValue = "Custom " + (index + 1);
            element.FindPropertyRelative("curve").animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            SetCurveVisibility(CustomCurveVisibilityKey(index), true);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);

            RebuildCurveList();
            ScheduleGraphSectionFit();
            RepaintGraphs();
        }

        private void RemoveCustomCurve(int index)
        {
            serializedObject.Update();
            SerializedProperty customCurves = serializedObject.FindProperty("customCurves");
            if (index >= 0 && index < customCurves.arraySize)
            {
                customCurves.DeleteArrayElementAtIndex(index);
                RemoveCurveVisibility(CustomCurveVisibilityKey(index));
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(serializedObject.targetObject);
            }

            RebuildCurveList();
            RepaintGraphs();
        }

        private void RepaintGraphs()
        {
            positionGraph?.Refresh();
            rotationGraph?.Refresh();
            scaleGraph?.Refresh();
            customGraph?.Refresh();
            combinedGraph?.Refresh();
        }

        private void FrameGraphs()
        {
            positionGraph?.FrameToFit();
            rotationGraph?.FrameToFit();
            scaleGraph?.FrameToFit();
            customGraph?.FrameToFit();
            combinedGraph?.FrameToFit();
        }

        private IReadOnlyList<CurveChannel> GetChannels(CurveClipCurveGroup group)
        {
            return GetChannels(group, true);
        }

        private IReadOnlyList<CurveChannel> GetChannels(CurveClipCurveGroup group, bool visibleOnly)
        {
            var channels = new List<CurveChannel>();

            if (group == CurveClipCurveGroup.Position)
            {
                AddBuiltIn(channels, group, "X", "posX", CurvePalette.Red);
                AddBuiltIn(channels, group, "Y", "posY", CurvePalette.Green);
                AddBuiltIn(channels, group, "Z", "posZ", CurvePalette.Blue);
            }
            else if (group == CurveClipCurveGroup.Rotation)
            {
                AddBuiltIn(channels, group, "X", "rotX", CurvePalette.Red);
                AddBuiltIn(channels, group, "Y", "rotY", CurvePalette.Green);
                AddBuiltIn(channels, group, "Z", "rotZ", CurvePalette.Blue);
            }
            else if (group == CurveClipCurveGroup.Scale)
            {
                AddBuiltIn(channels, group, "X", "scaleX", CurvePalette.Red);
                AddBuiltIn(channels, group, "Y", "scaleY", CurvePalette.Green);
                AddBuiltIn(channels, group, "Z", "scaleZ", CurvePalette.Blue);
            }
            else
            {
                SerializedProperty customCurves = serializedObject.FindProperty("customCurves");
                for (int i = 0; i < customCurves.arraySize; i++)
                {
                    SerializedProperty element = customCurves.GetArrayElementAtIndex(i);
                    string label = element.FindPropertyRelative("name").stringValue;
                    if (string.IsNullOrEmpty(label))
                        label = "Custom " + (i + 1);

                    channels.Add(new CurveChannel(
                        group,
                        label,
                        "customCurves.Array.data[" + i + "].curve",
                        "customCurves.Array.data[" + i + "].name",
                        CurvePalette.Custom(i),
                        i));
                }
            }

            if (!visibleOnly)
                return channels;

            channels.RemoveAll(channel => !IsVisible(channel));
            return channels;
        }

        private IReadOnlyList<CurveChannel> GetCombinedChannels(bool visibleOnly)
        {
            var channels = new List<CurveChannel>();
            AddCombinedChannels(channels, CurveClipCurveGroup.Position);
            AddCombinedChannels(channels, CurveClipCurveGroup.Rotation);
            AddCombinedChannels(channels, CurveClipCurveGroup.Scale);
            AddCombinedChannels(channels, CurveClipCurveGroup.Custom);

            if (visibleOnly)
                channels.RemoveAll(channel => !IsVisible(channel));

            return channels;
        }

        private void AddCombinedChannels(List<CurveChannel> combined, CurveClipCurveGroup group)
        {
            IReadOnlyList<CurveChannel> channels = GetChannels(group, false);
            for (int i = 0; i < channels.Count; i++)
                combined.Add(RelabelForCombinedGraph(channels[i]));
        }

        private CurveChannel RelabelForCombinedGraph(CurveChannel channel)
        {
            string label = channel.Label;
            if (channel.Group == CurveClipCurveGroup.Position)
                label = channel.Label.ToLowerInvariant() + " Pos";
            else if (channel.Group == CurveClipCurveGroup.Rotation)
                label = channel.Label.ToLowerInvariant() + " Rot";
            else if (channel.Group == CurveClipCurveGroup.Scale)
                label = channel.Label.ToLowerInvariant() + " Scale";
            else if (channel.Group == CurveClipCurveGroup.Custom)
                label = channel.Label.StartsWith("Custom ", StringComparison.OrdinalIgnoreCase)
                    ? channel.Label
                    : channel.Label + " Custom";

            return new CurveChannel(
                channel.Group,
                label,
                channel.CurvePath,
                channel.NamePath,
                channel.Color,
                channel.CustomIndex);
        }

        private void AddBuiltIn(List<CurveChannel> channels, CurveClipCurveGroup group, string label, string path, Color color)
        {
            channels.Add(new CurveChannel(group, label, path, null, color, -1));
        }

        private bool IsVisible(CurveChannel channel)
        {
            SerializedProperty entry = FindCurveVisibility(channel.VisibilityKey);
            return entry != null && entry.FindPropertyRelative("visible").boolValue;
        }

        private SerializedProperty FindCurveVisibility(string key)
        {
            SerializedProperty visibility = serializedObject.FindProperty("editorCurveVisibility");
            if (visibility == null)
                return null;

            for (int i = 0; i < visibility.arraySize; i++)
            {
                SerializedProperty entry = visibility.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("key").stringValue == key)
                    return entry;
            }

            return null;
        }

        private void SetCurveVisibility(string key, bool visible)
        {
            SerializedProperty entry = FindCurveVisibility(key);
            if (entry == null)
            {
                SerializedProperty visibility = serializedObject.FindProperty("editorCurveVisibility");
                int index = visibility.arraySize;
                visibility.InsertArrayElementAtIndex(index);
                entry = visibility.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("key").stringValue = key;
            }

            entry.FindPropertyRelative("visible").boolValue = visible;
        }

        private void RemoveCurveVisibility(string key)
        {
            SerializedProperty visibility = serializedObject.FindProperty("editorCurveVisibility");
            if (visibility == null)
                return;

            for (int i = visibility.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entry = visibility.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("key").stringValue == key)
                    visibility.DeleteArrayElementAtIndex(i);
            }
        }

        private void ApplyVisibilityChange()
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        private static string CustomCurveVisibilityKey(int index)
        {
            return CurveClipCurveGroup.Custom + ":customCurves.Array.data[" + index + "].curve";
        }

        private float GetDuration()
        {
            SerializedProperty durationProperty = serializedObject.FindProperty("duration");
            return Mathf.Max(0.0001f, durationProperty.floatValue);
        }
    }

    internal sealed class CurveClipGraphElement : VisualElement
    {
        private const float LeftGutter = 0f;
        private const float BottomGutter = 0f;
        private const float TopPadding = 0f;
        private const float RightPadding = 0f;
        private const float KeyHitRadius = 7f;
        private const float SelectedKeyHitRadius = 12f;
        private const float MarqueeDragThreshold = 4f;
        private const float KeyDrawRadius = 4f;
        private const float TangentHandleMinTimeDelta = 0.0001f;
        private const float SelectionHandleSize = 8f;
        private const float ActiveTimeMin = 0f;
        private const float ActiveTimeMax = 1f;
        private const float SingleKeyOverlayWidth = 72f;
        private const float SingleKeyOverlayHeight = 36f;
        private const float WheelZoomStep = 1.08f;
        private const double WheelZoomDuration = 0.055;

        private readonly SerializedObject serializedObject;
        private readonly SerializedProperty durationProperty;
        private readonly Func<IReadOnlyList<CurveChannel>> getChannels;
        private readonly Func<IReadOnlyList<CurveChannel>> getAllChannels;
        private readonly Func<CurveChannel, bool> isChannelVisible;
        private readonly Action<CurveChannel, bool> setChannelVisible;
        private readonly Action repaintAllGraphs;
        private readonly Rect defaultView;
        private readonly HashSet<string> selection = new HashSet<string>();
        private readonly List<KeyDragState> keyDragStates = new List<KeyDragState>();
        private readonly List<KeyDragCurveState> keyDragCurveStates = new List<KeyDragCurveState>();
        private readonly Label highValueLabel;
        private readonly Label lowValueLabel;
        private readonly VisualElement singleKeyOverlay;
        private readonly FloatField singleKeyTimeField;
        private readonly FloatField singleKeyValueField;
        private readonly VisualElement titleChip;
        private readonly VisualElement curveVisibilityChip;
        private readonly Button fitChip;

        private Rect view;
        private Rect zoomStartView;
        private Rect zoomTargetView;
        private Rect graphRect;
        private Vector2 lastMousePosition;
        private Vector2 pointerStart;
        private Vector2 pointerCurrent;
        private Vector2 panStartMin;
        private Vector2 panStartMax;
        private Vector2 selectionStartGraph;
        private Vector2 scaleAnchor;
        private SelectionScaleHandle activeScaleHandle;
        private InteractionMode interactionMode;
        private TangentDragState tangentDragState;
        private double zoomAnimationStartTime;
        private bool additiveSelection;
        private bool updatingSingleKeyOverlay;
        private bool needsInitialFrame = true;
        private bool zoomAnimationActive;
        private bool zoomAnimationScheduled;
        private static AnimationCurve copiedCurve;
        private static string copiedCurveLabel;

        public CurveClipGraphElement(
            string title,
            SerializedObject serializedObject,
            SerializedProperty durationProperty,
            Func<IReadOnlyList<CurveChannel>> getChannels,
            Func<IReadOnlyList<CurveChannel>> getAllChannels,
            Func<CurveChannel, bool> isChannelVisible,
            Action<CurveChannel, bool> setChannelVisible,
            Action repaintAllGraphs,
            Rect defaultView)
        {
            this.serializedObject = serializedObject;
            this.durationProperty = durationProperty;
            this.getChannels = getChannels;
            this.getAllChannels = getAllChannels;
            this.isChannelVisible = isChannelVisible;
            this.setChannelVisible = setChannelVisible;
            this.repaintAllGraphs = repaintAllGraphs;
            this.defaultView = defaultView;
            view = defaultView;

            focusable = true;
            pickingMode = PickingMode.Position;
            style.borderBottomColor = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.62f, 0.62f, 0.62f);
            style.borderTopColor = style.borderBottomColor;
            style.borderLeftColor = style.borderBottomColor;
            style.borderRightColor = style.borderBottomColor;
            style.borderBottomWidth = 1;
            style.borderTopWidth = 1;
            style.borderLeftWidth = 1;
            style.borderRightWidth = 1;
            style.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.76f, 0.76f, 0.76f);

            highValueLabel = CreateValueGuideLabel();
            lowValueLabel = CreateValueGuideLabel();
            Add(highValueLabel);
            Add(lowValueLabel);

            singleKeyTimeField = CreateSingleKeyField("t");
            singleKeyValueField = CreateSingleKeyField("v");
            singleKeyOverlay = CreateSingleKeyOverlay(singleKeyTimeField, singleKeyValueField);
            Add(singleKeyOverlay);

            titleChip = CreateTitleChip(title);
            curveVisibilityChip = CreateCurveVisibilityChip();
            fitChip = CreateFitChip();
            Add(titleChip);
            Add(curveVisibilityChip);
            Add(fitChip);
            UpdateCurveVisibilityChip();

            singleKeyTimeField.RegisterValueChangedCallback(evt => ApplySingleKeyOverlayEdit(true, evt.newValue));
            singleKeyValueField.RegisterValueChangedCallback(evt => ApplySingleKeyOverlayEdit(false, evt.newValue));

            generateVisualContent += GenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateGraphRect();
            UpdateCurveVisibilityChip();
            if (needsInitialFrame && HasUsableGraphRect())
            {
                needsInitialFrame = false;
                FrameAll();
                return;
            }

            UpdateGraphOverlays();
            MarkDirtyRepaint();
        }

        public void Refresh()
        {
            UpdateGraphRect();
            UpdateCurveVisibilityChip();
            UpdateGraphOverlays();
            MarkDirtyRepaint();
        }

        public void FrameToFit()
        {
            UpdateCurveVisibilityChip();
            FrameAll();
        }

        private void GenerateVisualContent(MeshGenerationContext ctx)
        {
            serializedObject.Update();
            UpdateGraphRect();

            Painter2D painter = ctx.painter2D;
            DrawBackground(painter);
            DrawGrid(painter);
            DrawValueExtentGuides(painter);
            DrawCurves(painter);
            DrawSelection(painter);
            DrawMarquee(painter);
        }

        private void UpdateGraphRect()
        {
            Rect rect = contentRect;
            graphRect = new Rect(
                rect.xMin + LeftGutter,
                rect.yMin + TopPadding,
                Mathf.Max(1f, rect.width - LeftGutter - RightPadding),
                Mathf.Max(1f, rect.height - BottomGutter - TopPadding));
        }

        private bool HasUsableGraphRect()
        {
            Rect rect = contentRect;
            return rect.width > LeftGutter + RightPadding + 1f
                && rect.height > TopPadding + BottomGutter + 1f;
        }

        private void DrawBackground(Painter2D painter)
        {
            Rect rect = contentRect;
            FillRect(painter, rect, EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.70f, 0.70f, 0.70f));
            FillRect(painter, graphRect, EditorGUIUtility.isProSkin ? new Color(0.105f, 0.105f, 0.105f) : new Color(0.82f, 0.82f, 0.82f));
            DrawInactiveTimeBackground(painter);
        }

        private void DrawInactiveTimeBackground(Painter2D painter)
        {
            Color inactive = EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.30f) : new Color(0f, 0f, 0f, 0.12f);
            Rect activeRect = GetActiveTimeScreenRect();

            if (activeRect.width <= 0f)
            {
                FillRect(painter, graphRect, inactive);
                return;
            }

            if (activeRect.xMin > graphRect.xMin)
                FillRect(painter, Rect.MinMaxRect(graphRect.xMin, graphRect.yMin, activeRect.xMin, graphRect.yMax), inactive);
            if (activeRect.xMax < graphRect.xMax)
                FillRect(painter, Rect.MinMaxRect(activeRect.xMax, graphRect.yMin, graphRect.xMax, graphRect.yMax), inactive);
        }

        private Label CreateValueGuideLabel()
        {
            var label = new Label();
            label.pickingMode = PickingMode.Ignore;
            label.style.position = Position.Absolute;
            label.style.width = 66;
            label.style.height = 16;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 10;
            label.style.color = Color.white;
            label.style.backgroundColor = Color.clear;
            label.style.display = DisplayStyle.None;
            return label;
        }

        private Label CreateTitleChip(string text)
        {
            var label = new Label(text);
            label.pickingMode = PickingMode.Ignore;
            label.style.position = Position.Absolute;
            label.style.left = 8;
            label.style.top = 6;
            label.style.width = Mathf.Clamp(text.Length * 6f + 14f, 42f, 110f);
            label.style.height = 17;
            label.style.paddingLeft = 6;
            label.style.paddingRight = 6;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Normal;
            label.style.color = new Color(1f, 1f, 1f, 0.78f);
            label.style.backgroundColor = new Color(0f, 0f, 0f, 0.68f);
            label.style.borderTopLeftRadius = 4;
            label.style.borderTopRightRadius = 4;
            label.style.borderBottomLeftRadius = 4;
            label.style.borderBottomRightRadius = 4;
            return label;
        }

        private VisualElement CreateCurveVisibilityChip()
        {
            var chip = new VisualElement();
            chip.style.position = Position.Absolute;
            chip.style.left = 8;
            chip.style.top = 28;
            chip.style.flexDirection = FlexDirection.Column;
            chip.style.flexWrap = Wrap.NoWrap;
            chip.style.alignItems = Align.FlexStart;
            chip.style.maxWidth = 240;
            chip.style.paddingLeft = 6;
            chip.style.paddingRight = 6;
            chip.style.paddingTop = 4;
            chip.style.paddingBottom = 3;
            chip.style.backgroundColor = new Color(0f, 0f, 0f, 0.60f);
            chip.style.borderTopLeftRadius = 4;
            chip.style.borderTopRightRadius = 4;
            chip.style.borderBottomLeftRadius = 4;
            chip.style.borderBottomRightRadius = 4;
            chip.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            chip.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());
            chip.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            return chip;
        }

        private void UpdateCurveVisibilityChip()
        {
            curveVisibilityChip.Clear();

            IReadOnlyList<CurveChannel> channels = getAllChannels();
            if (channels.Count == 0)
            {
                curveVisibilityChip.style.display = DisplayStyle.None;
                return;
            }

            curveVisibilityChip.style.display = DisplayStyle.Flex;
            for (int i = 0; i < channels.Count; i++)
                curveVisibilityChip.Add(CreateCurveVisibilityEntry(channels[i]));
        }

        private VisualElement CreateCurveVisibilityEntry(CurveChannel channel)
        {
            bool visible = isChannelVisible(channel);

            var entry = new VisualElement();
            entry.style.flexDirection = FlexDirection.Row;
            entry.style.alignItems = Align.Center;
            entry.style.height = 14;
            entry.style.marginBottom = 1;
            entry.tooltip = "Right-click to copy or paste this curve";
            entry.AddManipulator(new ContextualMenuManipulator(evt => BuildCurveChipContextMenu(evt, channel)));

            var swatch = new VisualElement();
            swatch.tooltip = visible ? "Hide " + channel.Label : "Show " + channel.Label;
            swatch.style.width = 9;
            swatch.style.height = 9;
            swatch.style.marginRight = 4;
            swatch.style.backgroundColor = WithAlpha(channel.Color, visible ? 1f : 0.26f);
            swatch.style.borderTopLeftRadius = 2;
            swatch.style.borderTopRightRadius = 2;
            swatch.style.borderBottomLeftRadius = 2;
            swatch.style.borderBottomRightRadius = 2;
            swatch.style.borderTopColor = new Color(1f, 1f, 1f, visible ? 0.34f : 0.12f);
            swatch.style.borderRightColor = swatch.style.borderTopColor;
            swatch.style.borderBottomColor = swatch.style.borderTopColor;
            swatch.style.borderLeftColor = swatch.style.borderTopColor;
            swatch.style.borderTopWidth = 1;
            swatch.style.borderRightWidth = 1;
            swatch.style.borderBottomWidth = 1;
            swatch.style.borderLeftWidth = 1;
            swatch.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                setChannelVisible(channel, !visible);
                evt.StopPropagation();
            });
            entry.Add(swatch);

            var label = new Label(channel.Label);
            label.pickingMode = PickingMode.Ignore;
            label.style.fontSize = 9;
            label.style.height = 13;
            label.style.maxWidth = 72;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.color = new Color(1f, 1f, 1f, visible ? 0.74f : 0.34f);
            entry.Add(label);

            return entry;
        }

        private void BuildCurveChipContextMenu(ContextualMenuPopulateEvent evt, CurveChannel channel)
        {
            evt.StopPropagation();
            evt.menu.AppendAction(
                "Copy Curve",
                _ => CopyCurve(channel),
                _ => GetCurveProperty(channel) != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            string pasteLabel = string.IsNullOrEmpty(copiedCurveLabel)
                ? "Paste Curve"
                : "Paste Curve from " + copiedCurveLabel;
            evt.menu.AppendAction(
                pasteLabel,
                _ => PasteCurve(channel),
                _ => copiedCurve != null && GetCurveProperty(channel) != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        private SerializedProperty GetCurveProperty(CurveChannel channel)
        {
            serializedObject.Update();
            SerializedProperty property = serializedObject.FindProperty(channel.CurvePath);
            return property != null && property.propertyType == SerializedPropertyType.AnimationCurve
                ? property
                : null;
        }

        private void CopyCurve(CurveChannel channel)
        {
            SerializedProperty property = GetCurveProperty(channel);
            if (property == null)
                return;

            copiedCurve = CloneCurve(property.animationCurveValue);
            copiedCurveLabel = channel.Label;
        }

        private void PasteCurve(CurveChannel channel)
        {
            if (copiedCurve == null)
                return;

            SerializedProperty property = GetCurveProperty(channel);
            if (property == null)
                return;

            Undo.RecordObject(serializedObject.targetObject, "Paste Curve");
            property.animationCurveValue = CloneCurve(copiedCurve);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
            selection.Clear();
            UpdateGraphOverlays();
            MarkDirtyRepaint();
            repaintAllGraphs?.Invoke();
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
                return new AnimationCurve();

            var clone = new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
            return clone;
        }

        private Button CreateFitChip()
        {
            var button = new Button(FrameAll) { text = "Fit" };
            button.tooltip = "Frame visible curves";
            button.style.position = Position.Absolute;
            button.style.right = 12;
            button.style.top = 6;
            button.style.width = 38;
            button.style.height = 20;
            button.style.paddingLeft = 0;
            button.style.paddingRight = 0;
            button.style.fontSize = 10;
            button.style.color = new Color(1f, 1f, 1f, 0.78f);
            button.style.backgroundColor = new Color(0f, 0f, 0f, 0.68f);
            button.style.borderTopLeftRadius = 4;
            button.style.borderTopRightRadius = 4;
            button.style.borderBottomLeftRadius = 4;
            button.style.borderBottomRightRadius = 4;
            button.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            button.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());
            button.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            return button;
        }

        private VisualElement CreateSingleKeyOverlay(FloatField timeField, FloatField valueField)
        {
            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.width = SingleKeyOverlayWidth;
            overlay.style.height = SingleKeyOverlayHeight;
            overlay.style.paddingLeft = 4;
            overlay.style.paddingRight = 4;
            overlay.style.paddingTop = 3;
            overlay.style.paddingBottom = 3;
            overlay.style.borderBottomLeftRadius = 3;
            overlay.style.borderBottomRightRadius = 3;
            overlay.style.borderTopLeftRadius = 3;
            overlay.style.borderTopRightRadius = 3;
            overlay.style.backgroundColor = new Color(0.04f, 0.04f, 0.04f, 0.84f);
            overlay.style.display = DisplayStyle.None;

            overlay.Add(timeField);
            overlay.Add(valueField);
            overlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<WheelEvent>(evt => evt.StopPropagation());
            overlay.RegisterCallback<KeyDownEvent>(evt => evt.StopPropagation());
            return overlay;
        }

        private FloatField CreateSingleKeyField(string label)
        {
            var field = new FloatField(label);
            Color textColor = new Color(1f, 1f, 1f, 0.72f);
            field.style.height = 15;
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            field.style.marginTop = 0;
            field.style.marginBottom = 1;
            field.style.fontSize = 9;
            field.style.color = textColor;
            field.labelElement.style.minWidth = 12;
            field.labelElement.style.width = 12;
            field.labelElement.style.fontSize = 9;
            field.labelElement.style.color = textColor;
            return field;
        }

        private void DrawGrid(Painter2D painter)
        {
            Color major = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.11f) : new Color(0f, 0f, 0f, 0.16f);
            Color minor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.055f) : new Color(0f, 0f, 0f, 0.075f);

            DrawVerticalGrid(painter, minor, major);
            DrawHorizontalGrid(painter, minor, major);
            DrawReferenceLines(painter);
        }

        private void DrawReferenceLines(Painter2D painter)
        {
            Color color = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.24f) : new Color(0f, 0f, 0f, 0.28f);

            DrawVerticalReferenceLine(painter, ActiveTimeMin, color);
            DrawVerticalReferenceLine(painter, ActiveTimeMax, color);

            if (view.yMin < 0f && view.yMax > 0f)
            {
                float y = ValueToY(0f);
                DrawDashedLine(painter, new Vector2(graphRect.xMin, y), new Vector2(graphRect.xMax, y), color, 1.5f);
            }
        }

        private void DrawVerticalReferenceLine(Painter2D painter, float time, Color color)
        {
            if (time < view.xMin || time > view.xMax)
                return;

            float x = TimeToX(time);
            DrawDashedLine(painter, new Vector2(x, graphRect.yMin), new Vector2(x, graphRect.yMax), color, 1.5f);
        }

        private void DrawVerticalGrid(Painter2D painter, Color minor, Color major)
        {
            float step = NiceStep(view.width / 8f);
            float visibleMin = Mathf.Max(view.xMin, ActiveTimeMin);
            float visibleMax = Mathf.Min(view.xMax, ActiveTimeMax);
            if (visibleMax <= visibleMin)
                return;

            float first = Mathf.Floor(visibleMin / step) * step;
            for (float time = first; time <= visibleMax; time += step)
            {
                if (time < visibleMin || time > visibleMax)
                    continue;

                float x = TimeToX(time);
                if (x < graphRect.xMin || x > graphRect.xMax)
                    continue;

                bool isMajor = Mathf.Abs(Mathf.Repeat(time / step, 2f)) < 0.01f;
                DrawLine(painter, new Vector2(x, graphRect.yMin), new Vector2(x, graphRect.yMax), isMajor ? major : minor, isMajor ? 1.1f : 1f);
            }
        }

        private void DrawHorizontalGrid(Painter2D painter, Color minor, Color major)
        {
            Rect activeRect = GetActiveTimeScreenRect();
            if (activeRect.width <= 0f)
                return;

            float step = NiceStep(view.height / 6f);
            float first = Mathf.Floor(view.yMin / step) * step;
            for (float value = first; value <= view.yMax; value += step)
            {
                float y = ValueToY(value);
                if (y < graphRect.yMin || y > graphRect.yMax)
                    continue;

                bool isMajor = Mathf.Abs(Mathf.Repeat(value / step, 2f)) < 0.01f;
                DrawLine(painter, new Vector2(activeRect.xMin, y), new Vector2(activeRect.xMax, y), isMajor ? major : minor, isMajor ? 1.1f : 1f);
            }
        }

        private Rect GetActiveTimeScreenRect()
        {
            float minTime = Mathf.Max(view.xMin, ActiveTimeMin);
            float maxTime = Mathf.Min(view.xMax, ActiveTimeMax);
            if (maxTime <= minTime)
                return default;

            float xMin = Mathf.Clamp(TimeToX(minTime), graphRect.xMin, graphRect.xMax);
            float xMax = Mathf.Clamp(TimeToX(maxTime), graphRect.xMin, graphRect.xMax);
            if (xMax <= xMin)
                return default;

            return Rect.MinMaxRect(xMin, graphRect.yMin, xMax, graphRect.yMax);
        }

        private void DrawValueExtentGuides(Painter2D painter)
        {
            Rect activeRect = GetActiveTimeScreenRect();
            if (activeRect.width <= 0f || !TryGetActiveKeyValueExtents(out float minValue, out float maxValue))
                return;

            Color color = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.16f);
            DrawValueGuide(painter, activeRect, maxValue, color);

            if (Mathf.Abs(maxValue - minValue) > 0.0001f)
                DrawValueGuide(painter, activeRect, minValue, color);
        }

        private void DrawValueGuide(Painter2D painter, Rect activeRect, float value, Color color)
        {
            if (value < view.yMin || value > view.yMax)
                return;

            float y = ValueToY(value);
            DrawDashedLine(painter, new Vector2(activeRect.xMin, y), new Vector2(activeRect.xMax, y), color, 1f);
        }

        private void UpdateValueGuideLabels()
        {
            HideValueGuideLabels();

            Rect activeRect = GetActiveTimeScreenRect();
            if (activeRect.width <= 0f || !TryGetActiveKeyValueExtents(out float minValue, out float maxValue))
                return;

            PositionValueGuideLabel(highValueLabel, activeRect.center.x, ValueToY(maxValue), maxValue);

            if (Mathf.Abs(maxValue - minValue) > 0.0001f)
                PositionValueGuideLabel(lowValueLabel, activeRect.center.x, ValueToY(minValue), minValue);
        }

        private void UpdateGraphOverlays()
        {
            UpdateGraphRect();
            if (!HasUsableGraphRect())
            {
                HideValueGuideLabels();
                singleKeyOverlay.style.display = DisplayStyle.None;
                BringGraphChipsToFront();
                return;
            }

            UpdateValueGuideLabels();
            UpdateSingleKeyOverlay();
            BringGraphChipsToFront();
        }

        private void BringGraphChipsToFront()
        {
            titleChip.BringToFront();
            curveVisibilityChip.BringToFront();
            fitChip.BringToFront();
        }

        private void UpdateSingleKeyOverlay()
        {
            if (!TryGetSingleSelectedKey(out _, out _, out Keyframe key))
            {
                singleKeyOverlay.style.display = DisplayStyle.None;
                return;
            }

            if (!TryGetKeyScreenPoint(key, out Vector2 point))
            {
                singleKeyOverlay.style.display = DisplayStyle.None;
                return;
            }

            updatingSingleKeyOverlay = true;
            singleKeyTimeField.SetValueWithoutNotify(key.time);
            singleKeyValueField.SetValueWithoutNotify(key.value);
            updatingSingleKeyOverlay = false;

            singleKeyOverlay.style.left = Mathf.Clamp(point.x + 16f, graphRect.xMin, Mathf.Max(graphRect.xMin, graphRect.xMax - SingleKeyOverlayWidth));
            singleKeyOverlay.style.top = Mathf.Clamp(point.y - SingleKeyOverlayHeight - 14f, graphRect.yMin, Mathf.Max(graphRect.yMin, graphRect.yMax - SingleKeyOverlayHeight));
            singleKeyOverlay.style.display = DisplayStyle.Flex;
        }

        private void ApplySingleKeyOverlayEdit(bool editTime, float value)
        {
            if (updatingSingleKeyOverlay)
                return;

            if (!TryGetSingleSelectedKey(out string path, out int index, out Keyframe key))
                return;

            SerializedProperty property = serializedObject.FindProperty(path);
            if (property == null)
                return;

            AnimationCurve curve = property.animationCurveValue;
            if (index < 0 || index >= curve.length)
                return;

            if (editTime)
                key.time = Mathf.Clamp(value, ActiveTimeMin, ActiveTimeMax);
            else
                key.value = value;

            int newIndex = curve.MoveKey(index, key);
            if (newIndex < 0)
                newIndex = index;
            property.animationCurveValue = curve;

            selection.Clear();
            selection.Add(KeyId(path, newIndex));
            ApplyCurveChange();
            UpdateGraphOverlays();
            MarkDirtyRepaint();
        }

        private bool TryGetSingleSelectedKey(out string path, out int index, out Keyframe key)
        {
            path = null;
            index = -1;
            key = default;

            if (selection.Count != 1)
                return false;

            foreach (string id in selection)
            {
                if (!TryParseKeyId(id, out path, out index))
                    return false;

                SerializedProperty property = serializedObject.FindProperty(path);
                if (property == null)
                    return false;

                AnimationCurve curve = property.animationCurveValue;
                if (index < 0 || index >= curve.length)
                    return false;

                key = curve.keys[index];
                return true;
            }

            return false;
        }

        private void PositionValueGuideLabel(Label label, float x, float y, float value)
        {
            if (y < graphRect.yMin || y > graphRect.yMax)
                return;

            const float width = 66f;
            const float height = 16f;
            label.text = FormatValue(value);
            label.style.left = Mathf.Clamp(x - width * 0.5f, graphRect.xMin, Mathf.Max(graphRect.xMin, graphRect.xMax - width));
            label.style.top = Mathf.Clamp(y - height * 0.5f, graphRect.yMin, Mathf.Max(graphRect.yMin, graphRect.yMax - height));
            label.style.display = DisplayStyle.Flex;
        }

        private void HideValueGuideLabels()
        {
            highValueLabel.style.display = DisplayStyle.None;
            lowValueLabel.style.display = DisplayStyle.None;
        }

        private bool TryGetActiveKeyValueExtents(out float minValue, out float maxValue)
        {
            minValue = float.PositiveInfinity;
            maxValue = float.NegativeInfinity;

            IReadOnlyList<CurveChannel> channels = getChannels();
            for (int c = 0; c < channels.Count; c++)
            {
                SerializedProperty property = serializedObject.FindProperty(channels[c].CurvePath);
                if (property == null)
                    continue;

                Keyframe[] keys = property.animationCurveValue.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].time < ActiveTimeMin || keys[i].time > ActiveTimeMax)
                        continue;

                    minValue = Mathf.Min(minValue, keys[i].value);
                    maxValue = Mathf.Max(maxValue, keys[i].value);
                }
            }

            return !float.IsInfinity(minValue);
        }

        private static string FormatValue(float value)
        {
            return value.ToString("0.###");
        }

        private void DrawCurves(Painter2D painter)
        {
            IReadOnlyList<CurveChannel> channels = getAllChannels();
            for (int i = 0; i < channels.Count; i++)
            {
                CurveChannel channel = channels[i];
                if (isChannelVisible(channel))
                    continue;

                SerializedProperty property = serializedObject.FindProperty(channel.CurvePath);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                DrawCurve(painter, curve, HiddenCurveColor());
            }

            for (int i = 0; i < channels.Count; i++)
            {
                CurveChannel channel = channels[i];
                if (!isChannelVisible(channel))
                    continue;

                SerializedProperty property = serializedObject.FindProperty(channel.CurvePath);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                DrawCurve(painter, curve, channel.Color);
                DrawKeys(painter, channel, curve);
            }
        }

        private static Color HiddenCurveColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.82f, 0.82f, 0.82f, 0.22f)
                : new Color(0.45f, 0.45f, 0.45f, 0.28f);
        }

        private void DrawCurve(Painter2D painter, AnimationCurve curve, Color color)
        {
            if (curve == null || curve.length == 0)
                return;

            painter.strokeColor = color;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            bool isDashed = false;

            int steps = Mathf.Clamp(Mathf.CeilToInt(graphRect.width / .5f), 24, 1000);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float time = Mathf.Lerp(view.xMin, view.xMax, t);
                float eval = curve.Evaluate(time);
                Vector2 point = GraphToScreen(time, eval);
                if (point.y >= graphRect.yMax || point.y <= graphRect.yMin)
                {
                    if (!isDashed && i > 0)
                    {
                        painter.Stroke();
                        painter.BeginPath();
                    }
                    painter.dashPattern = new float[] { 1, 4 };
                    isDashed = true;
                }
                else
                {
                    if (isDashed && i > 0)
                    {
                        painter.Stroke();
                        painter.BeginPath();
                    }
                    painter.dashPattern = new float[] { 1 };
                    isDashed = false;
                }
                
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }

            painter.Stroke();
        }

        private void DrawKeys(Painter2D painter, CurveChannel channel, AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!TryGetKeyScreenPoint(keys[i], out Vector2 point))
                    continue;

                bool selected = selection.Contains(KeyId(channel.CurvePath, i));
                FillDiamond(painter, point, selected ? KeyDrawRadius + 1.5f : KeyDrawRadius, selected ? Color.white : channel.Color);

                if (selected)
                    DrawTangentHandles(painter, keys, i, channel.Color);
            }
        }

        private void DrawTangentHandles(Painter2D painter, Keyframe[] keys, int index, Color color)
        {
            Keyframe key = keys[index];
            if (!TryGetKeyScreenPoint(key, out Vector2 keyPoint))
                return;

            Color handleColor = new Color(color.r, color.g, color.b, 0.65f);

            if (CanShowTangentHandle(keys, index, false))
            {
                Vector2 inHandle = TangentHandlePosition(keys, index, false);
                DrawLine(painter, keyPoint, inHandle, handleColor, 1f);
                FillRect(painter, RectFromCenter(inHandle, 5f), handleColor);
            }

            if (CanShowTangentHandle(keys, index, true))
            {
                Vector2 outHandle = TangentHandlePosition(keys, index, true);
                DrawLine(painter, keyPoint, outHandle, handleColor, 1f);
                FillRect(painter, RectFromCenter(outHandle, 5f), handleColor);
            }
        }

        private void DrawSelection(Painter2D painter)
        {
            Rect selectionBounds = GetSelectionScreenBounds();
            if (selectionBounds.width <= 0f || selectionBounds.height <= 0f)
                return;

            Color border = new Color(1f, 1f, 1f, 0.45f);
            StrokeRect(painter, selectionBounds, border, 1f);

            if (selection.Count > 1)
            {
                Vector2 center = selectionBounds.center;
                FillRect(painter, RectFromCenter(selectionBounds.min, SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(new Vector2(selectionBounds.xMax, selectionBounds.yMin), SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(new Vector2(selectionBounds.xMin, selectionBounds.yMax), SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(selectionBounds.max, SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(new Vector2(selectionBounds.xMin, center.y), SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(new Vector2(selectionBounds.xMax, center.y), SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(new Vector2(center.x, selectionBounds.yMin), SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(new Vector2(center.x, selectionBounds.yMax), SelectionHandleSize), border);
            }
        }

        private void DrawMarquee(Painter2D painter)
        {
            if (interactionMode != InteractionMode.Marquee)
                return;

            Rect rect = MakeScreenRect(pointerStart, pointerCurrent);
            FillRect(painter, rect, new Color(0.35f, 0.55f, 1f, 0.12f));
            StrokeRect(painter, rect, new Color(0.55f, 0.72f, 1f, 0.65f), 1f);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            Focus();
            UpdateGraphRect();
            lastMousePosition = evt.localPosition;

            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                StopZoomAnimation();
                interactionMode = InteractionMode.Pan;
                pointerStart = evt.localPosition;
                panStartMin = new Vector2(view.xMin, view.yMin);
                panStartMax = new Vector2(view.xMax, view.yMax);
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            if (evt.button != 0 || !graphRect.Contains(evt.localPosition))
                return;

            if (evt.clickCount == 2)
            {
                AddKeyAt(evt.localPosition);
                evt.StopPropagation();
                return;
            }

            KeyHit keyHit = HitKey(evt.localPosition);
            if (keyHit.IsValid)
            {
                string id = KeyId(keyHit.Path, keyHit.Index);
                if (!selection.Contains(id))
                {
                    if (!evt.shiftKey && !evt.actionKey)
                        selection.Clear();
                    selection.Add(id);
                }
                else if (evt.actionKey)
                {
                    selection.Remove(id);
                    UpdateGraphOverlays();
                    MarkDirtyRepaint();
                    return;
                }

                BeginKeyDrag(evt.localPosition);
                interactionMode = InteractionMode.DragKeys;
                this.CapturePointer(evt.pointerId);
                UpdateGraphOverlays();
                evt.StopPropagation();
                return;
            }

            TangentDragState tangent = HitTangent(evt.localPosition);
            if (tangent.IsValid)
            {
                tangentDragState = tangent;
                interactionMode = InteractionMode.Tangent;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            SelectionScaleHandle scaleHandle = HitSelectionScaleHandle(evt.localPosition);
            if (scaleHandle != SelectionScaleHandle.None)
            {
                activeScaleHandle = scaleHandle;
                BeginScale(evt.localPosition, scaleHandle);
                interactionMode = InteractionMode.ScaleSelection;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            additiveSelection = evt.shiftKey || evt.actionKey;
            pointerStart = evt.localPosition;
            pointerCurrent = evt.localPosition;
            interactionMode = InteractionMode.PendingMarquee;
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            lastMousePosition = evt.localPosition;

            if (interactionMode == InteractionMode.None)
                return;

            pointerCurrent = evt.localPosition;

            if (interactionMode == InteractionMode.PendingMarquee)
            {
                Vector2 mouse = evt.localPosition;
                if ((mouse - pointerStart).sqrMagnitude < MarqueeDragThreshold * MarqueeDragThreshold)
                {
                    evt.StopPropagation();
                    return;
                }

                interactionMode = InteractionMode.Marquee;
                if (!additiveSelection)
                    selection.Clear();
                UpdateMarquee(mouse);
            }
            else if (interactionMode == InteractionMode.Pan)
                UpdatePan(evt.localPosition);
            else if (interactionMode == InteractionMode.DragKeys)
                UpdateKeyDrag(evt.localPosition, evt.shiftKey);
            else if (interactionMode == InteractionMode.Tangent)
                UpdateTangentDrag(evt.localPosition);
            else if (interactionMode == InteractionMode.Marquee)
                UpdateMarquee(evt.localPosition);
            else if (interactionMode == InteractionMode.ScaleSelection)
                UpdateScale(evt.localPosition);

            UpdateGraphOverlays();
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (interactionMode == InteractionMode.None)
                return;

            InteractionMode completedMode = interactionMode;
            interactionMode = InteractionMode.None;
            keyDragStates.Clear();
            keyDragCurveStates.Clear();
            tangentDragState = default;
            activeScaleHandle = SelectionScaleHandle.None;
            if (this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);

            if (completedMode == InteractionMode.PendingMarquee && !additiveSelection)
                selection.Clear();

            UpdateGraphOverlays();
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnWheel(WheelEvent evt)
        {
            UpdateGraphRect();
            if (!graphRect.Contains(evt.localMousePosition))
                return;

            Rect baseView = zoomAnimationActive ? zoomTargetView : view;
            Vector2 graphPoint = ScreenToGraph(evt.localMousePosition, baseView);
            bool horizontalWheel = Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y);
            float wheelDelta = horizontalWheel ? evt.delta.x : evt.delta.y;
            float zoom = Mathf.Pow(WheelZoomStep, wheelDelta);
            bool verticalOnly = evt.ctrlKey || evt.actionKey;
            bool horizontalOnly = (evt.shiftKey || evt.altKey || horizontalWheel) && !verticalOnly;
            float xZoom = verticalOnly ? 1f : zoom;
            float yZoom = horizontalOnly ? 1f : zoom;
            Rect targetView = MakeViewRect(
                graphPoint.x + (baseView.xMin - graphPoint.x) * xZoom,
                graphPoint.x + (baseView.xMax - graphPoint.x) * xZoom,
                graphPoint.y + (baseView.yMin - graphPoint.y) * yZoom,
                graphPoint.y + (baseView.yMax - graphPoint.y) * yZoom);

            StartZoomAnimation(targetView);
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedKeys();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.A)
            {
                FrameAll();
                evt.StopPropagation();
            }
        }

        private void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("Add Key", _ => AddKeyAt(lastMousePosition), _ => graphRect.Contains(lastMousePosition) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Delete Selected", _ => DeleteSelectedKeys(), _ => selection.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Frame All", _ => FrameAll());
            evt.menu.AppendAction("Reset View", _ =>
            {
                StopZoomAnimation();
                view = defaultView;
                view.width = Mathf.Max(ActiveTimeMax - ActiveTimeMin, defaultView.width);
                UpdateGraphOverlays();
                MarkDirtyRepaint();
            });
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Tangents/Flat", _ => SetSelectedTangents(TangentPreset.Flat), _ => selection.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Tangents/Linear", _ => SetSelectedTangents(TangentPreset.Linear), _ => selection.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Tangents/Smooth", _ => SetSelectedTangents(TangentPreset.Smooth), _ => selection.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Tangents/Constant", _ => SetSelectedTangents(TangentPreset.Constant), _ => selection.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        private void UpdatePan(Vector2 mouse)
        {
            Vector2 deltaPixels = mouse - pointerStart;
            float deltaTime = -deltaPixels.x / graphRect.width * view.width;
            float deltaValue = deltaPixels.y / graphRect.height * view.height;
            SetView(
                panStartMin.x + deltaTime,
                panStartMax.x + deltaTime,
                panStartMin.y + deltaValue,
                panStartMax.y + deltaValue);
        }

        private void BeginKeyDrag(Vector2 mouse)
        {
            pointerStart = mouse;
            keyDragStates.Clear();
            keyDragCurveStates.Clear();

            foreach (string id in selection)
            {
                if (!TryParseKeyId(id, out string path, out int index))
                    continue;

                SerializedProperty property = serializedObject.FindProperty(path);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                if (index < 0 || index >= curve.length)
                    continue;

                if (!HasKeyDragCurveState(path))
                    keyDragCurveStates.Add(new KeyDragCurveState(path, curve.keys));

                keyDragStates.Add(new KeyDragState(path, index, curve.keys[index]));
            }
        }

        private bool HasKeyDragCurveState(string path)
        {
            for (int i = 0; i < keyDragCurveStates.Count; i++)
            {
                if (keyDragCurveStates[i].Path == path)
                    return true;
            }

            return false;
        }

        private void UpdateKeyDrag(Vector2 mouse, bool constrainAxis)
        {
            Vector2 startGraph = ScreenToGraph(pointerStart);
            Vector2 currentGraph = ScreenToGraph(mouse);
            Vector2 delta = currentGraph - startGraph;
            if (constrainAxis)
                delta = ConstrainDeltaToNearestAxis(delta, mouse - pointerStart);

            ApplyKeyTransform(state => new Vector2(
                Mathf.Clamp(state.OriginalKey.time + delta.x, ActiveTimeMin, ActiveTimeMax),
                state.OriginalKey.value + delta.y));
        }

        private static Vector2 ConstrainDeltaToNearestAxis(Vector2 graphDelta, Vector2 screenDelta)
        {
            if (Mathf.Abs(screenDelta.x) >= Mathf.Abs(screenDelta.y))
                graphDelta.y = 0f;
            else
                graphDelta.x = 0f;

            return graphDelta;
        }

        private void UpdateTangentDrag(Vector2 mouse)
        {
            if (!tangentDragState.IsValid)
                return;

            SerializedProperty property = serializedObject.FindProperty(tangentDragState.Path);
            if (property == null)
                return;

            AnimationCurve curve = property.animationCurveValue;
            if (tangentDragState.Index < 0 || tangentDragState.Index >= curve.length)
                return;

            Keyframe[] keys = curve.keys;
            if (!CanShowTangentHandle(keys, tangentDragState.Index, tangentDragState.OutHandle))
                return;

            Keyframe key = keys[tangentDragState.Index];
            Vector2 graph = ScreenToGraph(mouse);
            float segmentTime = TangentSegmentTime(keys, tangentDragState.Index, tangentDragState.OutHandle);
            float handleDistance = tangentDragState.OutHandle
                ? graph.x - key.time
                : key.time - graph.x;
            handleDistance = Mathf.Max(handleDistance, TangentHandleMinTimeDelta);

            float deltaTime = tangentDragState.OutHandle ? handleDistance : -handleDistance;
            if (Mathf.Abs(deltaTime) < 0.0001f)
                return;

            float tangent = (graph.y - key.value) / deltaTime;
            if (tangentDragState.OutHandle)
            {
                key.outTangent = tangent;
                key.outWeight = handleDistance / segmentTime;
                key.weightedMode = AddWeightedMode(key.weightedMode, WeightedMode.Out);
            }
            else
            {
                key.inTangent = tangent;
                key.inWeight = handleDistance / segmentTime;
                key.weightedMode = AddWeightedMode(key.weightedMode, WeightedMode.In);
            }

            curve.MoveKey(tangentDragState.Index, key);
            property.animationCurveValue = curve;
            ApplyCurveChange();
        }

        private void UpdateMarquee(Vector2 mouse)
        {
            Rect marquee = MakeScreenRect(pointerStart, mouse);
            if (!additiveSelection)
                selection.Clear();

            IReadOnlyList<CurveChannel> channels = getChannels();
            for (int c = 0; c < channels.Count; c++)
            {
                SerializedProperty property = serializedObject.FindProperty(channels[c].CurvePath);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                Keyframe[] keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    Vector2 point = GraphToScreen(keys[i].time, keys[i].value);
                    if (marquee.Contains(point))
                        selection.Add(KeyId(channels[c].CurvePath, i));
                }
            }
        }

        private void BeginScale(Vector2 mouse, SelectionScaleHandle handle)
        {
            pointerStart = mouse;
            selectionStartGraph = ScreenToGraph(mouse);
            BeginKeyDrag(mouse);

            Rect bounds = GetSelectionGraphBounds();
            bool anchorRight = handle == SelectionScaleHandle.TopLeft || handle == SelectionScaleHandle.BottomLeft || handle == SelectionScaleHandle.Left;
            bool anchorTop = handle == SelectionScaleHandle.BottomLeft || handle == SelectionScaleHandle.BottomRight || handle == SelectionScaleHandle.Bottom;
            scaleAnchor = new Vector2(anchorRight ? bounds.xMax : bounds.xMin, anchorTop ? bounds.yMax : bounds.yMin);
        }

        private void UpdateScale(Vector2 mouse)
        {
            Vector2 current = ScreenToGraph(mouse);
            float startX = selectionStartGraph.x - scaleAnchor.x;
            float startY = selectionStartGraph.y - scaleAnchor.y;
            float scaleX = ScalesTime(activeScaleHandle) && Mathf.Abs(startX) > 0.0001f ? (current.x - scaleAnchor.x) / startX : 1f;
            float scaleY = ScalesValue(activeScaleHandle) && Mathf.Abs(startY) > 0.0001f ? (current.y - scaleAnchor.y) / startY : 1f;
            ApplyKeyTransform(state => new Vector2(
                Mathf.Clamp(scaleAnchor.x + (state.OriginalKey.time - scaleAnchor.x) * scaleX, ActiveTimeMin, ActiveTimeMax),
                scaleAnchor.y + (state.OriginalKey.value - scaleAnchor.y) * scaleY));
        }

        private void ApplyKeyTransform(Func<KeyDragState, Vector2> transform)
        {
            var byPath = new Dictionary<string, List<KeyDragState>>();
            for (int i = 0; i < keyDragStates.Count; i++)
            {
                KeyDragState state = keyDragStates[i];
                if (!byPath.TryGetValue(state.Path, out List<KeyDragState> states))
                {
                    states = new List<KeyDragState>();
                    byPath[state.Path] = states;
                }

                states.Add(state);
            }

            selection.Clear();
            foreach (KeyDragCurveState curveState in keyDragCurveStates)
            {
                if (!byPath.TryGetValue(curveState.Path, out List<KeyDragState> states))
                    continue;

                SerializedProperty property = serializedObject.FindProperty(curveState.Path);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                List<TransformedKey> transformed = new List<TransformedKey>(curveState.OriginalKeys.Length);
                for (int i = 0; i < curveState.OriginalKeys.Length; i++)
                    transformed.Add(new TransformedKey(curveState.OriginalKeys[i], i, false));

                for (int i = 0; i < states.Count; i++)
                {
                    KeyDragState state = states[i];
                    if (state.Index < 0 || state.Index >= transformed.Count)
                        continue;

                    Vector2 next = transform(state);
                    Keyframe key = state.OriginalKey;
                    key.time = next.x;
                    key.value = next.y;
                    transformed[state.Index] = new TransformedKey(key, state.Index, true);
                }

                transformed.Sort((a, b) => a.Key.time.CompareTo(b.Key.time));

                Keyframe[] nextKeys = new Keyframe[transformed.Count];
                for (int i = 0; i < transformed.Count; i++)
                {
                    nextKeys[i] = transformed[i].Key;
                    if (transformed[i].Selected)
                        selection.Add(KeyId(curveState.Path, i));
                }

                PreserveTangentHandles(curveState.OriginalKeys, transformed, nextKeys);
                curve.keys = nextKeys;
                property.animationCurveValue = curve;
            }

            ApplyCurveChange();
        }

        private void PreserveTangentHandles(Keyframe[] originalKeys, List<TransformedKey> transformed, Keyframe[] nextKeys)
        {
            for (int i = 0; i < nextKeys.Length; i++)
            {
                int originalIndex = transformed[i].OriginalIndex;
                if (originalIndex < 0 || originalIndex >= originalKeys.Length)
                    continue;

                Keyframe key = nextKeys[i];
                Vector2 originalPoint = KeyPoint(originalKeys[originalIndex]);
                Vector2 keyDelta = KeyPoint(key) - originalPoint;

                if (CanShowTangentHandle(originalKeys, originalIndex, false))
                    PreserveTangentHandle(ref key, nextKeys, i, false, TangentHandleGraphPosition(originalKeys, originalIndex, false) + keyDelta);

                if (CanShowTangentHandle(originalKeys, originalIndex, true))
                    PreserveTangentHandle(ref key, nextKeys, i, true, TangentHandleGraphPosition(originalKeys, originalIndex, true) + keyDelta);

                nextKeys[i] = key;
            }
        }

        private void PreserveTangentHandle(ref Keyframe key, Keyframe[] keys, int index, bool outHandle, Vector2 handle)
        {
            if (outHandle)
            {
                if (index >= keys.Length - 1 || float.IsInfinity(key.outTangent))
                    return;
            }
            else if (index <= 0 || float.IsInfinity(key.inTangent))
                return;

            Vector2 currentHandle = TangentHandleGraphPosition(keys, index, outHandle);
            if ((currentHandle - handle).sqrMagnitude <= 0.0000000001f)
                return;

            float segmentTime = TangentSegmentTime(keys, index, outHandle);
            float handleDistance = outHandle ? handle.x - key.time : key.time - handle.x;
            handleDistance = Mathf.Max(handleDistance, TangentHandleMinTimeDelta);

            float deltaTime = outHandle ? handleDistance : -handleDistance;
            float tangent = (handle.y - key.value) / deltaTime;
            if (outHandle)
            {
                key.outTangent = tangent;
                key.outWeight = handleDistance / segmentTime;
                key.weightedMode = AddWeightedMode(key.weightedMode, WeightedMode.Out);
            }
            else
            {
                key.inTangent = tangent;
                key.inWeight = handleDistance / segmentTime;
                key.weightedMode = AddWeightedMode(key.weightedMode, WeightedMode.In);
            }
        }

        private KeyHit HitKey(Vector2 mouse)
        {
            KeyHit selectedHit = HitKey(mouse, true, SelectedKeyHitRadius);
            if (selectedHit.IsValid)
                return selectedHit;

            return HitKey(mouse, false, KeyHitRadius);
        }

        private KeyHit HitKey(Vector2 mouse, bool selectedOnly, float radius)
        {
            IReadOnlyList<CurveChannel> channels = getChannels();
            KeyHit best = default;
            float bestDistance = float.MaxValue;

            for (int c = 0; c < channels.Count; c++)
            {
                SerializedProperty property = serializedObject.FindProperty(channels[c].CurvePath);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                Keyframe[] keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    string id = KeyId(channels[c].CurvePath, i);
                    if (selectedOnly && !selection.Contains(id))
                        continue;

                    if (!TryGetKeyScreenPoint(keys[i], out Vector2 point))
                        continue;

                    float distance = Vector2.Distance(point, mouse);
                    if (distance <= radius && distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new KeyHit(channels[c].CurvePath, i);
                    }
                }
            }

            return best;
        }

        private TangentDragState HitTangent(Vector2 mouse)
        {
            foreach (string id in selection)
            {
                if (!TryParseKeyId(id, out string path, out int index))
                    continue;

                SerializedProperty property = serializedObject.FindProperty(path);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                if (index < 0 || index >= curve.length)
                    continue;

                Keyframe[] keys = curve.keys;
                if (CanShowTangentHandle(keys, index, false) && Vector2.Distance(TangentHandlePosition(keys, index, false), mouse) <= KeyHitRadius)
                    return new TangentDragState(path, index, false);
                if (CanShowTangentHandle(keys, index, true) && Vector2.Distance(TangentHandlePosition(keys, index, true), mouse) <= KeyHitRadius)
                    return new TangentDragState(path, index, true);
            }

            return default;
        }

        private SelectionScaleHandle HitSelectionScaleHandle(Vector2 mouse)
        {
            if (selection.Count <= 1)
                return SelectionScaleHandle.None;

            Rect bounds = GetSelectionScreenBounds();
            if (bounds.width <= 0f || bounds.height <= 0f)
                return SelectionScaleHandle.None;

            if (RectFromCenter(bounds.min, SelectionHandleSize + 3f).Contains(mouse))
                return SelectionScaleHandle.TopLeft;
            if (RectFromCenter(new Vector2(bounds.xMax, bounds.yMin), SelectionHandleSize + 3f).Contains(mouse))
                return SelectionScaleHandle.TopRight;
            if (RectFromCenter(new Vector2(bounds.xMin, bounds.yMax), SelectionHandleSize + 3f).Contains(mouse))
                return SelectionScaleHandle.BottomLeft;
            if (RectFromCenter(bounds.max, SelectionHandleSize + 3f).Contains(mouse))
                return SelectionScaleHandle.BottomRight;

            Vector2 center = bounds.center;
            if (RectFromCenter(new Vector2(bounds.xMin, center.y), SelectionHandleSize + 3f).Contains(mouse))
                return SelectionScaleHandle.Left;
            if (RectFromCenter(new Vector2(bounds.xMax, center.y), SelectionHandleSize + 3f).Contains(mouse))
                return SelectionScaleHandle.Right;
            if (RectFromCenter(new Vector2(center.x, bounds.yMin), SelectionHandleSize + 3f).Contains(mouse))
                return SelectionScaleHandle.Top;
            if (RectFromCenter(new Vector2(center.x, bounds.yMax), SelectionHandleSize + 3f).Contains(mouse))
                return SelectionScaleHandle.Bottom;

            return SelectionScaleHandle.None;
        }

        private static bool ScalesTime(SelectionScaleHandle handle)
        {
            return handle == SelectionScaleHandle.TopLeft
                || handle == SelectionScaleHandle.TopRight
                || handle == SelectionScaleHandle.BottomLeft
                || handle == SelectionScaleHandle.BottomRight
                || handle == SelectionScaleHandle.Left
                || handle == SelectionScaleHandle.Right;
        }

        private static bool ScalesValue(SelectionScaleHandle handle)
        {
            return handle == SelectionScaleHandle.TopLeft
                || handle == SelectionScaleHandle.TopRight
                || handle == SelectionScaleHandle.BottomLeft
                || handle == SelectionScaleHandle.BottomRight
                || handle == SelectionScaleHandle.Top
                || handle == SelectionScaleHandle.Bottom;
        }

        private void AddKeyAt(Vector2 screenPosition)
        {
            IReadOnlyList<CurveChannel> channels = getChannels();
            if (channels.Count == 0)
                return;

            Vector2 graph = ScreenToGraph(screenPosition);
            float time = Mathf.Clamp(graph.x, ActiveTimeMin, ActiveTimeMax);
            string targetPath = FindClosestCurvePath(channels, time, graph.y);
            if (string.IsNullOrEmpty(targetPath))
                return;

            SerializedProperty property = serializedObject.FindProperty(targetPath);
            if (property == null)
                return;

            AnimationCurve curve = property.animationCurveValue;
            int index = curve.AddKey(time, curve.Evaluate(time));
            property.animationCurveValue = curve;
            ApplyCurveChange();

            selection.Clear();
            selection.Add(KeyId(targetPath, Mathf.Max(0, index)));
            BeginKeyDrag(screenPosition);
            UpdateGraphOverlays();
            MarkDirtyRepaint();
        }

        private string FindClosestCurvePath(IReadOnlyList<CurveChannel> channels, float time, float value)
        {
            string closestPath = null;
            float closestDistance = float.PositiveInfinity;

            for (int i = 0; i < channels.Count; i++)
            {
                SerializedProperty property = serializedObject.FindProperty(channels[i].CurvePath);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                float distance = Mathf.Abs(curve.Evaluate(time) - value);
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closestPath = channels[i].CurvePath;
            }

            return closestPath;
        }

        private void DeleteSelectedKeys()
        {
            if (selection.Count == 0)
                return;

            var byPath = new Dictionary<string, List<int>>();
            foreach (string id in selection)
            {
                if (!TryParseKeyId(id, out string path, out int index))
                    continue;

                if (!byPath.TryGetValue(path, out List<int> indices))
                {
                    indices = new List<int>();
                    byPath[path] = indices;
                }

                indices.Add(index);
            }

            foreach (KeyValuePair<string, List<int>> pair in byPath)
            {
                SerializedProperty property = serializedObject.FindProperty(pair.Key);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                pair.Value.Sort((a, b) => b.CompareTo(a));
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    int index = pair.Value[i];
                    if (index >= 0 && index < curve.length)
                        curve.RemoveKey(index);
                }

                property.animationCurveValue = curve;
            }

            selection.Clear();
            ApplyCurveChange();
            UpdateGraphOverlays();
            MarkDirtyRepaint();
        }

        private void SetSelectedTangents(TangentPreset preset)
        {
            if (selection.Count == 0)
                return;

            var byPath = new Dictionary<string, List<int>>();
            foreach (string id in selection)
            {
                if (!TryParseKeyId(id, out string path, out int index))
                    continue;

                if (!byPath.TryGetValue(path, out List<int> indices))
                {
                    indices = new List<int>();
                    byPath[path] = indices;
                }

                indices.Add(index);
            }

            foreach (KeyValuePair<string, List<int>> pair in byPath)
            {
                SerializedProperty property = serializedObject.FindProperty(pair.Key);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    int index = pair.Value[i];
                    if (index < 0 || index >= curve.length)
                        continue;

                    Keyframe key = curve.keys[index];
                    if (preset == TangentPreset.Flat)
                    {
                        key.inTangent = 0f;
                        key.outTangent = 0f;
                    }
                    else if (preset == TangentPreset.Constant)
                    {
                        key.inTangent = float.PositiveInfinity;
                        key.outTangent = float.PositiveInfinity;
                    }
                    else if (preset == TangentPreset.Linear)
                    {
                        key.inTangent = CalculateLinearTangent(curve, index, false);
                        key.outTangent = CalculateLinearTangent(curve, index, true);
                    }
                    else if (preset == TangentPreset.Smooth)
                    {
                        float tangent = CalculateSmoothTangent(curve, index);
                        key.inTangent = tangent;
                        key.outTangent = tangent;
                    }

                    key.weightedMode = WeightedMode.None;
                    curve.MoveKey(index, key);
                }

                property.animationCurveValue = curve;
            }

            ApplyCurveChange();
            MarkDirtyRepaint();
        }

        private float CalculateLinearTangent(AnimationCurve curve, int index, bool outTangent)
        {
            Keyframe[] keys = curve.keys;
            int otherIndex = outTangent ? index + 1 : index - 1;
            if (otherIndex < 0 || otherIndex >= keys.Length)
                return 0f;

            float dt = keys[otherIndex].time - keys[index].time;
            if (Mathf.Abs(dt) < 0.0001f)
                return 0f;

            return (keys[otherIndex].value - keys[index].value) / dt;
        }

        private float CalculateSmoothTangent(AnimationCurve curve, int index)
        {
            Keyframe[] keys = curve.keys;
            if (keys.Length <= 1)
                return 0f;

            if (index == 0)
                return CalculateLinearTangent(curve, index, true);
            if (index == keys.Length - 1)
                return CalculateLinearTangent(curve, index, false);

            float dt = keys[index + 1].time - keys[index - 1].time;
            if (Mathf.Abs(dt) < 0.0001f)
                return 0f;

            return (keys[index + 1].value - keys[index - 1].value) / dt;
        }

        private void FrameAll()
        {
            StopZoomAnimation();
            IReadOnlyList<CurveChannel> channels = getChannels();
            float minTime = ActiveTimeMin;
            float maxTime = ActiveTimeMax;
            float minValue = float.PositiveInfinity;
            float maxValue = float.NegativeInfinity;
            bool hasKey = false;

            for (int c = 0; c < channels.Count; c++)
            {
                SerializedProperty property = serializedObject.FindProperty(channels[c].CurvePath);
                if (property == null)
                    continue;

                Keyframe[] keys = property.animationCurveValue.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    hasKey = true;
                    minTime = Mathf.Min(minTime, keys[i].time);
                    maxTime = Mathf.Max(maxTime, keys[i].time);
                    minValue = Mathf.Min(minValue, keys[i].value);
                    maxValue = Mathf.Max(maxValue, keys[i].value);
                }
            }

            if (!hasKey)
            {
                view = defaultView;
                view.width = Mathf.Max(ActiveTimeMax - ActiveTimeMin, defaultView.width);
                UpdateGraphOverlays();
                MarkDirtyRepaint();
                return;
            }

            if (Mathf.Abs(maxValue - minValue) < 0.001f)
            {
                minValue -= 1f;
                maxValue += 1f;
            }

            float timePadding = Mathf.Max(0.05f, (maxTime - minTime) * 0.08f);
            float valuePadding = Mathf.Max(0.05f, (maxValue - minValue) * 0.12f);
            SetView(
                Mathf.Min(ActiveTimeMin, minTime - timePadding),
                Mathf.Max(ActiveTimeMax, maxTime + timePadding),
                minValue - valuePadding,
                maxValue + valuePadding);
            UpdateGraphOverlays();
            MarkDirtyRepaint();
        }

        private Rect GetSelectionScreenBounds()
        {
            Rect graphBounds = GetSelectionGraphBounds();
            if (graphBounds.width <= 0f && graphBounds.height <= 0f)
                return default;

            Vector2 min = GraphToScreen(graphBounds.xMin, graphBounds.yMin);
            Vector2 max = GraphToScreen(graphBounds.xMax, graphBounds.yMax);
            return MakeScreenRect(min, max);
        }

        private Rect GetSelectionGraphBounds()
        {
            float minTime = float.PositiveInfinity;
            float maxTime = float.NegativeInfinity;
            float minValue = float.PositiveInfinity;
            float maxValue = float.NegativeInfinity;

            foreach (string id in selection)
            {
                if (!TryParseKeyId(id, out string path, out int index))
                    continue;

                SerializedProperty property = serializedObject.FindProperty(path);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                if (index < 0 || index >= curve.length)
                    continue;

                Keyframe key = curve.keys[index];
                minTime = Mathf.Min(minTime, key.time);
                maxTime = Mathf.Max(maxTime, key.time);
                minValue = Mathf.Min(minValue, key.value);
                maxValue = Mathf.Max(maxValue, key.value);
            }

            if (float.IsInfinity(minTime))
                return default;

            if (Mathf.Abs(maxTime - minTime) < 0.0001f)
            {
                minTime -= 0.01f;
                maxTime += 0.01f;
            }

            if (Mathf.Abs(maxValue - minValue) < 0.0001f)
            {
                minValue -= 0.01f;
                maxValue += 0.01f;
            }

            return Rect.MinMaxRect(minTime, minValue, maxTime, maxValue);
        }

        private bool CanShowTangentHandle(Keyframe[] keys, int index, bool outHandle)
        {
            if (index < 0 || index >= keys.Length)
                return false;
            if (outHandle)
                return index < keys.Length - 1 && !float.IsInfinity(keys[index].outTangent);

            return index > 0 && !float.IsInfinity(keys[index].inTangent);
        }

        private Vector2 TangentHandlePosition(Keyframe[] keys, int index, bool outHandle)
        {
            Vector2 graph = TangentHandleGraphPosition(keys, index, outHandle);
            return GraphToScreen(graph.x, graph.y);
        }

        private Vector2 TangentHandleGraphPosition(Keyframe[] keys, int index, bool outHandle)
        {
            Keyframe key = keys[index];
            float tangent = outHandle ? key.outTangent : key.inTangent;
            float dt = TangentHandleTimeDelta(keys, index, outHandle);

            return new Vector2(key.time + dt, key.value + tangent * dt);
        }

        private static Vector2 KeyPoint(Keyframe key)
        {
            return new Vector2(key.time, key.value);
        }

        private float TangentHandleTimeDelta(Keyframe[] keys, int index, bool outHandle)
        {
            float segmentTime = TangentSegmentTime(keys, index, outHandle);
            float weight = TangentHandleWeight(keys[index], outHandle);

            if (outHandle)
                return segmentTime * weight;

            return -segmentTime * weight;
        }

        private float TangentSegmentTime(Keyframe[] keys, int index, bool outHandle)
        {
            if (outHandle)
                return Mathf.Max(keys[index + 1].time - keys[index].time, TangentHandleMinTimeDelta);

            return Mathf.Max(keys[index].time - keys[index - 1].time, TangentHandleMinTimeDelta);
        }

        private float TangentHandleWeight(Keyframe key, bool outHandle)
        {
            WeightedMode side = outHandle ? WeightedMode.Out : WeightedMode.In;
            bool weighted = (key.weightedMode & side) == side;
            float weight = outHandle ? key.outWeight : key.inWeight;
            if (!weighted || weight <= 0f)
                return 1f / 3f;

            return weight;
        }

        private static WeightedMode AddWeightedMode(WeightedMode current, WeightedMode side)
        {
            return (WeightedMode)((int)current | (int)side);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private Vector2 GraphToScreen(float time, float value)
        {
            return new Vector2(TimeToX(time), ValueToY(value));
        }

        private bool TryGetKeyScreenPoint(Keyframe key, out Vector2 point)
        {
            point = GraphToScreen(key.time, key.value);
            if (point.x < graphRect.xMin || point.x > graphRect.xMax)
                return false;

            point.y = Mathf.Clamp(point.y, graphRect.yMin, graphRect.yMax);
            return true;
        }

        private Vector2 ScreenToGraph(Vector2 screen)
        {
            return ScreenToGraph(screen, view);
        }

        private Vector2 ScreenToGraph(Vector2 screen, Rect sourceView)
        {
            float normalizedTime = Mathf.InverseLerp(graphRect.xMin, graphRect.xMax, screen.x);
            float normalizedValue = Mathf.InverseLerp(graphRect.yMax, graphRect.yMin, screen.y);
            return new Vector2(
                Mathf.Lerp(sourceView.xMin, sourceView.xMax, normalizedTime),
                Mathf.Lerp(sourceView.yMin, sourceView.yMax, normalizedValue));
        }

        private float TimeToX(float time)
        {
            return Mathf.Lerp(graphRect.xMin, graphRect.xMax, Mathf.InverseLerp(view.xMin, view.xMax, time));
        }

        private float ValueToY(float value)
        {
            return Mathf.Lerp(graphRect.yMax, graphRect.yMin, Mathf.InverseLerp(view.yMin, view.yMax, value));
        }

        private void SetView(float xMin, float xMax, float yMin, float yMax)
        {
            view = MakeViewRect(xMin, xMax, yMin, yMax);
        }

        private static Rect MakeViewRect(float xMin, float xMax, float yMin, float yMax)
        {
            if (xMax - xMin < 0.01f)
                xMax = xMin + 0.01f;
            if (yMax - yMin < 0.01f)
                yMax = yMin + 0.01f;

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void StartZoomAnimation(Rect targetView)
        {
            zoomStartView = view;
            zoomTargetView = targetView;
            zoomAnimationStartTime = EditorApplication.timeSinceStartup;
            zoomAnimationActive = true;

            if (zoomAnimationScheduled)
                return;

            zoomAnimationScheduled = true;
            schedule.Execute(UpdateZoomAnimation).Every(16).Until(() => !zoomAnimationActive);
        }

        private void UpdateZoomAnimation()
        {
            if (!zoomAnimationActive)
            {
                zoomAnimationScheduled = false;
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - zoomAnimationStartTime;
            float t = Mathf.Clamp01((float)(elapsed / WheelZoomDuration));
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            view = LerpView(zoomStartView, zoomTargetView, eased);

            if (t >= 1f)
            {
                view = zoomTargetView;
                zoomAnimationActive = false;
                zoomAnimationScheduled = false;
            }

            UpdateGraphOverlays();
            MarkDirtyRepaint();
        }

        private void StopZoomAnimation()
        {
            if (!zoomAnimationActive)
                return;

            zoomAnimationActive = false;
            zoomAnimationScheduled = false;
        }

        private static Rect LerpView(Rect from, Rect to, float t)
        {
            return Rect.MinMaxRect(
                Mathf.Lerp(from.xMin, to.xMin, t),
                Mathf.Lerp(from.yMin, to.yMin, t),
                Mathf.Lerp(from.xMax, to.xMax, t),
                Mathf.Lerp(from.yMax, to.yMax, t));
        }

        private float GetDuration()
        {
            serializedObject.Update();
            return Mathf.Max(0.0001f, durationProperty.floatValue);
        }

        private void ApplyCurveChange()
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        private static float NiceStep(float roughStep)
        {
            roughStep = Mathf.Max(roughStep, 0.0001f);
            float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(roughStep)));
            float normalized = roughStep / power;

            if (normalized < 1.5f)
                return power;
            if (normalized < 3f)
                return power * 2f;
            if (normalized < 7f)
                return power * 5f;
            return power * 10f;
        }

        private static string KeyId(string path, int index)
        {
            return path + "#" + index;
        }

        private static bool TryParseKeyId(string id, out string path, out int index)
        {
            path = null;
            index = -1;

            int separator = id.LastIndexOf('#');
            if (separator < 0)
                return false;

            path = id.Substring(0, separator);
            return int.TryParse(id.Substring(separator + 1), out index);
        }

        private static Rect RectFromCenter(Vector2 center, float size)
        {
            return new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
        }

        private static Rect MakeScreenRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x),
                Mathf.Max(a.y, b.y));
        }

        private static void DrawLine(Painter2D painter, Vector2 from, Vector2 to, Color color, float width)
        {
            painter.dashPattern = new float[] { 1 };
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        private static void DrawDashedLine(Painter2D painter, Vector2 from, Vector2 to, Color color, float width)
        {
            painter.dashPattern = new float[] { 5, 4 };
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
            painter.dashPattern = new float[] { 1 };
        }

        private static void FillRect(Painter2D painter, Rect rect, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private static void StrokeRect(Painter2D painter, Rect rect, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Stroke();
        }

        private static void FillDiamond(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x, center.y - radius));
            painter.LineTo(new Vector2(center.x + radius, center.y));
            painter.LineTo(new Vector2(center.x, center.y + radius));
            painter.LineTo(new Vector2(center.x - radius, center.y));
            painter.ClosePath();
            painter.Fill();
        }

        private readonly struct KeyHit
        {
            public readonly string Path;
            public readonly int Index;
            public bool IsValid => !string.IsNullOrEmpty(Path) && Index >= 0;

            public KeyHit(string path, int index)
            {
                Path = path;
                Index = index;
            }
        }

        private readonly struct KeyDragState
        {
            public readonly string Path;
            public readonly int Index;
            public readonly Keyframe OriginalKey;

            public KeyDragState(string path, int index, Keyframe originalKey)
            {
                Path = path;
                Index = index;
                OriginalKey = originalKey;
            }
        }

        private readonly struct KeyDragCurveState
        {
            public readonly string Path;
            public readonly Keyframe[] OriginalKeys;

            public KeyDragCurveState(string path, Keyframe[] originalKeys)
            {
                Path = path;
                OriginalKeys = originalKeys;
            }
        }

        private readonly struct TransformedKey
        {
            public readonly Keyframe Key;
            public readonly int OriginalIndex;
            public readonly bool Selected;

            public TransformedKey(Keyframe key, int originalIndex, bool selected)
            {
                Key = key;
                OriginalIndex = originalIndex;
                Selected = selected;
            }
        }

        private readonly struct TangentDragState
        {
            public readonly string Path;
            public readonly int Index;
            public readonly bool OutHandle;
            public bool IsValid => !string.IsNullOrEmpty(Path) && Index >= 0;

            public TangentDragState(string path, int index, bool outHandle)
            {
                Path = path;
                Index = index;
                OutHandle = outHandle;
            }
        }

        private enum InteractionMode
        {
            None,
            Pan,
            DragKeys,
            Tangent,
            PendingMarquee,
            Marquee,
            ScaleSelection
        }

        private enum SelectionScaleHandle
        {
            None,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
            Left,
            Right,
            Top,
            Bottom
        }

        private enum TangentPreset
        {
            Flat,
            Linear,
            Smooth,
            Constant
        }
    }

    internal readonly struct CurveChannel
    {
        public readonly CurveClipCurveGroup Group;
        public readonly string Label;
        public readonly string CurvePath;
        public readonly string NamePath;
        public readonly Color Color;
        public readonly int CustomIndex;

        public string VisibilityKey => Group + ":" + CurvePath;

        public CurveChannel(
            CurveClipCurveGroup group,
            string label,
            string curvePath,
            string namePath,
            Color color,
            int customIndex)
        {
            Group = group;
            Label = label;
            CurvePath = curvePath;
            NamePath = namePath;
            Color = color;
            CustomIndex = customIndex;
        }
    }

    internal static class CurvePalette
    {
        public static readonly Color Red = new Color(0.94f, 0.28f, 0.30f);
        public static readonly Color Green = new Color(0.32f, 0.78f, 0.38f);
        public static readonly Color Blue = new Color(0.30f, 0.55f, 1f);

        private static readonly Color[] CustomColors =
        {
            new Color(1f, 0.72f, 0.28f),
            new Color(0.72f, 0.46f, 1f),
            new Color(0.25f, 0.86f, 0.82f),
            new Color(1f, 0.44f, 0.72f),
            new Color(0.86f, 0.88f, 0.30f),
            new Color(0.54f, 0.74f, 1f)
        };

        public static Color Custom(int index)
        {
            return CustomColors[Mathf.Abs(index) % CustomColors.Length];
        }
    }
}
