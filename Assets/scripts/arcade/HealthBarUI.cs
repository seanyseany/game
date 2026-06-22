using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthBarUI : MonoBehaviour
{
    [Header("Heart UI")]
    public Image[] hearts; // Heart1~3

    // 체력 하트 갱신
    public void SetHealth(int current)
    {
        int visibleCount = Mathf.Clamp(current, 0, hearts.Length);

        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[hearts.Length - 1 - i] == null)
                continue;

            if (i < visibleCount)
                hearts[hearts.Length - 1 - i].enabled = true;
            else
                hearts[hearts.Length - 1 - i].enabled = false;
        }
    }

    // ✅ 추가 — 모든 하트를 다시 켜주는 리셋용 메서드
    public void ResetBar()
    {
        foreach (var h in hearts)
            if (h != null)
                h.enabled = true;
    }
}
