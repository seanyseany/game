using UnityEngine;
using UnityEngine.UI;

public static class BuildingListUiFactory
{
    public static Button CreateButton(Transform parent, Font font)
    {
        GameObject buttonObject = new GameObject("PurchaseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.2f, 0.26f, 0.34f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.28f, 0.36f, 0.46f, 1f);
        colors.pressedColor = new Color(0.16f, 0.2f, 0.26f, 1f);
        colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.7f);
        button.colors = colors;

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 112f;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 8f);
        labelRect.offsetMax = new Vector2(-16f, -8f);

        Text label = labelObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = 26;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;

        return button;
    }
}
