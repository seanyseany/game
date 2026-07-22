using UnityEngine;
using UnityEngine.UI;

public class ShopPlaceholderUI : ShopSectionUI
{
    [SerializeField] private string sectionTitle = "Section";
    [SerializeField] [TextArea] private string message = "준비 중입니다.";

    private GameObject rootObject;

    public override string SectionTitle => sectionTitle;

    public override void ShowSection(RectTransform contentRoot)
    {
        if (contentRoot == null)
            return;

        if (rootObject == null)
            rootObject = BuildRoot(contentRoot);
        else
            rootObject.transform.SetParent(contentRoot, false);

        rootObject.SetActive(true);
    }

    public override void HideSection()
    {
        if (rootObject != null)
            rootObject.SetActive(false);
    }

    private GameObject BuildRoot(RectTransform parent)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject root = new GameObject($"{sectionTitle}Placeholder", typeof(RectTransform));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = new Vector2(0f, 0f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image background = root.AddComponent<Image>();
        background.color = new Color(0.12f, 0.16f, 0.22f, 0.85f);

        GameObject textObject = new GameObject("Message", typeof(RectTransform));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(root.transform, false);
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(40f, 40f);
        textRect.offsetMax = new Vector2(-40f, -40f);

        Text text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 32;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = message;

        return root;
    }
}
