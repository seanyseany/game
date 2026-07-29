using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class O2Icon : MonoBehaviour
{
    private const float CollectFadeDuration = 0.7f;
    private const float CollectMoveSpeed = 1.2f;
    private const float FloatYOffset = 0.1f;
    private const float FloatSpeed = 2f;

    [SerializeField] private TMP_Text amountText;
    [SerializeField] private int sortingOrder = 200;

    private Oxygen sourceOxygen;
    private SpriteRenderer spriteRenderer;
    private Collider2D hitCollider;
    private bool mousePressedOnIcon;
    private int activeTouchId = -1;
    private bool isCollecting;
    private Color baseSpriteColor = Color.white;
    private Color baseTextColor = Color.white;
    private Vector3 idleBaseLocalPosition;

    public void Bind(Oxygen oxygen)
    {
        sourceOxygen = oxygen;
        idleBaseLocalPosition = transform.localPosition;
        Refresh();
    }

    private void Awake()
    {
        if (amountText == null)
            amountText = GetComponentInChildren<TMP_Text>(true);

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = sortingOrder;
            baseSpriteColor = spriteRenderer.color;
        }

        if (amountText != null)
            baseTextColor = amountText.color;

        hitCollider = GetComponent<Collider2D>();
        if (hitCollider == null)
            hitCollider = gameObject.AddComponent<CircleCollider2D>();

        hitCollider.isTrigger = true;

        if (hitCollider is CircleCollider2D circleCollider)
            circleCollider.radius = Mathf.Max(circleCollider.radius, 1.1f);
    }

    private void Update()
    {
        if (isCollecting || sourceOxygen == null)
            return;

        ApplyFloatingMotion();
        Refresh();
        HandleTouchInput();
        HandleMouseInput();
    }

    private void Refresh()
    {
        if (isCollecting || amountText == null || sourceOxygen == null)
            return;

        amountText.text = sourceOxygen.StoredOxygen.ToString();
    }

    private void HandleTouchInput()
    {
        if (Input.touchCount <= 0)
        {
            activeTouchId = -1;
            return;
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            bool overIcon = IsScreenPointOverIcon(touch.position);

            if (touch.phase == TouchPhase.Began && overIcon)
            {
                activeTouchId = touch.fingerId;
                continue;
            }

            if (touch.fingerId != activeTouchId)
                continue;

            if (touch.phase == TouchPhase.Ended)
            {
                if (overIcon)
                    Collect();

                activeTouchId = -1;
            }
            else if (touch.phase == TouchPhase.Canceled)
            {
                activeTouchId = -1;
            }
        }
    }

    private void HandleMouseInput()
    {
        if (Input.GetMouseButtonDown(0))
            mousePressedOnIcon = IsScreenPointOverIcon(Input.mousePosition);

        if (!Input.GetMouseButtonUp(0))
            return;

        bool releasedOnIcon = IsScreenPointOverIcon(Input.mousePosition);
        if (mousePressedOnIcon && releasedOnIcon)
            Collect();

        mousePressedOnIcon = false;
    }

    private bool IsScreenPointOverIcon(Vector2 screenPoint)
    {
        if (hitCollider == null)
            return false;

        Camera cameraRef = Camera.main;
        if (cameraRef == null)
            return false;

        Vector3 worldPoint = cameraRef.ScreenToWorldPoint(new Vector3(
            screenPoint.x,
            screenPoint.y,
            Mathf.Abs(cameraRef.transform.position.z - transform.position.z)));

        return hitCollider.OverlapPoint(worldPoint);
    }

    private void Collect()
    {
        if (isCollecting || sourceOxygen == null || sourceOxygen.StoredOxygen <= 0)
            return;

        isCollecting = true;
        activeTouchId = -1;
        mousePressedOnIcon = false;

        sourceOxygen.CollectStoredOxygen(this);

        if (hitCollider != null)
            hitCollider.enabled = false;

        transform.SetParent(null, true);
        StopAllCoroutines();
        StartCoroutine(PlayCollectAnimation());
    }

    private IEnumerator PlayCollectAnimation()
    {
        float elapsed = 0f;
        Vector3 startPosition = transform.position;

        while (elapsed < CollectFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / CollectFadeDuration);

            transform.position = startPosition + Vector3.up * (CollectMoveSpeed * elapsed);
            ApplyAlpha(1f - t);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void ApplyFloatingMotion()
    {
        float offsetY = Mathf.Sin(Time.time * FloatSpeed) * FloatYOffset;
        transform.localPosition = idleBaseLocalPosition + Vector3.up * offsetY;
    }

    private void ApplyAlpha(float alpha)
    {
        if (spriteRenderer != null)
        {
            Color color = baseSpriteColor;
            color.a = alpha;
            spriteRenderer.color = color;
        }

        if (amountText != null)
        {
            Color color = baseTextColor;
            color.a = alpha;
            amountText.color = color;
        }
    }
}
