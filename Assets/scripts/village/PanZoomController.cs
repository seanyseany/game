using UnityEngine;

public class PanZoomController : MonoBehaviour
{
    [SerializeField] private Transform contentRoot;
    [SerializeField] private Transform startBound;
    [SerializeField] private Transform endBound;
    [SerializeField] private float minZoom = 1f;
    [SerializeField] private float maxZoom = 1.5f;
    [SerializeField] private float zoomSpeed = 0.01f;
    [SerializeField] private float dragSpeed = 0.01f;

    private Vector3 lastSinglePointerPosition;
    private bool dragging;

    private void Awake()
    {
        if (contentRoot == null)
            contentRoot = transform;
    }

    private void Update()
    {
        HandlePinchZoom();
        HandleMouseScrollZoom();
        HandleSingleDrag();
        ClampTransform();
    }

    private void HandlePinchZoom()
    {
        if (Input.touchCount < 2)
            return;

        Touch first = Input.GetTouch(0);
        Touch second = Input.GetTouch(1);

        Vector2 prevFirst = first.position - first.deltaPosition;
        Vector2 prevSecond = second.position - second.deltaPosition;

        float prevDistance = Vector2.Distance(prevFirst, prevSecond);
        float currentDistance = Vector2.Distance(first.position, second.position);
        float delta = (currentDistance - prevDistance) * zoomSpeed * 0.01f;
        ApplyZoom(delta);
    }

    private void HandleMouseScrollZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.001f)
            return;

        ApplyZoom(scroll * zoomSpeed);
    }

    private void HandleSingleDrag()
    {
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
                lastSinglePointerPosition = touch.position;
            else if (touch.phase == TouchPhase.Moved)
            {
                Vector3 delta = touch.position - (Vector2)lastSinglePointerPosition;
                MoveContent(delta);
                lastSinglePointerPosition = touch.position;
            }

            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            dragging = true;
            lastSinglePointerPosition = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            dragging = false;
        }
        else if (dragging && Input.GetMouseButton(0))
        {
            Vector3 current = Input.mousePosition;
            Vector3 delta = current - lastSinglePointerPosition;
            MoveContent(delta);
            lastSinglePointerPosition = current;
        }
    }

    private void ApplyZoom(float delta)
    {
        float current = contentRoot.localScale.x;
        float next = Mathf.Clamp(current + delta, minZoom, maxZoom);
        contentRoot.localScale = new Vector3(next, next, 1f);
    }

    private void MoveContent(Vector3 screenDelta)
    {
        Vector3 worldDelta = new Vector3(screenDelta.x * dragSpeed, screenDelta.y * dragSpeed, 0f);
        contentRoot.position += worldDelta;
    }

    private void ClampTransform()
    {
        if (startBound == null || endBound == null || contentRoot == null)
            return;

        float minX = Mathf.Min(startBound.position.x, endBound.position.x);
        float maxX = Mathf.Max(startBound.position.x, endBound.position.x);
        float minY = Mathf.Min(startBound.position.y, endBound.position.y);
        float maxY = Mathf.Max(startBound.position.y, endBound.position.y);

        Vector3 position = contentRoot.position;
        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.y = Mathf.Clamp(position.y, minY, maxY);
        contentRoot.position = position;
    }
}
