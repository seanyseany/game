using UnityEngine;

public class trainbody : MonoBehaviour, IReinitializable
{
    [Header("Y Movement Range")]
    public float minYOffset = -0.5f;
    public float maxYOffset = 0.5f;

    [Header("Y Movement Speed")]
    public float moveSpeed = 1f;
    public bool useAbsoluteMultiplier = true;

    private Vector3 baseLocalPosition;
    private bool basePositionCaptured;
    private bool movingUp = true;

    void Awake()
    {
        CaptureBasePositionIfNeeded();
        Reinit();
    }

    void OnEnable()
    {
        Reinit();
    }

    public void Reinit()
    {
        CaptureBasePositionIfNeeded();

        float clampedMin = Mathf.Min(minYOffset, maxYOffset);

        transform.localPosition = new Vector3(
            baseLocalPosition.x,
            baseLocalPosition.y + clampedMin,
            baseLocalPosition.z);

        movingUp = true;
    }

    void Update()
    {
        float mult = 1f;
        if (GameData.Instance != null)
            mult = GameData.Instance.GetStageSpeedMult();

        if (useAbsoluteMultiplier)
            mult = Mathf.Abs(mult);

        float speed = moveSpeed * mult;
        if (speed <= 0f)
            return;

        float clampedMin = Mathf.Min(minYOffset, maxYOffset);
        float clampedMax = Mathf.Max(minYOffset, maxYOffset);

        float targetOffset = movingUp ? clampedMax : clampedMin;
        float currentOffset = transform.localPosition.y - baseLocalPosition.y;
        float nextOffset = Mathf.MoveTowards(currentOffset, targetOffset, speed * Time.deltaTime);

        transform.localPosition = new Vector3(
            baseLocalPosition.x,
            baseLocalPosition.y + nextOffset,
            baseLocalPosition.z);

        if (Mathf.Approximately(nextOffset, targetOffset))
            movingUp = !movingUp;
    }

    private void CaptureBasePositionIfNeeded()
    {
        if (basePositionCaptured)
            return;

        baseLocalPosition = transform.localPosition;
        basePositionCaptured = true;
    }
}
