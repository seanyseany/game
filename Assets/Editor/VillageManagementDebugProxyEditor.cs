using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VillageManagementDebugProxy))]
public class VillageManagementDebugProxyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VillageManagementDebugProxy proxy = (VillageManagementDebugProxy)target;
        if (proxy == null)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Actions", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Select Runtime VillageManagement"))
            {
                proxy.SelectRuntimeManager();
                if (proxy.Target != null)
                    Selection.activeObject = proxy.Target;
            }

            if (GUILayout.Button("Clear Placed Village Objects"))
                proxy.ClearPlacedVillageObjects();

            if (GUILayout.Button("Reset All Village Progress"))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Reset All Village Progress",
                    "This will reset selected village progress fields and clear placed village objects. Continue?",
                    "Reset",
                    "Cancel");

                if (confirmed)
                    proxy.ResetAllVillageProgress();
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "These buttons are enabled only in Play Mode.",
                MessageType.Info);
        }
    }
}
