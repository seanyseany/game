using UnityEngine;

public class BackgroundScroller : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("베이스 스크롤 속도(px/sec)")]
    public float baseScrollSpeed = 0.2f;

    private MeshRenderer mr;

    void Awake() => mr = GetComponent<MeshRenderer>();

    void Update()
    {
        float mult = GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;

        // ✅ 분노 모드 포함해서 스크롤 속도 변경
        Vector2 ofs = mr.material.mainTextureOffset;
        ofs.x += baseScrollSpeed * mult * Time.deltaTime;
        mr.material.mainTextureOffset = ofs;
    }
}
