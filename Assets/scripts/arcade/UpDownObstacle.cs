using UnityEngine;

public class UpDownObstacle : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveDistance = 2.5f;   // 이동 거리
    public float moveSpeed = 2f;        // 이동 속도

    [Header("References")]
    public Transform verticalObstacle;  // VerticalObstacle 연결

    private Vector3 startLocalPos;

    void Start()
    {
        if (verticalObstacle != null)
            startLocalPos = verticalObstacle.localPosition;
    }

    void Update()
    {
        if (verticalObstacle == null) return;

        float offset = Mathf.PingPong(Time.time * moveSpeed, moveDistance * 2) - moveDistance;
        verticalObstacle.localPosition = startLocalPos + new Vector3(0, offset, 0);
    }
}
