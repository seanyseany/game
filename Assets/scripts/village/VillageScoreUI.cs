using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class VillageScoreUI : MonoBehaviour
{
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private Slider scoreSlider;
    [SerializeField] private GameObject gaugeRoot;

    private VillageManagement manager;
    private int displayedLevel = -1;
    private long displayedScore = -1;
    private int displayedRequirement = -1;

    private void OnEnable()
    {
        VillageManagement.InstanceReady += Bind;
        Bind(VillageManagement.Instance);
    }

    private void OnDisable()
    {
        VillageManagement.InstanceReady -= Bind;
        if (manager != null) manager.SaveDataChanged -= Refresh;
        manager = null;
    }

    private void Bind(VillageManagement next)
    {
        if (manager != null) manager.SaveDataChanged -= Refresh;
        manager = next;
        displayedLevel = -1;
        if (manager == null) return;
        manager.SaveDataChanged += Refresh;
        Refresh(manager.SaveData);
    }

    private void Refresh(VillageManagement.VillageSaveData _)
    {
        if (manager == null) return;
        int level = manager.VillageLevel;
        long score = manager.VillageScore;
        int requirement = manager.ScoreToNextVillageLevel;
        if (displayedLevel == level && displayedScore == score && displayedRequirement == requirement) return;
        displayedLevel = level;
        displayedScore = score;
        displayedRequirement = requirement;

        if (levelText != null) levelText.text = $"Lv. {level}";
        if (scoreText != null) scoreText.text = manager.IsMaxVillageLevel ? score.ToString("N0") : $"{score:N0} / {requirement:N0}";
        if (gaugeRoot != null) gaugeRoot.SetActive(!manager.IsMaxVillageLevel);
        if (scoreSlider != null)
        {
            scoreSlider.interactable = false;
            scoreSlider.transition = Selectable.Transition.None;
            scoreSlider.minValue = 0f;
            scoreSlider.maxValue = Mathf.Max(1, requirement);
            scoreSlider.SetValueWithoutNotify(score);
        }
    }
}
