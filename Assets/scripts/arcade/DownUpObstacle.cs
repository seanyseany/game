using UnityEngine;

public class DownUpObstacle : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveDistance = 2.5f;   // 이동 거리
    public float moveSpeed = 2f;        // 이동 속도
    public float startDelay = 0f;       // 작동 시작 딜레이 시간

    [Header("References")]
    public Transform verticalObstacle;  // VerticalObstacle 연결

    private Vector3 startLocalPos;
    private float moveTime;
    private float delayTimer;
    private ObstacleRageMover rageMover;

    private void OnEnable()
    {
        moveTime = 0f;
        delayTimer = 0f;

        if (verticalObstacle == null)
        {
            return;
        }

        rageMover = verticalObstacle.GetComponent<ObstacleRageMover>();
        startLocalPos = verticalObstacle.localPosition;
    }

    private void Update()
    {
        if (verticalObstacle == null)
        {
            return;
        }

        if (delayTimer < Mathf.Max(0f, startDelay))
        {
            delayTimer += Time.deltaTime;
            return;
        }

        moveTime += Time.deltaTime * Mathf.Max(0f, moveSpeed);

        // Keep the same range and cycle duration, easing to zero speed at each turn.
        float distance = Mathf.Max(0f, moveDistance);
        float progress = distance > 0f
            ? Mathf.PingPong(moveTime, distance * 2f) / (distance * 2f)
            : 0f;
        float offset = Mathf.Lerp(distance, -distance, Mathf.SmoothStep(0f, 1f, progress));
        Vector3 localOffset = new Vector3(0f, offset, 0f);

        if (rageMover != null)
            rageMover.SetExternalLocalOffset(localOffset);
        else
            verticalObstacle.localPosition = startLocalPos + localOffset;
    }
}
