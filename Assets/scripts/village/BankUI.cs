using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BankUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Image bankImage;
    [SerializeField] private Sprite level1Sprite;
    [SerializeField] private Sprite level2Sprite;
    [SerializeField] private Sprite level3Sprite;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text upgradeText;

    private Bank boundBank;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);

        if (upgradeButton != null)
            upgradeButton.onClick.AddListener(HandleUpgrade);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    public void Open(Bank bank)
    {
        boundBank = bank;
        if (panel != null)
            panel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
        boundBank = null;
    }

    private void Update()
    {
        if (panel != null && panel.activeSelf)
            Refresh();
    }

    private void HandleUpgrade()
    {
        if (boundBank == null)
            return;

        boundBank.TryUpgrade();
        Refresh();
    }

    private void Refresh()
    {
        if (VillageManagement.Instance == null)
            return;

        int level = VillageManagement.Instance.BankLevel;
        if (bankImage != null)
        {
            bankImage.sprite = level >= 3 ? level3Sprite : level == 2 ? level2Sprite : level1Sprite;
        }

        bool canUpgrade = level < 3;
        int nextLevel = Mathf.Clamp(level + 1, 1, 3);
        int price = boundBank != null ? boundBank.GetUpgradePriceForLevel(nextLevel) : 0;

        if (upgradeButton != null)
            upgradeButton.gameObject.SetActive(canUpgrade);
        if (upgradeButton != null)
            upgradeButton.interactable = canUpgrade && VillageManagement.Instance.CurrentOxygen >= price;
        if (upgradeText != null)
            upgradeText.text = canUpgrade ? $"Upgrade O2 {price}" : string.Empty;
    }
}
