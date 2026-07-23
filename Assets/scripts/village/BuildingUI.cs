using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BuildingUI : MonoBehaviour
{
    public static BuildingUI Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Image buildingImage;
    [SerializeField] private Image workerImage;
    [SerializeField] private Image salaryFill;
    [SerializeField] private Button pay30Button;
    [SerializeField] private Button pay60Button;
    [SerializeField] private Button pay100Button;
    [SerializeField] private Button removeButton;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text pay30Text;
    [SerializeField] private TMP_Text pay60Text;
    [SerializeField] private TMP_Text pay100Text;
    [SerializeField] private TMP_Text upgradeText;

    private Path boundPath;
    private Building boundBuilding;

    private void Awake()
    {
        Instance = this;

        if (panel != null)
            panel.SetActive(false);

        if (pay30Button != null)
            pay30Button.onClick.AddListener(() => TryPay(30));
        if (pay60Button != null)
            pay60Button.onClick.AddListener(() => TryPay(60));
        if (pay100Button != null)
            pay100Button.onClick.AddListener(() => TryPay(100));
        if (removeButton != null)
            removeButton.onClick.AddListener(HandleRemove);
        if (upgradeButton != null)
            upgradeButton.onClick.AddListener(HandleUpgrade);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Open(Path path, Building building)
    {
        boundPath = path;
        boundBuilding = building;
        if (panel != null)
            panel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
        boundPath = null;
        boundBuilding = null;
    }

    public void Refresh()
    {
        if (boundBuilding == null)
            return;

        if (buildingImage != null)
            buildingImage.sprite = boundBuilding.Level >= 2 ? boundBuilding.Level2Sprite : boundBuilding.Level1Sprite;
        if (workerImage != null)
            workerImage.sprite = boundBuilding.WorkingBloodSprite;
        if (salaryFill != null)
            salaryFill.fillAmount = boundBuilding.MaxSalary > 0 ? (float)boundBuilding.CurrentSalary / boundBuilding.MaxSalary : 0f;

        RefreshPayButton(pay30Button, pay30Text, 30);
        RefreshPayButton(pay60Button, pay60Text, 60);
        RefreshPayButton(pay100Button, pay100Text, 100);

        bool canUpgrade = boundBuilding.Level < 2;
        if (upgradeButton != null)
            upgradeButton.gameObject.SetActive(canUpgrade);

        int upgradePrice = canUpgrade ? boundBuilding.GetPurchasePriceForLevel(2) : 0;
        if (upgradeText != null)
            upgradeText.text = canUpgrade ? $"Upgrade O2 {upgradePrice}" : string.Empty;
        if (upgradeButton != null && VillageManagement.Instance != null)
            upgradeButton.interactable = canUpgrade && VillageManagement.Instance.CurrentOxygen >= upgradePrice;
    }

    private void TryPay(int percent)
    {
        if (boundBuilding == null || VillageManagement.Instance == null)
            return;

        int price = boundBuilding.GetSalaryPriceForPercent(percent);
        if (!boundBuilding.CanReceiveSalaryPercent(percent))
            return;
        if (!VillageManagement.Instance.TrySpendOxygen(price))
            return;

        boundBuilding.TryAddSalaryPercent(percent);
        Refresh();
    }

    private void HandleRemove()
    {
        boundPath?.RemoveCurrentBuilding();
        Close();
    }

    private void HandleUpgrade()
    {
        boundPath?.TryUpgradeCurrentBuilding();
        Refresh();
    }

    private void RefreshPayButton(Button button, TMP_Text label, int percent)
    {
        if (boundBuilding == null)
            return;

        int price = boundBuilding.GetSalaryPriceForPercent(percent);
        if (label != null)
            label.text = $"{percent}% O2 {price}";

        if (button != null && VillageManagement.Instance != null)
        {
            button.interactable = boundBuilding.CanReceiveSalaryPercent(percent) &&
                                 VillageManagement.Instance.CurrentOxygen >= price;
        }
    }
}
