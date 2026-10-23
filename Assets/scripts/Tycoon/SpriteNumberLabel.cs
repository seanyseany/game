using UnityEngine;
using UnityEngine.UI;

namespace Tycoon
{
    [AddComponentMenu("")]
    public sealed class SpriteNumberLabel : MonoBehaviour
    {
        private static readonly int[] SegmentMasks = { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };
        private static Sprite whiteSprite;
        private Sprite[] digits;
        private Sprite slash;
        private Sprite question;
        private float scale = 1f;
        private bool useUi;
        private readonly Sprite[] configuredDigits = new Sprite[10];
        private bool configured;
        public string Value { get; private set; } = string.Empty;

        public static GameObject Create(Transform anchor, Sprite[] digits, float scale, Sprite slash = null, Sprite question = null)
        {
            bool ui = anchor.GetComponentInParent<Canvas>() != null;
            var root = ui ? new GameObject("NumberSprites", typeof(RectTransform)) : new GameObject("NumberSprites");
            root.transform.SetParent(anchor, false);
            // A position may have been a Text UI in the previous setup. Only the sprites should render now.
            Text legacy = anchor.GetComponent<Text>();
            if (legacy != null) legacy.enabled = false;
            TMPro.TMP_Text tmp = anchor.GetComponent<TMPro.TMP_Text>();
            if (tmp != null) tmp.enabled = false;
            var label = root.AddComponent<SpriteNumberLabel>();
            label.hideFlags = HideFlags.HideInInspector;
            label.useUi = ui;
            label.Configure(digits, scale, slash, question);
            return root;
        }

        public void Configure(Sprite[] digitSprites, float numberScale, Sprite slashSprite = null, Sprite questionSprite = null)
        {
            numberScale = Mathf.Max(0.01f, numberScale);
            bool changed = !configured || scale != numberScale || slash != slashSprite || question != questionSprite;
            for (int i = 0; i < configuredDigits.Length; i++)
            {
                Sprite next = digitSprites != null && i < digitSprites.Length ? digitSprites[i] : null;
                if (configuredDigits[i] != next) changed = true;
                configuredDigits[i] = next;
            }
            digits = digitSprites;
            scale = numberScale;
            slash = slashSprite;
            question = questionSprite;
            configured = true;
            if (changed) Rebuild();
        }

        public void SetValue(string value)
        {
            value = value ?? string.Empty;
            if (Value == value) return;
            Value = value;
            Rebuild();
        }

        private Sprite GetSprite(char character)
        {
            if (character >= '0' && character <= '9')
            {
                int index = character - '0';
                return digits != null && index < digits.Length ? digits[index] : null;
            }
            return character == '/' ? slash : character == '?' ? question : null;
        }

        private float Width(char character)
        {
            Sprite sprite = GetSprite(character);
            if (sprite != null)
                return useUi ? sprite.rect.width / Mathf.Max(1f, sprite.rect.height)
                    : sprite.bounds.size.x / Mathf.Max(0.0001f, sprite.bounds.size.y);
            return character == '/' ? 0.45f : 0.65f;
        }

        private float CellWidth(char character, float digitWidth)
        {
            return character >= '0' && character <= '9' ? digitWidth : Width(character);
        }

        private void Rebuild()
        {
            // The hierarchy survives script reloads even when managed caches do not.
            for (int i = transform.childCount - 1; i >= 0; i--)
                DeliveryView.Remove(transform.GetChild(i).gameObject);
            float height = (useUi ? 40f : 0.45f) * scale;
            float gap = height * 0.08f;
            // Use every registered digit so changing 111 to 000 cannot change the spacing.
            float digitWidth = 0f;
            for (char digit = '0'; digit <= '9'; digit++) digitWidth = Mathf.Max(digitWidth, Width(digit));
            float width = Mathf.Max(0, Value.Length - 1) * gap;
            foreach (char character in Value) width += CellWidth(character, digitWidth) * height;
            float x = -width * 0.5f;
            foreach (char character in Value)
            {
                float glyphWidth = Width(character) * height;
                float cellWidth = CellWidth(character, digitWidth) * height;
                var glyph = useUi ? new GameObject(character.ToString(), typeof(RectTransform)) : new GameObject(character.ToString());
                glyph.transform.SetParent(transform, false);
                Vector2 position = new Vector2(x + cellWidth * 0.5f, 0f);
                if (useUi)
                {
                    var rect = (RectTransform)glyph.transform;
                    rect.sizeDelta = new Vector2(cellWidth, height);
                    rect.anchoredPosition = position;
                }
                else glyph.transform.localPosition = position;
                Sprite sprite = GetSprite(character);
                if (sprite != null) AddImage(glyph.transform, sprite, Vector2.zero, new Vector2(glyphWidth, height), Color.white);
                else DrawFallback(glyph.transform, character, height);
                x += cellWidth + gap;
            }
            if (transform is RectTransform rootRect) rootRect.sizeDelta = new Vector2(width, height);
        }

        private void AddImage(Transform parent, Sprite sprite, Vector2 position, Vector2 size, Color color, float angle = 0f)
        {
            var part = useUi ? new GameObject("Sprite", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                : new GameObject("Sprite", typeof(SpriteRenderer));
            part.transform.SetParent(parent, false);
            part.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            if (useUi)
            {
                var rect = (RectTransform)part.transform;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
                Image image = part.GetComponent<Image>();
                image.sprite = sprite;
                image.color = color;
                image.raycastTarget = false;
            }
            else
            {
                SpriteRenderer renderer = part.GetComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = color;
                renderer.sortingOrder = 1500;
                Vector3 sizeInUnits = sprite.bounds.size;
                Vector3 localScale = new Vector3(size.x / Mathf.Max(0.0001f, sizeInUnits.x), size.y / Mathf.Max(0.0001f, sizeInUnits.y), 1f);
                part.transform.localScale = localScale;
                // Center even sprites with a noncentral pivot.
                part.transform.localPosition = (Vector3)position - part.transform.localRotation * Vector3.Scale(sprite.bounds.center, localScale);
            }
        }

        private void DrawFallback(Transform parent, char character, float height)
        {
            if (whiteSprite == null) whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f, 1f);
            Color color = new Color(0.2f, 0.12f, 0.08f);
            float centerOffset = character == '1' ? -0.26f : character == '3' || character == '7' ? -0.025f : 0f;
            void Stroke(float x, float y, float width, float length, float angle = 0f)
                => AddImage(parent, whiteSprite, new Vector2(x + centerOffset, y) * height, new Vector2(width, length) * height, color, angle);
            if (character >= '0' && character <= '9')
            {
                int mask = SegmentMasks[character - '0'];
                if ((mask & 1) != 0) Stroke(0f, 0.43f, 0.5f, 0.08f);
                if ((mask & 2) != 0) Stroke(0.26f, 0.215f, 0.08f, 0.35f);
                if ((mask & 4) != 0) Stroke(0.26f, -0.215f, 0.08f, 0.35f);
                if ((mask & 8) != 0) Stroke(0f, -0.43f, 0.5f, 0.08f);
                if ((mask & 16) != 0) Stroke(-0.26f, -0.215f, 0.08f, 0.35f);
                if ((mask & 32) != 0) Stroke(-0.26f, 0.215f, 0.08f, 0.35f);
                if ((mask & 64) != 0) Stroke(0f, 0f, 0.5f, 0.08f);
            }
            else if (character == '/') Stroke(0f, 0f, 0.08f, 0.95f, -25f);
            else
            {
                Stroke(0f, 0.42f, 0.35f, 0.08f);
                Stroke(-0.2f, 0.31f, 0.08f, 0.14f);
                Stroke(0.2f, 0.27f, 0.08f, 0.25f);
                Stroke(0.1f, 0.05f, 0.08f, 0.28f, -45f);
                Stroke(0f, -0.16f, 0.08f, 0.15f);
                Stroke(0f, -0.4f, 0.1f, 0.1f);
            }
        }
    }
}
