using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class VillageHitScreenBorder : MaskableGraphic
{
    [SerializeField, Min(0f)] private float borderWidth = 80f;
    [SerializeField, Min(0f)] private float cornerRadius = 96f;

    private const int CornerSegments = 24;
    private const int GradientSteps = 12;
    private const int PointsPerRing = 4 * (CornerSegments + 1);

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        Rect rect = GetPixelAdjustedRect();
        float halfSize = Mathf.Min(rect.width, rect.height) * 0.5f;
        float width = Mathf.Min(borderWidth, halfSize);
        if (width <= 0f || color.a <= 0f) return;
        float radius = Mathf.Clamp(cornerRadius, 0f, halfSize);

        // Concentric rounded contours make a hollow border. Vertex alpha fades
        // continuously inward; no geometry covers the transparent screen center.
        for (int ring = 0; ring <= GradientSteps; ring++)
        {
            float progress = ring / (float)GradientSteps;
            float inset = width * progress;
            float ringRadius = Mathf.Max(0f, radius - inset);
            Color tint = color;
            tint.a *= (1f - progress) * (1f - progress);

            for (int corner = 0; corner < 4; corner++)
            {
                bool right = corner < 2;
                bool top = corner == 1 || corner == 2;
                Vector2 center = new Vector2(
                    right ? rect.xMax - inset - ringRadius : rect.xMin + inset + ringRadius,
                    top ? rect.yMax - inset - ringRadius : rect.yMin + inset + ringRadius);
                for (int step = 0; step <= CornerSegments; step++)
                {
                    float angle = (-90f + corner * 90f + 90f * step / CornerSegments) * Mathf.Deg2Rad;
                    Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;
                    vertices.AddVert(point, tint, Vector2.zero);
                }
            }

            if (ring == 0) continue;
            int outer = (ring - 1) * PointsPerRing;
            int inner = ring * PointsPerRing;
            for (int point = 0; point < PointsPerRing; point++)
            {
                int next = (point + 1) % PointsPerRing;
                vertices.AddTriangle(outer + point, outer + next, inner + point);
                vertices.AddTriangle(outer + next, inner + next, inner + point);
            }
        }
    }
}
