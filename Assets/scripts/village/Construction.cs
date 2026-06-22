using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Construction : MonoBehaviour
{
    [SerializeField] private Image timeBarFill;
    [SerializeField] private GameObject level1Visual;
    [SerializeField] private GameObject level2Visual;

    private Coroutine routine;

    public void Begin(float duration, int targetLevel, Action onCompleted)
    {
        if (routine != null)
            StopCoroutine(routine);

        if (level1Visual != null)
            level1Visual.SetActive(targetLevel <= 1);
        if (level2Visual != null)
            level2Visual.SetActive(targetLevel >= 2);

        routine = StartCoroutine(Run(duration, onCompleted));
    }

    private IEnumerator Run(float duration, Action onCompleted)
    {
        duration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (timeBarFill != null)
                timeBarFill.fillAmount = Mathf.Clamp01(elapsed / duration);

            yield return null;
        }

        onCompleted?.Invoke();
        Destroy(gameObject);
    }
}
