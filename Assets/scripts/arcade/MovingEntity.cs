using UnityEngine;
using System;

public class MovingEntity : MonoBehaviour
{
    public float baseSpeed = 6f;
    public float despawnX = -20f;
    public float triggerX = 0f;
    public Action<MovingEntity> onCross;

    private float prevX;

    void OnEnable()
    {
        prevX = transform.position.x;
    }

    void Update()
    {
        float mult = GameData.Instance ? GameData.Instance.GetStageSpeedMult() : 1f;
        transform.position += Vector3.left * baseSpeed * mult * Time.deltaTime;

        float x = transform.position.x;

        if (prevX > triggerX && x <= triggerX)
        {
            onCross?.Invoke(this);
            onCross = null;
        }

        if (x <= despawnX)
            Destroy(gameObject);

        prevX = x;
    }
}
