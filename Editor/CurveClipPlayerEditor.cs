using Less3.CurveClips;
using UnityEditor;
using UnityEngine;

namespace Less3.CurveClips.Editor
{
    [CustomEditor(typeof(CurveClipPlayer))]
    [CanEditMultipleObjects]
    public sealed class CurveClipPlayerEditor : UnityEditor.Editor
    {
        private SerializedProperty targetProperty;
        private SerializedProperty clipsProperty;
        private SerializedProperty restartIfAlreadyPlayingProperty;
        private SerializedProperty resetTransformBeforePlayProperty;
        private SerializedProperty resetOnCompleteProperty;

        private void OnEnable()
        {
            targetProperty = serializedObject.FindProperty("target");
            clipsProperty = serializedObject.FindProperty("clips");
            restartIfAlreadyPlayingProperty = serializedObject.FindProperty("restartIfAlreadyPlaying");
            resetTransformBeforePlayProperty = serializedObject.FindProperty("resetTransformBeforePlay");
            resetOnCompleteProperty = serializedObject.FindProperty("resetOnComplete");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTargetField();
            EditorGUILayout.PropertyField(clipsProperty);
            EditorGUILayout.PropertyField(restartIfAlreadyPlayingProperty);
            EditorGUILayout.PropertyField(resetTransformBeforePlayProperty);
            EditorGUILayout.PropertyField(resetOnCompleteProperty);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTargetField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(targetProperty);

            using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects && targets.Length == 0))
            {
                if (GUILayout.Button("This", GUILayout.Width(52f)))
                    SetTargetsToOwnTransform();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SetTargetsToOwnTransform()
        {
            serializedObject.ApplyModifiedProperties();

            foreach (Object selectedTarget in targets)
            {
                if (selectedTarget is not CurveClipPlayer player)
                    continue;

                Undo.RecordObject(player, "Set Curve Clip Player Target");
                player.target = player.transform;
                player.RecaptureOriginalTransform();
                EditorUtility.SetDirty(player);
            }

            serializedObject.Update();
        }
    }
}
