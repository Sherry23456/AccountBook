using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 步骤 01 验收：运行时切换自检（编辑器菜单可重跑）。
/// 自动进 Play 模式 → 依次触发底栏三按钮与两个弹窗开关 → 校验面板激活状态 → 退出 Play。
/// 结果逐条打 [PlayCheck] 日志。
/// </summary>
public static class RuntimeSwitchCheck
{
    private const string MenuItemPath = "AccountBook/03-运行时切换自检";
    private const string StartedKey = "AccountBook.RuntimeCheck.Started";
    private const string DoneKey = "AccountBook.RuntimeCheck.Done";
    private const string AutoRunKey = "AccountBook.RuntimeCheck.AutoRan";

    /// <summary>
    /// 编译后自动执行一次（每编辑器会话最多一次），无需手动点菜单
    /// </summary>
    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        if (SessionState.GetBool(AutoRunKey, false))
        {
            return;
        }

        SessionState.SetBool(AutoRunKey, true);

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode == false)
            {
                Debug.Log("[PlayCheck] 自动开始运行时切换自检（进 Play → 校验 → 退 Play）...");
                Run();
            }
        };
    }

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
            UIManager uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
            bool allPass = true;

            if (flow == null || uiManager == null)
            {
                Debug.LogError("[PlayCheck] FAIL - AppFlowManager/UIManager 未找到（场景未加载或接线缺失）。");
                SessionState.SetBool(DoneKey, true);
                EditorApplication.isPlaying = false;
                return;
            }

            SerializedObject soFlow = new SerializedObject(flow);
            Button btnDetail = soFlow.FindProperty("btnDetail").objectReferenceValue as Button;
            Button btnRecord = soFlow.FindProperty("btnRecord").objectReferenceValue as Button;
            Button btnChart = soFlow.FindProperty("btnChart").objectReferenceValue as Button;

            SerializedObject soUi = new SerializedObject(uiManager);
            GameObject detailPanel = soUi.FindProperty("detailPanel").objectReferenceValue as GameObject;
            GameObject chartPanel = soUi.FindProperty("chartPanel").objectReferenceValue as GameObject;
            GameObject recordPanel = soUi.FindProperty("recordPanel").objectReferenceValue as GameObject;
            GameObject exportPanel = soUi.FindProperty("exportPanel").objectReferenceValue as GameObject;
            GameObject importPanel = soUi.FindProperty("importPanel").objectReferenceValue as GameObject;

            allPass &= Check("底栏三按钮引用已接线",
                btnDetail != null && btnRecord != null && btnChart != null);

            if (btnChart != null)
            {
                btnChart.onClick.Invoke();
                allPass &= Check("点图表按钮 → 仅图表页激活",
                    IsOnlyActive(chartPanel, detailPanel, recordPanel));
            }

            if (btnRecord != null)
            {
                btnRecord.onClick.Invoke();
                allPass &= Check("点记账按钮 → 仅记账页激活（半屏覆盖）",
                    IsOnlyActive(recordPanel, detailPanel, chartPanel));
            }

            if (btnDetail != null)
            {
                btnDetail.onClick.Invoke();
                allPass &= Check("点明细按钮 → 仅明细页激活（默认页可切回）",
                    IsOnlyActive(detailPanel, chartPanel, recordPanel));
            }

            if (exportPanel != null && importPanel != null)
            {
                uiManager.OpenExportPanel();
                bool exportOverlay = exportPanel.activeSelf && detailPanel.activeSelf;
                uiManager.OpenImportPanel();
                bool importOverlay = importPanel.activeSelf && exportPanel.activeSelf && detailPanel.activeSelf;
                uiManager.CloseExportPanel();
                uiManager.CloseImportPanel();
                allPass &= Check("导出/导入弹窗叠加打开互不影响、可关闭",
                    exportOverlay && importOverlay && !exportPanel.activeSelf && !importPanel.activeSelf);
            }

            Debug.Log($"[PlayCheck] ===== 运行时切换自检{(allPass ? "全部通过" : "存在失败项")} =====");
        }
        catch (Exception ex)
        {
            Debug.LogError("[PlayCheck] 异常：" + ex);
        }
        finally
        {
            SessionState.SetBool(DoneKey, true);
            EditorApplication.isPlaying = false;
        }
    }

    /// <summary>
    /// target 激活且其他主面板全部隐藏
    /// </summary>
    private static bool IsOnlyActive(GameObject target, params GameObject[] others)
    {
        if (target == null || target.activeSelf == false)
        {
            return false;
        }

        for (int i = 0; i < others.Length; i++)
        {
            if (others[i] != null && others[i].activeSelf)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[PlayCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}

// 桥重连触发：修改此文件可引发域重载，让 WebSocket 重连 MCP 桥。
