using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class fishbomb : MonoBehaviour, IReinitializable
{
    [Header("Arc Movement")]
    [SerializeField] private float arcHeight = 1f;

    [Header("Trigger Timing")]
    [SerializeField] private float triggerWorldX = 3f;

    [Header("Explosion")]
    [SerializeField] private string destroyTriggerName = "Destroy";
    [SerializeField] private float expandDuration = 0.3f;
    [SerializeField] private float targetRadius = 3f;
    [SerializeField] private float activeDuration = 1f;
    [SerializeField] private float colliderActiveDuration = 0.1f;
    [SerializeField] private int damage = 1;

    private CircleCollider2D circleCol;
    private Animator animator;

    private float initialRadius;
    private float baseRadius;
    private float previousX;
    private float startX;
    private float baseY;
    private bool triggered = false;
    private bool obstacleSlowApplied = false;
    private readonly HashSet<int> hitPlayers = new HashSet<int>();
    private Coroutine explodeCo;
    private Coroutine obstacleSlowCo;

    private void Awake()
    {
        circleCol = GetComponent<CircleCollider2D>();
        animator = GetComponent<Animator>();

        baseRadius = circleCol.radius;
        initialRadius = baseRadius;
        circleCol.isTrigger = true;
        circleCol.enabled = false;
    }

    private void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        triggered = false;
        obstacleSlowApplied = false;
        previousX = transform.position.x;
        startX = transform.position.x;
        baseY = transform.position.y;
        hitPlayers.Clear();

        if (explodeCo != null)
        {
            StopCoroutine(explodeCo);
            explodeCo = null;
        }

        if (obstacleSlowCo != null)
        {
            StopCoroutine(obstacleSlowCo);
            obstacleSlowCo = null;
        }

        if (circleCol == null)
            circleCol = GetComponent<CircleCollider2D>();

        initialRadius = baseRadius;
        circleCol.radius = initialRadius;
        circleCol.isTrigger = true;
        circleCol.enabled = false;

        if (animator != null && !string.IsNullOrEmpty(destroyTriggerName))
        {
            animator.ResetTrigger(destroyTriggerName);
            animator.Rebind();
            animator.Update(0f);
        }
    }

    private void OnDisable()
    {
        if (explodeCo != null)
        {
            StopCoroutine(explodeCo);
            explodeCo = null;
        }

        if (obstacleSlowCo != null)
        {
            StopCoroutine(obstacleSlowCo);
            obstacleSlowCo = null;
        }
    }

    private void Update()
    {
        float currentX = transform.position.x;
        if (!triggered)
            ApplyArcMovement(currentX);

        if (triggered)
            return;

        if (previousX > triggerWorldX && currentX <= triggerWorldX)
        {
            TriggerExplosion();
        }

        previousX = currentX;
    }

    private void TriggerExplosion()
    {
        if (triggered)
            return;

        triggered = true;

        if (animator != null && !string.IsNullOrEmpty(destroyTriggerName))
            animator.SetTrigger(destroyTriggerName);

        explodeCo = StartCoroutine(CoExplode());
    }

    private IEnumerator CoExplode()
    {
        circleCol.enabled = true;

        float elapsed = 0f;
        while (elapsed < expandDuration)
        {
            elapsed += Time.deltaTime;
            float t = expandDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / expandDuration);
            circleCol.radius = Mathf.Lerp(initialRadius, targetRadius, t);
            yield return null;
        }

        circleCol.radius = targetRadius;
        float colliderDisableDelay = Mathf.Min(colliderActiveDuration, activeDuration);
        yield return new WaitForSeconds(colliderDisableDelay);
        circleCol.enabled = false;
        yield return new WaitForSeconds(Mathf.Max(0f, activeDuration - colliderDisableDelay));

        gameObject.SetActive(false);
    }

    private void ApplyArcMovement(float currentX)
    {
        float totalDistance = startX - triggerWorldX;
        if (Mathf.Abs(totalDistance) <= Mathf.Epsilon)
            return;

        float traveled = startX - currentX;
        float progress = Mathf.Clamp01(traveled / totalDistance);
        float yOffset = 4f * arcHeight * progress * (1f - progress);

        Vector3 pos = transform.position;
        pos.y = baseY + yOffset;
        transform.position = pos;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamagePlayer(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryDamagePlayer(other);
    }

    private void TryDamagePlayer(Collider2D other)
    {
        if (!triggered || !circleCol.enabled || other == null)
            return;

        Player player = other.GetComponent<Player>() ?? other.GetComponentInParent<Player>();
        if (player == null)
            return;

        int playerId = player.GetInstanceID();
        if (hitPlayers.Contains(playerId))
            return;

        hitPlayers.Add(playerId);

        if (GameData.Instance != null && GameData.Instance.selectedPlayerType == 3)
            return;

        if (!player.IsRageModeActive())
        {
            player.TakeDamage(damage);
            ApplyObstacleSlow();
        }
    }

    private void ApplyObstacleSlow()
    {
        if (obstacleSlowApplied || GameData.Instance == null)
            return;

        obstacleSlowApplied = true;
        obstacleSlowCo = StartCoroutine(CoObstacleSlowContact());
    }

    private IEnumerator CoObstacleSlowContact()
    {
        GameData.Instance.BeginObstacleContact();
        yield return null;

        if (GameData.Instance != null)
            GameData.Instance.EndObstacleContact();

        obstacleSlowCo = null;
    }
}
