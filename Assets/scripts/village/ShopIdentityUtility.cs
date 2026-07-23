using UnityEngine;

public static class ShopIdentityUtility
{
    public static string GetStableId(string explicitId, Object context)
    {
        if (!string.IsNullOrWhiteSpace(explicitId))
            return explicitId.Trim();

        string rawName = context != null ? context.name : string.Empty;
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        string trimmed = rawName.Trim();
        int end = trimmed.Length - 1;
        while (end >= 0 && char.IsDigit(trimmed[end]))
            end--;

        while (end >= 0 && (trimmed[end] == ' ' || trimmed[end] == '_' || trimmed[end] == '-'))
            end--;

        string fallback = end >= 0 ? trimmed.Substring(0, end + 1).Trim() : trimmed;
        return string.IsNullOrWhiteSpace(fallback) ? trimmed : fallback;
    }
}
