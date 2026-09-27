using UnityEditor;
using UnityEngine;

/// <summary>
/// 安全区换算自检（编辑模式，菜单 AccountBook/98-安全区换算自检）。
/// 背景：SafeAreaFitter 曾用"直接父容器高度"换算像素→Canvas单位，顶栏父容器只有 240~300 单位高，
/// 真机上内容几乎不下移，年月标题被前置摄像头挖孔遮住；改为 Canvas 全屏高度换算后用本自检锁住基准。
/// </summary>
public static class SafeAreaCalcCheck
{
    [MenuItem("AccountBook/98-安全区换算自检")]
    private static void Run()
    {
        bool allPass = true;
        float top, bottom;

        // 典型挖孔屏：1080×2400、Canvas 单位高 1920（比例 0.8 单位/像素），
        // 安全区底 132px（手势条）、顶 32px（状态栏）→ top=32*0.8+20=45.6，bottom=132*0.8=105.6
        SafeAreaFitter.ComputeInsets(1920f, 2400f, new Rect(0f, 132f, 1080f, 2236f), 20f, out top, out bottom);
        allPass &= Check("顶栏换算：32px+留白 → 45.6 单位", Mathf.Approximately(top, 45.6f));
        allPass &= Check("底栏换算：132px → 105.6 单位", Mathf.Approximately(bottom, 105.6f));

        // 旧 Bug 回归锁：若换算基准退回 300 单位高的顶栏父容器，32px 只会算出 4 单位（远小于 25.6 的纯像素换算）
        allPass &= Check("顶栏像素换算不低于 0.8 单位/像素基准", top >= 32f * (1920f / 2400f) - 0.01f);

        // 无刘海全屏（编辑器 Game 视图）：无像素内边距，只剩顶部额外留白
        SafeAreaFitter.ComputeInsets(1920f, 2400f, new Rect(0f, 0f, 1080f, 2400f), 20f, out top, out bottom);
        allPass &= Check("全屏安全区：顶部只留额外 20 单位", Mathf.Approximately(top, 20f) && Mathf.Approximately(bottom, 0f));

        // 异常兜底：Canvas/屏幕尺寸非法时不产生位移
        SafeAreaFitter.ComputeInsets(0f, 2400f, new Rect(0f, 132f, 1080f, 2236f), 20f, out top, out bottom);
        allPass &= Check("非法 Canvas 高度：返回 0", Mathf.Approximately(top, 0f) && Mathf.Approximately(bottom, 0f));

        Debug.Log($"[SafeAreaCheck] ===== 安全区换算自检{(allPass ? "全部通过" : "存在失败项(见上方 FAIL)")} =====");
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[SafeAreaCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
