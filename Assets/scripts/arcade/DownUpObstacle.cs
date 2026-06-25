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

        // 시작할 때 반대 방향에서 움직이도록 offset에 -1 곱하기
        float offset = -(Mathf.PingPong(moveTime, moveDistance * 2f) - moveDistance);
        Vector3 localOffset = new Vector3(0f, offset, 0f);

        if (rageMover != null)
            rageMover.SetExternalLocalOffset(localOffset);
        else
            verticalObstacle.localPosition = startLocalPos + localOffset;
    }
}
