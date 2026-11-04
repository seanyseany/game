using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class VillageTownHall : MonoBehaviour
{
    [Serializable]
    public class LevelActivation
    {
        [Range(1, 9)] public int requiredVillageLevel = 1;
        [Tooltip("씬 오브젝트는 직접 활성화하고, 프리팹 에셋은 Visual Root 아래에 생성합니다. 달성한 레벨의 연결은 계속 활성화됩니다.")]
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
    [Header("Hit Feedback")]
    [SerializeField, Min(0.01f)] private float hitDuration = 0.2f;
    [SerializeField, Min(0f)] private float shakeDistance = 0.08f;
    [SerializeField, Min(0f)] private float shakeFrequency = 18f;

    private VillageManagement manager;
    private int appliedLevel = -1;
    private Coroutine hitRoutine;
    private Transform shakenTransform;
    private Vector3 restingPosition;
    private SpriteRenderer[] tintedSprites;
    private Color[] restingColors;

    private void OnEnable()
    {
        VillageManagement.InstanceReady += Bind;
        Bind(VillageManagement.Instance);
    }

    private void OnDisable()
    {
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
            bool active = manager.IsVillageLevelUnlocked(entry.requiredVillageLevel);
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

    private IEnumerator HitFeedback()
    {
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
        if (shakenTransform != null) shakenTransform.localPosition = restingPosition;
        if (tintedSprites != null)
            for (int i = 0; i < tintedSprites.Length; i++)
                if (tintedSprites[i] != null) tintedSprites[i].color = restingColors[i];
        shakenTransform = null;
        tintedSprites = null;
        restingColors = null;
    }
}
