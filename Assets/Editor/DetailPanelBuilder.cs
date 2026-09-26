using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 03 一键搭建明细页：重建 DetailPanel（年月切换顶栏 + 收支合计 + 分组列表 ScrollView + 空态）、
/// 生成 DayHeaderUI / RecordItemUI 预制体、搭建可复用 ConfirmDialog、接线 DetailPanelUI / UIManager，
/// 并做引用自检。幂等可重跑；依赖步骤 01 场景、Dynamic 字体与圆形 sprite。菜单：AccountBook/06-搭建明细页。
/// 自动链（每编辑器会话一次）：轮询等步骤 01/02 旧自检链（RuntimeCheck/RecordCheck/RecordShot）空闲后
/// 搭建 → 明细页流程自检 → 截图，结果全部落 Editor.log。
/// </summary>
public static class DetailPanelBuilder
{
    private const string MenuItemPath = "AccountBook/06-搭建明细页";
    private const string AutoRunSessionKey = "AccountBook.DetailPanelBuilder.SessionRan";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string DayHeaderPrefabPath = "Assets/Prefabs/DayHeaderUI.prefab";
    private const string RecordItemPrefabPath = "Assets/Prefabs/RecordItemUI.prefab";
    private const string CircleSpritePath = "Assets/Art/Generated/circle.png";
    private const string FontAssetPath = "Assets/Fonts/STKAITI Dynamic SDF.asset";
    private const double IdleWaitSeconds = 2.0;   // 旧链连续空闲该秒数才启动，避免多个 Play 会话互相打断

    // 色板（步骤00 §2.4，与 MainSceneBuilder/RecordPanelBuilder 一致）
    private static readonly Color ColYellow = FromHex(0xFFD100);
    private static readonly Color ColBlack = FromHex(0x222222);
    private static readonly Color ColGray = FromHex(0x666666);
    private static readonly Color ColGrayText = FromHex(0x444444);
    private static readonly Color ColPlaceholder = FromHex(0x999999);
    private static readonly Color ColDivider = FromHex(0xEEEEEE);
    private static readonly Color ColBackground = FromHex(0xF7F7F7);
    private static readonly Color ColWhite = FromHex(0xFFFFFF);
    private static readonly Color ColHeaderBand = FromHex(0xEFEFEF);
    private static readonly Color ColKey = FromHex(0xF2F2F2);
    private static readonly Color ColMask = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color ColLabelOnYellow = new Color(0f, 0f, 0f, 0.62f);

    /// <summary>
    /// 搭建产物引用包
    /// </summary>
    private class PanelRefs
    {
        public GameObject Panel;
        public Button BtnPrevMonth;
        public Button BtnNextMonth;
        public TextMeshProUGUI TxtYearMonth;
        public TextMeshProUGUI TxtIncome;
        public TextMeshProUGUI TxtExpense;
        public RectTransform ListContent;
        public DayHeaderUI DayHeaderPrefab;
        public RecordItemUI RecordItemPrefab;
        public GameObject EmptyLabel;
        public ConfirmDialog ConfirmBox;
    }

    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        if (SessionState.GetBool(AutoRunSessionKey, false))
        {
            return;
        }

        SessionState.SetBool(AutoRunSessionKey, true);

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        EditorApplication.update -= PumpIdle;
        EditorApplication.update += PumpIdle;
    }

    /// <summary>
    /// 轮询等待旧自检链空闲（连续 IdleWaitSeconds 无 Play 会话、无挂起标记）后启动搭建+自检
    /// </summary>
    private static void PumpIdle()
    {
        if (IsOtherChainsBusy())
        {
            idleSince = -1.0;
            return;
        }

        if (idleSince < 0.0)
        {
            idleSince = EditorApplication.timeSinceStartup;
            return;
        }

        if (EditorApplication.timeSinceStartup - idleSince < IdleWaitSeconds)
        {
            return;
        }

        EditorApplication.update -= PumpIdle;

        try
        {
            Build();
            SessionState.SetBool("AccountBook.DetailShot.Pending", true);
            DetailFlowCheck.Run();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DetailBuild] 自动搭建失败：{ex}\n可用菜单 {MenuItemPath} 重跑。");
        }
    }

    private static double idleSince = -1.0;

    /// <summary>
    /// 步骤 01/02 旧链是否在途：Play 会话中、自检未收尾、或截图标记挂起
    /// </summary>
    private static bool IsOtherChainsBusy()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return true;
        }

        // RecordShotRerun 每会话会重新武装旧截图链，它没表态前先等
        if (SessionState.GetBool("AccountBook.RecordShotRerun.Ran2", false) == false)
        {
            return true;
        }

        if (SessionState.GetBool("AccountBook.RuntimeCheck.Started", false) &&
            SessionState.GetBool("AccountBook.RuntimeCheck.Done", false) == false)
        {
            return true;
        }

        if (SessionState.GetBool("AccountBook.RecordCheck.Started", false) &&
            SessionState.GetBool("AccountBook.RecordCheck.Done", false) == false)
        {
            return true;
        }

        if (SessionState.GetBool("AccountBook.RecordShot.Pending", false) ||
            SessionState.GetBool("AccountBook.RecordShot.Armed", false))
        {
            return true;
        }

        return false;
    }

    [MenuItem(MenuItemPath)]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[DetailBuild] 编辑器正在 Play 模式，退出后再搭建。");
            return;
        }

        try
        {
            Build();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DetailBuild] 搭建失败：{ex}\n请确认步骤 01/02 已完成（菜单 AccountBook/01、04）。");
        }
    }

    /// <summary>
    /// 主入口：预制体 → 重建面板与确认弹窗 → 接线 → 存场景 → 预固化字形 → 自检
    /// </summary>
    private static void Build()
    {
        Debug.Log("[DetailBuild] ===== 步骤03 明细页搭建开始 =====");

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        if (font == null)
        {
            throw new FileNotFoundException("找不到 " + FontAssetPath + "，请先运行步骤 01。");
        }

        Sprite circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);

        if (circleSprite == null)
        {
            throw new FileNotFoundException("找不到 " + CircleSpritePath + "，请先运行步骤 01。");
        }

        DayHeaderUI dayHeaderPrefab;
        RecordItemUI recordItemPrefab;

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject canvasGo = GameObject.Find("Canvas");

        if (canvasGo == null)
        {
            throw new InvalidOperationException("场景中找不到 Canvas，请先运行步骤 01。");
        }

        Transform canvas = canvasGo.transform;

        // 预制体在开场景之后创建：OpenScene 会触发资源重导入，先建后开会让本地引用变假 null（首轮自检踩过）
        dayHeaderPrefab = CreateDayHeaderPrefab(font);
        recordItemPrefab = CreateRecordItemPrefab(font, circleSprite);

        // 保留原绘制顺序（明细 < 图表 < 记账 < 导出 < 导入 < 底栏）
        Transform oldPanel = canvas.Find("DetailPanel");
        int siblingIndex = oldPanel != null ? oldPanel.GetSiblingIndex() : 0;

        if (oldPanel != null)
        {
            UnityEngine.Object.DestroyImmediate(oldPanel.gameObject);
        }

        PanelRefs refs = BuildDetailPanel(canvas, font, circleSprite, dayHeaderPrefab, recordItemPrefab);
        refs.Panel.transform.SetSiblingIndex(siblingIndex);
        refs.Panel.SetActive(true);   // 明细是默认页

        // 确认弹窗放 Canvas 根最后（绘制在最上层，底栏也被遮罩盖住），供后续流程复用
        Transform oldDialog = canvas.Find("ConfirmDialog");

        if (oldDialog != null)
        {
            UnityEngine.Object.DestroyImmediate(oldDialog.gameObject);
        }

        refs.ConfirmBox = BuildConfirmDialog(canvas, font);

        // —— 接线 DetailPanelUI ——
        DetailPanelUI panelUi = refs.Panel.GetComponent<DetailPanelUI>();
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        CategoryIconProvider iconProvider = UnityEngine.Object.FindFirstObjectByType<CategoryIconProvider>();
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();

        SerializedObject so = new SerializedObject(panelUi);
        SetRef(so, "accountManager", accountManager);
        SetRef(so, "iconProvider", iconProvider);
        SetRef(so, "recordPanelUI", UnityEngine.Object.FindFirstObjectByType<RecordPanelUI>(FindObjectsInactive.Include));
        SetRef(so, "uiManager", uiManager);
        SetRef(so, "btnPrevMonth", refs.BtnPrevMonth);
        SetRef(so, "btnNextMonth", refs.BtnNextMonth);
        SetRef(so, "txtYearMonth", refs.TxtYearMonth);
        SetRef(so, "txtIncome", refs.TxtIncome);
        SetRef(so, "txtExpense", refs.TxtExpense);
        SetRef(so, "listContent", refs.ListContent);
        SetRef(so, "dayHeaderPrefab", refs.DayHeaderPrefab);
        SetRef(so, "recordItemPrefab", refs.RecordItemPrefab);
        SetRef(so, "emptyLabel", refs.EmptyLabel);
        SetRef(so, "confirmDialog", refs.ConfirmBox);
        so.ApplyModifiedPropertiesWithoutUndo();

        // —— 重接 UIManager.detailPanel（旧面板已销毁） ——
        SerializedObject soUi = new SerializedObject(uiManager);
        soUi.FindProperty("detailPanel").objectReferenceValue = refs.Panel;
        soUi.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        PrePopulateFontGlyphs(font);

        bool pass = VerifyWiring(refs, panelUi, uiManager);
        Debug.Log($"[DetailBuild] ===== 步骤03 明细页搭建完成，接线自检 {(pass ? "通过" : "失败(见上方 [DetailBuild] FAIL 日志)")} =====");
    }

    /// <summary>
    /// 预固化明细页界面用字进 Dynamic 图集并落盘（冷启动防方块，步骤02 同款）
    /// </summary>
    private static void PrePopulateFontGlyphs(TMP_FontAsset font)
    {
        System.Text.StringBuilder chars = new System.Text.StringBuilder();

        for (int i = 0; i < CategoryTable.All.Count; i++)
        {
            chars.Append(CategoryTable.All[i].Name);
        }

        chars.Append("支出收入取消今天退格完成备注：点击填写备分明细图表记账元一二三四五六日回月年0123456789.+-");
        chars.Append("这个月还没有记录星期删除无法恢复确认吗本笔<>:");

        bool ok = font.TryAddCharacters(chars.ToString());

        if (ok)
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            Debug.Log("[DetailBuild] 已预固化明细页界面用字进 Dynamic 图集（" + chars.Length + " 字）。");
        }
        else
        {
            Debug.LogWarning("[DetailBuild] 部分界面用字未能预固化（运行时动态添加仍会兜底）。");
        }
    }

    // ---------- 面板搭建 ----------

    /// <summary>
    /// DetailPanel 全量重建：灰白底全屏 + 内容容器（底部让出底栏 170）
    /// </summary>
    private static PanelRefs BuildDetailPanel(Transform canvas, TMP_FontAsset font, Sprite circleSprite,
        DayHeaderUI dayHeaderPrefab, RecordItemUI recordItemPrefab)
    {
        PanelRefs refs = new PanelRefs();
        refs.DayHeaderPrefab = dayHeaderPrefab;
        refs.RecordItemPrefab = recordItemPrefab;

        GameObject panel = new GameObject("DetailPanel", typeof(RectTransform));
        panel.transform.SetParent(canvas, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = ColBackground;
        panelBg.raycastTarget = false;
        panel.AddComponent<DetailPanelUI>();

        RectTransform content = CreateRect(panel.transform, "PanelContent", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        content.offsetMin = new Vector2(0f, 170f);   // 底栏让位（底栏覆盖在上层可点，同记账页）
        content.offsetMax = Vector2.zero;

        BuildTopBar(content, font, refs);
        BuildListScrollView(content, font, refs);
        refs.EmptyLabel = BuildEmptyLabel(content, font);

        refs.Panel = panel;
        return refs;
    }

    /// <summary>
    /// 顶栏：主黄底 + ‹ 2026年 09月 › 月切换 + 本月支出/收入两列合计（黄底沉浸到状态栏后面）
    /// </summary>
    private static void BuildTopBar(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform topBar = CreateRect(content, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        topBar.sizeDelta = new Vector2(0f, 300f);
        Image topBarImage = topBar.gameObject.AddComponent<Image>();
        topBarImage.color = ColYellow;
        topBarImage.raycastTarget = false;

        RectTransform topContent = CreateRect(topBar.transform, "TopContent", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        topContent.offsetMin = Vector2.zero;
        topContent.offsetMax = Vector2.zero;
        SafeAreaFitter topFitter = topContent.gameObject.AddComponent<SafeAreaFitter>();
        SetEnumField(topFitter, "mode", SafeAreaFitter.Mode.Top);

        // 第一行：‹ 年月 ›
        RectTransform monthRow = CreateRect(topContent, "MonthRow", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        monthRow.sizeDelta = new Vector2(0f, 110f);
        monthRow.anchoredPosition = new Vector2(0f, -8f);

        refs.BtnPrevMonth = CreateTextButton(monthRow, "btnPrevMonth", "<", font,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(140f, 100f), new Vector2(20f, 0f), 56f);

        refs.TxtYearMonth = CreateAnchoredLabel(monthRow, "txtYearMonth", "2026年 09月", font, 48f, ColBlack,
            TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(440f, 90f), Vector2.zero);

        refs.BtnNextMonth = CreateTextButton(monthRow, "btnNextMonth", ">", font,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(140f, 100f), new Vector2(-20f, 0f), 56f);

        // 第二行：本月支出 / 本月收入 两列合计
        RectTransform summaryRow = CreateRect(topContent, "SummaryRow", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        summaryRow.sizeDelta = new Vector2(0f, 160f);

        refs.TxtExpense = BuildSummaryColumn(summaryRow, "ExpenseCol", "本月支出", font);
        refs.TxtIncome = BuildSummaryColumn(summaryRow, "IncomeCol", "本月收入", font);
    }

    /// <summary>
    /// 合计列：小标签 + 大号金额（支出左列黑、收入右列黑；符号语义在流水行体现）
    /// </summary>
    private static TextMeshProUGUI BuildSummaryColumn(RectTransform summaryRow, string columnName, string label, TMP_FontAsset font)
    {
        int half = columnName == "ExpenseCol" ? 0 : 1;

        RectTransform column = CreateRect(summaryRow, columnName, new Vector2(half, 0f), new Vector2(half + 0.5f, 1f), new Vector2(0.5f, 0.5f));
        column.offsetMin = Vector2.zero;
        column.offsetMax = Vector2.zero;

        CreateStretchLabelWithPosition(column, "Label", label, font, 26f, ColLabelOnYellow, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(300f, 46f), new Vector2(0f, -6f));

        TextMeshProUGUI value = CreateAnchoredLabel(column, "Value", "0.00", font, 54f, ColBlack,
            TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(440f, 90f), new Vector2(0f, 14f));
        value.fontStyle = FontStyles.Bold;

        return value;
    }

    /// <summary>
    /// 分组列表：ScrollView（顶栏 300 以下到底，弹性吸收 16:9~21:9 高度差）
    /// Content = 平铺 组头行 + 记录行（单层 VerticalLayoutGroup，无嵌套 Fitter）
    /// </summary>
    private static void BuildListScrollView(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        GameObject scrollView = CreateRect(content, "ListScrollView", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f)).gameObject;
        RectTransform scrollRectT = scrollView.GetComponent<RectTransform>();
        scrollRectT.offsetMin = Vector2.zero;
        scrollRectT.offsetMax = new Vector2(0f, -300f);   // TopBar 底边

        RectTransform viewport = CreateRect(scrollView.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform listContent = CreateRect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        listContent.offsetMin = Vector2.zero;
        listContent.offsetMax = Vector2.zero;

        // 透明可点底：列表空白区域也能拖动滚动
        Image contentRaycast = listContent.gameObject.AddComponent<Image>();
        contentRaycast.color = new Color(0f, 0f, 0f, 0f);
        contentRaycast.raycastTarget = true;

        VerticalLayoutGroup layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 12, 40);
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.viewport = viewport;
        scrollRect.content = listContent;

        refs.ListContent = listContent;
    }

    /// <summary>
    /// 空态占位："这个月还没有记录"（列表区垂直居中偏上），默认隐藏由 DetailPanelUI 切换
    /// </summary>
    private static GameObject BuildEmptyLabel(RectTransform content, TMP_FontAsset font)
    {
        TextMeshProUGUI label = CreateAnchoredLabel(content, "EmptyLabel", "这个月还没有记录", font, 36f, ColPlaceholder,
            TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(700f, 80f), new Vector2(0f, -80f));

        label.gameObject.SetActive(false);
        return label.gameObject;
    }

    /// <summary>
    /// 确认弹窗：全屏遮罩（点遮罩=取消）+ 白底对话框（文案 + 取消/删除两按钮）
    /// </summary>
    private static ConfirmDialog BuildConfirmDialog(Transform canvas, TMP_FontAsset font)
    {
        GameObject dialog = CreateRect(canvas, "ConfirmDialog", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f)).gameObject;

        Image mask = dialog.AddComponent<Image>();
        mask.color = ColMask;
        mask.raycastTarget = true;
        Button maskButton = dialog.AddComponent<Button>();
        maskButton.targetGraphic = mask;

        RectTransform box = CreateRect(dialog.transform, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        box.sizeDelta = new Vector2(680f, 320f);
        Image boxImage = box.gameObject.AddComponent<Image>();
        boxImage.color = ColWhite;
        boxImage.raycastTarget = true;   // 点对话框空白不关闭（只有遮罩响应）

        TextMeshProUGUI message = CreateAnchoredLabel(box, "Message", "确认删除？", font, 36f, ColBlack,
            TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        RectTransform messageRect = message.GetComponent<RectTransform>();
        messageRect.offsetMin = new Vector2(44f, 120f);
        messageRect.offsetMax = new Vector2(-44f, -24f);

        RectTransform btnRow = CreateRect(box, "BtnRow", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        btnRow.sizeDelta = new Vector2(0f, 110f);

        Button btnCancel = CreateBoxButton(btnRow, "btnCancel", "取消", font, ColKey, ColBlack, new Vector2(-165f, 0f));
        Button btnConfirm = CreateBoxButton(btnRow, "btnConfirm", "删除", font, ColBlack, ColWhite, new Vector2(165f, 0f));

        ConfirmDialog component = dialog.AddComponent<ConfirmDialog>();
        SerializedObject so = new SerializedObject(component);
        SetRef(so, "messageLabel", message);
        SetRef(so, "confirmButton", btnConfirm);
        SetRef(so, "cancelButton", btnCancel);
        SetRef(so, "maskButton", maskButton);
        so.ApplyModifiedPropertiesWithoutUndo();

        dialog.SetActive(false);
        Debug.Log("[DetailBuild] 已搭建确认弹窗（Canvas 根，可复用）。");
        return component;
    }

    // ---------- 预制体 ----------

    /// <summary>
    /// 生成/刷新组头预制体：浅灰带 + 左"09月26日 星期六" + 右"支出: 28.8"，行高 80
    /// </summary>
    private static DayHeaderUI CreateDayHeaderPrefab(TMP_FontAsset font)
    {
        if (File.Exists(DayHeaderPrefabPath))
        {
            AssetDatabase.DeleteAsset(DayHeaderPrefabPath);
        }

        Directory.CreateDirectory("Assets/Prefabs");

        GameObject root = new GameObject("DayHeaderUI", typeof(RectTransform));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 80f);

        Image bg = root.AddComponent<Image>();
        bg.color = ColHeaderBand;
        bg.raycastTarget = false;

        TextMeshProUGUI date = CreateAnchoredLabel(root.transform, "DateLabel", "09月26日 星期六", font, 32f, ColGrayText,
            TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(560f, 80f), new Vector2(30f, 0f));

        TextMeshProUGUI expense = CreateAnchoredLabel(root.transform, "ExpenseLabel", "支出: 0", font, 30f, ColGray,
            TextAlignmentOptions.Right, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
            new Vector2(500f, 80f), new Vector2(-30f, 0f));

        DayHeaderUI item = root.AddComponent<DayHeaderUI>();
        SerializedObject so = new SerializedObject(item);
        SetRef(so, "dateLabel", date);
        SetRef(so, "expenseLabel", expense);
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, DayHeaderPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        Debug.Log("[DetailBuild] 已生成组头预制体：" + DayHeaderPrefabPath);
        return prefab != null ? prefab.GetComponent<DayHeaderUI>() : null;
    }

    /// <summary>
    /// 生成/刷新记录行预制体：白底可点 + 圆底图标 + 分类名（备注拼接）+ 右侧金额，行高 120
    /// </summary>
    private static RecordItemUI CreateRecordItemPrefab(TMP_FontAsset font, Sprite circleSprite)
    {
        if (File.Exists(RecordItemPrefabPath))
        {
            AssetDatabase.DeleteAsset(RecordItemPrefabPath);
        }

        Directory.CreateDirectory("Assets/Prefabs");

        GameObject root = new GameObject("RecordItemUI", typeof(RectTransform));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 120f);

        // 白底同时是点击 raycast 面（Pointer 接口判定点击/长按）
        Image background = root.AddComponent<Image>();
        background.color = ColWhite;
        background.raycastTarget = true;

        RectTransform divider = CreateRect(root.transform, "Divider", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        divider.sizeDelta = new Vector2(0f, 2f);
        Image dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.color = ColDivider;
        dividerImage.raycastTarget = false;

        RectTransform circle = CreateRect(root.transform, "IconCircle", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        circle.sizeDelta = new Vector2(84f, 84f);
        circle.anchoredPosition = new Vector2(40f, 0f);
        Image circleImage = circle.gameObject.AddComponent<Image>();
        circleImage.sprite = circleSprite;
        circleImage.color = ColKey;
        circleImage.raycastTarget = false;

        RectTransform icon = CreateRect(circle, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        icon.sizeDelta = new Vector2(52f, 52f);
        Image iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.raycastTarget = false;

        TextMeshProUGUI name = CreateAnchoredLabel(root.transform, "NameLabel", "分类", font, 34f, ColBlack,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 60f), new Vector2(144f, 0f));
        RectTransform nameRect = name.GetComponent<RectTransform>();
        nameRect.offsetMin = new Vector2(144f, -60f);
        nameRect.offsetMax = new Vector2(-280f, 60f);
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Truncate;

        TextMeshProUGUI amount = CreateAnchoredLabel(root.transform, "AmountLabel", "-0.00", font, 38f, ColBlack,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(240f, 120f), new Vector2(-30f, 0f));

        RecordItemUI item = root.AddComponent<RecordItemUI>();
        SerializedObject so = new SerializedObject(item);
        SetRef(so, "backgroundImage", background);
        SetRef(so, "iconCircleImage", circleImage);
        SetRef(so, "iconImage", iconImage);
        SetRef(so, "nameLabel", name);
        SetRef(so, "amountLabel", amount);
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RecordItemPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        Debug.Log("[DetailBuild] 已生成记录行预制体：" + RecordItemPrefabPath);
        return prefab != null ? prefab.GetComponent<RecordItemUI>() : null;
    }

    // ---------- UI 工具 ----------

    /// <summary>
    /// 纯文字按钮（透明底，用于 ‹ › 翻月）
    /// </summary>
    private static Button CreateTextButton(Transform parent, string name, string label, TMP_FontAsset font,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition, float fontSize)
    {
        RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, pivot);
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        CreateStretchLabel(rect, "Label", label, font, fontSize, ColBlack, TextAlignmentOptions.Center);
        return button;
    }

    /// <summary>
    /// 底色块按钮（确认弹窗取消/删除）
    /// </summary>
    private static Button CreateBoxButton(Transform parent, string name, string label, TMP_FontAsset font,
        Color bgColor, Color textColor, Vector2 anchoredPosition)
    {
        RectTransform rect = CreateRect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        rect.sizeDelta = new Vector2(280f, 90f);
        rect.anchoredPosition = anchoredPosition;

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = bgColor;
        image.raycastTarget = true;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        CreateStretchLabel(rect, "Label", label, font, 36f, textColor, TextAlignmentOptions.Center);
        return button;
    }

    /// <summary>
    /// 撑满父节点的文本
    /// </summary>
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
        tmp.fontStyle = FontStyles.Normal;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>
    /// 指定锚点位置的文本（合计列小标签等）
    /// </summary>
    private static TextMeshProUGUI CreateStretchLabelWithPosition(Transform parent, string name, string text, TMP_FontAsset font, float fontSize, Color color,
        TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition)
    {
        return CreateAnchoredLabel(parent, name, text, font, fontSize, color, alignment, anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition);
    }

    /// <summary>
    /// 按锚点/尺寸/位置创建文本
    /// </summary>
    private static TextMeshProUGUI CreateAnchoredLabel(Transform parent, string name, string text, TMP_FontAsset font, float fontSize, Color color,
        TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPosition)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Normal;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
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

    // ---------- 序列化工具 ----------

    private static void SetRef(SerializedObject so, string fieldName, UnityEngine.Object value)
    {
        so.FindProperty(fieldName).objectReferenceValue = value;
    }

    private static void SetEnumField(UnityEngine.Object target, string fieldName, Enum value)
    {
        SerializedObject so = new SerializedObject(target);
        so.FindProperty(fieldName).enumValueIndex = Convert.ToInt32(value);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Color FromHex(int hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Color(r, g, b, 1f);
    }

    // ---------- 自检 ----------

    /// <summary>
    /// 接线自检：逐项核对 DetailPanelUI 序列化引用、预制体、ConfirmDialog 内部引用、UIManager 反向引用
    /// </summary>
    private static bool VerifyWiring(PanelRefs refs, DetailPanelUI panelUi, UIManager uiManager)
    {
        bool allPass = true;
        SerializedObject so = new SerializedObject(panelUi);

        string[] fields =
        {
            "accountManager", "iconProvider", "recordPanelUI", "uiManager",
            "btnPrevMonth", "btnNextMonth", "txtYearMonth", "txtIncome", "txtExpense",
            "listContent", "dayHeaderPrefab", "recordItemPrefab", "emptyLabel", "confirmDialog"
        };

        for (int i = 0; i < fields.Length; i++)
        {
            allPass &= CheckRef(so, fields[i]);
        }

        allPass &= Check("明细预制体非空", refs.DayHeaderPrefab != null && refs.RecordItemPrefab != null);

        ConfirmDialog dialog = refs.ConfirmBox;
        SerializedObject soDialog = dialog != null ? new SerializedObject(dialog) : null;
        bool dialogOk = soDialog != null &&
            soDialog.FindProperty("messageLabel").objectReferenceValue != null &&
            soDialog.FindProperty("confirmButton").objectReferenceValue != null &&
            soDialog.FindProperty("cancelButton").objectReferenceValue != null &&
            soDialog.FindProperty("maskButton").objectReferenceValue != null;
        allPass &= Check("ConfirmDialog 内部引用完整", dialogOk);

        GameObject uiDetailPanel = new SerializedObject(uiManager).FindProperty("detailPanel").objectReferenceValue as GameObject;
        allPass &= Check("UIManager.detailPanel 指向新面板", uiDetailPanel == refs.Panel);

        return allPass;
    }

    private static bool CheckRef(SerializedObject so, string fieldName)
    {
        SerializedProperty prop = so.FindProperty(fieldName);
        bool ok = prop != null && prop.objectReferenceValue != null;
        return Check("引用已接线：" + fieldName, ok);
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[DetailBuild] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
