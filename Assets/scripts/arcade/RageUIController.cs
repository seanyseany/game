using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class RageUIController : MonoBehaviour
{
    [Header("UI")]
    public Slider rageSlider;   // Rage 게이지 슬라이더

    [Header("Colors")]
    public Color normalColor = new Color(0f, 1f, 1f); // 청록색 (기본)
    public Color rageColor = Color.red;               // 분노 모드일 때 색상

    private Image fillImage;                // Fill Area > Fill 의 Image
    private Outline outlineEffect;

    private int killCount = 0;
    private int killsToActivate = 10;

    private Coroutine rageRoutineCoroutine;

    void Start()
    {
        rageSlider.interactable = false;
        var nav = new Navigation { mode = Navigation.Mode.None };
        rageSlider.navigation = nav;

        // Fill 이미지 캐싱 (Fill Area의 자식 Fill → Image)
        fillImage = rageSlider.fillRect.GetComponentInChildren<Image>();

        // Outline 효과 자동 추가
        if (fillImage != null)
        {
            outlineEffect = fillImage.gameObject.GetComponent<Outline>();
            if (!outlineEffect) outlineEffect = fillImage.gameObject.AddComponent<Outline>();
            outlineEffect.effectColor = new Color(1f, 1f, 1f, 0f);
            outlineEffect.effectDistance = new Vector2(2f, -2f);
        }

        // Kill 게이지 모드로 초기화
        ResetToKillsMode();
    }

    // === 적 처치 시 호출됨 ===
    public void AddKill()
    {
        if (GameData.Instance.rageMode) return; // Rage 중에는 무시

        killCount++;
        rageSlider.value = killCount;

        if (killCount >= killsToActivate)
        {
            // 자동 Rage 모드 발동
            float duration = GameData.Instance.rageDuration;
            GameData.Instance.ActivateRageMode(duration);

            // UI 카운트다운 시작
            if (rageRoutineCoroutine != null) StopCoroutine(rageRoutineCoroutine);
            rageRoutineCoroutine = StartCoroutine(RageRoutine(duration));
        }
    }

    // === Rage 모드 카운트다운 ===
    IEnumerator RageRoutine(float duration)
    {
        killCount = 0;
        rageSlider.maxValue = duration;
        rageSlider.value = duration;

        UpdateFillColor(rageColor);

        float endTime = Time.time + duration;
        while (Time.time < endTime)
        {
            rageSlider.value = endTime - Time.time;
            yield return null;
        }

        // Rage 끝 → Kill 모드로 복귀
        ResetToKillsMode();
    }

    // === Kill 게이지 모드로 초기화 ===
    private void ResetToKillsMode()
    {
        killCount = 0;
        rageSlider.maxValue = killsToActivate;
        rageSlider.value = 0;
        UpdateFillColor(normalColor);

        if (rageRoutineCoroutine != null)
        {
            StopCoroutine(rageRoutineCoroutine);
            rageRoutineCoroutine = null;
        }

        if (outlineEffect != null)
        {
            outlineEffect.effectColor = new Color(1f, 1f, 1f, 0f);
        }
    }

    // Fill 색상 변경
    private void UpdateFillColor(Color c)
    {
        if (fillImage != null)
            fillImage.color = c;
    }

    // UI 리셋 (게임 재시작 시 호출용)
    public void ResetRageUI()
    {
        ResetToKillsMode();
    }

    public void BeginRageCountdown(float duration)
    {
        if (rageRoutineCoroutine != null)
            StopCoroutine(rageRoutineCoroutine);
        rageRoutineCoroutine = StartCoroutine(RageRoutine(duration));
    }

}
