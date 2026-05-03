using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class GameOverUI : MonoBehaviour
{
    [Header("UI")]
    public GameObject panel;
    public TMP_Text gameOverText;  
    public Button retryButton;
    public Button quitButton;

    [Header("Score Texts")]
    public TMP_Text cleanScoreText;   // SCORE
    public TMP_Text o2ScoreText;      // O2

    void Awake()
    {
        panel.SetActive(false); // 시작 시 꺼두기
        retryButton.onClick.AddListener(OnRetry);
        quitButton.onClick.AddListener(OnQuit);
    }

    public void Show()
    {
        panel.SetActive(true);

        if (gameOverText != null)
            gameOverText.text = "GAME OVER";

        int clean = GameData.Instance.GetCleanScore();
        int o2 = GameData.Instance.GetO2Score();

        if (cleanScoreText != null)
            cleanScoreText.text = $"SCORE: {clean}";
        if (o2ScoreText != null)
            o2ScoreText.text = $"O2: {o2}";

    }

    void OnRetry()
    {
        Time.timeScale = 1f;

        panel.SetActive(false);

        // ✅ GameData 초기화
        if (GameData.Instance != null)
            GameData.Instance.ResetGame();

        // 하트 UI 리셋
        var bar = FindFirstObjectByType<HealthBarUI>();
        if (bar != null) bar.SetHealth(3);
    }


    void OnQuit()
    {
        Application.Quit();
    }
}
