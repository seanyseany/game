#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class VillageHitScreenBorderTests
{
    [TestCase(1080f, 1920f)]
    [TestCase(1920f, 1080f)]
    [TestCase(512f, 512f)]
    public void BorderFadesInwardLeavesCenterClearAndRoundsTheCorners(float width, float height)
    {
        var root = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer));
        Mesh mesh = new Mesh();
        try
        {
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            var border = root.AddComponent<VillageHitScreenBorder>();
            border.color = new Color(1f, 0f, 0f, 0.7f);
            using (var vertices = new VertexHelper())
            {
                typeof(VillageHitScreenBorder).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(VertexHelper) }, null)
                    .Invoke(border, new object[] { vertices });
                vertices.FillMesh(mesh);
            }

            float outer = AlphaAt(mesh, new Vector2(0f, height * 0.5f - 1f));
            float middle = AlphaAt(mesh, new Vector2(0f, height * 0.5f - 35f));
            float inner = AlphaAt(mesh, new Vector2(0f, height * 0.5f - 79f));
            Assert.That(outer, Is.InRange(0.6f, 0.71f));
            Assert.Greater(outer, middle);
            Assert.Greater(middle, inner);
            Assert.Less(inner, 0.01f);
            Assert.AreEqual(0f, AlphaAt(mesh, Vector2.zero), "The center must stay transparent.");
            Assert.AreEqual(0f, AlphaAt(mesh, new Vector2(width * 0.5f - 1f, height * 0.5f - 1f)),
                "The extreme corner must be outside the rounded contour.");
            Assert.Greater(AlphaAt(mesh, new Vector2(width * 0.5f - 30f, height * 0.5f - 30f)), 0.5f,
                "The red border must curve around the corner.");
            Assert.That(AlphaAt(mesh, new Vector2(width * 0.5f - 1f, 0f)), Is.EqualTo(outer).Within(0.005f));
            Assert.That(AlphaAt(mesh, new Vector2(-width * 0.5f + 1f, 0f)), Is.EqualTo(outer).Within(0.005f));
            Assert.That(AlphaAt(mesh, new Vector2(0f, -height * 0.5f + 1f)), Is.EqualTo(outer).Within(0.005f));
        }
        finally
        {
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(root);
        }
    }

    private static float AlphaAt(Mesh mesh, Vector2 point)
    {
        Vector3[] positions = mesh.vertices;
        Color32[] colors = mesh.colors32;
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector2 pa = positions[a], pb = positions[b], pc = positions[c];
            float area = Cross(pb - pa, pc - pa);
            if (Mathf.Abs(area) < 0.00001f) continue;
            float wa = Cross(pb - point, pc - point) / area;
            float wb = Cross(pc - point, pa - point) / area;
            float wc = 1f - wa - wb;
            if (wa < -0.00001f || wb < -0.00001f || wc < -0.00001f) continue;
            return (wa * colors[a].a + wb * colors[b].a + wc * colors[c].a) / 255f;
        }
        return 0f;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
#endif
