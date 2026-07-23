using UnityEngine;

public class ColliderPointerForwarder2D : MonoBehaviour
{
    private void OnMouseDown()
    {
        GetComponentInParent<IColliderPointerTarget>()?.HandleColliderPointerDown();
    }

    private void OnMouseUp()
    {
        GetComponentInParent<IColliderPointerTarget>()?.HandleColliderPointerUp();
    }

    private void OnMouseUpAsButton()
    {
        GetComponentInParent<IColliderPointerTarget>()?.HandleColliderPointerUpAsButton();
    }
}
