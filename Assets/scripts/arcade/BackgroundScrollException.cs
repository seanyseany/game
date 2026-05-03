using UnityEngine;

public class BackgroundScrollerException: MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("베이스 스크롤 속도(px/sec)")]
    public float baseScrollSpeed = 0.2f;

    private MeshRenderer mr;

    void Awake() => mr = GetComponent<MeshRenderer>();

    void Update()
    {
        float mult = 1f;
        float minMult = 0.5f;

        if (GameData.Instance != null)
        {
            float currentMult = GameData.Instance.GetStageSpeedMult();
            float baseMult = GameData.Instance.GetStageSpeedMultIgnoringObstacleSlowdown();
            minMult = Mathf.Max(baseMult * 0.5f, 0.05f);

            if (currentMult < baseMult)
            {
                float softenedMult = Mathf.Lerp(baseMult, currentMult, 0.5f);
                mult = softenedMult;
            }
            else
            {
                mult = currentMult;
            }
        }

        mult = Mathf.Max(mult, minMult);

        // 일반 배경보다 감속 영향을 절반만 받고, 기준 속도의 절반 아래로는 내려가지 않음
        Vector2 ofs = mr.material.mainTextureOffset;
        ofs.x += baseScrollSpeed * mult * Time.deltaTime;
        mr.material.mainTextureOffset = ofs;
    }
}
