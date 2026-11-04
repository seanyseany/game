using System;
using UnityEditor;
using UnityEngine;

internal static class VillageScoreInspector
{
    public static void Draw(VillageManagement manager)
    {
        if (manager == null) return;
        var settings = new SerializedObject(manager);
        settings.Update();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("마을 점수 / 레벨 설정", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(settings.FindProperty("initialVillageLevel"), new GUIContent("시작 마을 레벨"));
        SerializedProperty initialScore = settings.FindProperty("initialVillageScore");
        EditorGUILayout.PropertyField(initialScore, new GUIContent("시작 마을 점수"));
        initialScore.longValue = Math.Max(0L, initialScore.longValue);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("레벨별 게이지 총량", EditorStyles.boldLabel);
        SerializedProperty requirements = settings.FindProperty("villageLevelRequirements");
        for (int level = 1; level < VillageManagement.MaxVillageLevel; level++)
        {
            SerializedProperty required = requirements.FindPropertyRelative($"level{level}To{level + 1}");
            EditorGUILayout.PropertyField(required, new GUIContent($"{level} → {level + 1} 레벨 필요 점수"));
            required.intValue = Mathf.Max(1, required.intValue);
        }
        EditorGUILayout.LabelField("9레벨", "게이지 없이 점수 계속 누적");

        if (settings.ApplyModifiedProperties() && Application.isPlaying)
            manager.SetVillageProgress(manager.VillageLevel, manager.VillageScore);

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("현재 진행 값 (수정 즉시 반영·저장)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int level = EditorGUILayout.IntSlider("현재 마을 레벨", manager.VillageLevel, 1, VillageManagement.MaxVillageLevel);
            long score = EditorGUILayout.LongField("현재 마을 점수", manager.VillageScore);
            if (EditorGUI.EndChangeCheck()) manager.SetVillageProgress(level, score);
        }
        else
        {
            EditorGUILayout.HelpBox("시작 값은 새 저장 또는 마을 진행 초기화 시 적용됩니다. 실행 중에는 현재 레벨과 점수도 수정할 수 있습니다.", MessageType.Info);
        }
    }
}
