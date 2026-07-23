using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Play : MonoBehaviour
{
    [System.Serializable]
    public class ArcadeSceneEntry
    {
        public string displayName = "Arcade";
        public string sceneName = "arcade 1";
    }

    [Header("Arcade Scenes")]
    [SerializeField] private List<ArcadeSceneEntry> arcadeScenes = new List<ArcadeSceneEntry>();

    [Header("UI")]
    [SerializeField] private Button openButton;

    private readonly List<Button> sceneButtons = new List<Button>();
    private readonly List<Button> playerButtons = new List<Button>();

    private Canvas rootCanvas;
    private RectTransform panelRoot;
    private RectTransform sceneListRoot;
    private RectTransform playerListRoot;
    private Text titleText;
    private Text selectedSceneText;
    private Text selectedPlayerText;
    private Text emptyPlayersText;
    private Button closeButton;
    private Button playButton;
    private bool uiBuilt;
    private int selectedSceneIndex;
    private int selectedPlayerType = -1;

    private void Awake()
    {
        if (openButton == null)
            openButton = GetComponent<Button>();

        if (openButton != null)
            openButton.onClick.AddListener(OpenPlayMenu);

        BuildUiIfNeeded();
        RefreshSceneButtons();
        RefreshPlayerButtons();
        SetPanelVisible(false);
    }

    private void OnEnable()
    {
        VillageManagement.InstanceReady += HandleVillageReady;
        if (VillageManagement.Instance != null)
            VillageManagement.Instance.SaveDataChanged += HandleSaveDataChanged;
    }

    private void OnDisable()
    {
        VillageManagement.InstanceReady -= HandleVillageReady;
        if (VillageManagement.Instance != null)
            VillageManagement.Instance.SaveDataChanged -= HandleSaveDataChanged;
    }

    public void OpenPlayMenu()
    {
        BuildUiIfNeeded();
        SetPanelVisible(true);
        panelRoot.SetAsLastSibling();
        RefreshSceneButtons();
        RefreshPlayerButtons();
    }

    public void ClosePlayMenu()
    {
        SetPanelVisible(false);
    }

    private void HandleVillageReady(VillageManagement villageManagement)
    {
        if (villageManagement == null)
            return;

        villageManagement.SaveDataChanged -= HandleSaveDataChanged;
        villageManagement.SaveDataChanged += HandleSaveDataChanged;
        RefreshPlayerButtons();
    }

    private void HandleSaveDataChanged(VillageManagement.VillageSaveData _)
    {
        RefreshPlayerButtons();
    }

    private void BuildUiIfNeeded()
    {
        if (uiBuilt)
            return;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        rootCanvas = GetComponentInParent<Canvas>();
        Transform panelParent = rootCanvas != null ? rootCanvas.transform : transform;

        GameObject panelObject = CreateUiObject("PlayPanel", panelParent);
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.07f, 0.1f, 0.15f, 0.96f);
        panelRoot = panelObject.GetComponent<RectTransform>();
        panelRoot.anchorMin = new Vector2(0.12f, 0.12f);
        panelRoot.anchorMax = new Vector2(0.88f, 0.88f);
        panelRoot.offsetMin = Vector2.zero;
        panelRoot.offsetMax = Vector2.zero;

        titleText = CreateText("Title", panelObject.transform, font, 40, TextAnchor.MiddleLeft);
        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(28f, -84f);
        titleRect.offsetMax = new Vector2(-140f, -20f);
        titleText.text = "Play";

        closeButton = CreateButton("CloseButton", panelObject.transform, new Vector2(120f, 56f), new Vector2(-90f, -56f), "Close", font);
        RectTransform closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 1f);
        closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeButton.onClick.AddListener(ClosePlayMenu);

        GameObject bodyObject = CreateUiObject("Body", panelObject.transform);
        RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.offsetMin = new Vector2(20f, 20f);
        bodyRect.offsetMax = new Vector2(-20f, -96f);

        sceneListRoot = CreateSection("SceneSection", bodyObject.transform, new Vector2(0f, 0.66f), new Vector2(1f, 1f), new Color(0.12f, 0.16f, 0.22f, 0.98f));
        playerListRoot = CreateSection("PlayerSection", bodyObject.transform, new Vector2(0f, 0f), new Vector2(1f, 0.66f), new Color(0.1f, 0.14f, 0.19f, 0.98f));

        Text sceneHeader = CreateText("SceneHeader", sceneListRoot, font, 28, TextAnchor.UpperLeft);
        RectTransform sceneHeaderRect = sceneHeader.rectTransform;
        sceneHeaderRect.anchorMin = new Vector2(0f, 1f);
        sceneHeaderRect.anchorMax = new Vector2(1f, 1f);
        sceneHeaderRect.pivot = new Vector2(0.5f, 1f);
        sceneHeaderRect.offsetMin = new Vector2(20f, -48f);
        sceneHeaderRect.offsetMax = new Vector2(-20f, -8f);
        sceneHeader.text = "Arcade Scene";

        selectedSceneText = CreateText("SelectedScene", sceneListRoot, font, 24, TextAnchor.UpperRight);
        RectTransform selectedRect = selectedSceneText.rectTransform;
        selectedRect.anchorMin = new Vector2(0f, 1f);
        selectedRect.anchorMax = new Vector2(1f, 1f);
        selectedRect.pivot = new Vector2(0.5f, 1f);
        selectedRect.offsetMin = new Vector2(220f, -48f);
        selectedRect.offsetMax = new Vector2(-20f, -8f);

        Text playerHeader = CreateText("PlayerHeader", playerListRoot, font, 28, TextAnchor.UpperLeft);
        RectTransform playerHeaderRect = playerHeader.rectTransform;
        playerHeaderRect.anchorMin = new Vector2(0f, 1f);
        playerHeaderRect.anchorMax = new Vector2(1f, 1f);
        playerHeaderRect.pivot = new Vector2(0.5f, 1f);
        playerHeaderRect.offsetMin = new Vector2(20f, -48f);
        playerHeaderRect.offsetMax = new Vector2(-20f, -8f);
        playerHeader.text = "Available Players";

        selectedPlayerText = CreateText("SelectedPlayer", playerListRoot, font, 24, TextAnchor.UpperRight);
        RectTransform selectedPlayerRect = selectedPlayerText.rectTransform;
        selectedPlayerRect.anchorMin = new Vector2(0f, 1f);
        selectedPlayerRect.anchorMax = new Vector2(1f, 1f);
        selectedPlayerRect.pivot = new Vector2(0.5f, 1f);
        selectedPlayerRect.offsetMin = new Vector2(220f, -48f);
        selectedPlayerRect.offsetMax = new Vector2(-20f, -8f);

        emptyPlayersText = CreateText("EmptyPlayers", playerListRoot, font, 26, TextAnchor.MiddleCenter);
        RectTransform emptyRect = emptyPlayersText.rectTransform;
        emptyRect.anchorMin = Vector2.zero;
        emptyRect.anchorMax = Vector2.one;
        emptyRect.offsetMin = new Vector2(20f, 96f);
        emptyRect.offsetMax = new Vector2(-20f, -56f);
        emptyPlayersText.text = "No available players.";

        playButton = CreateButton("PlayButton", playerListRoot, Vector2.zero, Vector2.zero, "Play", font);
        RectTransform playRect = playButton.GetComponent<RectTransform>();
        playRect.anchorMin = new Vector2(0f, 0f);
        playRect.anchorMax = new Vector2(1f, 0f);
        playRect.pivot = new Vector2(0.5f, 0f);
        playRect.offsetMin = new Vector2(20f, 20f);
        playRect.offsetMax = new Vector2(-20f, 82f);
        playButton.onClick.AddListener(StartSelectedArcade);

        uiBuilt = true;
    }

    private void RefreshSceneButtons()
    {
        if (sceneListRoot == null)
            return;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < sceneButtons.Count; i++)
        {
            if (sceneButtons[i] != null)
                Destroy(sceneButtons[i].gameObject);
        }
        sceneButtons.Clear();

        if (arcadeScenes.Count == 0)
        {
            if (selectedSceneText != null)
                selectedSceneText.text = "Scene not registered";
            return;
        }

        selectedSceneIndex = Mathf.Clamp(selectedSceneIndex, 0, arcadeScenes.Count - 1);
        if (selectedSceneText != null)
            selectedSceneText.text = $"Selected: {GetSceneDisplayName(selectedSceneIndex)}";

        float top = -70f;
        float buttonHeight = 54f;
        float spacing = 12f;
        for (int i = 0; i < arcadeScenes.Count; i++)
        {
            int sceneIndex = i;
            Button button = CreateButton($"Scene_{i}", sceneListRoot, Vector2.zero, Vector2.zero, GetSceneDisplayName(i), font);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(20f, top - buttonHeight);
            rect.offsetMax = new Vector2(-20f, top);
            top -= buttonHeight + spacing;

            button.onClick.AddListener(() =>
            {
                selectedSceneIndex = sceneIndex;
                RefreshSceneButtons();
            });

            sceneButtons.Add(button);
        }

        RefreshSceneButtonColors();
    }

    private void RefreshSceneButtonColors()
    {
        for (int i = 0; i < sceneButtons.Count; i++)
        {
            Image image = sceneButtons[i] != null ? sceneButtons[i].GetComponent<Image>() : null;
            if (image == null)
                continue;

            image.color = i == selectedSceneIndex
                ? new Color(0.94f, 0.57f, 0.18f, 1f)
                : new Color(0.2f, 0.26f, 0.34f, 1f);
        }
    }

    private void RefreshPlayerButtons()
    {
        if (playerListRoot == null)
            return;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < playerButtons.Count; i++)
        {
            if (playerButtons[i] != null)
                Destroy(playerButtons[i].gameObject);
        }
        playerButtons.Clear();

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        List<VillageManagement.ArcadePlayerEntry> availablePlayers = villageManagement != null
            ? villageManagement.GetAvailableArcadePlayers()
            : new List<VillageManagement.ArcadePlayerEntry>();

        bool hasPlayers = availablePlayers.Count > 0;
        bool selectedPlayerStillAvailable = false;
        for (int i = 0; i < availablePlayers.Count; i++)
        {
            if (availablePlayers[i].playerType == selectedPlayerType)
            {
                selectedPlayerStillAvailable = true;
                break;
            }
        }

        if (!selectedPlayerStillAvailable)
            selectedPlayerType = hasPlayers ? availablePlayers[0].playerType : -1;

        if (emptyPlayersText != null)
            emptyPlayersText.gameObject.SetActive(!hasPlayers);
        if (selectedPlayerText != null)
            selectedPlayerText.text = hasPlayers
                ? $"Selected: Player {selectedPlayerType}"
                : "Selected: None";
        if (playButton != null)
            playButton.interactable = hasPlayers && selectedPlayerType > 0;

        if (!hasPlayers)
            return;

        int columns = 2;
        float widthPadding = 20f;
        float top = -70f;
        float height = 62f;
        float spacingY = 14f;
        float spacingX = 14f;

        for (int i = 0; i < availablePlayers.Count; i++)
        {
            VillageManagement.ArcadePlayerEntry player = availablePlayers[i];
            Button button = CreateButton($"Player_{player.playerType}", playerListRoot, Vector2.zero, Vector2.zero, BuildPlayerLabel(player), font);
            RectTransform rect = button.GetComponent<RectTransform>();

            int row = i / columns;
            int column = i % columns;
            float yTop = top - ((height + spacingY) * row);

            if (column == 0)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(widthPadding, yTop - height);
                rect.offsetMax = new Vector2(-spacingX * 0.5f, yTop);
            }
            else
            {
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.offsetMin = new Vector2(spacingX * 0.5f, yTop - height);
                rect.offsetMax = new Vector2(-widthPadding, yTop);
            }

            rect.pivot = new Vector2(0.5f, 1f);

            int playerType = player.playerType;
            button.onClick.AddListener(() =>
            {
                selectedPlayerType = playerType;
                RefreshPlayerButtons();
            });
            playerButtons.Add(button);
        }

        RefreshPlayerButtonColors();
    }

    private void RefreshPlayerButtonColors()
    {
        for (int i = 0; i < playerButtons.Count; i++)
        {
            Button button = playerButtons[i];
            if (button == null)
                continue;

            Image image = button.GetComponent<Image>();
            if (image == null)
                continue;

            bool isSelected = button.name == $"Player_{selectedPlayerType}";
            image.color = isSelected
                ? new Color(0.94f, 0.57f, 0.18f, 1f)
                : new Color(0.2f, 0.26f, 0.34f, 1f);
        }
    }

    private void StartSelectedArcade()
    {
        if (selectedPlayerType <= 0)
            return;

        StartArcade(selectedPlayerType);
    }

    private void StartArcade(int playerType)
    {
        if (arcadeScenes.Count == 0)
            return;

        VillageManagement villageManagement = VillageManagement.EnsureInstance();
        if (villageManagement == null || !villageManagement.IsArcadePlayerAvailable(playerType))
            return;

        GameData gameData = EnsureGameData();
        if (gameData != null)
            gameData.PrepareForSceneTransition();
        GameData.SetPendingSelectedPlayer(playerType, 3);

        string sceneName = arcadeScenes[Mathf.Clamp(selectedSceneIndex, 0, arcadeScenes.Count - 1)].sceneName;
        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogWarning($"Play scene '{sceneName}' is not available in Build Settings.");
            return;
        }

        VillageCameraScroller.ResetActiveToDefaultPosition();
        ClosePlayMenu();
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private static GameData EnsureGameData()
    {
        if (GameData.Instance != null)
            return GameData.Instance;

        GameData existing = FindFirstObjectByType<GameData>();
        return existing;
    }

    private string GetSceneDisplayName(int index)
    {
        if (index < 0 || index >= arcadeScenes.Count || arcadeScenes[index] == null)
            return "Arcade";

        return string.IsNullOrWhiteSpace(arcadeScenes[index].displayName)
            ? arcadeScenes[index].sceneName
            : arcadeScenes[index].displayName;
    }

    private static string BuildPlayerLabel(VillageManagement.ArcadePlayerEntry player)
    {
        return $"Player {player.playerType}";
    }

    private void SetPanelVisible(bool visible)
    {
        if (panelRoot != null)
            panelRoot.gameObject.SetActive(visible);
    }

    private static RectTransform CreateSection(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color backgroundColor)
    {
        GameObject sectionObject = CreateUiObject(name, parent);
        Image image = sectionObject.AddComponent<Image>();
        image.color = backgroundColor;

        RectTransform rect = sectionObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(0f, 0f);
        rect.offsetMax = new Vector2(0f, -10f);
        return rect;
    }

    private static Button CreateButton(string name, Transform parent, Vector2 size, Vector2 anchoredPosition, string label, Font font)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.2f, 0.26f, 0.34f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.28f, 0.36f, 0.46f, 1f);
        colors.pressedColor = new Color(0.16f, 0.2f, 0.26f, 1f);
        colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.7f);
        button.colors = colors;

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        Text buttonText = CreateText("Label", buttonObject.transform, font, 24, TextAnchor.MiddleCenter);
        buttonText.text = label;
        RectTransform textRect = buttonText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private static Text CreateText(string name, Transform parent, Font font, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = CreateUiObject(name, parent);
        Text text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return gameObject;
    }
}
