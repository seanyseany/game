using UnityEngine;

public class Escalator : MonoBehaviour, IReinitializable
{
    [Header("Explosion Sprites")]
    public Sprite[] breakSprites;       // Inspector에서 순서대로 3개 넣기
    public float spriteInterval = 0.2f; // 스프라이트 간 전환 속도

    [Header("Pooling")]
    public bool usePool = true;
    [Tooltip("ObjectPool 태그. 비워두면 Destroy 대신 SetActive(false)로 재사용")]
    public string poolTag = "";

    private SpriteRenderer sr;
    private Collider2D col;
    private Rigidbody2D rb;
    private bool isBreaking = false;
    private Sprite intactSprite;
    private Coroutine breakRoutine;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 initialLocalScale;
    private Color initialColor;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        initialLocalScale = transform.localScale;
        if (sr != null)
        {
            intactSprite = sr.sprite;
            initialColor = sr.color;
        }
    }

    private void OnEnable()
    {
        GameData.OnRageStart += HandleRageStart;
        Reinit();
    }

    private void OnDisable()
    {
        GameData.OnRageStart -= HandleRageStart;

        if (breakRoutine != null)
        {
            StopCoroutine(breakRoutine);
            breakRoutine = null;
        }
    }

    public void Reinit()
    {
        isBreaking = false;

        if (sr != null)
        {
            sr.enabled = true;
            if (intactSprite != null)
                sr.sprite = intactSprite;
            sr.color = initialColor;
        }

        if (col != null)
            col.enabled = true;

        if (rb != null)
        {
            if (rb.bodyType != RigidbodyType2D.Static)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            rb.simulated = true;
        }

        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;
        transform.localScale = initialLocalScale;
        breakRoutine = null;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isBreaking && CanBreakBy(other))
            breakRoutine = StartCoroutine(BreakAndDestroy());
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null) return;
        if (!isBreaking && CanBreakBy(collision.collider))
            breakRoutine = StartCoroutine(BreakAndDestroy());
    }

    private void HandleRageStart()
    {
        if (!isActiveAndEnabled || isBreaking)
            return;

        breakRoutine = StartCoroutine(BreakAndDestroy());
    }

    private bool CanBreakBy(Collider2D other)
    {
        if (other == null) return false;

        // 플레이어 기본 근접 히트박스
        if (other.GetComponent<Hitbox>() != null) return true;
        // 폭탄 히트박스
        if (other.GetComponent<BombHitBox>() != null) return true;
        // 레이지 공격체
        if (other.GetComponent<ZigzagLightning>() != null) return true;
        if (other.GetComponent<ProjectileBall>() != null) return true;

        return false;
    }

    private System.Collections.IEnumerator BreakAndDestroy()
    {
        if (isBreaking) yield break;
        isBreaking = true;

        // ✅ 바로 충돌/물리 작용 제거
        if (col != null) col.enabled = false;
        if (rb != null) rb.simulated = false;

        // 순서대로 스프라이트 교체
        if (breakSprites.Length > 0)
        {
            for (int i = 0; i < breakSprites.Length; i++)
            {
                sr.sprite = breakSprites[i];
                yield return new WaitForSeconds(spriteInterval);
            }
        }

        // 최종 제거(풀 반환 또는 비활성화 재사용)
        breakRoutine = null;
        ReturnSelf();
    }

    private void ReturnSelf()
    {
        if (IsPhaseOwnedEscalator())
        {
            gameObject.SetActive(false);
            return;
        }

        if (usePool && ObjectPool.Instance != null && !string.IsNullOrEmpty(poolTag) && ObjectPool.Instance.HasPool(poolTag))
            ObjectPool.Instance.ReturnToPool(poolTag, gameObject);
        else
            gameObject.SetActive(false);
    }

    private bool IsPhaseOwnedEscalator()
    {
        return GetComponentInParent<PhaseLayoutSnapshot>(true) != null;
    }
}
