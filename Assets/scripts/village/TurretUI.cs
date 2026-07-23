using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TurretUI : MonoBehaviour
{
    public static TurretUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private Image turretImage;
    [SerializeField] private Image ammoFill;
    [SerializeField] private Button ammo30Button;
    [SerializeField] private Button ammo60Button;
    [SerializeField] private Button ammo100Button;
    [SerializeField] private Button removeButton;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text ammo30Text;
    [SerializeField] private TMP_Text ammo60Text;
    [SerializeField] private TMP_Text ammo100Text;
    [SerializeField] private TMP_Text upgradeText;

    private TurretImplementation boundImplementation;
    private BaseTurret boundTurret;

    private void Awake()
    {
        Instance = this;

        if (panel != null)
            panel.SetActive(false);
        if (ammo30Button != null)
            ammo30Button.onClick.AddListener(() => TryBuyAmmo(30));
        if (ammo60Button != null)
            ammo60Button.onClick.AddListener(() => TryBuyAmmo(60));
        if (ammo100Button != null)
            ammo100Button.onClick.AddListener(() => TryBuyAmmo(100));
        if (removeButton != null)
            removeButton.onClick.AddListener(() => boundImplementation?.RemoveTurret());
        if (upgradeButton != null)
            upgradeButton.onClick.AddListener(() => boundImplementation?.TryUpgrade());
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (panel != null && panel.activeSelf && boundTurret != null)
            Refresh();
    }

    public void Open(TurretImplementation implementation, BaseTurret turret)
    {
        boundImplementation = implementation;
        boundTurret = turret;
        if (panel != null)
            panel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (panel != null)
            panel.SetActive(false);
        boundImplementation = null;
        boundTurret = null;
    }

    public void Refresh()
    {
        if (boundTurret == null)
            return;

        if (turretImage != null)
            turretImage.sprite = boundTurret.CurrentSprite;
        if (ammoFill != null)
            ammoFill.fillAmount = boundTurret.AmmoCapacity > 0 ? (float)boundTurret.AmmoCurrent / boundTurret.AmmoCapacity : 0f;

        RefreshAmmoButton(ammo30Button, ammo30Text, 30);
        RefreshAmmoButton(ammo60Button, ammo60Text, 60);
        RefreshAmmoButton(ammo100Button, ammo100Text, 100);

        bool canUpgrade = boundTurret.CanUpgrade();
        if (upgradeButton != null)
            upgradeButton.gameObject.SetActive(canUpgrade);

        if (upgradeText != null)
        {
            BaseTurret upgradePrefab = boundTurret.GetUpgradePrefab();
            upgradeText.text = canUpgrade && upgradePrefab != null ? $"Upgrade O2 {upgradePrefab.CurrentOxygenPrice}" : string.Empty;
        }
        if (upgradeButton != null && VillageManagement.Instance != null)
        {
            BaseTurret upgradePrefab = boundTurret.GetUpgradePrefab();
            upgradeButton.interactable = canUpgrade && upgradePrefab != null &&
                                         VillageManagement.Instance.CurrentOxygen >= upgradePrefab.CurrentOxygenPrice;
        }
    }

    private void TryBuyAmmo(int percent)
    {
        if (boundTurret == null)
            return;

        boundTurret.TryBuyAmmoPercent(percent);
        Refresh();
    }

    private void RefreshAmmoButton(Button button, TMP_Text label, int percent)
    {
        if (boundTurret == null)
            return;

        int price = boundTurret.GetBulletPriceForPercent(percent);
        if (label != null)
            label.text = $"{percent}% O2 {price}";

        if (button != null && VillageManagement.Instance != null)
            button.interactable = boundTurret.CanRefillPercent(percent) && VillageManagement.Instance.CurrentOxygen >= price;
    }
}
