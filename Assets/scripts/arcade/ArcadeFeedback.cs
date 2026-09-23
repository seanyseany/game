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

    // A revision, rather than remaining lives, also detects damage followed by healing.
    public int LifeLossRevision { get; private set; }

    private Transform popupRoot;
    private readonly List<Vector3> pendingKillPositions = new List<Vector3>(8);
    private KillPopupType nextKillPopup = KillPopupType.Good;
    private double nextKillPopupDeadline = double.NegativeInfinity;

    private static ArcadeFeedback Current => Player.Instance != null
        ? Player.Instance.GetComponent<ArcadeFeedback>() : null;

    public static void ShowBoom(Vector3 position)
    {
        ArcadeFeedback feedback = Current;
        if (feedback != null && feedback.isActiveAndEnabled)
            feedback.Spawn(feedback.boomPrefab, position);
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
        if (showEmoji)
            ShowRandomEmoji(damageEmojiPrefabs, true);
    }

    public void ShowRandomEmoji(ArcadePopup[] prefabs, bool followPlayer)
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
        Spawn(selected, transform.position, followPlayer ? transform : null);
    }

    private bool Spawn(ArcadePopup prefab, Vector3 position, Transform followTarget = null)
    {
        if (prefab == null) return false;
        if (popupRoot == null)
        {
            GameObject root = new GameObject("Arcade Feedback Popups");
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            popupRoot = root.transform;
        }
        ArcadePopup popup = Instantiate(prefab, position, Quaternion.identity, popupRoot);
        popup.Play(position, followTarget);
        return true;
    }

    public void ResetFeedback()
    {
        LifeLossRevision++;
        pendingKillPositions.Clear();
        nextKillPopup = KillPopupType.Good;
        nextKillPopupDeadline = double.NegativeInfinity;
        if (popupRoot != null)
        {
            popupRoot.gameObject.SetActive(false);
            Destroy(popupRoot.gameObject);
            popupRoot = null;
        }
    }
}
