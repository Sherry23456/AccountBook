using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤06 一键搭建导入弹窗：在步骤01 预留的 ImportPanel 壳（遮罩+Dialog+标题）里填充
/// 风险提示行 + 选择文件按钮 + 文件名/预解析统计/错误预览区 + 确认/取消按钮，
/// 挂 ImportPanelUI 接线；图表页 btnImport 启用并回填 ChartPanelUI.importPanel 引用
/// → 存场景 → 预固化字形 → 接线自检。ExcelTransferManager/Toast 复用步骤05 产物（缺失报错）。
/// 幂等：重复运行先清 Dialog 内容重建。
/// </summary>
public static class ImportPanelBuilder
{
    private const string MenuItemPath = "AccountBook/12-搭建导入弹窗";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string FontAssetPath = "Assets/Fonts/STKAITI Dynamic SDF.asset";

    // 色板（与 MainSceneBuilder/ExportPanelBuilder 一致）
    private static readonly Color ColYellow = FromHex(0xFFD100);
    private static readonly Color ColBlack = FromHex(0x222222);
    private static readonly Color ColGray = FromHex(0x666666);
    private static readonly Color ColGrayText = FromHex(0x444444);
    private static readonly Color ColWhite = FromHex(0xFFFFFF);
    private static readonly Color ColKey = FromHex(0xF2F2F2);
    private static readonly Color ColError = FromHex(0xC62828);

    /// <summary>
    /// 搭建产物引用包
    /// </summary>
    private class PanelRefs
    {
        public GameObject Panel;
        public Button BtnPick;
        public TextMeshProUGUI TxtFileName;
        public TextMeshProUGUI TxtStats;
        public TextMeshProUGUI TxtErrors;
        public Button BtnConfirm;
        public Button BtnCancel;
    }

    [MenuItem(MenuItemPath)]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[ImportBuild] 编辑器正在 Play 模式，退出后再搭建。");
            return;
        }

        try
        {
            Build();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ImportBuild] 搭建失败：{ex}\n请确认步骤 01/05 已完成（菜单 AccountBook/01、10）。");
        }
    }

    /// <summary>
    /// 主入口：填壳 → 挂组件接线 → 图表页启用导入按钮 → 存场景 → 自检
    /// </summary>
    private static void Build()
    {
        Debug.Log("[ImportBuild] ===== 步骤06 导入弹窗搭建开始 =====");

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        if (font == null)
        {
            throw new FileNotFoundException("找不到 " + FontAssetPath + "，请先运行步骤 01。");
        }

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject canvasGo = GameObject.Find("Canvas");

        if (canvasGo == null)
        {
            throw new InvalidOperationException("场景中找不到 Canvas，请先运行步骤 01。");
        }

        Transform canvas = canvasGo.transform;
        Transform oldPanel = canvas.Find("ImportPanel");

        if (oldPanel == null)
        {
            throw new InvalidOperationException("场景中找不到 ImportPanel 壳，请先运行步骤 01。");
        }

        // —— 填充 Dialog（壳的遮罩 Button 绑定 UIManager.CloseImportPanel，保留不动） ——
        Transform dialog = oldPanel.Find("Dialog");

        if (dialog == null)
        {
            throw new InvalidOperationException("ImportPanel 下找不到 Dialog（步骤01 壳结构不符）。");
        }

        RectTransform dialogRect = (RectTransform)dialog;
        dialogRect.sizeDelta = new Vector2(620f, 660f);   // 壳默认 420 高，导入弹窗含统计+错误区更高

        ClearChildren(dialog);

        PanelRefs refs = BuildImportDialog(dialog, font);
        refs.Panel = oldPanel.gameObject;

        // —— ExcelTransferManager（步骤05 产物，缺失则先跑步骤05；理论上不会走 AddComponent 兜底） ——
        GameObject managersGo = GameObject.Find("Managers");

        if (managersGo == null)
        {
            throw new InvalidOperationException("场景中找不到 Managers，请先运行步骤 01。");
        }

        ExcelTransferManager excelTransferManager = managersGo.GetComponent<ExcelTransferManager>();

        if (excelTransferManager == null)
        {
            excelTransferManager = managersGo.AddComponent<ExcelTransferManager>();
            Debug.LogWarning("[ImportBuild] Managers 上没有 ExcelTransferManager（步骤05 产物缺失），已补挂。");
        }

        // —— 挂 ImportPanelUI 并一次性接线 ——
        ImportPanelUI panelUi = refs.Panel.GetComponent<ImportPanelUI>();

        if (panelUi == null)
        {
            panelUi = refs.Panel.AddComponent<ImportPanelUI>();
        }

        SerializedObject so = new SerializedObject(panelUi);
        SetRef(so, "excelTransferManager", excelTransferManager);
        SetRef(so, "btnPick", refs.BtnPick);
        SetRef(so, "txtFileName", refs.TxtFileName);
        SetRef(so, "txtStats", refs.TxtStats);
        SetRef(so, "txtErrors", refs.TxtErrors);
        SetRef(so, "btnConfirm", refs.BtnConfirm);
        SetRef(so, "btnCancel", refs.BtnCancel);
        so.ApplyModifiedPropertiesWithoutUndo();

        // —— 图表页导入按钮启用 + 弹窗引用回填 ——
        ChartPanelUI chartPanelUi = UnityEngine.Object.FindFirstObjectByType<ChartPanelUI>(FindObjectsInactive.Include);

        if (chartPanelUi != null)
        {
            SerializedObject soChart = new SerializedObject(chartPanelUi);
            SetRef(soChart, "importPanel", panelUi);
            soChart.ApplyModifiedPropertiesWithoutUndo();

            SerializedProperty btnImportProp = soChart.FindProperty("btnImport");

            if (btnImportProp != null && btnImportProp.objectReferenceValue != null)
            {
                ((Button)btnImportProp.objectReferenceValue).interactable = true;   // 步骤06 启用
            }
        }

        // —— ToastUI 为步骤05 产物，仅校验存在 ——
        ToastUI toastUi = UnityEngine.Object.FindFirstObjectByType<ToastUI>(FindObjectsInactive.Include);

        if (toastUi == null)
        {
            Debug.LogError("[ImportBuild] FAIL - 场景中没有 ToastUI，请先运行步骤 05（菜单 AccountBook/10）。");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        PrePopulateFontGlyphs(font);

        bool pass = VerifyWiring(refs, panelUi, excelTransferManager, toastUi, chartPanelUi);
        Debug.Log($"[ImportBuild] ===== 步骤06 导入弹窗搭建完成，接线自检 {(pass ? "通过" : "失败(见上方 [ImportBuild] FAIL 日志)")} =====");
    }

    // ---------- UI 搭建 ----------

    /// <summary>
    /// Dialog 内容纵向堆叠（高 660）：标题 96 / 风险提示 56 / 选文件按钮 96 /
    /// 文件名 56 / 统计 90 / 错误预览 170 / 按钮行 110
    /// </summary>
    private static PanelRefs BuildImportDialog(Transform dialog, TMP_FontAsset font)
    {
        PanelRefs refs = new PanelRefs();

        // 标题（壳原本有 TitleArea，ClearChildren 已清，重建）
        RectTransform titleArea = CreateRect(dialog, "TitleArea",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        titleArea.sizeDelta = new Vector2(0f, 96f);
        titleArea.anchoredPosition = new Vector2(0f, -48f);
        CreateStretchLabel(titleArea, "Title", "导入数据", font, 44f, ColBlack, TextAlignmentOptions.Center);

        // 风险提示（策划案 §5.6）
        RectTransform riskRow = CreateRect(dialog, "RiskRow",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        riskRow.sizeDelta = new Vector2(-60f, 56f);
        riskRow.anchoredPosition = new Vector2(0f, -124f);
        CreateStretchLabel(riskRow, "Label", "导入不会删除现有数据，重复记录会自动跳过", font, 26f, ColGray, TextAlignmentOptions.Center);

        // 选择文件按钮（黄底黑字，通栏）
        RectTransform pickRow = CreateRect(dialog, "PickRow",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        pickRow.sizeDelta = new Vector2(0f, 96f);
        pickRow.anchoredPosition = new Vector2(0f, -200f);

        refs.BtnPick = CreateColorButton(pickRow, "btnPick", "选择 Excel 文件", font,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero, 36f, ColYellow, ColBlack);
        Stretch(refs.BtnPick.GetComponent<RectTransform>(), new Vector2(50f, 8f), new Vector2(-50f, -8f));

        // 文件名行
        RectTransform fileNameRect = CreateRect(dialog, "txtFileName",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        fileNameRect.sizeDelta = new Vector2(-60f, 56f);
        fileNameRect.anchoredPosition = new Vector2(0f, -276f);
        refs.TxtFileName = CreateStretchLabel(fileNameRect, "Label", "未选择文件", font, 28f, ColGrayText, TextAlignmentOptions.Center);

        // 预解析统计（可换行两行）
        RectTransform statsRect = CreateRect(dialog, "txtStats",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        statsRect.sizeDelta = new Vector2(-60f, 90f);
        statsRect.anchoredPosition = new Vector2(0f, -349f);
        refs.TxtStats = CreateStretchLabel(statsRect, "Label", "选择文件后自动预解析", font, 30f, ColBlack, TextAlignmentOptions.Center);

        // 错误行预览（前 5 条，红色，左上对齐）
        RectTransform errorsRect = CreateRect(dialog, "txtErrors",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        errorsRect.sizeDelta = new Vector2(-60f, 170f);
        errorsRect.anchoredPosition = new Vector2(0f, -479f);
        refs.TxtErrors = CreateStretchLabel(errorsRect, "Label", "", font, 28f, ColError, TextAlignmentOptions.TopLeft);
        refs.TxtErrors.margin = new Vector4(24f, 12f, 24f, 6f);

        // 按钮行：左 确认导入（黄底黑字）/ 右 取消（灰底）
        RectTransform btnRow = CreateRect(dialog, "BtnRow",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        btnRow.sizeDelta = new Vector2(0f, 110f);
        btnRow.anchoredPosition = new Vector2(0f, -605f);

        refs.BtnConfirm = CreateColorButton(btnRow, "btnConfirm", "确认导入", font,
            new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero, 36f, ColYellow, ColBlack);
        Stretch(refs.BtnConfirm.GetComponent<RectTransform>(), new Vector2(50f, 12f), new Vector2(-10f, -12f));

        refs.BtnCancel = CreateColorButton(btnRow, "btnCancel", "取消", font,
            new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero, 36f, ColKey, ColGray);
        Stretch(refs.BtnCancel.GetComponent<RectTransform>(), new Vector2(10f, 12f), new Vector2(-50f, -12f));

        return refs;
    }

    /// <summary>
    /// 预固化导入弹窗界面用字进 Dynamic 图集并落盘（冷启动防方块，步骤02/03/04/05 同款）
    /// </summary>
    private static void PrePopulateFontGlyphs(TMP_FontAsset font)
    {
        string chars = "导入数据不会删除现有重复记录会自动跳过选择件后预解析未行可笔格式错误请式符在或法访败" +
                       "确认完成新增全部取消问授权访问不存读取失败内容为空日期能别类型支收金额正数 AccountBook_*.xlsxExcel" +
                       "0123456789｜（）：，.-~_";
        bool ok = font.TryAddCharacters(chars);

        if (ok)
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            Debug.Log("[ImportBuild] 已预固化导入弹窗界面用字进 Dynamic 图集（" + chars.Length + " 字）。");
        }
        else
        {
            Debug.LogWarning("[ImportBuild] 部分界面用字未能预固化（运行时动态添加仍会兜底）。");
        }
    }

    // ---------- 接线自检 ----------

    /// <summary>
    /// 搭建即自检：逐项核对 ImportPanelUI 序列化引用、ExcelTransferManager、
    /// UIManager/ChartPanelUI 反向引用与导入按钮可用性、ToastUI 存在
    /// </summary>
    private static bool VerifyWiring(PanelRefs refs, ImportPanelUI panelUi, ExcelTransferManager excelTransferManager,
        ToastUI toastUi, ChartPanelUI chartPanelUi)
    {
        bool allPass = true;
        SerializedObject so = new SerializedObject(panelUi);
        allPass &= CheckRef(so, "btnPick");
        allPass &= CheckRef(so, "txtFileName") && CheckRef(so, "txtStats") && CheckRef(so, "txtErrors");
        allPass &= CheckRef(so, "btnConfirm") && CheckRef(so, "btnCancel");
        allPass &= Check("ExcelTransferManager 已接入导入弹窗",
            excelTransferManager != null && so.FindProperty("excelTransferManager").objectReferenceValue == excelTransferManager);

        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        allPass &= Check("UIManager.importPanel 指向 ImportPanel",
            uiManager != null && new SerializedObject(uiManager).FindProperty("importPanel").objectReferenceValue == refs.Panel);

        if (chartPanelUi != null)
        {
            SerializedObject soChart = new SerializedObject(chartPanelUi);
            SerializedProperty importPanelProp = soChart.FindProperty("importPanel");
            SerializedProperty btnImportProp = soChart.FindProperty("btnImport");
            allPass &= Check("ChartPanelUI.importPanel 指向导入弹窗",
                importPanelProp != null && importPanelProp.objectReferenceValue == panelUi);
            allPass &= Check("图表页导入按钮已启用",
                btnImportProp != null && btnImportProp.objectReferenceValue != null &&
                ((Button)btnImportProp.objectReferenceValue).interactable);
        }
        else
        {
            allPass &= Check("ChartPanelUI 存在", false);
        }

        allPass &= Check("ToastUI 已搭建（步骤05 产物）", toastUi != null);

        return allPass;
    }

    private static bool CheckRef(SerializedObject so, string fieldName)
    {
        return Check(fieldName + " 已接线", so.FindProperty(fieldName).objectReferenceValue != null);
    }

    private static bool Check(string label, bool pass)
    {
        Debug.Log($"[ImportBuild] {(pass ? "PASS" : "FAIL")} - {label}");
        return pass;
    }

    // ---------- 工具（与 ExportPanelBuilder 同款） ----------

    private static void SetRef(SerializedObject so, string fieldName, UnityEngine.Object value)
    {
        so.FindProperty(fieldName).objectReferenceValue = value;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        return rect;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Button CreateColorButton(Transform parent, string name, string label, TMP_FontAsset font,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition,
        float fontSize, Color bgColor, Color textColor)
    {
        RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, pivot);
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = bgColor;
        image.raycastTarget = true;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        CreateStretchLabel(rect, "Label", label, font, fontSize, textColor, TextAlignmentOptions.Center);
        return button;
    }

    private static TextMeshProUGUI CreateStretchLabel(Transform parent, string name, string text, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }

    private static Color FromHex(int hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Color(r, g, b, 1f);
    }
}
