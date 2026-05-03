using UnityEngine;

[RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))]
public class PhaseDetector : MonoBehaviour
{
    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.simulated = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ;
    }
}
