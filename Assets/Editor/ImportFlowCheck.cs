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
/// 步骤 06 验收：导入流程自检（Play 模式，菜单 AccountBook/13-导入流程自检）。
/// 自动进 Play → ExcelImportService 单元（导出文件回环逐字段比对、日期三形态、表头校验、
/// 行级错误不中断、空行跳过、空表/缺文件拒绝）→ 文件名白名单 → 场景引用链 →
/// UI 全流程（基线感知）：图表页点导入开弹窗 → 预解析统计回填（重复/错误行混合）→
/// 确认导入落库+Toast+自动关闭 → 同文件重复导入可导入 0 笔 → 错误文件名/坏表头提示 →
/// 回环测试（备份 JSON → 删光 → 导入全量文件 → 与导出前逐笔一致 → 还原）→ 截图 →
/// 清理自建数据/临时文件 → 基线还原断言 → 退 Play。
/// 回环段先复制 account_records.json 为 .bak 兜底（硬崩溃可手工还原），恢复用
/// ReplaceAllRecords(快照) 保证 Id 原样，finally 兜底防中途异常丢数据。
/// </summary>
public static class ImportFlowCheck
{
    private const string MenuItemPath = "AccountBook/13-导入流程自检";
    private const string StartedKey = "AccountBook.ImportCheck.Started";
    private const string DoneKey = "AccountBook.ImportCheck.Done";
    private const string SelfCheckNote = "自检数据";
    private const string ShotPath = "D:\\Qklunity\\AccountBook\\.shots\\import_panel.png";
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
    /// 进 Play 触发域重载会丢事件订阅，用 SessionState 记忆重挂（ExportFlowCheck 同款）
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
            Debug.LogError("[ImportCheck] 异常：" + ex);
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
                allPass &= Check("[截图] import_panel.png 已落盘", File.Exists(ShotPath));
                Debug.Log($"[ImportCheck] ===== 导入流程自检{(allPass ? "全部通过" : "存在失败项")} =====");
                EditorApplication.isPlaying = false;
            }
        };

        EditorApplication.update += tick;
    }

    private static bool RunChecks()
    {
        bool allPass = true;

        // ============ 一、ExcelImportService 单元断言 ============
        string tempDir = Application.temporaryCachePath;

        // 1) 导出→导入回环：WriteExcel 产物读回逐字段比对
        string roundtripPath = Path.Combine(tempDir, "AccountBook_U_roundtrip.xlsx");
        TryDeleteFile(roundtripPath);

        List<AccountRecord> unitRecords = new List<AccountRecord>
        {
            MakeUnitRecord((int)RecordType.Expense, "餐饮", 1030, "2026-09-24", "午饭"),
            MakeUnitRecord((int)RecordType.Expense, "娱乐", 1500, "2026-09-26", "电影"),
            MakeUnitRecord((int)RecordType.Income, "兼职", 60000, "2026-09-26", "周末兼职"),
            MakeUnitRecord((int)RecordType.Expense, "交通", 500, "2026-09-27", ""),
        };

        bool unitWriteOk = ExcelExportService.WriteExcel(unitRecords, roundtripPath, out string unitWriteMsg);
        allPass &= Check("单元：导出产物可供导入", unitWriteOk);

        if (unitWriteOk)
        {
            bool readOk = ExcelImportService.ReadExcel(roundtripPath, out ImportParseResult rt, out string rtMsg);
            allPass &= Check("单元：回环 ReadExcel 成功", readOk);
            allPass &= Check("单元：回环行数=4 无错误", readOk && rt.TotalRows == 4 && rt.Errors.Count == 0 && rt.Valid.Count == 4);

            if (readOk)
            {
                // 导出按 日期升序+同日金额降序 排列，读回顺序与输入不同，按内容查找比对
                bool fieldsOk = true;

                for (int i = 0; i < unitRecords.Count; i++)
                {
                    AccountRecord src = unitRecords[i];
                    AccountRecord got = rt.Valid.Find(r => r.Date == src.Date && r.Type == src.Type &&
                        r.AmountFen == src.AmountFen && r.Category == src.Category && r.Note == src.Note);

                    if (got == null)
                    {
                        fieldsOk = false;
                        break;
                    }
                }

                allPass &= Check("单元：回环逐字段一致（日期/类型/分类/金额/备注）", fieldsOk);
                allPass &= Check("单元：导入记录 Id 全新", readOk && rt.Valid.TrueForAll(r => !string.IsNullOrEmpty(r.Id) &&
                    r.Id != unitRecords[0].Id));
            }
        }

        // 2) 日期三形态 + 分类归「其他」+ 行级错误 + 空行跳过
        string trickyPath = Path.Combine(tempDir, "AccountBook_U_tricky.xlsx");
        BuildTrickyFile(trickyPath);
        bool trickyOk = ExcelImportService.ReadExcel(trickyPath, out ImportParseResult tricky, out string trickyMsg);
        allPass &= Check("单元：多形态文件解析成功", trickyOk);

        if (trickyOk)
        {
            allPass &= Check("单元：DateTime 形态日期", tricky.Valid.Count > 0 && ContainsRecord(tricky.Valid, "2026-09-26", RecordType.Expense, 1030));
            allPass &= Check("单元：序列数(double)形态日期", tricky.Valid.Count > 0 && ContainsRecord(tricky.Valid, "2026-09-24", RecordType.Income, 60000));
            allPass &= Check("单元：yyyy-MM-dd 文本形态日期", tricky.Valid.Count > 0 && ContainsRecord(tricky.Valid, "2026-09-25", RecordType.Expense, 550));
            allPass &= Check("单元：未知分类归「其他」不报错", tricky.Valid.Count > 0 && FindRecord(tricky.Valid, "2026-09-24", RecordType.Income, 60000).Category == "其他");
            allPass &= Check("单元：总行数=7（全空行不计）", tricky.TotalRows == 7);
            allPass &= Check("单元：错误行=4", tricky.Errors.Count == 4);
            allPass &= Check("单元：错误-类型非法（第5行）", HasLine(tricky.Errors, 5, "类型"));
            allPass &= Check("单元：错误-金额负数（第6行）", HasLine(tricky.Errors, 6, "金额"));
            allPass &= Check("单元：错误-日期乱写（第7行）", HasLine(tricky.Errors, 7, "日期"));
            allPass &= Check("单元：错误-金额为零（第9行）", HasLine(tricky.Errors, 9, "金额"));
        }

        // 3) 表头校验
        string tamperedPath = Path.Combine(tempDir, "AccountBook_U_tampered.xlsx");
        File.Copy(roundtripPath, tamperedPath, true);
        using (ExcelPackage pkg = new ExcelPackage(new FileInfo(tamperedPath)))
        {
            pkg.Workbook.Worksheets[1].Cells[1, 1].Value = "日期X";
            pkg.Save();
        }
        bool tamperedOk = ExcelImportService.ReadExcel(tamperedPath, out _, out string tamperedMsg);
        allPass &= Check("单元：表头被改拒绝（表格格式不符）", tamperedOk == false && tamperedMsg == "表格格式不符");

        // 4) 空表 / 缺文件
        string emptyPath = Path.Combine(tempDir, "AccountBook_U_empty.xlsx");
        using (ExcelPackage pkg = new ExcelPackage(new FileInfo(emptyPath)))
        {
            pkg.Workbook.Worksheets.Add("空表");
            pkg.Save();
        }
        bool emptyOk = ExcelImportService.ReadExcel(emptyPath, out _, out string emptyMsg);
        allPass &= Check("单元：空表拒绝", emptyOk == false && emptyMsg == "表格内容为空");

        bool missingOk = ExcelImportService.ReadExcel(Path.Combine(tempDir, "AccountBook_U_missing.xlsx"), out _, out string missingMsg);
        allPass &= Check("单元：缺文件拒绝", missingOk == false && missingMsg == "文件不存在或无法访问");

        // ============ 二、文件名白名单（策划案 §6.3） ============
        ExcelTransferManager transfer = UnityEngine.Object.FindFirstObjectByType<ExcelTransferManager>(FindObjectsInactive.Include);

        if (transfer == null)
        {
            Debug.LogError("[ImportCheck] FAIL - 场景组件缺失（ExcelTransferManager），请先跑 AccountBook/10、12。");
            return false;
        }

        string badNamePath = Path.Combine(tempDir, "backup_x.xlsx");
        File.Copy(roundtripPath, badNamePath, true);
        bool badNameOk = transfer.PreparseImport(badNamePath, out _, out _, out string badNameMsg);
        allPass &= Check("白名单：非 AccountBook_ 命名拒绝", badNameOk == false && badNameMsg.Contains("AccountBook_"));

        string xlsNamePath = Path.Combine(tempDir, "AccountBook_M_2026-09.xls");
        File.Copy(roundtripPath, xlsNamePath, true);
        bool xlsNameOk = transfer.PreparseImport(xlsNamePath, out _, out _, out string xlsNameMsg);
        allPass &= Check("白名单：.xls 老格式拒绝", xlsNameOk == false && xlsNameMsg.Contains("AccountBook_"));

        string goodNamePath = Path.Combine(tempDir, "AccountBook_U_good.xlsx");
        File.Copy(roundtripPath, goodNamePath, true);
        bool goodNameOk = transfer.PreparseImport(goodNamePath, out ImportParseResult goodParse, out _, out string goodNameMsg);
        allPass &= Check("白名单：合规命名通过", goodNameOk && goodParse != null && goodParse.Valid.Count == 4 && goodNameMsg == string.Empty);

        // ============ 三、场景引用链 ============
        ImportPanelUI importPanel = UnityEngine.Object.FindFirstObjectByType<ImportPanelUI>(FindObjectsInactive.Include);
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        ChartPanelUI chart = UnityEngine.Object.FindFirstObjectByType<ChartPanelUI>(FindObjectsInactive.Include);
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        ToastUI toast = UnityEngine.Object.FindFirstObjectByType<ToastUI>(FindObjectsInactive.Include);

        if (importPanel == null || uiManager == null || chart == null || accountManager == null || toast == null)
        {
            Debug.LogError("[ImportCheck] FAIL - 场景组件缺失（ImportPanelUI/UIManager/ChartPanelUI/AccountManager/ToastUI），请先跑 AccountBook/12。");
            return false;
        }

        SerializedObject soPanel = new SerializedObject(importPanel);
        bool panelRefsOk = true;

        string[] panelFields = { "excelTransferManager", "btnPick", "txtFileName", "txtStats", "txtErrors", "btnConfirm", "btnCancel" };

        for (int i = 0; i < panelFields.Length; i++)
        {
            panelRefsOk &= soPanel.FindProperty(panelFields[i]).objectReferenceValue != null;
        }

        allPass &= Check("引用链：弹窗 7 项序列化引用完整", panelRefsOk);
        allPass &= Check("引用链：UIManager.importPanel 指向弹窗",
            new SerializedObject(uiManager).FindProperty("importPanel").objectReferenceValue == importPanel.gameObject);
        allPass &= Check("引用链：ChartPanelUI.importPanel 已接线",
            new SerializedObject(chart).FindProperty("importPanel").objectReferenceValue == importPanel);

        Button chartImportBtn = new SerializedObject(chart).FindProperty("btnImport").objectReferenceValue as Button;
        allPass &= Check("引用链：图表页导入按钮已启用", chartImportBtn != null && chartImportBtn.interactable);

        TextMeshProUGUI toastLabel = new SerializedObject(toast).FindProperty("label").objectReferenceValue as TextMeshProUGUI;
        allPass &= Check("引用链：Toast label 已接线", toastLabel != null);

        // ============ 四、UI 全流程（基线感知） ============
        string today = DateTime.Now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // 基线快照 + 自建两笔（重复比对锚点）
        List<AccountRecord> baselineOriginal = accountManager.GetAllRecords();
        AccountRecord selfA = MakeSelfCheckRecord((int)RecordType.Expense, "餐饮", 1030, today);
        AccountRecord selfB = MakeSelfCheckRecord((int)RecordType.Income, "兼职", 60000, today);
        allPass &= Check("自建：餐饮+兼职 两笔入库", accountManager.AddRecord(selfA) && accountManager.AddRecord(selfB));
        int baseCount = baselineOriginal.Count + 2;

        // 混合文件：1 重复（selfA）+ 1 新（书籍 25.00）+ 2 坏行
        string mixedPath = Path.Combine(tempDir, "AccountBook_W_selfcheck.xlsx");
        BuildMixedFile(mixedPath, selfA, today);
        string mixedFileName = Path.GetFileName(mixedPath);

        Button btnPick = soPanel.FindProperty("btnPick").objectReferenceValue as Button;
        Button btnConfirm = soPanel.FindProperty("btnConfirm").objectReferenceValue as Button;
        Button btnCancel = soPanel.FindProperty("btnCancel").objectReferenceValue as Button;
        TextMeshProUGUI txtFileName = soPanel.FindProperty("txtFileName").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI txtStats = soPanel.FindProperty("txtStats").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI txtErrors = soPanel.FindProperty("txtErrors").objectReferenceValue as TextMeshProUGUI;

        // 1) 图表页点导入 → 弹窗打开且为初始态
        uiManager.OpenChartPanel();
        chartImportBtn.onClick.Invoke();
        allPass &= Check("流程：图表页点导入 → 弹窗打开", importPanel.gameObject.activeSelf);
        allPass &= Check("流程：初始态未选文件", txtFileName.text == "未选择文件" && btnConfirm.interactable == false);

        // 2) 选文件自动预解析：混合文件 → 可导入 1 / 跳过 1 / 错误 2
        importPanel.HandlePickedPath(mixedPath);
        allPass &= Check("流程：文件名回显", txtFileName.text == mixedFileName);
        allPass &= Check("流程：统计行文案", txtStats.text == "共 4 行 ｜ 可导入 1 笔 ｜ 跳过 1 笔（重复） ｜ 格式错误 2 行");
        allPass &= Check("流程：错误预览含前 5 条内的两行", txtErrors.text.Contains("第3行：类型不是 支出/收入") &&
            txtErrors.text.Contains("第4行：日期无法识别"));
        allPass &= Check("流程：有可导入时确认可用", btnConfirm.interactable);

        // 3) 确认导入 → Toast + 自动关闭 + 落库
        btnConfirm.onClick.Invoke();
        allPass &= Check("流程：Toast 显示导入完成", toastLabel.text == "导入完成：新增 1 笔，跳过 1 笔");
        allPass &= Check("流程：导入成功后弹窗自动关闭", importPanel.gameObject.activeSelf == false);
        List<AccountRecord> afterImport = accountManager.GetAllRecords();
        allPass &= Check("流程：库内 = 基线+3", afterImport.Count == baseCount + 1);
        AccountRecord imported = afterImport.Find(r => r != null && r.Note == SelfCheckNote && r.Category == "书籍");
        allPass &= Check("流程：新记录已入库（书籍 2500 分）", imported != null && imported.AmountFen == 2500 && imported.Date == today);
        allPass &= Check("流程：新记录 CreatedAt=导入时刻", imported != null && !string.IsNullOrEmpty(imported.CreatedAt));

        // 4) 同一文件再导：可导入 0 笔，确认置灰（验收：重复导入）
        importPanel.Open();
        importPanel.HandlePickedPath(mixedPath);
        allPass &= Check("流程：重复导入可导入 0 笔", txtStats.text == "共 4 行 ｜ 可导入 0 笔 ｜ 跳过 2 笔（重复） ｜ 格式错误 2 行");
        allPass &= Check("流程：可导入 0 时确认置灰", btnConfirm.interactable == false);

        // 5) 文件名不符 / 坏表头（验收：坏文件）
        importPanel.HandlePickedPath(badNamePath);
        allPass &= Check("流程：文件名不符提示", txtStats.text == "请选择 AccountBook_*.xlsx 格式文件");
        importPanel.HandlePickedPath(tamperedPath);
        allPass &= Check("流程：坏表头提示", txtStats.text == "表格格式不符" && btnConfirm.interactable == false);

        // 6) 取消按钮关弹窗
        btnCancel.onClick.Invoke();
        allPass &= Check("流程：取消关闭弹窗", importPanel.gameObject.activeSelf == false);

        // 清掉流程中导入的测试记录，回环测试前恢复到 基线+自建2笔
        accountManager.DeleteRecord(imported.Id);

        // 7) 回环测试（验收第 1 条：删光 → 导入 → 与导出前完全一致）
        allPass &= RunRoundtripCheck(accountManager, importPanel, btnConfirm, txtStats, tempDir, baselineOriginal, ref allPass);

        // ============ 五、清理（自愈：残留同标记数据一并清扫） ============
        accountManager.DeleteRecord(selfA.Id);
        accountManager.DeleteRecord(selfB.Id);

        int swept = 0;
        List<AccountRecord> everything = accountManager.GetAllRecords();

        for (int i = everything.Count - 1; i >= 0; i--)
        {
            if (everything[i] != null && everything[i].Note == SelfCheckNote)
            {
                accountManager.DeleteRecord(everything[i].Id);
                swept++;
            }
        }

        if (swept > 0)
        {
            Debug.Log($"[ImportCheck] 自愈清扫了 {swept} 笔此前崩溃残留的测试数据。");
        }

        // —— 基线还原断言（记录数 + Id 一一对应） ——
        List<AccountRecord> finalRecords = accountManager.GetAllRecords();
        allPass &= Check("[还原] 记录数还原", finalRecords.Count == baselineOriginal.Count);

        bool idsOk = true;

        for (int i = 0; i < baselineOriginal.Count; i++)
        {
            if (finalRecords.Find(r => r != null && r.Id == baselineOriginal[i].Id) == null)
            {
                idsOk = false;
                break;
            }
        }

        allPass &= Check("[还原] 基线记录 Id 一一还原", idsOk);

        // ============ 六、截图（弹窗停在混合文件统计态） ============
        importPanel.Open();
        importPanel.HandlePickedPath(mixedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));
        ScreenCapture.CaptureScreenshot(ShotPath, 2);
        shotRequested = true;

        // —— 临时文件清理 ——
        TryDeleteFile(roundtripPath);
        TryDeleteFile(trickyPath);
        TryDeleteFile(tamperedPath);
        TryDeleteFile(emptyPath);
        TryDeleteFile(badNamePath);
        TryDeleteFile(xlsNamePath);
        TryDeleteFile(goodNamePath);
        TryDeleteFile(mixedPath);

        return allPass;
    }

    /// <summary>
    /// 回环测试：备份 JSON → 全量导出 → 删光 → 导入 → 逐笔比对 → 还原（finally 兜底）。
    /// 返回本段是否全过，通过 ref 累加进总结果。
    /// </summary>
    private static bool RunRoundtripCheck(AccountManager accountManager, ImportPanelUI importPanel,
        Button btnConfirm, TextMeshProUGUI txtStats, string tempDir,
        List<AccountRecord> baselineOriginal, ref bool allPass)
    {
        string jsonPath = Path.Combine(Application.persistentDataPath, "account_records.json");
        string bakPath = jsonPath + ".importcheck.bak";
        bool hadJson = File.Exists(jsonPath);

        if (hadJson)
        {
            File.Copy(jsonPath, bakPath, true);   // 硬崩溃时可手工还原
        }

        List<AccountRecord> fullBefore = accountManager.GetAllRecords();
        string fullExportPath = Path.Combine(tempDir, "AccountBook_M_selfcheck_full.xlsx");

        try
        {
            bool exportOk = ExcelExportService.WriteExcel(fullBefore, fullExportPath, out string exportMsg);
            allPass &= Check("回环：全量导出成功", exportOk);

            accountManager.ReplaceAllRecords(new List<AccountRecord>());   // 删光
            allPass &= Check("回环：已删光", accountManager.GetAllRecords().Count == 0);

            importPanel.Open();
            importPanel.HandlePickedPath(fullExportPath);
            allPass &= Check("回环：预解析可导入=N 笔", txtStats.text.Contains($"可导入 {fullBefore.Count} 笔"));

            btnConfirm.onClick.Invoke();
            List<AccountRecord> fullAfter = accountManager.GetAllRecords();
            allPass &= Check("回环：导入后笔数一致", fullAfter.Count == fullBefore.Count);

            bool contentOk = true;

            for (int i = 0; i < fullBefore.Count; i++)
            {
                AccountRecord before = fullBefore[i];
                AccountRecord match = fullAfter.Find(r => r != null &&
                    r.Date == before.Date && r.Type == before.Type && r.Category == before.Category &&
                    r.AmountFen == before.AmountFen && r.Note == before.Note);

                if (match == null)
                {
                    contentOk = false;
                    break;
                }
            }

            allPass &= Check("回环：逐笔内容一致（日期/类型/分类/金额/备注）", contentOk);

            // 还原：原对象（含 Id）原样写回
            accountManager.ReplaceAllRecords(fullBefore);
            allPass &= Check("回环：还原后笔数一致", accountManager.GetAllRecords().Count == fullBefore.Count);
        }
        catch (Exception ex)
        {
            Debug.LogError("[ImportCheck] 回环段异常：" + ex);
            allPass = false;
            accountManager.ReplaceAllRecords(fullBefore);
        }
        finally
        {
            if (hadJson && File.Exists(bakPath))
            {
                TryDeleteFile(bakPath);
            }

            TryDeleteFile(fullExportPath);
        }

        return allPass;
    }

    // ---------- 造文件/记录工具 ----------

    /// <summary>
    /// 多形态文件：日期三种底层形态、未知分类、3 坏行、1 全空行（共 9 数据行，1 行不计）
    /// </summary>
    private static void BuildTrickyFile(string path)
    {
        TryDeleteFile(path);

        using (ExcelPackage package = new ExcelPackage(new FileInfo(path)))
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add(ExcelExportService.SheetName);

            for (int col = 0; col < ExcelExportService.HeaderTexts.Length; col++)
            {
                sheet.Cells[1, col + 1].Value = ExcelExportService.HeaderTexts[col];
            }

            // 行2：DateTime 形态
            sheet.Cells[2, 1].Value = new DateTime(2026, 9, 26);
            sheet.Cells[2, 2].Value = "支出";
            sheet.Cells[2, 3].Value = "餐饮";
            sheet.Cells[2, 4].Value = 10.30;
            sheet.Cells[2, 5].Value = "午饭";
            // 行3：序列数 double 形态 + 未知分类
            sheet.Cells[3, 1].Value = new DateTime(2026, 9, 24).ToOADate();
            sheet.Cells[3, 2].Value = "收入";
            sheet.Cells[3, 3].Value = "不明分类";
            sheet.Cells[3, 4].Value = 600.0;
            sheet.Cells[3, 5].Value = "周末兼职";
            // 行4：yyyy-MM-dd 文本形态 + 金额文本形态
            sheet.Cells[4, 1].Value = "2026-09-25";
            sheet.Cells[4, 2].Value = "支出";
            sheet.Cells[4, 3].Value = "交通";
            sheet.Cells[4, 4].Value = "5.50";
            sheet.Cells[4, 5].Value = "地铁";
            // 行5：类型非法
            sheet.Cells[5, 1].Value = "2026-09-25";
            sheet.Cells[5, 2].Value = "收入a";
            sheet.Cells[5, 3].Value = "兼职";
            sheet.Cells[5, 4].Value = 100.0;
            // 行6：金额负数
            sheet.Cells[6, 1].Value = "2026-09-25";
            sheet.Cells[6, 2].Value = "支出";
            sheet.Cells[6, 3].Value = "餐饮";
            sheet.Cells[6, 4].Value = -5.0;
            // 行7：日期乱写
            sheet.Cells[7, 1].Value = "乱写";
            sheet.Cells[7, 2].Value = "支出";
            sheet.Cells[7, 3].Value = "餐饮";
            sheet.Cells[7, 4].Value = 10.0;
            // 行8：全空行（不计 TotalRows）
            // 行9：金额为零
            sheet.Cells[9, 1].Value = "2026-09-25";
            sheet.Cells[9, 2].Value = "支出";
            sheet.Cells[9, 3].Value = "餐饮";
            sheet.Cells[9, 4].Value = 0.0;

            package.Save();
        }
    }

    /// <summary>
    /// 混合文件：行2=重复锚点（selfA 三元组）/ 行3=类型非法 / 行4=日期乱写 / 行5=新记录（书籍 25.00）
    /// </summary>
    private static void BuildMixedFile(string path, AccountRecord duplicateAnchor, string date)
    {
        TryDeleteFile(path);

        using (ExcelPackage package = new ExcelPackage(new FileInfo(path)))
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add(ExcelExportService.SheetName);

            for (int col = 0; col < ExcelExportService.HeaderTexts.Length; col++)
            {
                sheet.Cells[1, col + 1].Value = ExcelExportService.HeaderTexts[col];
            }

            sheet.Cells[2, 1].Value = duplicateAnchor.Date;
            sheet.Cells[2, 2].Value = duplicateAnchor.Type == (int)RecordType.Income ? "收入" : "支出";
            sheet.Cells[2, 3].Value = duplicateAnchor.Category;
            sheet.Cells[2, 4].Value = duplicateAnchor.AmountFen / 100.0;
            sheet.Cells[2, 5].Value = duplicateAnchor.Note;

            sheet.Cells[3, 1].Value = date;
            sheet.Cells[3, 2].Value = "收入a";
            sheet.Cells[3, 3].Value = "兼职";
            sheet.Cells[3, 4].Value = 100.0;

            sheet.Cells[4, 1].Value = "乱写";
            sheet.Cells[4, 2].Value = "支出";
            sheet.Cells[4, 3].Value = "餐饮";
            sheet.Cells[4, 4].Value = 10.0;

            sheet.Cells[5, 1].Value = date;
            sheet.Cells[5, 2].Value = "支出";
            sheet.Cells[5, 3].Value = "书籍";
            sheet.Cells[5, 4].Value = 25.0;
            sheet.Cells[5, 5].Value = SelfCheckNote;

            package.Save();
        }
    }

    private static bool ContainsRecord(List<AccountRecord> records, string date, RecordType type, long fen)
    {
        return FindRecord(records, date, type, fen) != null;
    }

    private static AccountRecord FindRecord(List<AccountRecord> records, string date, RecordType type, long fen)
    {
        return records.Find(r => r.Date == date && r.Type == (int)type && r.AmountFen == fen);
    }

    private static bool HasLine(List<string> errors, int row, string keyword)
    {
        return errors.Exists(e => e.StartsWith($"第{row}行：", StringComparison.Ordinal) && e.Contains(keyword));
    }

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
            Debug.LogWarning($"[ImportCheck] 文件清理失败: {fullPath} / {ex.Message}");
        }
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[ImportCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
