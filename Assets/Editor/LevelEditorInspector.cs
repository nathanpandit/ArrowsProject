using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelEditor))]
public class LevelEditorInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LevelEditor levelEditor = target as LevelEditor;
        if (levelEditor == null)
        {
            return;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Solvability", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Toggle("Solvable", levelEditor.CurrentConfigurationSolvable);
            EditorGUILayout.IntField("Arrows", levelEditor.CurrentSolvabilityArrowCount);
            EditorGUILayout.IntField("Playable Cells", levelEditor.CurrentSolvabilityPlayableCellCount);
            EditorGUILayout.IntField("Blockages", levelEditor.CurrentSolvabilityBlockageCount);
        }

        MessageType messageType = levelEditor.CurrentConfigurationSolvable
            ? MessageType.Info
            : MessageType.Warning;
        EditorGUILayout.HelpBox(levelEditor.CurrentSolvabilityStatus, messageType);
    }
}
