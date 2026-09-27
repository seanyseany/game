using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HitCount : MonoBehaviour
{
    [Header("Digit Sprites (0-9)")]
    [SerializeField] private Sprite[] digitSprites = new Sprite[10];

    [Header("Layout")]
    [Min(0f)] [SerializeField] private float digitGap = 0.05f;
    [Tooltip("투명 여백 보정이 필요한 숫자에만 추가 간격을 입력하세요.")]
    [SerializeField] private float[] digitSpacingAdjustments = new float[10];

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 100;

    private const float GrowDuration = 0.3f;
    private const float HoldDuration = 0.2f;
    private const float FadeDuration = 0.5f;
    private const float RiseDistance = 1f;

    private readonly List<SpriteRenderer> digits = new List<SpriteRenderer>();
    private Vector3 fullScale;
    private Vector3 origin;
    private float elapsed;
    private bool playing;

    private void Awake()
    {
        fullScale = transform.localScale;
        transform.localScale = Vector3.zero;
    }

    public void Play(int value)
    {
        BuildNumber(Mathf.Max(0, value));
        origin = transform.position;
        elapsed = 0f;
        playing = true;
        transform.localScale = Vector3.zero;
        SetAlpha(1f);
    }

    private void Update()
    {
        if (!playing) return;

        elapsed += Time.deltaTime;
        float growProgress = Mathf.Clamp01(elapsed / GrowDuration);
        float fadeProgress = Mathf.Clamp01(
            (elapsed - GrowDuration - HoldDuration) / FadeDuration);

        transform.localScale = fullScale * Mathf.SmoothStep(0f, 1f, growProgress);
        transform.position = origin + Vector3.up * (RiseDistance * fadeProgress);
        SetAlpha(1f - fadeProgress);

        if (elapsed >= GrowDuration + HoldDuration + FadeDuration)
            Destroy(gameObject);
    }

    private void BuildNumber(int value)
    {
        string number = value.ToString();
        float rightEdge = 0f;

        for (int i = number.Length - 1; i >= 0; i--)
        {
            int digit = number[i] - '0';
            Sprite sprite = digitSprites != null && digit < digitSprites.Length
                ? digitSprites[digit]
                : null;

            GameObject digitObject = new GameObject($"Digit {number.Length - 1 - i}");
            digitObject.transform.SetParent(transform, false);

            SpriteRenderer renderer = digitObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            digits.Add(renderer);

            if (sprite == null) continue;

            Bounds bounds = sprite.bounds;
            digitObject.transform.localPosition = new Vector3(
                rightEdge - bounds.max.x,
                -bounds.center.y,
                0f);

            float adjustment = digitSpacingAdjustments != null && digit < digitSpacingAdjustments.Length
                ? digitSpacingAdjustments[digit]
                : 0f;
            rightEdge -= Mathf.Max(0f, bounds.size.x + digitGap + adjustment);
        }
    }

    private void SetAlpha(float alpha)
    {
        for (int i = 0; i < digits.Count; i++)
        {
            if (digits[i] == null) continue;
            Color color = digits[i].color;
            color.a = alpha;
            digits[i].color = color;
        }
    }

    public static int CalculateValue(int bossMaxHp)
    {
        return Mathf.RoundToInt(Mathf.Max(0, bossMaxHp) * GetPlayerDamageMultiplier());
    }

    public static float GetPlayerDamageMultiplier()
    {
        int playerType = GameData.Instance != null ? GameData.Instance.selectedPlayerType : 2;
        switch (playerType)
        {
            case 1: return 2.1f;
            case 2: return 4.9f;
            case 3: return 1.53f;
            case 4: return 5.5f;
            case 5: return 3.5f;
            default: return 1f;
        }
    }
}
