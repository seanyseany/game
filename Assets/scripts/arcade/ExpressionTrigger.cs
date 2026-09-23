using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class ExpressionTrigger : MonoBehaviour
{
    public enum Boundary { Start, End }
    [SerializeField] private Expression expression;
    [SerializeField] private Boundary boundary;

    private void Reset()
    {
        GetComponent<BoxCollider2D>().isTrigger = true;
        expression = GetComponentInParent<Expression>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (expression == null || !expression.isActiveAndEnabled) return;
        // Only the body collider counts; a child attack hitbox must not cross a boundary early.
        Player player = other.GetComponent<Player>();
        if (player == null) return;
        if (boundary == Boundary.Start)
            expression.EnterStart(player);
        else
            expression.EnterEnd(player);
    }
}
