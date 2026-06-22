using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Blood : MonoBehaviour
{
    [Header("Bounce Move")]
    public float moveSpeed = 4f;
    public float bounceForce = 5f;
    public string floorTag = "floor";

    [Header("Suction")]
    public float suctionSpeed = 12f;
    public float suctionArriveDistance = 0.12f;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private readonly List<Collider2D> ignoredPlayerColliders = new List<Collider2D>(8);
    private bool isBeingSucked = false;
    private bool gateShovelTouched = false;
    private Transform suctionTarget;
    private Collider2D ignoredSuctionCollider;
    private Action<Blood> suctionComplete;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        isBeingSucked = false;
        gateShovelTouched = false;
        suctionTarget = null;
        ignoredSuctionCollider = null;
        suctionComplete = null;

        if (bodyCollider != null)
            bodyCollider.enabled = true;

        RefreshIgnoredPlayerCollisions();

        if (rb != null)
        {
            float stageMult = GameData.Instance != null ? GameData.Instance.GetStageSpeedMult() : 1f;
            rb.linearVelocity = new Vector2(-Mathf.Abs(moveSpeed) * stageMult, bounceForce);
            rb.angularVelocity = 0f;
        }
    }

    private void OnDisable()
    {
        isBeingSucked = false;
        suctionTarget = null;
        RestoreIgnoredSuctionCollision();
        suctionComplete = null;

        if (bodyCollider != null)
            bodyCollider.enabled = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    private void FixedUpdate()
    {
        if (rb == null)
            return;

        if (isBeingSucked)
        {
            UpdateSuctionMovement();
            return;
        }

        float stageMult = GameData.Instance != null ? GameData.Instance.GetStageSpeedMult() : 1f;
        Vector2 velocity = rb.linearVelocity;
        velocity.x = -Mathf.Abs(moveSpeed) * stageMult;
        rb.linearVelocity = velocity;
    }

    public void BeginShovelSuction(Transform target, Collider2D shovelCollider, Action<Blood> onComplete)
    {
        if (target == null || rb == null || bodyCollider == null)
            return;

        isBeingSucked = true;
        suctionTarget = target;
        SetIgnoredSuctionCollider(shovelCollider);
        suctionComplete = onComplete;
        rb.linearVelocity = Vector2.zero;
        bodyCollider.enabled = false;
    }

    public bool TryMarkGateShovelTouch()
    {
        if (gateShovelTouched)
            return false;

        gateShovelTouched = true;
        return true;
    }

    public void ResetGateShovelTouch()
    {
        gateShovelTouched = false;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (TryHandleGateShovelContact(collision.collider))
            return;

        TryBounceFromFloor(collision);
        IgnoreIfUnsupportedCollider(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (TryHandleGateShovelContact(collision.collider))
            return;

        TryBounceFromFloor(collision);
        IgnoreIfUnsupportedCollider(collision.collider);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        TryHandleGateShovelExit(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (TryIgnorePlayerCollision(other))
            return;

        TryHandleGateShovelContact(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (TryIgnorePlayerCollision(other))
            return;

        TryHandleGateShovelContact(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        TryHandleGateShovelExit(other);
    }

    private void TryBounceFromFloor(Collision2D collision)
    {
        if (isBeingSucked || collision.collider == null)
            return;

        if (!collision.collider.CompareTag(floorTag))
            return;

        if (rb.linearVelocity.y > 0.05f)
            return;

        Vector2 velocity = rb.linearVelocity;
        float stageMult = GameData.Instance != null ? GameData.Instance.GetStageSpeedMult() : 1f;
        velocity.x = -Mathf.Abs(moveSpeed) * stageMult;
        velocity.y = bounceForce;
        rb.linearVelocity = velocity;
    }

    private void UpdateSuctionMovement()
    {
        if (suctionTarget == null)
        {
            ReturnSelf();
            return;
        }

        Vector2 current = rb.position;
        Vector2 target = suctionTarget.position;
        Vector2 toTarget = target - current;

        if (toTarget.sqrMagnitude <= suctionArriveDistance * suctionArriveDistance)
        {
            Action<Blood> callback = suctionComplete;
            isBeingSucked = false;
            suctionTarget = null;
            RestoreIgnoredSuctionCollision();
            suctionComplete = null;
            bodyCollider.enabled = true;
            callback?.Invoke(this);
            ReturnSelf();
            return;
        }

        float moveStep = suctionSpeed * Time.fixedDeltaTime;
        if (toTarget.magnitude <= moveStep + suctionArriveDistance)
        {
            rb.position = target;
            Action<Blood> callback = suctionComplete;
            isBeingSucked = false;
            suctionTarget = null;
            RestoreIgnoredSuctionCollision();
            suctionComplete = null;
            bodyCollider.enabled = true;
            callback?.Invoke(this);
            ReturnSelf();
            return;
        }

        rb.MovePosition(Vector2.MoveTowards(current, target, moveStep));
    }

    private void SetIgnoredSuctionCollider(Collider2D shovelCollider)
    {
        RestoreIgnoredSuctionCollision();

        if (bodyCollider == null || shovelCollider == null)
            return;

        ignoredSuctionCollider = shovelCollider;
        Physics2D.IgnoreCollision(bodyCollider, ignoredSuctionCollider, true);
    }

    private void RestoreIgnoredSuctionCollision()
    {
        if (bodyCollider == null || ignoredSuctionCollider == null)
        {
            ignoredSuctionCollider = null;
            return;
        }

        Physics2D.IgnoreCollision(bodyCollider, ignoredSuctionCollider, false);
        ignoredSuctionCollider = null;
    }

    private bool TryHandleGateShovelContact(Collider2D other)
    {
        GateShovel gateShovel = GetGateShovel(other);
        if (gateShovel == null)
            return false;

        gateShovel.RegisterBloodInstance(this);
        return true;
    }

    private void TryHandleGateShovelExit(Collider2D other)
    {
        GateShovel gateShovel = GetGateShovel(other);
        if (gateShovel == null)
            return;

        gateShovel.UnregisterBloodInstance(this);
    }

    private GateShovel GetGateShovel(Collider2D other)
    {
        if (other == null)
            return null;

        GateShovel gateShovel = other.GetComponent<GateShovel>();
        if (gateShovel != null)
            return gateShovel;

        return other.GetComponentInParent<GateShovel>();
    }

    private void RefreshIgnoredPlayerCollisions()
    {
        ignoredPlayerColliders.Clear();

        if (bodyCollider == null)
            return;

        Player player = FindObjectOfType<Player>();
        if (player == null)
            return;

        Collider2D[] playerColliders = player.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < playerColliders.Length; i++)
        {
            Collider2D playerCollider = playerColliders[i];
            if (playerCollider == null)
                continue;

            Physics2D.IgnoreCollision(bodyCollider, playerCollider, true);
            AddIgnoredPlayerCollider(playerCollider);
        }
    }

    private bool TryIgnorePlayerCollision(Collider2D other)
    {
        if (bodyCollider == null || other == null)
            return false;

        if (!IsPlayerCollider(other))
            return false;

        Physics2D.IgnoreCollision(bodyCollider, other, true);
        AddIgnoredPlayerCollider(other);
        return true;
    }

    private void AddIgnoredPlayerCollider(Collider2D other)
    {
        if (other == null || ignoredPlayerColliders.Contains(other))
            return;

        ignoredPlayerColliders.Add(other);
    }

    private static bool IsPlayerCollider(Collider2D other)
    {
        if (other == null)
            return false;

        if (other.CompareTag("player"))
            return true;

        if (other.GetComponent<Player>() != null)
            return true;

        return other.GetComponentInParent<Player>() != null;
    }

    private void IgnoreIfUnsupportedCollider(Collider2D other)
    {
        if (bodyCollider == null || other == null)
            return;

        if (TryIgnorePlayerCollision(other))
            return;

        if (other.GetComponent<Blood>() != null)
            return;

        if (other.CompareTag(floorTag))
            return;

        if (GetGateShovel(other) != null)
            return;

        Physics2D.IgnoreCollision(bodyCollider, other, true);
    }

    private void ReturnSelf()
    {
        if (ObjectPool.Instance != null && ObjectPool.Instance.HasPool("Blood"))
            ObjectPool.Instance.ReturnToPool("Blood", gameObject);
        else
            Destroy(gameObject);
    }
}
