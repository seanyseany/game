using TMPro;
using UnityEngine;

public class ScoreUI : MonoBehaviour
{
    public TextMeshProUGUI scoreText; // ENERGY
    public TextMeshProUGUI o2Text;    // O2
    public TextMeshProUGUI gameplayEnergyText;
    public TextMeshProUGUI gameplayO2Text;
    private Transform gameplayHudRoot;
    private float nextResolveTime;
    private int displayedEnergy = int.MinValue;
    private int displayedOxygen = int.MinValue;
    private string energyLabel;
    private string oxygenLabel;

    private void OnEnable() { nextResolveTime = 0f; displayedEnergy = displayedOxygen = int.MinValue; }

    void Update()
    {
        if (GameData.Instance == null) return;

        if ((gameplayEnergyText == null || gameplayO2Text == null) && Time.unscaledTime >= nextResolveTime)
        {
            ResolveGameplayTexts();
            nextResolveTime = Time.unscaledTime + 0.5f;
        }

        int energy = GameData.Instance.GetCleanScore();
        int oxygen = GameData.Instance.GetO2Score();
        if (energy != displayedEnergy) { displayedEnergy = energy; energyLabel = energy.ToString(); }
        if (oxygen != displayedOxygen) { displayedOxygen = oxygen; oxygenLabel = oxygen.ToString(); }

        SetLabel(scoreText, energyLabel);
        SetLabel(o2Text, oxygenLabel);
        SetLabel(gameplayEnergyText, energyLabel);
        SetLabel(gameplayO2Text, oxygenLabel);
    }

    private static void SetLabel(TMP_Text text, string value)
    {
        if (text != null && text.text != value) text.text = value;
    }

    private void ResolveGameplayTexts()
    {
        if (gameplayEnergyText != null && gameplayO2Text != null)
            return;

        if (gameplayHudRoot == null)
        {
            GameOverUI gameOverUI = FindFirstObjectByType<GameOverUI>();
            if (gameOverUI != null && gameOverUI.gameplayHud != null)
                gameplayHudRoot = gameOverUI.gameplayHud.transform;
        }

        if (gameplayHudRoot == null)
            return;

        if (gameplayEnergyText == null)
        {
            Transform energy = gameplayHudRoot.Find("Energy");
            if (energy != null)
                gameplayEnergyText = energy.GetComponent<TextMeshProUGUI>();
        }

        if (gameplayO2Text == null)
        {
            Transform o2 = gameplayHudRoot.Find("O2");
            if (o2 != null)
                gameplayO2Text = o2.GetComponent<TextMeshProUGUI>();
        }
    }
}
