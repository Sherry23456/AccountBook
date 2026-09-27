using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 04 一键搭建图表页：重建 ChartPanel（黄顶栏 支出/收入+导入导出 / 周/月/年 Toggle 组 /
/// 周期条 ScrollView / 汇总行 / 折线图区 / 排行榜）、生成 RankItemUI / PeriodStripButton 预制体、
/// 接线 ChartPanelUI / UIManager 并做引用自检。幂等可重跑；依赖步骤 01 场景、Dynamic 字体与圆形 sprite。
/// 菜单：AccountBook/08-搭建图表页。
/// 自动链（每编辑器会话一次）：轮询等步骤 01/02/03 旧链（含明细页截图 Taken 标记）空闲后
/// 搭建 → 图表页流程自检 → 截图，结果全部落 Editor.log；等待超 300 秒告警放行防死锁。
/// </summary>
public static class ChartPanelBuilder
{
    private const string MenuItemPath = "AccountBook/08-搭建图表页";
    private const string AutoRunSessionKey = "AccountBook.ChartPanelBuilder.SessionRan";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string RankItemPrefabPath = "Assets/Prefabs/RankItemUI.prefab";
    private const string PeriodButtonPrefabPath = "Assets/Prefabs/PeriodStripButton.prefab";
    private const string CircleSpritePath = "Assets/Art/Generated/circle.png";
    private const string SquareSpritePath = "Assets/Art/Generated/square.png";
    private const string FontAssetPath = "Assets/Fonts/STKAITI Dynamic SDF.asset";
    private const double IdleWaitSeconds = 2.0;   // 旧链连续空闲该秒数才启动，避免多个 Play 会话互相打断
    private const double BusyTimeoutSeconds = 300.0;   // 旧链等太久（可能崩了没收尾）就放行，防死锁

    // 色板（步骤00 §2.4，与 MainSceneBuilder/DetailPanelBuilder 一致）
    private static readonly Color ColYellow = FromHex(0xFFD100);
    private static readonly Color ColBlack = FromHex(0x222222);
    private static readonly Color ColGray = FromHex(0x666666);
    private static readonly Color ColGrayText = FromHex(0x444444);
    private static readonly Color ColBackground = FromHex(0xF7F7F7);
    private static readonly Color ColWhite = FromHex(0xFFFFFF);
    private static readonly Color ColKey = FromHex(0xF2F2F2);
    private static readonly Color ColBarBg = FromHex(0xE8E8E8);
    private static readonly Color ColBubble = new Color(0.13f, 0.13f, 0.13f, 0.94f);

    /// <summary>
    /// 搭建产物引用包
    /// </summary>
    private class PanelRefs
    {
        public GameObject Panel;
        public Button BtnType;
        public TextMeshProUGUI BtnTypeLabel;
        public Button BtnExport;
        public Button BtnImport;
        public Toggle ToggleWeek;
        public Toggle ToggleMonth;
        public Toggle ToggleYear;
        public TextMeshProUGUI ToggleWeekLabel;
        public TextMeshProUGUI ToggleMonthLabel;
        public TextMeshProUGUI ToggleYearLabel;
        public Image ToggleWeekSelectedBg;
        public Image ToggleMonthSelectedBg;
        public Image ToggleYearSelectedBg;
        public RectTransform PeriodViewport;
        public RectTransform PeriodContent;
        public PeriodStripButton PeriodButtonPrefab;
        public TextMeshProUGUI TxtTotalCaption;
        public TextMeshProUGUI TxtTotal;
        public TextMeshProUGUI TxtAvg;
        public LineChartGraphic LineChart;
        public RectTransform XLabelsContainer;
        public TextMeshProUGUI TxtMax;
        public GameObject EmptyChartLabel;
        public RectTransform NodeBubble;
        public TextMeshProUGUI NodeBubbleText;
        public TextMeshProUGUI RankTitle;
        public RectTransform RankContent;
        public RankItemUI RankItemPrefab;
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
    /// 轮询等待旧链空闲（含明细页截图链完整收尾）后启动搭建+自检；久等告警放行
    /// </summary>
    private static void PumpIdle()
    {
        if (IsOtherChainsBusy())
        {
            if (busySince < 0.0)
            {
                busySince = EditorApplication.timeSinceStartup;
            }
            else if (EditorApplication.timeSinceStartup - busySince > BusyTimeoutSeconds)
            {
                Debug.LogWarning("[ChartBuild] 等待旧自检链超 300 秒，放弃等待直接搭建（旧链可能崩溃未收尾）。");
                EditorApplication.update -= PumpIdle;

                try
                {
                    StartBuildChain();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ChartBuild] 自动搭建失败：{ex}\n可用菜单 {MenuItemPath} 重跑。");
                }
            }

            idleSince = -1.0;
            return;
        }

        busySince = -1.0;

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
            StartBuildChain();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChartBuild] 自动搭建失败：{ex}\n可用菜单 {MenuItemPath} 重跑。");
        }
    }

    private static double idleSince = -1.0;
    private static double busySince = -1.0;

    private static void StartBuildChain()
    {
        Build();
        SessionState.SetBool("AccountBook.ChartShot.Pending", true);
        ChartFlowCheck.Run();
    }

    /// <summary>
    /// 步骤 01/02/03 旧链是否在途：Play 会话中、自检未收尾、或截图标记挂起/未完成
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

        // 明细链：自检未收尾、截图挂起，或本会话截图尚未完成（ Taken 保证步骤03 全链跑完）
        if (SessionState.GetBool("AccountBook.DetailCheck.Started", false) &&
            SessionState.GetBool("AccountBook.DetailCheck.Done", false) == false)
        {
            return true;
        }

        if (SessionState.GetBool("AccountBook.DetailShot.Pending", false) ||
            SessionState.GetBool("AccountBook.DetailShot.Armed", false))
        {
            return true;
        }

        if (SessionState.GetBool("AccountBook.DetailShot.Taken", false) == false)
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
            Debug.LogError("[ChartBuild] 编辑器正在 Play 模式，退出后再搭建。");
            return;
        }

        try
        {
            Build();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChartBuild] 搭建失败：{ex}\n请确认步骤 01/02/03 已完成（菜单 AccountBook/01、04、06）。");
        }
    }

    /// <summary>
    /// 主入口：预制体 → 重建面板 → 接线 → 存场景 → 预固化字形 → 自检
    /// </summary>
    private static void Build()
    {
        Debug.Log("[ChartBuild] ===== 步骤04 图表页搭建开始 =====");

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

        Sprite squareSprite = EnsureSquareSprite();

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject canvasGo = GameObject.Find("Canvas");

        if (canvasGo == null)
        {
            throw new InvalidOperationException("场景中找不到 Canvas，请先运行步骤 01。");
        }

        Transform canvas = canvasGo.transform;

        // 预制体在开场景之后创建：OpenScene 会触发资源重导入，先建后开会让本地引用变假 null（步骤03 首轮踩过）
        RankItemUI rankItemPrefab = CreateRankItemPrefab(font, circleSprite, squareSprite);
        PeriodStripButton periodButtonPrefab = CreatePeriodButtonPrefab(font);

        // 保留原绘制顺序（明细 < 图表 < 记账 < 导出 < 导入 < 底栏）
        Transform oldPanel = canvas.Find("ChartPanel");
        int siblingIndex = oldPanel != null ? oldPanel.GetSiblingIndex() : 1;

        if (oldPanel != null)
        {
            UnityEngine.Object.DestroyImmediate(oldPanel.gameObject);
        }

        PanelRefs refs = BuildChartPanel(canvas, font, circleSprite, rankItemPrefab, periodButtonPrefab);
        refs.Panel.transform.SetSiblingIndex(siblingIndex);
        refs.Panel.SetActive(false);   // 明细是默认页，图表页默认隐藏

        // —— 接线 ChartPanelUI ——
        ChartPanelUI panelUi = refs.Panel.GetComponent<ChartPanelUI>();
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>();
        CategoryIconProvider iconProvider = UnityEngine.Object.FindFirstObjectByType<CategoryIconProvider>();
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();

        SerializedObject so = new SerializedObject(panelUi);
        SetRef(so, "accountManager", accountManager);
        SetRef(so, "iconProvider", iconProvider);
        SetRef(so, "labelFont", font);
        SetRef(so, "btnType", refs.BtnType);
        SetRef(so, "btnTypeLabel", refs.BtnTypeLabel);
        SetRef(so, "btnExport", refs.BtnExport);
        SetRef(so, "btnImport", refs.BtnImport);
        SetRef(so, "toggleWeek", refs.ToggleWeek);
        SetRef(so, "toggleMonth", refs.ToggleMonth);
        SetRef(so, "toggleYear", refs.ToggleYear);
        SetRef(so, "toggleWeekLabel", refs.ToggleWeekLabel);
        SetRef(so, "toggleMonthLabel", refs.ToggleMonthLabel);
        SetRef(so, "toggleYearLabel", refs.ToggleYearLabel);
        SetRef(so, "toggleWeekSelectedBg", refs.ToggleWeekSelectedBg);
        SetRef(so, "toggleMonthSelectedBg", refs.ToggleMonthSelectedBg);
        SetRef(so, "toggleYearSelectedBg", refs.ToggleYearSelectedBg);
        SetRef(so, "periodViewport", refs.PeriodViewport);
        SetRef(so, "periodContent", refs.PeriodContent);
        SetRef(so, "periodButtonPrefab", refs.PeriodButtonPrefab);
        SetRef(so, "txtTotalCaption", refs.TxtTotalCaption);
        SetRef(so, "txtTotal", refs.TxtTotal);
        SetRef(so, "txtAvg", refs.TxtAvg);
        SetRef(so, "lineChart", refs.LineChart);
        SetRef(so, "xLabelsContainer", refs.XLabelsContainer);
        SetRef(so, "txtMax", refs.TxtMax);
        SetRef(so, "emptyChartLabel", refs.EmptyChartLabel);
        SetRef(so, "nodeBubble", refs.NodeBubble);
        SetRef(so, "nodeBubbleText", refs.NodeBubbleText);
        SetRef(so, "rankTitle", refs.RankTitle);
        SetRef(so, "rankContent", refs.RankContent);
        SetRef(so, "rankItemPrefab", refs.RankItemPrefab);
        so.ApplyModifiedPropertiesWithoutUndo();

        // —— 重接 UIManager.chartPanel（旧面板已销毁） ——
        SerializedObject soUi = new SerializedObject(uiManager);
        soUi.FindProperty("chartPanel").objectReferenceValue = refs.Panel;
        soUi.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        PrePopulateFontGlyphs(font);

        bool pass = VerifyWiring(refs, panelUi, uiManager);
        Debug.Log($"[ChartBuild] ===== 步骤04 图表页搭建完成，接线自检 {(pass ? "通过" : "失败(见上方 [ChartBuild] FAIL 日志)")} =====");
    }

    /// <summary>
    /// 预固化图表页界面用字进 Dynamic 图集并落盘（冷启动防方块，步骤02/03 同款）
    /// </summary>
    private static void PrePopulateFontGlyphs(TMP_FontAsset font)
    {
        System.Text.StringBuilder chars = new System.Text.StringBuilder();

        for (int i = 0; i < CategoryTable.All.Count; i++)
        {
            chars.Append(CategoryTable.All[i].Name);
        }

        chars.Append("支出收入周月年总平均值最高没有费用排行榜导入导出数据本期一二三四五六七八九十月日年");
        chars.Append("▼最大笔交易当日合计：｜%0123456789.-/ ");

        bool ok = font.TryAddCharacters(chars.ToString());

        if (ok)
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            Debug.Log("[ChartBuild] 已预固化图表页界面用字进 Dynamic 图集（" + chars.Length + " 字）。");
        }
        else
        {
            Debug.LogWarning("[ChartBuild] 部分界面用字未能预固化（运行时动态添加仍会兜底）。");
        }
    }

    // ---------- 面板搭建 ----------

    /// <summary>
    /// ChartPanel 全量重建：灰白底全屏 + 内容容器（底部让出底栏 170）
    /// 纵向堆叠：TopBar 300 / PeriodStrip 104 / SummaryRow 150 / ChartArea 610 / RankTitle 70 / 排行滚动区
    /// </summary>
    private static PanelRefs BuildChartPanel(Transform canvas, TMP_FontAsset font, Sprite circleSprite,
        RankItemUI rankItemPrefab, PeriodStripButton periodButtonPrefab)
    {
        PanelRefs refs = new PanelRefs();
        refs.RankItemPrefab = rankItemPrefab;
        refs.PeriodButtonPrefab = periodButtonPrefab;

        GameObject panel = new GameObject("ChartPanel", typeof(RectTransform));
        panel.transform.SetParent(canvas, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = ColBackground;
        panelBg.raycastTarget = false;
        panel.AddComponent<ChartPanelUI>();

        RectTransform content = CreateRect(panel.transform, "PanelContent", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        content.offsetMin = new Vector2(0f, 170f);   // 底栏让位（同明细/记账页）
        content.offsetMax = Vector2.zero;

        BuildTopBar(content, font, refs);
        BuildPeriodStrip(content, font, refs);
        BuildSummaryRow(content, font, refs);
        BuildChartArea(content, font, refs);
        BuildRankArea(content, font, refs);

        refs.Panel = panel;
        return refs;
    }

    /// <summary>
    /// 顶栏：主黄底沉浸状态栏，两行——支出/收入两态按钮 + 导入导出（置灰）、周/月/年 Toggle 组
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

        // 第一行：左 支出/收入 两态按钮，右 导出/导入（置灰，步骤 05/06 接线）
        RectTransform row1 = CreateRect(topContent, "Row1", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        row1.sizeDelta = new Vector2(0f, 110f);
        row1.anchoredPosition = new Vector2(0f, -8f);

        refs.BtnType = CreateColorButton(row1, "btnType", "支出 ▼", font,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(240f, 84f), new Vector2(30f, 0f), 36f, ColBlack, ColWhite);
        refs.BtnTypeLabel = refs.BtnType.GetComponentInChildren<TextMeshProUGUI>();

        refs.BtnImport = CreateColorButton(row1, "btnImport", "导入数据", font,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(210f, 72f), new Vector2(-30f, 0f), 30f, ColKey, ColGray);
        refs.BtnExport = CreateColorButton(row1, "btnExport", "导出数据", font,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(210f, 72f), new Vector2(-260f, 0f), 30f, ColKey, ColGray);
        refs.BtnExport.interactable = false;   // 步骤 05 接线
        refs.BtnImport.interactable = false;   // 步骤 06 接线

        // 第二行：周/月/年 Toggle 组（选中块变黑，同录屏）
        RectTransform row2 = CreateRect(topContent, "Row2", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        row2.sizeDelta = new Vector2(0f, 140f);

        RectTransform segment = CreateRect(row2, "Segmented", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        segment.sizeDelta = new Vector2(640f, 92f);
        ToggleGroup group = segment.gameObject.AddComponent<ToggleGroup>();

        refs.ToggleWeek = CreateSegmentToggle(segment, "toggleWeek", "周", font, group, -213f, true, refs, "Week");
        refs.ToggleMonth = CreateSegmentToggle(segment, "toggleMonth", "月", font, group, 0f, false, refs, "Month");
        refs.ToggleYear = CreateSegmentToggle(segment, "toggleYear", "年", font, group, 213f, false, refs, "Year");
    }

    /// <summary>
    /// 档位 Toggle：透明底收点击 + 黑选中块（toggle.graphic）+ 文字，选中态文字白色由运行时同步
    /// </summary>
    private static Toggle CreateSegmentToggle(RectTransform segment, string name, string label, TMP_FontAsset font,
        ToggleGroup group, float x, bool isOn, PanelRefs refs, string refPrefix)
    {
        RectTransform rect = CreateRect(segment, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        rect.sizeDelta = new Vector2(200f, 88f);
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
    /// 周期条：横向 ScrollView，运行时重建 6 个周期按钮（左旧右新，本期在最右并滚到可视区）
    /// </summary>
    private static void BuildPeriodStrip(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform strip = CreateRect(content, "PeriodStrip", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        strip.sizeDelta = new Vector2(0f, 104f);
        strip.anchoredPosition = new Vector2(0f, -308f);

        RectTransform viewport = CreateRect(strip.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform stripContent = CreateRect(viewport, "StripContent",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));

        HorizontalLayoutGroup layout = stripContent.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 0, 0);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = stripContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = strip.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = true;
        scrollRect.vertical = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.viewport = viewport;
        scrollRect.content = stripContent;

        refs.PeriodViewport = viewport;
        refs.PeriodContent = stripContent;
    }

    /// <summary>
    /// 汇总行：左 总支出/总收入（随类型换文案），右 平均值
    /// </summary>
    private static void BuildSummaryRow(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform row = CreateRect(content, "SummaryRow", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        row.sizeDelta = new Vector2(0f, 150f);
        row.anchoredPosition = new Vector2(0f, -412f);

        refs.TxtTotalCaption = BuildSummaryColumn(row, "TotalCol", "总支出", font, true, out refs.TxtTotal);
        BuildSummaryColumn(row, "AvgCol", "平均值", font, false, out refs.TxtAvg);
    }

    /// <summary>
    /// 汇总列：小标签 + 大号金额（左列标签随类型换文案需要接线，右列静态）
    /// </summary>
    private static TextMeshProUGUI BuildSummaryColumn(RectTransform row, string columnName, string label,
        TMP_FontAsset font, bool wireLabel, out TextMeshProUGUI value)
    {
        int half = columnName == "TotalCol" ? 0 : 1;

        // 右列锚点必须是 0.5~1（写成 1~1.5 会整体跑出屏幕外，步骤04 验收截图发现）
        RectTransform column = CreateRect(row, columnName, new Vector2(0.5f * half, 0f), new Vector2(0.5f * half + 0.5f, 1f), new Vector2(0.5f, 0.5f));
        column.offsetMin = Vector2.zero;
        column.offsetMax = Vector2.zero;

        TextMeshProUGUI caption = CreateAnchoredLabel(column, "Label", label, font, 26f, ColGray,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(300f, 46f), new Vector2(0f, -6f));

        value = CreateAnchoredLabel(column, "Value", "0.00", font, 54f, ColBlack,
            TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(440f, 90f), new Vector2(0f, 14f));
        value.fontStyle = FontStyles.Bold;

        return wireLabel ? caption : null;
    }

    /// <summary>
    /// 折线图区：自绘 LineChart（pivot 左下）+ X 轴标签容器（同坐标系）+ 最高值 + 空态气泡 + 节点气泡
    /// </summary>
    private static void BuildChartArea(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform area = CreateRect(content, "ChartArea", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        area.sizeDelta = new Vector2(0f, 610f);
        area.anchoredPosition = new Vector2(0f, -562f);

        // 折线图本体：pivot 左下，X 轴标签与气泡共用这套坐标（步骤04 §4）
        // new GameObject 的 RectTransform 默认 sizeDelta=(100,100)，拉伸锚点下必须显式清零，
        // 否则图表比父级宽/高各多 100，最后一个点被顶出屏幕（步骤04 首轮实测踩坑）
        RectTransform chart = CreateRect(area, "LineChart", Vector2.zero, Vector2.one, Vector2.zero);
        chart.offsetMin = Vector2.zero;
        chart.offsetMax = Vector2.zero;

        // 自定义 Graphic 子类不继承 Image 的 RequireComponent(CanvasRenderer)，
        // 缺它整张图静默不渲染（步骤04 首轮实测踩坑），这里显式补上
        if (chart.gameObject.GetComponent<CanvasRenderer>() == null)
        {
            chart.gameObject.AddComponent<CanvasRenderer>();
        }

        LineChartGraphic graphic = chart.gameObject.AddComponent<LineChartGraphic>();
        graphic.color = Color.white;
        graphic.raycastTarget = true;
        refs.LineChart = graphic;

        RectTransform labels = CreateRect(area, "XLabels", Vector2.zero, Vector2.one, Vector2.zero);
        labels.offsetMin = Vector2.zero;   // 同上：清掉默认 (100,100)
        labels.offsetMax = Vector2.zero;
        refs.XLabelsContainer = labels;

        refs.TxtMax = CreateAnchoredLabel(area, "txtMax", "最高 0", font, 32f, ColGrayText,
            TextAlignmentOptions.Right, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(400f, 60f), new Vector2(-40f, -18f));

        // 空态气泡："没有费用"（黑底白字小胶囊，录屏同款）
        RectTransform emptyChip = CreateRect(area, "EmptyChartLabel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        emptyChip.sizeDelta = new Vector2(320f, 84f);
        emptyChip.anchoredPosition = new Vector2(0f, 20f);
        Image emptyBg = emptyChip.gameObject.AddComponent<Image>();
        emptyBg.color = ColBlack;
        emptyBg.raycastTarget = false;
        CreateStretchLabel(emptyChip, "Text", "没有费用", font, 34f, ColWhite, TextAlignmentOptions.Center);
        refs.EmptyChartLabel = emptyChip.gameObject;

        // 节点气泡：黑底白字一行式，点击节点时定位弹出
        RectTransform bubble = CreateRect(area, "NodeBubble", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        bubble.sizeDelta = new Vector2(1000f, 88f);
        bubble.anchoredPosition = new Vector2(0f, 300f);
        Image bubbleBg = bubble.gameObject.AddComponent<Image>();
        bubbleBg.color = ColBubble;
        bubbleBg.raycastTarget = false;

        TextMeshProUGUI bubbleText = CreateStretchLabel(bubble, "Text", "", font, 28f, ColWhite, TextAlignmentOptions.Center);
        bubble.gameObject.SetActive(false);
        refs.NodeBubble = bubble;
        refs.NodeBubbleText = bubbleText;
    }

    /// <summary>
    /// 排行榜：标题（支出排行榜/收入排行榜，随类型换）+ 垂直滚动区（RankItemUI 平铺，最多 6 行不滚动）
    /// </summary>
    private static void BuildRankArea(RectTransform content, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform titleRow = CreateRect(content, "RankTitleRow", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        titleRow.sizeDelta = new Vector2(0f, 70f);
        titleRow.anchoredPosition = new Vector2(0f, -1172f);

        TextMeshProUGUI title = CreateAnchoredLabel(titleRow, "Text", "支出排行榜", font, 34f, ColBlack,
            TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(-60f, 0f), new Vector2(30f, 0f));
        title.fontStyle = FontStyles.Bold;
        refs.RankTitle = title;

        GameObject scrollView = CreateRect(content, "RankScrollView", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f)).gameObject;
        RectTransform scrollRectT = scrollView.GetComponent<RectTransform>();
        scrollRectT.offsetMin = Vector2.zero;
        scrollRectT.offsetMax = new Vector2(0f, -1242f);   // RankTitle 底边

        RectTransform viewport = CreateRect(scrollView.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform rankContent = CreateRect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        rankContent.offsetMin = Vector2.zero;
        rankContent.offsetMax = Vector2.zero;

        // 透明可点底：排行区空白处也能拖动滚动
        Image contentRaycast = rankContent.gameObject.AddComponent<Image>();
        contentRaycast.color = new Color(0f, 0f, 0f, 0f);
        contentRaycast.raycastTarget = true;

        VerticalLayoutGroup layout = rankContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 12, 40);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = rankContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;
        scrollRect.viewport = viewport;
        scrollRect.content = rankContent;

        refs.RankContent = rankContent;
    }

    // ---------- 预制体 ----------

    /// <summary>
    /// 生成/加载纯白方块 sprite：排行条 Filled 渲染必须有 sprite——UGUI Image 无 sprite 时
    /// OnPopulateMesh 直接走 Simple 全幅渲染，fillAmount 会被无视（步骤04 首轮实测踩坑）
    /// </summary>
    private static Sprite EnsureSquareSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(SquareSpritePath);

        if (existing != null)
        {
            return existing;
        }

        Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[64];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.white;
        }

        texture.SetPixels(pixels);
        texture.Apply();

        Directory.CreateDirectory("Assets/Art/Generated");
        File.WriteAllBytes(SquareSpritePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(SquareSpritePath);
        TextureImporter importer = AssetImporter.GetAtPath(SquareSpritePath) as TextureImporter;

        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        Debug.Log("[ChartBuild] 已生成方块 sprite：" + SquareSpritePath);
        return AssetDatabase.LoadAssetAtPath<Sprite>(SquareSpritePath);
    }

    /// <summary>
    /// 生成/刷新排行行预制体：白底 + 圆底图标 + 分类名 + 占比 + 灰底条上黄填充条（Filled 从右往左填）+ 右侧金额，行高 96
    /// </summary>
    private static RankItemUI CreateRankItemPrefab(TMP_FontAsset font, Sprite circleSprite, Sprite squareSprite)
    {
        if (File.Exists(RankItemPrefabPath))
        {
            AssetDatabase.DeleteAsset(RankItemPrefabPath);
        }

        Directory.CreateDirectory("Assets/Prefabs");

        GameObject root = new GameObject("RankItemUI", typeof(RectTransform));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1080f, 96f);

        Image background = root.AddComponent<Image>();
        background.color = ColWhite;
        background.raycastTarget = false;

        RectTransform circle = CreateRect(root.transform, "IconCircle", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        circle.sizeDelta = new Vector2(64f, 64f);
        circle.anchoredPosition = new Vector2(44f, 0f);
        Image circleImage = circle.gameObject.AddComponent<Image>();
        circleImage.sprite = circleSprite;
        circleImage.color = ColKey;
        circleImage.raycastTarget = false;

        RectTransform icon = CreateRect(circle, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        icon.sizeDelta = new Vector2(44f, 44f);
        Image iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.raycastTarget = false;

        TextMeshProUGUI name = CreateAnchoredLabel(root.transform, "NameLabel", "分类", font, 32f, ColBlack,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(170f, 96f), new Vector2(100f, 0f));
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Truncate;

        TextMeshProUGUI percent = CreateAnchoredLabel(root.transform, "PercentLabel", "0%", font, 28f, ColGray,
            TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(110f, 96f), new Vector2(280f, 0f));

        RectTransform barBg = CreateRect(root.transform, "BarBg", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        barBg.sizeDelta = new Vector2(440f, 18f);
        barBg.anchoredPosition = new Vector2(400f, 0f);
        Image barBgImage = barBg.gameObject.AddComponent<Image>();
        barBgImage.sprite = squareSprite;
        barBgImage.color = ColBarBg;
        barBgImage.raycastTarget = false;

        RectTransform barFill = CreateRect(barBg, "BarFill", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        barFill.offsetMin = Vector2.zero;
        barFill.offsetMax = Vector2.zero;
        Image barFillImage = barFill.gameObject.AddComponent<Image>();
        barFillImage.sprite = squareSprite;
        barFillImage.color = ColYellow;
        barFillImage.type = Image.Type.Filled;                 // 必须 Filled 才有 fillAmount（步骤04 §4）
        barFillImage.fillMethod = Image.FillMethod.Horizontal;
        barFillImage.fillOrigin = (int)Image.OriginHorizontal.Right;   // 从右往左填，同录屏
        barFillImage.fillAmount = 0f;
        barFillImage.raycastTarget = false;

        TextMeshProUGUI amount = CreateAnchoredLabel(root.transform, "AmountLabel", "0", font, 34f, ColBlack,
            TextAlignmentOptions.Right, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(190f, 96f), new Vector2(-30f, 0f));

        RankItemUI item = root.AddComponent<RankItemUI>();
        SerializedObject so = new SerializedObject(item);
        SetRef(so, "iconCircleImage", circleImage);
        SetRef(so, "iconImage", iconImage);
        SetRef(so, "nameLabel", name);
        SetRef(so, "percentLabel", percent);
        SetRef(so, "barBackgroundImage", barBgImage);
        SetRef(so, "barFillImage", barFillImage);
        SetRef(so, "amountLabel", amount);
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RankItemPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        Debug.Log("[ChartBuild] 已生成排行行预制体：" + RankItemPrefabPath);
        return prefab != null ? prefab.GetComponent<RankItemUI>() : null;
    }

    /// <summary>
    /// 生成/刷新周期条按钮预制体：230×72 白底黑字，选中态由 PeriodStripButton.Setup 换色
    /// </summary>
    private static PeriodStripButton CreatePeriodButtonPrefab(TMP_FontAsset font)
    {
        if (File.Exists(PeriodButtonPrefabPath))
        {
            AssetDatabase.DeleteAsset(PeriodButtonPrefabPath);
        }

        Directory.CreateDirectory("Assets/Prefabs");

        GameObject root = new GameObject("PeriodStripButton", typeof(RectTransform));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(230f, 72f);

        Image background = root.AddComponent<Image>();
        background.color = ColWhite;
        background.raycastTarget = true;

        Button button = root.AddComponent<Button>();
        button.targetGraphic = background;

        TextMeshProUGUI label = CreateStretchLabel(root.transform, "Label", "9.21-9.27", font, 30f, ColBlack, TextAlignmentOptions.Center);

        PeriodStripButton item = root.AddComponent<PeriodStripButton>();
        SerializedObject so = new SerializedObject(item);
        SetRef(so, "backgroundImage", background);
        SetRef(so, "label", label);
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PeriodButtonPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        Debug.Log("[ChartBuild] 已生成周期条按钮预制体：" + PeriodButtonPrefabPath);
        return prefab != null ? prefab.GetComponent<PeriodStripButton>() : null;
    }

    // ---------- UI 工具 ----------

    /// <summary>
    /// 底色块按钮（支出/收入两态、导入导出）
    /// </summary>
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
    /// 接线自检：逐项核对 ChartPanelUI 序列化引用、预制体、置灰按钮、UIManager 反向引用
    /// </summary>
    private static bool VerifyWiring(PanelRefs refs, ChartPanelUI panelUi, UIManager uiManager)
    {
        bool allPass = true;
        SerializedObject so = new SerializedObject(panelUi);

        string[] fields =
        {
            "accountManager", "iconProvider", "labelFont",
            "btnType", "btnTypeLabel", "btnExport", "btnImport",
            "toggleWeek", "toggleMonth", "toggleYear",
            "toggleWeekLabel", "toggleMonthLabel", "toggleYearLabel",
            "toggleWeekSelectedBg", "toggleMonthSelectedBg", "toggleYearSelectedBg",
            "periodViewport", "periodContent", "periodButtonPrefab",
            "txtTotalCaption", "txtTotal", "txtAvg",
            "lineChart", "xLabelsContainer", "txtMax", "emptyChartLabel", "nodeBubble", "nodeBubbleText",
            "rankTitle", "rankContent", "rankItemPrefab"
        };

        for (int i = 0; i < fields.Length; i++)
        {
            allPass &= CheckRef(so, fields[i]);
        }

        allPass &= Check("图表预制体非空", refs.RankItemPrefab != null && refs.PeriodButtonPrefab != null);
        allPass &= Check("导入导出按钮已置灰", refs.BtnExport != null && refs.BtnImport != null &&
            refs.BtnExport.interactable == false && refs.BtnImport.interactable == false);
        allPass &= Check("默认选中周档位", refs.ToggleWeek != null && refs.ToggleWeek.isOn &&
            refs.ToggleMonth != null && refs.ToggleMonth.isOn == false &&
            refs.ToggleYear != null && refs.ToggleYear.isOn == false);

        GameObject uiChartPanel = new SerializedObject(uiManager).FindProperty("chartPanel").objectReferenceValue as GameObject;
        allPass &= Check("UIManager.chartPanel 指向新面板", uiChartPanel == refs.Panel);

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
        Debug.Log($"[ChartBuild] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
