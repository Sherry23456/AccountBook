using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 导入弹窗控制器（步骤06，策划案 §5.6）：选择 Excel 文件 → 自动预解析（不写库）→
/// 状态区显示「共 N 行 ｜ 可导入 X 笔 ｜ 跳过 Y 笔（重复） ｜ 格式错误 Z 行」+ 前 5 条错误原因 →
/// 确认导入（X=0 置灰）→ 三元组去重合并落库 → Toast 汇报 → 自动关闭。
/// 顶部固定风险提示：导入不会删除现有数据，重复记录会自动跳过。
/// 点遮罩关闭复用步骤01 壳上 UIManager.CloseImportPanel 的持久监听。
/// 选文件平台分流：编辑器 EditorUtility.OpenFilePanel；安卓/iOS NativeFilePicker——
/// 部分国产 ROM 回调不在主线程（步骤06 §4），回调先入队、Update 泵回主线程再操作 UI。
/// </summary>
public class ImportPanelUI : MonoBehaviour
{
    /// <summary>
    /// xlsx 的 MIME 类型（安卓文件选择过滤；仅非编辑器选文件分支使用）
    /// </summary>
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX)
    private static readonly string[] XlsxMimeTypes =
    {
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };
#endif

    private const int MaxErrorPreviewLines = 5;

    private static readonly Color ColorError = new Color32(0xC6, 0x28, 0x28, 0xFF);

    [Header("Dependencies")]
    [SerializeField] private ExcelTransferManager excelTransferManager;

    [Header("Controls")]
    [SerializeField] private Button btnPick;
    [SerializeField] private TextMeshProUGUI txtFileName;
    [SerializeField] private TextMeshProUGUI txtStats;
    [SerializeField] private TextMeshProUGUI txtErrors;
    [SerializeField] private Button btnConfirm;
    [SerializeField] private Button btnCancel;

    /// <summary>
    /// 最近一次预解析产物（确认导入的数据来源）
    /// </summary>
    private ImportParseResult currentParse;

    /// <summary>
    /// NativeFilePicker 回调线程不定（步骤06 §4），先入队由 Update 泵回主线程
    /// </summary>
    private readonly Queue<string> pendingPickedPaths = new Queue<string>();

    private void OnEnable()
    {
        TryInitializeDependencies();
        RegisterEvents();
        ResetState();
    }

    /// <summary>
    /// 外部引用为空时自动查找（弹窗初始未激活，需包含未激活对象）
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (excelTransferManager == null)
        {
            excelTransferManager = FindFirstObjectByType<ExcelTransferManager>(FindObjectsInactive.Include);
        }
    }

    /// <summary>
    /// 图表页导入入口：每次打开都回到未选文件状态（上一次的预解析结果不残留）
    /// </summary>
    public void Open()
    {
        ResetState();

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);   // OnEnable 里 RegisterEvents + ResetState
        }
    }

    /// <summary>
    /// 事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(btnPick, OnPickClicked);
        RegisterButton(btnConfirm, OnConfirmClicked);
        RegisterButton(btnCancel, Close);
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

    private void Update()
    {
        // NativeFilePicker 回调泵：非主线程回调在这里安全地操作 UI/Unity API
        lock (pendingPickedPaths)
        {
            while (pendingPickedPaths.Count > 0)
            {
                HandlePickedPath(pendingPickedPaths.Dequeue());
            }
        }
    }

    // ---------- 选文件（平台分流，策划案 §5.6 / 步骤06 §2.1） ----------

    private void OnPickClicked()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("选择记账Excel", "", "xlsx");
        HandlePickedPath(path);
#elif UNITY_ANDROID || UNITY_IOS || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
        NativeFilePicker.PickFile(OnNativePickResult, XlsxMimeTypes);
#else
        ToastUI.Show("当前平台不支持选择文件");
#endif
    }

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX)
    /// <summary>
    /// NativeFilePicker 回调：取消/拒绝权限时 path 为 null（步骤06 §2.1：拒绝权限提示未授权）
    /// </summary>
    private void OnNativePickResult(string path)
    {
        lock (pendingPickedPaths)
        {
            pendingPickedPaths.Enqueue(path);
        }
    }
#endif

    /// <summary>
    /// 拿到路径后的统一入口（编辑器同步调用；真机经 Update 泵），也供自检直接驱动。
    /// 自动预解析并回填状态区；失败（文件名/格式/读取）时统计区显示原因并置灰确认。
    /// </summary>
    public void HandlePickedPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            ToastUI.Show("未选择文件或未授权文件访问");
            return;
        }

        if (excelTransferManager == null)
        {
            TryInitializeDependencies();
        }

        txtFileName.text = Path.GetFileName(path);
        txtErrors.text = string.Empty;
        btnConfirm.interactable = false;
        currentParse = null;

        if (excelTransferManager == null)
        {
            txtStats.text = "导入服务未初始化";
            return;
        }

        bool ok = excelTransferManager.PreparseImport(path, out ImportParseResult parse,
            out int duplicateCount, out string message);

        if (!ok)
        {
            txtStats.text = message;
            return;
        }

        currentParse = parse;
        int importable = parse.Valid.Count - duplicateCount;

        txtStats.text = $"共 {parse.TotalRows} 行 ｜ 可导入 {importable} 笔 ｜ " +
                        $"跳过 {duplicateCount} 笔（重复） ｜ 格式错误 {parse.Errors.Count} 行";
        txtErrors.text = BuildErrorPreview(parse.Errors, MaxErrorPreviewLines);
        btnConfirm.interactable = importable > 0;
    }

    // ---------- 确认导入 / 关闭 ----------

    private void OnConfirmClicked()
    {
        if (currentParse == null || excelTransferManager == null)
        {
            return;
        }

        bool success = excelTransferManager.ConfirmImport(currentParse,
            out int importedCount, out int skippedCount, out string message);

        if (success)
        {
            ToastUI.Show(message);
            Close();
        }
        else
        {
            ToastUI.Show(message);
        }
    }

    /// <summary>
    /// 关闭弹窗（取消/导入成功后）；遮罩点击走 UIManager.CloseImportPanel 同效
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

    // ---------- 刷新 ----------

    /// <summary>
    /// 回到未选文件状态（Open/OnEnable 共用，已激活弹窗重开也强制归零——步骤05 同坑：不触发 OnEnable）
    /// </summary>
    private void ResetState()
    {
        currentParse = null;

        if (txtFileName != null)
        {
            txtFileName.text = "未选择文件";
        }

        if (txtStats != null)
        {
            txtStats.text = "选择文件后自动预解析";
        }

        if (txtErrors != null)
        {
            txtErrors.text = string.Empty;
        }

        if (btnConfirm != null)
        {
            btnConfirm.interactable = false;
        }
    }

    /// <summary>
    /// 错误行预览：最多前 N 条，超出部分以「…共 X 条」收尾
    /// </summary>
    private static string BuildErrorPreview(List<string> errors, int maxLines)
    {
        if (errors == null || errors.Count == 0)
        {
            return string.Empty;
        }

        List<string> lines = new List<string>();

        for (int i = 0; i < errors.Count && i < maxLines; i++)
        {
            lines.Add(errors[i]);
        }

        if (errors.Count > maxLines)
        {
            lines.Add($"…共 {errors.Count} 条错误");
        }

        return string.Join("\n", lines);
    }
}
