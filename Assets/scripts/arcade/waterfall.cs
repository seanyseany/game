using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class waterfall : MonoBehaviour, IReinitializable
{
    [Header("Pull")]
    [SerializeField] private float downwardMoveSpeedWhileJumpHeld = 1.5f;
    [SerializeField] private float downwardMoveSpeedWhileJumpReleased = 3f;

    [Header("Hit Effect")]
    [SerializeField] private GameObject waterfallHitPrefab;
    [SerializeField] private Vector3 waterfallHitOffset = new Vector3(0f, 0.4f, 0f);

    private BoxCollider2D boxCollider;
    private Rigidbody2D rb;
    private Player touchingPlayer;
    private Collider2D touchingPlayerCollider;
    private GameObject activeWaterfallHit;

    private void Awake()
    {
        boxCollider = GetComponent<BoxCollider2D>();
        rb = GetComponent<Rigidbody2D>();
        EnsureTriggerSetup();
    }

    private void OnEnable()
    {
        Reinit();
    }

    private void OnDisable()
    {
        ClearContact();
        DespawnWaterfallHit();
    }

    public void Reinit()
    {
        EnsureTriggerSetup();
        ClearContact();
        DespawnWaterfallHit();
    }

    private void EnsureTriggerSetup()
    {
        if (boxCollider == null)
            boxCollider = GetComponent<BoxCollider2D>();

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (boxCollider != null)
        {
            boxCollider.isTrigger = true;
            boxCollider.enabled = true;
        }

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (touchingPlayer == null)
            return;

        if (!IsPlayerContactValid())
        {
            ClearContact();
            DespawnWaterfallHit();
            return;
        }

        if (GameData.Instance != null && GameData.Instance.rageMode)
        {
            DespawnWaterfallHit();
            return;
        }

        MovePlayerDownward();
    }

    private void LateUpdate()
    {
        if (touchingPlayer == null)
        {
            DespawnWaterfallHit();
            return;
        }

        if (!IsPlayerContactValid())
        {
            ClearContact();
            DespawnWaterfallHit();
            return;
        }

        if (GameData.Instance != null && GameData.Instance.rageMode)
        {
            DespawnWaterfallHit();
            return;
        }

        EnsureWaterfallHit();

        if (activeWaterfallHit != null)
            activeWaterfallHit.transform.position = touchingPlayer.transform.position + waterfallHitOffset;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        CachePlayerContact(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        CachePlayerContact(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Player player = ExtractPlayer(other);
        if (player == null || player != touchingPlayer)
            return;

        ClearContact();
        DespawnWaterfallHit();
    }

    private void CachePlayerContact(Collider2D other)
    {
        Player player = ExtractPlayer(other);
        if (player == null)
            return;

        touchingPlayer = player;
        touchingPlayerCollider = player.GetComponent<Collider2D>() ?? other;
        EnsureWaterfallHit();
    }

    private Player ExtractPlayer(Collider2D other)
    {
        if (other == null)
            return null;

        return other.GetComponent<Player>() ?? other.GetComponentInParent<Player>();
    }

    private bool IsPlayerContactValid()
    {
        if (touchingPlayer == null || boxCollider == null)
            return false;

        if (!touchingPlayer.gameObject.activeInHierarchy || touchingPlayer.isDead)
            return false;

        Collider2D playerCollider = touchingPlayerCollider;
        if (playerCollider == null)
            playerCollider = touchingPlayer.GetComponent<Collider2D>();

        if (playerCollider == null || !playerCollider.enabled)
            return false;

        return boxCollider.IsTouching(playerCollider);
    }

    private void MovePlayerDownward()
    {
        if (touchingPlayer == null)
            return;

        Rigidbody2D playerRb = touchingPlayer.rb != null ? touchingPlayer.rb : touchingPlayer.GetComponent<Rigidbody2D>();
        float downwardMoveSpeed = Input.GetKey(KeyCode.Space)
            ? downwardMoveSpeedWhileJumpHeld
            : downwardMoveSpeedWhileJumpReleased;
        Vector2 nextPosition;

        if (playerRb != null && playerRb.simulated)
        {
            nextPosition = playerRb.position + Vector2.down * downwardMoveSpeed * Time.fixedDeltaTime;
            playerRb.MovePosition(nextPosition);
            return;
        }

        nextPosition = (Vector2)touchingPlayer.transform.position + Vector2.down * downwardMoveSpeed * Time.fixedDeltaTime;
        touchingPlayer.transform.position = nextPosition;
    }

    private void EnsureWaterfallHit()
    {
        if (activeWaterfallHit != null || touchingPlayer == null || waterfallHitPrefab == null)
            return;

        Vector3 spawnPos = touchingPlayer.transform.position + waterfallHitOffset;
        activeWaterfallHit = Instantiate(waterfallHitPrefab, spawnPos, Quaternion.identity);

        if (activeWaterfallHit == null)
            return;

        Animator hitAnimator = activeWaterfallHit.GetComponent<Animator>();
        if (hitAnimator != null)
        {
            hitAnimator.Rebind();
            hitAnimator.Update(0f);
        }
    }

    private void DespawnWaterfallHit()
    {
        if (activeWaterfallHit == null)
            return;

        Destroy(activeWaterfallHit);

        activeWaterfallHit = null;
    }

    private void ClearContact()
    {
        touchingPlayer = null;
        touchingPlayerCollider = null;
    }
}
