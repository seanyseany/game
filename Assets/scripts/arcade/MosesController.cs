using System;
using System.Collections;
using UnityEngine;

public class MosesController : MonoBehaviour
{
    [Header("Animator")]
    public Animator anim;
    public string idleStateName = "Idle";     // 너 Animator state 이름에 맞게
    public string appearTrigger = "Appear";   // Trigger 파라미터
    public string vanishTrigger = "Vanish";   // 없으면 비워도 됨

    private Vector3 originPos;
    private Coroutine moveCo;

    void Awake()
    {
        if (!anim) anim = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        // ✅ 게임 시작하자마자 자동 재생 방지
        if (anim)
        {
            anim.enabled = false; // 이동 끝나기 전엔 애니 비활성
        }
    }

    public void SetOrigin(Vector3 pos)
    {
        originPos = pos;
    }

    public void SnapTo(Vector3 pos)
    {
        transform.position = pos;
    }

    public void MoveTo(Vector3 target, float time, Action onDone = null)
    {
        if (moveCo != null) StopCoroutine(moveCo);
        moveCo = StartCoroutine(CoMove(target, time, onDone));
    }

    public void ReturnToOrigin(float time, Action onDone = null)
    {
        if (moveCo != null) StopCoroutine(moveCo);
        moveCo = StartCoroutine(CoMove(originPos, time, onDone));
    }

    IEnumerator CoMove(Vector3 target, float time, Action onDone)
    {
        Vector3 start = transform.position;

        time = Mathf.Max(0.0001f, time);
        float t = 0f;

        while (t < time)
        {
            float k = t / time;
            transform.position = Vector3.Lerp(start, target, k);
            t += Time.deltaTime;
            yield return null;
        }

        transform.position = target;
        moveCo = null;
        onDone?.Invoke();
    }

    public void PlayAppear()
    {
        if (!anim) return;

        anim.enabled = true;
        if (!string.IsNullOrEmpty(idleStateName))
            anim.Play(idleStateName, 0, 0f);

        if (!string.IsNullOrEmpty(appearTrigger))
            anim.SetTrigger(appearTrigger);
    }

    public void PlayVanish()
    {
        if (!anim) return;

        anim.enabled = true;
        if (!string.IsNullOrEmpty(vanishTrigger))
            anim.SetTrigger(vanishTrigger);
    }
}
