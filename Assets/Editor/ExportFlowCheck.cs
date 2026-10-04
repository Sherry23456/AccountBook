using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 05 验收：导出流程自检（Play 模式，菜单 AccountBook/11-导出流程自检）。
/// 自动进 Play → ExcelExportService 单元（写临时 xlsx 读回逐格比对、空集拒绝）→ 文件名规则 →
/// 场景引用链（弹窗/Toast/TransferManager/图表页启用）→ UI 全流程：图表页点导出带入档位周期、
/// 预览基线感知、‹›平移、补记历史日期按 Date 落上周、空周期置灰且不产文件、真实导出读回
/// 行数与内容（周文件只含 7 天）、Toast 与自动关闭、月/年档标题、年档导出 → 截图 →
/// 清理自建数据/导出文件 → 基线还原断言 → 退 Play。
/// 全程基线感知：只对本次自建数据做断言，既有真实数据不受影响并在结束时还原。
/// </summary>
public static class ExportFlowCheck
{
    private const string MenuItemPath = "AccountBook/11-导出流程自检";
    private const string StartedKey = "AccountBook.ExportCheck.Started";
    private const string DoneKey = "AccountBook.ExportCheck.Done";
    private const string SelfCheckNote = "自检数据";
    private const string ShotPath = "D:\\Qklunity\\AccountBook\\.shots\\export_panel.png";
    private const double ShotWaitSeconds = 3.0;

    private static bool shotRequested = false;

    /// <summary>
    /// 入口（菜单）：进 Play → 自动跑全流程校验 → 截图 → 退 Play
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
    /// 进 Play 触发域重载会丢事件订阅，用 SessionState 记忆重挂（ChartFlowCheck 同款）
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

        bool allPass = true;

        try
        {
            allPass = RunChecks();
        }
        catch (Exception ex)
        {
            Debug.LogError("[ExportCheck] 异常：" + ex);
            allPass = false;
        }
        finally
        {
            SessionState.SetBool(DoneKey, true);

            if (shotRequested)
            {
                WaitShotThenExit(allPass);
            }
            else
            {
                EditorApplication.isPlaying = false;
            }
        }
    }

    /// <summary>
    /// 截图异步落盘：轮询等文件出现（上限 ShotWaitSeconds）再退 Play
    /// </summary>
    private static void WaitShotThenExit(bool allPass)
    {
        double elapsed = 0.0;
        EditorApplication.CallbackFunction tick = null;

        tick = () =>
        {
            elapsed += EditorApplication.timeSinceStartup;

            if (File.Exists(ShotPath) || elapsed > ShotWaitSeconds)
            {
                EditorApplication.update -= tick;
                allPass &= Check("[截图] export_panel.png 已落盘", File.Exists(ShotPath));
                Debug.Log($"[ExportCheck] ===== 导出流程自检{(allPass ? "全部通过" : "存在失败项")} =====");
                EditorApplication.isPlaying = false;
            }
        };

        EditorApplication.update += tick;
    }

    private static bool RunChecks()
    {
        bool allPass = true;

        // ============ 一、ExcelExportService 单元断言（临时 xlsx 读回逐格比对） ============
        string tempDir = Application.temporaryCachePath;
        string unitPath = Path.Combine(tempDir, "ab_export_unit_check.xlsx");
        TryDeleteFile(unitPath);

        List<AccountRecord> unitRecords = new List<AccountRecord>
        {
            MakeUnitRecord((int)RecordType.Expense, "餐饮", 1030, "2026-09-26", "午饭"),
            MakeUnitRecord((int)RecordType.Expense, "娱乐", 1500, "2026-09-26", "电影"),
            MakeUnitRecord((int)RecordType.Income, "兼职", 60000, "2026-09-27", "周末兼职"),
            MakeUnitRecord((int)RecordType.Expense, "交通", 500, "2026-09-24", ""),
        };

        bool unitWriteOk = ExcelExportService.WriteExcel(unitRecords, unitPath, out string unitMsg);
        allPass &= Check("单元：写 xlsx 成功", unitWriteOk);

        if (unitWriteOk && File.Exists(unitPath))
        {
            using (ExcelPackage package = new ExcelPackage(new FileInfo(unitPath)))
            {
                ExcelWorksheet sheet = package.Workbook.Worksheets[1];
                allPass &= Check("单元：Sheet 名=记账明细", sheet.Name == ExcelExportService.SheetName);
                allPass &= Check("单元：行数=5（表头+4 笔）", sheet.Dimension.Rows == 5);

                bool headerOk = true;

                for (int col = 1; col <= 5; col++)
                {
                    headerOk &= sheet.Cells[1, col].Text == ExcelExportService.HeaderTexts[col - 1];
                }

                allPass &= Check("单元：表头 5 列逐列一致", headerOk);
                allPass &= Check("单元：日期升序 A2=09-24", sheet.Cells[2, 1].Text == "2026-09-24");
                allPass &= Check("单元：同日两笔 A3/A4=09-26", sheet.Cells[3, 1].Text == "2026-09-26" && sheet.Cells[4, 1].Text == "2026-09-26");
                allPass &= Check("单元：同日金额降序 D3=15.00 D4=10.30",
                    Math.Abs(ToDouble(sheet.Cells[3, 4].Value) - 15.0) < 0.0001 &&
                    Math.Abs(ToDouble(sheet.Cells[4, 4].Value) - 10.3) < 0.0001);
                allPass &= Check("单元：类型中文 B3=支出 B5=收入", sheet.Cells[3, 2].Text == "支出" && sheet.Cells[5, 2].Text == "收入");
                allPass &= Check("单元：金额 double 可求和 D5=600.00", Math.Abs(ToDouble(sheet.Cells[5, 4].Value) - 600.0) < 0.0001);
                allPass &= Check("单元：备注 E2 空 E3=电影", string.IsNullOrEmpty(sheet.Cells[2, 5].Text) && sheet.Cells[3, 5].Text == "电影");
            }
        }
        else
        {
            allPass &= Check("单元：xlsx 已产出", false);
        }

        TryDeleteFile(unitPath);

        bool emptyRejected = ExcelExportService.WriteExcel(new List<AccountRecord>(),
            Path.Combine(tempDir, "ab_export_empty.xlsx"), out string emptyMsg);
        allPass &= Check("单元：空集拒绝不产文件", emptyRejected == false && emptyMsg.Contains("没有"));

        // ============ 二、文件名规则（策划案 §6.1） ============
        allPass &= Check("文件名：周", ExcelTransferManager.BuildFileName(ExportRange.Week, "2026-09-21", "2026-09-27")
            == "AccountBook_W_2026-09-21_2026-09-27.xlsx");
        allPass &= Check("文件名：月", ExcelTransferManager.BuildFileName(ExportRange.Month, "2026-09-01", "2026-09-30")
            == "AccountBook_M_2026-09.xlsx");
        allPass &= Check("文件名：年", ExcelTransferManager.BuildFileName(ExportRange.Year, "2026-01-01", "2026-12-31")
            == "AccountBook_Y_2026.xlsx");

        // ============ 三、场景引用链 ============
        ExportPanelUI exportPanel = UnityEngine.Object.FindFirstObjectByType<ExportPanelUI>(FindObjectsInactive.Include);
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        ChartPanelUI chart = UnityEngine.Object.FindFirstObjectByType<ChartPanelUI>(FindObjectsInactive.Include);
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        ExcelTransferManager transfer = UnityEngine.Object.FindFirstObjectByType<ExcelTransferManager>(FindObjectsInactive.Include);
        ToastUI toast = UnityEngine.Object.FindFirstObjectByType<ToastUI>(FindObjectsInactive.Include);

        if (exportPanel == null || uiManager == null || chart == null || accountManager == null ||
            transfer == null || toast == null)
        {
            Debug.LogError("[ExportCheck] FAIL - 场景组件缺失（ExportPanelUI/UIManager/ChartPanelUI/AccountManager/ExcelTransferManager/ToastUI），请先跑 AccountBook/10-搭建导出弹窗。");
            return false;
        }

        allPass &= Check("引用链：ExcelTransferManager 已接入弹窗",
            new SerializedObject(exportPanel).FindProperty("excelTransferManager").objectReferenceValue == transfer);
        allPass &= Check("引用链：UIManager.exportPanel 指向弹窗",
            new SerializedObject(uiManager).FindProperty("exportPanel").objectReferenceValue == exportPanel.gameObject);
        // 图表页旧入口已迁至设置页（08c），图表页不再持有导出弹窗引用与按钮

        SerializedObject soPanel = new SerializedObject(exportPanel);
        bool panelRefsOk = true;

        string[] panelFields =
        {
            "accountManager", "excelTransferManager", "toggleWeek", "toggleMonth", "toggleYear",
            "toggleWeekLabel", "toggleMonthLabel", "toggleYearLabel",
            "toggleWeekSelectedBg", "toggleMonthSelectedBg", "toggleYearSelectedBg",
            "btnPrev", "btnNext", "txtPeriod", "txtPreview", "btnExport", "btnCancel",
        };

        for (int i = 0; i < panelFields.Length; i++)
        {
            panelRefsOk &= soPanel.FindProperty(panelFields[i]).objectReferenceValue != null;
        }

        allPass &= Check("引用链：弹窗 17 项序列化引用完整", panelRefsOk);

        TextMeshProUGUI toastLabel = new SerializedObject(toast).FindProperty("label").objectReferenceValue as TextMeshProUGUI;
        allPass &= Check("引用链：Toast label 已接线", toastLabel != null);

        // ============ 四、UI 全流程（基线感知） ============
        string ws, we;
        DateRangeUtil.GetWeekRange(DateTime.Now.Date, out ws, out we);
        string pws, pwe;
        DateRangeUtil.GetWeekRange(DateTime.Now.Date.AddDays(-7), out pws, out pwe);
        string ms, me;
        DateRangeUtil.GetMonthRange(DateTime.Now.Year, DateTime.Now.Month, out ms, out me);
        string ys, ye;
        DateRangeUtil.GetYearRange(DateTime.Now.Year, out ys, out ye);

        List<AccountRecord> baseWeek = accountManager.GetRecordsInRange(ws, we);
        List<AccountRecord> basePrevWeek = accountManager.GetRecordsInRange(pws, pwe);
        long baseWeekExpense = SumFen(baseWeek, RecordType.Expense);
        long baseWeekIncome = SumFen(baseWeek, RecordType.Income);

        // 自建：今天一笔 + 补记上周一一笔（步骤02 日期补记场景，按 Date 落上周）
        AccountRecord todayRecord = MakeSelfCheckRecord((int)RecordType.Expense, "餐饮", 1030,
            DateTime.Now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AccountRecord prevWeekRecord = MakeSelfCheckRecord((int)RecordType.Expense, "日用", 520, pws);
        allPass &= Check("自建：今天+上周一 两笔入库", accountManager.AddRecord(todayRecord) && accountManager.AddRecord(prevWeekRecord));

        // 1) 设置页点导出 → 弹窗打开（08c 迁移后入口走 UIManager），默认周档
        uiManager.OpenChartPanel();
        uiManager.OpenExportPanel();

        allPass &= Check("流程：设置页点导出 → 弹窗打开", exportPanel.gameObject.activeSelf);

        Toggle toggleWeek = soPanel.FindProperty("toggleWeek").objectReferenceValue as Toggle;
        Toggle toggleMonth = soPanel.FindProperty("toggleMonth").objectReferenceValue as Toggle;
        Toggle toggleYear = soPanel.FindProperty("toggleYear").objectReferenceValue as Toggle;
        TextMeshProUGUI txtPeriod = soPanel.FindProperty("txtPeriod").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI txtPreview = soPanel.FindProperty("txtPreview").objectReferenceValue as TextMeshProUGUI;
        Button btnExport = soPanel.FindProperty("btnExport").objectReferenceValue as Button;
        Button btnPrev = soPanel.FindProperty("btnPrev").objectReferenceValue as Button;

        allPass &= Check("流程：默认周档且互斥", toggleWeek.isOn && toggleMonth.isOn == false && toggleYear.isOn == false);
        allPass &= Check("流程：周期标题=本周 yyyy-MM-dd ~ MM-dd", txtPeriod.text == ws + " ~ " + we.Substring(5));
        allPass &= Check("流程：预览笔数=基线+1", txtPreview.text.Contains($"本周期 {baseWeek.Count + 1} 笔"));
        allPass &= Check("流程：预览支出合计含自建 10.30", txtPreview.text.Contains(MoneyText.FormatYuan(baseWeekExpense + 1030)));
        allPass &= Check("流程：预览收入合计=基线", txtPreview.text.Contains(MoneyText.FormatYuan(baseWeekIncome)));
        allPass &= Check("流程：有数据导出按钮可用", btnExport.interactable);

        // 2) ‹ 平移到上周：补记的历史日期按 Date 归入上周周期
        btnPrev.onClick.Invoke();
        allPass &= Check("流程：‹ 到上周标题", txtPeriod.text == pws + " ~ " + pwe.Substring(5));
        allPass &= Check("流程：补记上周一笔计入预览", txtPreview.text.Contains($"本周期 {basePrevWeek.Count + 1} 笔"));

        // 3) 空周期：置灰 + 导出被拒不产文件
        exportPanel.Open(ExportRange.Week, "2099-01-05", "2099-01-11");
        allPass &= Check("流程：空周期预览文案", txtPreview.text == "本周期没有记录");
        allPass &= Check("流程：空周期导出按钮置灰", btnExport.interactable == false);

        bool emptyExportOk = transfer.ExportExcel(ExportRange.Week, "2099-01-05", "2099-01-11",
            out string emptyFileName, out string emptyFullPath, out string emptyResult);
        allPass &= Check("流程：空周期导出被拒", emptyExportOk == false && emptyResult == "本周期没有记录");
        allPass &= Check("流程：空周期不生成文件", string.IsNullOrEmpty(emptyFullPath) && File.Exists(Path.Combine(Application.persistentDataPath, "AccountBook_W_2099-01-05_2099-01-11.xlsx")) == false);

        // 4) 周档真实导出：先低层调 TransferManager 断言文件内容，再走 UI 按钮断言 Toast/自动关闭
        exportPanel.Open(ExportRange.Week, ws, we);
        bool exportOk = transfer.ExportExcel(ExportRange.Week, ws, we,
            out string fileName, out string fullPath, out string exportMessage);
        allPass &= Check("流程：周档导出成功", exportOk);
        allPass &= Check("流程：周文件名正确", exportOk && fileName == $"AccountBook_W_{ws}_{we}.xlsx");
        allPass &= Check("流程：文件落在本机 Download", exportOk && File.Exists(fullPath));

        if (exportOk && File.Exists(fullPath))
        {
            using (ExcelPackage package = new ExcelPackage(new FileInfo(fullPath)))
            {
                ExcelWorksheet sheet = package.Workbook.Worksheets[1];
                allPass &= Check("流程：周文件行数=基线+自建+表头", sheet.Dimension.Rows == baseWeek.Count + 2);

                bool hasToday = false;
                bool hasPrevWeekDate = false;
                string todayText = DateTime.Now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                for (int row = 2; row <= sheet.Dimension.Rows; row++)
                {
                    string cellDate = sheet.Cells[row, 1].Text;

                    if (cellDate == todayText)
                    {
                        hasToday = true;
                    }

                    if (cellDate == pws)
                    {
                        hasPrevWeekDate = true;
                    }
                }

                allPass &= Check("流程：周文件含今天那笔", hasToday);
                allPass &= Check("流程：周文件只含 7 天（上周一不在）", hasPrevWeekDate == false);
            }
        }

        // UI 按钮再导一次（同名覆盖）：断言 Toast 文案与成功后自动关闭
        exportPanel.Open(ExportRange.Week, ws, we);
        btnExport.onClick.Invoke();
        allPass &= Check("流程：Toast 显示导出完成", toastLabel.text == "导出完成: " + fileName);
        allPass &= Check("流程：导出成功后弹窗自动关闭", exportPanel.gameObject.activeSelf == false);

        if (exportOk && File.Exists(fullPath))
        {
            TryDeleteFile(fullPath);   // 自检文件不留在用户 Downloads
        }

        // 5) 月/年档标题与年档导出（自建今天的记录保证年周期必有数据）
        exportPanel.Open(ExportRange.Month, ms, me);
        allPass &= Check("流程：月档标题 yyyy-MM", txtPeriod.text == ms.Substring(0, 7));

        exportPanel.Open(ExportRange.Year, ys, ye);
        allPass &= Check("流程：年档标题 yyyy 年", txtPeriod.text == DateTime.Now.Year + " 年");

        bool yearExportOk = transfer.ExportExcel(ExportRange.Year, ys, ye,
            out string yearFileName, out string yearFullPath, out string yearMessage);
        allPass &= Check("流程：年档导出成功", yearExportOk);
        allPass &= Check("流程：年文件名正确", yearExportOk && yearFileName == $"AccountBook_Y_{DateTime.Now.Year}.xlsx");

        if (yearExportOk && File.Exists(yearFullPath))
        {
            TryDeleteFile(yearFullPath);
        }

        // 6) 截图：弹窗回到本周有数据状态
        exportPanel.Open(ExportRange.Week, ws, we);
        ScreenCapture.CaptureScreenshot(ShotPath, 2);
        shotRequested = true;

        // ============ 五、清理（自愈：残留同标记数据一并清扫） ============
        accountManager.DeleteRecord(todayRecord.Id);
        accountManager.DeleteRecord(prevWeekRecord.Id);

        int swept = 0;
        List<AccountRecord> everything = accountManager.GetRecordsInRange("0000-01-01", "9999-12-31");

        for (int i = everything.Count - 1; i >= 0; i--)
        {
            if (everything[i] != null && everything[i].Note == SelfCheckNote)
            {
                accountManager.DeleteRecord(everything[i].Id);
                swept++;
            }
        }

        if (swept > 2)
        {
            Debug.Log($"[ExportCheck] 自愈清扫了 {swept - 2} 笔此前崩溃残留的测试数据。");
        }

        // —— 基线还原断言 ——
        allPass &= Check("[还原] 本周记录数/收支还原", PeriodRestored(accountManager, ws, we, baseWeek));
        allPass &= Check("[还原] 上周记录数/收支还原", PeriodRestored(accountManager, pws, pwe, basePrevWeek));

        return allPass;
    }

    // ---------- 工具 ----------

    private static AccountRecord MakeUnitRecord(int type, string category, long fen, string date, string note)
    {
        return new AccountRecord
        {
            Type = type,
            Category = category,
            AmountFen = fen,
            Date = date,
            Note = note,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    private static AccountRecord MakeSelfCheckRecord(int type, string category, long fen, string date)
    {
        return MakeUnitRecord(type, category, fen, date, SelfCheckNote);
    }

    private static long SumFen(List<AccountRecord> records, RecordType type)
    {
        long sum = 0;

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record != null && record.Type == (int)type)
            {
                sum += record.AmountFen;
            }
        }

        return sum;
    }

    /// <summary>
    /// 基线还原：周期内记录数与收支合计与进自检前一致
    /// </summary>
    private static bool PeriodRestored(AccountManager accountManager, string start, string end, List<AccountRecord> baseline)
    {
        List<AccountRecord> current = accountManager.GetRecordsInRange(start, end);

        if (current.Count != baseline.Count)
        {
            return false;
        }

        for (int i = 0; i < baseline.Count; i++)
        {
            if (baseline[i] == null || current[i] == null || baseline[i].Id != current[i].Id)
            {
                return false;
            }
        }

        return SumFen(current, RecordType.Expense) == SumFen(baseline, RecordType.Expense) &&
               SumFen(current, RecordType.Income) == SumFen(baseline, RecordType.Income);
    }

    private static double ToDouble(object value)
    {
        return value is double ? (double)value : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static void TryDeleteFile(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ExportCheck] 文件清理失败: {fullPath} / {ex.Message}");
        }
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[ExportCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
