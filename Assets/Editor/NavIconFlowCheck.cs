using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 步骤10 验收：底栏图标态自检（编辑器菜单可重跑，不自动运行）。
/// 自动进 Play → 依次点四个页签按钮与记账 → 校验各页签图标 Normal/Selected 与文字色 → 退出 Play。
/// 高亮规则：当前主面板页签=Selected 黑图黑字；记账页四页签全灰；发现子页(账单/预算)归发现。
/// </summary>
public static class NavIconFlowCheck
{
    private const string MenuItemPath = "AccountBook/17-底栏图标态自检";
    private const string StartedKey = "AccountBook.NavIconCheck.Started";
    private const string DoneKey = "AccountBook.NavIconCheck.Done";
    private const string IconDir = "Assets/Art/icon";

    [MenuItem(MenuItemPath)]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            DoCheck();
            return;
        }

        SessionState.SetBool(StartedKey, true);
        SessionState.SetBool(DoneKey, false);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void HookAfterReload()
    {
        if (SessionState.GetBool(StartedKey, false) && SessionState.GetBool(DoneKey, false) == false)
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += DoCheck;
        }
    }

    private static void DoCheck()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        try
        {
            AppFlowManager flow = UnityEngine.Object.FindFirstObjectByType<AppFlowManager>();
            BottomNavUI navUi = UnityEngine.Object.FindFirstObjectByType<BottomNavUI>();
            UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();

            if (flow == null || navUi == null || uiManager == null)
            {
                Debug.LogError("[NavIconCheck] FAIL - AppFlowManager/BottomNavUI/UIManager 未找到。");
                return;
            }

            SerializedObject soNav = new SerializedObject(navUi);
            Image detailIcon = soNav.FindProperty("detailIcon").objectReferenceValue as Image;
            Image discoverIcon = soNav.FindProperty("discoverIcon").objectReferenceValue as Image;
            Image chartIcon = soNav.FindProperty("chartIcon").objectReferenceValue as Image;
            Image settingsIcon = soNav.FindProperty("settingsIcon").objectReferenceValue as Image;
            TextMeshProUGUI detailLabel = soNav.FindProperty("detailLabel").objectReferenceValue as TextMeshProUGUI;

            SerializedObject soFlow = new SerializedObject(flow);
            Button btnDetail = soFlow.FindProperty("btnDetail").objectReferenceValue as Button;
            Button btnDiscover = soFlow.FindProperty("btnDiscover").objectReferenceValue as Button;
            Button btnRecord = soFlow.FindProperty("btnRecord").objectReferenceValue as Button;
            Button btnChart = soFlow.FindProperty("btnChart").objectReferenceValue as Button;
            Button btnSettings = soFlow.FindProperty("btnSettings").objectReferenceValue as Button;

            bool allPass = true;

            allPass &= CheckTab("点明细 → 明细选中(Selected)其余灰(Normal)", btnDetail,
                detailIcon, "nav_detail_selected", discoverIcon, chartIcon, settingsIcon, detailLabel);

            allPass &= CheckTab("点图表 → 图表选中其余灰", btnChart,
                chartIcon, "nav_chart_selected", detailIcon, discoverIcon, settingsIcon, null);

            allPass &= CheckTab("点记账 → 四页签全灰（中间黄圆即焦点）", btnRecord,
                null, null, detailIcon, discoverIcon, settingsIcon, null, chartIcon);

            allPass &= CheckTab("点发现 → 发现选中其余灰", btnDiscover,
                discoverIcon, "nav_discover_selected", detailIcon, chartIcon, settingsIcon, null);

            allPass &= CheckTab("点设置 → 设置选中其余灰", btnSettings,
                settingsIcon, "nav_settings_selected", detailIcon, discoverIcon, chartIcon, null);

            allPass &= CheckTab("账单子页(发现子页) → 发现保持选中", null,
                discoverIcon, "nav_discover_selected", detailIcon, chartIcon, settingsIcon, null);

            Debug.Log($"[NavIconCheck] ===== 底栏图标态自检{(allPass ? "全部通过" : "存在失败项")} =====");
        }
        catch (Exception ex)
        {
            Debug.LogError("[NavIconCheck] 异常：" + ex);
        }
        finally
        {
            SessionState.SetBool(DoneKey, true);
            EditorApplication.isPlaying = false;
        }
    }

    /// <summary>
    /// 触发按钮（或直接调 UIManager.OpenBillPanel）后校验：focusIcon 换 Selected 资产、其余保持 Normal；
    /// focusIcon 为 null 表示记账档（四页签全灰，others 传四个页签图标）。
    /// </summary>
    private static bool CheckTab(string title, Button trigger, Image focusIcon, string focusSelectedName,
        Image otherA, Image otherB, Image otherC, TextMeshProUGUI focusLabel, Image otherD = null)
    {
        bool allPass = true;

        if (trigger != null)
        {
            trigger.onClick.Invoke();
        }
        else
        {
            UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
            uiManager.OpenBillPanel();
        }

        Sprite focusSelected = focusSelectedName != null ? AssetDatabase.LoadAssetAtPath<Sprite>($"{IconDir}/{focusSelectedName}.png") : null;

        if (focusIcon != null)
        {
            allPass &= Check(title + "｜选中图标", focusSelected != null && focusIcon.sprite == focusSelected);
        }

        allPass &= Check(title + "｜其余页签保持 Normal",
            IsNormal(otherA) && IsNormal(otherB) && IsNormal(otherC) && (otherD == null || IsNormal(otherD)));

        if (focusLabel != null && focusSelected != null)
        {
            allPass &= Check(title + "｜选中文字色变黑", focusLabel.color.r < 0.2f && focusLabel.color.g < 0.2f);
        }

        return allPass;
    }

    private static bool IsNormal(Image icon)
    {
        if (icon == null)
        {
            return false;
        }

        string path = AssetDatabase.GetAssetPath(icon.sprite);
        return path != null && path.Contains("_normal");
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[NavIconCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
