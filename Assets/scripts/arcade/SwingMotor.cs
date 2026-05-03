using UnityEngine;

public class SwingMotor : MonoBehaviour
{
    public Rigidbody2D rb;
    public float initialTorque = 200f; // 시작 회전 힘

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        rb.AddTorque(initialTorque); // 시작할 때 힘 줘서 흔들리게 함
    }
}
