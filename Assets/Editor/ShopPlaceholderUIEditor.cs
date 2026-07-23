using UnityEditor;

[CustomEditor(typeof(ShopPlaceholderUI))]
public class ShopPlaceholderUIEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawProperty("sectionTitle");
        DrawProperty("message");

        string sectionTitle = serializedObject.FindProperty("sectionTitle").stringValue;
        bool isOil = string.Equals(sectionTitle, "Oil", System.StringComparison.OrdinalIgnoreCase);
        bool isTurret = string.Equals(sectionTitle, "Turret", System.StringComparison.OrdinalIgnoreCase);

        if (isOil)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Oil Shop", EditorStyles.boldLabel);
            DrawProperty("registeredOilPrefabs");
            DrawProperty("oilShopSlotCount");
        }
        else if (isTurret)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Turret Shop", EditorStyles.boldLabel);
            DrawProperty("registeredTurretPrefabs");
            DrawProperty("registeredTurretPathPrefabs");
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawProperty(string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            EditorGUILayout.PropertyField(property, true);
    }
}
