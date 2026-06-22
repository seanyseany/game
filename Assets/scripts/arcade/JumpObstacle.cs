using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(EdgeCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class JumpObstacle : MonoBehaviour, IReinitializable
{
    [Header("Jump Boost")]
    [SerializeField] private float jumpVelocityY = 15f;
    [SerializeField] private float jumpInputLockSeconds = 0.5f;

    [Header("Contact")]
    [SerializeField] private bool allowTriggerContact = true;

    private EdgeCollider2D edgeCollider;
    private Rigidbody2D body;
    private readonly HashSet<int> activePlayerContacts = new HashSet<int>();

    private void Awake()
    {
        edgeCollider = GetComponent<EdgeCollider2D>();
        body = GetComponent<Rigidbody2D>();
        EnsureSetup();
    }

    private void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        EnsureSetup();
        activePlayerContacts.Clear();

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = true;
        }
    }

    private void Reset()
    {
        EnsureSetup();
    }

    private void EnsureSetup()
    {
        if (edgeCollider == null)
            edgeCollider = GetComponent<EdgeCollider2D>();

        if (body == null)
            body = GetComponent<Rigidbody2D>();

        if (edgeCollider != null)
            edgeCollider.enabled = true;

        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.simulated = true;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryBoostPlayer(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryBoostPlayer(collision);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        ReleasePlayerContact(collision != null ? collision.collider : null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!allowTriggerContact)
            return;

        TryBoostPlayer(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!allowTriggerContact)
            return;

        TryBoostPlayer(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        ReleasePlayerContact(other);
    }

    private void TryBoostPlayer(Collision2D collision)
    {
        if (collision == null || edgeCollider == null)
            return;

        if (collision.otherCollider != edgeCollider)
            return;

        TryApplyBoost(collision.collider);
    }

    private void TryBoostPlayer(Collider2D other)
    {
        if (other == null || edgeCollider == null)
            return;

        Player player = ExtractPlayer(other);
        if (player == null)
            return;

        Collider2D playerCollider = player.GetComponent<Collider2D>() ?? other;
        if (playerCollider == null || !edgeCollider.IsTouching(playerCollider))
            return;

        TryApplyBoost(playerCollider);
    }

    private void TryApplyBoost(Collider2D other)
    {
        Player player = ExtractPlayer(other);
        if (player == null || player.isDead)
            return;

        int playerId = player.GetInstanceID();
        if (!activePlayerContacts.Add(playerId))
            return;

        player.ApplyJumpObstacleBoost(jumpVelocityY, jumpInputLockSeconds);
    }

    private void ReleasePlayerContact(Collider2D other)
    {
        Player player = ExtractPlayer(other);
        if (player == null)
            return;

        activePlayerContacts.Remove(player.GetInstanceID());
    }

    private Player ExtractPlayer(Collider2D other)
    {
        if (other == null)
            return null;

        return other.GetComponent<Player>() ?? other.GetComponentInParent<Player>();
    }
}
