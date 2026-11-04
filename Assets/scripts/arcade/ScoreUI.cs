using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

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
    private const float OxygenHitDuration = 0.2f;
    private const float OxygenHitShakeDistance = 8f;
    private const float OxygenHitShakeFrequency = 18f;
    private readonly Dictionary<Graphic, Color> oxygenHitColors = new Dictionary<Graphic, Color>();
    private readonly Dictionary<RectTransform, Vector2> oxygenHitPositions = new Dictionary<RectTransform, Vector2>();
    private float oxygenHitStartedAt;
    private float oxygenHitFlashUntil = -1f;
    private readonly IconGainPulse energyIconPulse = new IconGainPulse();
    private readonly IconGainPulse oxygenIconPulse = new IconGainPulse();

    private sealed class IconGainPulse
    {
        public RectTransform Icon { get; private set; }
        private Vector3 originalScale;
        private float startedAt;
        private float startingMultiplier;
        private float currentMultiplier = 1f;
        private bool active;

        public void Play(RectTransform icon)
        {
            if (icon == null || !icon.gameObject.activeInHierarchy) return;
            if (active && Icon != icon) Restore();
            if (!active)
            {
                Icon = icon;
                originalScale = icon.localScale;
                currentMultiplier = 1f;
            }
            startingMultiplier = currentMultiplier;
            startedAt = Time.unscaledTime;
            active = true;
        }

        public void Tick()
        {
            if (!active) return;
            float progress = (Time.unscaledTime - startedAt) / 0.3f;
            if (Icon == null || !Icon.gameObject.activeInHierarchy || progress >= 1f)
            {
                Restore();
                return;
            }
            currentMultiplier = progress < 0.5f
                ? Mathf.Lerp(startingMultiplier, 1.2f, Mathf.SmoothStep(0f, 1f, progress * 2f))
                : Mathf.Lerp(1.2f, 1f, Mathf.SmoothStep(0f, 1f, (progress - 0.5f) * 2f));
            Icon.localScale = originalScale * currentMultiplier;
        }

        public void Restore()
        {
            if (!active) return;
            if (Icon != null) Icon.localScale = originalScale;
            active = false;
            currentMultiplier = 1f;
        }
    }

    private void PulseIcon(IconGainPulse pulse, string iconName, TextMeshProUGUI text)
    {
        RectTransform icon = pulse.Icon;
        if (icon == null)
        {
            Transform root = gameplayHudRoot;
            if (root == null && text != null)
            {
                Canvas canvas = text.GetComponentInParent<Canvas>();
                if (canvas != null) root = canvas.transform;
            }
            if (root == null) return;
            foreach (RectTransform child in root.GetComponentsInChildren<RectTransform>(true))
                if (child.name == iconName) { icon = child; break; }
        }
        pulse.Play(icon);
    }

    private void OnEnable() { nextResolveTime = 0f; displayedEnergy = displayedOxygen = int.MinValue; }

    public void FlashOxygenLoss()
    {
        ResolveGameplayTexts();
        FlashOxygenRoot(FindOxygenRoot(o2Text));
        FlashOxygenRoot(FindOxygenRoot(gameplayO2Text));
        oxygenHitStartedAt = Time.unscaledTime;
        oxygenHitFlashUntil = oxygenHitStartedAt + OxygenHitDuration;
    }

    private static RectTransform FindOxygenRoot(TextMeshProUGUI text)
    {
        if (text == null) return null;
        for (Transform current = text.transform; current != null; current = current.parent)
        {
            if (current.name == "O2" || current.name == "O2 Background") return current as RectTransform;
            if (current.GetComponent<Canvas>() != null) break;
        }
        return text.rectTransform;
    }

    private void FlashOxygenRoot(RectTransform root)
    {
        if (root == null) return;
        if (!oxygenHitPositions.ContainsKey(root))
            oxygenHitPositions.Add(root, root.anchoredPosition);
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            if (!oxygenHitColors.ContainsKey(graphic))
                oxygenHitColors.Add(graphic, graphic.color);
            graphic.color = new Color(1f, 0f, 0f, oxygenHitColors[graphic].a);
        }
    }

    private void LateUpdate()
    {
        energyIconPulse.Tick();
        oxygenIconPulse.Tick();
        if (oxygenHitFlashUntil < 0f) return;
        if (Time.unscaledTime >= oxygenHitFlashUntil)
        {
            RestoreOxygenHitColors();
            return;
        }

        foreach (var entry in oxygenHitColors)
            if (entry.Key != null)
                entry.Key.color = new Color(1f, 0f, 0f, entry.Value.a);
        float elapsed = Time.unscaledTime - oxygenHitStartedAt;
        float fade = Mathf.Clamp01((oxygenHitFlashUntil - Time.unscaledTime) / OxygenHitDuration);
        float offset = Mathf.Sin(elapsed * OxygenHitShakeFrequency * Mathf.PI * 2f) * OxygenHitShakeDistance * fade;
        foreach (var entry in oxygenHitPositions)
            if (entry.Key != null)
                entry.Key.anchoredPosition = entry.Value + Vector2.right * offset;
    }

    private void OnDisable()
    {
        RestoreOxygenHitColors();
        energyIconPulse.Restore();
        oxygenIconPulse.Restore();
    }

    private void RestoreOxygenHitColors()
    {
        foreach (var entry in oxygenHitColors)
            if (entry.Key != null)
                entry.Key.color = entry.Value;
        oxygenHitColors.Clear();
        foreach (var entry in oxygenHitPositions)
            if (entry.Key != null)
                entry.Key.anchoredPosition = entry.Value;
        oxygenHitPositions.Clear();
        oxygenHitFlashUntil = -1f;
    }

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
        if (energy != displayedEnergy)
        {
            if (displayedEnergy != int.MinValue && energy > displayedEnergy)
                PulseIcon(energyIconPulse, "EnergyIcon", gameplayEnergyText);
            displayedEnergy = energy;
            energyLabel = energy.ToString();
        }
        if (oxygen != displayedOxygen)
        {
            if (displayedOxygen != int.MinValue && oxygen > displayedOxygen)
                PulseIcon(oxygenIconPulse, "O2Icon", gameplayO2Text);
            displayedOxygen = oxygen;
            oxygenLabel = oxygen.ToString();
        }

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
            gameplayHudRoot = transform.Find("GameplayHUD");
            if (gameplayHudRoot == null)
            {
                GameOverUI gameOverUI = FindFirstObjectByType<GameOverUI>(FindObjectsInactive.Include);
                if (gameOverUI != null && gameOverUI.gameplayHud != null)
                    gameplayHudRoot = gameOverUI.gameplayHud.transform;
            }
        }

        if (gameplayHudRoot == null)
            return;

        if (gameplayEnergyText == null)
            gameplayEnergyText = FindResourceText("Energy");

        if (gameplayO2Text == null)
            gameplayO2Text = FindResourceText("O2");
    }

    private TextMeshProUGUI FindResourceText(string resourceName)
    {
        // Prefer the numeric label even when a separate title is present in the HUD.
        string numberName = resourceName + "Txt";
        foreach (TextMeshProUGUI text in gameplayHudRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (string.Equals(text.name, numberName, System.StringComparison.OrdinalIgnoreCase))
                return text;

        Transform group = gameplayHudRoot.Find(resourceName) ?? gameplayHudRoot.Find(resourceName + " Background");
        return group != null ? group.GetComponentInChildren<TextMeshProUGUI>(true) : null;
    }
}
