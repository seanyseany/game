using UnityEngine;

public class Mover : MonoBehaviour
{
    public float baseSpeed = 1f;

    // 🔹 기본 속도 백업 (prefab 기준)
    [HideInInspector] public float defaultBaseSpeed;

    void Awake()
    {
        defaultBaseSpeed = baseSpeed;
    }

    void Update()
    {
        float mult = GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;
        transform.position += Vector3.left * baseSpeed * mult * Time.deltaTime;
    }

    public void ApplyPhaseMultiplier(float phaseMult)
    {
        baseSpeed = defaultBaseSpeed * phaseMult;
    }
}
