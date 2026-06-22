using UnityEngine;

public class SwingMotor : MonoBehaviour, IReinitializable
{
    public Rigidbody2D rb;
    public float initialTorque = 200f; // 시작 회전 힘

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        Reinit();
    }

    public void Reinit()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (rb == null) return;

        rb.angularVelocity = 0f;
        rb.WakeUp();
        rb.AddTorque(initialTorque, ForceMode2D.Impulse);
    }
}
