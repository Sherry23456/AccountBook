using UnityEditor;
using UnityEngine;

/// <summary>
/// 顶栏截图链（菜单 AccountBook/96-顶栏截图链）：进 Play 后按 明细→图表→记账 三轮
/// 打开对应页签各截一张 Game 视图（.shots\topbar_*.png），退出 Play 自动进下一轮。
/// 用途：顶栏布局修复后的人工目检产物（TopBarLayoutFix 数值断言的视觉补充）。
/// </summary>
public static class TopBarShotChain
{
    private const string ArmedKey = "AccountBook.TopBarShot.Armed";
    private const string StageKey = "AccountBook.TopBarShot.Stage";

    [MenuItem("AccountBook/96-顶栏截图链")]
    private static void Arm()
    {
        SessionState.SetBool(ArmedKey, true);
        SessionState.SetInt(StageKey, 0);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.isPlaying = true;
        Debug.Log("[TopBarShot] 已武装，进入 Play 截明细页...");
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (SessionState.GetBool(ArmedKey, false) == false)
        {
            return;
        }

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += CaptureCurrentStage;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            int stage = SessionState.GetInt(StageKey, 0);
            if (stage < 2)
            {
                SessionState.SetInt(StageKey, stage + 1);
                Debug.Log("[TopBarShot] 进入下一页截图会话...");
                EditorApplication.isPlaying = true;
            }
            else
            {
                SessionState.SetBool(ArmedKey, false);
                Debug.Log("[TopBarShot] ===== 明细/图表/记账 三页截图完成 =====");
            }
        }
    }

    private static void CaptureCurrentStage()
    {
        // 再垫一拍：等 UIManager/首帧 UI 就绪
        EditorApplication.delayCall += () =>
        {
            UIManager ui = Object.FindFirstObjectByType<UIManager>();
            if (ui == null)
            {
                Debug.LogError("[TopBarShot] 场景里找不到 UIManager，截图链中止");
                SessionState.SetBool(ArmedKey, false);
                EditorApplication.isPlaying = false;
                return;
            }

            int stage = SessionState.GetInt(StageKey, 0);
            string file;
            switch (stage)
            {
                case 0:
                    ui.OpenDetailPanel();
                    file = "detail";
                    break;

                case 1:
                    ui.OpenChartPanel();
                    file = "chart";
                    break;

                default:
                    ui.OpenRecordPanel();
                    file = "record";
                    break;
            }

            // 再垫两拍：等页面切换与布局重建完成再截
            EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
            {
                ScreenCapture.CaptureScreenshot("D:\\Qklunity\\AccountBook\\.shots\\topbar_" + file + ".png", 2);
                Debug.Log("[TopBarShot] 已截图 topbar_" + file + ".png");
                EditorApplication.delayCall += () => { EditorApplication.isPlaying = false; };
            };
        };
    }
}
