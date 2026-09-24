using TMPro;
using UnityEngine;

public class ScoreUI : MonoBehaviour
{
    public TextMeshProUGUI scoreText; // ENERGY
    public TextMeshProUGUI o2Text;    // O2
    public TextMeshProUGUI gameplayEnergyText;
    public TextMeshProUGUI gameplayO2Text;
    private Transform gameplayHudRoot;

    void Update()
    {
        if (GameData.Instance == null) return;

        ResolveGameplayTexts();

        string energy = GameData.Instance.GetCleanScore().ToString();
        string o2 = GameData.Instance.GetO2Score().ToString();

        if (scoreText != null)
            scoreText.text = energy;
        if (o2Text != null)
            o2Text.text = o2;
        if (gameplayEnergyText != null)
            gameplayEnergyText.text = energy;
        if (gameplayO2Text != null)
            gameplayO2Text.text = o2;
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
