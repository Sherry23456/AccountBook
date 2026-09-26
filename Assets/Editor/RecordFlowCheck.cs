using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 02 验收：记账流程自检（Play 模式，菜单 AccountBook/05-记账流程自检）。
/// 自动进 Play → 模拟页签/宫格/键盘点击 → 校验金额状态机（连加/小数位/退格/置灰）与 JSON 落盘
/// （新增/编辑同 Id 更新/取消不落库）→ 清理测试数据 → 退出 Play。结果逐条打 [RecordCheck] 日志。
/// </summary>
public static class RecordFlowCheck
{
    private const string MenuItemPath = "AccountBook/05-记账流程自检";
    private const string StartedKey = "AccountBook.RecordCheck.Started";
    private const string DoneKey = "AccountBook.RecordCheck.Done";

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
            Debug.LogError("[RecordCheck] 异常：" + ex);
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

        AppFlowManager flow = UnityEngine.Object.FindFirstObjectByType<AppFlowManager>();
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        RecordPanelUI panel = UnityEngine.Object.FindFirstObjectByType<RecordPanelUI>(FindObjectsInactive.Include);
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();

        if (flow == null || uiManager == null || panel == null || accountManager == null)
        {
            Debug.LogError("[RecordCheck] FAIL - 场景组件缺失（AppFlowManager/UIManager/RecordPanelUI/AccountManager）。");
            return;
        }

        SerializedObject soFlow = new SerializedObject(flow);
        Button btnRecord = soFlow.FindProperty("btnRecord").objectReferenceValue as Button;

        SerializedObject soPanel = new SerializedObject(panel);
        Button tabExpense = soPanel.FindProperty("tabExpenseButton").objectReferenceValue as Button;
        Button tabIncome = soPanel.FindProperty("tabIncomeButton").objectReferenceValue as Button;
        TextMeshProUGUI amountText = soPanel.FindProperty("amountText").objectReferenceValue as TextMeshProUGUI;
        Button doneButton = soPanel.FindProperty("doneButton").objectReferenceValue as Button;
        Button plusButton = soPanel.FindProperty("plusButton").objectReferenceValue as Button;
        Button minusButton = soPanel.FindProperty("minusButton").objectReferenceValue as Button;
        Button dotButton = soPanel.FindProperty("dotButton").objectReferenceValue as Button;
        Button backspaceButton = soPanel.FindProperty("backspaceButton").objectReferenceValue as Button;

        SerializedProperty digitsProp = soPanel.FindProperty("digitButtons");
        Button[] digits = new Button[10];
        bool digitsOk = digitsProp != null && digitsProp.arraySize == 10;

        if (digitsOk)
        {
            for (int i = 0; i < 10; i++)
            {
                digits[i] = digitsProp.GetArrayElementAtIndex(i).objectReferenceValue as Button;
                digitsOk &= digits[i] != null;
            }
        }

        Button todayButton = soPanel.FindProperty("todayButton").objectReferenceValue as Button;
        GameObject datePickerPanel = soPanel.FindProperty("datePickerPanel").objectReferenceValue as GameObject;
        Button btnCancel = FindChildButton(panel.transform, "btnClose");

        if (btnRecord == null || amountText == null || doneButton == null || btnCancel == null ||
            tabExpense == null || tabIncome == null || plusButton == null || minusButton == null ||
            dotButton == null || backspaceButton == null || todayButton == null || datePickerPanel == null ||
            digitsOk == false)
        {
            Debug.LogError("[RecordCheck] FAIL - 关键引用缺失，请先运行菜单 AccountBook/04-搭建记账页。");
            return;
        }

        string month = DateTime.Now.ToString("yyyy-MM");
        string prevMonthKey = DateTime.Now.Date.AddMonths(-1).ToString("yyyy-MM");
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        List<string> baselineIds = GetRecordIds(accountManager.GetRecordsByMonth(month));
        int baselineCount = baselineIds.Count;
        List<string> prevBaselineIds = GetRecordIds(accountManager.GetRecordsByMonth(prevMonthKey));
        int prevBaselineCount = prevBaselineIds.Count;
        List<string> createdIds = new List<string>();

        // 1) 打开记账页（新增模式）
        btnRecord.onClick.Invoke();
        allPass &= Check("记账按钮 → 记账页激活", panel.gameObject.activeSelf);
        allPass &= Check("默认支出宫格 31 项", panel.GridItemCount == 31);
        allPass &= Check("初始金额显示 0.00", amountText.text == "0.00");
        allPass &= Check("空输入时完成键置灰", doneButton.interactable == false);

        // 2) 连加：3 + 8 → 完成 = 11.00
        digits[3].onClick.Invoke();
        allPass &= Check("按下 3 → 显示 3", amountText.text == "3");
        plusButton.onClick.Invoke();
        digits[8].onClick.Invoke();
        allPass &= Check("3 + 8 → 进行中式显示 3+8", amountText.text == "3+8");
        doneButton.onClick.Invoke();

        List<AccountRecord> monthRecords = accountManager.GetRecordsByMonth(month);
        AccountRecord added = FindNewRecord(monthRecords, baselineIds, createdIds);
        allPass &= Check("完成 → 落库 1100 分/支出/餐饮/今天",
            added != null && added.AmountFen == 1100 && added.Type == (int)RecordType.Expense &&
            added.Category == "餐饮" && added.Date == today);

        if (added != null)
        {
            createdIds.Add(added.Id);
        }

        GameObject detailPanel = new SerializedObject(uiManager).FindProperty("detailPanel").objectReferenceValue as GameObject;
        allPass &= Check("保存后回到明细页且记账页关闭", detailPanel != null && detailPanel.activeSelf && panel.gameObject.activeSelf == false);

        // 3) 小数位：12.345 的第 5 位按不进去 → 12.34
        btnRecord.onClick.Invoke();
        digits[1].onClick.Invoke();
        digits[2].onClick.Invoke();
        dotButton.onClick.Invoke();
        digits[3].onClick.Invoke();
        digits[4].onClick.Invoke();
        digits[5].onClick.Invoke();
        allPass &= Check("小数第三位按不进去 → 12.34", amountText.text == "12.34");
        doneButton.onClick.Invoke();

        monthRecords = accountManager.GetRecordsByMonth(month);
        AccountRecord added2 = FindNewRecord(monthRecords, baselineIds, createdIds);
        allPass &= Check("12.34 → 落库 1234 分", added2 != null && added2.AmountFen == 1234);

        if (added2 != null)
        {
            createdIds.Add(added2.Id);
        }

        // 4) 编辑模式：改金额 → 同 Id 更新而非新增
        panel.SetupForEdit(added);
        panel.gameObject.SetActive(true);
        allPass &= Check("编辑回填金额 11.00", amountText.text == "11.00");
        backspaceButton.onClick.Invoke();
        backspaceButton.onClick.Invoke();
        digits[5].onClick.Invoke();
        allPass &= Check("编辑改金额 → 11.5", amountText.text == "11.5");
        doneButton.onClick.Invoke();

        monthRecords = accountManager.GetRecordsByMonth(month);
        AccountRecord edited = monthRecords.Find(r => r.Id == added.Id);
        allPass &= Check("编辑保存：同 Id 金额 1150、总数不变、日期保留",
            edited != null && edited.AmountFen == 1150 && monthRecords.Count == baselineCount + 2 && edited.Date == today);

        // 5) 减号显示 + 退格 + 取消不落库
        btnRecord.onClick.Invoke();
        digits[5].onClick.Invoke();
        minusButton.onClick.Invoke();
        digits[3].onClick.Invoke();
        allPass &= Check("5 - 3 → 显示 5-3", amountText.text == "5-3");
        backspaceButton.onClick.Invoke();
        allPass &= Check("退格后回到连加结果 5", amountText.text == "5");
        btnCancel.onClick.Invoke();
        monthRecords = accountManager.GetRecordsByMonth(month);
        allPass &= Check("取消 → 不落库、回明细页",
            monthRecords.Count == baselineCount + 2 && panel.gameObject.activeSelf == false);

        // 6) 页签切换
        btnRecord.onClick.Invoke();
        tabIncome.onClick.Invoke();
        allPass &= Check("收入宫格 6 项", panel.GridItemCount == 6);
        tabExpense.onClick.Invoke();
        allPass &= Check("切回支出宫格 31 项", panel.GridItemCount == 31);
        btnCancel.onClick.Invoke();

        // 7) 日期补记：翻上月选 15 号 → 落库日期为上月 15 日
        btnRecord.onClick.Invoke();
        todayButton.onClick.Invoke();
        allPass &= Check("点今天键 → 日期弹窗打开", datePickerPanel.activeSelf);

        DatePickerPanel picker = datePickerPanel.GetComponent<DatePickerPanel>();
        SerializedObject soPicker = new SerializedObject(picker);
        Button btnPrevMonth = soPicker.FindProperty("prevMonthButton").objectReferenceValue as Button;
        TextMeshProUGUI pickerTitle = soPicker.FindProperty("titleLabel").objectReferenceValue as TextMeshProUGUI;

        btnPrevMonth.onClick.Invoke();
        DateTime targetDate = DateTime.Now.Date.AddMonths(-1);
        targetDate = new DateTime(targetDate.Year, targetDate.Month, 15);
        allPass &= Check("翻上月 → 标题正确", pickerTitle != null && pickerTitle.text == targetDate.ToString("yyyy年M月"));

        Button dayCell = FindDayCell(picker, "15");
        allPass &= Check("上月存在可点的 15 号日格", dayCell != null);

        if (dayCell != null)
        {
            dayCell.onClick.Invoke();
        }

        TextMeshProUGUI todayKeyLabel = todayButton.GetComponentInChildren<TextMeshProUGUI>();
        allPass &= Check("确认后弹窗关闭、键面显示 MM-dd",
            datePickerPanel.activeSelf == false && todayKeyLabel != null && todayKeyLabel.text == targetDate.ToString("MM-dd"));

        digits[2].onClick.Invoke();
        doneButton.onClick.Invoke();

        List<AccountRecord> prevMonthRecords = accountManager.GetRecordsByMonth(prevMonthKey);
        AccountRecord backdated = FindNewRecord(prevMonthRecords, prevBaselineIds, createdIds);
        allPass &= Check("补记 → 落库 2 元于上月 15 日",
            backdated != null && backdated.Date == targetDate.ToString("yyyy-MM-dd") && backdated.AmountFen == 200);

        if (backdated != null)
        {
            createdIds.Add(backdated.Id);
        }

        btnCancel.onClick.Invoke();

        // 8) 清理测试数据
        for (int i = 0; i < createdIds.Count; i++)
        {
            accountManager.DeleteRecord(createdIds[i]);
        }

        allPass &= Check("当月测试数据已清理", accountManager.GetRecordsByMonth(month).Count == baselineCount);
        allPass &= Check("上月测试数据已清理", accountManager.GetRecordsByMonth(prevMonthKey).Count == prevBaselineCount);

        Debug.Log($"[RecordCheck] ===== 记账流程自检{(allPass ? "全部通过" : "存在失败项")} =====");
    }

    /// <summary>
    /// 月记录 Id 集合（区分自建测试数据与既有数据）
    /// </summary>
    private static List<string> GetRecordIds(List<AccountRecord> records)
    {
        List<string> ids = new List<string>();

        for (int i = 0; i < records.Count; i++)
        {
            if (records[i] != null)
            {
                ids.Add(records[i].Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// 找出本次自检新建的一条记录（不在基线、也未登记过）
    /// </summary>
    private static AccountRecord FindNewRecord(List<AccountRecord> records, List<string> baselineIds, List<string> createdIds)
    {
        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null || baselineIds.Contains(record.Id) || createdIds.Contains(record.Id))
            {
                continue;
            }

            return record;
        }

        return null;
    }

    /// <summary>
    /// 在月历日格里找指定日号的可点日格（其他月/未来日不可点，会跳过）
    /// </summary>
    private static Button FindDayCell(DatePickerPanel picker, string dayText)
    {
        SerializedObject soPicker = new SerializedObject(picker);
        SerializedProperty dayProp = soPicker.FindProperty("dayButtons");

        if (dayProp == null)
        {
            return null;
        }

        for (int i = 0; i < dayProp.arraySize; i++)
        {
            Button cell = dayProp.GetArrayElementAtIndex(i).objectReferenceValue as Button;

            if (cell == null || cell.interactable == false)
            {
                continue;
            }

            TextMeshProUGUI label = cell.GetComponentInChildren<TextMeshProUGUI>();

            if (label != null && label.text == dayText)
            {
                return cell;
            }
        }

        return null;
    }

    /// <summary>
    /// 按名递归找子按钮（btnClose 未序列化在 RecordPanelUI 上）
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
        Debug.Log($"[RecordCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
