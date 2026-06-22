using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OxygenGeneratorUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Image oxygenImage;
    [SerializeField] private Sprite level1Sprite;
    [SerializeField] private Sprite level2Sprite;
    [SerializeField] private Sprite level3Sprite;
    [SerializeField] private TMP_Text oxygenPriceText;
    [SerializeField] private TMP_Text energyUsageText;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button closeButton;

    private OxygenImplementation boundImplementation;
    private Oxygen boundOxygen;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);
        if (upgradeButton != null)
            upgradeButton.onClick.AddListener(HandleUpgrade);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    private void Update()
    {
        if (panel != null && panel.activeSelf)
            Refresh();
    }

    public void Open(OxygenImplementation implementation, Oxygen oxygen)
    {
        boundImplementation = implementation;
        boundOxygen = oxygen;
        if (panel != null)
            panel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
        boundImplementation = null;
        boundOxygen = null;
    }

    private void HandleUpgrade()
    {
        if (boundImplementation != null)
            boundImplementation.TryUpgrade();
        Refresh();
    }

    private void Refresh()
    {
        if (boundOxygen == null || VillageManagement.Instance == null)
            return;

        int level = boundOxygen.Level;
        if (oxygenImage != null)
            oxygenImage.sprite = level >= 3 ? level3Sprite : level == 2 ? level2Sprite : level1Sprite;

        if (oxygenPriceText != null)
            oxygenPriceText.text = $"O2 {boundOxygen.CurrentOxygenPrice}";
        if (energyUsageText != null)
            energyUsageText.text = $"Energy {boundOxygen.CurrentEnergyUsage}";

        bool canUpgrade = level < 3 && boundOxygen.UpgradePrefab != null;
        if (upgradeButton != null)
            upgradeButton.gameObject.SetActive(canUpgrade);
        if (upgradeButton != null)
            upgradeButton.interactable = canUpgrade && VillageManagement.Instance.CurrentOxygen >= boundOxygen.UpgradePrefab.CurrentOxygenPrice;
    }
}
