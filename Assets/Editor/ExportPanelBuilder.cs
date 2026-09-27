using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤05 一键搭建导出弹窗：在步骤01 预留的 ExportPanel 壳（遮罩+Dialog+标题）里填充
/// 周/月/年三档 Toggle + ‹ 周期 › 平移行 + 预览行 + 导出/取消按钮，挂 ExportPanelUI 接线；
/// 同时建 ExcelTransferManager（Managers 下）、ToastUI（Canvas 顶悬浮提示）、
/// 启用图表页导出按钮并回填 UIManager/ChartPanelUI 引用 → 存场景 → 预固化字形 → 接线自检。
/// 幂等：重复运行先清 Dialog 内容重建。
/// </summary>
public static class ExportPanelBuilder
{
    private const string MenuItemPath = "AccountBook/10-搭建导出弹窗";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string FontAssetPath = "Assets/Fonts/STKAITI Dynamic SDF.asset";

    // 色板（与 MainSceneBuilder/ChartPanelBuilder 一致）
    private static readonly Color ColYellow = FromHex(0xFFD100);
    private static readonly Color ColBlack = FromHex(0x222222);
    private static readonly Color ColGray = FromHex(0x666666);
    private static readonly Color ColGrayText = FromHex(0x444444);
    private static readonly Color ColWhite = FromHex(0xFFFFFF);
    private static readonly Color ColKey = FromHex(0xF2F2F2);

    /// <summary>
    /// 搭建产物引用包
    /// </summary>
    private class PanelRefs
    {
        public GameObject Panel;
        public Toggle ToggleWeek;
        public Toggle ToggleMonth;
        public Toggle ToggleYear;
        public TextMeshProUGUI ToggleWeekLabel;
        public TextMeshProUGUI ToggleMonthLabel;
        public TextMeshProUGUI ToggleYearLabel;
        public Image ToggleWeekSelectedBg;
        public Image ToggleMonthSelectedBg;
        public Image ToggleYearSelectedBg;
        public Button BtnPrev;
        public Button BtnNext;
        public TextMeshProUGUI TxtPeriod;
        public TextMeshProUGUI TxtPreview;
        public Button BtnExport;
        public Button BtnCancel;
    }

    [MenuItem(MenuItemPath)]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[ExportBuild] 编辑器正在 Play 模式，退出后再搭建。");
            return;
        }

        try
        {
            Build();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ExportBuild] 搭建失败：{ex}\n请确认步骤 01/04 已完成（菜单 AccountBook/01、08）。");
        }
    }

    /// <summary>
    /// 主入口：填壳 → 挂组件接线 → Toast/Manager → 图表页启用导出按钮 → 存场景 → 自检
    /// </summary>
    private static void Build()
    {
        Debug.Log("[ExportBuild] ===== 步骤05 导出弹窗搭建开始 =====");

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
        Transform oldPanel = canvas.Find("ExportPanel");

        if (oldPanel == null)
        {
            throw new InvalidOperationException("场景中找不到 ExportPanel 壳，请先运行步骤 01。");
        }

        // —— 填充 Dialog（壳的遮罩 Button 绑定 UIManager.CloseExportPanel，保留不动） ——
        Transform dialog = oldPanel.Find("Dialog");

        if (dialog == null)
        {
            throw new InvalidOperationException("ExportPanel 下找不到 Dialog（步骤01 壳结构不符）。");
        }

        RectTransform dialogRect = (RectTransform)dialog;
        dialogRect.sizeDelta = new Vector2(620f, 560f);   // 壳默认 420 高，导出弹窗内容更多

        ClearChildren(dialog);

        PanelRefs refs = BuildExportDialog(dialog, font);
        refs.Panel = oldPanel.gameObject;

        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        ChartPanelUI chartPanelUi = UnityEngine.Object.FindFirstObjectByType<ChartPanelUI>(FindObjectsInactive.Include);

        // —— ExcelTransferManager（Managers 下，幂等） ——
        GameObject managersGo = GameObject.Find("Managers");

        if (managersGo == null)
        {
            throw new InvalidOperationException("场景中找不到 Managers，请先运行步骤 01。");
        }

        ExcelTransferManager excelTransferManager = managersGo.GetComponent<ExcelTransferManager>();

        if (excelTransferManager == null)
        {
            excelTransferManager = managersGo.AddComponent<ExcelTransferManager>();
        }

        // —— 挂 ExportPanelUI 并一次性接线 ——
        ExportPanelUI panelUi = refs.Panel.GetComponent<ExportPanelUI>();

        if (panelUi == null)
        {
            panelUi = refs.Panel.AddComponent<ExportPanelUI>();
        }

        SerializedObject so = new SerializedObject(panelUi);
        SetRef(so, "accountManager", accountManager);
        SetRef(so, "excelTransferManager", excelTransferManager);
        SetRef(so, "toggleWeek", refs.ToggleWeek);
        SetRef(so, "toggleMonth", refs.ToggleMonth);
        SetRef(so, "toggleYear", refs.ToggleYear);
        SetRef(so, "toggleWeekLabel", refs.ToggleWeekLabel);
        SetRef(so, "toggleMonthLabel", refs.ToggleMonthLabel);
        SetRef(so, "toggleYearLabel", refs.ToggleYearLabel);
        SetRef(so, "toggleWeekSelectedBg", refs.ToggleWeekSelectedBg);
        SetRef(so, "toggleMonthSelectedBg", refs.ToggleMonthSelectedBg);
        SetRef(so, "toggleYearSelectedBg", refs.ToggleYearSelectedBg);
        SetRef(so, "btnPrev", refs.BtnPrev);
        SetRef(so, "btnNext", refs.BtnNext);
        SetRef(so, "txtPeriod", refs.TxtPeriod);
        SetRef(so, "txtPreview", refs.TxtPreview);
        SetRef(so, "btnExport", refs.BtnExport);
        SetRef(so, "btnCancel", refs.BtnCancel);
        so.ApplyModifiedPropertiesWithoutUndo();

        // —— UIManager.exportPanel 重绑（幂等，壳未重建） ——
        SerializedObject soUi = new SerializedObject(uiManager);
        soUi.FindProperty("exportPanel").objectReferenceValue = refs.Panel;
        soUi.ApplyModifiedPropertiesWithoutUndo();

        // —— 图表页导出按钮启用 + 弹窗引用回填 ——
        if (chartPanelUi != null)
        {
            SerializedObject soChart = new SerializedObject(chartPanelUi);
            SetRef(soChart, "exportPanel", panelUi);
            soChart.ApplyModifiedPropertiesWithoutUndo();

            SerializedProperty btnExportProp = soChart.FindProperty("btnExport");

            if (btnExportProp != null && btnExportProp.objectReferenceValue != null)
            {
                ((Button)btnExportProp.objectReferenceValue).interactable = true;   // 步骤05 启用，步骤06 的 btnImport 保持置灰
            }
        }

        // —— ToastUI（Canvas 下最顶层，常驻隐藏） ——
        BuildToast(canvas, font);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        PrePopulateFontGlyphs(font);

        bool pass = VerifyWiring(refs, panelUi, uiManager, chartPanelUi);
        Debug.Log($"[ExportBuild] ===== 步骤05 导出弹窗搭建完成，接线自检 {(pass ? "通过" : "失败(见上方 [ExportBuild] FAIL 日志)")} =====");
    }

    // ---------- UI 搭建 ----------

    /// <summary>
    /// Dialog 内容纵向堆叠（高 560）：标题 96 / 档位 100 / 周期行 90 / 预览 80 / 按钮行 110
    /// </summary>
    private static PanelRefs BuildExportDialog(Transform dialog, TMP_FontAsset font)
    {
        PanelRefs refs = new PanelRefs();

        // 标题（壳原本有 TitleArea，ClearChildren 已清，重建）
        RectTransform titleArea = CreateRect(dialog, "TitleArea",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        titleArea.sizeDelta = new Vector2(0f, 96f);
        titleArea.anchoredPosition = new Vector2(0f, -48f);
        CreateStretchLabel(titleArea, "Title", "导出数据", font, 44f, ColBlack, TextAlignmentOptions.Center);

        // 第一行：周/月/年 三档 Toggle（选中块变黑白字，与图表页同款）
        RectTransform rangeRow = CreateRect(dialog, "RangeRow",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        rangeRow.sizeDelta = new Vector2(0f, 100f);
        rangeRow.anchoredPosition = new Vector2(0f, -150f);

        RectTransform segment = CreateRect(rangeRow, "Segmented",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        segment.sizeDelta = new Vector2(560f, 84f);
        ToggleGroup group = segment.gameObject.AddComponent<ToggleGroup>();

        refs.ToggleWeek = CreateSegmentToggle(segment, "toggleWeek", "周", font, group, -183f, true, refs, "Week");
        refs.ToggleMonth = CreateSegmentToggle(segment, "toggleMonth", "月", font, group, 0f, false, refs, "Month");
        refs.ToggleYear = CreateSegmentToggle(segment, "toggleYear", "年", font, group, 183f, false, refs, "Year");

        // 第二行：‹ 周期 ›
        RectTransform periodRow = CreateRect(dialog, "PeriodRow",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        periodRow.sizeDelta = new Vector2(0f, 90f);
        periodRow.anchoredPosition = new Vector2(0f, -255f);

        refs.BtnPrev = CreateColorButton(periodRow, "btnPrev", "‹", font,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(96f, 80f), new Vector2(45f, 0f), 44f, ColKey, ColBlack);
        refs.BtnNext = CreateColorButton(periodRow, "btnNext", "›", font,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(96f, 80f), new Vector2(-45f, 0f), 44f, ColKey, ColBlack);

        RectTransform periodTextRect = CreateRect(periodRow, "txtPeriod",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        periodTextRect.sizeDelta = new Vector2(330f, 80f);
        refs.TxtPeriod = CreateStretchLabel(periodTextRect, "Label", "2026-09-21 ~ 09-27", font, 34f, ColBlack, TextAlignmentOptions.Center);

        // 预览行
        RectTransform previewRect = CreateRect(dialog, "txtPreview",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        previewRect.sizeDelta = new Vector2(-60f, 80f);
        previewRect.anchoredPosition = new Vector2(0f, -340f);
        refs.TxtPreview = CreateStretchLabel(previewRect, "Label", "本周期 0 笔", font, 32f, ColGrayText, TextAlignmentOptions.Center);

        // 按钮行：左 导出 Excel（黄底黑字）/ 右 取消（灰底）
        RectTransform btnRow = CreateRect(dialog, "BtnRow",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        btnRow.sizeDelta = new Vector2(0f, 110f);
        btnRow.anchoredPosition = new Vector2(0f, -445f);

        refs.BtnExport = CreateColorButton(btnRow, "btnExport", "导出 Excel", font,
            new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero, 36f, ColYellow, ColBlack);
        Stretch(refs.BtnExport.GetComponent<RectTransform>(), new Vector2(50f, 12f), new Vector2(-10f, -12f));

        refs.BtnCancel = CreateColorButton(btnRow, "btnCancel", "取消", font,
            new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero, 36f, ColKey, ColGray);
        Stretch(refs.BtnCancel.GetComponent<RectTransform>(), new Vector2(10f, 12f), new Vector2(-50f, -12f));

        return refs;
    }

    /// <summary>
    /// 档位 Toggle：透明底收点击 + 黑选中块（toggle.graphic）+ 文字，选中态文字白色由运行时同步
    /// </summary>
    private static Toggle CreateSegmentToggle(RectTransform segment, string name, string label, TMP_FontAsset font,
        ToggleGroup group, float x, bool isOn, PanelRefs refs, string refPrefix)
    {
        RectTransform rect = CreateRect(segment, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        rect.sizeDelta = new Vector2(178f, 80f);
        rect.anchoredPosition = new Vector2(x, 0f);

        Image bg = rect.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0f);   // 透明收点击
        bg.raycastTarget = true;

        Toggle toggle = rect.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.toggleTransition = Toggle.ToggleTransition.None;
        toggle.group = group;
        toggle.isOn = isOn;

        RectTransform selectedBg = CreateRect(rect, "SelectedBg", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        selectedBg.offsetMin = Vector2.zero;
        selectedBg.offsetMax = Vector2.zero;
        Image selectedImage = selectedBg.gameObject.AddComponent<Image>();
        selectedImage.color = ColBlack;
        selectedImage.raycastTarget = false;

        TextMeshProUGUI labelTmp = CreateStretchLabel(rect, "Label", label, font, 34f, ColBlack, TextAlignmentOptions.Center);

        toggle.graphic = selectedImage;

        switch (refPrefix)
        {
            case "Week":
                refs.ToggleWeekLabel = labelTmp;
                refs.ToggleWeekSelectedBg = selectedImage;
                break;

            case "Month":
                refs.ToggleMonthLabel = labelTmp;
                refs.ToggleMonthSelectedBg = selectedImage;
                break;

            case "Year":
                refs.ToggleYearLabel = labelTmp;
                refs.ToggleYearSelectedBg = selectedImage;
                break;
        }

        return toggle;
    }

    /// <summary>
    /// Toast 悬浮提示条：Canvas 顶下方（避开状态栏），黑 78% 圆角条 + 白字，常驻 alpha=0
    /// </summary>
    private static void BuildToast(Transform canvas, TMP_FontAsset font)
    {
        Transform oldToast = canvas.Find("Toast");
        GameObject toast = oldToast != null ? oldToast.gameObject : null;

        if (toast == null)
        {
            toast = new GameObject("Toast", typeof(RectTransform));
            toast.transform.SetParent(canvas, false);
        }

        RectTransform rect = (RectTransform)toast.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(640f, 110f);
        rect.anchoredPosition = new Vector2(0f, -330f);   // 顶栏(300 高)之下、图表摘要区上方，避免遮周/月/年选择器

        Image bg = toast.GetComponent<Image>();

        if (bg == null)
        {
            bg = toast.AddComponent<Image>();
        }

        bg.color = new Color(0f, 0f, 0f, 0.78f);
        bg.raycastTarget = false;

        ToastUI toastUi = toast.GetComponent<ToastUI>();

        if (toastUi == null)
        {
            toastUi = toast.AddComponent<ToastUI>();
        }

        Transform labelTf = rect.Find("Label");
        TextMeshProUGUI label;

        if (labelTf != null)
        {
            label = labelTf.GetComponentInChildren<TextMeshProUGUI>();
        }
        else
        {
            RectTransform labelRect = CreateRect(rect, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            labelRect.offsetMin = new Vector2(24f, 8f);
            labelRect.offsetMax = new Vector2(-24f, -8f);
            label = CreateStretchLabel(labelRect, "Text", "", font, 34f, ColWhite, TextAlignmentOptions.Center);
        }

        CanvasGroup canvasGroup = toast.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            canvasGroup = toast.AddComponent<CanvasGroup>();
        }

        SerializedObject so = new SerializedObject(toastUi);
        SetRef(so, "label", label);
        SetRef(so, "canvasGroup", canvasGroup);
        so.ApplyModifiedPropertiesWithoutUndo();

        toast.transform.SetAsLastSibling();   // 压过所有面板（导出弹窗之上）
        toast.SetActive(true);   // 常驻 active，alpha 控制显隐（保证 Awake 注册静态实例）
    }

    /// <summary>
    /// 预固化导出弹窗界面用字进 Dynamic 图集并落盘（冷启动防方块，步骤02/03/04 同款）
    /// </summary>
    private static void PrePopulateFontGlyphs(TMP_FontAsset font)
    {
        string chars = "导出数据完成取周月年笔支入本没有记录服务未初始化‹›0123456789~-./： ，";
        bool ok = font.TryAddCharacters(chars);

        if (ok)
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            Debug.Log("[ExportBuild] 已预固化导出弹窗界面用字进 Dynamic 图集（" + chars.Length + " 字）。");
        }
        else
        {
            Debug.LogWarning("[ExportBuild] 部分界面用字未能预固化（运行时动态添加仍会兜底）。");
        }
    }

    // ---------- 接线自检 ----------

    /// <summary>
    /// 搭建即自检：逐项核对 ExportPanelUI 序列化引用、ToastUI、ExcelTransferManager、
    /// UIManager/ChartPanelUI 反向引用与导出按钮可用性
    /// </summary>
    private static bool VerifyWiring(PanelRefs refs, ExportPanelUI panelUi, UIManager uiManager, ChartPanelUI chartPanelUi)
    {
        bool allPass = true;
        SerializedObject so = new SerializedObject(panelUi);
        allPass &= CheckRef(so, "toggleWeek") && CheckRef(so, "toggleMonth") && CheckRef(so, "toggleYear");
        allPass &= CheckRef(so, "toggleWeekLabel") && CheckRef(so, "toggleMonthLabel") && CheckRef(so, "toggleYearLabel");
        allPass &= CheckRef(so, "toggleWeekSelectedBg") && CheckRef(so, "toggleMonthSelectedBg") && CheckRef(so, "toggleYearSelectedBg");
        allPass &= CheckRef(so, "btnPrev") && CheckRef(so, "btnNext");
        allPass &= CheckRef(so, "txtPeriod") && CheckRef(so, "txtPreview");
        allPass &= CheckRef(so, "btnExport") && CheckRef(so, "btnCancel");
        allPass &= Check("AccountManager 自动引用", so.FindProperty("accountManager").objectReferenceValue != null);
        allPass &= Check("ExcelTransferManager 已挂 Managers 并接入弹窗", so.FindProperty("excelTransferManager").objectReferenceValue != null);

        allPass &= Check("UIManager.exportPanel 指向 ExportPanel",
            uiManager != null && new SerializedObject(uiManager).FindProperty("exportPanel").objectReferenceValue == refs.Panel);

        if (chartPanelUi != null)
        {
            SerializedObject soChart = new SerializedObject(chartPanelUi);
            SerializedProperty exportPanelProp = soChart.FindProperty("exportPanel");
            SerializedProperty btnExportProp = soChart.FindProperty("btnExport");
            allPass &= Check("ChartPanelUI.exportPanel 指向导出弹窗",
                exportPanelProp != null && exportPanelProp.objectReferenceValue == panelUi);
            allPass &= Check("图表页导出按钮已启用",
                btnExportProp != null && btnExportProp.objectReferenceValue != null &&
                ((Button)btnExportProp.objectReferenceValue).interactable);
        }
        else
        {
            allPass &= Check("ChartPanelUI 存在", false);
        }

        ToastUI toastUi = UnityEngine.Object.FindFirstObjectByType<ToastUI>(FindObjectsInactive.Include);
        allPass &= Check("ToastUI 已搭建", toastUi != null);

        return allPass;
    }

    private static bool CheckRef(SerializedObject so, string fieldName)
    {
        return Check(fieldName + " 已接线", so.FindProperty(fieldName).objectReferenceValue != null);
    }

    private static bool Check(string label, bool pass)
    {
        Debug.Log($"[ExportBuild] {(pass ? "PASS" : "FAIL")} - {label}");
        return pass;
    }

    // ---------- 工具 ----------

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
