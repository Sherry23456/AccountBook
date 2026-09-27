using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 图表页截图（步骤04 验收辅助）：图表流程自检结束退 Play 后自动触发第二轮 Play，
/// 造演示数据（本周多分类 + 上月补记）→ 截周视图 → 点今日节点截气泡 → 切月档位截月视图 →
/// 清理演示数据 → 退 Play。产物：D:\Qklunity\AccountBook\.shots\chart_week.png、
/// chart_bubble.png、chart_month.png（.shots 已 gitignore）。
/// </summary>
public static class ChartPanelShot
{
    private const string PendingKey = "AccountBook.ChartShot.Pending";
    private const string ArmedKey = "AccountBook.ChartShot.Armed";
    private const string TakenKey = "AccountBook.ChartShot.Taken";
    private const string ShotPathWeek = "D:\\Qklunity\\AccountBook\\.shots\\chart_week.png";
    private const string ShotPathBubble = "D:\\Qklunity\\AccountBook\\.shots\\chart_bubble.png";
    private const string ShotPathMonth = "D:\\Qklunity\\AccountBook\\.shots\\chart_month.png";

    [InitializeOnLoadMethod]
    private static void Init()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            // 图表流程自检已完成且截图待执行 → 再次进 Play 截图
            if (SessionState.GetBool(TakenKey, false) == false &&
                SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                SessionState.SetBool(ArmedKey, true);
                Debug.Log("[ChartShot] 流程自检结束，进入截图 Play 会话...");
                EditorApplication.isPlaying = true;
            }

            return;
        }

        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ArmedKey, false))
        {
            SessionState.SetBool(ArmedKey, false);
            EditorApplication.delayCall += OpenPanelAndCapture;
        }
    }

    /// <summary>
    /// 造演示数据 → 45 帧截周视图 → 60 帧点今日节点 → 75 帧截气泡 → 85 帧切月 →
    /// 110 帧截月视图 → 125 帧清理 → 145 帧退 Play
    /// </summary>
    private static void OpenPanelAndCapture()
    {
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        ChartPanelUI chart = UnityEngine.Object.FindFirstObjectByType<ChartPanelUI>(FindObjectsInactive.Include);
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();

        if (accountManager == null || chart == null || uiManager == null)
        {
            Debug.LogError("[ChartShot] 找不到 AccountManager/ChartPanelUI/UIManager。");
            EditorApplication.isPlaying = false;
            return;
        }

        uiManager.OpenChartPanel();

        // 演示数据：今天 5 笔支出 + 1 笔收入（排行榜 6 分类触发"其他"合并）、周一与昨天各一笔、上月补记一笔
        DateTime today = DateTime.Now.Date;
        DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateTime dayG = today == monday ? monday : today.AddDays(-1);
        DateTime prevMonth15 = today.AddMonths(-1);
        prevMonth15 = new DateTime(prevMonth15.Year, prevMonth15.Month, 15);
        System.Collections.Generic.List<string> createdIds = new System.Collections.Generic.List<string>();

        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "餐饮", 2880, today, today.AddHours(9)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "零食", 1500, today, today.AddHours(8)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "交通", 1030, today, today.AddHours(10)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "购物", 600, today, today.AddHours(11)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "水果", 990, today, today.AddHours(7)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Income, "工资", 600000, today, today.AddHours(12)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "餐饮", 5000, monday, monday.AddHours(8)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "娱乐", 3300, dayG, dayG.AddHours(20)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "购物", 12000, prevMonth15, DateTime.Now));

        Debug.Log("[ChartShot] 演示数据已就位，等待布局稳定后截图。");

        Directory.CreateDirectory(Path.GetDirectoryName(ShotPathWeek));

        Toggle toggleMonth = new SerializedObject(chart).FindProperty("toggleMonth").objectReferenceValue as Toggle;
        LineChartGraphic lineChart = new SerializedObject(chart).FindProperty("lineChart").objectReferenceValue as LineChartGraphic;
        int todayIdx = (today - monday).Days;

        int frame = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;

        void Tick()
        {
            frame++;

            if (frame == 45)
            {
                ScreenCapture.CaptureScreenshot(ShotPathWeek, 2);
                Debug.Log("[ChartShot] 已请求周视图截图：" + ShotPathWeek);
            }

            if (frame == 60)
            {
                try
                {
                    lineChart.SimulateNodeClick(todayIdx);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ChartShot] 点节点失败：" + ex.Message);
                }
            }

            if (frame == 75)
            {
                ScreenCapture.CaptureScreenshot(ShotPathBubble, 2);
                Debug.Log("[ChartShot] 已请求节点气泡截图：" + ShotPathBubble);
            }

            if (frame == 85 && toggleMonth != null)
            {
                toggleMonth.isOn = true;   // RefreshAll 会顺带隐藏气泡
            }

            if (frame == 110)
            {
                ScreenCapture.CaptureScreenshot(ShotPathMonth, 2);
                Debug.Log("[ChartShot] 已请求月视图截图：" + ShotPathMonth);
            }

            if (frame == 125)
            {
                for (int i = 0; i < createdIds.Count; i++)
                {
                    accountManager.DeleteRecord(createdIds[i]);
                }

                // 自愈：清扫此前崩溃/中断残留的同标记演示数据
                System.Collections.Generic.List<AccountRecord> leftovers =
                    accountManager.GetRecordsInRange("0000-01-01", "9999-12-31");

                for (int i = leftovers.Count - 1; i >= 0; i--)
                {
                    if (leftovers[i] != null && leftovers[i].Note == "截图演示")
                    {
                        accountManager.DeleteRecord(leftovers[i].Id);
                    }
                }

                Debug.Log("[ChartShot] 演示数据已清理。");
            }

            if (frame >= 145)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(TakenKey, true);
                EditorApplication.isPlaying = false;
            }
        }
    }

    private static string AddDemoRecord(AccountManager manager, int type, string category, long amountFen,
        DateTime date, DateTime createdAt)
    {
        AccountRecord record = new AccountRecord();
        record.Type = type;
        record.Category = category;
        record.AmountFen = amountFen;
        record.Date = date.ToString("yyyy-MM-dd");
        record.CreatedAt = createdAt.ToString("yyyy-MM-dd HH:mm:ss");
        record.Note = "截图演示";

        manager.AddRecord(record);
        return record.Id;
    }
}
