using UnityEngine;

public abstract class ShopSectionUI : MonoBehaviour
{
    public abstract string SectionTitle { get; }

    public abstract void ShowSection(RectTransform contentRoot);

    public abstract void HideSection();
}
