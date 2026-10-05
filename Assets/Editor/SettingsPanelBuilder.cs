using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 步骤08c 设置/发现页搭建（替代 14 号顶栏圆钮方案）：
/// 1) 底栏重建为五栏：明细 | 发现 | 记账(正中黄圆) | 图表 | 设置，重建后回填 AppFlowManager 五个按钮引用；
/// 2) 新增 DiscoverPanel（占位空白页，后续开发）与 SettingsPanel（列表样式：音效 开/关、导出数据、导入数据），
///    回填 UIManager 两个面板引用；
/// 3) 删除顶栏 SfxToggleStrip 旧圆钮；
/// 4) Canvas 层级重排：主面板 → 底栏 → Toast → ConfirmDialog；
/// 5) 存场景 → 预固化字形 → 自检。可重复执行（先删旧再建）。
/// </summary>
public static class SettingsPanelBuilder
{
    private const string MenuItemPath = "AccountBook/15-搭建设置与发现页";
    private const string CircleSpritePath = "Assets/Art/Generated/circle.png";
    private const string FontAssetPath = "Assets/Fonts/STKAITI Dynamic SDF.asset";
    private const string NavIconDir = "Assets/Art/icon";

    /// <summary>底栏四页签图标（明细=三角 发现=X 图表=圆 设置=方），Normal 灰描边 / Selected 黑填充</summary>
    private static readonly string[] NavIconNames =
    {
        "nav_detail_normal", "nav_detail_selected",
        "nav_discover_normal", "nav_discover_selected",
        "nav_chart_normal", "nav_chart_selected",
        "nav_settings_normal", "nav_settings_selected",
    };

    private static readonly Color ColYellow = FromHex(0xFFD100);
    private static readonly Color ColBlack = FromHex(0x222222);
    private static readonly Color ColGray = FromHex(0x666666);
    private static readonly Color ColDivider = FromHex(0xEEEEEE);
    private static readonly Color ColBackground = FromHex(0xF7F7F7);
    private static readonly Color ColWhite = FromHex(0xFFFFFF);

    private const float TopBarHeight = 460f;
    private const float NavHeight = 170f;
    private const float RowHeight = 130f;
    private const float RowMargin = 48f;
    private const float RecordCircleSize = 140f;
    private const float NavIconSize = 96f;

    [MenuItem(MenuItemPath)]
    private static void BuildFromMenu()
    {
        try
        {
            Build();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SettingsBuild] 搭建失败：{ex.Message}\n{ex.StackTrace}\n可用菜单 {MenuItemPath} 重跑。");
        }
    }

    private static void Build()
    {
        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("[SettingsBuild] FAIL - 场景中没有 Canvas，请先跑 AccountBook/01-搭建主场景 Main。");
            return;
        }

        EnsureNavIconImport();

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        Sprite circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        AppFlowManager appFlow = UnityEngine.Object.FindFirstObjectByType<AppFlowManager>(FindObjectsInactive.Include);

        if (font == null || circleSprite == null || uiManager == null || appFlow == null)
        {
            Debug.LogError("[SettingsBuild] FAIL - 缺少前置依赖（字体/圆素材/UIManager/AppFlowManager）。");
            return;
        }

        Transform canvasTf = canvas.transform;

        // —— 清旧：旧圆钮、旧底栏、旧两面板（可重跑） ——
        DestroyChild(canvasTf, "SfxToggleStrip");
        DestroyChild(canvasTf, "BottomNav");
        DestroyChild(canvasTf, "SettingsPanel");
        DestroyChild(canvasTf, "DiscoverPanel");

        // —— 清旧：图表页导出/导入旧入口（08c 已迁至设置页，重跑 08/10/12 会重建，这里兜底删掉） ——
        Transform chartRow1 = canvasTf.Find("ChartPanel/PanelContent/TopBar/TopContent/Row1");

        if (chartRow1 != null)
        {
            DestroyChild(chartRow1, "btnExport");
            DestroyChild(chartRow1, "btnImport");
        }

        // —— 1) 五栏底栏（四页签 Normal/Selected 图标） ——
        BuildBottomNav(canvasTf, font, circleSprite, out Button btnDetail, out Button btnRecord,
            out Button btnChart, out Button btnDiscover, out Button btnSettings, out BottomNavUI bottomNav);

        SerializedObject flowSo = new SerializedObject(appFlow);
        flowSo.FindProperty("btnDetail").objectReferenceValue = btnDetail;
        flowSo.FindProperty("btnRecord").objectReferenceValue = btnRecord;
        flowSo.FindProperty("btnChart").objectReferenceValue = btnChart;
        flowSo.FindProperty("btnDiscover").objectReferenceValue = btnDiscover;
        flowSo.FindProperty("btnSettings").objectReferenceValue = btnSettings;
        flowSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(appFlow);

        // —— 2) 发现占位页 ——
        GameObject discoverPanel = BuildMainShell(canvasTf, "DiscoverPanel", "发现", font);
        discoverPanel.SetActive(false);

        // —— 3) 设置页（列表样式） ——
        GameObject settingsPanel = BuildMainShell(canvasTf, "SettingsPanel", "设置", font);
        settingsPanel.SetActive(false);
        Transform settingsContent = settingsPanel.transform.Find("PanelContent");

        GameObject rowSound = CreateSettingsRow(settingsContent, "RowSound", "音效", -TopBarHeight, font, "开", ColBlack, out TextMeshProUGUI soundValue);
        GameObject rowExport = CreateSettingsRow(settingsContent, "RowExport", "导出数据", -TopBarHeight - (RowHeight + 2f), font, "＞", ColGray, out _);
        GameObject rowImport = CreateSettingsRow(settingsContent, "RowImport", "导入数据", -TopBarHeight - (RowHeight + 2f) * 2f, font, "＞", ColGray, out _);
        CreateDivider(settingsContent, "Divider1", -TopBarHeight - RowHeight);
        CreateDivider(settingsContent, "Divider2", -TopBarHeight - RowHeight * 2f - 2f);

        SfxToggleUI sfxToggle = rowSound.AddComponent<SfxToggleUI>();
        SerializedObject toggleSo = new SerializedObject(sfxToggle);
        toggleSo.FindProperty("valueLabel").objectReferenceValue = soundValue;
        toggleSo.ApplyModifiedPropertiesWithoutUndo();

        SettingsPanelUI settingsUi = settingsPanel.AddComponent<SettingsPanelUI>();
        SerializedObject settingsSo = new SerializedObject(settingsUi);
        settingsSo.FindProperty("uiManager").objectReferenceValue = uiManager;
        settingsSo.FindProperty("exportButton").objectReferenceValue = rowExport.GetComponent<Button>();
        settingsSo.FindProperty("importButton").objectReferenceValue = rowImport.GetComponent<Button>();
        settingsSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settingsUi);

        // —— 4) UIManager 回填两个新面板引用 + 底栏图标态组件 ——
        SerializedObject uiSo = new SerializedObject(uiManager);
        uiSo.FindProperty("discoverPanel").objectReferenceValue = discoverPanel;
        uiSo.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        uiSo.FindProperty("bottomNav").objectReferenceValue = bottomNav;
        uiSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(uiManager);

        // —— 5) Canvas 层级：主面板（含发现/设置）→ 导出/导入弹窗（必须盖住所有主页面）→ 底栏 → Toast → ConfirmDialog ——
        string[] order = { "DetailPanel", "ChartPanel", "RecordPanel", "DiscoverPanel", "SettingsPanel",
                           "ExportPanel", "ImportPanel", "BottomNav", "Toast", "ConfirmDialog" };
        int sibling = 0;

        foreach (string name in order)
        {
            Transform t = canvasTf.Find(name);

            if (t != null)
            {
                t.SetSiblingIndex(sibling++);
            }
        }

        // —— 场景落盘前复位显隐：主面板回明细、弹窗全关（流程自检/手动测试会把编辑态页面切乱） ——
        SetActiveIfExists(canvasTf, "DetailPanel", true);
        SetActiveIfExists(canvasTf, "ChartPanel", false);
        SetActiveIfExists(canvasTf, "RecordPanel", false);
        SetActiveIfExists(canvasTf, "DiscoverPanel", false);
        SetActiveIfExists(canvasTf, "SettingsPanel", false);
        SetActiveIfExists(canvasTf, "ExportPanel", false);
        SetActiveIfExists(canvasTf, "ImportPanel", false);

        // 编辑态底栏同步回明细选中，落盘所见 = 运行初态（UIManager.Awake 兜底同一结果）
        bottomNav.SetTab(BottomNavUI.NavTab.Detail);

        UnityEngine.SceneManagement.Scene scene = canvas.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        PreSolidifyGlyphs(font);

        RunSelfCheck(canvasTf, appFlow, uiManager, circleSprite);
    }

    /// <summary>
    /// 五栏底栏：明细 | 发现 | 记账(正中) | 图表 | 设置（结构同 01 号 MainSceneBuilder，槽位改五等分）
    /// </summary>
    private static void BuildBottomNav(Transform canvasTf, TMP_FontAsset font, Sprite circleSprite,
        out Button btnDetail, out Button btnRecord, out Button btnChart, out Button btnDiscover, out Button btnSettings,
        out BottomNavUI bottomNav)
    {
        GameObject nav = CreateRect(canvasTf, "BottomNav",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f)).gameObject;
        RectTransform navRect = (RectTransform)nav.transform;
        navRect.sizeDelta = new Vector2(0f, NavHeight);

        Image navBg = nav.AddComponent<Image>();
        navBg.color = ColWhite;
        navBg.raycastTarget = false;

        RectTransform divider = CreateRect(nav.transform, "Divider",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        divider.sizeDelta = new Vector2(0f, 2f);
        divider.anchoredPosition = new Vector2(0f, -1f);
        Image dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.color = ColDivider;
        dividerImage.raycastTarget = false;

        RectTransform navContent = CreateRect(nav.transform, "NavContent",
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        navContent.offsetMin = Vector2.zero;
        navContent.offsetMax = Vector2.zero;
        SafeAreaFitter fitter = navContent.gameObject.AddComponent<SafeAreaFitter>();
        SetFitterMode(fitter, SafeAreaFitter.Mode.Bottom);

        Sprite detailNormal = LoadNavSprite("nav_detail_normal");
        Sprite detailSelected = LoadNavSprite("nav_detail_selected");
        Sprite discoverNormal = LoadNavSprite("nav_discover_normal");
        Sprite discoverSelected = LoadNavSprite("nav_discover_selected");
        Sprite chartNormal = LoadNavSprite("nav_chart_normal");
        Sprite chartSelected = LoadNavSprite("nav_chart_selected");
        Sprite settingsNormal = LoadNavSprite("nav_settings_normal");
        Sprite settingsSelected = LoadNavSprite("nav_settings_selected");

        btnDetail = CreateNavButton(navContent, "btnDetail", new Vector2(0f, 0f), new Vector2(0.2f, 1f), "明细", font, detailNormal);
        btnDiscover = CreateNavButton(navContent, "btnDiscover", new Vector2(0.2f, 0f), new Vector2(0.4f, 1f), "发现", font, discoverNormal);

        // 正中：大 + 记账（必须居中，五栏正中即 Canvas 中线）
        GameObject recordGo = CreateRect(navContent.transform, "btnRecord",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)).gameObject;
        RectTransform recordRect = (RectTransform)recordGo.transform;
        recordRect.sizeDelta = new Vector2(RecordCircleSize, RecordCircleSize);

        Image recordImage = recordGo.AddComponent<Image>();
        recordImage.sprite = circleSprite;
        recordImage.color = ColYellow;
        btnRecord = recordGo.AddComponent<Button>();
        btnRecord.targetGraphic = recordImage;

        CreateLabel(recordGo.transform, "Plus", "+", font, 72, ColBlack, TextAlignmentOptions.Center, new Vector2(0f, 14f));
        CreateLabel(recordGo.transform, "Label", "记账", font, 26, ColGray, TextAlignmentOptions.Center, new Vector2(0f, -38f));

        btnChart = CreateNavButton(navContent, "btnChart", new Vector2(0.6f, 0f), new Vector2(0.8f, 1f), "图表", font, chartNormal);
        btnSettings = CreateNavButton(navContent, "btnSettings", new Vector2(0.8f, 0f), new Vector2(1f, 1f), "设置", font, settingsNormal);

        // —— 底栏图标态组件接线（4×icon/label 指向按钮子节点，normal/selected 指向资产） ——
        bottomNav = nav.AddComponent<BottomNavUI>();
        SerializedObject navSo = new SerializedObject(bottomNav);
        WireNavItem(navSo, "detail", btnDetail.transform, detailNormal, detailSelected);
        WireNavItem(navSo, "discover", btnDiscover.transform, discoverNormal, discoverSelected);
        WireNavItem(navSo, "chart", btnChart.transform, chartNormal, chartSelected);
        WireNavItem(navSo, "settings", btnSettings.transform, settingsNormal, settingsSelected);
        navSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bottomNav);
    }

    /// <summary>
    /// BottomNavUI 单页签四字段接线：{prefix}Icon/{prefix}Label 取按钮子节点，{prefix}Normal/Selected 填资产
    /// </summary>
    private static void WireNavItem(SerializedObject navSo, string prefix, Transform buttonTf, Sprite normal, Sprite selected)
    {
        navSo.FindProperty(prefix + "Icon").objectReferenceValue = buttonTf.Find("Icon")?.GetComponent<Image>();
        navSo.FindProperty(prefix + "Label").objectReferenceValue = buttonTf.Find("Label")?.GetComponent<TextMeshProUGUI>();
        navSo.FindProperty(prefix + "Normal").objectReferenceValue = normal;
        navSo.FindProperty(prefix + "Selected").objectReferenceValue = selected;
    }

    /// <summary>
    /// 底栏图标资产加载（EnsureNavIconImport 已保证 Sprite/Single 导入设置）
    /// </summary>
    private static Sprite LoadNavSprite(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>($"{NavIconDir}/{name}.png");
    }

    /// <summary>
    /// 底栏图标导入设置兜底：Sprite 类型 + Single 模式（Unity6 新导入默认会落 Multiple）+ 上限 256
    /// </summary>
    private static void EnsureNavIconImport()
    {
        foreach (string name in NavIconNames)
        {
            string path = $"{NavIconDir}/{name}.png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                Debug.LogError($"[SettingsBuild] FAIL - 缺少底栏图标 {path}，请从完成版目录补齐 8 张。");
                continue;
            }

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            if (importer.maxTextureSize > 256)
            {
                importer.maxTextureSize = 256;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
                Debug.Log($"[SettingsBuild] 已修正图标导入设置：{name}（Sprite/Single/256）");
            }
        }
    }

    /// <summary>
    /// 主面板壳（与明细页同构）：ColBackground 底 + PanelContent(底 170 让位导航) + TopBar 460 黄条
    /// + TopContent(安全区 Top) + 顶锚标题行
    /// </summary>
    private static GameObject BuildMainShell(Transform canvasTf, string panelName, string title, TMP_FontAsset font)
    {
        GameObject panel = CreateStretchRect(canvasTf, panelName);
        Image bg = panel.AddComponent<Image>();
        bg.color = ColBackground;
        bg.raycastTarget = false;

        RectTransform content = CreateStretchRect(panel.transform, "PanelContent").GetComponent<RectTransform>();
        content.offsetMin = new Vector2(0f, NavHeight);
        content.offsetMax = Vector2.zero;

        RectTransform topBar = CreateRect(content, "TopBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        topBar.sizeDelta = new Vector2(0f, TopBarHeight);
        Image topBarImage = topBar.gameObject.AddComponent<Image>();
        topBarImage.color = ColYellow;
        topBarImage.raycastTarget = false;

        RectTransform topContent = CreateRect(topBar.transform, "TopContent",
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        topContent.offsetMin = Vector2.zero;
        topContent.offsetMax = Vector2.zero;
        SafeAreaFitter fitter = topContent.gameObject.AddComponent<SafeAreaFitter>();
        SetFitterMode(fitter, SafeAreaFitter.Mode.Top);

        RectTransform titleStrip = CreateRect(topContent, "Title",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        titleStrip.sizeDelta = new Vector2(0f, 110f);
        titleStrip.anchoredPosition = new Vector2(0f, -8f);

        TextMeshProUGUI titleLabel = titleStrip.gameObject.AddComponent<TextMeshProUGUI>();
        titleLabel.font = font;
        titleLabel.fontSize = 48f;
        titleLabel.alignment = TextAlignmentOptions.Center;
        titleLabel.color = ColBlack;
        titleLabel.text = title;
        titleLabel.raycastTarget = false;

        return panel;
    }

    /// <summary>
    /// 设置页列表行：白底整行按钮 + 左标签 + 右值（同明细列表的通栏白行样式）。
    /// 音效行右值为 开/关（SfxToggleUI 刷新），导出/导入行右值为 ＞ 跳转箭头。
    /// </summary>
    private static GameObject CreateSettingsRow(Transform parent, string rowName, string labelText, float topY, TMP_FontAsset font, string valueText, Color valueColor, out TextMeshProUGUI valueLabel)
    {
        GameObject row = CreateRect(parent, rowName,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f)).gameObject;
        RectTransform rowRect = (RectTransform)row.transform;
        rowRect.sizeDelta = new Vector2(0f, RowHeight);
        rowRect.anchoredPosition = new Vector2(0f, topY);

        Image bg = row.AddComponent<Image>();
        bg.color = ColWhite;
        bg.raycastTarget = true;

        Button button = row.AddComponent<Button>();
        button.targetGraphic = bg;

        TextMeshProUGUI label = CreateRowText(row.transform, "LabelText", labelText, font,
            RowMargin, 220f);
        label.alignment = TextAlignmentOptions.Left;

        valueLabel = CreateRowText(row.transform, "ValueText", valueText, font,
            220f, RowMargin);
        valueLabel.alignment = TextAlignmentOptions.Right;
        valueLabel.color = valueColor;

        return row;
    }

    /// <summary>
    /// 行内文字：通栏拉伸文本，leftInset/rightInset 为左右内边距（另一侧留 220 给对侧文字）
    /// </summary>
    private static TextMeshProUGUI CreateRowText(Transform parent, string name, string text, TMP_FontAsset font, float leftInset, float rightInset)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(leftInset, 0f);
        rect.offsetMax = new Vector2(-rightInset, 0f);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = 40f;
        tmp.color = ColBlack;
        tmp.text = text;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void CreateDivider(Transform parent, string name, float topY)
    {
        RectTransform divider = CreateRect(parent, name,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        divider.sizeDelta = new Vector2(0f, 2f);
        divider.anchoredPosition = new Vector2(0f, topY);
        Image image = divider.gameObject.AddComponent<Image>();
        image.color = ColDivider;
        image.raycastTarget = false;
    }

    /// <summary>
    /// 底栏两侧平铺按钮（透明可点区 + 图标 + 底部文字），图标初值 Normal，运行时由 BottomNavUI 切换
    /// </summary>
    private static Button CreateNavButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label, TMP_FontAsset font, Sprite iconSprite)
    {
        RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, new Vector2(0.5f, 0.5f));
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        RectTransform iconRect = CreateRect(rect, "Icon",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        iconRect.sizeDelta = new Vector2(NavIconSize, NavIconSize);
        iconRect.anchoredPosition = new Vector2(0f, -12f);
        Image iconImage = iconRect.gameObject.AddComponent<Image>();
        iconImage.sprite = iconSprite;
        iconImage.raycastTarget = false;

        CreateLabel(rect, "Label", label, font, 34, ColGray, TextAlignmentOptions.Center, new Vector2(0f, -52f));

        return button;
    }

    /// <summary>
    /// 预固化设置/发现页界面用字进 Dynamic 图集并落盘（冷启动防方块，步骤02-05 同款）
    /// </summary>
    private static void PreSolidifyGlyphs(TMP_FontAsset font)
    {
        if (font.TryAddCharacters("设置现发音效开关导出数据入>＞"))
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            Debug.Log("[SettingsBuild] 已预固化设置/发现页用字（" + "设置现发音效开关导出数据入>＞".Length + " 字）。");
        }
        else
        {
            Debug.LogWarning("[SettingsBuild] 部分字形未能预固化（运行时动态添加仍会兜底）。");
        }
    }

    private static void RunSelfCheck(Transform canvasTf, AppFlowManager appFlow, UIManager uiManager, Sprite circleSprite)
    {
        bool allPass = true;

        allPass &= Check("旧顶栏圆钮 SfxToggleStrip 已删除", canvasTf.Find("SfxToggleStrip") == null);

        Transform nav = canvasTf.Find("BottomNav/NavContent");
        allPass &= Check("五栏底栏已重建", nav != null
            && nav.Find("btnDetail") != null && nav.Find("btnDiscover") != null
            && nav.Find("btnRecord") != null && nav.Find("btnChart") != null && nav.Find("btnSettings") != null);

        RectTransform recordRect = nav != null ? nav.Find("btnRecord") as RectTransform : null;
        allPass &= Check("记账按钮锚定正中 (0.5,0.5)", recordRect != null
            && recordRect.anchorMin == new Vector2(0.5f, 0.5f) && recordRect.anchorMax == new Vector2(0.5f, 0.5f));
        Image recordImage = recordRect != null ? recordRect.GetComponent<Image>() : null;
        allPass &= Check("记账按钮黄圆素材", recordImage != null && recordImage.sprite == circleSprite);

        // —— 底栏图标（步骤10） ——
        allPass &= Check("八张底栏图标资产已导入且为 Single Sprite",
            LoadNavSprite("nav_detail_normal") != null && LoadNavSprite("nav_detail_selected") != null
            && LoadNavSprite("nav_discover_normal") != null && LoadNavSprite("nav_discover_selected") != null
            && LoadNavSprite("nav_chart_normal") != null && LoadNavSprite("nav_chart_selected") != null
            && LoadNavSprite("nav_settings_normal") != null && LoadNavSprite("nav_settings_selected") != null);

        Image detailIcon = nav != null ? nav.Find("btnDetail/Icon")?.GetComponent<Image>() : null;
        Image discoverIcon = nav != null ? nav.Find("btnDiscover/Icon")?.GetComponent<Image>() : null;
        Image chartIcon = nav != null ? nav.Find("btnChart/Icon")?.GetComponent<Image>() : null;
        Image settingsIcon = nav != null ? nav.Find("btnSettings/Icon")?.GetComponent<Image>() : null;
        allPass &= Check("四页签图标子节点存在，落盘态明细=Selected 其余=Normal（所见=运行初态）",
            detailIcon != null && detailIcon.sprite == LoadNavSprite("nav_detail_selected")
            && discoverIcon != null && discoverIcon.sprite == LoadNavSprite("nav_discover_normal")
            && chartIcon != null && chartIcon.sprite == LoadNavSprite("nav_chart_normal")
            && settingsIcon != null && settingsIcon.sprite == LoadNavSprite("nav_settings_normal"));

        BottomNavUI navUi = canvasTf.Find("BottomNav") != null ? canvasTf.Find("BottomNav").GetComponent<BottomNavUI>() : null;
        SerializedObject navSoCheck = navUi != null ? new SerializedObject(navUi) : null;
        allPass &= Check("BottomNavUI 图标态接线（icon/label/normal/selected ×4）", navSoCheck != null
            && navSoCheck.FindProperty("detailIcon").objectReferenceValue == detailIcon
            && navSoCheck.FindProperty("detailLabel").objectReferenceValue == (nav?.Find("btnDetail/Label")?.GetComponent<TextMeshProUGUI>())
            && navSoCheck.FindProperty("detailNormal").objectReferenceValue == LoadNavSprite("nav_detail_normal")
            && navSoCheck.FindProperty("detailSelected").objectReferenceValue == LoadNavSprite("nav_detail_selected")
            && navSoCheck.FindProperty("discoverIcon").objectReferenceValue == discoverIcon
            && navSoCheck.FindProperty("discoverLabel").objectReferenceValue == (nav?.Find("btnDiscover/Label")?.GetComponent<TextMeshProUGUI>())
            && navSoCheck.FindProperty("discoverNormal").objectReferenceValue == LoadNavSprite("nav_discover_normal")
            && navSoCheck.FindProperty("discoverSelected").objectReferenceValue == LoadNavSprite("nav_discover_selected")
            && navSoCheck.FindProperty("chartIcon").objectReferenceValue == chartIcon
            && navSoCheck.FindProperty("chartLabel").objectReferenceValue == (nav?.Find("btnChart/Label")?.GetComponent<TextMeshProUGUI>())
            && navSoCheck.FindProperty("chartNormal").objectReferenceValue == LoadNavSprite("nav_chart_normal")
            && navSoCheck.FindProperty("chartSelected").objectReferenceValue == LoadNavSprite("nav_chart_selected")
            && navSoCheck.FindProperty("settingsIcon").objectReferenceValue == settingsIcon
            && navSoCheck.FindProperty("settingsLabel").objectReferenceValue == (nav?.Find("btnSettings/Label")?.GetComponent<TextMeshProUGUI>())
            && navSoCheck.FindProperty("settingsNormal").objectReferenceValue == LoadNavSprite("nav_settings_normal")
            && navSoCheck.FindProperty("settingsSelected").objectReferenceValue == LoadNavSprite("nav_settings_selected"));

        allPass &= Check("UIManager.bottomNav 已接线", navUi != null
            && new SerializedObject(uiManager).FindProperty("bottomNav").objectReferenceValue == navUi);

        SerializedObject flowSo = new SerializedObject(appFlow);
        allPass &= Check("AppFlowManager 五个按钮引用已接线",
            flowSo.FindProperty("btnDetail").objectReferenceValue != null
            && flowSo.FindProperty("btnRecord").objectReferenceValue != null
            && flowSo.FindProperty("btnChart").objectReferenceValue != null
            && flowSo.FindProperty("btnDiscover").objectReferenceValue != null
            && flowSo.FindProperty("btnSettings").objectReferenceValue != null);

        GameObject settingsPanel = canvasTf.Find("SettingsPanel") != null ? canvasTf.Find("SettingsPanel").gameObject : null;
        GameObject discoverPanel = canvasTf.Find("DiscoverPanel") != null ? canvasTf.Find("DiscoverPanel").gameObject : null;
        allPass &= Check("设置/发现面板已搭且初始未激活", settingsPanel != null && discoverPanel != null
            && settingsPanel.activeSelf == false && discoverPanel.activeSelf == false);

        Transform rowSound = settingsPanel != null ? settingsPanel.transform.Find("PanelContent/RowSound") : null;
        Transform rowExport = settingsPanel != null ? settingsPanel.transform.Find("PanelContent/RowExport") : null;
        Transform rowImport = settingsPanel != null ? settingsPanel.transform.Find("PanelContent/RowImport") : null;
        allPass &= Check("设置页三行列表已搭", rowSound != null && rowExport != null && rowImport != null);

        TextMeshProUGUI soundLabel = rowSound != null ? rowSound.Find("LabelText")?.GetComponent<TextMeshProUGUI>() : null;
        TextMeshProUGUI soundValue = rowSound != null ? rowSound.Find("ValueText")?.GetComponent<TextMeshProUGUI>() : null;
        allPass &= Check("音效行文案 左=音效 右=开", soundLabel != null && soundLabel.text == "音效"
            && soundValue != null && soundValue.text == "开");

        SfxToggleUI sfxToggle = rowSound != null ? rowSound.GetComponent<SfxToggleUI>() : null;
        allPass &= Check("SfxToggleUI valueLabel 已接线", sfxToggle != null
            && new SerializedObject(sfxToggle).FindProperty("valueLabel").objectReferenceValue == soundValue);

        SettingsPanelUI settingsUi = settingsPanel != null ? settingsPanel.GetComponent<SettingsPanelUI>() : null;
        SerializedObject settingsSo = settingsUi != null ? new SerializedObject(settingsUi) : null;
        allPass &= Check("SettingsPanelUI 三引用已接线", settingsSo != null
            && settingsSo.FindProperty("uiManager").objectReferenceValue == uiManager
            && settingsSo.FindProperty("exportButton").objectReferenceValue == rowExport?.GetComponent<Button>()
            && settingsSo.FindProperty("importButton").objectReferenceValue == rowImport?.GetComponent<Button>());

        TextMeshProUGUI exportLabel = rowExport != null ? rowExport.Find("LabelText")?.GetComponent<TextMeshProUGUI>() : null;
        TextMeshProUGUI importLabel = rowImport != null ? rowImport.Find("LabelText")?.GetComponent<TextMeshProUGUI>() : null;
        allPass &= Check("导出/导入行文案", exportLabel != null && exportLabel.text == "导出数据"
            && importLabel != null && importLabel.text == "导入数据");

        TextMeshProUGUI exportValue = rowExport != null ? rowExport.Find("ValueText")?.GetComponent<TextMeshProUGUI>() : null;
        TextMeshProUGUI importValue = rowImport != null ? rowImport.Find("ValueText")?.GetComponent<TextMeshProUGUI>() : null;
        allPass &= Check("导出/导入行右值为 ＞ 跳转箭头", exportValue != null && exportValue.text == "＞"
            && importValue != null && importValue.text == "＞");

        Transform chartRow1Check = canvasTf.Find("ChartPanel/PanelContent/TopBar/TopContent/Row1");
        allPass &= Check("图表页旧导出/导入按钮已删除", chartRow1Check == null
            || (chartRow1Check.Find("btnExport") == null && chartRow1Check.Find("btnImport") == null));

        SerializedObject uiSo = new SerializedObject(uiManager);
        allPass &= Check("UIManager 发现/设置面板引用已接线",
            uiSo.FindProperty("discoverPanel").objectReferenceValue == discoverPanel
            && uiSo.FindProperty("settingsPanel").objectReferenceValue == settingsPanel);

        int settingsIdx = canvasTf.Find("SettingsPanel") != null ? canvasTf.Find("SettingsPanel").GetSiblingIndex() : -1;
        int exportIdx = canvasTf.Find("ExportPanel") != null ? canvasTf.Find("ExportPanel").GetSiblingIndex() : int.MaxValue;
        int importIdx = canvasTf.Find("ImportPanel") != null ? canvasTf.Find("ImportPanel").GetSiblingIndex() : int.MaxValue;
        int navIdx = canvasTf.Find("BottomNav") != null ? canvasTf.Find("BottomNav").GetSiblingIndex() : -1;
        int toastIdx = canvasTf.Find("Toast") != null ? canvasTf.Find("Toast").GetSiblingIndex() : int.MaxValue;
        int confirmIdx = canvasTf.Find("ConfirmDialog") != null ? canvasTf.Find("ConfirmDialog").GetSiblingIndex() : int.MaxValue;
        allPass &= Check("层级顺序 主面板(含设置)<导出/导入弹窗<底栏<Toast<ConfirmDialog（弹窗必须盖住设置页）",
            settingsIdx < exportIdx && exportIdx <= importIdx && importIdx < navIdx && navIdx < toastIdx && toastIdx < confirmIdx);

        allPass &= Check("落盘显隐：明细页开，图表/记账/发现/设置与两弹窗全关",
            canvasTf.Find("DetailPanel").gameObject.activeSelf == true
            && canvasTf.Find("ChartPanel").gameObject.activeSelf == false
            && canvasTf.Find("RecordPanel").gameObject.activeSelf == false
            && canvasTf.Find("DiscoverPanel").gameObject.activeSelf == false
            && canvasTf.Find("SettingsPanel").gameObject.activeSelf == false
            && canvasTf.Find("ExportPanel").gameObject.activeSelf == false
            && canvasTf.Find("ImportPanel").gameObject.activeSelf == false);

        Debug.Log($"[SettingsBuild] ===== 设置/发现页搭建完成，自检 {(allPass ? "通过" : "失败(见上方 [SettingsBuild] FAIL 日志)")} =====");
    }

    private static bool Check(string title, bool condition)
    {
        Debug.Log($"[SettingsBuild] {(condition ? "PASS" : "FAIL")} - {title}");
        return condition;
    }

    // ---------- 工具 ----------

    private static void DestroyChild(Transform parent, string childName)
    {
        Transform old = parent.Find(childName);

        if (old != null)
        {
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
    }

    private static void SetActiveIfExists(Transform parent, string childName, bool active)
    {
        Transform child = parent.Find(childName);

        if (child != null)
        {
            child.gameObject.SetActive(active);
        }
    }

    private static void SetFitterMode(SafeAreaFitter fitter, SafeAreaFitter.Mode mode)
    {
        SerializedObject so = new SerializedObject(fitter);
        so.FindProperty("mode").enumValueIndex = (int)mode;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Color FromHex(long hex)
    {
        return new Color(
            ((hex >> 16) & 0xFF) / 255f,
            ((hex >> 8) & 0xFF) / 255f,
            (hex & 0xFF) / 255f,
            1f);
    }

    private static GameObject CreateStretchRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        return rect;
    }

    private static TMP_Text CreateLabel(Transform parent, string name, string text, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions alignment, Vector2 anchoredPosition = default)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = anchoredPosition;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        return tmp;
    }
}
