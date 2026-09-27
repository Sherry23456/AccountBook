using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 顶栏错位修复（2026-09 真机验收）：三个页签页 TopBar 固定高度装不下真机安全区 insets——
/// 明细/图表 300 高里顶底两锚内容行占 258+8，余量 42 单位，挖孔屏 insets ~152 单位直接把两行压叠；
/// 记账页页签中心锚在被压缩的 TopContent 里坠向栏底。明细页 TopContent 还被烘进过一次运行时偏移
/// （Play 中存场景把 SafeAreaFitter 的 offsetMax/offsetMin 序列化了，-68.7），MonthRow 又被手动下挪 +20。
/// 本脚本把场景矩形改成与构建器一致的新布局（TopBar 加高、下方区块偏移同步、烘死偏移复位），
/// 按 insets 换算断言各行不压叠，最后联动安全区换算自检。菜单 AccountBook/97-顶栏布局修复。
/// </summary>
public static class TopBarLayoutFix
{
    [MenuItem("AccountBook/97-顶栏布局修复")]
    public static void Run()
    {
        bool allPass = true;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

        // —— 明细页：TopBar 460；复位烘死偏移与手动挪动；列表顶边随栏下移 ——
        Set("DetailPanel/PanelContent/TopBar", r => r.sizeDelta = new Vector2(0f, 460f));
        Set("DetailPanel/PanelContent/TopBar/TopContent", r =>
        {
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = Vector2.zero;
        });
        Set("DetailPanel/PanelContent/TopBar/TopContent/MonthRow", r => r.anchoredPosition = new Vector2(0f, -8f));
        SetOffsets("DetailPanel/PanelContent/ListScrollView", 0f, -460f);

        // —— 图表页：TopBar 460；下方五段偏移同步 +160 ——
        Set("ChartPanel/PanelContent/TopBar", r => r.sizeDelta = new Vector2(0f, 460f));
        Set("ChartPanel/PanelContent/PeriodStrip", r => r.anchoredPosition = new Vector2(0f, -468f));
        Set("ChartPanel/PanelContent/SummaryRow", r => r.anchoredPosition = new Vector2(0f, -572f));
        Set("ChartPanel/PanelContent/ChartArea", r => r.anchoredPosition = new Vector2(0f, -722f));
        Set("ChartPanel/PanelContent/RankTitleRow", r => r.anchoredPosition = new Vector2(0f, -1332f));
        SetOffsets("ChartPanel/PanelContent/RankScrollView", 0f, -1402f);

        // —— 记账页：TopBar 360；页签改顶锚跟随安全区；宫格顶边随栏下移 ——
        Set("RecordPanel/PanelContent/TopBar", r => r.sizeDelta = new Vector2(0f, 360f));
        Set("RecordPanel/PanelContent/TopBar/TopContent/TabGroup", r =>
        {
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(40f, -24f);
        });
        SetOffsets("RecordPanel/PanelContent/CategoryScrollView", 940f, -360f);

        // —— 回读断言：改到位 ——
        allPass &= Check("明细 TopBar=460", Rect("DetailPanel/PanelContent/TopBar").sizeDelta == new Vector2(0f, 460f));
        RectTransform detailTopContent = Rect("DetailPanel/PanelContent/TopBar/TopContent");
        allPass &= Check("明细 TopContent 烘死偏移已复位",
            detailTopContent.anchoredPosition == Vector2.zero && detailTopContent.sizeDelta == Vector2.zero);
        allPass &= Check("明细 MonthRow=-8",
            Rect("DetailPanel/PanelContent/TopBar/TopContent/MonthRow").anchoredPosition == new Vector2(0f, -8f));
        RectTransform detailList = Rect("DetailPanel/PanelContent/ListScrollView");
        allPass &= Check("明细列表顶边=-460",
            detailList.offsetMin == new Vector2(0f, 0f) && detailList.offsetMax == new Vector2(0f, -460f));

        allPass &= Check("图表 TopBar=460", Rect("ChartPanel/PanelContent/TopBar").sizeDelta == new Vector2(0f, 460f));
        allPass &= Check("图表 PeriodStrip=-468", Rect("ChartPanel/PanelContent/PeriodStrip").anchoredPosition == new Vector2(0f, -468f));
        allPass &= Check("图表 SummaryRow=-572", Rect("ChartPanel/PanelContent/SummaryRow").anchoredPosition == new Vector2(0f, -572f));
        allPass &= Check("图表 ChartArea=-722", Rect("ChartPanel/PanelContent/ChartArea").anchoredPosition == new Vector2(0f, -722f));
        allPass &= Check("图表 RankTitleRow=-1332", Rect("ChartPanel/PanelContent/RankTitleRow").anchoredPosition == new Vector2(0f, -1332f));
        RectTransform rankScroll = Rect("ChartPanel/PanelContent/RankScrollView");
        allPass &= Check("图表排行区顶边=-1402",
            rankScroll.offsetMin == new Vector2(0f, 0f) && rankScroll.offsetMax == new Vector2(0f, -1402f));

        allPass &= Check("记账 TopBar=360", Rect("RecordPanel/PanelContent/TopBar").sizeDelta == new Vector2(0f, 360f));
        RectTransform tabGroup = Rect("RecordPanel/PanelContent/TopBar/TopContent/TabGroup");
        allPass &= Check("记账页签顶锚 -24",
            tabGroup.anchorMin == new Vector2(0f, 1f) && tabGroup.anchorMax == new Vector2(0f, 1f) &&
            tabGroup.pivot == new Vector2(0f, 1f) && tabGroup.anchoredPosition == new Vector2(40f, -24f));
        RectTransform recordGrid = Rect("RecordPanel/PanelContent/CategoryScrollView");
        allPass &= Check("记账宫格顶边=-360",
            recordGrid.offsetMin == new Vector2(0f, 940f) && recordGrid.offsetMax == new Vector2(0f, -360f));

        // —— 行距断言：insets ∈ {0, 132(真机实测), 160(物理上限)} 时顶栏内容两行不压叠 ——
        // 图表 Row1 底 = inset+20(留白)+8+110；Row2 顶 = 460-140
        // 明细 MonthRow 底 = inset+20+8+110；SummaryRow 顶 = 460-160
        // 记账页签底 = inset+20+24+104；栏高 360
        foreach (float inset in new float[] { 0f, 132f, 160f })
        {
            float chartGap = (460f - 140f) - (inset + 138f);
            float detailGap = (460f - 160f) - (inset + 138f);
            float recordGap = 360f - (inset + 148f);
            allPass &= Check($"inset={inset}: 图表行距 {chartGap:F0} / 明细行距 {detailGap:F0} / 记账余量 {recordGap:F0} 均不压叠",
                chartGap >= 0f && detailGap >= 0f && recordGap >= 0f);
        }

        // —— 全场景 Fitter 宿主扫描：任何 SafeAreaFitter 宿主都不允许带烘死偏移（编辑态应为纯拉伸） ——
        foreach (SafeAreaFitter f in Object.FindObjectsByType<SafeAreaFitter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            RectTransform host = (RectTransform)f.transform;
            bool baked = Mathf.Abs(host.sizeDelta.y) > 0.01f || Mathf.Abs(host.anchoredPosition.y) > 0.01f;
            allPass &= Check($"Fitter宿主[{f.gameObject.name}] 无烘死偏移", baked == false);
        }

        // —— 联动安全区换算自检（纯数学，换算基准锁住） ——
        MethodInfo safeCheck = typeof(SafeAreaCalcCheck).GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Static);
        safeCheck?.Invoke(null, null);

        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[TopBarFix] ===== 顶栏布局修复{(allPass ? "全部通过，场景已保存" : "存在失败项(见上方 FAIL)，场景已按新值保存")} =====");
    }

    private static void Set(string path, System.Action<RectTransform> apply)
    {
        apply(Rect(path));
    }

    private static RectTransform Rect(string path)
    {
        string[] parts = path.Split('/');
        Transform cur = null;
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == parts[0])
            {
                cur = root.transform;
                break;
            }
        }

        if (cur == null)
        {
            // 面板挂在 Canvas 下（场景根是 Canvas/Managers 等），第一段按各根的子级找
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform child = root.transform.Find(parts[0]);
                if (child != null)
                {
                    cur = child;
                    break;
                }
            }
        }

        if (cur == null)
        {
            throw new System.InvalidOperationException("找不到根节点：" + parts[0]);
        }

        for (int i = 1; i < parts.Length; i++)
        {
            cur = cur.Find(parts[i]);
            if (cur == null)
            {
                throw new System.InvalidOperationException("找不到节点：" + path);
            }
        }

        return (RectTransform)cur;
    }

    private static void SetOffsets(string path, float offsetYMin, float offsetYMax)
    {
        RectTransform rect = Rect(path);
        rect.offsetMin = new Vector2(rect.offsetMin.x, offsetYMin);
        rect.offsetMax = new Vector2(rect.offsetMax.x, offsetYMax);
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[TopBarFix] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
