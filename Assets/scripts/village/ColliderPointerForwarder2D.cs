using UnityEngine;

public class ColliderPointerForwarder2D : MonoBehaviour
{
    private void OnMouseDown()
    {
        if (Tycoon.DeliveryRuntime.BlocksWorldInput) return;
        GetComponentInParent<IColliderPointerTarget>()?.HandleColliderPointerDown();
    }

    private void OnMouseUp()
    {
        GetComponentInParent<IColliderPointerTarget>()?.HandleColliderPointerUp();
    }

    private void OnMouseUpAsButton()
    {
        if (Tycoon.DeliveryRuntime.BlocksWorldInput) return;
        GetComponentInParent<IColliderPointerTarget>()?.HandleColliderPointerUpAsButton();
    }
}
