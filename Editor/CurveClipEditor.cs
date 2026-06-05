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
        private readonly Dictionary<string, bool> curveVisibility = new Dictionary<string, bool>();
        private VisualElement curveList;
        private CurveClipGraphElement positionGraph;
        private CurveClipGraphElement rotationGraph;
        private CurveClipGraphElement scaleGraph;
        private CurveClipGraphElement customGraph;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.style.paddingLeft = 2;
            root.style.paddingRight = 2;

            if (serializedObject.isEditingMultipleObjects)
            {
                root.Add(new HelpBox("Curve Clip graph editing supports one asset at a time.", HelpBoxMessageType.Info));
                InspectorElement.FillDefaultInspector(root, serializedObject, this);
                return root;
            }

            root.Add(BuildSettingsSection());
            root.Add(BuildCurveListSection());
            root.Add(BuildGraphSection());
            root.Bind(serializedObject);
            return root;
        }

        private VisualElement BuildSettingsSection()
        {
            var foldout = new Foldout { text = "Settings", value = true };
            foldout.style.marginBottom = 3;

            foldout.Add(CreateProperty("duration"));
            foldout.Add(CreateProperty("updateMode"));
            foldout.Add(CreateProperty("transformSpace"));
            foldout.Add(CreateProperty("valueMode"));

            var toggles = new VisualElement();
            toggles.style.flexDirection = FlexDirection.Row;
            toggles.style.flexWrap = Wrap.Wrap;
            toggles.style.marginLeft = 3;
            toggles.style.marginTop = 2;
            toggles.Add(CreateCompactToggle("applyPosition", "Position"));
            toggles.Add(CreateCompactToggle("applyRotation", "Rotation"));
            toggles.Add(CreateCompactToggle("applyScale", "Scale"));
            toggles.Add(CreateCompactToggle("sampleEndOnComplete", "Sample End"));
            foldout.Add(toggles);

            return foldout;
        }

        private PropertyField CreateProperty(string propertyName)
        {
            return new PropertyField(serializedObject.FindProperty(propertyName));
        }

        private Toggle CreateCompactToggle(string propertyName, string label)
        {
            var toggle = new Toggle(label);
            toggle.BindProperty(serializedObject.FindProperty(propertyName));
            toggle.style.marginRight = 12;
            toggle.style.minWidth = 86;
            return toggle;
        }

        private VisualElement BuildCurveListSection()
        {
            var foldout = new Foldout { text = "Curves", value = true };
            foldout.style.marginBottom = 3;

            curveList = new VisualElement();
            curveList.style.marginLeft = 2;
            foldout.Add(curveList);

            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginTop = 3;

            var showAll = new Button(() => SetAllVisible(true)) { text = "Show All" };
            var hideAll = new Button(() => SetAllVisible(false)) { text = "Hide All" };
            var addCustom = new Button(AddCustomCurve) { text = "+ Custom" };
            showAll.style.flexGrow = 1;
            hideAll.style.flexGrow = 1;
            addCustom.style.flexGrow = 1;
            toolbar.Add(showAll);
            toolbar.Add(hideAll);
            toolbar.Add(addCustom);
            foldout.Add(toolbar);

            RebuildCurveList();
            return foldout;
        }

        private VisualElement BuildGraphSection()
        {
            var root = new VisualElement();
            root.style.marginTop = 2;

            positionGraph = CreateGraph("Position", CurveClipCurveGroup.Position, new Rect(0f, -1f, 1f, 2f));
            rotationGraph = CreateGraph("Rotation", CurveClipCurveGroup.Rotation, new Rect(0f, -90f, 1f, 180f));
            scaleGraph = CreateGraph("Scale", CurveClipCurveGroup.Scale, new Rect(0f, 0f, 1f, 2f));
            customGraph = CreateGraph("Custom", CurveClipCurveGroup.Custom, new Rect(0f, 0f, 1f, 1f));

            root.Add(positionGraph);
            root.Add(rotationGraph);
            root.Add(scaleGraph);
            root.Add(customGraph);
            return root;
        }

        private CurveClipGraphElement CreateGraph(string title, CurveClipCurveGroup group, Rect defaultView)
        {
            var graph = new CurveClipGraphElement(
                title,
                serializedObject,
                serializedObject.FindProperty("duration"),
                () => GetChannels(group),
                defaultView);
            graph.style.height = group == CurveClipCurveGroup.Custom ? 190 : 220;
            graph.style.marginBottom = 6;
            return graph;
        }

        private void RebuildCurveList()
        {
            if (curveList == null)
                return;

            curveList.Clear();
            AddGroupRows(curveList, CurveClipCurveGroup.Position);
            AddGroupRows(curveList, CurveClipCurveGroup.Rotation);
            AddGroupRows(curveList, CurveClipCurveGroup.Scale);
            AddGroupRows(curveList, CurveClipCurveGroup.Custom);
        }

        private void AddGroupRows(VisualElement parent, CurveClipCurveGroup group)
        {
            IReadOnlyList<CurveChannel> channels = GetChannels(group, false);
            if (channels.Count == 0)
                return;

            var label = new Label(group.ToString());
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = group == CurveClipCurveGroup.Position ? 0 : 4;
            label.style.marginBottom = 1;
            parent.Add(label);

            for (int i = 0; i < channels.Count; i++)
                parent.Add(CreateCurveRow(channels[i]));
        }

        private VisualElement CreateCurveRow(CurveChannel channel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 22;
            row.style.marginBottom = 1;

            var visible = new Toggle();
            visible.value = IsVisible(channel);
            visible.tooltip = "Show in graph";
            visible.style.width = 18;
            visible.RegisterValueChangedCallback(evt =>
            {
                curveVisibility[channel.VisibilityKey] = evt.newValue;
                RepaintGraphs();
            });
            row.Add(visible);

            var swatch = new VisualElement();
            swatch.style.width = 10;
            swatch.style.height = 10;
            swatch.style.marginRight = 5;
            swatch.style.backgroundColor = channel.Color;
            swatch.style.borderBottomLeftRadius = 2;
            swatch.style.borderBottomRightRadius = 2;
            swatch.style.borderTopLeftRadius = 2;
            swatch.style.borderTopRightRadius = 2;
            row.Add(swatch);

            if (channel.Group == CurveClipCurveGroup.Custom)
            {
                SerializedProperty nameProperty = serializedObject.FindProperty(channel.NamePath);
                var nameField = new TextField();
                nameField.BindProperty(nameProperty);
                nameField.style.width = 82;
                nameField.style.marginRight = 4;
                row.Add(nameField);
            }
            else
            {
                var label = new Label(channel.Label);
                label.style.width = 64;
                label.style.marginRight = 4;
                row.Add(label);
            }

            SerializedProperty curveProperty = serializedObject.FindProperty(channel.CurvePath);
            var curveField = new CurveField();
            curveField.BindProperty(curveProperty);
            curveField.style.flexGrow = 1;
            curveField.RegisterValueChangedCallback(_ => RepaintGraphs());
            row.Add(curveField);

            if (channel.Group == CurveClipCurveGroup.Custom)
            {
                var remove = new Button(() => RemoveCustomCurve(channel.CustomIndex)) { text = "-" };
                remove.style.width = 22;
                remove.style.marginLeft = 3;
                row.Add(remove);
            }

            return row;
        }

        private void SetAllVisible(bool visible)
        {
            foreach (CurveClipCurveGroup group in Enum.GetValues(typeof(CurveClipCurveGroup)))
            {
                IReadOnlyList<CurveChannel> channels = GetChannels(group, false);
                for (int i = 0; i < channels.Count; i++)
                    curveVisibility[channels[i].VisibilityKey] = visible;
            }

            RebuildCurveList();
            RepaintGraphs();
        }

        private void AddCustomCurve()
        {
            serializedObject.Update();
            SerializedProperty customCurves = serializedObject.FindProperty("customCurves");
            int index = customCurves.arraySize;
            customCurves.InsertArrayElementAtIndex(index);

            SerializedProperty element = customCurves.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("name").stringValue = "Custom " + (index + 1);
            element.FindPropertyRelative("curve").animationCurveValue = AnimationCurve.Linear(0f, 0f, GetDuration(), 1f);
            serializedObject.ApplyModifiedProperties();

            RebuildCurveList();
            RepaintGraphs();
        }

        private void RemoveCustomCurve(int index)
        {
            serializedObject.Update();
            SerializedProperty customCurves = serializedObject.FindProperty("customCurves");
            if (index >= 0 && index < customCurves.arraySize)
            {
                customCurves.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
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

        private void AddBuiltIn(List<CurveChannel> channels, CurveClipCurveGroup group, string label, string path, Color color)
        {
            channels.Add(new CurveChannel(group, label, path, null, color, -1));
        }

        private bool IsVisible(CurveChannel channel)
        {
            if (!curveVisibility.TryGetValue(channel.VisibilityKey, out bool visible))
            {
                visible = true;
                curveVisibility[channel.VisibilityKey] = visible;
            }

            return visible;
        }

        private float GetDuration()
        {
            SerializedProperty durationProperty = serializedObject.FindProperty("duration");
            return Mathf.Max(0.0001f, durationProperty.floatValue);
        }
    }

    internal sealed class CurveClipGraphElement : VisualElement
    {
        private const float HeaderHeight = 24f;
        private const float LeftGutter = 40f;
        private const float BottomGutter = 20f;
        private const float TopPadding = 6f;
        private const float RightPadding = 8f;
        private const float KeyHitRadius = 7f;
        private const float KeyDrawRadius = 4f;
        private const float TangentHandleLength = 42f;
        private const float SelectionHandleSize = 8f;

        private readonly string title;
        private readonly SerializedObject serializedObject;
        private readonly SerializedProperty durationProperty;
        private readonly Func<IReadOnlyList<CurveChannel>> getChannels;
        private readonly Rect defaultView;
        private readonly HashSet<string> selection = new HashSet<string>();
        private readonly List<KeyDragState> keyDragStates = new List<KeyDragState>();

        private Rect view;
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
        private bool additiveSelection;

        public CurveClipGraphElement(
            string title,
            SerializedObject serializedObject,
            SerializedProperty durationProperty,
            Func<IReadOnlyList<CurveChannel>> getChannels,
            Rect defaultView)
        {
            this.title = title;
            this.serializedObject = serializedObject;
            this.durationProperty = durationProperty;
            this.getChannels = getChannels;
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

            var titleLabel = new Label(title);
            titleLabel.pickingMode = PickingMode.Ignore;
            titleLabel.style.position = Position.Absolute;
            titleLabel.style.left = 10;
            titleLabel.style.top = 4;
            titleLabel.style.height = 18;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            Add(titleLabel);

            var fitButton = new Button(FrameAll) { text = "Fit" };
            fitButton.tooltip = "Frame visible curves";
            fitButton.style.position = Position.Absolute;
            fitButton.style.right = 8;
            fitButton.style.top = 2;
            fitButton.style.width = 44;
            fitButton.style.height = 20;
            Add(fitButton);

            generateVisualContent += GenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
        }

        public void Refresh()
        {
            MarkDirtyRepaint();
        }

        private void GenerateVisualContent(MeshGenerationContext ctx)
        {
            serializedObject.Update();
            UpdateGraphRect();

            Painter2D painter = ctx.painter2D;
            DrawBackground(painter);
            DrawGrid(painter);
            DrawCurves(painter);
            DrawSelection(painter);
            DrawHeader(painter);
            DrawMarquee(painter);
        }

        private void UpdateGraphRect()
        {
            Rect rect = contentRect;
            graphRect = new Rect(
                rect.xMin + LeftGutter,
                rect.yMin + HeaderHeight + TopPadding,
                Mathf.Max(1f, rect.width - LeftGutter - RightPadding),
                Mathf.Max(1f, rect.height - HeaderHeight - BottomGutter - TopPadding));
        }

        private void DrawBackground(Painter2D painter)
        {
            Rect rect = contentRect;
            FillRect(painter, rect, EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.70f, 0.70f, 0.70f));
            FillRect(painter, graphRect, EditorGUIUtility.isProSkin ? new Color(0.105f, 0.105f, 0.105f) : new Color(0.82f, 0.82f, 0.82f));
        }

        private void DrawHeader(Painter2D painter)
        {
            Rect rect = contentRect;
            FillRect(painter, new Rect(rect.xMin, rect.yMin, rect.width, HeaderHeight), EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.67f, 0.67f, 0.67f));

            // Painter2D has no text API; use a compact title tab shape as the visual anchor.
            Color tab = EditorGUIUtility.isProSkin ? new Color(0.28f, 0.28f, 0.28f) : new Color(0.52f, 0.52f, 0.52f);
            FillRect(painter, new Rect(rect.xMin + 6f, rect.yMin + 6f, Mathf.Clamp(title.Length * 7f + 18f, 58f, 110f), 12f), tab);
        }

        private void DrawGrid(Painter2D painter)
        {
            Color major = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.11f) : new Color(0f, 0f, 0f, 0.16f);
            Color minor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.055f) : new Color(0f, 0f, 0f, 0.075f);

            DrawVerticalGrid(painter, minor, major);
            DrawHorizontalGrid(painter, minor, major);

            if (view.yMin < 0f && view.yMax > 0f)
            {
                float y = ValueToY(0f);
                DrawLine(painter, new Vector2(graphRect.xMin, y), new Vector2(graphRect.xMax, y), major, 1.5f);
            }

            float duration = GetDuration();
            float durationX = TimeToX(duration);
            if (durationX >= graphRect.xMin && durationX <= graphRect.xMax)
                DrawLine(painter, new Vector2(durationX, graphRect.yMin), new Vector2(durationX, graphRect.yMax), new Color(1f, 1f, 1f, 0.18f), 1.5f);
        }

        private void DrawVerticalGrid(Painter2D painter, Color minor, Color major)
        {
            float step = NiceStep(view.width / 8f);
            float first = Mathf.Floor(view.xMin / step) * step;
            for (float time = first; time <= view.xMax; time += step)
            {
                float x = TimeToX(time);
                if (x < graphRect.xMin || x > graphRect.xMax)
                    continue;

                bool isMajor = Mathf.Abs(Mathf.Repeat(time / step, 2f)) < 0.01f;
                DrawLine(painter, new Vector2(x, graphRect.yMin), new Vector2(x, graphRect.yMax), isMajor ? major : minor, isMajor ? 1.1f : 1f);
            }
        }

        private void DrawHorizontalGrid(Painter2D painter, Color minor, Color major)
        {
            float step = NiceStep(view.height / 6f);
            float first = Mathf.Floor(view.yMin / step) * step;
            for (float value = first; value <= view.yMax; value += step)
            {
                float y = ValueToY(value);
                if (y < graphRect.yMin || y > graphRect.yMax)
                    continue;

                bool isMajor = Mathf.Abs(Mathf.Repeat(value / step, 2f)) < 0.01f;
                DrawLine(painter, new Vector2(graphRect.xMin, y), new Vector2(graphRect.xMax, y), isMajor ? major : minor, isMajor ? 1.1f : 1f);
            }
        }

        private void DrawCurves(Painter2D painter)
        {
            IReadOnlyList<CurveChannel> channels = getChannels();
            for (int i = 0; i < channels.Count; i++)
            {
                CurveChannel channel = channels[i];
                SerializedProperty property = serializedObject.FindProperty(channel.CurvePath);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                DrawCurve(painter, curve, channel.Color);
                DrawKeys(painter, channel, curve);
            }
        }

        private void DrawCurve(Painter2D painter, AnimationCurve curve, Color color)
        {
            if (curve == null || curve.length == 0)
                return;

            painter.strokeColor = color;
            painter.lineWidth = 1.8f;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();

            int steps = Mathf.Clamp(Mathf.CeilToInt(graphRect.width / 4f), 24, 240);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float time = Mathf.Lerp(view.xMin, view.xMax, t);
                Vector2 point = GraphToScreen(time, curve.Evaluate(time));
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
                Vector2 point = GraphToScreen(keys[i].time, keys[i].value);
                if (!graphRect.Contains(point))
                    continue;

                bool selected = selection.Contains(KeyId(channel.CurvePath, i));
                FillDiamond(painter, point, selected ? KeyDrawRadius + 1.5f : KeyDrawRadius, selected ? Color.white : channel.Color);

                if (selected)
                    DrawTangentHandles(painter, channel.CurvePath, i, keys[i], channel.Color);
            }
        }

        private void DrawTangentHandles(Painter2D painter, string path, int index, Keyframe key, Color color)
        {
            Vector2 keyPoint = GraphToScreen(key.time, key.value);
            Color handleColor = new Color(color.r, color.g, color.b, 0.65f);

            if (!float.IsInfinity(key.inTangent))
            {
                Vector2 inHandle = TangentHandlePosition(key, false);
                DrawLine(painter, keyPoint, inHandle, handleColor, 1f);
                FillRect(painter, RectFromCenter(inHandle, 5f), handleColor);
            }

            if (!float.IsInfinity(key.outTangent))
            {
                Vector2 outHandle = TangentHandlePosition(key, true);
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
                FillRect(painter, RectFromCenter(selectionBounds.min, SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(new Vector2(selectionBounds.xMax, selectionBounds.yMin), SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(new Vector2(selectionBounds.xMin, selectionBounds.yMax), SelectionHandleSize), border);
                FillRect(painter, RectFromCenter(selectionBounds.max, SelectionHandleSize), border);
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
                    MarkDirtyRepaint();
                    return;
                }

                BeginKeyDrag(evt.localPosition);
                interactionMode = InteractionMode.DragKeys;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            additiveSelection = evt.shiftKey || evt.actionKey;
            if (!additiveSelection)
                selection.Clear();
            interactionMode = InteractionMode.Marquee;
            pointerStart = evt.localPosition;
            pointerCurrent = evt.localPosition;
            this.CapturePointer(evt.pointerId);
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            lastMousePosition = evt.localPosition;

            if (interactionMode == InteractionMode.None)
                return;

            pointerCurrent = evt.localPosition;

            if (interactionMode == InteractionMode.Pan)
                UpdatePan(evt.localPosition);
            else if (interactionMode == InteractionMode.DragKeys)
                UpdateKeyDrag(evt.localPosition);
            else if (interactionMode == InteractionMode.Tangent)
                UpdateTangentDrag(evt.localPosition);
            else if (interactionMode == InteractionMode.Marquee)
                UpdateMarquee(evt.localPosition);
            else if (interactionMode == InteractionMode.ScaleSelection)
                UpdateScale(evt.localPosition);

            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (interactionMode == InteractionMode.None)
                return;

            interactionMode = InteractionMode.None;
            keyDragStates.Clear();
            tangentDragState = default;
            activeScaleHandle = SelectionScaleHandle.None;
            if (this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);

            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnWheel(WheelEvent evt)
        {
            UpdateGraphRect();
            if (!graphRect.Contains(evt.localMousePosition))
                return;

            Vector2 graphPoint = ScreenToGraph(evt.localMousePosition);
            float zoom = Mathf.Pow(1.08f, evt.delta.y);
            float xMin = graphPoint.x + (view.xMin - graphPoint.x) * zoom;
            float xMax = graphPoint.x + (view.xMax - graphPoint.x) * zoom;
            float yMin = graphPoint.y + (view.yMin - graphPoint.y) * zoom;
            float yMax = graphPoint.y + (view.yMax - graphPoint.y) * zoom;

            SetView(xMin, xMax, yMin, yMax);
            MarkDirtyRepaint();
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
                view = defaultView;
                view.width = Mathf.Max(GetDuration(), defaultView.width);
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

                keyDragStates.Add(new KeyDragState(path, index, curve.keys[index]));
            }
        }

        private void UpdateKeyDrag(Vector2 mouse)
        {
            Vector2 startGraph = ScreenToGraph(pointerStart);
            Vector2 currentGraph = ScreenToGraph(mouse);
            Vector2 delta = currentGraph - startGraph;
            ApplyKeyTransform(state => new Vector2(
                Mathf.Clamp(state.OriginalKey.time + delta.x, 0f, GetDuration()),
                state.OriginalKey.value + delta.y));
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

            Keyframe key = curve.keys[tangentDragState.Index];
            Vector2 graph = ScreenToGraph(mouse);
            float deltaTime = graph.x - key.time;
            if (Mathf.Abs(deltaTime) < 0.0001f)
                return;

            float tangent = (graph.y - key.value) / deltaTime;
            if (tangentDragState.OutHandle)
                key.outTangent = tangent;
            else
                key.inTangent = tangent;

            key.weightedMode = WeightedMode.None;
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
            bool anchorRight = handle == SelectionScaleHandle.TopLeft || handle == SelectionScaleHandle.BottomLeft;
            bool anchorTop = handle == SelectionScaleHandle.BottomLeft || handle == SelectionScaleHandle.BottomRight;
            scaleAnchor = new Vector2(anchorRight ? bounds.xMax : bounds.xMin, anchorTop ? bounds.yMax : bounds.yMin);
        }

        private void UpdateScale(Vector2 mouse)
        {
            Vector2 current = ScreenToGraph(mouse);
            float startX = selectionStartGraph.x - scaleAnchor.x;
            float startY = selectionStartGraph.y - scaleAnchor.y;
            float scaleX = Mathf.Abs(startX) > 0.0001f ? (current.x - scaleAnchor.x) / startX : 1f;
            float scaleY = Mathf.Abs(startY) > 0.0001f ? (current.y - scaleAnchor.y) / startY : 1f;

            ApplyKeyTransform(state => new Vector2(
                Mathf.Clamp(scaleAnchor.x + (state.OriginalKey.time - scaleAnchor.x) * scaleX, 0f, GetDuration()),
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
            foreach (KeyValuePair<string, List<KeyDragState>> pair in byPath)
            {
                SerializedProperty property = serializedObject.FindProperty(pair.Key);
                if (property == null)
                    continue;

                AnimationCurve curve = property.animationCurveValue;
                Keyframe[] keys = curve.keys;
                List<Keyframe> transformed = new List<Keyframe>(keys);

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    KeyDragState state = pair.Value[i];
                    if (state.Index < 0 || state.Index >= transformed.Count)
                        continue;

                    Vector2 next = transform(state);
                    Keyframe key = state.OriginalKey;
                    key.time = next.x;
                    key.value = next.y;
                    transformed[state.Index] = key;
                }

                transformed.Sort((a, b) => a.time.CompareTo(b.time));
                curve.keys = transformed.ToArray();
                property.animationCurveValue = curve;

                for (int i = 0; i < transformed.Count; i++)
                {
                    for (int j = 0; j < pair.Value.Count; j++)
                    {
                        Vector2 next = transform(pair.Value[j]);
                        if (Mathf.Abs(transformed[i].time - next.x) < 0.0001f && Mathf.Abs(transformed[i].value - next.y) < 0.0001f)
                            selection.Add(KeyId(pair.Key, i));
                    }
                }
            }

            ApplyCurveChange();
        }

        private KeyHit HitKey(Vector2 mouse)
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
                    Vector2 point = GraphToScreen(keys[i].time, keys[i].value);
                    float distance = Vector2.Distance(point, mouse);
                    if (distance <= KeyHitRadius && distance < bestDistance)
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

                Keyframe key = curve.keys[index];
                if (!float.IsInfinity(key.inTangent) && Vector2.Distance(TangentHandlePosition(key, false), mouse) <= KeyHitRadius)
                    return new TangentDragState(path, index, false);
                if (!float.IsInfinity(key.outTangent) && Vector2.Distance(TangentHandlePosition(key, true), mouse) <= KeyHitRadius)
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

            return SelectionScaleHandle.None;
        }

        private void AddKeyAt(Vector2 screenPosition)
        {
            IReadOnlyList<CurveChannel> channels = getChannels();
            if (channels.Count == 0)
                return;

            string targetPath = null;
            foreach (string id in selection)
            {
                if (TryParseKeyId(id, out string path, out _))
                {
                    targetPath = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(targetPath))
                targetPath = channels[0].CurvePath;

            SerializedProperty property = serializedObject.FindProperty(targetPath);
            if (property == null)
                return;

            Vector2 graph = ScreenToGraph(screenPosition);
            float time = Mathf.Clamp(graph.x, 0f, GetDuration());
            AnimationCurve curve = property.animationCurveValue;
            int index = curve.AddKey(time, curve.Evaluate(time));
            property.animationCurveValue = curve;
            ApplyCurveChange();

            selection.Clear();
            selection.Add(KeyId(targetPath, Mathf.Max(0, index)));
            BeginKeyDrag(screenPosition);
            MarkDirtyRepaint();
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
            IReadOnlyList<CurveChannel> channels = getChannels();
            float duration = GetDuration();
            float minTime = 0f;
            float maxTime = duration;
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
                view.width = Mathf.Max(duration, defaultView.width);
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
                Mathf.Max(0f, minTime - timePadding),
                Mathf.Max(duration, maxTime + timePadding),
                minValue - valuePadding,
                maxValue + valuePadding);
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

        private Vector2 TangentHandlePosition(Keyframe key, bool outHandle)
        {
            float tangent = outHandle ? key.outTangent : key.inTangent;
            float direction = outHandle ? 1f : -1f;
            float dt = Mathf.Max(0.0001f, view.width * 0.08f) * direction;
            Vector2 keyPoint = GraphToScreen(key.time, key.value);
            Vector2 target = GraphToScreen(key.time + dt, key.value + tangent * dt);
            Vector2 delta = target - keyPoint;
            if (delta.sqrMagnitude < 0.0001f)
                delta = new Vector2(direction, 0f);

            return keyPoint + delta.normalized * TangentHandleLength;
        }

        private Vector2 GraphToScreen(float time, float value)
        {
            return new Vector2(TimeToX(time), ValueToY(value));
        }

        private Vector2 ScreenToGraph(Vector2 screen)
        {
            float normalizedTime = Mathf.InverseLerp(graphRect.xMin, graphRect.xMax, screen.x);
            float normalizedValue = Mathf.InverseLerp(graphRect.yMax, graphRect.yMin, screen.y);
            return new Vector2(
                Mathf.Lerp(view.xMin, view.xMax, normalizedTime),
                Mathf.Lerp(view.yMin, view.yMax, normalizedValue));
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
            if (xMax - xMin < 0.01f)
                xMax = xMin + 0.01f;
            if (yMax - yMin < 0.01f)
                yMax = yMin + 0.01f;

            view = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
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
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
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
            Marquee,
            ScaleSelection
        }

        private enum SelectionScaleHandle
        {
            None,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
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
