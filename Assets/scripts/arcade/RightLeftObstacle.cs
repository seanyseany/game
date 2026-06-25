using UnityEngine;

public class RightLeftObstacle : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveDistance = 2.5f;   // 이동 거리
    public float moveSpeed = 2f;        // 이동 속도

    [Header("References")]
    public Transform verticalObstacle;  // VerticalObstacle 연결

    private Vector3 startLocalPos;
    private float moveTime;
    private ObstacleRageMover rageMover;

    private void OnEnable()
    {
        moveTime = 0f;

        if (verticalObstacle == null)
            return;

        rageMover = verticalObstacle.GetComponent<ObstacleRageMover>();
        startLocalPos = verticalObstacle.localPosition;
    }

    private void Update()
    {
        if (verticalObstacle == null) return;

        // 시작할 때 반대 방향에서 움직이도록 offset에 -1 곱하기
        moveTime += Time.deltaTime * Mathf.Max(0f, moveSpeed);
        float offset = -(Mathf.PingPong(moveTime, moveDistance * 2f) - moveDistance);
        Vector3 localOffset = new Vector3(offset, 0f, 0f);

        if (rageMover != null)
            rageMover.SetExternalLocalOffset(localOffset);
        else
            verticalObstacle.localPosition = startLocalPos + localOffset;
    }
}
