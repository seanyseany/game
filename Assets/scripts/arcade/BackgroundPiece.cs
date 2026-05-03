using UnityEngine;

public class BackgroundPiece : MonoBehaviour
{
    [Header("Scroll Settings")]
    public float baseSpeed = 2f;     // 기본 스크롤 속도 (px/sec)
    public float leftX = -28f;       // 파괴 기준 위치

    /// <summary>
    /// 외부에서 세팅할 수 있게 유지 (스포너에서 호출)
    /// </summary>
    public void Setup(float scrollSpeed, float leftLimitX)
    {
        baseSpeed = scrollSpeed;
        leftX = leftLimitX;
    }

    void Update()
    {
        // ✅ 스테이지 전체 속도 배수 가져오기 (분노, 충돌 등 반영됨)
        float mult = (GameData.Instance != null) ? GameData.Instance.GetStageSpeedMult() : 1f;

        // 배경 이동
        float move = baseSpeed * mult * Time.deltaTime;
        transform.position += Vector3.left * move;

        // 왼쪽 화면 밖으로 나가면 삭제
        if (transform.position.x <= leftX)
        {
            Destroy(gameObject);
        }
    }
}
