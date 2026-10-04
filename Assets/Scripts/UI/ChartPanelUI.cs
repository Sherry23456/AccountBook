using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 图表页控制器（步骤04）：支出/收入两态按钮 + 周/月/年 Toggle 组 + 周期条（月档为当年 1 月..当前月，
/// 周/年档为最近 6 期；左旧右新）+ 汇总行（总额/均值）+ 自绘折线图（点节点弹当期 Top3 气泡）+ 分类排行榜。
/// 导出/导入按钮本步置灰（interactable=false），步骤 05/06 启用并把当前档位+周期传给弹窗。
/// 刷新链：档位/周期/类型任一变化 → DateRangeUtil 算区间 → GetRecordsInRange →
/// ChartCalculator → 折线/汇总/排行榜全量重建（子项 DestroyImmediate，避开 Destroy 同帧幽灵行）。
/// </summary>
public class ChartPanelUI : MonoBehaviour
{
    private const int StripPeriodCount = 6;
    private const int BubbleTopCount = 3;

    private static readonly Color ColorToggleTextOn = Color.white;
    private static readonly Color ColorToggleTextOff = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColorXLabel = new Color32(0x44, 0x44, 0x44, 0xFF);

    [Header("Dependencies")]
    [SerializeField] private AccountManager accountManager;
    [SerializeField] private CategoryIconProvider iconProvider;
    [SerializeField] private TMP_FontAsset labelFont;   // 运行时动态生成的 X 轴标签用

    [Header("Top Bar")]
    [SerializeField] private Button btnType;
    [SerializeField] private TextMeshProUGUI btnTypeLabel;

    [Header("Range Toggles")]
    [SerializeField] private Toggle toggleWeek;
    [SerializeField] private Toggle toggleMonth;
    [SerializeField] private Toggle toggleYear;
    [SerializeField] private TextMeshProUGUI toggleWeekLabel;
    [SerializeField] private TextMeshProUGUI toggleMonthLabel;
    [SerializeField] private TextMeshProUGUI toggleYearLabel;
    [SerializeField] private Image toggleWeekSelectedBg;
    [SerializeField] private Image toggleMonthSelectedBg;
    [SerializeField] private Image toggleYearSelectedBg;

    [Header("Period Strip")]
    [SerializeField] private RectTransform periodViewport;
    [SerializeField] private RectTransform periodContent;
    [SerializeField] private PeriodStripButton periodButtonPrefab;

    [Header("Summary Row")]
    [SerializeField] private TextMeshProUGUI txtTotalCaption;
    [SerializeField] private TextMeshProUGUI txtTotal;
    [SerializeField] private TextMeshProUGUI txtAvg;

    [Header("Chart Area")]
    [SerializeField] private LineChartGraphic lineChart;
    [SerializeField] private RectTransform xLabelsContainer;
    [SerializeField] private TextMeshProUGUI txtMax;
    [SerializeField] private GameObject emptyChartLabel;
    [SerializeField] private RectTransform nodeBubble;
    [SerializeField] private TextMeshProUGUI nodeBubbleText;

    [Header("Rank List")]
    [SerializeField] private TextMeshProUGUI rankTitle;
    [SerializeField] private RectTransform rankContent;
    [SerializeField] private RankItemUI rankItemPrefab;

    /// <summary>
    /// 最近一次计算结果（自检/截图核对用）
    /// </summary>
    public ChartResult LastResult { get; private set; }

    private RecordType currentType = RecordType.Expense;
    private ExportRange currentRange = ExportRange.Week;
    private int currentOffset = 0;   // 0=本期，-1 上一周期……
    private List<AccountRecord> lastRecords = new List<AccountRecord>();
    private string lastStartDate = string.Empty;
    private string lastEndDate = string.Empty;
    private readonly List<PeriodStripButton> stripButtons = new List<PeriodStripButton>();
    private bool layoutRetryQueued = false;
    private float lastChartW = -1f;
    private float lastChartH = -1f;
    private int currentBubbleNode = -1;

    /// <summary>
    /// 折线图 rect 变了就重排 X 轴标签与气泡（进 Play 最大化晚于面板激活会导致激活瞬间
    /// 拿到旧尺寸，画布稳定后必须重排，步骤04 首轮实测踩坑）
    /// </summary>
    private void LateUpdate()
    {
        if (lineChart == null)
        {
            return;
        }

        Rect r = lineChart.rectTransform.rect;

        if (Mathf.Approximately(r.width, lastChartW) && Mathf.Approximately(r.height, lastChartH))
        {
            return;
        }

        lastChartW = r.width;
        lastChartH = r.height;

        if (lastChartW > 1f && LastResult != null)
        {
            RebuildXLabels(LastResult);

            if (currentBubbleNode >= 0 && nodeBubble != null && nodeBubble.gameObject.activeSelf)
            {
                PositionBubble(currentBubbleNode);
            }
        }
    }

    private void OnEnable()
    {
        TryInitializeDependencies();
        RegisterEvents();

        if (accountManager != null)
        {
            accountManager.OnDataChanged -= RefreshCurrentView;
            accountManager.OnDataChanged += RefreshCurrentView;
        }

        RefreshAll();
    }

    private void OnDisable()
    {
        if (accountManager != null)
        {
            accountManager.OnDataChanged -= RefreshCurrentView;
        }

        HideBubble();
    }

    /// <summary>
    /// 外部引用为空时自动查找（图表页初始未激活，需包含未激活对象）
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (accountManager == null)
        {
            accountManager = FindFirstObjectByType<AccountManager>();
        }

        if (iconProvider == null)
        {
            iconProvider = FindFirstObjectByType<CategoryIconProvider>();
        }
    }

    /// <summary>
    /// 事件绑定（幂等，可重复调用）；周期条按钮在重建时逐个绑定
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(btnType, OnTypeClicked);

        RegisterToggle(toggleWeek, ExportRange.Week);
        RegisterToggle(toggleMonth, ExportRange.Month);
        RegisterToggle(toggleYear, ExportRange.Year);

        if (lineChart != null)
        {
            lineChart.NodeClicked -= ShowNodeBubble;
            lineChart.NodeClicked += ShowNodeBubble;
        }
    }

    private static void RegisterButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(SfxManager.PlayClick);
        button.onClick.AddListener(action);
    }

    private void RegisterToggle(Toggle toggle, ExportRange range)
    {
        if (toggle == null)
        {
            return;
        }

        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(isOn =>
        {
            if (isOn)
            {
                SetRange(range);
            }
        });
    }

    // ---------- 交互 ----------

    /// <summary>
    /// 支出/收入两态切换（整页数据 + 标题 + 按钮文案同步）
    /// </summary>
    private void OnTypeClicked()
    {
        currentType = currentType == RecordType.Expense ? RecordType.Income : RecordType.Expense;
        RefreshAll();
    }

    /// <summary>
    /// 切换周/月/年档位：重置回本期并重建周期条
    /// </summary>
    public void SetRange(ExportRange range)
    {
        if (currentRange == range)
        {
            UpdateToggleVisuals();
            return;
        }

        currentRange = range;
        currentOffset = 0;
        RebuildPeriodStrip();
        RefreshAll();
    }

    /// <summary>
    /// 选中周期（0=本期，-1 上一周期……周期条与自检共用）
    /// </summary>
    public void SelectPeriod(int offset)
    {
        if (currentOffset == offset)
        {
            UpdateStripHighlight();
            return;
        }

        currentOffset = offset;
        UpdateStripHighlight();
        RefreshAll();
    }

    // ---------- 刷新 ----------

    /// <summary>
    /// OnDataChanged 订阅目标：图表页激活中才重建（编辑落库时本页被隐藏，回来时 OnEnable 已刷新）
    /// </summary>
    private void RefreshCurrentView()
    {
        if (isActiveAndEnabled)
        {
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        if (btnTypeLabel != null)
        {
            btnTypeLabel.text = currentType == RecordType.Expense ? "支出 ▼" : "收入 ▼";
        }

        if (accountManager == null)
        {
            return;
        }

        string start;
        string end;
        DateRangeUtil.ShiftRange(currentRange, DateTime.Now.Date, currentOffset, out start, out end);
        lastStartDate = start;
        lastEndDate = end;
        lastRecords = accountManager.GetRecordsInRange(start, end);

        ChartResult result = ChartCalculator.Calculate(lastRecords, start, end, currentRange, currentType);
        LastResult = result;

        if (txtTotalCaption != null)
        {
            txtTotalCaption.text = currentType == RecordType.Expense ? "总支出" : "总收入";
        }

        if (txtTotal != null)
        {
            txtTotal.text = MoneyText.FormatYuan(result.TotalFen);
        }

        if (txtAvg != null)
        {
            txtAvg.text = MoneyText.FormatYuan(result.AvgFen);
        }

        if (rankTitle != null)
        {
            rankTitle.text = currentType == RecordType.Expense ? "支出排行榜" : "收入排行榜";
        }

        if (txtMax != null)
        {
            txtMax.gameObject.SetActive(result.TotalFen > 0);
            txtMax.text = "最高 " + MoneyText.FormatTrim(result.MaxFen);
        }

        if (emptyChartLabel != null)
        {
            emptyChartLabel.SetActive(result.TotalFen == 0);
        }

        if (lineChart != null)
        {
            lineChart.SetData(result.Points, result.AvgFen, result.ScaleMaxFen);
        }

        RebuildXLabels(result);
        RebuildRanks(result);
        HideBubble();
        UpdateToggleVisuals();
    }

    /// <summary>
    /// 重建 X 轴标签：与折线节点同坐标（容器与图表矩形重合、pivot 均为左下），
    /// 月视图由 ChartCalculator 抽稀后只生成非空标签。布局未就绪（rect 宽 0）时延一帧重刷。
    /// </summary>
    private void RebuildXLabels(ChartResult result)
    {
        if (xLabelsContainer == null)
        {
            return;
        }

        ClearChildren(xLabelsContainer);

        if (result == null || result.Points.Count == 0)
        {
            return;
        }

        List<Vector2> positions = new List<Vector2>();

        for (int i = 0; i < result.Points.Count; i++)
        {
            positions.Add(Vector2.zero);

            if (string.IsNullOrEmpty(result.XLabels[i]))
            {
                continue;
            }

            Vector2 pos;

            if (lineChart == null || !lineChart.TryGetNodeLocalPos(i, out pos))
            {
                if (!layoutRetryQueued)
                {
                    layoutRetryQueued = true;
                    StartCoroutine(RefreshNextFrame());
                }

                return;
            }

            positions[i] = pos;
        }

        for (int i = 0; i < result.Points.Count; i++)
        {
            if (string.IsNullOrEmpty(result.XLabels[i]))
            {
                continue;
            }

            CreateXLabel(result.XLabels[i], positions[i].x);
        }
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        layoutRetryQueued = false;
        RefreshAll();
    }

    private void CreateXLabel(string text, float x)
    {
        GameObject go = new GameObject("XLabel", typeof(RectTransform));
        go.transform.SetParent(xLabelsContainer, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(140f, 40f);
        rect.anchoredPosition = new Vector2(x, 26f);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = labelFont;
        tmp.text = text;
        tmp.fontSize = 24f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = ColorXLabel;
        tmp.raycastTarget = false;
    }

    private void RebuildRanks(ChartResult result)
    {
        if (rankContent == null)
        {
            return;
        }

        ClearChildren(rankContent);

        if (rankItemPrefab == null || result == null || result.Ranks == null)
        {
            return;
        }

        for (int i = 0; i < result.Ranks.Count; i++)
        {
            RankItemUI item = Instantiate(rankItemPrefab, rankContent);
            item.Setup(result.Ranks[i], GetIconSprite(result.Ranks[i].Category));
        }
    }

    /// <summary>
    /// 分类名 → 图标 sprite（CategoryTable 反查 IconName；"其他"合并行同样命中 icon_general）
    /// </summary>
    private Sprite GetIconSprite(string categoryName)
    {
        if (iconProvider == null || string.IsNullOrEmpty(categoryName))
        {
            return null;
        }

        return iconProvider.GetSprite(CategoryTable.GetIconName(categoryName));
    }

    // ---------- 周期条 ----------

    private void RebuildPeriodStrip()
    {
        if (periodContent == null || periodButtonPrefab == null)
        {
            return;
        }

        ClearChildren(periodContent);
        stripButtons.Clear();

        DateTime today = DateTime.Now.Date;

        // 月档：当年 1 月..当前月（未来月份无数据不上条，12 月时正好 1..12 全年）；
        // 周/年档：维持最近 6 期窗口。左旧右新，本期在最右
        int count = currentRange == ExportRange.Month ? today.Month : StripPeriodCount;

        for (int i = count - 1; i >= 0; i--)
        {
            int offset = -i;
            string start;
            string end;
            DateRangeUtil.ShiftRange(currentRange, today, offset, out start, out end);

            PeriodStripButton button = Instantiate(periodButtonPrefab, periodContent);
            button.Setup(offset, FormatPeriodLabel(currentRange, start, end, today), offset == currentOffset);

            int capturedOffset = offset;
            Button clickable = button.GetComponent<Button>();

            if (clickable != null)
            {
                clickable.onClick.AddListener(SfxManager.PlayClick);
                clickable.onClick.AddListener(() => SelectPeriod(capturedOffset));
            }

            stripButtons.Add(button);
        }

        ScrollStripToEnd();
    }

    private void UpdateStripHighlight()
    {
        for (int i = 0; i < stripButtons.Count; i++)
        {
            PeriodStripButton button = stripButtons[i];

            if (button != null)
            {
                button.Setup(button.Offset, button.LabelText, button.Offset == currentOffset);
            }
        }
    }

    /// <summary>
    /// 周期条标签：周 "09.21-09.27" / 月 "9月"（跨年 "2025年12月"）/ 年 "2026年"
    /// </summary>
    private static string FormatPeriodLabel(ExportRange range, string start, string end, DateTime today)
    {
        DateTime startDate;

        if (!DateTime.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out startDate))
        {
            return start;
        }

        if (range == ExportRange.Week)
        {
            DateTime endDate;
            string endText = DateTime.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out endDate) ? endDate.ToString("MM.dd") : "";
            return startDate.ToString("MM.dd") + "-" + endText;
        }

        if (range == ExportRange.Month)
        {
            return startDate.Year == today.Year
                ? startDate.ToString("M月")
                : startDate.ToString("yyyy年M月");
        }

        return startDate.ToString("yyyy年");
    }

    /// <summary>
    /// 重建后滚到最右（本期），ForceUpdateCanvases 先让 ContentSizeFitter 算出 Content 宽
    /// </summary>
    private void ScrollStripToEnd()
    {
        Canvas.ForceUpdateCanvases();

        if (periodContent == null || periodViewport == null)
        {
            return;
        }

        float excess = periodContent.rect.width - periodViewport.rect.width;
        periodContent.anchoredPosition = excess > 0f ? new Vector2(-excess, 0f) : Vector2.zero;
    }

    // ---------- 节点气泡 ----------

    /// <summary>
    /// 点折线节点弹一行式气泡："最大3笔交易：10.5 / 10.3 / 6.0 ｜ 当日合计 28.8"
    /// （年视图为"当月合计"；节点无数据时显示"当日没有费用"）
    /// </summary>
    private void ShowNodeBubble(int nodeIndex)
    {
        if (nodeBubble == null || LastResult == null || nodeIndex < 0 ||
            nodeIndex >= LastResult.Points.Count)
        {
            HideBubble();
            return;
        }

        currentBubbleNode = nodeIndex;

        string nodeStart;
        string nodeEnd;
        string unitWord;

        if (currentRange == ExportRange.Year)
        {
            DateTime yearStart;
            DateTime.TryParseExact(lastStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out yearStart);
            DateTime monthStart = yearStart.AddMonths(nodeIndex);
            nodeStart = monthStart.ToString("yyyy-MM-dd");
            nodeEnd = new DateTime(monthStart.Year, monthStart.Month,
                DateTime.DaysInMonth(monthStart.Year, monthStart.Month)).ToString("yyyy-MM-dd");
            unitWord = "当月";
        }
        else
        {
            DateTime day;
            DateTime.TryParseExact(lastStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out day);
            day = day.AddDays(nodeIndex);
            nodeStart = nodeEnd = day.ToString("yyyy-MM-dd");
            unitWord = "当日";
        }

        long totalFen = 0;
        List<long> amounts = new List<long>();

        for (int i = 0; i < lastRecords.Count; i++)
        {
            AccountRecord record = lastRecords[i];

            if (record == null || record.Type != (int)currentType)
            {
                continue;
            }

            if (record.Date.CompareTo(nodeStart) < 0 || record.Date.CompareTo(nodeEnd) > 0)
            {
                continue;
            }

            totalFen += record.AmountFen;
            amounts.Add(record.AmountFen);
        }

        if (nodeBubbleText != null)
        {
            if (totalFen == 0)
            {
                nodeBubbleText.text = unitWord + "没有费用";
            }
            else
            {
                amounts.Sort((a, b) => b.CompareTo(a));

                if (amounts.Count > BubbleTopCount)
                {
                    amounts.RemoveRange(BubbleTopCount, amounts.Count - BubbleTopCount);
                }

                List<string> parts = new List<string>();

                for (int i = 0; i < amounts.Count; i++)
                {
                    parts.Add(MoneyText.FormatTrim(amounts[i]));
                }

                nodeBubbleText.text = "最大" + amounts.Count + "笔交易：" +
                    string.Join(" / ", parts.ToArray()) +
                    " ｜ " + unitWord + "合计 " + MoneyText.FormatTrim(totalFen);
            }
        }

        PositionBubble(nodeIndex);
        nodeBubble.gameObject.SetActive(true);
    }

    /// <summary>
    /// 气泡挂在节点上方并做水平/垂直收敛；顶部放不下时翻到节点下方
    /// </summary>
    private void PositionBubble(int nodeIndex)
    {
        Vector2 nodePos;

        if (lineChart == null || !lineChart.TryGetNodeLocalPos(nodeIndex, out nodePos))
        {
            return;
        }

        float chartW = lineChart.rectTransform.rect.width;
        float chartH = lineChart.rectTransform.rect.height;
        float bubbleW = nodeBubble.rect.width;
        float bubbleH = nodeBubble.rect.height;

        float x = Mathf.Clamp(nodePos.x, bubbleW * 0.5f + 10f,
            Mathf.Max(bubbleW * 0.5f + 10f, chartW - bubbleW * 0.5f - 10f));
        float y = nodePos.y + bubbleH * 0.5f + 24f;

        if (y + bubbleH * 0.5f > chartH - 8f)
        {
            y = nodePos.y - bubbleH * 0.5f - 24f;
        }

        nodeBubble.anchorMin = Vector2.zero;
        nodeBubble.anchorMax = Vector2.zero;
        nodeBubble.pivot = new Vector2(0.5f, 0.5f);
        nodeBubble.anchoredPosition = new Vector2(x, y);
    }

    private void HideBubble()
    {
        currentBubbleNode = -1;

        if (nodeBubble != null)
        {
            nodeBubble.gameObject.SetActive(false);
        }
    }

    // ---------- Toggle 视觉 ----------

    /// <summary>
    /// 选中块 Toggle.graphic 是 CrossFadeAlpha 控制的，这里用 0 时长同步一遍，
    /// 保证首帧打开就是正确的高亮与文字颜色
    /// </summary>
    private void UpdateToggleVisuals()
    {
        ApplyToggleVisual(toggleWeek, toggleWeekLabel, toggleWeekSelectedBg);
        ApplyToggleVisual(toggleMonth, toggleMonthLabel, toggleMonthSelectedBg);
        ApplyToggleVisual(toggleYear, toggleYearLabel, toggleYearSelectedBg);
    }

    private static void ApplyToggleVisual(Toggle toggle, TextMeshProUGUI label, Image selectedBg)
    {
        bool selected = toggle != null && toggle.isOn;

        if (label != null)
        {
            label.color = selected ? ColorToggleTextOn : ColorToggleTextOff;
        }

        if (selectedBg != null)
        {
            selectedBg.canvasRenderer.SetAlpha(selected ? 1f : 0f);
        }
    }

    // ---------- 工具 ----------

    private static void ClearChildren(RectTransform container)
    {
        // DestroyImmediate 而非 Destroy：OnDataChanged 可能同帧连发，Destroy 帧末才生效会叠幽灵行（步骤03 §4）
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(container.GetChild(i).gameObject);
        }
    }
}
