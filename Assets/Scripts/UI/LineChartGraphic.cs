using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 折线图自绘组件（步骤04 §2.4，UGUI 无第三方插件）：OnPopulateMesh 内画基线、均值虚线、
/// 折线段、节点（有数据黄实心圆 / 无数据空心灰圈）。坐标以矩形左下角为原点（pivot=(0,0)，
/// 步骤04 §4），点 y = padding + value/scaleMax × 内高；scaleMax=0（空周期）时点落在基线。
/// 点击按 X 反算最近节点上抛 NodeClicked，气泡文案与定位由 ChartPanelUI 组装。
/// </summary>
public class LineChartGraphic : MaskableGraphic, IPointerClickHandler
{
    private static readonly Color ColorLine = FromHex(0xE0E0E0);
    private static readonly Color ColorNode = FromHex(0xFFD100);
    private static readonly Color ColorNodeEmpty = FromHex(0xCCCCCC);
    private static readonly Color ColorAvgDash = FromHex(0xBBBBBB);
    private static readonly Color ColorBaseline = FromHex(0xE6E6E6);

    private const float LineWidth = 4f;
    private const float NodeRadius = 9f;
    private const float EmptyRingWidth = 3f;
    private const float BaselineHeight = 2f;
    private const float PaddingLeft = 60f;
    private const float PaddingRight = 60f;
    private const float PaddingTop = 96f;
    private const float PaddingBottom = 64f;
    private const int CircleSegments = 24;

    private List<long> points = new List<long>();
    private long avgFen = 0;
    private long scaleMaxFen = 0;
    private float lastRectW = -1f;
    private float lastRectH = -1f;

    /// <summary>
    /// 节点点击通知（参数=节点索引，ChartPanelUI 据此弹气泡）
    /// </summary>
    public event Action<int> NodeClicked;

    /// <summary>
    /// 画布尺寸变化（如进 Play 时游戏视图最大化晚于面板激活）不会自动触发 Graphic 重绘，
    /// 这里每帧比对 rect，变了就置脏重画（步骤04 首轮实测踩坑）
    /// </summary>
    private void Update()
    {
        Rect r = rectTransform.rect;

        if (Mathf.Approximately(r.width, lastRectW) && Mathf.Approximately(r.height, lastRectH))
        {
            return;
        }

        lastRectW = r.width;
        lastRectH = r.height;

        if (lastRectW > 1f && lastRectH > 1f)
        {
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// 当前折线点数（自检用）
    /// </summary>
    public int PointCount
    {
        get { return points != null ? points.Count : 0; }
    }

    /// <summary>
    /// 数据变化后调用：重设折线并置脏重绘
    /// </summary>
    public void SetData(List<long> newPoints, long newAvgFen, long newScaleMaxFen)
    {
        points = newPoints ?? new List<long>();
        avgFen = newAvgFen;
        scaleMaxFen = newScaleMaxFen;
        SetVerticesDirty();
    }

    /// <summary>
    /// 节点在矩形局部坐标（原点=左下）中的位置；布局未完成（宽或高不足 1px）返回 false
    /// </summary>
    public bool TryGetNodeLocalPos(int index, out Vector2 pos)
    {
        pos = Vector2.zero;

        if (points == null || index < 0 || index >= points.Count)
        {
            return false;
        }

        float w = rectTransform.rect.width;
        float h = rectTransform.rect.height;

        if (w < 1f || h < 1f)
        {
            return false;
        }

        pos = GetNodePos(index, w, h);
        return true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        int index;

        if (TryHitNode(eventData, out index) && NodeClicked != null)
        {
            NodeClicked.Invoke(index);
        }
    }

    /// <summary>
    /// 自检/截图用：直接模拟点击某节点（Play 模式无法合成指针事件）
    /// </summary>
    public void SimulateNodeClick(int index)
    {
        if (index >= 0 && index < points.Count && NodeClicked != null)
        {
            NodeClicked.Invoke(index);
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float w = rectTransform.rect.width;
        float h = rectTransform.rect.height;

        if (w < 1f || h < 1f || points == null || points.Count == 0)
        {
            return;
        }

        // 基线
        AddHLine(vh, PaddingLeft, w - PaddingRight, PaddingBottom, BaselineHeight, ColorBaseline);

        // 均值虚线（AvgFen=0 时跳过，除零特判，步骤04 §4）
        if (avgFen > 0 && scaleMaxFen > 0)
        {
            float avgY = PaddingBottom + (float)avgFen / scaleMaxFen * InnerHeight(h);
            AddDashedLine(vh, PaddingLeft, w - PaddingRight, avgY, ColorAvgDash);
        }

        // 折线段
        for (int i = 0; i + 1 < points.Count; i++)
        {
            AddSegment(vh, GetNodePos(i, w, h), GetNodePos(i + 1, w, h), LineWidth, ColorLine);
        }

        // 节点：有数据实心黄圆，无数据空心灰圈
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = GetNodePos(i, w, h);

            if (points[i] > 0)
            {
                AddDisc(vh, p, NodeRadius, ColorNode);
            }
            else
            {
                AddRing(vh, p, NodeRadius, EmptyRingWidth, ColorNodeEmpty);
            }
        }
    }

    private float InnerHeight(float h)
    {
        return h - PaddingTop - PaddingBottom;
    }

    private Vector2 GetNodePos(int index, float w, float h)
    {
        float innerW = w - PaddingLeft - PaddingRight;
        float x = points.Count > 1
            ? PaddingLeft + innerW * index / (points.Count - 1)
            : PaddingLeft + innerW * 0.5f;
        float y = scaleMaxFen > 0
            ? PaddingBottom + (float)points[index] / scaleMaxFen * InnerHeight(h)
            : PaddingBottom;
        return new Vector2(x, y);
    }

    private bool TryHitNode(PointerEventData eventData, out int index)
    {
        index = -1;

        if (points == null || points.Count == 0)
        {
            return false;
        }

        Vector2 local;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out local))
        {
            return false;
        }

        float w = rectTransform.rect.width;

        if (w < 1f)
        {
            return false;
        }

        float innerW = w - PaddingLeft - PaddingRight;
        float colWidth = points.Count > 1 ? innerW / (points.Count - 1) : innerW;
        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < points.Count; i++)
        {
            float innerX = points.Count > 1
                ? PaddingLeft + innerW * i / (points.Count - 1)
                : PaddingLeft + innerW * 0.5f;
            float dist = Mathf.Abs(local.x - innerX);

            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        if (best < 0 || bestDist > colWidth * 0.5f)
        {
            return false;
        }

        index = best;
        return true;
    }

    // ---------- 网格图元（z=0，顶点 UV 对默认 UI 材质无效） ----------

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, color, Vector2.zero);
        vh.AddVert(b, color, Vector2.zero);
        vh.AddVert(c, color, Vector2.zero);
        vh.AddVert(d, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 1, start + 3);
    }

    /// <summary>
    /// 两点间沿线法线扩宽的线段四边形
    /// </summary>
    private static void AddSegment(VertexHelper vh, Vector2 from, Vector2 to, float width, Color color)
    {
        Vector2 dir = (to - from).normalized;
        Vector2 normal = new Vector2(-dir.y, dir.x) * (width * 0.5f);
        AddQuad(vh, from + normal, to + normal, to - normal, from - normal, color);
    }

    private static void AddHLine(VertexHelper vh, float x0, float x1, float y, float height, Color color)
    {
        float half = height * 0.5f;
        AddQuad(vh, new Vector2(x0, y + half), new Vector2(x1, y + half),
            new Vector2(x1, y - half), new Vector2(x0, y - half), color);
    }

    /// <summary>
    /// 横向虚线：短画 18px、间隔 12px，末端不足一画就补一画
    /// </summary>
    private static void AddDashedLine(VertexHelper vh, float x0, float x1, float y, Color color)
    {
        const float dash = 18f;
        const float gap = 12f;
        float x = x0;

        while (x < x1)
        {
            float end = Mathf.Min(x + dash, x1);
            AddHLine(vh, x, end, y, 3f, color);
            x = end + gap;
        }
    }

    private static void AddDisc(VertexHelper vh, Vector2 center, float radius, Color color)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);

        for (int i = 0; i <= CircleSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / CircleSegments;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius), color, Vector2.zero);
        }

        for (int i = 0; i < CircleSegments; i++)
        {
            vh.AddTriangle(start, start + 1 + i, start + 2 + i);
        }
    }

    /// <summary>
    /// 空心圆环：沿圆周逐段画四边形
    /// </summary>
    private static void AddRing(VertexHelper vh, Vector2 center, float radius, float ringWidth, Color color)
    {
        float inner = Mathf.Max(0.5f, radius - ringWidth);

        for (int i = 0; i < CircleSegments; i++)
        {
            float a0 = Mathf.PI * 2f * i / CircleSegments;
            float a1 = Mathf.PI * 2f * (i + 1) / CircleSegments;
            Vector2 out0 = center + new Vector2(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius);
            Vector2 out1 = center + new Vector2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
            Vector2 in0 = center + new Vector2(Mathf.Cos(a0) * inner, Mathf.Sin(a0) * inner);
            Vector2 in1 = center + new Vector2(Mathf.Cos(a1) * inner, Mathf.Sin(a1) * inner);
            AddQuad(vh, in0, out0, out1, in1, color);
        }
    }

    private static Color FromHex(int hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Color(r, g, b, 1f);
    }
}
