using UnityEngine;
using System.Collections.Generic;

public class PhaseLayoutSnapshot : MonoBehaviour
{
    [System.Serializable]
    public struct Entry
    {
        public Transform t;
        public Vector3 localPos;
        public Quaternion localRot;
        public Vector3 localScale;
        public bool activeSelf;
    }

    [SerializeField] private List<Entry> entries;

    public void EnsureCaptured()
    {
        if (entries != null && entries.Count > 0) return;
        Capture();
    }

    public void Capture()
    {
        entries = new List<Entry>(64);
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            entries.Add(new Entry
            {
                t = t,
                localPos = t.localPosition,
                localRot = t.localRotation,
                localScale = t.localScale,
                activeSelf = t.gameObject.activeSelf
            });
        }
    }

    // ⚠️ 성능 위해: 스폰 시점이 아니라 '반환 시점'에서만 호출
    public void Restore()
    {
        if (entries == null) return;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!e.t) continue;

            if (e.t.gameObject.activeSelf != e.activeSelf)
                e.t.gameObject.SetActive(e.activeSelf);

            e.t.localPosition = e.localPos;
            e.t.localRotation = e.localRot;
            e.t.localScale    = e.localScale;

            var rb2d = e.t.GetComponent<Rigidbody2D>();
            if (rb2d)
            {
                if (rb2d.bodyType != RigidbodyType2D.Static)
                {
                    rb2d.linearVelocity = Vector2.zero;
                    rb2d.angularVelocity = 0f;
                }
            }
        }
    }
    
}
