using UnityEditor;
using UnityEngine;

/// <summary>
/// 截图重跑（步骤02 排查）：编译后自动进入 Play 并重拍记账页（复用 RecordPanelShot 的 Armed 机制）。
/// 每编辑器会话一次；配合字体探针验证 Play 模式下的字体验证结果。
/// </summary>
public static class RecordShotRerun
{
    private const string Key = "AccountBook.RecordShotRerun.Ran2";

    [InitializeOnLoadMethod]
    private static void Run()
    {
        if (SessionState.GetBool(Key, false))
        {
            return;
        }

        SessionState.SetBool(Key, true);

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

            SessionState.SetBool("AccountBook.RecordShot.Pending", false);
            SessionState.SetBool("AccountBook.RecordShot.Armed", true);
            SessionState.SetBool("AccountBook.RecordShot.Taken", false);
            Debug.Log("[ShotRerun] 重新武装截图流程，进入 Play...");
            EditorApplication.isPlaying = true;
        };
    }
}
