using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 03 验收：明细页流程自检（Play 模式，菜单 AccountBook/07-明细页流程自检）。
/// 自动进 Play → 直接经 AccountManager 造跨日测试数据 → 校验分组/组头合计/排序/金额格式颜色 →
/// 翻月空态 → 点击行进编辑 → 长按删除确认 → 清理测试数据 → 退 Play。结果逐条打 [DetailCheck] 日志。
/// 全程基线感知：只对本次自建数据做断言，既有真实数据不受影响并在结束时还原。
/// </summary>
public static class DetailFlowCheck
{
    private const string MenuItemPath = "AccountBook/07-明细页流程自检";
    private const string StartedKey = "AccountBook.DetailCheck.Started";
    private const string DoneKey = "AccountBook.DetailCheck.Done";

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
            Debug.LogError("[DetailCheck] 异常：" + ex);
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

        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        DetailPanelUI detail = UnityEngine.Object.FindFirstObjectByType<DetailPanelUI>(FindObjectsInactive.Include);
        RecordPanelUI panel = UnityEngine.Object.FindFirstObjectByType<RecordPanelUI>(FindObjectsInactive.Include);
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();

        if (uiManager == null || detail == null || panel == null || accountManager == null)
        {
            Debug.LogError("[DetailCheck] FAIL - 场景组件缺失（UIManager/DetailPanelUI/RecordPanelUI/AccountManager）。");
            return;
        }

        SerializedObject soDetail = new SerializedObject(detail);
        Button btnPrevMonth = soDetail.FindProperty("btnPrevMonth").objectReferenceValue as Button;
        Button btnNextMonth = soDetail.FindProperty("btnNextMonth").objectReferenceValue as Button;
        TextMeshProUGUI txtYearMonth = soDetail.FindProperty("txtYearMonth").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI txtIncome = soDetail.FindProperty("txtIncome").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI txtExpense = soDetail.FindProperty("txtExpense").objectReferenceValue as TextMeshProUGUI;
        RectTransform listContent = soDetail.FindProperty("listContent").objectReferenceValue as RectTransform;
        GameObject emptyLabel = soDetail.FindProperty("emptyLabel").objectReferenceValue as GameObject;
        ConfirmDialog confirmBox = detail.ConfirmBox;

        if (btnPrevMonth == null || btnNextMonth == null || txtYearMonth == null || txtIncome == null ||
            txtExpense == null || listContent == null || emptyLabel == null || confirmBox == null)
        {
            Debug.LogError("[DetailCheck] FAIL - 关键引用缺失，请先运行菜单 AccountBook/06-搭建明细页。");
            return;
        }

        // —— 基线：涉及月份的既有记录与合计 ——
        DateTime today = DateTime.Now.Date;
        DateTime yesterday = today.AddDays(-1);
        DateTime prevMonth15 = today.AddMonths(-1);
        prevMonth15 = new DateTime(prevMonth15.Year, prevMonth15.Month, 15);
        string currentMonthKey = today.ToString("yyyy-MM");
        string yesterdayMonthKey = yesterday.ToString("yyyy-MM");
        string prevMonthKey = prevMonth15.ToString("yyyy-MM");
        string farMonthKey = today.AddMonths(3).ToString("yyyy-MM");

        Dictionary<string, int> baselineCounts = new Dictionary<string, int>();
        Dictionary<string, long> baselineExpense = new Dictionary<string, long>();
        Dictionary<string, long> baselineIncome = new Dictionary<string, long>();
        string[] monthKeys = { currentMonthKey, yesterdayMonthKey, prevMonthKey, farMonthKey };

        foreach (string key in monthKeys)
        {
            List<AccountRecord> records = accountManager.GetRecordsByMonth(key);
            accountManager.GetMonthSummary(key, out long incomeFen, out long expenseFen);
            baselineCounts[key] = records.Count;
            baselineExpense[key] = expenseFen;
            baselineIncome[key] = incomeFen;
        }

        long baselineTodayExpense = GetDayExpense(accountManager.GetRecordsByMonth(currentMonthKey), today.ToString("yyyy-MM-dd"));

        // —— 造测试数据（CreatedAt 排开控制组内顺序；补记账 E 的 CreatedAt 是补记时刻，复刻真实补记场景） ——
        List<string> createdIds = new List<string>();
        AccountRecord a = CreateRecord(accountManager, createdIds, (int)RecordType.Expense, "餐饮", 2880,
            today.ToString("yyyy-MM-dd"), today.AddHours(9));
        AccountRecord b = CreateRecord(accountManager, createdIds, (int)RecordType.Expense, "交通", 1030,
            today.ToString("yyyy-MM-dd"), today.AddHours(10));
        AccountRecord c = CreateRecord(accountManager, createdIds, (int)RecordType.Income, "工资", 60000,
            today.ToString("yyyy-MM-dd"), today.AddHours(11));
        AccountRecord d = CreateRecord(accountManager, createdIds, (int)RecordType.Expense, "购物", 1500,
            yesterday.ToString("yyyy-MM-dd"), yesterday.AddHours(20));
        AccountRecord e = CreateRecord(accountManager, createdIds, (int)RecordType.Expense, "餐饮", 5000,
            prevMonth15.ToString("yyyy-MM-dd"), DateTime.Now);   // 补记：CreatedAt=现在，Date=上月15

        // 本次自建数据按月累计的期望增量
        Dictionary<string, long> addedExpense = new Dictionary<string, long>();
        Dictionary<string, long> addedIncome = new Dictionary<string, long>();

        foreach (string id in createdIds)
        {
            AccountRecord record = FindById(accountManager.GetRecordsByMonth(currentMonthKey), id) ??
                FindById(accountManager.GetRecordsByMonth(prevMonthKey), id);
            string key = record.Date.Substring(0, 7);

            if (record.Type == (int)RecordType.Expense)
            {
                addedExpense[key] = GetOrZero(addedExpense, key) + record.AmountFen;
            }
            else
            {
                addedIncome[key] = GetOrZero(addedIncome, key) + record.AmountFen;
            }
        }

        // 1) 打开明细页 → 年月标题 + 本月合计
        uiManager.OpenDetailPanel();
        allPass &= Check("明细页激活", detail.gameObject.activeInHierarchy);
        allPass &= Check("年月标题格式 2026年 09月",
            txtYearMonth.text == today.ToString("yyyy") + "年 " + today.ToString("MM") + "月");
        allPass &= Check("本月支出合计=基线+自建",
            txtExpense.text == MoneyText.FormatYuan(baselineExpense[currentMonthKey] + GetOrZero(addedExpense, currentMonthKey)));
        allPass &= Check("本月收入合计=基线+自建",
            txtIncome.text == MoneyText.FormatYuan(baselineIncome[currentMonthKey] + GetOrZero(addedIncome, currentMonthKey)));

        // 2) 分组：今天组头存在，星期与日期正确，组头支出=基线+当日自建（收入不上组头）
        DayHeaderUI todayHeader = FindHeader(listContent, today.ToString("MM月dd日"));
        allPass &= Check("今天组头存在且格式 MM月dd日", todayHeader != null);
        allPass &= Check("组头星期为" + WeekdayName(today.DayOfWeek),
            todayHeader != null && todayHeader.DateText.EndsWith(WeekdayName(today.DayOfWeek)));
        allPass &= Check("组头当日支出=基线+当日自建(28.8+10.3)",
            todayHeader != null &&
            todayHeader.ExpenseText == "支出: " + MoneyText.FormatTrim(baselineTodayExpense + 2880 + 1030));

        // 3) 组内排序：CreatedAt 降序 → 工资(11点) > 交通(10点) > 餐饮(9点)；金额格式与颜色
        List<RecordItemUI> todayItems = GetGroupItems(listContent, todayHeader);
        int indexA = todayItems.FindIndex(item => item.Record != null && item.Record.Id == a.Id);
        int indexB = todayItems.FindIndex(item => item.Record != null && item.Record.Id == b.Id);
        int indexC = todayItems.FindIndex(item => item.Record != null && item.Record.Id == c.Id);
        allPass &= Check("今天组内三笔自建记录齐全", indexA >= 0 && indexB >= 0 && indexC >= 0);
        allPass &= Check("组内按 CreatedAt 降序（工资>交通>餐饮）", indexC < indexB && indexB < indexA);

        if (indexA >= 0 && indexB >= 0 && indexC >= 0)
        {
            allPass &= Check("支出行金额 -28.80", todayItems[indexA].AmountText == "-28.80");
            allPass &= Check("支出行金额色近黑", todayItems[indexA].AmountColor == MoneyText.ColorExpense);
            allPass &= Check("收入行金额 +600.00", todayItems[indexC].AmountText == "+600.00");
            allPass &= Check("收入行金额色为强调色 #FFA000", todayItems[indexC].AmountColor == MoneyText.ColorIncome);
        }

        // 4) 翻月空态：+3 个月 → 空态文案；再 -3 回来数据复原
        string farMonthLabel = today.AddMonths(3).ToString("yyyy") + "年 " + today.AddMonths(3).ToString("MM") + "月";
        btnNextMonth.onClick.Invoke();
        btnNextMonth.onClick.Invoke();
        btnNextMonth.onClick.Invoke();
        bool farEmpty = accountManager.GetRecordsByMonth(farMonthKey).Count == 0;
        allPass &= Check("翻月后标题+3月", txtYearMonth.text == farMonthLabel);
        allPass &= Check("空月显示占位文案", emptyLabel.activeSelf == farEmpty && (farEmpty == false || listContent.childCount == 0));

        btnPrevMonth.onClick.Invoke();
        btnPrevMonth.onClick.Invoke();
        btnPrevMonth.onClick.Invoke();
        allPass &= Check("翻回本月标题复原", txtYearMonth.text == today.ToString("yyyy") + "年 " + today.ToString("MM") + "月");
        allPass &= Check("翻回后今天组头仍在", FindHeader(listContent, today.ToString("MM月dd日")) != null);

        // 5) 点击行 → 编辑模式（记账页回填），取消后回明细页不落库
        todayItems = GetGroupItems(listContent, FindHeader(listContent, today.ToString("MM月dd日")));
        RecordItemUI itemA = todayItems.Find(item => item.Record != null && item.Record.Id == a.Id);
        int countBeforeEdit = accountManager.GetRecordsByMonth(currentMonthKey).Count;
        itemA.SimulateClick();
        TextMeshProUGUI amountText = new SerializedObject(panel).FindProperty("amountText").objectReferenceValue as TextMeshProUGUI;
        allPass &= Check("点击行 → 记账页激活、明细页隐藏",
            panel.gameObject.activeSelf && detail.gameObject.activeSelf == false);
        allPass &= Check("编辑回填金额 28.80", amountText != null && amountText.text == "28.80");
        Button btnCancel = FindChildButton(panel.transform, "btnClose");
        btnCancel.onClick.Invoke();
        allPass &= Check("取消后回明细页且未落库",
            detail.gameObject.activeInHierarchy && accountManager.GetRecordsByMonth(currentMonthKey).Count == countBeforeEdit);

        // 6) 长按删除：确认弹窗 → 取消不删 → 再长按确认删除 → 列表与组头自动刷新（OnDataChanged）
        todayItems = GetGroupItems(listContent, FindHeader(listContent, today.ToString("MM月dd日")));
        RecordItemUI itemB = todayItems.Find(item => item.Record != null && item.Record.Id == b.Id);
        itemB.SimulateLongPress();
        allPass &= Check("长按 → 确认弹窗打开", confirmBox.gameObject.activeSelf);

        SerializedObject soDialog = new SerializedObject(confirmBox);
        Button dialogCancel = soDialog.FindProperty("cancelButton").objectReferenceValue as Button;
        Button dialogConfirm = soDialog.FindProperty("confirmButton").objectReferenceValue as Button;
        dialogCancel.onClick.Invoke();
        allPass &= Check("弹窗取消 → 记录仍在", FindById(accountManager.GetRecordsByMonth(currentMonthKey), b.Id) != null);

        todayItems = GetGroupItems(listContent, FindHeader(listContent, today.ToString("MM月dd日")));
        itemB = todayItems.Find(item => item.Record != null && item.Record.Id == b.Id);
        itemB.SimulateLongPress();
        dialogConfirm.onClick.Invoke();
        allPass &= Check("确认删除 → 记录落库移除", FindById(accountManager.GetRecordsByMonth(currentMonthKey), b.Id) == null);
        allPass &= Check("列表自动刷新 → 该行消失",
            GetGroupItems(listContent, FindHeader(listContent, today.ToString("MM月dd日"))).FindIndex(
                item => item.Record != null && item.Record.Id == b.Id) < 0);
        allPass &= Check("组头合计同步更新（去掉10.3）",
            FindHeader(listContent, today.ToString("MM月dd日")).ExpenseText ==
            "支出: " + MoneyText.FormatTrim(baselineTodayExpense + 2880));

        // 7) 清理测试数据，还原基线（含自愈：此前自检崩溃残留的同标记数据一并清除）
        foreach (string id in createdIds)
        {
            accountManager.DeleteRecord(id);
        }

        int swept = 0;

        foreach (string key in monthKeys)
        {
            List<AccountRecord> leftovers = accountManager.GetRecordsByMonth(key);

            for (int i = leftovers.Count - 1; i >= 0; i--)
            {
                if (leftovers[i] != null && leftovers[i].Note == "自检数据")
                {
                    accountManager.DeleteRecord(leftovers[i].Id);
                    swept++;
                }
            }
        }

        if (swept > 0)
        {
            Debug.Log($"[DetailCheck] 自愈清扫了 {swept} 笔此前崩溃残留的测试数据。");
        }

        foreach (string key in monthKeys)
        {
            accountManager.GetMonthSummary(key, out long incomeFen, out long expenseFen);
            allPass &= Check($"[{key}] 记录数还原基线", accountManager.GetRecordsByMonth(key).Count == baselineCounts[key]);
            allPass &= Check($"[{key}] 支出合计还原基线", expenseFen == baselineExpense[key]);
            allPass &= Check($"[{key}] 收入合计还原基线", incomeFen == baselineIncome[key]);
        }

        Debug.Log($"[DetailCheck] ===== 明细页流程自检{(allPass ? "全部通过" : "存在失败项")} =====");
    }

    // ---------- 工具 ----------

    private static AccountRecord CreateRecord(AccountManager manager, List<string> createdIds, int type,
        string category, long amountFen, string date, DateTime createdAt)
    {
        AccountRecord record = new AccountRecord();
        record.Type = type;
        record.Category = category;
        record.AmountFen = amountFen;
        record.Date = date;
        record.CreatedAt = createdAt.ToString("yyyy-MM-dd HH:mm:ss");
        record.Note = "自检数据";

        if (manager.AddRecord(record))
        {
            createdIds.Add(record.Id);
            return record;
        }

        Debug.LogError("[DetailCheck] FAIL - 测试记录创建失败：" + category);
        return record;
    }

    private static AccountRecord FindById(List<AccountRecord> records, string id)
    {
        return records.Find(record => record != null && record.Id == id);
    }

    private static long GetOrZero(Dictionary<string, long> dict, string key)
    {
        return dict.ContainsKey(key) ? dict[key] : 0;
    }

    private static long GetDayExpense(List<AccountRecord> records, string yyyyMMdd)
    {
        long total = 0;

        for (int i = 0; i < records.Count; i++)
        {
            if (records[i] != null && records[i].Date == yyyyMMdd && records[i].Type == (int)RecordType.Expense)
            {
                total += records[i].AmountFen;
            }
        }

        return total;
    }

    private static string WeekdayName(DayOfWeek day)
    {
        string[] names = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
        return names[(int)day];
    }

    /// <summary>
    /// 在列表容器里找指定日期前缀（"09月26日"）的组头
    /// </summary>
    private static DayHeaderUI FindHeader(RectTransform listContent, string datePrefix)
    {
        for (int i = 0; i < listContent.childCount; i++)
        {
            DayHeaderUI header = listContent.GetChild(i).GetComponent<DayHeaderUI>();

            if (header != null && header.DateText.StartsWith(datePrefix))
            {
                return header;
            }
        }

        return null;
    }

    /// <summary>
    /// 收集指定组头之下、下一个组头之前的全部记录行
    /// </summary>
    private static List<RecordItemUI> GetGroupItems(RectTransform listContent, DayHeaderUI header)
    {
        List<RecordItemUI> items = new List<RecordItemUI>();

        if (header == null)
        {
            return items;
        }

        bool inGroup = false;

        for (int i = 0; i < listContent.childCount; i++)
        {
            Transform child = listContent.GetChild(i);
            DayHeaderUI childHeader = child.GetComponent<DayHeaderUI>();

            if (childHeader != null)
            {
                if (inGroup)
                {
                    break;   // 已越过目标组
                }

                inGroup = childHeader == header;
                continue;
            }

            if (inGroup)
            {
                RecordItemUI item = child.GetComponent<RecordItemUI>();

                if (item != null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    /// <summary>
    /// 按名递归找子按钮（记账页 btnClose 未序列化在 RecordPanelUI 上）
    /// </summary>
    private static Button FindChildButton(Transform root, string name)
    {
        if (root.name == name)
        {
            return root.GetComponent<Button>();
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Button found = FindChildButton(root.GetChild(i), name);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[DetailCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
