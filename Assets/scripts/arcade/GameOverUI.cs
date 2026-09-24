using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class GameOverUI : MonoBehaviour
{
    private const string DefaultQuitSceneName = "Village";
    private const string DefaultQuitScenePath = "Assets/Scenes/Village";

    [Header("UI")]
    public GameObject panel;
    [Tooltip("Canvas 아래에 있는 GameplayHUD 오브젝트")]
    public GameObject gameplayHud;
    public TMP_Text gameOverText;  
    public Button retryButton;
    public Button quitButton;

    [Header("Score Texts")]
    public TMP_Text cleanScoreText;   // ENERGY
    public TMP_Text o2ScoreText;      // O2
    public TMP_Text runScoreText;     // SCORE

    [Header("Scene")]
    [SerializeField] private string quitSceneName = DefaultQuitSceneName;

    void Awake()
    {
        ResolveGameOverScoreText();
        SetGameOverVisible(false);
        retryButton.onClick.AddListener(OnRetry);
        quitButton.onClick.AddListener(OnQuit);
    }

    private void ResolveGameOverScoreText()
    {
        if (runScoreText != null || panel == null)
            return;

        Transform scoreTransform = panel.transform.Find("Score");
        if (scoreTransform == null)
            return;

        ArcadeScoreDisplay spriteScore = scoreTransform.GetComponent<ArcadeScoreDisplay>();
        if (spriteScore != null)
        {
            spriteScore.enabled = false;
            for (int i = 0; i < scoreTransform.childCount; i++)
            {
                Transform child = scoreTransform.GetChild(i);
                if (child.name.StartsWith("Digit "))
                    child.gameObject.SetActive(false);
            }
        }

        runScoreText = scoreTransform.GetComponent<TMP_Text>();
        if (runScoreText != null)
            return;

        TextMeshProUGUI text = scoreTransform.gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;

        if (cleanScoreText != null)
        {
            text.font = cleanScoreText.font;
            text.fontSharedMaterial = cleanScoreText.fontSharedMaterial;
            text.fontSize = cleanScoreText.fontSize;
            text.fontStyle = cleanScoreText.fontStyle;
            text.color = cleanScoreText.color;
            text.alignment = cleanScoreText.alignment;
            text.enableAutoSizing = cleanScoreText.enableAutoSizing;
            text.fontSizeMin = cleanScoreText.fontSizeMin;
            text.fontSizeMax = cleanScoreText.fontSizeMax;
        }

        runScoreText = text;
    }

    private void SetGameOverVisible(bool visible)
    {
        if (panel != null)
            panel.SetActive(visible);
        if (gameplayHud != null)
            gameplayHud.SetActive(!visible);
    }

    public void Show()
    {
        SetGameOverVisible(true);

        if (gameOverText != null)
            gameOverText.text = "GAME OVER";

        int clean = GameData.Instance.GetCleanScore();
        int o2 = GameData.Instance.GetO2Score();

        if (cleanScoreText != null)
            cleanScoreText.text = clean.ToString();
        if (o2ScoreText != null)
            o2ScoreText.text = o2.ToString();
        if (runScoreText != null)
            runScoreText.text = $"SCORE: {GameData.Instance.GetRunScore()}";

    }

    void OnRetry()
    {
        Time.timeScale = 1f;

        SetGameOverVisible(false);

        // ✅ GameData 초기화
        if (GameData.Instance != null)
            GameData.Instance.ResetGame();

        // 하트 UI 리셋
        var bar = FindFirstObjectByType<HealthBarUI>();
        if (bar != null) bar.SetHealth(3);
    }


    void OnQuit()
    {
        Time.timeScale = 1f;
        SetGameOverVisible(false);

        if (GameData.Instance != null)
            GameData.Instance.PrepareForSceneTransition();

        string sceneToLoad = string.IsNullOrWhiteSpace(quitSceneName) ? DefaultQuitSceneName : quitSceneName;
        if (Application.CanStreamedLevelBeLoaded(sceneToLoad))
        {
            SceneManager.LoadScene(sceneToLoad, LoadSceneMode.Single);
            return;
        }

        if (Application.CanStreamedLevelBeLoaded(DefaultQuitScenePath))
        {
            SceneManager.LoadScene(DefaultQuitScenePath, LoadSceneMode.Single);
            return;
        }

        Debug.LogError($"[GameOverUI] Quit target scene could not be loaded. Check Build Settings. name={sceneToLoad}, path={DefaultQuitScenePath}");
    }
}
