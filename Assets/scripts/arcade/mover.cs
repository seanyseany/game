using UnityEngine;

public class Mover : MonoBehaviour
{
    public float baseSpeed = 1f;
    [HideInInspector] public bool applyStageSpeedMultiplier = true;

    // 🔹 기본 속도 백업 (prefab 기준)
    [HideInInspector] public float defaultBaseSpeed;

    void Awake()
    {
        defaultBaseSpeed = baseSpeed;
    }

    void Update()
    {
        float mult = applyStageSpeedMultiplier && GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;
        transform.position += Vector3.left * baseSpeed * mult * Time.deltaTime;
    }

    public void ApplyPhaseMultiplier(float phaseMult)
    {
        baseSpeed = defaultBaseSpeed * phaseMult;
    }
}
