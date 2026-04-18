using PixelFlow.Runtime.Visuals;
using UnityEditor;

namespace PixelFlow.Editor.Visuals
{
    [CustomEditor(typeof(PixelFlowAtlasColorTarget))]
    [CanEditMultipleObjects]
    public sealed class PixelFlowAtlasColorTargetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            DrawIfPresent("currentColor");
            DrawIfPresent("renderers");
            DrawIfPresent("enableOutline");

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawIfPresent(string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, true);
            }
        }
    }
}
