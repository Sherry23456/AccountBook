using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤09 一键搭建发现页功能（依赖 15 号五栏底栏）：
/// 1) 生成圆角卡 sprite（rounded.png，9-slice）与空态文档图标（icon_doc.png）；
/// 2) 新增 BudgetRepository/BudgetManager（budgets.json 持久化）；
/// 3) 重建 DiscoverPanel（账单卡 + 预算卡）；
/// 4) 新建 BillPanel（月/年账单 + 年份下拉）、BudgetPanel（月/年预算 + 总预算卡 + 分类预算）、
///    BudgetDialog（每月/年度总预算 + 分类预算设置底部弹窗，4×3 键盘）；
/// 5) 回填 UIManager 四个引用 + 明细页跳月联动；
/// 6) 层级重排：主面板(含账单/预算) → 导出/导入/预算弹窗 → 底栏 → Toast → ConfirmDialog；
/// 7) 存场景 → 预固化字形（含全部分类名）→ 接线自检。可重复执行（先删旧再建）。
/// 注意：需在 15 号之后执行；重跑 15 号后需重跑本菜单。
/// </summary>
public static class DiscoverFeatureBuilder
{
    private const string MenuItemPath = "AccountBook/16-搭建发现页功能";
    private const string FontAssetPath = "Assets/Fonts/STKAITI Dynamic SDF.asset";
    private const string RoundedSpritePath = "Assets/Art/Generated/rounded.png";
    private const string DocIconSpritePath = "Assets/Art/Generated/icon_doc.png";
    private const string CategoryPrefabPath = "Assets/Prefabs/CategoryGridItem.prefab";

    // 色板（与 15 号一致 + 参考图取色）
    private static readonly Color ColYellow = FromHex(0xFFD100);
    private static readonly Color ColBlack = FromHex(0x222222);
    private static readonly Color ColGray = FromHex(0x666666);
    private static readonly Color ColLightGray = FromHex(0x999999);
    private static readonly Color ColUnset = FromHex(0xC8C8C8);
    private static readonly Color ColDivider = FromHex(0xEEEEEE);
    private static readonly Color ColBackground = FromHex(0xF7F7F7);
    private static readonly Color ColWhite = FromHex(0xFFFFFF);
    private static readonly Color ColInputBg = FromHex(0xF2F2F2);
    private static readonly Color ColDoneDisabled = FromHex(0xDDDDDD);
    private static readonly Color ColMask = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color ColRingTrack = FromHex(0xE8E8E8);

    private const float TopBarHeight = 460f;
    private const float NavHeight = 170f;
    private const float CardMargin = 48f;
    private const float CardGap = 24f;
    private const float BillCardHeight = 340f;
    private const float BudgetCardHeight = 500f;
    private const float HeaderBarHeight = 170f;
    private const float SummaryCardHeight = 400f;
    private const float ListHeaderHeight = 90f;
    private const float TotalCardHeight = 470f;
    private const float BottomBarHeight = 130f;
    private const float SheetBaseHeight = 1150f;
    private const float KeypadHeight = 600f;

    [MenuItem(MenuItemPath)]
    private static void BuildFromMenu()
    {
        try
        {
            Build();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DiscoverBuild] 搭建失败：{ex.Message}\n{ex.StackTrace}\n可用菜单 {MenuItemPath} 重跑。");
        }
    }

    private class PanelRefs
    {
        // 发现页
        public Button BillCard;
        public TextMeshProUGUI TxtBillMonth;
        public TextMeshProUGUI TxtBillIncome;
        public TextMeshProUGUI TxtBillExpense;
        public TextMeshProUGUI TxtBillBalance;
        public Button BudgetCard;
        public Button BtnSetBudget;
        public TextMeshProUGUI TxtBudgetCardTitle;
        public RingGraphic RingTrack;
        public RingGraphic RingFill;
        public TextMeshProUGUI TxtRingRemain;
        public TextMeshProUGUI TxtRingPercent;
        public TextMeshProUGUI LbRemain;
        public TextMeshProUGUI LbBudget;
        public TextMeshProUGUI LbSpent;
        public TextMeshProUGUI TxtRemain;
        public TextMeshProUGUI TxtBudget;
        public TextMeshProUGUI TxtSpent;

        // 账单页
        public Button BtnBillBack;
        public Button BtnMonth;
        public Image ImgMonthBg;
        public TextMeshProUGUI LbMonth;
        public Button BtnYear;
        public Image ImgYearBg;
        public TextMeshProUGUI LbYear;
        public Button BtnYearPicker;
        public TextMeshProUGUI TxtYearLabel;
        public GameObject YearPickerBlocker;
        public GameObject YearPickerPopup;
        public RectTransform YearPickerContent;
        public TextMeshProUGUI TxtCaption;
        public TextMeshProUGUI TxtValue;
        public TextMeshProUGUI TxtIncome;
        public TextMeshProUGUI TxtExpense;
        public TextMeshProUGUI[] HeaderLabels = new TextMeshProUGUI[4];
        public RectTransform BillListContent;
        public GameObject BillEmptyLabel;
        public GameObject FooterNote;

        // 预算页
        public Button BtnBudgetBack;
        public Button BtnModeTitle;
        public TextMeshProUGUI TxtModeTitle;
        public GameObject ModeBlocker;
        public GameObject ModePopup;
        public Button BtnPickMonth;
        public Button BtnPickYear;
        public GameObject TotalCard;
        public TextMeshProUGUI TxtCardTitle;
        public Button BtnEdit;
        public RingGraphic RingTrack2;
        public RingGraphic RingFill2;
        public TextMeshProUGUI TxtRingRemain2;
        public TextMeshProUGUI TxtRingPercent2;
        public TextMeshProUGUI LbRemain2;
        public TextMeshProUGUI LbBudget2;
        public TextMeshProUGUI LbSpent2;
        public TextMeshProUGUI TxtRemain2;
        public TextMeshProUGUI TxtBudget2;
        public TextMeshProUGUI TxtSpent2;
        public GameObject EmptyState;
        public Button BtnEmptySet;
        public GameObject CategoryArea;
        public GameObject CategoryEmpty;
        public RectTransform CategoryContent;
        public GameObject BottomAddBar;
        public Button BtnAddCategory;

        // 预算弹窗
        public RectTransform Sheet;
        public TextMeshProUGUI TitleLabel;
        public Button BtnClose;
        public Button MaskButton;
        public TextMeshProUGUI TxtInput;
        public Button BtnConfirm;
        public Image ImgConfirm;
        public Button BtnDeleteBudget;
        public GameObject CategoryScroll;
        public RectTransform GridContent;
        public Button[] Digits = new Button[10];
        public Button Dot;
        public Button Backspace;
    }

    private static void Build()
    {
        // 外部覆写过的生成素材（rounded/icon_doc）先让资产库跟上，避免 Load 拿 null
        AssetDatabase.Refresh();

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("[DiscoverBuild] FAIL - 场景中没有 Canvas，请先跑 AccountBook/01-搭建主场景 Main。");
            return;
        }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        Sprite rounded = EnsureRoundedSprite();
        Sprite docIcon = EnsureDocIconSprite();
        // 预制体主资产是 GameObject，按组件类型 Load 会得到 null
        GameObject categoryPrefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(CategoryPrefabPath);
        CategoryGridItem categoryPrefab = categoryPrefabGo != null ? categoryPrefabGo.GetComponent<CategoryGridItem>() : null;
        UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        AccountManager accountManager = UnityEngine.Object.FindFirstObjectByType<AccountManager>(FindObjectsInactive.Include);
        DetailPanelUI detailPanel = UnityEngine.Object.FindFirstObjectByType<DetailPanelUI>(FindObjectsInactive.Include);
        CategoryIconProvider iconProvider = UnityEngine.Object.FindFirstObjectByType<CategoryIconProvider>(FindObjectsInactive.Include);

        if (font == null || rounded == null || docIcon == null || uiManager == null
            || accountManager == null || detailPanel == null || categoryPrefab == null || iconProvider == null)
        {
            Debug.LogError("[DiscoverBuild] FAIL - 缺少前置依赖：" +
                " font=" + (font == null) +
                " rounded=" + (rounded == null) +
                " docIcon=" + (docIcon == null) +
                " uiManager=" + (uiManager == null) +
                " accountManager=" + (accountManager == null) +
                " detailPanel=" + (detailPanel == null) +
                " categoryPrefab=" + (categoryPrefab == null) +
                " iconProvider=" + (iconProvider == null));
            return;
        }

        // —— 预算数据层：BudgetRepository 挂 JsonFileService 同物体，BudgetManager 独立物体 ——
        JsonFileService jsonFileService = UnityEngine.Object.FindFirstObjectByType<JsonFileService>(FindObjectsInactive.Include);
        BudgetRepository budgetRepository = UnityEngine.Object.FindFirstObjectByType<BudgetRepository>(FindObjectsInactive.Include);

        if (budgetRepository == null)
        {
            if (jsonFileService == null)
            {
                Debug.LogError("[DiscoverBuild] FAIL - 场景中没有 JsonFileService。");
                return;
            }

            budgetRepository = jsonFileService.gameObject.AddComponent<BudgetRepository>();
            Debug.Log("[DiscoverBuild] 已新增 BudgetRepository（挂 " + jsonFileService.gameObject.name + "）。");
        }

        BudgetManager budgetManager = UnityEngine.Object.FindFirstObjectByType<BudgetManager>(FindObjectsInactive.Include);

        if (budgetManager == null)
        {
            GameObject managerGo = new GameObject("BudgetManager");
            managerGo.transform.SetParent(accountManager.transform.parent, false);
            budgetManager = managerGo.AddComponent<BudgetManager>();
            Debug.Log("[DiscoverBuild] 已新增 BudgetManager。");
        }

        Transform canvasTf = canvas.transform;

        // —— 清旧（可重跑） ——
        DestroyChild(canvasTf, "DiscoverPanel");
        DestroyChild(canvasTf, "BillPanel");
        DestroyChild(canvasTf, "BudgetPanel");
        DestroyChild(canvasTf, "BudgetDialog");

        PanelRefs refs = new PanelRefs();

        // —— 1) 发现页 ——
        BuildDiscoverPanel(canvasTf, font, rounded, refs);
        // —— 2) 账单页 ——
        BuildBillPanel(canvasTf, font, rounded, refs);
        // —— 3) 预算页 ——
        BuildBudgetPanel(canvasTf, font, rounded, docIcon, refs);
        // —— 4) 预算弹窗 ——
        BuildBudgetDialog(canvasTf, font, rounded, refs);

        WireDiscoverPanel(canvasTf, refs, accountManager, budgetManager, uiManager);
        WireBillPanel(canvasTf, refs, font, accountManager, uiManager, detailPanel);
        WireBudgetPanel(canvasTf, refs, font, accountManager, budgetManager, uiManager);
        WireBudgetDialog(canvasTf, refs, budgetManager, uiManager, iconProvider, categoryPrefab);

        // —— 5) UIManager 回填 ——
        GameObject discoverPanel = canvasTf.Find("DiscoverPanel").gameObject;
        GameObject billPanel = canvasTf.Find("BillPanel").gameObject;
        GameObject budgetPanel = canvasTf.Find("BudgetPanel").gameObject;
        GameObject budgetDialog = canvasTf.Find("BudgetDialog").gameObject;

        SerializedObject uiSo = new SerializedObject(uiManager);
        uiSo.FindProperty("discoverPanel").objectReferenceValue = discoverPanel;
        uiSo.FindProperty("billPanel").objectReferenceValue = billPanel;
        uiSo.FindProperty("budgetPanel").objectReferenceValue = budgetPanel;
        uiSo.FindProperty("budgetDialog").objectReferenceValue = budgetDialog;
        uiSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(uiManager);

        // —— 6) 层级：主面板(含账单/预算) → 弹窗(导出/导入/预算) → 底栏 → Toast → ConfirmDialog ——
        string[] order = { "DetailPanel", "ChartPanel", "RecordPanel", "DiscoverPanel", "SettingsPanel",
                           "BillPanel", "BudgetPanel",
                           "ExportPanel", "ImportPanel", "BudgetDialog",
                           "BottomNav", "Toast", "ConfirmDialog" };
        int sibling = 0;

        foreach (string name in order)
        {
            Transform t = canvasTf.Find(name);

            if (t != null)
            {
                t.SetSiblingIndex(sibling++);
            }
        }

        // —— 落盘显隐复位 ——
        SetActiveIfExists(canvasTf, "DetailPanel", true);
        SetActiveIfExists(canvasTf, "ChartPanel", false);
        SetActiveIfExists(canvasTf, "RecordPanel", false);
        SetActiveIfExists(canvasTf, "DiscoverPanel", false);
        SetActiveIfExists(canvasTf, "SettingsPanel", false);
        SetActiveIfExists(canvasTf, "BillPanel", false);
        SetActiveIfExists(canvasTf, "BudgetPanel", false);
        SetActiveIfExists(canvasTf, "ExportPanel", false);
        SetActiveIfExists(canvasTf, "ImportPanel", false);
        SetActiveIfExists(canvasTf, "BudgetDialog", false);

        UnityEngine.SceneManagement.Scene scene = canvas.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        PreSolidifyGlyphs(font);

        RunSelfCheck(canvasTf, uiManager, refs);
    }

    // ---------- 发现页 ----------

    private static void BuildDiscoverPanel(Transform canvasTf, TMP_FontAsset font, Sprite rounded, PanelRefs refs)
    {
        GameObject panel = BuildMainShell(canvasTf, "DiscoverPanel", "发现", font);
        panel.SetActive(false);
        Transform content = panel.transform.Find("PanelContent");

        // —— 账单卡 ——
        GameObject billCard = CreateCard(content, "BillCard", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), -(TopBarHeight + CardGap), 0f, BillCardHeight, rounded);
        refs.BillCard = billCard.GetComponent<Button>();

        CreateLabel(billCard.transform, "Title", "账单", font, 46f, ColBlack, TextAlignmentOptions.Left,
            new Vector2(0.06f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(0f, -46f), new Vector2(300f, 80f));

        CreateLabel(billCard.transform, "Chevron", "＞", font, 40f, ColLightGray, TextAlignmentOptions.Center,
            new Vector2(0.94f, 1f), new Vector2(0.94f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(60f, 80f));

        // "10 月"（左大字 + 竖分隔线）
        refs.TxtBillMonth = CreateLabel(billCard.transform, "MonthLabel", "10 月", font, 58f, ColBlack,
            TextAlignmentOptions.Left,
            new Vector2(0.06f, 0.5f), new Vector2(0.06f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(220f, 120f));

        RectTransform vLine = CreateRect(billCard.transform, "VLine",
            new Vector2(0.26f, 0.12f), new Vector2(0.26f, 0.88f), new Vector2(0.5f, 0.5f));
        vLine.sizeDelta = new Vector2(3f, 0f);
        Image vLineImage = vLine.gameObject.AddComponent<Image>();
        vLineImage.color = ColDivider;
        vLineImage.raycastTarget = false;

        // 收/支/结余三列（标签在上值在下）
        CreateSummaryColumn(billCard.transform, "ColIncome", "收入", 0.34f, font, out refs.TxtBillIncome);
        CreateSummaryColumn(billCard.transform, "ColExpense", "支出", 0.56f, font, out refs.TxtBillExpense);
        CreateSummaryColumn(billCard.transform, "ColBalance", "结余", 0.78f, font, out refs.TxtBillBalance);

        // —— 预算卡 ——
        float budgetCardY = -(TopBarHeight + CardGap + BillCardHeight + CardGap);
        GameObject budgetCard = CreateCard(content, "BudgetCard", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), budgetCardY, 0f, BudgetCardHeight, rounded);
        refs.BudgetCard = budgetCard.GetComponent<Button>();

        refs.TxtBudgetCardTitle = CreateLabel(budgetCard.transform, "Title", "10月总预算", font, 46f, ColBlack,
            TextAlignmentOptions.Left,
            new Vector2(0.06f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(0f, -46f), new Vector2(500f, 80f));

        // + 设置预算 黄色胶囊（内层按钮，点击不透传卡身）
        GameObject setBudget = CreateRectGo(budgetCard.transform, "BtnSetBudget",
            new Vector2(0.94f, 1f), new Vector2(0.94f, 1f), new Vector2(1f, 1f));
        setBudget.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 84f);
        setBudget.GetComponent<RectTransform>().anchoredPosition = new Vector2(-24f, -46f);
        Image setBudgetImage = setBudget.AddComponent<Image>();
        setBudgetImage.sprite = rounded;
        setBudgetImage.type = Image.Type.Sliced;
        setBudgetImage.color = ColYellow;
        setBudgetImage.raycastTarget = true;
        refs.BtnSetBudget = setBudget.AddComponent<Button>();
        refs.BtnSetBudget.targetGraphic = setBudgetImage;
        CreateLabel(setBudget.transform, "Label", "+ 设置预算", font, 34f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // 圆环（底环灰 + 进度环黄 + 中心两行字）
        RectTransform ringRoot = CreateRect(budgetCard.transform, "RingRoot",
            new Vector2(0.14f, 0.36f), new Vector2(0.14f, 0.36f), new Vector2(0.5f, 0.5f));
        ringRoot.sizeDelta = new Vector2(240f, 240f);

        refs.RingTrack = CreateRing(ringRoot, "RingTrack", ColRingTrack, 26f, 1f);
        refs.RingFill = CreateRing(ringRoot, "RingFill", ColYellow, 26f, 0f);
        refs.RingFill.gameObject.SetActive(false);

        refs.TxtRingRemain = CreateLabel(ringRoot, "RingRemain", "剩余", font, 30f, ColUnset, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 36f), Vector2.zero);
        refs.TxtRingPercent = CreateLabel(ringRoot, "RingPercent", "0%", font, 44f, ColUnset, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), Vector2.zero);

        // 右侧三行
        refs.LbRemain = CreateLabel(budgetCard.transform, "LbRemain", "剩余预算:", font, 42f, ColUnset,
            TextAlignmentOptions.Left,
            new Vector2(0.34f, 1f), new Vector2(0.34f, 1f), new Vector2(0f, 1f), new Vector2(0f, -120f), new Vector2(320f, 70f));
        refs.TxtRemain = CreateLabel(budgetCard.transform, "TxtRemain", "0.00", font, 48f, ColUnset,
            TextAlignmentOptions.Right,
            new Vector2(0.94f, 1f), new Vector2(0.94f, 1f), new Vector2(1f, 1f), new Vector2(0f, -120f), new Vector2(300f, 70f));

        RectTransform hLine = CreateRect(budgetCard.transform, "HLine",
            new Vector2(0.34f, 1f), new Vector2(0.94f, 1f), new Vector2(0.5f, 1f));
        hLine.sizeDelta = new Vector2(0f, 2f);
        hLine.anchoredPosition = new Vector2(0f, -186f);
        Image hLineImage = hLine.gameObject.AddComponent<Image>();
        hLineImage.color = ColDivider;
        hLineImage.raycastTarget = false;

        refs.LbBudget = CreateLabel(budgetCard.transform, "LbBudget", "本月预算:", font, 38f, ColUnset,
            TextAlignmentOptions.Left,
            new Vector2(0.34f, 1f), new Vector2(0.34f, 1f), new Vector2(0f, 1f), new Vector2(0f, -234f), new Vector2(320f, 64f));
        refs.TxtBudget = CreateLabel(budgetCard.transform, "TxtBudget", "0.00", font, 40f, ColUnset,
            TextAlignmentOptions.Right,
            new Vector2(0.94f, 1f), new Vector2(0.94f, 1f), new Vector2(1f, 1f), new Vector2(0f, -234f), new Vector2(300f, 64f));

        refs.LbSpent = CreateLabel(budgetCard.transform, "LbSpent", "本月支出:", font, 38f, ColUnset,
            TextAlignmentOptions.Left,
            new Vector2(0.34f, 1f), new Vector2(0.34f, 1f), new Vector2(0f, 1f), new Vector2(0f, -322f), new Vector2(320f, 64f));
        refs.TxtSpent = CreateLabel(budgetCard.transform, "TxtSpent", "0.00", font, 40f, ColUnset,
            TextAlignmentOptions.Right,
            new Vector2(0.94f, 1f), new Vector2(0.94f, 1f), new Vector2(1f, 1f), new Vector2(0f, -322f), new Vector2(300f, 64f));
    }

    private static void CreateSummaryColumn(Transform parent, string name, string labelText, float xFrac,
        TMP_FontAsset font, out TextMeshProUGUI valueLabel)
    {
        CreateLabel(parent, name + "_Label", labelText, font, 32f, ColGray, TextAlignmentOptions.Left,
            new Vector2(xFrac, 1f), new Vector2(xFrac, 1f), new Vector2(0f, 1f), new Vector2(0f, -176f), new Vector2(180f, 50f));
        valueLabel = CreateLabel(parent, name + "_Value", "0.00", font, 42f, ColBlack, TextAlignmentOptions.Left,
            new Vector2(xFrac, 1f), new Vector2(xFrac, 1f), new Vector2(0f, 1f), new Vector2(0f, -252f), new Vector2(200f, 64f));
    }

    // ---------- 账单页 ----------

    private static void BuildBillPanel(Transform canvasTf, TMP_FontAsset font, Sprite rounded, PanelRefs refs)
    {
        GameObject panel = CreateStretchRect(canvasTf, "BillPanel");
        Image bg = panel.AddComponent<Image>();
        bg.color = ColBackground;
        bg.raycastTarget = false;

        RectTransform content = CreateStretchRect(panel.transform, "PanelContent").GetComponent<RectTransform>();
        content.offsetMin = new Vector2(0f, NavHeight);
        content.offsetMax = Vector2.zero;

        // 顶栏 + 返回 + 标题
        BuildSubPageTopBar(content, font, "账单", out refs.BtnBillBack);

        // 头部条：年份下拉（左） + 月/年分段（中）
        RectTransform headerBar = CreateRect(content, "HeaderBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        headerBar.sizeDelta = new Vector2(0f, HeaderBarHeight);
        headerBar.anchoredPosition = new Vector2(0f, -TopBarHeight);
        Image headerBarImage = headerBar.gameObject.AddComponent<Image>();
        headerBarImage.color = ColWhite;
        headerBarImage.raycastTarget = false;

        GameObject yearPicker = CreateRectGo(headerBar.transform, "BtnYearPicker",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        yearPicker.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 110f);
        yearPicker.GetComponent<RectTransform>().anchoredPosition = new Vector2(CardMargin, 0f);
        Image yearPickerImage = yearPicker.AddComponent<Image>();
        yearPickerImage.color = new Color(0f, 0f, 0f, 0f);
        refs.BtnYearPicker = yearPicker.AddComponent<Button>();
        refs.BtnYearPicker.targetGraphic = yearPickerImage;
        refs.TxtYearLabel = CreateLabel(yearPicker.transform, "Label", "2026 年 ▼", font, 40f, ColBlack,
            TextAlignmentOptions.Left, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // 分段控件（居中）
        RectTransform seg = CreateRect(headerBar.transform, "Segment",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        seg.sizeDelta = new Vector2(460f, 116f);
        Image segImage = seg.gameObject.AddComponent<Image>();
        segImage.sprite = rounded;
        segImage.type = Image.Type.Sliced;
        segImage.color = ColWhite;
        segImage.raycastTarget = false;

        BuildSegmentButton(seg, "BtnMonth", "月账单", -115f, font, rounded, out refs.BtnMonth, out refs.ImgMonthBg, out refs.LbMonth);
        BuildSegmentButton(seg, "BtnYear", "年账单", 115f, font, rounded, out refs.BtnYear, out refs.ImgYearBg, out refs.LbYear);

        // 汇总黄卡
        float cardY = -(TopBarHeight + HeaderBarHeight + CardGap);
        GameObject summary = CreateCard(content, "SummaryCard", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), cardY, 0f, SummaryCardHeight, rounded);
        Image summaryBg = summary.GetComponent<Image>();
        summaryBg.color = ColYellow;
        summaryBg.raycastTarget = false;

        CreateLabel(summary.transform, "Watermark", "￥", font, 200f, new Color(1f, 1f, 1f, 0.35f),
            TextAlignmentOptions.Center,
            new Vector2(0.92f, 0.7f), new Vector2(0.92f, 0.7f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 220f));

        refs.TxtCaption = CreateLabel(summary.transform, "Caption", "年结余", font, 36f, new Color(0.13f, 0.13f, 0.13f, 0.8f),
            TextAlignmentOptions.Left,
            new Vector2(0.06f, 1f), new Vector2(0.06f, 1f), new Vector2(0f, 1f), new Vector2(0f, -48f), new Vector2(300f, 60f));

        refs.TxtValue = CreateLabel(summary.transform, "Value", "0.00", font, 92f, ColBlack, TextAlignmentOptions.Left,
            new Vector2(0.06f, 1f), new Vector2(0.06f, 1f), new Vector2(0f, 1f), new Vector2(0f, -190f), new Vector2(700f, 130f));

        refs.TxtIncome = CreateLabel(summary.transform, "Income", "年收入 0.00", font, 40f, ColBlack,
            TextAlignmentOptions.Left,
            new Vector2(0.06f, 0f), new Vector2(0.06f, 0f), new Vector2(0f, 0f), new Vector2(0f, 44f), new Vector2(420f, 60f));

        refs.TxtExpense = CreateLabel(summary.transform, "Expense", "年支出 0.00", font, 40f, ColBlack,
            TextAlignmentOptions.Left,
            new Vector2(0.56f, 0f), new Vector2(0.56f, 0f), new Vector2(0f, 0f), new Vector2(0f, 44f), new Vector2(420f, 60f));

        // 表头行
        RectTransform listHeader = CreateRect(content, "ListHeader",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        listHeader.sizeDelta = new Vector2(0f, ListHeaderHeight);
        listHeader.anchoredPosition = new Vector2(0f, cardY - SummaryCardHeight);
        Image listHeaderImage = listHeader.gameObject.AddComponent<Image>();
        listHeaderImage.color = ColWhite;
        listHeaderImage.raycastTarget = false;

        string[] headerTexts = { "月份", "月收入", "月支出", "月结余" };
        float[] headerFracs = { 0.07f, 0.30f, 0.53f, 0.76f };

        for (int i = 0; i < 4; i++)
        {
            refs.HeaderLabels[i] = CreateLabel(listHeader, "H" + i, headerTexts[i], font, 32f, ColLightGray,
                TextAlignmentOptions.Left,
                new Vector2(headerFracs[i], 0f), new Vector2(headerFracs[i] + 0.20f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
        }

        // 列表滚动区
        float listTop = cardY - SummaryCardHeight - ListHeaderHeight;
        GameObject scrollView = CreateRectGo(content, "ListScrollView",
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f)).gameObject;
        RectTransform scrollRectTf = (RectTransform)scrollView.transform;
        scrollRectTf.offsetMin = Vector2.zero;
        scrollRectTf.offsetMax = new Vector2(0f, listTop);

        RectTransform viewport = CreateRect(scrollView.transform, "Viewport",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform listContent = CreateRect(viewport, "Content",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        listContent.offsetMin = Vector2.zero;
        listContent.offsetMax = Vector2.zero;

        Image contentRaycast = listContent.gameObject.AddComponent<Image>();
        contentRaycast.color = new Color(0f, 0f, 0f, 0f);
        contentRaycast.raycastTarget = true;

        VerticalLayoutGroup layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 0f;

        ContentSizeFitter fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.content = listContent;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        refs.BillListContent = listContent;

        // 空态
        GameObject empty = CreateLabel(listContent, "EmptyLabel", "暂无账单", font, 36f, ColLightGray,
            TextAlignmentOptions.Center, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
        LayoutElement emptyLayout = empty.AddComponent<LayoutElement>();
        emptyLayout.preferredHeight = 300f;
        empty.SetActive(false);
        refs.BillEmptyLabel = empty;

        // 年账单脚注（固定在滚动区之上底部）
        refs.FooterNote = CreateLabel(content, "FooterNote", "年账单为自然年（1.1-12.31）", font, 32f, ColLightGray,
            TextAlignmentOptions.Center,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(0f, 50f)).gameObject;
        refs.FooterNote.SetActive(false);

        // 年份下拉弹层（含全屏透明遮罩按钮，遮罩在 HeaderBar 之后、顶栏之前）
        GameObject blocker = CreateStretchRect(content, "YearPickerBlocker");
        Image blockerImage = blocker.AddComponent<Image>();
        blockerImage.color = new Color(0f, 0f, 0f, 0f);
        blockerImage.raycastTarget = true;
        blocker.AddComponent<Button>();
        blocker.SetActive(false);
        refs.YearPickerBlocker = blocker;

        GameObject popup = CreateRectGo(content, "YearPickerPopup",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        popup.GetComponent<RectTransform>().sizeDelta = new Vector2(340f, 232f);
        popup.GetComponent<RectTransform>().anchoredPosition = new Vector2(CardMargin, -(TopBarHeight + HeaderBarHeight + 8f));
        Image popupImage = popup.AddComponent<Image>();
        popupImage.sprite = rounded;
        popupImage.type = Image.Type.Sliced;
        popupImage.color = ColWhite;
        popupImage.raycastTarget = true;
        popup.SetActive(false);
        refs.YearPickerPopup = popup;

        RectTransform pickerViewport = CreateRect(popup.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        pickerViewport.gameObject.AddComponent<RectMask2D>();
        RectTransform pickerContent = CreateRect(pickerViewport, "Content",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        pickerContent.offsetMin = Vector2.zero;
        pickerContent.offsetMax = Vector2.zero;
        VerticalLayoutGroup pickerLayout = pickerContent.gameObject.AddComponent<VerticalLayoutGroup>();
        pickerLayout.childControlWidth = true;
        pickerLayout.childControlHeight = true;
        pickerLayout.childForceExpandWidth = true;
        pickerLayout.childForceExpandHeight = false;
        pickerLayout.spacing = 0f;
        ContentSizeFitter pickerFitter = pickerContent.gameObject.AddComponent<ContentSizeFitter>();
        pickerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        refs.YearPickerContent = pickerContent;
    }

    private static void BuildSegmentButton(RectTransform parent, string name, string label, float x,
        TMP_FontAsset font, Sprite rounded, out Button button, out Image bgImage, out TextMeshProUGUI labelTmp)
    {
        GameObject go = CreateRectGo(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(230f, 116f);
        go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, 0f);

        bgImage = go.AddComponent<Image>();
        bgImage.sprite = rounded;
        bgImage.type = Image.Type.Sliced;
        bgImage.color = ColWhite;

        button = go.AddComponent<Button>();
        button.targetGraphic = bgImage;

        labelTmp = CreateLabel(go.transform, "Label", label, font, 38f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    // ---------- 预算页 ----------

    private static void BuildBudgetPanel(Transform canvasTf, TMP_FontAsset font, Sprite rounded, Sprite docIcon, PanelRefs refs)
    {
        GameObject panel = CreateStretchRect(canvasTf, "BudgetPanel");
        Image bg = panel.AddComponent<Image>();
        bg.color = ColBackground;
        bg.raycastTarget = false;

        RectTransform content = CreateStretchRect(panel.transform, "PanelContent").GetComponent<RectTransform>();
        content.offsetMin = new Vector2(0f, NavHeight);
        content.offsetMax = Vector2.zero;

        // 顶栏 + 返回 + 标题按钮（月预算 ▼）
        BuildSubPageTopBar(content, font, "", out refs.BtnBudgetBack);

        GameObject modeTitle = CreateRectGo(content.Find("TopBar/TopContent"), "ModeTitle",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        modeTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 110f);
        modeTitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -8f);
        Image modeTitleImage = modeTitle.AddComponent<Image>();
        modeTitleImage.color = new Color(0f, 0f, 0f, 0f);
        refs.BtnModeTitle = modeTitle.AddComponent<Button>();
        refs.BtnModeTitle.targetGraphic = modeTitleImage;
        refs.TxtModeTitle = CreateLabel(modeTitle.transform, "Label", "月预算 ▼", font, 48f, ColBlack,
            TextAlignmentOptions.Center, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // 档位下拉弹层（遮罩 + 白色两选项）
        GameObject modeBlocker = CreateStretchRect(content, "ModeBlocker");
        Image modeBlockerImage = modeBlocker.AddComponent<Image>();
        modeBlockerImage.color = new Color(0f, 0f, 0f, 0f);
        modeBlockerImage.raycastTarget = true;
        modeBlocker.AddComponent<Button>();
        modeBlocker.SetActive(false);
        refs.ModeBlocker = modeBlocker;

        GameObject modePopup = CreateRectGo(content, "ModePopup",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        modePopup.GetComponent<RectTransform>().sizeDelta = new Vector2(360f, 248f);
        modePopup.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -(TopBarHeight + 12f));
        Image modePopupImage = modePopup.AddComponent<Image>();
        modePopupImage.sprite = rounded;
        modePopupImage.type = Image.Type.Sliced;
        modePopupImage.color = ColWhite;
        modePopupImage.raycastTarget = true;
        modePopup.SetActive(false);
        refs.ModePopup = modePopup;

        refs.BtnPickMonth = CreatePopupOption(modePopup.transform, "OptMonth", "月预算", 0f, font, rounded);
        CreateDividerLine(modePopup.transform, "Divider", -124f);
        refs.BtnPickYear = CreatePopupOption(modePopup.transform, "OptYear", "年预算", -124f, font, rounded);

        // 总预算卡（默认隐藏，运行时按预算有无切换）
        float cardY = -(TopBarHeight + CardGap);
        GameObject totalCard = CreateCard(content, "TotalCard", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), cardY, 0f, TotalCardHeight, rounded);
        totalCard.SetActive(false);
        refs.TotalCard = totalCard;

        refs.TxtCardTitle = CreateLabel(totalCard.transform, "Title", "10月总预算", font, 44f, ColBlack,
            TextAlignmentOptions.Left,
            new Vector2(0.05f, 1f), new Vector2(0.05f, 1f), new Vector2(0f, 1f), new Vector2(0f, -44f), new Vector2(500f, 76f));

        GameObject editBtn = CreateRectGo(totalCard.transform, "BtnEdit",
            new Vector2(0.95f, 1f), new Vector2(0.95f, 1f), new Vector2(1f, 1f));
        editBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 76f);
        editBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(-24f, -44f);
        Image editImage = editBtn.AddComponent<Image>();
        editImage.color = new Color(0f, 0f, 0f, 0f);
        refs.BtnEdit = editBtn.AddComponent<Button>();
        refs.BtnEdit.targetGraphic = editImage;
        CreateLabel(editBtn.transform, "Label", "编辑", font, 38f, ColGray, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // 圆环块
        RectTransform ringRoot = CreateRect(totalCard.transform, "RingRoot",
            new Vector2(0.16f, 0.42f), new Vector2(0.16f, 0.42f), new Vector2(0.5f, 0.5f));
        ringRoot.sizeDelta = new Vector2(250f, 250f);

        refs.RingTrack2 = CreateRing(ringRoot, "RingTrack", ColRingTrack, 30f, 1f);
        refs.RingFill2 = CreateRing(ringRoot, "RingFill", ColYellow, 30f, 1f);

        refs.TxtRingRemain2 = CreateLabel(ringRoot, "RingRemain", "剩余", font, 32f, ColGray, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 38f), Vector2.zero);
        refs.TxtRingPercent2 = CreateLabel(ringRoot, "RingPercent", "100%", font, 46f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -32f), Vector2.zero);

        // 右侧三行（剩余预算加粗行 + 分隔线 + 预算/支出两行）
        refs.LbRemain2 = CreateLabel(totalCard.transform, "LbRemain", "剩余预算:", font, 40f, ColBlack,
            TextAlignmentOptions.Left,
            new Vector2(0.36f, 1f), new Vector2(0.36f, 1f), new Vector2(0f, 1f), new Vector2(0f, -130f), new Vector2(320f, 66f));
        refs.TxtRemain2 = CreateLabel(totalCard.transform, "TxtRemain", "0.00", font, 50f, ColBlack,
            TextAlignmentOptions.Right,
            new Vector2(0.95f, 1f), new Vector2(0.95f, 1f), new Vector2(1f, 1f), new Vector2(0f, -130f), new Vector2(300f, 66f));

        RectTransform hLine = CreateRect(totalCard.transform, "HLine",
            new Vector2(0.36f, 1f), new Vector2(0.95f, 1f), new Vector2(0.5f, 1f));
        hLine.sizeDelta = new Vector2(0f, 2f);
        hLine.anchoredPosition = new Vector2(0f, -200f);
        Image hLineImage = hLine.gameObject.AddComponent<Image>();
        hLineImage.color = ColDivider;
        hLineImage.raycastTarget = false;

        refs.LbBudget2 = CreateLabel(totalCard.transform, "LbBudget", "本月预算:", font, 36f, ColGray,
            TextAlignmentOptions.Left,
            new Vector2(0.36f, 1f), new Vector2(0.36f, 1f), new Vector2(0f, 1f), new Vector2(0f, -252f), new Vector2(320f, 62f));
        refs.TxtBudget2 = CreateLabel(totalCard.transform, "TxtBudget", "0.00", font, 40f, ColGray,
            TextAlignmentOptions.Right,
            new Vector2(0.95f, 1f), new Vector2(0.95f, 1f), new Vector2(1f, 1f), new Vector2(0f, -252f), new Vector2(300f, 62f));

        refs.LbSpent2 = CreateLabel(totalCard.transform, "LbSpent", "本月支出:", font, 36f, ColGray,
            TextAlignmentOptions.Left,
            new Vector2(0.36f, 1f), new Vector2(0.36f, 1f), new Vector2(0f, 1f), new Vector2(0f, -346f), new Vector2(320f, 62f));
        refs.TxtSpent2 = CreateLabel(totalCard.transform, "TxtSpent", "0.00", font, 40f, ColGray,
            TextAlignmentOptions.Right,
            new Vector2(0.95f, 1f), new Vector2(0.95f, 1f), new Vector2(1f, 1f), new Vector2(0f, -346f), new Vector2(300f, 62f));

        // 空态（暂无预算）：图标 + 文案 + 设置按钮，居中于顶栏下方区域
        GameObject emptyState = CreateRectGo(content, "EmptyState", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        emptyState.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        emptyState.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -TopBarHeight);
        emptyState.SetActive(false);
        refs.EmptyState = emptyState;

        RectTransform docImage = CreateRect(emptyState.transform, "DocIcon",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        docImage.sizeDelta = new Vector2(220f, 220f);
        docImage.anchoredPosition = new Vector2(0f, 130f);
        Image docIconImage = docImage.gameObject.AddComponent<Image>();
        docIconImage.sprite = docIcon;
        docIconImage.raycastTarget = false;

        CreateLabel(emptyState.transform, "Text", "暂无预算", font, 40f, ColLightGray, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(400f, 70f));

        GameObject emptySet = CreateRectGo(emptyState.transform, "BtnEmptySet",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        emptySet.GetComponent<RectTransform>().sizeDelta = new Vector2(340f, 110f);
        emptySet.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -150f);
        Image emptySetImage = emptySet.AddComponent<Image>();
        emptySetImage.sprite = rounded;
        emptySetImage.type = Image.Type.Sliced;
        emptySetImage.color = ColYellow;
        refs.BtnEmptySet = emptySet.AddComponent<Button>();
        refs.BtnEmptySet.targetGraphic = emptySetImage;
        CreateLabel(emptySet.transform, "Label", "+ 设置预算", font, 36f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // 分类预算滚动区（顶栏下、底栏上；内容含"未设置分类预算"空态与分类行）
        float areaTop = cardY - TotalCardHeight - 16f;
        GameObject categoryArea = CreateRectGo(content, "CategoryArea",
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f)).gameObject;
        RectTransform categoryAreaRect = (RectTransform)categoryArea.transform;
        categoryAreaRect.offsetMin = new Vector2(0f, BottomBarHeight);
        categoryAreaRect.offsetMax = new Vector2(0f, areaTop);
        categoryArea.SetActive(false);
        refs.CategoryArea = categoryArea;

        RectTransform catViewport = CreateRect(categoryArea.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        catViewport.gameObject.AddComponent<RectMask2D>();

        RectTransform catContent = CreateRect(catViewport, "Content",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        catContent.offsetMin = Vector2.zero;
        catContent.offsetMax = Vector2.zero;
        VerticalLayoutGroup catLayout = catContent.gameObject.AddComponent<VerticalLayoutGroup>();
        catLayout.childControlWidth = true;
        catLayout.childControlHeight = true;
        catLayout.childForceExpandWidth = true;
        catLayout.childForceExpandHeight = false;
        catLayout.spacing = 0f;
        ContentSizeFitter catFitter = catContent.gameObject.AddComponent<ContentSizeFitter>();
        catFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        refs.CategoryContent = catContent;

        // 分类空态（挂在滚动 Content 内，随行有无切换）
        GameObject catEmpty = new GameObject("CategoryEmpty", typeof(RectTransform));
        catEmpty.transform.SetParent(catContent, false);
        RectTransform catEmptyRect = (RectTransform)catEmpty.transform;
        catEmptyRect.anchorMin = new Vector2(0f, 0f);
        catEmptyRect.anchorMax = new Vector2(1f, 0f);
        catEmptyRect.pivot = new Vector2(0.5f, 0.5f);
        catEmptyRect.sizeDelta = new Vector2(0f, 700f);
        LayoutElement catEmptyLayout = catEmpty.AddComponent<LayoutElement>();
        catEmptyLayout.preferredHeight = 700f;

        RectTransform catDoc = CreateRect(catEmpty.transform, "DocIcon",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        catDoc.sizeDelta = new Vector2(220f, 220f);
        catDoc.anchoredPosition = new Vector2(0f, -200f);
        Image catDocImage = catDoc.gameObject.AddComponent<Image>();
        catDocImage.sprite = docIcon;
        catDocImage.raycastTarget = false;

        CreateLabel(catEmpty.transform, "Text", "未设置分类预算", font, 40f, ColLightGray, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -360f), new Vector2(600f, 70f));
        catEmpty.SetActive(false);
        refs.CategoryEmpty = catEmpty;

        // 底部"+ 添加分类预算"常驻条（月档且有总预算时显示）
        GameObject bottomBar = CreateRectGo(content, "BottomAddBar",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        bottomBar.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, BottomBarHeight);
        Image bottomBarImage = bottomBar.gameObject.AddComponent<Image>();
        bottomBarImage.color = ColWhite;
        bottomBarImage.raycastTarget = false;
        bottomBar.SetActive(false);
        refs.BottomAddBar = bottomBar;

        CreateDividerLine(bottomBar.transform, "TopDivider", 0f);

        GameObject addBtn = CreateRectGo(bottomBar.transform, "BtnAddCategory",
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        addBtn.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        addBtn.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        Image addBtnImage = addBtn.AddComponent<Image>();
        addBtnImage.color = new Color(0f, 0f, 0f, 0f);
        refs.BtnAddCategory = addBtn.AddComponent<Button>();
        refs.BtnAddCategory.targetGraphic = addBtnImage;
        CreateLabel(addBtn.transform, "Label", "+ 添加分类预算", font, 42f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private static Button CreatePopupOption(Transform parent, string name, string label, float topY,
        TMP_FontAsset font, Sprite rounded)
    {
        GameObject go = CreateRectGo(parent, name,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 124f);
        go.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, topY);

        Image image = go.AddComponent<Image>();
        image.sprite = rounded;
        image.type = Image.Type.Sliced;
        image.color = ColWhite;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;

        CreateLabel(go.transform, "Label", label, font, 40f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return button;
    }

    private static void CreateDividerLine(Transform parent, string name, float topY)
    {
        RectTransform divider = CreateRect(parent, name,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        divider.sizeDelta = new Vector2(0f, 2f);
        divider.anchoredPosition = new Vector2(0f, topY);
        Image image = divider.gameObject.AddComponent<Image>();
        image.color = ColDivider;
        image.raycastTarget = false;
    }

    // ---------- 预算弹窗 ----------

    private static void BuildBudgetDialog(Transform canvasTf, TMP_FontAsset font, Sprite rounded, PanelRefs refs)
    {
        GameObject dialog = CreateStretchRect(canvasTf, "BudgetDialog");
        dialog.SetActive(false);

        GameObject mask = CreateStretchRect(dialog.transform, "Mask");
        Image maskImage = mask.AddComponent<Image>();
        maskImage.color = ColMask;
        maskImage.raycastTarget = true;
        refs.MaskButton = mask.AddComponent<Button>();
        refs.MaskButton.targetGraphic = maskImage;

        GameObject sheet = CreateRectGo(dialog.transform, "Sheet",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        RectTransform sheetRect = (RectTransform)sheet.transform;
        sheetRect.sizeDelta = new Vector2(0f, SheetBaseHeight);
        sheetRect.anchoredPosition = new Vector2(0f, NavHeight);
        Image sheetImage = sheet.AddComponent<Image>();
        sheetImage.sprite = rounded;
        sheetImage.type = Image.Type.Sliced;
        sheetImage.color = ColWhite;
        sheetImage.raycastTarget = true;
        refs.Sheet = sheetRect;

        // 标题 + 关闭
        refs.TitleLabel = CreateLabel(sheet.transform, "Title", "每月总预算", font, 48f, ColBlack,
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(600f, 90f));

        GameObject closeBtn = CreateRectGo(sheet.transform, "BtnClose",
            new Vector2(0.95f, 1f), new Vector2(0.95f, 1f), new Vector2(0.5f, 0.5f));
        closeBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(90f, 90f);
        closeBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -80f);
        Image closeImage = closeBtn.AddComponent<Image>();
        closeImage.color = new Color(0f, 0f, 0f, 0f);
        refs.BtnClose = closeBtn.AddComponent<Button>();
        refs.BtnClose.targetGraphic = closeImage;
        CreateLabel(closeBtn.transform, "Label", "×", font, 52f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // 金额显示框
        GameObject inputBox = CreateRectGo(sheet.transform, "InputBox",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        inputBox.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 120f);
        inputBox.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -170f);
        Image inputImage = inputBox.AddComponent<Image>();
        inputImage.sprite = rounded;
        inputImage.type = Image.Type.Sliced;
        inputImage.color = ColInputBg;
        inputImage.raycastTarget = false;

        refs.TxtInput = CreateLabel(inputBox.transform, "InputText", "请输入预算金额", font, 44f, ColGray,
            TextAlignmentOptions.Left,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        // 拉伸锚 + 显式左右内边距（SettingsPanelBuilder 同款，避免 anchoredPosition 平移歧义）
        refs.TxtInput.rectTransform.offsetMin = new Vector2(40f, 0f);
        refs.TxtInput.rectTransform.offsetMax = new Vector2(-40f, 0f);

        // 分类宫格滚动区（分类模式显示）
        GameObject categoryScroll = CreateRectGo(sheet.transform, "CategoryScroll",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f)).gameObject;
        categoryScroll.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 470f);
        categoryScroll.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -330f);
        categoryScroll.SetActive(false);
        refs.CategoryScroll = categoryScroll;

        RectTransform catViewport = CreateRect(categoryScroll.transform, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        catViewport.gameObject.AddComponent<RectMask2D>();

        RectTransform gridContent = CreateRect(catViewport, "GridContent",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        gridContent.offsetMin = Vector2.zero;
        gridContent.offsetMax = Vector2.zero;
        GridLayoutGroup grid = gridContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(228f, 220f);
        grid.spacing = new Vector2(16f, 16f);
        grid.padding = new RectOffset(24, 24, 0, 0);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment = TextAnchor.UpperCenter;
        ContentSizeFitter gridFitter = gridContent.gameObject.AddComponent<ContentSizeFitter>();
        gridFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        refs.GridContent = gridContent;

        // 确认行（删除预算 + 确定）
        GameObject confirmRow = CreateRectGo(sheet.transform, "ConfirmRow",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        confirmRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 140f);
        confirmRow.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, KeypadHeight + 40f);

        GameObject deleteBtn = CreateRectGo(confirmRow.transform, "BtnDeleteBudget",
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
        deleteBtn.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);
        deleteBtn.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);
        deleteBtn.GetComponent<RectTransform>().offsetMin = new Vector2(CardMargin, 0f);
        deleteBtn.GetComponent<RectTransform>().offsetMax = new Vector2(240f, 0f);
        Image deleteImage = deleteBtn.AddComponent<Image>();
        deleteImage.color = new Color(0f, 0f, 0f, 0f);
        refs.BtnDeleteBudget = deleteBtn.AddComponent<Button>();
        refs.BtnDeleteBudget.targetGraphic = deleteImage;
        CreateLabel(deleteBtn.transform, "Label", "删除预算", font, 38f, ColGray, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        GameObject confirmBtn = CreateRectGo(confirmRow.transform, "BtnConfirm",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        confirmBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(560f, 120f);
        Image confirmImage = confirmBtn.AddComponent<Image>();
        confirmImage.sprite = rounded;
        confirmImage.type = Image.Type.Sliced;
        confirmImage.color = ColDoneDisabled;
        refs.BtnConfirm = confirmBtn.AddComponent<Button>();
        refs.BtnConfirm.targetGraphic = confirmImage;
        refs.ImgConfirm = confirmImage;
        CreateLabel(confirmBtn.transform, "Label", "确定", font, 42f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // 4×3 键盘（7 8 9 / 4 5 6 / 1 2 3 / . 0 退格）+ 细分隔线
        BuildDialogKeypad(sheet.transform, font, refs);
    }

    private static void BuildDialogKeypad(Transform sheet, TMP_FontAsset font, PanelRefs refs)
    {
        RectTransform keypad = CreateRect(sheet, "Keypad",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        keypad.sizeDelta = new Vector2(0f, KeypadHeight);

        // 细分隔线：2 竖 3 横
        for (int i = 1; i <= 2; i++)
        {
            RectTransform v = CreateRect(keypad, "VLine" + i,
                new Vector2(i / 3f, 0f), new Vector2(i / 3f, 1f), new Vector2(0.5f, 0.5f));
            v.sizeDelta = new Vector2(2f, 0f);
            Image vImage = v.gameObject.AddComponent<Image>();
            vImage.color = ColDivider;
            vImage.raycastTarget = false;
        }

        for (int i = 1; i <= 3; i++)
        {
            RectTransform h = CreateRect(keypad, "HLine" + i,
                new Vector2(0f, i / 4f), new Vector2(1f, i / 4f), new Vector2(0.5f, 0.5f));
            h.sizeDelta = new Vector2(0f, 2f);
            Image hImage = h.gameObject.AddComponent<Image>();
            hImage.color = ColDivider;
            hImage.raycastTarget = false;
        }

        // 键面（行优先）：7 8 9 / 4 5 6 / 1 2 3 / . 0 退格
        string[] keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", ".", "0", "退格" };

        for (int i = 0; i < keys.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            string key = keys[i];

            GameObject keyGo = CreateRectGo(keypad, "Key_" + key,
                new Vector2(col / 3f, 1f - row / 4f), new Vector2((col + 1) / 3f, 1f - (row + 1) / 4f),
                new Vector2(0.5f, 0.5f));
            keyGo.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            keyGo.GetComponent<RectTransform>().offsetMax = Vector2.zero;

            Image keyImage = keyGo.AddComponent<Image>();
            keyImage.color = new Color(0f, 0f, 0f, 0f);

            Button keyButton = keyGo.AddComponent<Button>();
            keyButton.targetGraphic = keyImage;

            float fontSize = key == "退格" ? 40f : 52f;
            CreateLabel(keyGo.transform, "Label", key, font, fontSize, ColBlack, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            if (key == ".")
            {
                refs.Dot = keyButton;
            }
            else if (key == "退格")
            {
                refs.Backspace = keyButton;
            }
            else
            {
                int digit = int.Parse(key);
                refs.Digits[digit] = keyButton;
            }
        }
    }

    // ---------- 接线 ----------

    private static void WireDiscoverPanel(Transform canvasTf, PanelRefs refs, AccountManager accountManager,
        BudgetManager budgetManager, UIManager uiManager)
    {
        GameObject panel = canvasTf.Find("DiscoverPanel").gameObject;
        DiscoverPanelUI ui = panel.GetComponent<DiscoverPanelUI>();

        if (ui == null)
        {
            ui = panel.AddComponent<DiscoverPanelUI>();
        }

        BudgetDialogUI dialogUi = canvasTf.Find("BudgetDialog").GetComponent<BudgetDialogUI>();

        SerializedObject so = new SerializedObject(ui);
        SetRef(so, "accountManager", accountManager);
        SetRef(so, "budgetManager", budgetManager);
        SetRef(so, "uiManager", uiManager);
        SetRef(so, "budgetDialog", dialogUi);
        SetRef(so, "billCardButton", refs.BillCard);
        SetRef(so, "txtBillMonth", refs.TxtBillMonth);
        SetRef(so, "txtBillIncome", refs.TxtBillIncome);
        SetRef(so, "txtBillExpense", refs.TxtBillExpense);
        SetRef(so, "txtBillBalance", refs.TxtBillBalance);
        SetRef(so, "budgetCardButton", refs.BudgetCard);
        SetRef(so, "btnSetBudget", refs.BtnSetBudget);
        SetRef(so, "txtBudgetCardTitle", refs.TxtBudgetCardTitle);
        SetRef(so, "ringTrack", refs.RingTrack);
        SetRef(so, "ringFill", refs.RingFill);
        SetRef(so, "txtRingRemain", refs.TxtRingRemain);
        SetRef(so, "txtRingPercent", refs.TxtRingPercent);
        SetRef(so, "lbRemain", refs.LbRemain);
        SetRef(so, "lbBudget", refs.LbBudget);
        SetRef(so, "lbSpent", refs.LbSpent);
        SetRef(so, "txtRemain", refs.TxtRemain);
        SetRef(so, "txtBudget", refs.TxtBudget);
        SetRef(so, "txtSpent", refs.TxtSpent);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
    }

    private static void WireBillPanel(Transform canvasTf, PanelRefs refs, TMP_FontAsset font, AccountManager accountManager,
        UIManager uiManager, DetailPanelUI detailPanel)
    {
        GameObject panel = canvasTf.Find("BillPanel").gameObject;
        BillPanelUI ui = panel.GetComponent<BillPanelUI>();

        if (ui == null)
        {
            ui = panel.AddComponent<BillPanelUI>();
        }

        SerializedObject so = new SerializedObject(ui);
        SetRef(so, "accountManager", accountManager);
        SetRef(so, "uiManager", uiManager);
        SetRef(so, "detailPanelUI", detailPanel);
        SetRef(so, "labelFont", font);
        SetRef(so, "btnBack", refs.BtnBillBack);
        SetRef(so, "btnMonth", refs.BtnMonth);
        SetRef(so, "imgMonthBg", refs.ImgMonthBg);
        SetRef(so, "lbMonth", refs.LbMonth);
        SetRef(so, "btnYear", refs.BtnYear);
        SetRef(so, "imgYearBg", refs.ImgYearBg);
        SetRef(so, "lbYear", refs.LbYear);
        SetRef(so, "btnYearPicker", refs.BtnYearPicker);
        SetRef(so, "txtYearLabel", refs.TxtYearLabel);
        SetRef(so, "yearPickerBlocker", refs.YearPickerBlocker);
        SetRef(so, "yearPickerPopup", refs.YearPickerPopup);
        SetRef(so, "yearPickerContent", refs.YearPickerContent);
        SetRef(so, "txtCaption", refs.TxtCaption);
        SetRef(so, "txtValue", refs.TxtValue);
        SetRef(so, "txtIncome", refs.TxtIncome);
        SetRef(so, "txtExpense", refs.TxtExpense);

        SerializedProperty headersProp = so.FindProperty("headerLabels");
        headersProp.arraySize = 4;

        for (int i = 0; i < 4; i++)
        {
            headersProp.GetArrayElementAtIndex(i).objectReferenceValue = refs.HeaderLabels[i];
        }

        SetRef(so, "listContent", refs.BillListContent);
        SetRef(so, "emptyLabel", refs.BillEmptyLabel);
        SetRef(so, "footerNote", refs.FooterNote);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
    }

    private static void WireBudgetPanel(Transform canvasTf, PanelRefs refs, TMP_FontAsset font, AccountManager accountManager,
        BudgetManager budgetManager, UIManager uiManager)
    {
        GameObject panel = canvasTf.Find("BudgetPanel").gameObject;
        BudgetPanelUI ui = panel.GetComponent<BudgetPanelUI>();

        if (ui == null)
        {
            ui = panel.AddComponent<BudgetPanelUI>();
        }

        SerializedObject so = new SerializedObject(ui);
        SetRef(so, "accountManager", accountManager);
        SetRef(so, "budgetManager", budgetManager);
        SetRef(so, "uiManager", uiManager);
        SetRef(so, "labelFont", font);
        SetRef(so, "btnBack", refs.BtnBudgetBack);
        SetRef(so, "btnModeTitle", refs.BtnModeTitle);
        SetRef(so, "txtModeTitle", refs.TxtModeTitle);
        SetRef(so, "modeBlocker", refs.ModeBlocker);
        SetRef(so, "modePopup", refs.ModePopup);
        SetRef(so, "btnPickMonth", refs.BtnPickMonth);
        SetRef(so, "btnPickYear", refs.BtnPickYear);
        SetRef(so, "totalCard", refs.TotalCard);
        SetRef(so, "txtCardTitle", refs.TxtCardTitle);
        SetRef(so, "btnEdit", refs.BtnEdit);
        SetRef(so, "ringTrack", refs.RingTrack2);
        SetRef(so, "ringFill", refs.RingFill2);
        SetRef(so, "txtRingRemain", refs.TxtRingRemain2);
        SetRef(so, "txtRingPercent", refs.TxtRingPercent2);
        SetRef(so, "lbRemain", refs.LbRemain2);
        SetRef(so, "lbBudget", refs.LbBudget2);
        SetRef(so, "lbSpent", refs.LbSpent2);
        SetRef(so, "txtRemain", refs.TxtRemain2);
        SetRef(so, "txtBudget", refs.TxtBudget2);
        SetRef(so, "txtSpent", refs.TxtSpent2);
        SetRef(so, "emptyState", refs.EmptyState);
        SetRef(so, "btnEmptySet", refs.BtnEmptySet);
        SetRef(so, "labelFont", font);
        SetRef(so, "categoryArea", refs.CategoryArea);
        SetRef(so, "categoryEmpty", refs.CategoryEmpty);
        SetRef(so, "categoryContent", refs.CategoryContent);
        SetRef(so, "bottomAddBar", refs.BottomAddBar);
        SetRef(so, "btnAddCategory", refs.BtnAddCategory);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
    }

    private static void WireBudgetDialog(Transform canvasTf, PanelRefs refs, BudgetManager budgetManager, UIManager uiManager,
        CategoryIconProvider iconProvider, CategoryGridItem categoryPrefab)
    {
        GameObject dialog = canvasTf.Find("BudgetDialog").gameObject;
        BudgetDialogUI ui = dialog.GetComponent<BudgetDialogUI>();

        if (ui == null)
        {
            ui = dialog.AddComponent<BudgetDialogUI>();
        }

        SerializedObject so = new SerializedObject(ui);
        SetRef(so, "budgetManager", budgetManager);
        SetRef(so, "uiManager", uiManager);
        SetRef(so, "iconProvider", iconProvider);
        SetRef(so, "categoryItemPrefab", categoryPrefab);
        SetRef(so, "sheet", refs.Sheet);
        SetRef(so, "titleLabel", refs.TitleLabel);
        SetRef(so, "btnClose", refs.BtnClose);
        SetRef(so, "maskButton", refs.MaskButton);
        SetRef(so, "txtInput", refs.TxtInput);
        SetRef(so, "btnConfirm", refs.BtnConfirm);
        SetRef(so, "imgConfirm", refs.ImgConfirm);
        SetRef(so, "btnDeleteBudget", refs.BtnDeleteBudget);
        SetRef(so, "categoryScroll", refs.CategoryScroll);
        SetRef(so, "gridContent", refs.GridContent);

        SerializedProperty digitsProp = so.FindProperty("digitButtons");
        digitsProp.arraySize = 10;

        for (int i = 0; i < 10; i++)
        {
            digitsProp.GetArrayElementAtIndex(i).objectReferenceValue = refs.Digits[i];
        }

        SetRef(so, "dotButton", refs.Dot);
        SetRef(so, "backspaceButton", refs.Backspace);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
    }

    // ---------- 自检 ----------

    private static void RunSelfCheck(Transform canvasTf, UIManager uiManager, PanelRefs refs)
    {
        bool allPass = true;

        allPass &= Check("发现/账单/预算/弹窗四面板已搭", canvasTf.Find("DiscoverPanel") != null
            && canvasTf.Find("BillPanel") != null
            && canvasTf.Find("BudgetPanel") != null
            && canvasTf.Find("BudgetDialog") != null);

        DiscoverPanelUI discoverUi = canvasTf.Find("DiscoverPanel")?.GetComponent<DiscoverPanelUI>();
        allPass &= Check("发现页 DiscoverPanelUI 已挂且关键引用接线", discoverUi != null
            && new SerializedObject(discoverUi).FindProperty("billCardButton").objectReferenceValue == refs.BillCard
            && new SerializedObject(discoverUi).FindProperty("budgetCardButton").objectReferenceValue == refs.BudgetCard
            && new SerializedObject(discoverUi).FindProperty("ringFill").objectReferenceValue == refs.RingFill);

        BillPanelUI billUi = canvasTf.Find("BillPanel")?.GetComponent<BillPanelUI>();
        allPass &= Check("账单页 BillPanelUI 已挂且关键引用接线", billUi != null
            && new SerializedObject(billUi).FindProperty("listContent").objectReferenceValue == refs.BillListContent
            && new SerializedObject(billUi).FindProperty("btnYearPicker").objectReferenceValue == refs.BtnYearPicker);

        BudgetPanelUI budgetUi = canvasTf.Find("BudgetPanel")?.GetComponent<BudgetPanelUI>();
        allPass &= Check("预算页 BudgetPanelUI 已挂且关键引用接线", budgetUi != null
            && new SerializedObject(budgetUi).FindProperty("categoryContent").objectReferenceValue == refs.CategoryContent
            && new SerializedObject(budgetUi).FindProperty("btnModeTitle").objectReferenceValue == refs.BtnModeTitle);

        BudgetDialogUI dialogUi = canvasTf.Find("BudgetDialog")?.GetComponent<BudgetDialogUI>();
        SerializedProperty digitsProp = dialogUi != null ? new SerializedObject(dialogUi).FindProperty("digitButtons") : null;
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

        allPass &= Check("预算弹窗 BudgetDialogUI 已挂且 10 个数字键全部接线", dialogUi != null && digitsOk);

        allPass &= Check("BudgetManager/BudgetRepository 已就位",
            UnityEngine.Object.FindFirstObjectByType<BudgetManager>(FindObjectsInactive.Include) != null
            && UnityEngine.Object.FindFirstObjectByType<BudgetRepository>(FindObjectsInactive.Include) != null);

        SerializedObject uiSo = new SerializedObject(uiManager);
        allPass &= Check("UIManager 发现/账单/预算/弹窗四引用已接线",
            uiSo.FindProperty("discoverPanel").objectReferenceValue != null
            && uiSo.FindProperty("billPanel").objectReferenceValue != null
            && uiSo.FindProperty("budgetPanel").objectReferenceValue != null
            && uiSo.FindProperty("budgetDialog").objectReferenceValue != null);

        int billIdx = canvasTf.Find("BillPanel") != null ? canvasTf.Find("BillPanel").GetSiblingIndex() : -1;
        int budgetIdx = canvasTf.Find("BudgetPanel") != null ? canvasTf.Find("BudgetPanel").GetSiblingIndex() : -1;
        int exportIdx = canvasTf.Find("ExportPanel") != null ? canvasTf.Find("ExportPanel").GetSiblingIndex() : int.MaxValue;
        int importIdx = canvasTf.Find("ImportPanel") != null ? canvasTf.Find("ImportPanel").GetSiblingIndex() : int.MaxValue;
        int dialogIdx = canvasTf.Find("BudgetDialog") != null ? canvasTf.Find("BudgetDialog").GetSiblingIndex() : -1;
        int navIdx = canvasTf.Find("BottomNav") != null ? canvasTf.Find("BottomNav").GetSiblingIndex() : -1;
        int toastIdx = canvasTf.Find("Toast") != null ? canvasTf.Find("Toast").GetSiblingIndex() : int.MaxValue;
        int confirmIdx = canvasTf.Find("ConfirmDialog") != null ? canvasTf.Find("ConfirmDialog").GetSiblingIndex() : int.MaxValue;
        allPass &= Check("层级顺序 主面板(含账单/预算) < 导出/导入/预算弹窗 < 底栏 < Toast < ConfirmDialog",
            budgetIdx < exportIdx && exportIdx <= importIdx && importIdx < dialogIdx
            && dialogIdx < navIdx && navIdx < toastIdx && toastIdx < confirmIdx && billIdx < budgetIdx);

        allPass &= Check("落盘显隐：明细页开，账单/预算/弹窗全关",
            canvasTf.Find("DetailPanel").gameObject.activeSelf == true
            && canvasTf.Find("BillPanel").gameObject.activeSelf == false
            && canvasTf.Find("BudgetPanel").gameObject.activeSelf == false
            && canvasTf.Find("BudgetDialog").gameObject.activeSelf == false
            && canvasTf.Find("DiscoverPanel").gameObject.activeSelf == false);

        Debug.Log($"[DiscoverBuild] ===== 发现页功能搭建完成，自检 {(allPass ? "通过" : "失败(见上方 [DiscoverBuild] FAIL 日志)")} =====");
    }

    private static bool Check(string title, bool condition)
    {
        Debug.Log($"[DiscoverBuild] {(condition ? "PASS" : "FAIL")} - {title}");
        return condition;
    }

    // ---------- 素材生成 ----------

    /// <summary>
    /// 导入设置统一收口：Sprite + Single（Unity 6 默认可能落 Multiple，无切片时 Load<Sprite> 为 null）
    /// + 9-slice 边框（可选）。枚举名各版本有差异，用 SerializedObject 直设 m_SpriteMode。
    /// </summary>
    private static void ApplySpriteImportSettings(string path, Vector4? border)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
        {
            Debug.LogError("[DiscoverBuild] 拿不到 TextureImporter：" + path);
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.mipmapEnabled = false;

        SerializedObject importerSo = new SerializedObject(importer);
        SerializedProperty modeProp = importerSo.FindProperty("m_SpriteMode");

        if (modeProp != null)
        {
            modeProp.intValue = 1;   // Single
        }

        importerSo.ApplyModifiedPropertiesWithoutUndo();

        if (border.HasValue)
        {
            importer.spriteBorder = border.Value;
        }

        importer.SaveAndReimport();
    }

    /// <summary>
    /// 同步导入后取 Sprite（Unity 6 异步导入管线下 SaveAndReimport 后立即 Load 可能拿 null）
    /// </summary>
    private static Sprite LoadSpriteSync(string path)
    {
        Sprite loaded = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (loaded == null)
        {
            AssetDatabase.ImportAsset(path,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            loaded = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        if (loaded == null)
        {
            Debug.LogError("[DiscoverBuild] Sprite 导入后仍不可用（重跑一次菜单通常可恢复）：" + path);
        }

        return loaded;
    }

    /// <summary>
    /// 圆角白卡 sprite（9-slice，边框 36）：卡片/弹窗/胶囊底
    /// </summary>
    private static Sprite EnsureRoundedSprite()
    {
        Sprite loaded = LoadSpriteSync(RoundedSpritePath);

        if (loaded != null)
        {
            return loaded;
        }

        const int size = 128;
        const float radius = 36f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x, radius, size - radius);
                float cy = Mathf.Clamp(y, radius, size - radius);
                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float alpha = Mathf.Clamp01(radius - dist + 1f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        Directory.CreateDirectory("Assets/Art/Generated");
        File.WriteAllBytes(RoundedSpritePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);

        ApplySpriteImportSettings(RoundedSpritePath, new Vector4(radius, radius, radius, radius));
        Debug.Log("[DiscoverBuild] 已生成圆角卡 sprite：Assets/Art/Generated/rounded.png");
        return LoadSpriteSync(RoundedSpritePath);
    }

    /// <summary>
    /// 空态文档图标（参考图"暂无预算/未设置分类预算"线稿风）：浅灰圆角纸 + 三条横线
    /// </summary>
    private static Sprite EnsureDocIconSprite()
    {
        Sprite loaded = LoadSpriteSync(DocIconSpritePath);

        if (loaded != null)
        {
            return loaded;
        }

        const int size = 256;
        const float radius = 40f;
        const float lineWidth = 12f;
        Color ink = new Color(0.78f, 0.78f, 0.78f, 1f);
        Color[] pixels = new Color[size * size];

        bool InRoundedRect(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            float cx = Mathf.Clamp(x, x0 + r, x1 - r);
            float cy = Mathf.Clamp(y, y0 + r, y1 - r);
            float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            bool insideOuter = dist <= r + 0.5f || (x >= x0 && x <= x1 && y >= y0 && y <= y1 && dist <= r);
            return insideOuter;
        }

        // 外纸轮廓：外圈圆角矩形边框（线宽 lineWidth），内部镂空
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float outerX0 = 40f;
                float outerY0 = 24f;
                float outerX1 = 216f;
                float outerY1 = 232f;

                bool inOuterBorder = InRoundedRect(px, py, outerX0, outerY0, outerX1, outerY1, radius)
                    && !InRoundedRect(px, py, outerX0 + lineWidth, outerY0 + lineWidth,
                        outerX1 - lineWidth, outerY1 - lineWidth, radius - lineWidth);

                bool inLine = false;

                if (inOuterBorder == false)
                {
                    float[] lineYs = { 92f, 128f, 164f };

                    foreach (float lineY in lineYs)
                    {
                        if (px >= 76f && px <= 180f && py >= lineY - lineWidth / 2f && py <= lineY + lineWidth / 2f)
                        {
                            inLine = true;
                            break;
                        }
                    }
                }

                pixels[y * size + x] = inOuterBorder || inLine ? ink : new Color(0f, 0f, 0f, 0f);
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();

        Directory.CreateDirectory("Assets/Art/Generated");
        File.WriteAllBytes(DocIconSpritePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);

        ApplySpriteImportSettings(DocIconSpritePath, null);
        Debug.Log("[DiscoverBuild] 已生成空态文档图标：Assets/Art/Generated/icon_doc.png");
        return LoadSpriteSync(DocIconSpritePath);
    }

    /// <summary>
    /// 预固化发现页全套界面用字进 Dynamic 图集并落盘（冷启动防方块，步骤02-08 同款；含全部分类名）
    /// </summary>
    private static void PreSolidifyGlyphs(TMP_FontAsset font)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("发现账单收入支出结余月年总预算剩设置编辑添加分类删除确定请输入金额返回退格暂无未比为本支笔份自然");
        sb.Append("每月年度0123456789.%+-()（）");
        sb.Append("＜＞×▼￥");

        List<CategoryTable.CategoryDef> defs = CategoryTable.All;

        for (int i = 0; i < defs.Count; i++)
        {
            sb.Append(defs[i].Name);
        }

        string chars = sb.ToString();

        if (font.TryAddCharacters(chars))
        {
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            Debug.Log("[DiscoverBuild] 已预固发现页用字（" + chars.Length + " 字符）。");
        }
        else
        {
            Debug.LogWarning("[DiscoverBuild] 部分字形未能预固化（运行时动态添加仍会兜底），可用 FontProbe 复测。");
        }
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

    /// <summary>
    /// CreateRect 的 GameObject 版（声明处直接拿 GameObject 用）
    /// </summary>
    private static GameObject CreateRectGo(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        return CreateRect(parent, name, anchorMin, anchorMax, pivot).gameObject;
    }

    /// <summary>
    /// 主面板壳（与 15 号同构）：ColBackground 底 + PanelContent(底 170 让位导航) + TopBar 460 黄条
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
    /// 子页顶栏：返回钮（左 ＜）+ 居中标题（title 传空 = 标题由调用方自建，如预算页"月预算 ▼"）
    /// </summary>
    private static void BuildSubPageTopBar(RectTransform content, TMP_FontAsset font, string title, out Button btnBack)
    {
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

        GameObject back = CreateRectGo(topContent, "BtnBack",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        back.GetComponent<RectTransform>().sizeDelta = new Vector2(140f, 110f);
        back.GetComponent<RectTransform>().anchoredPosition = new Vector2(CardMargin, -8f);
        Image backImage = back.AddComponent<Image>();
        backImage.color = new Color(0f, 0f, 0f, 0f);
        btnBack = back.AddComponent<Button>();
        btnBack.targetGraphic = backImage;

        CreateLabel(back.transform, "Label", "＜", font, 52f, ColBlack, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        if (!string.IsNullOrEmpty(title))
        {
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
        }
    }

    /// <summary>
    /// 白底圆角卡（9-slice），带 Button：顶部居中点锚，sizeDelta 即实际尺寸
    /// （宽 = 参考宽 1080 - 左右各 48 边距；点锚下 offsetMin/Max 不产生拉伸）
    /// </summary>
    private static GameObject CreateCard(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, float y, float x, float height, Sprite rounded)
    {
        GameObject card = CreateRectGo(parent, name, anchorMin, anchorMax, pivot);
        RectTransform cardRect = (RectTransform)card.transform;
        cardRect.pivot = new Vector2(0.5f, 1f);
        cardRect.sizeDelta = new Vector2(1080f - CardMargin * 2f, height);
        cardRect.anchoredPosition = new Vector2(0f, y);

        Image bg = card.AddComponent<Image>();
        bg.sprite = rounded;
        bg.type = Image.Type.Sliced;
        bg.color = ColWhite;
        bg.raycastTarget = true;

        card.AddComponent<Button>();
        return card;
    }

    private static RingGraphic CreateRing(Transform parent, string name, Color color, float thickness, float progress)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        RingGraphic ring = go.AddComponent<RingGraphic>();
        ring.color = color;
        ring.Thickness = thickness;
        ring.Progress = progress;
        ring.raycastTarget = false;
        return ring;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, TMP_FontAsset font,
        float fontSize, Color color, TextAlignmentOptions alignment,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void SetFitterMode(SafeAreaFitter fitter, SafeAreaFitter.Mode mode)
    {
        SerializedObject so = new SerializedObject(fitter);
        so.FindProperty("mode").enumValueIndex = (int)mode;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetRef(SerializedObject so, string fieldName, UnityEngine.Object value)
    {
        SerializedProperty prop = so.FindProperty(fieldName);

        if (prop != null)
        {
            prop.objectReferenceValue = value;
        }
        else
        {
            Debug.LogError("[DiscoverBuild] 接线失败：字段不存在 " + fieldName);
        }
    }
}
