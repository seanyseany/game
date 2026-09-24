using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class ArcadeScoreDisplay : MonoBehaviour
{
    private enum DigitLayoutMode
    {
        SpriteWidth,
        FixedSpacing
    }

    [Header("Digit Sprites (0-9)")]
    [SerializeField] private Sprite[] digitSprites = new Sprite[10];

    [Header("Unit Sprite")]
    [SerializeField] private Sprite unitSprite;
    [Min(0f)] [SerializeField] private float unitGap = 2f;
    [SerializeField] private Vector2 unitOffset = Vector2.zero;

    [Header("Layout")]
    [SerializeField] private DigitLayoutMode layoutMode = DigitLayoutMode.SpriteWidth;
    [Min(0f)] [SerializeField] private float visualGap = 2f;
    [Min(0f)] [SerializeField] private float digitSpacing = 32f;
    [Tooltip("숫자별 추가 간격입니다. 1의 간격을 좁히려면 Element 1에 음수 값을 넣으세요.")]
    [SerializeField] private float[] digitSpacingAdjustments = new float[10];

    private readonly List<Image> digitImages = new List<Image>();
    private Image unitImage;
    private int displayedScore = -1;
    private float displayedSpacing = -1f;
    private float displayedVisualGap = -1f;
    private DigitLayoutMode displayedLayoutMode;
    private int displayedAdjustmentHash;
    private Sprite displayedUnitSprite;
    private float displayedUnitGap = -1f;
    private Vector2 displayedUnitOffset = new Vector2(float.NaN, float.NaN);

    private void OnEnable()
    {
        displayedScore = -1;
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (GameData.Instance == null)
            return;

        int score = GameData.Instance.GetRunScore();
        int adjustmentHash = GetAdjustmentHash();
        if (score == displayedScore
            && layoutMode == displayedLayoutMode
            && Mathf.Approximately(visualGap, displayedVisualGap)
            && Mathf.Approximately(digitSpacing, displayedSpacing)
            && adjustmentHash == displayedAdjustmentHash
            && unitSprite == displayedUnitSprite
            && Mathf.Approximately(unitGap, displayedUnitGap)
            && unitOffset == displayedUnitOffset)
            return;

        displayedScore = score;
        displayedLayoutMode = layoutMode;
        displayedVisualGap = visualGap;
        displayedSpacing = digitSpacing;
        displayedAdjustmentHash = adjustmentHash;
        displayedUnitSprite = unitSprite;
        displayedUnitGap = unitGap;
        displayedUnitOffset = unitOffset;
        DrawNumber(score);
    }

    private void DrawNumber(int value)
    {
        string number = Mathf.Max(0, value).ToString();
        EnsureImageCount(number.Length);
        float rightEdge = PrepareUnitImage();

        for (int i = 0; i < digitImages.Count; i++)
        {
            bool used = i < number.Length;
            Image image = digitImages[i];
            image.gameObject.SetActive(used);
            if (!used)
                continue;

            int digit = number[number.Length - 1 - i] - '0';
            image.sprite = digitSprites != null && digit < digitSprites.Length
                ? digitSprites[digit]
                : null;
            image.SetNativeSize();
        }

        for (int i = 0; i < number.Length; i++)
        {
            Image image = digitImages[i];
            image.rectTransform.anchoredPosition = new Vector2(rightEdge, 0f);

            int digit = number[number.Length - 1 - i] - '0';
            float advance = layoutMode == DigitLayoutMode.SpriteWidth
                ? image.rectTransform.rect.width + visualGap
                : digitSpacing;
            if (digitSpacingAdjustments != null && digit < digitSpacingAdjustments.Length)
                advance += digitSpacingAdjustments[digit];

            rightEdge -= Mathf.Max(0f, advance);
        }
    }

    private float PrepareUnitImage()
    {
        if (unitSprite == null)
        {
            if (unitImage != null)
                unitImage.gameObject.SetActive(false);
            return 0f;
        }

        if (unitImage == null)
        {
            GameObject unitObject = CreateImageObject("Unit");
            unitImage = unitObject.GetComponent<Image>();
        }

        unitImage.gameObject.SetActive(true);
        unitImage.sprite = unitSprite;
        unitImage.SetNativeSize();
        unitImage.rectTransform.anchoredPosition = unitOffset;
        return -(unitImage.rectTransform.rect.width + unitGap);
    }

    private int GetAdjustmentHash()
    {
        if (digitSpacingAdjustments == null)
            return 0;

        unchecked
        {
            int hash = 17;
            for (int i = 0; i < digitSpacingAdjustments.Length; i++)
                hash = hash * 31 + digitSpacingAdjustments[i].GetHashCode();
            return hash;
        }
    }

    private void EnsureImageCount(int requiredCount)
    {
        while (digitImages.Count < requiredCount)
        {
            GameObject digitObject = CreateImageObject($"Digit {digitImages.Count}");
            digitImages.Add(digitObject.GetComponent<Image>());
        }
    }

    private GameObject CreateImageObject(string objectName)
    {
        GameObject imageObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

        RectTransform imageTransform = imageObject.GetComponent<RectTransform>();
        imageTransform.SetParent(transform, false);
        imageTransform.anchorMin = new Vector2(1f, 0.5f);
        imageTransform.anchorMax = new Vector2(1f, 0.5f);
        imageTransform.pivot = new Vector2(1f, 0.5f);
        imageTransform.localScale = Vector3.one;

        Image image = imageObject.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        return imageObject;
    }
}
