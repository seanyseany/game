using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VillageManagement))]
public class VillageManagementEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VillageManagement villageManagement = (VillageManagement)target;
        if (villageManagement == null)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug Actions", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Clear Placed Village Objects removes only currently placed buildings, turrets, and oil objects.\n" +
            "Reset All Village Progress resets the entire village save back to its initial state.",
            MessageType.Warning);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Clear Placed Village Objects"))
                villageManagement.ClearPlacedVillageObjects();

            if (GUILayout.Button("Reset All Village Progress"))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Reset All Village Progress",
                    "This will reset the entire village save back to its initial state. Continue?",
                    "Reset",
                    "Cancel");

                if (confirmed)
                    villageManagement.ResetAllVillageProgress();
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "These buttons are enabled only in Play Mode so they do not accidentally modify edit-time scene data.",
                MessageType.Info);
        }
    }
}
