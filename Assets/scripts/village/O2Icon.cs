using TMPro;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class O2Icon : MonoBehaviour
{
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private int sortingOrder = 200;

    private Oxygen sourceOxygen;
    private SpriteRenderer spriteRenderer;
    private Collider2D hitCollider;
    private bool mousePressedOnIcon;
    private int activeTouchId = -1;

    public void Bind(Oxygen oxygen)
    {
        sourceOxygen = oxygen;
        Refresh();
    }

    private void Awake()
    {
        if (amountText == null)
            amountText = GetComponentInChildren<TMP_Text>(true);

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.sortingOrder = sortingOrder;

        hitCollider = GetComponent<Collider2D>();
        if (hitCollider == null)
            hitCollider = gameObject.AddComponent<CircleCollider2D>();

        hitCollider.isTrigger = true;

        if (hitCollider is CircleCollider2D circleCollider)
            circleCollider.radius = Mathf.Max(circleCollider.radius, 1.1f);
    }

    private void Update()
    {
        if (sourceOxygen == null)
            return;

        Refresh();
        HandleTouchInput();
        HandleMouseInput();
    }

    private void Refresh()
    {
        if (amountText == null || sourceOxygen == null)
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
        if (sourceOxygen == null || sourceOxygen.StoredOxygen <= 0)
            return;

        sourceOxygen.CollectStoredOxygen();
    }
}
