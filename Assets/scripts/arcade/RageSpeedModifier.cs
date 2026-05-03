using UnityEngine;

public class RageScrollSpeedModifier : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("분노 상태에서 적용할 속도 배수")]
    public float rageMultiplier = 1.5f;

    private BackgroundScroller scroller;
    private BackgroundScrollerException exceptionScroller;
    private float originalSpeed;

    void Awake()
    {
        scroller = GetComponent<BackgroundScroller>();
        exceptionScroller = GetComponent<BackgroundScrollerException>();

        if (scroller == null && exceptionScroller == null)
        {
            Debug.LogWarning($"{nameof(RageScrollSpeedModifier)} requires BackgroundScroller or BackgroundScrollerException on {name}.", this);
            enabled = false;
            return;
        }

        originalSpeed = GetCurrentScrollSpeed();
    }

    void OnEnable()
    {
        GameData.OnRageStart += HandleRageStart;
        GameData.OnRageEnd += HandleRageEnd;
    }

    void OnDisable()
    {
        GameData.OnRageStart -= HandleRageStart;
        GameData.OnRageEnd -= HandleRageEnd;
    }

    private void HandleRageStart()
    {
        originalSpeed = GetCurrentScrollSpeed();
        SetCurrentScrollSpeed(originalSpeed * rageMultiplier);
    }

    private void HandleRageEnd()
    {
        SetCurrentScrollSpeed(originalSpeed);
    }

    private float GetCurrentScrollSpeed()
    {
        if (scroller != null)
            return scroller.baseScrollSpeed;

        return exceptionScroller.baseScrollSpeed;
    }

    private void SetCurrentScrollSpeed(float speed)
    {
        if (scroller != null)
            scroller.baseScrollSpeed = speed;

        if (exceptionScroller != null)
            exceptionScroller.baseScrollSpeed = speed;
    }
}
