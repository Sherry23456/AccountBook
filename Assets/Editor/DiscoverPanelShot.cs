using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 发现页截图（步骤09 验收辅助，菜单 AccountBook/16b-发现页截图）：
/// 进 Play → 种测试数据 + 设预算 → 依次打开 发现页/账单页(月)/账单页(年)/预算页(月档含分类)/
/// 预算弹窗/预算页(年档) → 每态等 40 帧截图 → 还原数据 → 退 Play。
/// 产物：D:\Qklunity\AccountBook\.shots\discover_*.png。注意：Game 视图需前台可见（后台截不出，步骤08 坑）。
/// </summary>
public static class DiscoverPanelShot
{
    private const string MenuItemPath = "AccountBook/16b-发现页截图";
    private const string StartedKey = "AccountBook.DiscoverShot.Started";
    private const string SeedNote = "[DiscoverShot]";
    private const int FramesPerState = 40;

    private static readonly string[] ShotNames =
        { "discover_main", "discover_bill_month", "discover_bill_year", "discover_budget_month",
          "discover_budget_dialog", "discover_budget_year" };

    private static int frame;
    private static int state;

    [MenuItem(MenuItemPath)]
    internal static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[DiscoverShot] 请在编辑模式运行（本脚本自带进出 Play）。");
            return;
        }

        SessionState.SetBool(StartedKey, true);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void HookAfterReload()
    {
        if (SessionState.GetBool(StartedKey, false))
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += StartCapture;
        }
    }

    private static List<AccountRecord> backupRecords;
    private static BudgetData backupBudgets;

    private static void StartCapture()
    {
        // 备份真实数据（记录 + 预算），结束时还原（种数据前先自愈清扫残留种子）
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        BudgetManager budgetManager = UnityEngine.Object.FindFirstObjectByType<BudgetManager>(FindObjectsInactive.Include);

        List<AccountRecord> pre = accountManager.GetAllRecords();
        List<string> stale = new List<string>();

        foreach (AccountRecord record in pre)
        {
            if (record != null && record.Note == SeedNote)
            {
                stale.Add(record.Id);
            }
        }

        foreach (string staleId in stale)
        {
            accountManager.DeleteRecord(staleId);
        }

        backupRecords = accountManager.GetAllRecords();
        backupBudgets = budgetManager.ExportForTest();
        Debug.Log($"[DiscoverShot] 已备份 {backupRecords.Count} 笔记录与预算数据。");

        SeedData();
        frame = 0;
        state = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        ApplyState(0);
        Debug.Log("[DiscoverShot] 开始截图序列（共 " + ShotNames.Length + " 态）。");
    }

    /// <summary>
    /// 种演示数据（带标记，结束时按标记清扫）：当月三笔支出+一笔收入、上月/去年各一笔，
    /// 月总预算 2000、分类预算 餐饮500/交通300、年总预算 24000 —— 让六张图都有内容
    /// </summary>
    private static void SeedData()
    {
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        BudgetManager budgetManager = UnityEngine.Object.FindFirstObjectByType<BudgetManager>(FindObjectsInactive.Include);
        DateTime now = DateTime.Now;
        string curMonthKey = now.ToString("yyyy-MM");
        string curYear = now.Year.ToString("D4");
        int lastMonth = now.Month == 1 ? 12 : now.Month - 1;
        int lastMonthYear = now.Month == 1 ? now.Year - 1 : now.Year;

        accountManager.AddRecord(MakeSeed((int)RecordType.Expense, "餐饮", 3260, now.ToString("yyyy-MM-") + "05"));
        accountManager.AddRecord(MakeSeed((int)RecordType.Expense, "交通", 1580, now.ToString("yyyy-MM-") + "06"));
        accountManager.AddRecord(MakeSeed((int)RecordType.Expense, "日用", 9900, now.ToString("yyyy-MM-") + "06"));
        accountManager.AddRecord(MakeSeed((int)RecordType.Income, "工资", 500000, now.ToString("yyyy-MM-") + "10"));
        accountManager.AddRecord(MakeSeed((int)RecordType.Expense, "购物", 8600, lastMonthYear + "-" + lastMonth.ToString("D2") + "-15"));
        accountManager.AddRecord(MakeSeed((int)RecordType.Expense, "餐饮", 4200, "2025-06-18"));
        accountManager.AddRecord(MakeSeed((int)RecordType.Income, "兼职", 83900, "2025-08-02"));

        budgetManager.SetMonthBudget(curMonthKey, 200000);
        budgetManager.SetCategoryBudget(curMonthKey, "餐饮", 50000);
        budgetManager.SetCategoryBudget(curMonthKey, "交通", 30000);
        budgetManager.SetYearBudget(curYear, 2400000);
    }

    /// <summary>
    /// 帧驱动：每态等 FramesPerState 帧截图，走完还原数据退 Play
    /// </summary>
    private static void Tick()
    {
        frame++;

        if (frame == FramesPerState)
        {
            string path = "D:\\Qklunity\\AccountBook\\.shots\\" + ShotNames[state] + ".png";
            ScreenCapture.CaptureScreenshot(path, 1);
            Debug.Log("[DiscoverShot] 已请求截图：" + path);
        }

        if (frame >= FramesPerState * 2)
        {
            state++;

            if (state >= ShotNames.Length)
            {
                EditorApplication.update -= Tick;
                FinishAndRestore();
                return;
            }

            frame = 0;
            ApplyState(state);
        }
    }

    private static void ApplyState(int stateIndex)
    {
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        DiscoverPanelUI discoverUi = UnityEngine.Object.FindFirstObjectByType<DiscoverPanelUI>(FindObjectsInactive.Include);
        BillPanelUI billUi = UnityEngine.Object.FindFirstObjectByType<BillPanelUI>(FindObjectsInactive.Include);
        BudgetPanelUI budgetUi = UnityEngine.Object.FindFirstObjectByType<BudgetPanelUI>(FindObjectsInactive.Include);
        BudgetDialogUI dialogUi = UnityEngine.Object.FindFirstObjectByType<BudgetDialogUI>(FindObjectsInactive.Include);

        switch (stateIndex)
        {
            case 0:   // 发现页
                uiManager.OpenDiscoverPanel();
                break;

            case 1:   // 账单页·月账单
                uiManager.OpenBillPanel();
                break;

            case 2:   // 账单页·年账单
                GetBtn(billUi, "btnYear").onClick.Invoke();
                break;

            case 3:   // 预算页·月档（总预算卡 + 分类两行）
                uiManager.OpenBudgetPanel();
                break;

            case 4:   // 预算弹窗（每月总预算）
                GetBtn(budgetUi, "btnEdit").onClick.Invoke();
                break;

            case 5:   // 预算页·年档
                dialogUi.Close();
                GetBtn(budgetUi, "btnPickYear").onClick.Invoke();
                break;
        }
    }

    private static void FinishAndRestore()
    {
        try
        {
            AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
            BudgetManager budgetManager = UnityEngine.Object.FindFirstObjectByType<BudgetManager>(FindObjectsInactive.Include);
            UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();

            // 双保险：种子按标记清扫 + 预算/记录整份还原
            List<AccountRecord> records = accountManager.GetAllRecords();
            List<string> staleIds = new List<string>();

            foreach (AccountRecord record in records)
            {
                if (record != null && record.Note == SeedNote)
                {
                    staleIds.Add(record.Id);
                }
            }

            foreach (string staleId in staleIds)
            {
                accountManager.DeleteRecord(staleId);
            }

            if (backupRecords != null)
            {
                accountManager.ReplaceAllRecords(backupRecords);
            }

            if (backupBudgets != null)
            {
                budgetManager.RestoreForTest(backupBudgets);
            }

            Debug.Log($"[DiscoverShot] 六张截图完成，清扫 {staleIds.Count} 笔种子，还原现场。");
            uiManager.OpenDetailPanel();
        }
        catch (Exception ex)
        {
            Debug.LogError("[DiscoverShot] 还原失败：" + ex);
        }
        finally
        {
            SessionState.SetBool(StartedKey, false);
            EditorApplication.isPlaying = false;
        }
    }

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

    private static Button GetBtn(MonoBehaviour host, string fieldName)
    {
        SerializedObject so = new SerializedObject(host);
        return so.FindProperty(fieldName)?.objectReferenceValue as Button;
    }
}
