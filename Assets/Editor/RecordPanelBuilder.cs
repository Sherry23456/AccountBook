using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 02 一键搭建记账页：重建 RecordPanel（页签 / 分类宫格 ScrollView / 金额备注行 / 4×4 键盘）、
/// 生成 CategoryGridItem 预制体、接线 RecordPanelUI / UIManager / AppFlowManager，并做引用自检。
/// 幂等可重跑；依赖步骤 01 的场景、Dynamic 字体与圆形 sprite。菜单：AccountBook/04-搭建记账页。
/// </summary>
public static class RecordPanelBuilder
{
    private const string MenuItemPath = "AccountBook/04-搭建记账页";
    private const string AutoRunSessionKey = "AccountBook.RecordPanelBuilder.SessionRan4";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string PrefabPath = "Assets/Prefabs/CategoryGridItem.prefab";
    private const string CircleSpritePath = "Assets/Art/Generated/circle.png";
    private const string FontAssetPath = "Assets/Fonts/STKAITI Dynamic SDF.asset";

    // 色板（步骤00 §2.4，与 MainSceneBuilder 一致）
    private static readonly Color ColYellow = FromHex(0xFFD100);
    private static readonly Color ColBlack = FromHex(0x222222);
    private static readonly Color ColGray = FromHex(0x666666);
    private static readonly Color ColDivider = FromHex(0xEEEEEE);
    private static readonly Color ColWhite = FromHex(0xFFFFFF);
    private static readonly Color ColKey = FromHex(0xF2F2F2);
    private static readonly Color ColPlaceholder = FromHex(0x999999);
    private static readonly Color ColMask = new Color(0f, 0f, 0f, 0.55f);

    /// <summary>
    /// 搭建产物引用包（面板内部结构与对外接线各字段的临时载体）
    /// </summary>
    private class PanelRefs
    {
        public GameObject Panel;
        public Button TabExpense;
        public Button TabIncome;
        public Button BtnClose;
        public RectTransform GridContent;
        public TextMeshProUGUI AmountText;
        public TMP_InputField NoteInput;
        public Button[] Digits = new Button[10];
        public Button Dot;
        public Button Plus;
        public Button Minus;
        public Button Today;
        public Button Backspace;
        public Button Done;
        public GameObject DatePickerPanel;
    }

    /// <summary>
    /// 编译后自动执行一次（每编辑器会话一次，步骤01 同款模式）：
    /// 搭建 → 接线自检 → 登记 Play 流程自检与截图标记 → 自动进入记账流程自检。
    /// </summary>
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

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            try
            {
                Build();
                SessionState.SetBool("AccountBook.RecordShot.Pending", true);
                RecordFlowCheck.Run();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RecordBuild] 自动搭建失败：{ex}\n可用菜单 {MenuItemPath} 重跑。");
            }
        };
    }

    [MenuItem(MenuItemPath)]
    private static void RunFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[RecordBuild] 编辑器正在 Play 模式，退出后再搭建。");
            return;
        }

        try
        {
            Build();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RecordBuild] 搭建失败：{ex}\n请确认步骤 01 已完成（菜单 AccountBook/01-搭建主场景 Main）。");
        }
    }

    /// <summary>
    /// 主入口：预制体 → 重建面板 → 接线 → 存场景 → 自检
    /// </summary>
    private static void Build()
    {
        Debug.Log("[RecordBuild] ===== 步骤02 记账页搭建开始 =====");

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

        CategoryGridItem itemPrefab = CreateCategoryItemPrefab(font, circleSprite);

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject canvasGo = GameObject.Find("Canvas");

        if (canvasGo == null)
        {
            throw new InvalidOperationException("场景中找不到 Canvas，请先运行步骤 01。");
        }

        Transform canvas = canvasGo.transform;

        // 保留原绘制顺序（明细 < 图表 < 记账 < 导出 < 导入 < 底栏），底栏必须压在记账页上面
        Transform oldPanel = canvas.Find("RecordPanel");
        int siblingIndex = oldPanel != null ? oldPanel.GetSiblingIndex() : 2;

        if (oldPanel != null)
        {
            UnityEngine.Object.DestroyImmediate(oldPanel.gameObject);
        }

        PanelRefs refs = BuildRecordPanel(canvas, font, circleSprite);
        refs.Panel.transform.SetSiblingIndex(siblingIndex);
        refs.Panel.SetActive(false);

        // —— 接线 RecordPanelUI ——
        RecordPanelUI panelUi = refs.Panel.GetComponent<RecordPanelUI>();
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        CategoryIconProvider iconProvider = UnityEngine.Object.FindFirstObjectByType<CategoryIconProvider>();
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
        AppFlowManager flow = UnityEngine.Object.FindFirstObjectByType<AppFlowManager>();

        SerializedObject so = new SerializedObject(panelUi);
        SetRef(so, "accountManager", accountManager);
        SetRef(so, "iconProvider", iconProvider);
        SetRef(so, "uiManager", uiManager);
        SetRef(so, "tabExpenseButton", refs.TabExpense);
        SetRef(so, "tabIncomeButton", refs.TabIncome);
        SetRef(so, "gridContent", refs.GridContent);
        SetRef(so, "categoryItemPrefab", itemPrefab);
        SetRef(so, "amountText", refs.AmountText);
        SetRef(so, "noteInput", refs.NoteInput);

        SerializedProperty digitsProp = so.FindProperty("digitButtons");
        digitsProp.arraySize = 10;

        for (int i = 0; i < 10; i++)
        {
            digitsProp.GetArrayElementAtIndex(i).objectReferenceValue = refs.Digits[i];
        }

        SetRef(so, "dotButton", refs.Dot);
        SetRef(so, "plusButton", refs.Plus);
        SetRef(so, "minusButton", refs.Minus);
        SetRef(so, "todayButton", refs.Today);
        SetRef(so, "backspaceButton", refs.Backspace);
        SetRef(so, "doneButton", refs.Done);
        SetRef(so, "closeButton", refs.BtnClose);
        SetRef(so, "datePickerPanel", refs.DatePickerPanel);
        so.ApplyModifiedPropertiesWithoutUndo();

        // —— 重接 UIManager.recordPanel（旧面板已销毁）与 AppFlowManager.recordPanelUI ——
        SerializedObject soUi = new SerializedObject(uiManager);
        soUi.FindProperty("recordPanel").objectReferenceValue = refs.Panel;
        soUi.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject soFlow = new SerializedObject(flow);
        soFlow.FindProperty("recordPanelUI").objectReferenceValue = panelUi;
        soFlow.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        PrePopulateFontGlyphs(font);

        bool pass = VerifyWiring(refs, panelUi, uiManager, flow);
        Debug.Log($"[RecordBuild] ===== 步骤02 记账页搭建完成，接线自检 {(pass ? "通过" : "失败(见上方 [RecordBuild] FAIL 日志)")} =====");
    }

    /// <summary>
    /// 预固化全部界面用字进 Dynamic 图集并落盘：真机冷启动首帧即可正确渲染，
    /// 避免动态字形首次入库延迟导致的开屏方块（用户备注里的新字仍走运行时动态添加）。
    /// </summary>
    private static void PrePopulateFontGlyphs(TMP_FontAsset font)
    {
        System.Text.StringBuilder chars = new System.Text.StringBuilder();

        for (int i = 0; i < CategoryTable.All.Count; i++)
        {
            chars.Append(CategoryTable.All[i].Name);
        }

        chars.Append("支出收入取消今天退格完成备注：点击填写备分明细图表记账元一二三四五六日回月年0123456789.+-");

        bool ok = font.TryAddCharacters(chars.ToString());

        if (ok)
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            Debug.Log("[RecordBuild] 已预固化界面用字进 Dynamic 图集（" + chars.Length + " 字）。");
        }
        else
        {
            Debug.LogWarning("[RecordBuild] 部分界面用字未能预固化（运行时动态添加仍会兜底）。");
        }
    }

    // ---------- 面板搭建 ----------

    /// <summary>
    /// RecordPanel 全量重建：白底全屏 + 内容容器（底部让出底栏 170，底栏覆盖在上层可点，同录屏）
    /// </summary>
    private static PanelRefs BuildRecordPanel(Transform canvas, TMP_FontAsset font, Sprite circleSprite)
    {
        PanelRefs refs = new PanelRefs();

        GameObject panel = new GameObject("RecordPanel", typeof(RectTransform));
        panel.transform.SetParent(canvas, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = ColWhite;
        panelBg.raycastTarget = false;
        panel.AddComponent<RecordPanelUI>();

        RectTransform content = CreateRect(panel.transform, "PanelContent", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        content.offsetMin = new Vector2(0f, 170f);
        content.offsetMax = Vector2.zero;

        BuildTopBar(content, font, refs);
        BuildCategoryScrollView(content, refs);
        BuildAmountRow(content, font, refs);
        BuildKeyboard(content, font, refs);
        refs.DatePickerPanel = BuildDatePickerPanel(panel.transform, font);

        refs.Panel = panel;
        return refs;
    }

    /// <summary>
    /// 顶栏：主黄底 + 支出/收入页签（黑块高亮选中）+ 右上「取消」
    /// </summary>
    private static void BuildTopBar(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform topBar = CreateRect(content, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        topBar.sizeDelta = new Vector2(0f, 240f);
        Image topBarImage = topBar.gameObject.AddComponent<Image>();
        topBarImage.color = ColYellow;
        topBarImage.raycastTarget = false;

        // 黄底沉浸到状态栏后面，交互内容整体抬进安全区（步骤00 §2.4）
        RectTransform topContent = CreateRect(topBar.transform, "TopContent", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        topContent.offsetMin = Vector2.zero;
        topContent.offsetMax = Vector2.zero;
        SafeAreaFitter topFitter = topContent.gameObject.AddComponent<SafeAreaFitter>();
        SetEnumField(topFitter, "mode", SafeAreaFitter.Mode.Top);

        RectTransform tabGroup = CreateRect(topContent, "TabGroup", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        tabGroup.sizeDelta = new Vector2(440f, 104f);
        tabGroup.anchoredPosition = new Vector2(40f, 0f);

        refs.TabExpense = CreateTabButton(tabGroup, "Tab_Expense", "支出", font, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
        refs.TabIncome = CreateTabButton(tabGroup, "Tab_Income", "收入", font, new Vector2(0.5f, 0f), new Vector2(1f, 1f));

        refs.BtnClose = CreateTextButton(topContent, "btnClose", "取消", font,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(160f, 104f), new Vector2(-30f, -20f), 40f);
    }

    /// <summary>
    /// 分类宫格：ScrollView（16:9~21:9 高度差全靠它弹性吸收，步骤00 §2.4）+ 4 列宫格
    /// </summary>
    private static void BuildCategoryScrollView(RectTransform content, PanelRefs refs)
    {
        GameObject scrollView = CreateRect(content, "CategoryScrollView", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f)).gameObject;
        RectTransform scrollRectT = scrollView.GetComponent<RectTransform>();
        scrollRectT.offsetMin = new Vector2(0f, 940f);    // AmountRow 顶边（720 + 220）
        scrollRectT.offsetMax = new Vector2(0f, -240f);   // TopBar 底边

        RectTransform viewport = CreateRect(scrollView.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();

        // Content：锚顶拉伸 + ContentSizeFitter，随宫格行数向下生长
        RectTransform gridContent = CreateRect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        gridContent.offsetMin = Vector2.zero;
        gridContent.offsetMax = Vector2.zero;

        // 透明可点底：宫格空白区域也能拖动滚动
        Image contentRaycast = gridContent.gameObject.AddComponent<Image>();
        contentRaycast.color = new Color(0f, 0f, 0f, 0f);
        contentRaycast.raycastTarget = true;

        GridLayoutGroup grid = gridContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(30, 30, 24, 24);
        grid.spacing = new Vector2(15f, 24f);
        grid.cellSize = new Vector2(243f, 230f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment = TextAnchor.UpperLeft;

        ContentSizeFitter fitter = gridContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.viewport = viewport;
        scrollRect.content = gridContent;

        refs.GridContent = gridContent;
    }

    /// <summary>
    /// 金额行：右对齐大号金额 + 备注输入框（放键盘上方，防真机系统键盘遮挡，步骤02 §4）
    /// </summary>
    private static void BuildAmountRow(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform amountRow = CreateRect(content, "AmountRow", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        amountRow.sizeDelta = new Vector2(0f, 220f);
        amountRow.anchoredPosition = new Vector2(0f, 720f);

        refs.AmountText = CreateAnchoredLabel(amountRow, "AmountText", "0.00", font, 96f, ColBlack,
            TextAlignmentOptions.Right, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 130f), Vector2.zero);
        refs.AmountText.margin = new Vector4(0f, 0f, 44f, 0f);

        refs.NoteInput = CreateNoteInput(amountRow, font);
    }

    /// <summary>
    /// 备注输入框：标准 TMP_InputField 结构（背景 + Text Area 遮罩 + Placeholder + 输入文本），单行限 50 字
    /// </summary>
    private static TMP_InputField CreateNoteInput(Transform parent, TMP_FontAsset font)
    {
        GameObject go = new GameObject("NoteInput", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(40f, 14f);
        rect.offsetMax = new Vector2(-40f, 98f);

        Image bg = go.AddComponent<Image>();
        bg.color = ColKey;
        bg.raycastTarget = true;

        TMP_InputField input = go.AddComponent<TMP_InputField>();

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform));
        textArea.transform.SetParent(go.transform, false);
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(20f, 6f);
        textAreaRect.offsetMax = new Vector2(-20f, -6f);
        textArea.AddComponent<RectMask2D>();

        TextMeshProUGUI placeholder = CreateStretchLabel(textArea.transform, "Placeholder", "备注：点击填写备注", font, 34f, ColPlaceholder, TextAlignmentOptions.Left);
        placeholder.raycastTarget = true;

        TextMeshProUGUI text = CreateStretchLabel(textArea.transform, "Text", "", font, 34f, ColBlack, TextAlignmentOptions.Left);
        text.raycastTarget = true;

        input.textViewport = textAreaRect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 50;

        return input;
    }

    /// <summary>
    /// 键盘：4×4 网格 7 8 9 今天 / 4 5 6 + / 1 2 3 - / . 0 退格 完成（黄块）
    /// </summary>
    private static void BuildKeyboard(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform keyboard = CreateRect(content, "Keyboard", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        keyboard.sizeDelta = new Vector2(0f, 720f);
        Image keyboardBg = keyboard.gameObject.AddComponent<Image>();
        keyboardBg.color = ColWhite;
        keyboardBg.raycastTarget = false;

        RectTransform keyDivider = CreateRect(keyboard, "Divider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        keyDivider.sizeDelta = new Vector2(0f, 2f);
        keyDivider.anchoredPosition = new Vector2(0f, -1f);
        Image keyDividerImage = keyDivider.gameObject.AddComponent<Image>();
        keyDividerImage.color = ColDivider;
        keyDividerImage.raycastTarget = false;

        RectTransform keyGrid = CreateRect(keyboard, "KeyGrid", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        keyGrid.offsetMin = Vector2.zero;
        keyGrid.offsetMax = Vector2.zero;
        GridLayoutGroup keyLayout = keyGrid.gameObject.AddComponent<GridLayoutGroup>();
        keyLayout.padding = new RectOffset(24, 24, 24, 24);
        keyLayout.spacing = new Vector2(16f, 16f);
        keyLayout.cellSize = new Vector2(246f, 168f);
        keyLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        keyLayout.constraintCount = 4;
        keyLayout.childAlignment = TextAnchor.UpperLeft;

        refs.Digits[7] = CreateKey(keyGrid, "Key_7", "7", font, 60f, ColKey);
        refs.Digits[8] = CreateKey(keyGrid, "Key_8", "8", font, 60f, ColKey);
        refs.Digits[9] = CreateKey(keyGrid, "Key_9", "9", font, 60f, ColKey);
        refs.Today = CreateKey(keyGrid, "Key_Today", "今天", font, 44f, ColKey);
        refs.Digits[4] = CreateKey(keyGrid, "Key_4", "4", font, 60f, ColKey);
        refs.Digits[5] = CreateKey(keyGrid, "Key_5", "5", font, 60f, ColKey);
        refs.Digits[6] = CreateKey(keyGrid, "Key_6", "6", font, 60f, ColKey);
        refs.Plus = CreateKey(keyGrid, "Key_Plus", "+", font, 60f, ColKey);
        refs.Digits[1] = CreateKey(keyGrid, "Key_1", "1", font, 60f, ColKey);
        refs.Digits[2] = CreateKey(keyGrid, "Key_2", "2", font, 60f, ColKey);
        refs.Digits[3] = CreateKey(keyGrid, "Key_3", "3", font, 60f, ColKey);
        refs.Minus = CreateKey(keyGrid, "Key_Minus", "-", font, 60f, ColKey);
        refs.Dot = CreateKey(keyGrid, "Key_Dot", ".", font, 60f, ColKey);
        refs.Digits[0] = CreateKey(keyGrid, "Key_0", "0", font, 60f, ColKey);
        refs.Backspace = CreateKey(keyGrid, "Key_Backspace", "退格", font, 40f, ColKey);
        refs.Done = CreateKey(keyGrid, "Key_Done", "完成", font, 48f, ColYellow);
    }

    /// <summary>
    /// 日期选择弹窗（补记漏账）：全屏遮罩（点遮罩关闭）+ 白底月历对话框
    /// （‹ 2026年9月 › / 星期行周一为首 / 6×7 日格 / 回到今天），默认隐藏。
    /// </summary>
    private static GameObject BuildDatePickerPanel(Transform panelRoot, TMP_FontAsset font)
    {
        GameObject picker = CreateRect(panelRoot, "DatePickerPanel", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f)).gameObject;

        Image mask = picker.AddComponent<Image>();
        mask.color = ColMask;
        mask.raycastTarget = true;
        Button maskButton = picker.AddComponent<Button>();
        maskButton.targetGraphic = mask;

        RectTransform dialog = CreateRect(picker.transform, "Dialog", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        dialog.sizeDelta = new Vector2(840f, 920f);
        Image dialogImage = dialog.gameObject.AddComponent<Image>();
        dialogImage.color = ColWhite;
        dialogImage.raycastTarget = true;   // 点对话框空白不关闭（只有遮罩和日格响应）

        // 头部：‹ 2026年9月 ›
        RectTransform header = CreateRect(dialog, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        header.sizeDelta = new Vector2(0f, 120f);

        Button prevMonth = CreateTextButton(header, "btnPrevMonth", "<", font,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(140f, 110f), new Vector2(15f, 0f), 52f);

        TextMeshProUGUI title = CreateAnchoredLabel(header, "Title", "", font, 46f, ColBlack,
            TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.offsetMin = new Vector2(160f, 0f);
        titleRect.offsetMax = new Vector2(-160f, 0f);

        Button nextMonth = CreateTextButton(header, "btnNextMonth", ">", font,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(140f, 110f), new Vector2(-15f, 0f), 52f);

        // 星期行（周一为第一列，与 DatePickerPanel 的偏移算法一致）
        RectTransform weekdayRow = CreateRect(dialog, "WeekdayRow", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        weekdayRow.sizeDelta = new Vector2(0f, 70f);
        weekdayRow.anchoredPosition = new Vector2(0f, -120f);
        GridLayoutGroup weekdayLayout = weekdayRow.gameObject.AddComponent<GridLayoutGroup>();
        weekdayLayout.cellSize = new Vector2(120f, 70f);
        weekdayLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        weekdayLayout.constraintCount = 7;

        string[] weekdayNames = { "一", "二", "三", "四", "五", "六", "日" };

        for (int i = 0; i < weekdayNames.Length; i++)
        {
            CreateStretchLabel(weekdayRow, "W" + i, weekdayNames[i], font, 30f, ColPlaceholder, TextAlignmentOptions.Center);
        }

        // 日格 6×7（固定 42 格，运行时由 DatePickerPanel 刷新文字与颜色）
        RectTransform dayGrid = CreateRect(dialog, "DayGrid", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        dayGrid.sizeDelta = new Vector2(0f, 624f);
        dayGrid.anchoredPosition = new Vector2(0f, -190f);
        GridLayoutGroup dayLayout = dayGrid.gameObject.AddComponent<GridLayoutGroup>();
        dayLayout.padding = new RectOffset(0, 0, 24, 24);
        dayLayout.spacing = Vector2.zero;
        dayLayout.cellSize = new Vector2(120f, 96f);
        dayLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        dayLayout.constraintCount = 7;

        Button[] dayButtons = new Button[42];

        for (int i = 0; i < 42; i++)
        {
            dayButtons[i] = CreateKey(dayGrid, "DayCell_" + i, "", font, 34f, ColKey);
        }

        // 底部：回到今天（选中今天并立即确认）
        RectTransform footer = CreateRect(dialog, "Footer", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        footer.sizeDelta = new Vector2(0f, 100f);
        Button backToday = CreateTextButton(footer, "btnBackToday", "回到今天", font,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(280f, 90f), new Vector2(30f, 0f), 36f);

        DatePickerPanel pickerComponent = picker.AddComponent<DatePickerPanel>();
        SerializedObject soPicker = new SerializedObject(pickerComponent);
        SetRef(soPicker, "titleLabel", title);
        SetRef(soPicker, "prevMonthButton", prevMonth);
        SetRef(soPicker, "nextMonthButton", nextMonth);

        SerializedProperty dayProp = soPicker.FindProperty("dayButtons");
        dayProp.arraySize = 42;

        for (int i = 0; i < 42; i++)
        {
            dayProp.GetArrayElementAtIndex(i).objectReferenceValue = dayButtons[i];
        }

        SetRef(soPicker, "backTodayButton", backToday);
        SetRef(soPicker, "maskButton", maskButton);
        soPicker.ApplyModifiedPropertiesWithoutUndo();

        picker.SetActive(false);
        Debug.Log("[RecordBuild] 已搭建日期选择弹窗（迷你月历 42 格）。");
        return picker;
    }

    // ---------- 预制体 ----------

    /// <summary>
    /// 生成/刷新分类子项预制体：圆底(circle.png) + 图标位 + 名称；选中态由 RecordPanelUI 变色
    /// </summary>
    private static CategoryGridItem CreateCategoryItemPrefab(TMP_FontAsset font, Sprite circleSprite)
    {
        if (File.Exists(PrefabPath))
        {
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        Directory.CreateDirectory("Assets/Prefabs");

        GameObject root = new GameObject("CategoryGridItem", typeof(RectTransform));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(243f, 230f);

        RectTransform circle = CreateRect(root.transform, "Circle", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        circle.sizeDelta = new Vector2(132f, 132f);
        circle.anchoredPosition = new Vector2(0f, -10f);
        Image circleImage = circle.gameObject.AddComponent<Image>();
        circleImage.sprite = circleSprite;
        circleImage.color = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
        circleImage.raycastTarget = true;

        RectTransform icon = CreateRect(circle, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        icon.sizeDelta = new Vector2(84f, 84f);
        Image iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.raycastTarget = false;

        TextMeshProUGUI name = CreateAnchoredLabel(root.transform, "Name", "分类", font, 34f, ColGray,
            TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 56f), new Vector2(0f, 2f));

        Button button = root.AddComponent<Button>();
        button.targetGraphic = circleImage;

        CategoryGridItem item = root.AddComponent<CategoryGridItem>();
        SerializedObject so = new SerializedObject(item);
        SetRef(so, "circleImage", circleImage);
        SetRef(so, "iconImage", iconImage);
        SetRef(so, "nameLabel", name);
        SetRef(so, "clickButton", button);
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        Debug.Log("[RecordBuild] 已生成分类子项预制体：" + PrefabPath);
        return prefab != null ? prefab.GetComponent<CategoryGridItem>() : null;
    }

    // ---------- UI 工具 ----------

    /// <summary>
    /// 页签按钮：底图与顶栏同色，选中态由 RecordPanelUI 刷成黑块
    /// </summary>
    private static Button CreateTabButton(Transform parent, string name, string label, TMP_FontAsset font, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, new Vector2(0.5f, 0.5f));
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = ColYellow;
        image.raycastTarget = true;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        CreateStretchLabel(rect, "Label", label, font, 44f, ColBlack, TextAlignmentOptions.Center);
        return button;
    }

    /// <summary>
    /// 纯文字按钮（透明底，用于「取消」）
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
    /// 键盘按键：底色块 + 居中文字，尺寸交给 GridLayoutGroup
    /// </summary>
    private static Button CreateKey(Transform parent, string name, string label, TMP_FontAsset font, float fontSize, Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = bgColor;
        image.raycastTarget = true;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;

        CreateStretchLabel(go.transform, "Label", label, font, fontSize, ColBlack, TextAlignmentOptions.Center);
        return button;
    }

    /// <summary>
    /// 撑满父节点的文本（按钮/按键居中文字等）
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
    /// 按锚点/尺寸/位置创建文本（金额、宫格名称等）
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
    /// 接线自检：逐项核对 RecordPanelUI 序列化引用、分类表数量、UIManager/AppFlowManager 反向引用
    /// </summary>
    private static bool VerifyWiring(PanelRefs refs, RecordPanelUI panelUi, UIManager uiManager, AppFlowManager flow)
    {
        bool allPass = true;
        SerializedObject so = new SerializedObject(panelUi);

        allPass &= CheckRef(so, "accountManager");
        allPass &= CheckRef(so, "iconProvider");
        allPass &= CheckRef(so, "uiManager");
        allPass &= CheckRef(so, "tabExpenseButton");
        allPass &= CheckRef(so, "tabIncomeButton");
        allPass &= CheckRef(so, "gridContent");
        allPass &= CheckRef(so, "categoryItemPrefab");
        allPass &= CheckRef(so, "amountText");
        allPass &= CheckRef(so, "noteInput");
        allPass &= CheckRef(so, "dotButton");
        allPass &= CheckRef(so, "plusButton");
        allPass &= CheckRef(so, "minusButton");
        allPass &= CheckRef(so, "todayButton");
        allPass &= CheckRef(so, "backspaceButton");
        allPass &= CheckRef(so, "doneButton");
        allPass &= CheckRef(so, "closeButton");
        allPass &= CheckRef(so, "datePickerPanel");

        SerializedProperty digitsProp = so.FindProperty("digitButtons");
        bool digitsOk = digitsProp != null && digitsProp.arraySize == 10;

        if (digitsOk)
        {
            for (int i = 0; i < 10; i++)
            {
                if (digitsProp.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    digitsOk = false;
                }
            }
        }

        allPass &= Check("10 个数字键全部接线", digitsOk);

        allPass &= Check("支出分类 31 项", CategoryTable.GetByType((int)RecordType.Expense).Count == 31);
        allPass &= Check("收入分类 6 项", CategoryTable.GetByType((int)RecordType.Income).Count == 6);

        GameObject uiRecordPanel = new SerializedObject(uiManager).FindProperty("recordPanel").objectReferenceValue as GameObject;
        allPass &= Check("UIManager.recordPanel 指向新面板", uiRecordPanel == refs.Panel);

        RecordPanelUI flowPanel = new SerializedObject(flow).FindProperty("recordPanelUI").objectReferenceValue as RecordPanelUI;
        allPass &= Check("AppFlowManager.recordPanelUI 已接线", flowPanel == panelUi);

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
        Debug.Log($"[RecordBuild] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
