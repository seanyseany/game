using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class ArcadeFeedback : MonoBehaviour
{
    private enum KillPopupType
    {
        Good,
        Perfect,
        MultiKill
    }

    [Header("Text prefabs")]
    [SerializeField] private ArcadePopup boomPrefab;
    [SerializeField] private ArcadePopup goodPrefab;
    [SerializeField] private ArcadePopup perfectPrefab;
    [SerializeField] private ArcadePopup multiKillPrefab;
    [SerializeField, Min(0f)] private float killChainWindow = 1f;

    [Header("Life lost emoji prefabs")]
    [SerializeField] private ArcadePopup[] damageEmojiPrefabs;

    [Header("Rage and mini boss feedback")]
    [SerializeField] private ArcadePopup rageModePrefab;
    [SerializeField] private ArcadePopup[] miniBossPositiveEmojiPrefabs;
    [SerializeField] private ArcadePopup miniBossNegativeEmojiPrefab;
    [SerializeField] private ArcadePopup miniBossDeathEmojiPrefab;

    // A revision, rather than remaining lives, also detects damage followed by healing.
    public int LifeLossRevision { get; private set; }

    private Transform popupRoot;
    private ArcadePopup activePlayerEmoji;
    private readonly Dictionary<int, ArcadePopup> activeMiniBossEmojis = new Dictionary<int, ArcadePopup>();
    private readonly List<Vector3> pendingKillPositions = new List<Vector3>(8);
    private KillPopupType nextKillPopup = KillPopupType.Good;
    private double nextKillPopupDeadline = double.NegativeInfinity;

    private static ArcadeFeedback Current => Player.Instance != null
        ? Player.Instance.GetComponent<ArcadeFeedback>() : null;

    private void OnEnable()
    {
        GameData.OnRageStart += HandleRageStart;
    }

    private void OnDisable()
    {
        GameData.OnRageStart -= HandleRageStart;
    }

    public static void ShowBoom(Vector3 position)
    {
        ArcadeFeedback feedback = Current;
        if (feedback != null && feedback.isActiveAndEnabled)
            feedback.Spawn(feedback.boomPrefab, position);
    }

    public static void ShowScaledBoom(Vector3 position, float sizeMultiplier)
    {
        ArcadeFeedback feedback = Current;
        if (feedback != null && feedback.isActiveAndEnabled)
            feedback.Spawn(feedback.boomPrefab, position, sizeMultiplier: sizeMultiplier);
    }

    public static void NotifyEnemyKilled(Vector3 enemyPosition)
    {
        ArcadeFeedback feedback = Current;
        if (feedback == null || !feedback.isActiveAndEnabled) return;
        if (GameData.Instance != null && (GameData.Instance.gameOver || GameData.Instance.IsResetting)) return;

        feedback.pendingKillPositions.Add(enemyPosition);
    }

    private void Update()
    {
        if (pendingKillPositions.Count == 0) return;

        // Physics callbacks for simultaneous kills arrive one by one. Process the
        // completed frame as one batch so the left enemy always advances first.
        pendingKillPositions.Sort(CompareKillPositions);
        double now = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < pendingKillPositions.Count; i++)
            ShowNextKillPopup(pendingKillPositions[i], now);

        pendingKillPositions.Clear();
    }

    private static int CompareKillPositions(Vector3 a, Vector3 b)
    {
        int xOrder = a.x.CompareTo(b.x);
        return xOrder != 0 ? xOrder : a.y.CompareTo(b.y);
    }

    private void ShowNextKillPopup(Vector3 enemyPosition, double now)
    {
        if (now > nextKillPopupDeadline)
            nextKillPopup = KillPopupType.Good;

        KillPopupType popupToShow = nextKillPopup;
        ArcadePopup prefab = popupToShow == KillPopupType.Good ? goodPrefab
            : popupToShow == KillPopupType.Perfect ? perfectPrefab
            : multiKillPrefab;

        // Advance only when the selected popup was actually created.
        if (!Spawn(prefab, enemyPosition)) return;

        nextKillPopup = popupToShow == KillPopupType.Good
            ? KillPopupType.Perfect
            : KillPopupType.MultiKill;
        nextKillPopupDeadline = now + killChainWindow;
    }

    public void NotifyLifeLost(bool showEmoji = true)
    {
        LifeLossRevision++;
        QueueNegativeEmojiForActiveMiniBosses();
        RemovePlayerEmoji();
        if (showEmoji)
            ShowRandomEmojiAt(damageEmojiPrefabs, transform.position, transform, true);
    }

    public static void ShowMiniBossAttackResult(Transform miniBoss, bool showNegativeEmoji)
    {
        ArcadeFeedback feedback = Current;
        if (feedback == null || !feedback.isActiveAndEnabled || miniBoss == null) return;

        if (showNegativeEmoji)
        {
            feedback.Spawn(feedback.miniBossNegativeEmojiPrefab, miniBoss.position, miniBoss);
            return;
        }

        feedback.ShowRandomEmojiAt(
            feedback.miniBossPositiveEmojiPrefabs,
            miniBoss.position,
            miniBoss);
    }

    public static void ShowMiniBossDeath(Transform miniBoss)
    {
        ArcadeFeedback feedback = Current;
        if (feedback == null || !feedback.isActiveAndEnabled || miniBoss == null) return;

        feedback.RemoveMiniBossEmoji(miniBoss.GetInstanceID());
        feedback.Spawn(feedback.miniBossDeathEmojiPrefab, miniBoss.position);
    }

    public void ShowRandomEmoji(ArcadePopup[] prefabs, bool followPlayer)
    {
        ShowRandomEmojiAt(prefabs, transform.position, followPlayer ? transform : null);
    }

    private void ShowRandomEmojiAt(
        ArcadePopup[] prefabs,
        Vector3 position,
        Transform followTarget,
        bool isDamageEmoji = false)
    {
        if (!isActiveAndEnabled || prefabs == null || prefabs.Length == 0) return;

        // Ignore unassigned Inspector slots without biasing the remaining choices.
        int validCount = 0;
        ArcadePopup selected = null;
        foreach (ArcadePopup prefab in prefabs)
        {
            if (prefab != null && Random.Range(0, ++validCount) == 0)
                selected = prefab;
        }
        Spawn(selected, position, followTarget, isDamageEmoji);
    }

    private void HandleRageStart()
    {
        Spawn(rageModePrefab, new Vector3(2.4f, 0.5f, 0f));
    }

    private void QueueNegativeEmojiForActiveMiniBosses()
    {
        if (miniBossNegativeEmojiPrefab == null) return;

        MiniBoss[] miniBosses = FindObjectsByType<MiniBoss>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < miniBosses.Length; i++)
        {
            MiniBoss miniBoss = miniBosses[i];
            if (miniBoss == null || !miniBoss.IsFeedbackActive) continue;
            miniBoss.QueuePlayerDamageReaction();
        }
    }

    private bool Spawn(
        ArcadePopup prefab,
        Vector3 position,
        Transform followTarget = null,
        bool isDamageEmoji = false,
        float sizeMultiplier = 1f)
    {
        if (prefab == null) return false;
        if (prefab.IsEmoji && GameData.Instance != null && GameData.Instance.rageMode)
            return false;

        bool isPlayerFollowingEmoji = prefab.IsEmoji && followTarget == transform;
        if (isPlayerFollowingEmoji && activePlayerEmoji != null)
        {
            // Expression zones can overlap or finish close together. Keep the
            // current ordinary emoji; only life-loss feedback may replace it.
            if (!isDamageEmoji)
                return false;

            RemovePlayerEmoji();
        }

        MiniBoss miniBoss = prefab.IsEmoji && followTarget != null
            ? followTarget.GetComponent<MiniBoss>()
            : null;
        int miniBossId = miniBoss != null ? miniBoss.GetInstanceID() : 0;
        if (miniBossId != 0)
            RemoveMiniBossEmoji(miniBossId);

        if (popupRoot == null)
        {
            GameObject root = new GameObject("Arcade Feedback Popups");
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            popupRoot = root.transform;
        }
        ArcadePopup popup = Instantiate(prefab, position, Quaternion.identity, popupRoot);
        popup.Play(position, followTarget, sizeMultiplier);
        if (isPlayerFollowingEmoji)
            activePlayerEmoji = popup;
        if (miniBossId != 0)
            activeMiniBossEmojis[miniBossId] = popup;
        return true;
    }

    private void RemoveMiniBossEmoji(int miniBossId)
    {
        if (!activeMiniBossEmojis.Remove(miniBossId, out ArcadePopup popup)) return;
        RemovePopup(popup);
    }

    private void RemovePlayerEmoji()
    {
        RemovePopup(activePlayerEmoji);
        activePlayerEmoji = null;
    }

    private static void RemovePopup(ArcadePopup popup)
    {
        if (popup == null) return;
        popup.gameObject.SetActive(false);
        Destroy(popup.gameObject);
    }

    public void ResetFeedback()
    {
        LifeLossRevision++;
        pendingKillPositions.Clear();
        nextKillPopup = KillPopupType.Good;
        nextKillPopupDeadline = double.NegativeInfinity;
        activePlayerEmoji = null;
        activeMiniBossEmojis.Clear();
        if (popupRoot != null)
        {
            popupRoot.gameObject.SetActive(false);
            Destroy(popupRoot.gameObject);
            popupRoot = null;
        }
    }
}
