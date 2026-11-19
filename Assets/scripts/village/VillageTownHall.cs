using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class VillageTownHall : MonoBehaviour
{
    [Serializable]
    public class LevelActivation
    {
        [Range(1, 9), Tooltip("현재 마을 레벨이 이 값과 같을 때만 표시합니다.")] public int requiredVillageLevel = 1;
        [Tooltip("씬 오브젝트는 직접 활성화하고, 프리팹 에셋은 Visual Root 아래에 생성합니다. 현재 레벨만 켜지고 이전 레벨은 꺼집니다.")]
        public GameObject linkedPrefab;
        [NonSerialized] public GameObject instance;
    }

    [Header("Village Level Activations")]
    [SerializeField] private List<LevelActivation> levelActivations = new List<LevelActivation>
    {
        new LevelActivation { requiredVillageLevel = 1 },
        new LevelActivation { requiredVillageLevel = 2 },
        new LevelActivation { requiredVillageLevel = 3 },
        new LevelActivation { requiredVillageLevel = 4 },
        new LevelActivation { requiredVillageLevel = 5 },
        new LevelActivation { requiredVillageLevel = 6 },
        new LevelActivation { requiredVillageLevel = 7 },
        new LevelActivation { requiredVillageLevel = 8 },
        new LevelActivation { requiredVillageLevel = 9 }
    };
    [SerializeField] private Transform visualRoot;
    [Header("Customer Satisfaction")]
    [SerializeField, Tooltip("거래 또는 도르래 이용 완료 시 손님 위치에 1초간 표시한 뒤 0.5초 동안 투명해지며 사라지는 만족도 이모지입니다.")]
    private GameObject customerEmojiPrefab;
    [Header("Hit Feedback")]
    [SerializeField, Min(0.01f)] private float hitDuration = 0.2f;
    [SerializeField, Min(0.01f), Tooltip("회관 피격 효과가 끝난 뒤 화면 테두리가 투명해지며 사라지는 시간입니다.")]
    private float screenBorderFadeDuration = 0.5f;
    [SerializeField, Min(0f)] private float shakeDistance = 0.08f;
    [SerializeField, Min(0f)] private float shakeFrequency = 18f;

    private VillageManagement manager;
    private int appliedLevel = -1;
    private Coroutine hitRoutine;
    private Transform shakenTransform;
    private Vector3 restingPosition;
    private SpriteRenderer[] tintedSprites;
    private Color[] restingColors;
    private GameObject hitScreenBorder;
    private CanvasGroup screenBorderGroup;

    public static VillageTownHall Instance { get; private set; }

    private void OnEnable()
    {
        Instance = this;
        VillageManagement.InstanceReady += Bind;
        Bind(VillageManagement.Instance);
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        VillageManagement.InstanceReady -= Bind;
        if (manager != null) manager.SaveDataChanged -= RefreshLevel;
        manager = null;
        StopHitFeedback();
    }

    private void Bind(VillageManagement next)
    {
        if (manager != null) manager.SaveDataChanged -= RefreshLevel;
        manager = next;
        appliedLevel = -1;
        if (manager == null) return;
        manager.SaveDataChanged += RefreshLevel;
        RefreshLevel(manager.SaveData);
    }

    private void RefreshLevel(VillageManagement.VillageSaveData _)
    {
        if (manager == null || appliedLevel == manager.VillageLevel) return;
        appliedLevel = manager.VillageLevel;
        foreach (LevelActivation entry in levelActivations)
        {
            if (entry == null || entry.linkedPrefab == null || entry.linkedPrefab == gameObject) continue;
            bool active = entry.requiredVillageLevel == appliedLevel;
            if (entry.instance == null)
            {
                if (entry.linkedPrefab.scene.IsValid()) entry.instance = entry.linkedPrefab;
                else if (active) entry.instance = Instantiate(entry.linkedPrefab, visualRoot != null ? visualRoot : transform, false);
            }
            if (entry.instance != null && entry.instance.activeSelf != active) entry.instance.SetActive(active);
        }
    }

    public void TakeDamage(int scoreDamage)
    {
        VillageManagement.EnsureInstance().RemoveVillageScore(Mathf.Max(0, scoreDamage));
        StopHitFeedback();
        if (isActiveAndEnabled) hitRoutine = StartCoroutine(HitFeedback());
    }

    public void ShowCustomerSatisfaction(CustomerBlood customer)
    {
        if (customer != null && customerEmojiPrefab != null)
            customer.ShowSatisfactionEmoji(customerEmojiPrefab);
    }

    private IEnumerator HitFeedback()
    {
        EnsureHitScreenBorder();
        screenBorderGroup.alpha = 1f;
        hitScreenBorder.SetActive(true);
        shakenTransform = visualRoot != null ? visualRoot : transform;
        restingPosition = shakenTransform.localPosition;
        tintedSprites = shakenTransform.GetComponentsInChildren<SpriteRenderer>(true);
        restingColors = new Color[tintedSprites.Length];
        for (int i = 0; i < tintedSprites.Length; i++)
        {
            restingColors[i] = tintedSprites[i].color;
            tintedSprites[i].color = new Color(1f, 0f, 0f, restingColors[i].a);
        }
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, hitDuration);
        while (elapsed < duration)
        {
            shakenTransform.localPosition = restingPosition + Vector3.right * (Mathf.Sin(elapsed * shakeFrequency * Mathf.PI * 2f) * shakeDistance);
            yield return null;
            elapsed += Time.deltaTime;
        }
        RestoreHallVisuals();
        elapsed = 0f;
        float fadeDuration = Mathf.Max(0.01f, screenBorderFadeDuration);
        while (elapsed < fadeDuration)
        {
            screenBorderGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
            elapsed += Time.deltaTime;
        }
        RestoreHitFeedback();
        hitRoutine = null;
    }

    private void StopHitFeedback()
    {
        if (hitRoutine != null) StopCoroutine(hitRoutine);
        hitRoutine = null;
        RestoreHitFeedback();
    }

    private void RestoreHitFeedback()
    {
        if (screenBorderGroup != null) screenBorderGroup.alpha = 0f;
        if (hitScreenBorder != null) hitScreenBorder.SetActive(false);
        RestoreHallVisuals();
    }

    private void RestoreHallVisuals()
    {
        if (shakenTransform != null) shakenTransform.localPosition = restingPosition;
        if (tintedSprites != null)
            for (int i = 0; i < tintedSprites.Length; i++)
                if (tintedSprites[i] != null) tintedSprites[i].color = restingColors[i];
        shakenTransform = null;
        tintedSprites = null;
        restingColors = null;
    }

    private void EnsureHitScreenBorder()
    {
        if (hitScreenBorder != null)
        {
            if (screenBorderGroup == null)
            {
                screenBorderGroup = hitScreenBorder.GetComponent<CanvasGroup>();
                if (screenBorderGroup == null) screenBorderGroup = hitScreenBorder.AddComponent<CanvasGroup>();
                screenBorderGroup.blocksRaycasts = false;
                screenBorderGroup.interactable = false;
            }
            return;
        }
        hitScreenBorder = new GameObject("TownHallHitScreenBorder", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        hitScreenBorder.transform.SetParent(transform, false);
        screenBorderGroup = hitScreenBorder.GetComponent<CanvasGroup>();
        screenBorderGroup.blocksRaycasts = false;
        screenBorderGroup.interactable = false;
        Canvas canvas = hitScreenBorder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        CanvasScaler scaler = hitScreenBorder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        var edge = new GameObject("RoundedGradientBorder", typeof(RectTransform), typeof(CanvasRenderer), typeof(VillageHitScreenBorder));
        edge.transform.SetParent(hitScreenBorder.transform, false);
        RectTransform rect = (RectTransform)edge.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        VillageHitScreenBorder border = edge.GetComponent<VillageHitScreenBorder>();
        border.color = new Color(1f, 0f, 0f, 0.7f);
        border.raycastTarget = false;
    }
}
