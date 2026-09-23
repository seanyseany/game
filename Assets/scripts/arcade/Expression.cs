using UnityEngine;

// Place Start before End along the player's path through a phase.
[DisallowMultipleComponent]
public class Expression : MonoBehaviour
{
    [SerializeField] private ArcadePopup[] emojiPrefabs;

    private Player trackedPlayer;
    private ArcadeFeedback trackedFeedback;
    private int startRevision;
    private bool completed;

    private void OnEnable()
    {
        trackedPlayer = null;
        trackedFeedback = null;
        completed = false;
    }

    public void EnterStart(Player player)
    {
        // Multiple player colliders must not restart a damaged attempt.
        if (completed || trackedPlayer != null || player == null || player.lives <= 0) return;
        ArcadeFeedback feedback = player.GetComponent<ArcadeFeedback>();
        if (feedback == null) return;
        trackedPlayer = player;
        trackedFeedback = feedback;
        startRevision = feedback.LifeLossRevision;
    }

    public void EnterEnd(Player player)
    {
        if (completed || player == null || player != trackedPlayer) return;
        completed = true;
        if (trackedFeedback != null && player.lives > 0 &&
            trackedFeedback.LifeLossRevision == startRevision)
            trackedFeedback.ShowRandomEmoji(emojiPrefabs, true);
    }
}
