using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 04 验收：图表页流程自检（Play 模式，菜单 AccountBook/09-图表流程自检）。
/// 自动进 Play → 周期工具字面值锚点 → ChartCalculator 单元断言 → UI 全流程：
/// 周默认视图（点数/标签/汇总/均值/最高值/排行榜）、月/年档位、上一周期补记账计入、
/// 空周期"没有费用"、节点气泡、支出收入切换、导入导出置灰 → 清理测试数据 → 退 Play。
/// 全程基线感知：只对本次自建数据做断言，既有真实数据不受影响并在结束时还原。
/// </summary>
public static class ChartFlowCheck
{
    private const string MenuItemPath = "AccountBook/09-图表流程自检";
    private const string StartedKey = "AccountBook.ChartCheck.Started";
    private const string DoneKey = "AccountBook.ChartCheck.Done";
    private const int TopCount = 5;
    private const string OtherCategory = "其他";

    /// <summary>
    /// 入口（菜单/搭建脚本共用）：进 Play → 自动跑全流程校验 → 退 Play
    /// </summary>
    [MenuItem(MenuItemPath)]
    internal static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            DoCheck();
            return;
        }

        SessionState.SetBool(StartedKey, true);
        SessionState.SetBool(DoneKey, false);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    /// <summary>
    /// 进 Play 触发域重载会丢事件订阅，用 SessionState 记忆重挂
    /// </summary>
    [InitializeOnLoadMethod]
    private static void HookAfterReload()
    {
        if (SessionState.GetBool(StartedKey, false) && SessionState.GetBool(DoneKey, false) == false)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += DoCheck;
        }
    }

    private static void DoCheck()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        try
        {
            RunChecks();
        }
        catch (Exception ex)
        {
            Debug.LogError("[ChartCheck] 异常：" + ex);
        }
        finally
        {
            SessionState.SetBool(DoneKey, true);
            EditorApplication.isPlaying = false;
        }
    }

    private static void RunChecks()
    {
        bool allPass = true;

        // ============ 一、DateRangeUtil 字面值锚点（独立于被测代码的已知事实） ============
        DateTime anchorSaturday = new DateTime(2026, 9, 26);   // 该日为周六
        string ws0, we0;
        DateRangeUtil.GetWeekRange(anchorSaturday, out ws0, out we0);
        allPass &= Check("周区间：2026-09-26(周六) → 09-21~09-27", ws0 == "2026-09-21" && we0 == "2026-09-27");

        string ms0, me0;
        DateRangeUtil.GetMonthRange(2026, 2, out ms0, out me0);
        allPass &= Check("月区间：2026年2月 → 02-01~02-28", ms0 == "2026-02-01" && me0 == "2026-02-28");

        string ys0, ye0;
        DateRangeUtil.GetYearRange(2025, out ys0, out ye0);
        allPass &= Check("年区间：2025 → 01-01~12-31", ys0 == "2025-01-01" && ye0 == "2025-12-31");

        string ps0, pe0;
        DateRangeUtil.ShiftRange(ExportRange.Week, anchorSaturday, -1, out ps0, out pe0);
        allPass &= Check("周平移-1 → 09-14~09-20", ps0 == "2026-09-14" && pe0 == "2026-09-20");

        DateRangeUtil.ShiftRange(ExportRange.Month, anchorSaturday, -1, out ps0, out pe0);
        allPass &= Check("月平移-1 → 2026-08-01~08-31", ps0 == "2026-08-01" && pe0 == "2026-08-31");

        DateRangeUtil.ShiftRange(ExportRange.Year, anchorSaturday, 1, out ps0, out pe0);
        allPass &= Check("年平移+1 → 2027-01-01~12-31", ps0 == "2027-01-01" && pe0 == "2027-12-31");

        // ============ 二、ChartCalculator 单元断言（手工构数据对答案） ============
        List<AccountRecord> unitRecords = new List<AccountRecord>
        {
            MakeUnitRecord((int)RecordType.Expense, "餐饮", 1000, "2026-09-21"),
            MakeUnitRecord((int)RecordType.Expense, "交通", 2000, "2026-09-21"),
            MakeUnitRecord((int)RecordType.Expense, "购物", 3000, "2026-09-22"),
            MakeUnitRecord((int)RecordType.Expense, "日用", 400, "2026-09-23"),
            MakeUnitRecord((int)RecordType.Expense, "蔬菜", 500, "2026-09-23"),
            MakeUnitRecord((int)RecordType.Expense, "水果", 600, "2026-09-24"),
            MakeUnitRecord((int)RecordType.Income, "工资", 90000, "2026-09-25"),   // 应被类型过滤
        };

        ChartResult unit = ChartCalculator.Calculate(unitRecords, "2026-09-21", "2026-09-27",
            ExportRange.Week, RecordType.Expense);
        allPass &= Check("单元：周 7 点", unit.Points.Count == 7);
        allPass &= Check("单元：收入被过滤 总额=7500分", unit.TotalFen == 7500);
        allPass &= Check("单元：均值=总额/7 整除", unit.AvgFen == 7500 / 7);
        allPass &= Check("单元：最大值 3000 首个最大点索引=0", unit.MaxFen == 3000 && unit.MaxIndex == 0);
        allPass &= Check("单元：档位上限 3000→5000", unit.ScaleMaxFen == 5000);
        allPass &= Check("单元：X 轴首标签 09-21", unit.XLabels[0] == "09-21");
        allPass &= Check("单元：排行榜 Top5+其他", unit.Ranks.Count == 6 &&
            unit.Ranks[0].Category == "购物" && unit.Ranks[0].AmountFen == 3000 &&
            unit.Ranks[5].Category == "其他" && unit.Ranks[5].AmountFen == 400);
        allPass &= Check("单元：榜首占比 0.4", Mathf.Abs(unit.Ranks[0].Ratio - 0.4f) < 0.0001f);

        ChartResult unitMonth = ChartCalculator.Calculate(new List<AccountRecord>(), "2026-02-01", "2026-02-28",
            ExportRange.Month, RecordType.Expense);
        allPass &= Check("单元：2 月 28 点、标签抽稀 01/06", unitMonth.Points.Count == 28 &&
            unitMonth.XLabels[0] == "01" && unitMonth.XLabels[1] == "" && unitMonth.XLabels[5] == "06");
        allPass &= Check("单元：空数据均值 0 档位 0", unitMonth.AvgFen == 0 && unitMonth.ScaleMaxFen == 0);

        ChartResult unitYear = ChartCalculator.Calculate(new List<AccountRecord>(), "2026-01-01", "2026-12-31",
            ExportRange.Year, RecordType.Income);
        allPass &= Check("单元：年 12 点标签 1月..12月", unitYear.Points.Count == 12 &&
            unitYear.XLabels[0] == "1月" && unitYear.XLabels[11] == "12月");

        // ============ 三、UI 全流程（基线感知） ============
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        ChartPanelUI chart = UnityEngine.Object.FindFirstObjectByType<ChartPanelUI>(FindObjectsInactive.Include);
        DetailPanelUI detail = UnityEngine.Object.FindFirstObjectByType<DetailPanelUI>(FindObjectsInactive.Include);
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();

        if (uiManager == null || chart == null || detail == null || accountManager == null)
        {
            Debug.LogError("[ChartCheck] FAIL - 场景组件缺失（UIManager/ChartPanelUI/DetailPanelUI/AccountManager）。");
            return;
        }

        SerializedObject soChart = new SerializedObject(chart);
        Toggle toggleWeek = soChart.FindProperty("toggleWeek").objectReferenceValue as Toggle;
        Toggle toggleMonth = soChart.FindProperty("toggleMonth").objectReferenceValue as Toggle;
        Toggle toggleYear = soChart.FindProperty("toggleYear").objectReferenceValue as Toggle;
        TextMeshProUGUI toggleWeekLabel = soChart.FindProperty("toggleWeekLabel").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI toggleMonthLabel = soChart.FindProperty("toggleMonthLabel").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI btnTypeLabel = soChart.FindProperty("btnTypeLabel").objectReferenceValue as TextMeshProUGUI;
        Button btnType = soChart.FindProperty("btnType").objectReferenceValue as Button;
        TextMeshProUGUI txtTotal = soChart.FindProperty("txtTotal").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI txtAvg = soChart.FindProperty("txtAvg").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI txtMax = soChart.FindProperty("txtMax").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI rankTitle = soChart.FindProperty("rankTitle").objectReferenceValue as TextMeshProUGUI;
        LineChartGraphic lineChart = soChart.FindProperty("lineChart").objectReferenceValue as LineChartGraphic;
        RectTransform periodContent = soChart.FindProperty("periodContent").objectReferenceValue as RectTransform;
        RectTransform rankContent = soChart.FindProperty("rankContent").objectReferenceValue as RectTransform;
        GameObject emptyChartLabel = soChart.FindProperty("emptyChartLabel").objectReferenceValue as GameObject;
        RectTransform nodeBubble = soChart.FindProperty("nodeBubble").objectReferenceValue as RectTransform;
        TextMeshProUGUI nodeBubbleText = soChart.FindProperty("nodeBubbleText").objectReferenceValue as TextMeshProUGUI;

        if (toggleWeek == null || toggleMonth == null || toggleYear == null || btnTypeLabel == null ||
            btnType == null || txtTotal == null || txtAvg == null ||
            txtMax == null || rankTitle == null || lineChart == null || periodContent == null ||
            rankContent == null || emptyChartLabel == null || nodeBubble == null || nodeBubbleText == null)
        {
            System.Text.StringBuilder missing = new System.Text.StringBuilder();

            AppendIfNull(missing, "toggleWeek", toggleWeek);
            AppendIfNull(missing, "toggleMonth", toggleMonth);
            AppendIfNull(missing, "toggleYear", toggleYear);
            AppendIfNull(missing, "btnTypeLabel", btnTypeLabel);
            AppendIfNull(missing, "btnType", btnType);
            AppendIfNull(missing, "txtTotal", txtTotal);
            AppendIfNull(missing, "txtAvg", txtAvg);
            AppendIfNull(missing, "txtMax", txtMax);
            AppendIfNull(missing, "rankTitle", rankTitle);
            AppendIfNull(missing, "lineChart", lineChart);
            AppendIfNull(missing, "periodContent", periodContent);
            AppendIfNull(missing, "rankContent", rankContent);
            AppendIfNull(missing, "emptyChartLabel", emptyChartLabel);
            AppendIfNull(missing, "nodeBubble", nodeBubble);
            AppendIfNull(missing, "nodeBubbleText", nodeBubbleText);

            int panelCount = UnityEngine.Object.FindObjectsByType<ChartPanelUI>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            missing.Append(" | chart=").Append(chart != null ? chart.GetEntityId().ToString() : "null")
                .Append(" panelCount=").Append(panelCount)
                .Append(" active=").Append(chart != null && chart.gameObject.activeInHierarchy);

            Debug.LogError("[ChartCheck] FAIL - 关键引用缺失：" + missing.ToString() + "，请先运行菜单 AccountBook/08-搭建图表页。");
            return;
        }

        // —— 周期与基线（先拍照后造数） ——
        DateTime today = DateTime.Now.Date;
        DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateTime dayG = today == monday ? monday : today.AddDays(-1);   // 周内第二个记账日（<=今天）
        DateTime lastWeekThursday = monday.AddDays(-4);
        DateTime prevMonth15 = today.AddMonths(-1);
        prevMonth15 = new DateTime(prevMonth15.Year, prevMonth15.Month, 15);

        string ws, we, pws, pwe, fws, fwe, ms, me, ys, ye;
        DateRangeUtil.GetWeekRange(today, out ws, out we);
        DateRangeUtil.GetWeekRange(lastWeekThursday, out pws, out pwe);
        DateRangeUtil.GetWeekRange(monday.AddDays(-35), out fws, out fwe);   // 5 周前（空周期候选）
        DateRangeUtil.GetMonthRange(today.Year, today.Month, out ms, out me);
        DateRangeUtil.GetYearRange(today.Year, out ys, out ye);

        List<AccountRecord> baseWeek = accountManager.GetRecordsInRange(ws, we);
        List<AccountRecord> basePrevWeek = accountManager.GetRecordsInRange(pws, pwe);
        List<AccountRecord> baseFarWeek = accountManager.GetRecordsInRange(fws, fwe);
        List<AccountRecord> baseMonth = accountManager.GetRecordsInRange(ms, me);
        List<AccountRecord> baseYear = accountManager.GetRecordsInRange(ys, ye);

        // —— 造测试数据（Note=自检数据，自建自清；H 为补记账：CreatedAt=现在，Date=上周四） ——
        List<AccountRecord> created = new List<AccountRecord>();
        created.Add(CreateRecord(accountManager, (int)RecordType.Expense, "餐饮", 2880, today, today.AddHours(9)));
        created.Add(CreateRecord(accountManager, (int)RecordType.Expense, "交通", 1030, today, today.AddHours(10)));
        created.Add(CreateRecord(accountManager, (int)RecordType.Expense, "购物", 600, today, today.AddHours(11)));
        created.Add(CreateRecord(accountManager, (int)RecordType.Expense, "零食", 1500, today, today.AddHours(8)));
        created.Add(CreateRecord(accountManager, (int)RecordType.Income, "工资", 600000, today, today.AddHours(12)));
        created.Add(CreateRecord(accountManager, (int)RecordType.Expense, "餐饮", 5000, monday, monday.AddHours(8)));
        created.Add(CreateRecord(accountManager, (int)RecordType.Expense, "娱乐", 3300, dayG, dayG.AddHours(20)));
        created.Add(CreateRecord(accountManager, (int)RecordType.Expense, "餐饮", 3300, lastWeekThursday, DateTime.Now));
        created.Add(CreateRecord(accountManager, (int)RecordType.Expense, "购物", 12000, prevMonth15, DateTime.Now));

        // —— 期望值（手工聚合基线+自建，不经 ChartCalculator，防自证） ——
        long expectedWeekExpense = SumRecords(baseWeek, RecordType.Expense, ws, we) + SumRecords(created, RecordType.Expense, ws, we);
        long expectedWeekIncome = SumRecords(baseWeek, RecordType.Income, ws, we) + SumRecords(created, RecordType.Income, ws, we);
        long[] expectedWeekPoints = ExpectedPoints(baseWeek, created, RecordType.Expense, monday, ExportRange.Week);
        List<CategoryRank> expectedWeekRanks = BuildExpectedRanks(Join(baseWeek, created), RecordType.Expense, ws, we);
        long expectedMonthExpense = SumRecords(baseMonth, RecordType.Expense, ms, me) + SumRecords(created, RecordType.Expense, ms, me);
        long expectedYearExpense = SumRecords(baseYear, RecordType.Expense, ys, ye) + SumRecords(created, RecordType.Expense, ys, ye);
        long expectedPrevWeekExpense = SumRecords(basePrevWeek, RecordType.Expense, pws, pwe) + SumRecords(created, RecordType.Expense, pws, pwe);
        long expectedFarWeekExpense = SumRecords(baseFarWeek, RecordType.Expense, fws, fwe);

        int todayIdx = (today - monday).Days;

        // 1) 打开图表页 → 默认 周 + 支出
        uiManager.OpenChartPanel();
        allPass &= Check("图表页激活", chart.gameObject.activeInHierarchy);
        allPass &= Check("明细页已隐藏", detail.gameObject.activeInHierarchy == false);
        allPass &= Check("默认选中周档位", toggleWeek.isOn && toggleMonth.isOn == false && toggleYear.isOn == false);
        allPass &= Check("两态按钮初始文案 支出 ▼", btnTypeLabel.text == "支出 ▼");
        allPass &= Check("排行榜标题初始 支出排行榜", rankTitle.text == "支出排行榜");

        // 2) 周视图：点数/标签/汇总/均值/最高值
        ChartResult result = chart.LastResult;
        allPass &= Check("周折线 7 点", result != null && result.Points.Count == 7 && lineChart.PointCount == 7);
        allPass &= Check("周 X 轴首标签=周一日期", result != null && result.XLabels[0] == monday.ToString("MM-dd"));
        allPass &= Check("周总额=基线+自建", result != null && result.TotalFen == expectedWeekExpense);
        allPass &= Check("汇总行总额一致", txtTotal.text == MoneyText.FormatYuan(expectedWeekExpense));
        allPass &= Check("汇总行均值=总额/7", txtAvg.text == MoneyText.FormatYuan(expectedWeekExpense / 7));
        allPass &= Check("今日桶=基线+自建当日合计", result != null && result.Points[todayIdx] ==
            SumRecordsDay(baseWeek, RecordType.Expense, today.ToString("yyyy-MM-dd")) +
            SumRecordsDay(created, RecordType.Expense, today.ToString("yyyy-MM-dd")));
        allPass &= Check("最大值索引正确", result != null && result.MaxIndex == ArgMax(expectedWeekPoints) &&
            result.MaxFen == expectedWeekPoints[ArgMax(expectedWeekPoints)]);
        allPass &= Check("最高值标注显示且数值正确", txtMax.gameObject.activeSelf == (expectedWeekExpense > 0) &&
            (expectedWeekExpense == 0 || txtMax.text == "最高 " + MoneyText.FormatTrim(result.MaxFen)));
        allPass &= Check("有数据周期不显示空态", emptyChartLabel.activeSelf == false);

        // 3) 排行榜：期望行数与内容（名次/名称/占比/金额/条长）
        allPass &= Check("周排行榜行数一致", result != null && result.Ranks.Count == expectedWeekRanks.Count);
        allPass &= Check("周排行榜聚合一致", result != null && RanksMatch(result.Ranks, expectedWeekRanks));
        allPass &= Check("排行榜 UI 行数一致", rankContent.GetComponentsInChildren<RankItemUI>().Length == expectedWeekRanks.Count);
        allPass &= Check("排行榜 UI 内容一致", RankItemsMatch(rankContent, expectedWeekRanks));

        // 4) 月档位：点数=当月天数，总额同月数据
        toggleMonth.isOn = true;
        result = chart.LastResult;
        allPass &= Check("切月后 toggleMonth 选中、周取消", toggleMonth.isOn && toggleWeek.isOn == false);
        allPass &= Check("月档位选中块文字变白", toggleMonthLabel.color == Color.white);
        allPass &= Check("月折线点数=当月天数", result != null && result.Points.Count ==
            DateTime.DaysInMonth(today.Year, today.Month));
        allPass &= Check("月总额=基线+自建", result != null && txtTotal.text == MoneyText.FormatYuan(expectedMonthExpense));
        allPass &= Check("月周期条 1月..当前月", periodContent.childCount == today.Month &&
            periodContent.GetChild(0).GetComponent<PeriodStripButton>().LabelText == "1月" &&
            periodContent.GetChild(today.Month - 1).GetComponent<PeriodStripButton>().LabelText == today.Month + "月");

        // 5) 年档位：12 点按月汇总
        toggleYear.isOn = true;
        result = chart.LastResult;
        allPass &= Check("年折线 12 点", result != null && result.Points.Count == 12);
        allPass &= Check("年 X 轴末标签 12月", result != null && result.XLabels[11] == "12月");
        allPass &= Check("年总额=基线+自建", result != null && txtTotal.text == MoneyText.FormatYuan(expectedYearExpense));
        allPass &= Check("档位上限≥最大值", result != null && result.ScaleMaxFen >= result.MaxFen && result.ScaleMaxFen > 0);

        // 6) 回周档位 → 上一周期（补记账计入折线与总额）
        toggleWeek.isOn = true;
        chart.SelectPeriod(-1);
        result = chart.LastResult;
        allPass &= Check("切上一周期后总额=基线+补记 33.0", result != null && txtTotal.text == MoneyText.FormatYuan(expectedPrevWeekExpense));
        allPass &= Check("周期条 6 个按钮", periodContent.childCount == 6);

        // 7) 5 周前周期：空态或基线数据（基线感知，条件断言）
        chart.SelectPeriod(-5);
        result = chart.LastResult;
        allPass &= Check("5 周前总额=该周期基线", result != null && result.TotalFen == expectedFarWeekExpense);
        allPass &= Check("空周期显示没有费用、排行区空", result != null && (
            (expectedFarWeekExpense == 0 && emptyChartLabel.activeSelf &&
             rankContent.childCount == 0 && txtMax.gameObject.activeSelf == false) ||
            (expectedFarWeekExpense > 0 && emptyChartLabel.activeSelf == false)));

        // 8) 回本期 → 点今日节点弹气泡（Top3 + 当日合计）
        chart.SelectPeriod(0);
        result = chart.LastResult;
        string todayKey = today.ToString("yyyy-MM-dd");
        long expectedTodayExpense = SumRecordsDay(baseWeek, RecordType.Expense, todayKey) +
            SumRecordsDay(created, RecordType.Expense, todayKey);
        int expectedTodayCount = CountDay(baseWeek, RecordType.Expense, todayKey) + CountDay(created, RecordType.Expense, todayKey);

        lineChart.SimulateNodeClick(todayIdx);
        string bubbleHeader = "最大" + Math.Min(3, expectedTodayCount) + "笔交易：";
        allPass &= Check("点节点 → 气泡激活", nodeBubble.gameObject.activeSelf);
        allPass &= Check("气泡抬头 最大N笔交易", nodeBubbleText.text.StartsWith(bubbleHeader));
        allPass &= Check("气泡当日合计=基线+自建", nodeBubbleText.text.Contains("当日合计 " + MoneyText.FormatTrim(expectedTodayExpense)));

        int emptyIdx = result != null ? result.Points.FindIndex(v => v == 0) : -1;

        if (emptyIdx >= 0)
        {
            lineChart.SimulateNodeClick(emptyIdx);
            allPass &= Check("点空节点 → 当日没有费用", nodeBubble.gameObject.activeSelf && nodeBubbleText.text == "当日没有费用");
        }

        // 9) 支出 → 收入：整页数据切换
        btnType.onClick.Invoke();
        allPass &= Check("两态按钮切为 收入 ▼", btnTypeLabel.text == "收入 ▼");
        allPass &= Check("排行榜标题切为 收入排行榜", rankTitle.text == "收入排行榜");
        allPass &= Check("收入总额=基线+自建工资", txtTotal.text == MoneyText.FormatYuan(expectedWeekIncome));
        List<CategoryRank> expectedIncomeRanks = BuildExpectedRanks(Join(baseWeek, created), RecordType.Income, ws, we);
        allPass &= Check("收入排行榜内容一致", chart.LastResult != null &&
            RanksMatch(chart.LastResult.Ranks, expectedIncomeRanks) && RankItemsMatch(rankContent, expectedIncomeRanks));

        // 10) 切回明细页
        uiManager.OpenDetailPanel();
        allPass &= Check("切回明细页图表页隐藏", detail.gameObject.activeInHierarchy && chart.gameObject.activeInHierarchy == false);

        // —— 清理测试数据（自愈：此前崩溃残留的同标记数据一并清扫） ——
        List<string> createdIds = new List<string>();

        for (int i = 0; i < created.Count; i++)
        {
            createdIds.Add(created[i].Id);
            accountManager.DeleteRecord(created[i].Id);
        }

        int swept = 0;
        List<AccountRecord> everything = accountManager.GetRecordsInRange("0000-01-01", "9999-12-31");

        for (int i = everything.Count - 1; i >= 0; i--)
        {
            if (everything[i] != null && everything[i].Note == "自检数据")
            {
                accountManager.DeleteRecord(everything[i].Id);
                swept++;
            }
        }

        if (swept > created.Count)
        {
            Debug.Log($"[ChartCheck] 自愈清扫了 {swept - created.Count} 笔此前崩溃残留的测试数据。");
        }

        // —— 基线还原断言 ——
        allPass &= Check("[还原] 本周记录数/收支还原", PeriodRestored(accountManager, ws, we, baseWeek));
        allPass &= Check("[还原] 上周记录数/收支还原", PeriodRestored(accountManager, pws, pwe, basePrevWeek));
        allPass &= Check("[还原] 5 周前还原", PeriodRestored(accountManager, fws, fwe, baseFarWeek));
        allPass &= Check("[还原] 本月还原", PeriodRestored(accountManager, ms, me, baseMonth));
        allPass &= Check("[还原] 本年还原", PeriodRestored(accountManager, ys, ye, baseYear));

        Debug.Log($"[ChartCheck] ===== 图表页流程自检{(allPass ? "全部通过" : "存在失败项")} =====");
    }

    // ---------- 期望值计算（独立实现，不经 ChartCalculator） ----------

    private static long[] ExpectedPoints(List<AccountRecord> baseline, List<AccountRecord> created,
        RecordType type, DateTime periodStart, ExportRange range)
    {
        int count = range == ExportRange.Week ? 7 : 12;
        long[] points = new long[count];
        AppendPoints(points, baseline, type, periodStart, range);
        AppendPoints(points, created, type, periodStart, range);
        return points;
    }

    private static void AppendPoints(long[] points, List<AccountRecord> records, RecordType type,
        DateTime periodStart, ExportRange range)
    {
        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null || record.Type != (int)type)
            {
                continue;
            }

            DateTime date;

            if (!DateTime.TryParseExact(record.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date))
            {
                continue;
            }

            int bucket = range == ExportRange.Week ? (date.Date - periodStart.Date).Days : date.Month - 1;

            if (bucket >= 0 && bucket < points.Length)
            {
                points[bucket] += record.AmountFen;
            }
        }
    }

    private static int ArgMax(long[] points)
    {
        int index = 0;

        for (int i = 1; i < points.Length; i++)
        {
            if (points[i] > points[index])
            {
                index = i;
            }
        }

        return index;
    }

    private static long SumRecords(List<AccountRecord> records, RecordType type, string start, string end)
    {
        long total = 0;

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null || record.Type != (int)type)
            {
                continue;
            }

            if (record.Date.CompareTo(start) >= 0 && record.Date.CompareTo(end) <= 0)
            {
                total += record.AmountFen;
            }
        }

        return total;
    }

    private static long SumRecordsDay(List<AccountRecord> records, RecordType type, string day)
    {
        long total = 0;

        for (int i = 0; i < records.Count; i++)
        {
            if (records[i] != null && records[i].Type == (int)type && records[i].Date == day)
            {
                total += records[i].AmountFen;
            }
        }

        return total;
    }

    private static int CountDay(List<AccountRecord> records, RecordType type, string day)
    {
        int count = 0;

        for (int i = 0; i < records.Count; i++)
        {
            if (records[i] != null && records[i].Type == (int)type && records[i].Date == day)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 手工聚合排行：按分类求和 → 金额降序（同额名升序）→ Top5 + 其余合并"其他"
    /// （与 ChartCalculator 同语义的独立实现，防自证）
    /// </summary>
    private static List<CategoryRank> BuildExpectedRanks(List<AccountRecord> records, RecordType type, string start, string end)
    {
        Dictionary<string, long> sum = new Dictionary<string, long>();
        long total = 0;

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null || record.Type != (int)type)
            {
                continue;
            }

            if (record.Date.CompareTo(start) < 0 || record.Date.CompareTo(end) > 0)
            {
                continue;
            }

            string category = string.IsNullOrEmpty(record.Category) ? OtherCategory : record.Category;

            if (sum.ContainsKey(category))
            {
                sum[category] += record.AmountFen;
            }
            else
            {
                sum[category] = record.AmountFen;
            }

            total += record.AmountFen;
        }

        List<CategoryRank> ranks = new List<CategoryRank>();

        foreach (KeyValuePair<string, long> pair in sum)
        {
            ranks.Add(new CategoryRank { Category = pair.Key, AmountFen = pair.Value });
        }

        ranks.Sort((a, b) =>
        {
            int byAmount = b.AmountFen.CompareTo(a.AmountFen);
            return byAmount != 0 ? byAmount : string.CompareOrdinal(a.Category, b.Category);
        });

        List<CategoryRank> expected = new List<CategoryRank>();
        int take = Mathf.Min(TopCount, ranks.Count);

        for (int i = 0; i < take; i++)
        {
            expected.Add(new CategoryRank { Category = ranks[i].Category, AmountFen = ranks[i].AmountFen });
        }

        long otherFen = 0;

        for (int i = take; i < ranks.Count; i++)
        {
            otherFen += ranks[i].AmountFen;
        }

        if (take < ranks.Count || otherFen > 0)
        {
            CategoryRank existing = expected.Find(rank => rank.Category == OtherCategory);

            if (existing != null && otherFen > 0)
            {
                existing.AmountFen += otherFen;
            }
            else if (otherFen > 0)
            {
                expected.Add(new CategoryRank { Category = OtherCategory, AmountFen = otherFen });
            }
        }

        for (int i = 0; i < expected.Count; i++)
        {
            expected[i].Ratio = total > 0f ? expected[i].AmountFen / (float)total : 0f;
        }

        return expected;
    }

    private static bool RanksMatch(List<CategoryRank> actual, List<CategoryRank> expected)
    {
        if (actual == null || expected == null || actual.Count != expected.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (actual[i].Category != expected[i].Category || actual[i].AmountFen != expected[i].AmountFen ||
                Mathf.Abs(actual[i].Ratio - expected[i].Ratio) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool RankItemsMatch(RectTransform rankContent, List<CategoryRank> expected)
    {
        RankItemUI[] items = rankContent.GetComponentsInChildren<RankItemUI>();

        if (items.Length != expected.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            string percentText = Mathf.RoundToInt(expected[i].Ratio * 100f) + "%";

            if (items[i].NameText != expected[i].Category ||
                items[i].PercentText != percentText ||
                items[i].AmountText != MoneyText.FormatTrim(expected[i].AmountFen) ||
                Mathf.Abs(items[i].FillAmountValue - Mathf.Clamp01(expected[i].Ratio)) > 0.01f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PeriodRestored(AccountManager accountManager, string start, string end, List<AccountRecord> baseline)
    {
        List<AccountRecord> now = accountManager.GetRecordsInRange(start, end);

        if (now.Count != baseline.Count)
        {
            return false;
        }

        return SumRecords(now, RecordType.Expense, start, end) == SumRecords(baseline, RecordType.Expense, start, end) &&
            SumRecords(now, RecordType.Income, start, end) == SumRecords(baseline, RecordType.Income, start, end);
    }

    private static List<AccountRecord> Join(List<AccountRecord> a, List<AccountRecord> b)
    {
        List<AccountRecord> all = new List<AccountRecord>(a);
        all.AddRange(b);
        return all;
    }

    private static AccountRecord MakeUnitRecord(int type, string category, long amountFen, string date)
    {
        AccountRecord record = new AccountRecord();
        record.Type = type;
        record.Category = category;
        record.AmountFen = amountFen;
        record.Date = date;
        record.CreatedAt = "2026-09-26 12:00:00";
        record.Note = "自检数据";
        return record;
    }

    private static AccountRecord CreateRecord(AccountManager manager, int type, string category,
        long amountFen, DateTime date, DateTime createdAt)
    {
        AccountRecord record = new AccountRecord();
        record.Type = type;
        record.Category = category;
        record.AmountFen = amountFen;
        record.Date = date.ToString("yyyy-MM-dd");
        record.CreatedAt = createdAt.ToString("yyyy-MM-dd HH:mm:ss");
        record.Note = "自检数据";

        if (manager.AddRecord(record) == false)
        {
            Debug.LogError("[ChartCheck] FAIL - 测试记录创建失败：" + category);
        }

        return record;
    }

    private static void AppendIfNull(System.Text.StringBuilder sb, string name, UnityEngine.Object obj)
    {
        if (obj == null)
        {
            sb.Append(" ").Append(name);
        }
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[ChartCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
