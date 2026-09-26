using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 步骤 01 一键搭建主场景：重命名场景为 Main、建 Managers/Canvas/五面板/BottomNav/EventSystem、
/// 生成 Dynamic TMP 字体、批量导入分类图标为 Sprite、绑定全部序列化引用、设置竖屏与安全区。
/// 编译完成后自动执行一次（EditorPrefs 记忆），也可用菜单 AccountBook/01-搭建主场景 Main 手动重跑。
/// </summary>
public static class MainSceneBuilder
{
    private const string MenuItemPath = "AccountBook/01-搭建主场景 Main";
    private const string AutoRunSessionKey = "AccountBook.MainSceneBuilder.SessionRan";
    private const string BuiltPrefKey = "AccountBook.MainSceneBuilt";
    private const string FontAssetPath = "Assets/Fonts/STKAITI Dynamic SDF.asset";
    private const string SourceFontPath = "Assets/Fonts/STKAITI.TTF";
    private const string CircleSpritePath = "Assets/Art/Generated/circle.png";

    // 色板（步骤00 §2.4）
    private static readonly Color ColYellow = FromHex(0xFFD100);
    private static readonly Color ColBlack = FromHex(0x222222);
    private static readonly Color ColGray = FromHex(0x666666);
    private static readonly Color ColDivider = FromHex(0xEEEEEE);
    private static readonly Color ColBackground = FromHex(0xF7F7F7);
    private static readonly Color ColWhite = FromHex(0xFFFFFF);
    private static readonly Color ColMask = new Color(0f, 0f, 0f, 0.55f);

    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        if (SessionState.GetBool(AutoRunSessionKey, false))
        {
            return;
        }

        SessionState.SetBool(AutoRunSessionKey, true);

        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(BuiltPrefKey, false))
            {
                return;
            }

            try
            {
                BuildMainScene();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MainSceneBuilder] 自动搭建失败：{ex}\n可用菜单 {MenuItemPath} 重跑。");
            }
        };
    }

    [MenuItem(MenuItemPath)]
    private static void RunFromMenu()
    {
        BuildMainScene();
    }

    /// <summary>
    /// 主入口：字体/图标 → 场景 → 接线 → 数据自检 → MCP 桥
    /// </summary>
    private static void BuildMainScene()
    {
        Debug.Log("[MainSceneBuilder] ===== 步骤01 主场景搭建开始 =====");

        EnsureIconsAreSprites();
        TMP_FontAsset fontAsset = LoadOrCreateDynamicFontAsset();
        SetTmpDefaultFont(fontAsset);

        List<Sprite> iconSprites = LoadIconSprites();
        Sprite circleSprite = CreateCircleSprite();
        Dictionary<string, Sprite> auxSprites = new Dictionary<string, Sprite>();
        auxSprites["circle"] = circleSprite;

        BuildScene(fontAsset, iconSprites, auxSprites);

        bool selfCheckPass = DataLayerSelfCheck.Run();

        EnsureMcpHttpBridge();

        EditorPrefs.SetBool(BuiltPrefKey, true);

        Debug.Log($"[MainSceneBuilder] ===== 步骤01 主场景搭建完成，数据层自检 {(selfCheckPass ? "通过" : "失败(见上方 [SelfCheck] 日志)")} =====");
        Debug.Log("[MainSceneBuilder] 运行验收：按 Play 后点底栏三按钮切换面板；输入生僻字'赟'验证 Dynamic 字体；Game 视图 16:9 与 21:9 各看一遍安全区。");
    }

    // ---------- 资源准备 ----------

    /// <summary>
    /// Art/Pictures 全部贴图批量改为 Sprite (2D and UI)
    /// </summary>
    private static void EnsureIconsAreSprites()
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Pictures" });
        int changed = 0;

        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                continue;
            }

            if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                changed++;
            }
        }

        Debug.Log($"[MainSceneBuilder] 图标 Sprite 化完成，共 {textureGuids.Length} 张（本次改动 {changed} 张）。");
    }

    /// <summary>
    /// 加载全部分类图标 sprite
    /// </summary>
    private static List<Sprite> LoadIconSprites()
    {
        List<Sprite> result = new List<Sprite>();
        string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { "Assets/Art/Pictures" });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite != null)
            {
                result.Add(sprite);
            }
        }

        return result;
    }

    /// <summary>
    /// 确保 Dynamic 模式 TMP 字体资产存在（静态图集装不全 7264 字，必须 Dynamic）
    /// </summary>
    private static TMP_FontAsset LoadOrCreateDynamicFontAsset()
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        if (existing != null)
        {
            if (existing.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            {
                existing.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                existing.isMultiAtlasTexturesEnabled = true;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                Debug.Log("[MainSceneBuilder] 已把既有 STKAITI Dynamic SDF 切回 Dynamic 模式。");
            }

            return existing;
        }

        // 残缺文件清理
        if (File.Exists(FontAssetPath))
        {
            AssetDatabase.DeleteAsset(FontAssetPath);
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);

        if (sourceFont == null)
        {
            throw new FileNotFoundException("找不到源字体 " + SourceFontPath + "，请确认 Fonts 目录。");
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont, 64, 8, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, true);

        AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
        AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

        fontAsset.atlasTexture.name = "STKAITI Dynamic SDF Atlas";
        fontAsset.material.name = "STKAITI Dynamic SDF Material";
        fontAsset.isMultiAtlasTexturesEnabled = true;

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();

        TMP_FontAsset reloaded = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        if (reloaded == null || reloaded.atlasTexture == null || reloaded.material == null)
        {
            throw new InvalidOperationException("TMP 字体资产创建异常（图集/材质缺失），请用菜单 Window/TextMeshPro/Font Asset Creator 手动生成 Dynamic 资产。");
        }

        Debug.Log($"[MainSceneBuilder] 已创建 Dynamic 字体资产：{FontAssetPath}（64pt/1024²/多图集）。");
        return reloaded;
    }

    /// <summary>
    /// 所有 TMP 文本默认字体切到 Dynamic 资产（defaultFontAsset 只读，走 SerializedObject）
    /// </summary>
    private static void SetTmpDefaultFont(TMP_FontAsset fontAsset)
    {
        string[] settingGuids = AssetDatabase.FindAssets("t:TMP_Settings");

        if (settingGuids.Length == 0)
        {
            Debug.LogWarning("[MainSceneBuilder] 找不到 TMP Settings 资产，默认字体未切换。");
            return;
        }

        string settingsPath = AssetDatabase.GUIDToAssetPath(settingGuids[0]);
        TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath);

        if (settings == null || TMP_Settings.defaultFontAsset == fontAsset)
        {
            return;
        }

        SerializedObject so = new SerializedObject(settings);
        so.FindProperty("m_defaultFontAsset").objectReferenceValue = fontAsset;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log("[MainSceneBuilder] TMP Settings 默认字体已切换为 Dynamic 资产。");
    }

    /// <summary>
    /// 生成记账按钮用的白色圆形 sprite（抗锯齿）
    /// </summary>
    private static Sprite CreateCircleSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);

        if (existing != null)
        {
            return existing;
        }

        const int size = 128;
        float radius = size / 2f - 2f;
        float center = (size - 1) / 2f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                float alpha = Mathf.Clamp01(radius - dist + 1f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        Directory.CreateDirectory("Assets/Art/Generated");
        File.WriteAllBytes(CircleSpritePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(CircleSpritePath);
        TextureImporter importer = AssetImporter.GetAtPath(CircleSpritePath) as TextureImporter;

        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        Debug.Log("[MainSceneBuilder] 已生成圆形按钮 sprite：Assets/Art/Generated/circle.png");
        return AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
    }

    // ---------- 场景搭建 ----------

    private static void BuildScene(TMP_FontAsset font, List<Sprite> iconSprites, Dictionary<string, Sprite> auxSprites)
    {
        // 1) 场景改名 SampleScene → Main（先切到空场景避免"资源正在打开"冲突）
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string moveError = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", "Assets/Scenes/Main.unity");

            if (!string.IsNullOrEmpty(moveError))
            {
                throw new InvalidOperationException("场景改名失败：" + moveError);
            }

            Debug.Log("[MainSceneBuilder] SampleScene.unity 已改名为 Main.unity。");
        }

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

        // 2) 清理旧节点（重复执行时幂等），保留 Main Camera / Directional Light
        foreach (string nodeName in new[] { "Canvas", "Managers", "EventSystem" })
        {
            GameObject old = GameObject.Find(nodeName);

            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old);
            }
        }

        // 3) Managers 物体 + 五层组件
        GameObject managers = new GameObject("Managers");
        JsonFileService jsonFileService = managers.AddComponent<JsonFileService>();
        AccountRepository accountRepository = managers.AddComponent<AccountRepository>();
        AccountManager accountManager = managers.AddComponent<AccountManager>();
        UIManager uiManager = managers.AddComponent<UIManager>();
        AppFlowManager appFlowManager = managers.AddComponent<AppFlowManager>();
        CategoryIconProvider iconProvider = managers.AddComponent<CategoryIconProvider>();

        // 4) Canvas + EventSystem
        GameObject canvasGo = new GameObject("Canvas");
        RectTransform canvasRect = canvasGo.AddComponent<RectTransform>();
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2340f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;   // 按宽度缩放（步骤00 §2.4）
        canvasGo.AddComponent<GraphicRaycaster>();

        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // 5) 面板（创建顺序即绘制顺序：明细 < 图表 < 记账 < 导出 < 导入 < 底栏）
        GameObject detailPanel = BuildMainPanel(canvasRect.transform, "DetailPanel", "明细", font, ColBackground);
        GameObject chartPanel = BuildMainPanel(canvasRect.transform, "ChartPanel", "图表", font, ColBackground);
        GameObject recordPanel = BuildMainPanel(canvasRect.transform, "RecordPanel", "记账", font, ColBackground);
        GameObject exportPanel = BuildPopupShell(canvasRect.transform, "ExportPanel", "导出数据", "步骤 05 填充：范围/周期/预览/导出按钮", font, uiManager.CloseExportPanel);
        GameObject importPanel = BuildPopupShell(canvasRect.transform, "ImportPanel", "导入数据", "步骤 06 填充：选择文件/预解析/确认导入", font, uiManager.CloseImportPanel);
        GameObject bottomNav = BuildBottomNav(canvasRect.transform, font, auxSprites["circle"], out Button btnDetail, out Button btnRecord, out Button btnChart);

        detailPanel.SetActive(true);
        chartPanel.SetActive(false);
        recordPanel.SetActive(false);
        exportPanel.SetActive(false);
        importPanel.SetActive(false);
        bottomNav.SetActive(true);

        // 6) 序列化引用接线
        SetObjectReference(accountRepository, "jsonFileService", jsonFileService);
        SetObjectReference(accountManager, "accountRepository", accountRepository);
        SetObjectReference(iconProvider, "sprites", iconSprites);

        SerializedObject soUi = new SerializedObject(uiManager);
        soUi.FindProperty("detailPanel").objectReferenceValue = detailPanel;
        soUi.FindProperty("chartPanel").objectReferenceValue = chartPanel;
        soUi.FindProperty("recordPanel").objectReferenceValue = recordPanel;
        soUi.FindProperty("exportPanel").objectReferenceValue = exportPanel;
        soUi.FindProperty("importPanel").objectReferenceValue = importPanel;
        soUi.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject soFlow = new SerializedObject(appFlowManager);
        soFlow.FindProperty("btnDetail").objectReferenceValue = btnDetail;
        soFlow.FindProperty("btnRecord").objectReferenceValue = btnRecord;
        soFlow.FindProperty("btnChart").objectReferenceValue = btnChart;
        soFlow.ApplyModifiedPropertiesWithoutUndo();

        // 7) 保存场景 + Build Settings + 竖屏/安全区
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.Android.renderOutsideSafeArea = true;

        Debug.Log("[MainSceneBuilder] 场景搭建与引用接线完成：Main.unity（明细/图表/记账/导出/导入 + 底栏三按钮）。");
    }

    /// <summary>
    /// 主页面骨架：全屏背景 + 黄色顶栏（背景不收，内容容器挂 SafeAreaFitter=Top）+ 标题占位
    /// </summary>
    private static GameObject BuildMainPanel(Transform parent, string panelName, string title, TMP_FontAsset font, Color bgColor)
    {
        GameObject panel = CreateStretchRect(parent, panelName);
        Image bg = panel.AddComponent<Image>();
        bg.color = bgColor;
        bg.raycastTarget = false;

        RectTransform topBar = CreateRect(panel.transform, "TopBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        topBar.sizeDelta = new Vector2(0f, 180f);
        Image topBarImage = topBar.gameObject.AddComponent<Image>();
        topBarImage.color = ColYellow;
        topBarImage.raycastTarget = false;

        RectTransform topContent = CreateRect(topBar.transform, "TopContent",
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        topContent.offsetMin = Vector2.zero;
        topContent.offsetMax = Vector2.zero;
        SafeAreaFitter fitter = topContent.gameObject.AddComponent<SafeAreaFitter>();
        SetEnumField(fitter, "mode", SafeAreaFitter.Mode.Top);

        CreateLabel(topContent, "Title", title, font, 48, ColBlack, TextAlignmentOptions.Center);

        return panel;
    }

    /// <summary>
    /// 弹窗壳：半透明黑遮罩（点击关闭）+ 白色对话框 + 标题/提示占位
    /// </summary>
    private static GameObject BuildPopupShell(Transform parent, string panelName, string title, string hint, TMP_FontAsset font, UnityAction closeAction)
    {
        GameObject panel = CreateStretchRect(parent, panelName);
        Image mask = panel.AddComponent<Image>();
        mask.color = ColMask;
        mask.raycastTarget = true;

        Button maskButton = panel.AddComponent<Button>();
        maskButton.targetGraphic = mask;
        UnityEventTools.AddVoidPersistentListener(maskButton.onClick, closeAction);

        RectTransform dialog = CreateRect(panel.transform, "Dialog",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        dialog.sizeDelta = new Vector2(620f, 420f);
        Image dialogImage = dialog.gameObject.AddComponent<Image>();
        dialogImage.color = ColWhite;
        dialogImage.raycastTarget = false;

        RectTransform titleArea = CreateRect(dialog.transform, "TitleArea",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        titleArea.sizeDelta = new Vector2(0f, 96f);
        titleArea.anchoredPosition = new Vector2(0f, -48f);

        CreateLabel(titleArea, "Title", title, font, 44, ColBlack, TextAlignmentOptions.Center);

        CreateLabel(dialog.transform, "Hint", hint, font, 32, ColGray, TextAlignmentOptions.Center);

        return panel;
    }

    /// <summary>
    /// 底部导航：白底 + 手势条适配（SafeAreaFitter=Bottom）+ 明细/大+记账/图表 三按钮
    /// </summary>
    private static GameObject BuildBottomNav(Transform parent, TMP_FontAsset font, Sprite circleSprite, out Button btnDetail, out Button btnRecord, out Button btnChart)
    {
        GameObject nav = CreateRect(parent, "BottomNav",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f)).gameObject;
        RectTransform navRect = nav.GetComponent<RectTransform>();
        navRect.sizeDelta = new Vector2(0f, 170f);

        Image navBg = nav.AddComponent<Image>();
        navBg.color = ColWhite;
        navBg.raycastTarget = false;

        // 顶部分割线
        RectTransform divider = CreateRect(nav.transform, "Divider",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        divider.sizeDelta = new Vector2(0f, 2f);
        divider.anchoredPosition = new Vector2(0f, -1f);
        Image dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.color = ColDivider;
        dividerImage.raycastTarget = false;

        // 手势条适配容器
        RectTransform navContent = CreateRect(nav.transform, "NavContent",
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        navContent.offsetMin = Vector2.zero;
        navContent.offsetMax = Vector2.zero;
        SafeAreaFitter fitter = navContent.gameObject.AddComponent<SafeAreaFitter>();
        SetEnumField(fitter, "mode", SafeAreaFitter.Mode.Bottom);

        // 左：明细
        btnDetail = CreateNavButton(navContent, "btnDetail", new Vector2(0f, 0f), new Vector2(0.33f, 1f), "明细", font);

        // 中：大 + 记账
        GameObject recordGo = CreateRect(navContent.transform, "btnRecord",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)).gameObject;
        RectTransform recordRect = recordGo.GetComponent<RectTransform>();
        recordRect.sizeDelta = new Vector2(140f, 140f);

        Image recordImage = recordGo.AddComponent<Image>();
        recordImage.sprite = circleSprite;
        recordImage.color = ColYellow;
        btnRecord = recordGo.AddComponent<Button>();
        btnRecord.targetGraphic = recordImage;

        CreateLabel(recordGo.transform, "Plus", "+", font, 72, ColBlack, TextAlignmentOptions.Center, new Vector2(0f, 14f));
        CreateLabel(recordGo.transform, "Label", "记账", font, 26, ColGray, TextAlignmentOptions.Center, new Vector2(0f, -38f));

        // 右：图表
        btnChart = CreateNavButton(navContent, "btnChart", new Vector2(0.67f, 0f), new Vector2(1f, 1f), "图表", font);

        return nav;
    }

    /// <summary>
    /// 底栏两侧平铺按钮（透明可点区 + 文字）
    /// </summary>
    private static Button CreateNavButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label, TMP_FontAsset font)
    {
        RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, new Vector2(0.5f, 0.5f));
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        CreateLabel(rect, "Label", label, font, 36, ColBlack, TextAlignmentOptions.Center, new Vector2(0f, -18f));

        return button;
    }

    // ---------- UI 工具 ----------

    private static GameObject CreateStretchRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
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
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        return rect;
    }

    private static TMP_Text CreateLabel(Transform parent, string name, string text, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions alignment, Vector2 anchoredPosition = default)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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

    // ---------- 序列化工具 ----------

    private static void SetObjectReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
    {
        SerializedObject so = new SerializedObject(target);
        so.FindProperty(fieldName).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectReference(UnityEngine.Object target, string fieldName, List<Sprite> values)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(fieldName);
        prop.arraySize = values.Count;

        for (int i = 0; i < values.Count; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
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

    // ---------- MCP 桥（尽力而为，失败不影响框架） ----------

    /// <summary>
    /// 8080 无监听时通过反射重启 MCP for Unity 本地 HTTP 服务，供后续步骤的编辑器联动
    /// </summary>
    private static void EnsureMcpHttpBridge()
    {
        try
        {
            if (IsPortOpen("127.0.0.1", 8080, 250))
            {
                Debug.Log("[MainSceneBuilder] MCP HTTP 桥已在 8080 监听，跳过重启。");
                return;
            }

            Assembly mcpAssembly = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "MCPForUnity.Editor")
                {
                    mcpAssembly = assembly;
                    break;
                }
            }

            if (mcpAssembly == null)
            {
                Debug.LogWarning("[MainSceneBuilder] 未找到 MCPForUnity.Editor 程序集，跳过桥重启。");
                return;
            }

            Type locatorType = mcpAssembly.GetType("MCPForUnity.Editor.Services.MCPServiceLocator");

            if (locatorType == null)
            {
                Debug.LogWarning("[MainSceneBuilder] 未找到 MCPServiceLocator，跳过桥重启。");
                return;
            }

            PropertyInfo serverProperty = locatorType.GetProperty("Server", BindingFlags.Public | BindingFlags.Static);
            object serverService = serverProperty != null ? serverProperty.GetValue(null) : null;

            if (serverService == null)
            {
                Debug.LogWarning("[MainSceneBuilder] MCPServiceLocator.Server 为空，跳过桥重启。");
                return;
            }

            MethodInfo startMethod = serverService.GetType().GetMethod("StartLocalHttpServer", new[] { typeof(bool) });

            if (startMethod == null)
            {
                Debug.LogWarning("[MainSceneBuilder] 未找到 StartLocalHttpServer 方法，跳过桥重启。");
                return;
            }

            startMethod.Invoke(serverService, new object[] { true });
            Debug.Log("[MainSceneBuilder] 已请求重启 MCP HTTP 桥（8080），窗口打开后稍候生效。");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MainSceneBuilder] MCP 桥重启失败（不影响框架搭建）：" + ex.Message);
        }
    }

    private static bool IsPortOpen(string host, int port, int timeoutMs)
    {
        try
        {
            using (System.Net.Sockets.TcpClient client = new System.Net.Sockets.TcpClient())
            {
                IAsyncResult asyncResult = client.BeginConnect(host, port, null, null);
                bool connected = asyncResult.AsyncWaitHandle.WaitOne(timeoutMs, false) && client.Connected;
                client.Close();
                return connected;
            }
        }
        catch
        {
            return false;
        }
    }
}
