using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 明细页截图（步骤03 验收辅助）：明细流程自检结束退 Play 后自动触发第二轮 Play，
/// 造 4 笔跨月演示数据 → 等布局稳定 → 截明细页 → 长按弹确认框再截一张 → 清理演示数据 → 退 Play。
/// 产物：D:\Qklunity\AccountBook\.shots\detail_panel.png、detail_confirm.png（.shots 已 gitignore）。
/// </summary>
public static class DetailPanelShot
{
    private const string PendingKey = "AccountBook.DetailShot.Pending";
    private const string ArmedKey = "AccountBook.DetailShot.Armed";
    private const string TakenKey = "AccountBook.DetailShot.Taken";
    private const string ShotPath = "D:\\Qklunity\\AccountBook\\.shots\\detail_panel.png";
    private const string ShotPathConfirm = "D:\\Qklunity\\AccountBook\\.shots\\detail_confirm.png";

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
            // 明细流程自检已完成且截图待执行 → 再次进 Play 截图
            if (SessionState.GetBool(TakenKey, false) == false &&
                SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                SessionState.SetBool(ArmedKey, true);
                Debug.Log("[DetailShot] 流程自检结束，进入截图 Play 会话...");
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
    /// 造演示数据 → 40 帧截明细页 → 70 帧长按弹确认框 → 100 帧截确认框 → 清理 → 140 帧退 Play
    /// </summary>
    private static void OpenPanelAndCapture()
    {
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        DetailPanelUI detail = UnityEngine.Object.FindFirstObjectByType<DetailPanelUI>(FindObjectsInactive.Include);

        if (accountManager == null || detail == null)
        {
            Debug.LogError("[DetailShot] 找不到 AccountManager/DetailPanelUI。");
            EditorApplication.isPlaying = false;
            return;
        }

        // 演示数据：今天两笔 + 昨天一笔 + 上月补记一笔（自建自清，不碰真实数据）
        DateTime today = DateTime.Now.Date;
        DateTime prevMonth15 = today.AddMonths(-1);
        prevMonth15 = new DateTime(prevMonth15.Year, prevMonth15.Month, 15);
        System.Collections.Generic.List<string> createdIds = new System.Collections.Generic.List<string>();

        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "餐饮", 2880, today.ToString("yyyy-MM-dd"), today.AddHours(9)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "交通", 1030, today.ToString("yyyy-MM-dd"), today.AddHours(10)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Income, "工资", 600000, today.ToString("yyyy-MM-dd"), today.AddHours(11)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "购物", 1500, today.AddDays(-1).ToString("yyyy-MM-dd"), today.AddHours(8)));
        createdIds.Add(AddDemoRecord(accountManager, (int)RecordType.Expense, "餐饮", 5000, prevMonth15.ToString("yyyy-MM-dd"), DateTime.Now));

        Debug.Log("[DetailShot] 演示数据已就位，等待布局稳定后截图。");

        Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));

        int frame = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;

        void Tick()
        {
            frame++;

            if (frame == 40)
            {
                ScreenCapture.CaptureScreenshot(ShotPath, 2);
                Debug.Log("[DetailShot] 已请求明细页截图：" + ShotPath);
            }

            if (frame == 70)
            {
                // 长按第一行弹操作弹窗（修改/删除）拍第二张
                try
                {
                    RecordItemUI firstItem = detail.ListContent.GetComponentInChildren<RecordItemUI>();

                    if (firstItem != null)
                    {
                        firstItem.SimulateLongPress();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DetailShot] 弹操作弹窗失败：" + ex.Message);
                }
            }

            if (frame == 100)
            {
                ScreenCapture.CaptureScreenshot(ShotPathConfirm, 2);
                Debug.Log("[DetailShot] 已请求确认框截图：" + ShotPathConfirm);
            }

            if (frame == 120)
            {
                try
                {
                    detail.ConfirmBox.Close();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DetailShot] 关闭确认框失败：" + ex.Message);
                }

                for (int i = 0; i < createdIds.Count; i++)
                {
                    accountManager.DeleteRecord(createdIds[i]);
                }

                // 自愈：清扫此前崩溃/中断残留的同标记演示数据
                string[] months = { today.ToString("yyyy-MM"), today.AddDays(-1).ToString("yyyy-MM"), prevMonth15.ToString("yyyy-MM") };

                foreach (string monthKey in months)
                {
                    System.Collections.Generic.List<AccountRecord> leftovers = accountManager.GetRecordsByMonth(monthKey);

                    for (int i = leftovers.Count - 1; i >= 0; i--)
                    {
                        if (leftovers[i] != null && leftovers[i].Note == "截图演示")
                        {
                            accountManager.DeleteRecord(leftovers[i].Id);
                        }
                    }
                }

                Debug.Log("[DetailShot] 演示数据已清理。");
            }

            if (frame >= 140)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(TakenKey, true);
                EditorApplication.isPlaying = false;
            }
        }
    }

    private static string AddDemoRecord(AccountManager manager, int type, string category, long amountFen, string date, DateTime createdAt)
    {
        AccountRecord record = new AccountRecord();
        record.Type = type;
        record.Category = category;
        record.AmountFen = amountFen;
        record.Date = date;
        record.CreatedAt = createdAt.ToString("yyyy-MM-dd HH:mm:ss");
        record.Note = "截图演示";

        manager.AddRecord(record);
        return record.Id;
    }
}
