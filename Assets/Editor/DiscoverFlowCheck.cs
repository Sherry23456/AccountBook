using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 09 验收：发现页功能流程自检（Play 模式，菜单 AccountBook/16a-发现页流程自检）。
/// 自动进 Play → 备份真实数据（记录 + 预算）→ 种测试数据 → 全链路校验：
/// 发现页两卡片数值/圆环 → 账单卡跳账单页（月/年档、年份下拉、月行跳明细页）→
/// 预算卡跳预算页（总预算卡 + 编辑弹窗改额 + 添加/编辑/删除分类预算 + 年档总预算）→
/// 还原真实数据（记录 + 预算）→ 退 Play。全程基线感知：期望值由"快照 + 种子"独立聚合而得。
/// </summary>
public static class DiscoverFlowCheck
{
    private const string MenuItemPath = "AccountBook/16a-发现页流程自检";
    private const string StartedKey = "AccountBook.DiscoverCheck.Started";
    private const string DoneKey = "AccountBook.DiscoverCheck.Done";
    private const string SeedNote = "[DiscoverCheck]";

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

    private static List<AccountRecord> backupRecords;
    private static BudgetData backupBudgets;

    private static void DoCheck()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        try
        {
            RunChecks();
        }
        catch (Exception ex)
        {
            Debug.LogError("[DiscoverCheck] 异常：" + ex);
        }
        finally
        {
            // 崩溃中断也必须还原现场（上一轮曾在还原前崩出，残留测试数据靠自愈清扫兜底）
            RestoreBackup();
            SessionState.SetBool(DoneKey, true);
            EditorApplication.isPlaying = false;
        }
    }

    private static void RestoreBackup()
    {
        if (backupRecords == null && backupBudgets == null)
        {
            return;
        }

        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        BudgetManager budgetManager = UnityEngine.Object.FindFirstObjectByType<BudgetManager>(FindObjectsInactive.Include);

        if (accountManager != null && backupRecords != null)
        {
            accountManager.ReplaceAllRecords(backupRecords);
            Debug.Log($"[DiscoverCheck] 兜底还原记录 {backupRecords.Count} 笔。");
        }

        if (budgetManager != null && backupBudgets != null)
        {
            budgetManager.RestoreForTest(backupBudgets);
            Debug.Log("[DiscoverCheck] 兜底还原预算数据。");
        }

        backupRecords = null;
        backupBudgets = null;
    }

    private static void RunChecks()
    {
        bool allPass = true;

        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        BudgetManager budgetManager = UnityEngine.Object.FindFirstObjectByType<BudgetManager>(FindObjectsInactive.Include);
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        DiscoverPanelUI discoverUi = UnityEngine.Object.FindFirstObjectByType<DiscoverPanelUI>(FindObjectsInactive.Include);
        BillPanelUI billUi = UnityEngine.Object.FindFirstObjectByType<BillPanelUI>(FindObjectsInactive.Include);
        BudgetPanelUI budgetUi = UnityEngine.Object.FindFirstObjectByType<BudgetPanelUI>(FindObjectsInactive.Include);
        BudgetDialogUI dialogUi = UnityEngine.Object.FindFirstObjectByType<BudgetDialogUI>(FindObjectsInactive.Include);
        DetailPanelUI detailUi = UnityEngine.Object.FindFirstObjectByType<DetailPanelUI>(FindObjectsInactive.Include);

        allPass &= Check("七个控制器全部就位", accountManager != null && budgetManager != null && uiManager != null
            && discoverUi != null && billUi != null && budgetUi != null && dialogUi != null && detailUi != null);

        if (accountManager == null || budgetManager == null || uiManager == null
            || discoverUi == null || billUi == null || budgetUi == null || dialogUi == null)
        {
            Debug.LogError("[DiscoverCheck] 控制器缺失，中止。");
            return;
        }

        // ============ 备份 + 种数据 ============
        // 自愈清扫：上次自检异常中断可能残留种子（按备注标记识别），先扫掉再备份
        List<AccountRecord> preExisting = accountManager.GetAllRecords();
        List<string> staleIds = new List<string>();

        foreach (AccountRecord record in preExisting)
        {
            if (record != null && record.Note == SeedNote)
            {
                staleIds.Add(record.Id);
            }
        }

        if (staleIds.Count > 0)
        {
            foreach (string staleId in staleIds)
            {
                accountManager.DeleteRecord(staleId);
            }

            Debug.Log($"[DiscoverCheck] 自愈清扫了 {staleIds.Count} 笔上次残留的测试记录。");
        }

        List<AccountRecord> snapshot = accountManager.GetAllRecords();
        BudgetData budgetSnapshot = budgetManager.ExportForTest();
        backupRecords = snapshot;
        backupBudgets = budgetSnapshot;
        Debug.Log($"[DiscoverCheck] 已备份 {snapshot.Count} 笔记录与预算数据。");

        DateTime now = DateTime.Now;
        string curMonthKey = now.ToString("yyyy-MM");
        string curYear = now.Year.ToString("D4");
        int lastMonth = now.Month == 1 ? 12 : now.Month - 1;
        int lastMonthYear = now.Month == 1 ? now.Year - 1 : now.Year;

        List<AccountRecord> seeds = new List<AccountRecord>
        {
            MakeSeed((int)RecordType.Expense, "餐饮", 2000, now.ToString("yyyy-MM-") + "05"),
            MakeSeed((int)RecordType.Expense, "交通", 500, now.ToString("yyyy-MM-") + "08"),
            MakeSeed((int)RecordType.Income, "工资", 100000, now.ToString("yyyy-MM-") + "10"),
            MakeSeed((int)RecordType.Expense, "购物", 1500, lastMonthYear + "-" + lastMonth.ToString("D2") + "-15"),
            MakeSeed((int)RecordType.Expense, "餐饮", 3000, (now.Year - 1) + "-06-18"),
            MakeSeed((int)RecordType.Income, "兼职", 800, (now.Year - 1) + "-06-20"),
        };

        foreach (AccountRecord seed in seeds)
        {
            accountManager.AddRecord(seed);
        }

        // ============ 独立聚合期望值（快照 + 种子） ============
        Dictionary<string, long[]> byMonth = new Dictionary<string, long[]>();
        Dictionary<string, long[]> byYear = new Dictionary<string, long[]>();

        List<AccountRecord> all = accountManager.GetAllRecords();

        foreach (AccountRecord record in all)
        {
            if (record == null || string.IsNullOrEmpty(record.Date) || record.Date.Length < 7)
            {
                continue;
            }

            string monthKey = record.Date.Substring(0, 7);
            string yearKey = record.Date.Substring(0, 4);

            if (!byMonth.ContainsKey(monthKey))
            {
                byMonth[monthKey] = new long[2];
            }

            if (!byYear.ContainsKey(yearKey))
            {
                byYear[yearKey] = new long[2];
            }

            int slot = record.Type == (int)RecordType.Income ? 0 : 1;
            byMonth[monthKey][slot] += record.AmountFen;
            byYear[yearKey][slot] += record.AmountFen;
        }

        long curIncome = byMonth.ContainsKey(curMonthKey) ? byMonth[curMonthKey][0] : 0;
        long curExpense = byMonth.ContainsKey(curMonthKey) ? byMonth[curMonthKey][1] : 0;

        int curYearMonths = 0;

        foreach (KeyValuePair<string, long[]> kv in byMonth)
        {
            if (kv.Key.StartsWith(curYear) && (kv.Value[0] > 0 || kv.Value[1] > 0))
            {
                curYearMonths++;
            }
        }

        // ============ 一、发现页 ============
        uiManager.OpenDiscoverPanel();
        allPass &= Check("发现页已激活", discoverUi.gameObject.activeSelf);

        TextMeshProUGUI txtBillMonth = GetTmp(discoverUi, "txtBillMonth");
        TextMeshProUGUI txtBillIncome = GetTmp(discoverUi, "txtBillIncome");
        TextMeshProUGUI txtBillExpense = GetTmp(discoverUi, "txtBillExpense");
        TextMeshProUGUI txtBillBalance = GetTmp(discoverUi, "txtBillBalance");

        allPass &= Check($"账单卡月份 = {now.Month} 月", txtBillMonth != null && txtBillMonth.text == now.Month + " 月");
        allPass &= Check("账单卡收入 = 独立聚合值", txtBillIncome != null && txtBillIncome.text == MoneyText.FormatYuan(curIncome));
        allPass &= Check("账单卡支出 = 独立聚合值", txtBillExpense != null && txtBillExpense.text == MoneyText.FormatYuan(curExpense));
        allPass &= Check("账单卡结余 = 收入-支出", txtBillBalance != null
            && txtBillBalance.text == MoneyText.FormatYuan(curIncome - curExpense));

        // 未设预算：预算卡置灰态（基线感知：用户可能已设当月预算，此时只对账不断言置灰）
        long preBudget = budgetManager.GetMonthBudget(curMonthKey);
        TextMeshProUGUI txtBudget0 = GetTmp(discoverUi, "txtBudget");

        if (preBudget > 0)
        {
            allPass &= Check("已设基线预算时预算卡对账", txtBudget0 != null
                && txtBudget0.text == MoneyText.FormatYuan(preBudget));
        }
        else
        {
            allPass &= Check("未设预算时预算卡显示 0.00 置灰", txtBudget0 != null
                && txtBudget0.text == "0.00" && txtBudget0.color == new Color32(0xC8, 0xC8, 0xC8, 0xFF));
        }

        // 设预算 → 卡片刷新
        budgetManager.SetMonthBudget(curMonthKey, 50000);

        TextMeshProUGUI txtBudget = GetTmp(discoverUi, "txtBudget");
        TextMeshProUGUI txtSpent = GetTmp(discoverUi, "txtSpent");
        TextMeshProUGUI txtRemain = GetTmp(discoverUi, "txtRemain");
        TextMeshProUGUI txtPercent = GetTmp(discoverUi, "txtRingPercent");
        RingGraphic ringFill = GetRing(discoverUi, "ringFill");
        long expectRemain = 50000 - curExpense;
        float expectPercent = Mathf.Clamp01((float)((double)expectRemain / 50000));

        allPass &= Check("设预算后预算 = 500.00", txtBudget != null && txtBudget.text == "500.00");
        allPass &= Check("设预算后支出 = 当月支出", txtSpent != null && txtSpent.text == MoneyText.FormatYuan(curExpense));
        allPass &= Check("设预算后剩余 = 预算-支出", txtRemain != null && txtRemain.text == MoneyText.FormatYuan(expectRemain));
        allPass &= Check("圆环进度 = 剩余比例", ringFill != null && ringFill.gameObject.activeSelf
            && Mathf.Abs(ringFill.Progress - expectPercent) < 0.01f);
        allPass &= Check("圆环百分比文案", txtPercent != null
            && txtPercent.text == Mathf.RoundToInt(expectPercent * 100f) + "%");

        // ============ 二、账单页 ============
        Button billCard = GetBtn(discoverUi, "billCardButton");
        billCard.onClick.Invoke();
        allPass &= Check("点账单卡 → 账单页激活、发现页隐藏",
            billUi.gameObject.activeSelf && discoverUi.gameObject.activeSelf == false);

        allPass &= Check($"月账单行数 = {curYearMonths}（{curYear} 年有流水月份）", billUi.RowCount == curYearMonths);

        Button btnYear = GetBtn(billUi, "btnYear");
        btnYear.onClick.Invoke();
        TextMeshProUGUI txtCaption = GetTmp(billUi, "txtCaption");
        TextMeshProUGUI txtValue = GetTmp(billUi, "txtValue");
        GameObject footerNote = GetGo(billUi, "footerNote");

        long totalIncome = 0;
        long totalExpense = 0;

        foreach (KeyValuePair<string, long[]> kv in byYear)
        {
            totalIncome += kv.Value[0];
            totalExpense += kv.Value[1];
        }

        allPass &= Check("年账单档：总结余文案", txtCaption != null && txtCaption.text == "总结余");
        allPass &= Check("年账单档：总结余 = 全期收入-支出", txtValue != null
            && txtValue.text == MoneyText.FormatYuan(totalIncome - totalExpense));
        allPass &= Check("年账单档：脚注可见", footerNote != null && footerNote.activeSelf);
        allPass &= Check($"年账单行数 = {byYear.Count} 年", billUi.RowCount == byYear.Count);

        // 年份下拉
        Button btnYearPicker = GetBtn(billUi, "btnYearPicker");
        btnYearPicker.onClick.Invoke();
        GameObject pickerPopup = GetGo(billUi, "yearPickerPopup");
        allPass &= Check("年份下拉已打开", pickerPopup != null && pickerPopup.activeSelf);
        allPass &= Check("年份下拉选项 ≥ 2（当前年 + 2025 种子）", pickerPopup != null
            && pickerPopup.transform.Find("Viewport/Content").childCount >= 2);

        Transform pickerContent = pickerPopup.transform.Find("Viewport/Content");
        pickerContent.GetChild(0).GetComponent<Button>().onClick.Invoke();
        allPass &= Check("选年份后下拉关闭", pickerPopup != null && pickerPopup.activeSelf == false);
        TextMeshProUGUI txtYearLabel = GetTmp(billUi, "txtYearLabel");
        allPass &= Check("年份标签刷新", txtYearLabel != null && txtYearLabel.text.Contains("年 ▼"));

        // 回月账单档，点首月行跳明细
        Button btnMonth = GetBtn(billUi, "btnMonth");
        btnMonth.onClick.Invoke();

        Transform billContent = billUi.ListContent;
        Button firstRow = billContent.GetChild(0).GetComponent<Button>();
        firstRow.onClick.Invoke();

        TextMeshProUGUI detailTitle = GetTmp(detailUi, "txtYearMonth");
        allPass &= Check("点月行 → 明细页激活", detailUi.gameObject.activeSelf);
        allPass &= Check("明细页定位到该月", detailTitle != null
            && detailTitle.text == now.Year + "年 " + now.Month.ToString("D2") + "月");

        uiManager.OpenDiscoverPanel();

        // ============ 三、预算页（月档） ============
        Button budgetCard = GetBtn(discoverUi, "budgetCardButton");
        budgetCard.onClick.Invoke();
        allPass &= Check("点预算卡 → 预算页激活", budgetUi.gameObject.activeSelf);

        TextMeshProUGUI txtCardTitle = GetTmp(budgetUi, "txtCardTitle");
        allPass &= Check("总预算卡标题 = 10月总预算（当前月）", txtCardTitle != null
            && txtCardTitle.text == now.Month + "月总预算");
        allPass &= Check("总预算卡可见（已设预算）", GetGo(budgetUi, "totalCard") != null
            && GetGo(budgetUi, "totalCard").activeSelf);

        // 编辑弹窗：改额
        Button btnEdit = GetBtn(budgetUi, "btnEdit");
        btnEdit.onClick.Invoke();
        allPass &= Check("编辑 → 预算弹窗打开（每月总预算）", dialogUi.gameObject.activeSelf && dialogUi.Mode == BudgetDialogUI.DialogMode.MonthTotal);
        allPass &= Check("弹窗回填当前预算 500", dialogUi.InputText == "500");

        PressBackspace(dialogUi, 3);
        allPass &= Check("退格清空后显示占位文案", dialogUi.InputText == "请输入预算金额");
        Button confirm = GetBtn(dialogUi, "btnConfirm");
        allPass &= Check("空金额确定键置灰", confirm != null && confirm.interactable == false);

        PressDigits(dialogUi, "300");
        allPass &= Check("键入 300 后显示 300", dialogUi.InputText == "300");
        confirm.onClick.Invoke();
        allPass &= Check("确定后月总预算 = 300.00 元", budgetManager.GetMonthBudget(curMonthKey) == 30000);
        allPass &= Check("确定后弹窗关闭", dialogUi.gameObject.activeSelf == false);
        allPass &= Check("预算页刷新为 300.00", GetTmp(budgetUi, "txtBudget") != null
            && GetTmp(budgetUi, "txtBudget").text == "300.00");

        // 添加分类预算
        Button btnAddCategory = GetBtn(budgetUi, "btnAddCategory");
        btnAddCategory.onClick.Invoke();
        allPass &= Check("添加分类预算 → 弹窗（分类宫格模式）", dialogUi.gameObject.activeSelf
            && dialogUi.Mode == BudgetDialogUI.DialogMode.CategoryAdd);
        GameObject categoryScroll = GetGo(dialogUi, "categoryScroll");
        allPass &= Check("分类宫格已显示", categoryScroll != null && categoryScroll.activeSelf);
        allPass &= Check("空金额+未选分类确定置灰", confirm.interactable == false);

        Button firstGridButton = GetGridItemButton(dialogUi);
        allPass &= Check("分类宫格首项（餐饮）可点", firstGridButton != null);

        if (firstGridButton != null)
        {
            firstGridButton.onClick.Invoke();
        }

        allPass &= Check("选中分类后仍需金额", confirm != null && confirm.interactable == false);

        PressDigits(dialogUi, "100");
        confirm.onClick.Invoke();
        allPass &= Check("分类预算已存（餐饮 100.00）", budgetManager.GetCategoryBudget(curMonthKey, "餐饮") == 10000);
        allPass &= Check("预算页分类行数 = 1", budgetUi.CategoryRowCount == 1);

        // 编辑分类预算 → 改额（Content 第 0 项是常驻空态标签，须取第一行带 Button 的行）
        Transform catContent = GetRect(budgetUi, "categoryContent");
        Button firstCatRow = GetFirstRowButton(catContent);
        allPass &= Check("分类行可点（带 Button）", firstCatRow != null);

        if (firstCatRow != null)
        {
            firstCatRow.onClick.Invoke();
        }
        allPass &= Check("点分类行 → 编辑分类预算弹窗", dialogUi.Mode == BudgetDialogUI.DialogMode.CategoryEdit);
        Button btnDelete = GetBtn(dialogUi, "btnDeleteBudget");
        allPass &= Check("编辑模式含删除预算入口", btnDelete != null && btnDelete.gameObject.activeSelf);
        PressBackspace(dialogUi, 3);
        PressDigits(dialogUi, "200");
        confirm.onClick.Invoke();
        allPass &= Check("改额后分类预算 = 200.00", budgetManager.GetCategoryBudget(curMonthKey, "餐饮") == 20000);

        // 删除分类预算
        Button firstCatRowAgain = GetFirstRowButton(catContent);

        if (firstCatRowAgain != null)
        {
            firstCatRowAgain.onClick.Invoke();
        }

        btnDelete.onClick.Invoke();
        allPass &= Check("删除后分类预算清零", budgetManager.GetCategoryBudget(curMonthKey, "餐饮") == 0);
        // 空态标签常驻在 Content 里（随行有无切换显隐），恢复 = 空态可见且无数据行
        GameObject categoryEmptyGo = GetGo(budgetUi, "categoryEmpty");
        allPass &= Check("分类空态恢复（空态可见 + 无数据行）", categoryEmptyGo != null
            && categoryEmptyGo.activeSelf && GetFirstRowButton(GetRect(budgetUi, "categoryContent")) == null);

        // ============ 四、预算页（年档） ============
        Button btnPickYear = GetBtn(budgetUi, "btnPickYear");
        btnPickYear.onClick.Invoke();
        TextMeshProUGUI txtModeTitle = GetTmp(budgetUi, "txtModeTitle");
        allPass &= Check("切年档：标题变 年预算 ▼", txtModeTitle != null && txtModeTitle.text == "年预算 ▼");
        GameObject emptyState = GetGo(budgetUi, "emptyState");
        allPass &= Check("年档未设预算：空态可见", emptyState != null && emptyState.activeSelf);
        allPass &= Check("年档不显示分类区", GetGo(budgetUi, "categoryArea") != null
            && GetGo(budgetUi, "categoryArea").activeSelf == false);

        Button btnEmptySet = GetBtn(budgetUi, "btnEmptySet");
        btnEmptySet.onClick.Invoke();
        allPass &= Check("空态设置 → 年度总预算弹窗", dialogUi.gameObject.activeSelf
            && dialogUi.Mode == BudgetDialogUI.DialogMode.YearTotal);
        PressDigits(dialogUi, "12000");
        confirm.onClick.Invoke();
        allPass &= Check("年度总预算已存（12000.00）", budgetManager.GetYearBudget(curYear) == 1200000);
        allPass &= Check("年档总预算卡标题 = 2026年总预算", GetTmp(budgetUi, "txtCardTitle") != null
            && GetTmp(budgetUi, "txtCardTitle").text == now.Year + "年总预算");
        TextMeshProUGUI lbBudget = GetTmp(budgetUi, "lbBudget");
        TextMeshProUGUI lbSpent = GetTmp(budgetUi, "lbSpent");
        allPass &= Check("年档行标签切换为 本年预算/本年支出", lbBudget != null && lbSpent != null
            && lbBudget.text == "本年预算:" && lbSpent.text == "本年支出:");

        // 年份预算值与独立聚合对账
        long yearExpense = byYear.ContainsKey(curYear) ? byYear[curYear][1] : 0;
        allPass &= Check("年档支出 = 独立聚合值", GetTmp(budgetUi, "txtSpent") != null
            && GetTmp(budgetUi, "txtSpent").text == MoneyText.FormatYuan(yearExpense));

        // ============ 四b、返回键遮挡回归（09a：全宽档位标题条曾盖住 BtnBack 的射线） ============
        // 不走 EventSystem 屏幕射线——Game 视图在后台缩放时画布布局会滞后，坐标映射不可信；
        // 改为纯层级几何：收集"矩形包含按钮中心点"的活跃可射线 Graphic，按 UI 兄弟序链取最上层。
        Button budgetBack = GetBtn(budgetUi, "btnBack");
        Button modeTitle = GetBtn(budgetUi, "btnModeTitle");
        allPass &= Check("返回键/档位标题键引用就位", budgetBack != null && modeTitle != null);

        if (budgetBack != null && modeTitle != null)
        {
            RectTransform canvasRt = (RectTransform)budgetBack.GetComponentInParent<Canvas>(true).transform;
            GameObject topAtBack = TopmostRaycastableAt(canvasRt, CanvasLocalOf((RectTransform)budgetBack.transform, canvasRt));
            allPass &= Check("返回键中心最上层射线命中 = BtnBack（不被档位标题条遮挡）",
                topAtBack == budgetBack.gameObject);

            GameObject topAtTitle = TopmostRaycastableAt(canvasRt, CanvasLocalOf((RectTransform)modeTitle.transform, canvasRt));
            allPass &= Check("档位标题中心最上层命中 = ModeTitle（重排后标题仍可点）",
                topAtTitle == modeTitle.gameObject);

            GetBtn(budgetUi, "btnModeTitle").onClick.Invoke();
            allPass &= Check("点档位标题 → 弹层打开", GetGo(budgetUi, "modePopup") != null
                && GetGo(budgetUi, "modePopup").activeSelf);

            Button modeBlocker = GetGo(budgetUi, "modeBlocker") != null
                ? GetGo(budgetUi, "modeBlocker").GetComponent<Button>() : null;
            modeBlocker.onClick.Invoke();
            allPass &= Check("点遮罩 → 弹层关闭", GetGo(budgetUi, "modePopup") != null
                && GetGo(budgetUi, "modePopup").activeSelf == false);

            budgetBack.onClick.Invoke();
            allPass &= Check("点返回 → 回发现页", discoverUi.gameObject.activeSelf
                && budgetUi.gameObject.activeSelf == false);
        }

        // ============ 五、预算弹窗关闭路径 ============
        GetBtn(budgetUi, "btnEdit").onClick.Invoke();
        Button maskButton = GetBtn(dialogUi, "maskButton");
        maskButton.onClick.Invoke();
        allPass &= Check("点遮罩关闭弹窗", dialogUi.gameObject.activeSelf == false);

        // ============ 还原 ============
        RestoreBackup();
        allPass &= Check("真实记录已还原", accountManager.GetAllRecords().Count == snapshot.Count);
        allPass &= Check("真实预算已还原（整份 JSON 比对）",
            JsonUtility.ToJson(budgetManager.ExportForTest()) == JsonUtility.ToJson(budgetSnapshot));

        Debug.Log($"[DiscoverCheck] ===== 发现页功能流程自检 {(allPass ? "全部通过" : "失败(见上方 FAIL 日志)")} =====");
    }

    // ---------- 工具 ----------

    private static AccountRecord MakeSeed(int type, string category, long amountFen, string date)
    {
        AccountRecord record = new AccountRecord();
        record.Id = Guid.NewGuid().ToString("N");
        record.Type = type;
        record.Category = category;
        record.AmountFen = amountFen;
        record.Date = date;
        record.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        record.Note = SeedNote;
        return record;
    }

    private static TextMeshProUGUI GetTmp(MonoBehaviour host, string fieldName)
    {
        SerializedObject so = new SerializedObject(host);
        return so.FindProperty(fieldName)?.objectReferenceValue as TextMeshProUGUI;
    }

    private static RingGraphic GetRing(MonoBehaviour host, string fieldName)
    {
        SerializedObject so = new SerializedObject(host);
        return so.FindProperty(fieldName)?.objectReferenceValue as RingGraphic;
    }

    private static Button GetBtn(MonoBehaviour host, string fieldName)
    {
        SerializedObject so = new SerializedObject(host);
        return so.FindProperty(fieldName)?.objectReferenceValue as Button;
    }

    private static GameObject GetGo(MonoBehaviour host, string fieldName)
    {
        SerializedObject so = new SerializedObject(host);
        return so.FindProperty(fieldName)?.objectReferenceValue as GameObject;
    }

    private static RectTransform GetRect(MonoBehaviour host, string fieldName)
    {
        SerializedObject so = new SerializedObject(host);
        return so.FindProperty(fieldName)?.objectReferenceValue as RectTransform;
    }

    private static void PressDigits(BudgetDialogUI dialog, string digits)
    {
        SerializedObject so = new SerializedObject(dialog);
        SerializedProperty digitProps = so.FindProperty("digitButtons");
        Button dotButton = so.FindProperty("dotButton")?.objectReferenceValue as Button;

        foreach (char c in digits)
        {
            if (c == '.')
            {
                dotButton.onClick.Invoke();
                continue;
            }

            int digit = c - '0';
            Button button = digitProps.GetArrayElementAtIndex(digit).objectReferenceValue as Button;
            button.onClick.Invoke();
        }
    }

    private static void PressBackspace(BudgetDialogUI dialog, int times)
    {
        SerializedObject so = new SerializedObject(dialog);
        Button backspace = so.FindProperty("backspaceButton")?.objectReferenceValue as Button;

        for (int i = 0; i < times; i++)
        {
            backspace.onClick.Invoke();
        }
    }

    /// <summary>
    /// 分类宫格首项的按钮（CategoryGridItem.clickButton，经序列化字段取，不依赖预制体结构）
    /// </summary>
    private static Button GetGridItemButton(BudgetDialogUI dialog)
    {
        SerializedObject so = new SerializedObject(dialog);
        RectTransform gridContent = so.FindProperty("gridContent")?.objectReferenceValue as RectTransform;

        if (gridContent == null || gridContent.childCount == 0)
        {
            return null;
        }

        CategoryGridItem item = gridContent.GetChild(0).GetComponent<CategoryGridItem>();
        return item != null ? new SerializedObject(item).FindProperty("clickButton")?.objectReferenceValue as Button : null;
    }

    /// <summary>
    /// 分类行容器里第一个带 Button 的行（Content 首项是常驻"未设置分类预算"空态标签）
    /// </summary>
    private static Button GetFirstRowButton(Transform content)
    {
        if (content == null)
        {
            return null;
        }

        for (int i = 0; i < content.childCount; i++)
        {
            Transform child = content.GetChild(i);

            if (child.name == "CategoryEmpty")
            {
                continue;
            }

            Button button = child.GetComponent<Button>();

            if (button != null)
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>
    /// UI 元素中心的屏幕坐标（Overlay 画布；用 rect.center 变换，避免 pivot 不在中心时取到角点）
    /// </summary>
    /// <summary>
    /// 元素中心点换算到画布局部坐标（纯 Transform 运算，不依赖 Game 视图当前尺寸/布局时效）
    /// </summary>
    private static Vector2 CanvasLocalOf(RectTransform rect, RectTransform canvasRt)
    {
        Vector3 world = rect.TransformPoint(rect.rect.center);
        return canvasRt.InverseTransformPoint(world);
    }

    /// <summary>
    /// 画布局部坐标处的"最上层可射线 Graphic"所属物体：
    /// 收集所有活跃且 raycastTarget 的 Graphic 中矩形包含该点的，再按 UI 兄弟序链取最上层
    /// （兄弟链 = 从画布到节点的 GetSiblingIndex 序列，字典序大者后建在上层，同 GraphicRaycaster 排序规则）
    /// </summary>
    private static GameObject TopmostRaycastableAt(RectTransform canvasRt, Vector2 canvasLocalPoint)
    {
        Vector3 world = canvasRt.TransformPoint(canvasLocalPoint);
        GameObject top = null;
        List<int> topChain = null;

        Graphic[] graphics = canvasRt.GetComponentsInChildren<Graphic>(true);

        foreach (Graphic graphic in graphics)
        {
            if (graphic.raycastTarget == false || graphic.gameObject.activeInHierarchy == false)
            {
                continue;
            }

            Vector3 local = graphic.rectTransform.InverseTransformPoint(world);

            if (graphic.rectTransform.rect.Contains(local) == false)
            {
                continue;
            }

            List<int> chain = SiblingChain(graphic.transform, canvasRt);

            if (top == null || CompareChain(chain, topChain) > 0)
            {
                top = graphic.gameObject;
                topChain = chain;
            }
        }

        return top;
    }

    private static List<int> SiblingChain(Transform node, Transform canvasTf)
    {
        List<int> chain = new List<int>();
        Transform current = node;

        while (current != null && current != canvasTf)
        {
            chain.Insert(0, current.GetSiblingIndex());
            current = current.parent;
        }

        return chain;
    }

    private static int CompareChain(List<int> a, List<int> b)
    {
        for (int i = 0; i < a.Count && i < b.Count; i++)
        {
            if (a[i] != b[i])
            {
                return a[i].CompareTo(b[i]);
            }
        }

        return a.Count.CompareTo(b.Count);
    }

    private static bool Check(string title, bool condition)
    {
        Debug.Log($"[DiscoverCheck] {(condition ? "PASS" : "FAIL")} - {title}");
        return condition;
    }
}
