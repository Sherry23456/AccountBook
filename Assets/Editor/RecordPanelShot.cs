using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 记账页截图（步骤02 验收辅助）：记账流程自检结束退 Play 后自动触发第二轮 Play，
/// 打开记账页 → 等 UI 布局稳定 → ScreenCapture 截图存盘 → 退 Play。
/// 产物：D:\Qklunity\AccountBook\.shots\record_panel.png（供布局核对）。
/// </summary>
public static class RecordPanelShot
{
    private const string PendingKey = "AccountBook.RecordShot.Pending";
    private const string ArmedKey = "AccountBook.RecordShot.Armed";
    private const string TakenKey = "AccountBook.RecordShot.Taken";
    private const string ShotPath = "D:\\Qklunity\\AccountBook\\.shots\\record_panel.png";

    [InitializeOnLoadMethod]
    private static void Init()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            // 流程自检已完成且截图待执行 → 再次进 Play 截图
            if (SessionState.GetBool(TakenKey, false) == false &&
                SessionState.GetBool(PendingKey, false))
            {
                SessionState.SetBool(PendingKey, false);
                SessionState.SetBool(ArmedKey, true);
                Debug.Log("[RecordShot] 流程自检结束，进入截图 Play 会话...");
                EditorApplication.isPlaying = true;
            }

            return;
        }

        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ArmedKey, false))
        {
            SessionState.SetBool(ArmedKey, false);
            EditorApplication.delayCall += OpenPanelAndCapture;
        }
    }

    /// <summary>
    /// 打开记账页 → 等 40 帧布局稳定 → 截图 → 再等 20 帧 → 退 Play
    /// </summary>
    private static void OpenPanelAndCapture()
    {
        try
        {
            AppFlowManager flow = UnityEngine.Object.FindFirstObjectByType<AppFlowManager>();

            if (flow == null)
            {
                Debug.LogError("[RecordShot] 找不到 AppFlowManager。");
                EditorApplication.isPlaying = false;
                return;
            }

            SerializedObject soFlow = new SerializedObject(flow);
            Button btnRecord = soFlow.FindProperty("btnRecord").objectReferenceValue as Button;
            btnRecord.onClick.Invoke();
            Debug.Log("[RecordShot] 已打开记账页，等待布局稳定后截图。");
        }
        catch (Exception ex)
        {
            Debug.LogError("[RecordShot] 打开记账页失败：" + ex);
            EditorApplication.isPlaying = false;
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));

        int frame = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;

        void Tick()
        {
            frame++;

            if (frame == 40)
            {
                ScreenCapture.CaptureScreenshot(ShotPath, 2);
                Debug.Log("[RecordShot] 已请求截图：" + ShotPath);
            }

            if (frame >= 60)
            {
                EditorApplication.update -= Tick;
                SessionState.SetBool(TakenKey, true);
                EditorApplication.isPlaying = false;
            }
        }
    }
}
