using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(GameData.StageManagerEntry))]
public sealed class GameDataStageManagerEntryDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        position = EditorGUI.PrefixLabel(position, label);
        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        var prefabRect = new Rect(position.x, position.y, Mathf.Max(0f, position.width - 60f), position.height);
        var toggleRect = new Rect(prefabRect.xMax + 6f, position.y, 54f, position.height);
        SerializedProperty prefab = property.FindPropertyRelative("prefab");
        SerializedProperty use = property.FindPropertyRelative("use");
        StageManager manager = prefab.objectReferenceValue as StageManager;
        EditorGUI.BeginChangeCheck();
        // Unity's asset picker lists prefab GameObjects, not their MonoBehaviour components.
        GameObject selectedPrefab = EditorGUI.ObjectField(prefabRect,
            new GUIContent("", "StageManager 컴포넌트가 있는 프리팹을 등록하세요."),
            manager != null ? manager.gameObject : null, typeof(GameObject), false) as GameObject;
        if (EditorGUI.EndChangeCheck()) AssignPrefab(prefab, selectedPrefab);
        EditorGUI.BeginChangeCheck();
        bool selected = EditorGUI.ToggleLeft(toggleRect, "사용", use.boolValue);
        if (EditorGUI.EndChangeCheck())
        {
            if (selected)
            {
                SerializedProperty entries = property.serializedObject.FindProperty("stageManagers");
                for (int i = 0; i < entries.arraySize; i++)
                    entries.GetArrayElementAtIndex(i).FindPropertyRelative("use").boolValue = false;
            }
            use.boolValue = selected;
        }
        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }

    internal static bool AssignPrefab(SerializedProperty property, GameObject prefab)
    {
        StageManager manager = prefab != null ? prefab.GetComponent<StageManager>() : null;
        if (prefab != null && manager == null)
        {
            Debug.LogWarning("StageManager 컴포넌트가 있는 프리팹을 등록하세요.", prefab);
            return false;
        }
        property.objectReferenceValue = manager;
        return true;
    }
}
