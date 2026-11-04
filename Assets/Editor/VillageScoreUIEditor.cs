using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VillageScoreUI))]
public sealed class VillageScoreUIEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        VillageManagement manager = VillageManagement.Instance;
        if (manager == null)
        {
            VillageManagement[] candidates = Object.FindObjectsByType<VillageManagement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var ui = (VillageScoreUI)target;
            foreach (VillageManagement candidate in candidates)
                if (candidate.gameObject.scene == ui.gameObject.scene) { manager = candidate; break; }
        }
        if (manager != null)
            VillageScoreInspector.Draw(manager);
        else
            EditorGUILayout.HelpBox("같은 씬의 VillageManagement에서 마을 점수와 레벨별 게이지 총량을 설정할 수 있습니다.", MessageType.Info);
    }
}
