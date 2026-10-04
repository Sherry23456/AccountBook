using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 圆环进度（步骤09 发现页/预算页）：环形扇区自定义 Graphic，0 刻度在正上方、顺时针填充。
/// 底环与进度环各用一枚 RingGraphic 叠放（底环 progress=1 灰色，进度环按剩余比例填黄）。
/// 纯显示组件，与金额禁 float 约定无关（百分比换算在调用侧 long 完成后转 0~1）。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RingGraphic : MaskableGraphic
{
    [Range(0f, 1f)]
    [SerializeField] private float progress = 1f;

    [SerializeField] private float thickness = 24f;

    [SerializeField] private int segments = 96;

    /// <summary>
    /// 填充比例 0~1（写入即重绘）
    /// </summary>
    public float Progress
    {
        get { return progress; }
        set
        {
            progress = Mathf.Clamp01(value);
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// 环厚（像素，Canvas 参考分辨率下）
    /// </summary>
    public float Thickness
    {
        get { return thickness; }
        set
        {
            thickness = Mathf.Max(1f, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        float outer = Mathf.Min(r.width, r.height) * 0.5f;
        float inner = outer - thickness;

        if (inner <= 0f || outer <= 0f)
        {
            return;
        }

        int segCount = Mathf.Clamp(segments, 4, 720);
        int quadCount = Mathf.Max(1, Mathf.CeilToInt(segCount * progress));
        float sweep = progress * Mathf.PI * 2f;

        Vector2 center = r.center;
        Vector2 prevOuter = Point(center, outer, 0f);
        Vector2 prevInner = Point(center, inner, 0f);

        for (int i = 1; i <= quadCount; i++)
        {
            float angle = sweep * i / quadCount;
            Vector2 curOuter = Point(center, outer, angle);
            Vector2 curInner = Point(center, inner, angle);

            // 顺时针 quad：外前 → 外后 → 内后 → 内前
            vh.AddVert(prevOuter, color, Vector2.zero);
            vh.AddVert(curOuter, color, Vector2.zero);
            vh.AddVert(curInner, color, Vector2.zero);
            vh.AddVert(prevInner, color, Vector2.zero);

            int baseIndex = (i - 1) * 4;
            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);

            prevOuter = curOuter;
            prevInner = curInner;
        }
    }

    /// <summary>
    /// angle 为自正上方起顺时针弧度
    /// </summary>
    private static Vector2 Point(Vector2 center, float radius, float angle)
    {
        float rad = Mathf.PI * 0.5f - angle;
        return center + new Vector2(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
    }
}
