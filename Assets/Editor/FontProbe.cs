using UnityEditor;
using UnityEngine;
using TMPro;

/// <summary>
/// 字体探针（步骤02 排查）：编辑器模式直接测试 STKAITI Dynamic SDF 能否动态添加汉字，
/// 并对照静态资产的覆盖情况。每编辑器会话自动跑一次，结果打 [FontProbe] 日志。
/// </summary>
public static class FontProbe
{
    private const string Key = "AccountBook.FontProbe.Ran";

    [InitializeOnLoadMethod]
    private static void Run()
    {
        if (SessionState.GetBool(Key, false))
        {
            return;
        }

        SessionState.SetBool(Key, true);
        EditorApplication.delayCall += Probe;
    }

    private static void Probe()
    {
        TMP_FontAsset dyn = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/STKAITI Dynamic SDF.asset");

        if (dyn == null)
        {
            Debug.LogError("[FontProbe] 找不到 Dynamic 字体资产。");
            return;
        }

        Debug.Log($"[FontProbe] Dynamic: mode={dyn.atlasPopulationMode}, chars={dyn.characterTable.Count}, " +
                  $"glyphs={dyn.glyphTable.Count}, atlas={dyn.atlasTexture != null}, multiAtlas={dyn.isMultiAtlasTexturesEnabled}, " +
                  $"sourceFont={(dyn.sourceFontFile != null ? dyn.sourceFontFile.name : "NULL")}");

        bool addOk = dyn.TryAddCharacters("餐饮记账测试赟");
        Debug.Log($"[FontProbe] Dynamic TryAddCharacters(餐饮记账测试赟) → {addOk}, chars now={dyn.characterTable.Count}");

        bool hasCan = dyn.HasCharacter('餐');
        bool hasYun = dyn.HasCharacter('赟');
        Debug.Log($"[FontProbe] Dynamic HasCharacter: 餐={hasCan}, 赟={hasYun}");

        TMP_FontAsset staticAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/STKAITI SDF.asset");

        if (staticAsset != null)
        {
            Debug.Log($"[FontProbe] Static: chars={staticAsset.characterTable.Count}, 餐={staticAsset.HasCharacter('餐')}, " +
                      $"退={staticAsset.HasCharacter('退')}, 赟={staticAsset.HasCharacter('赟')}");
        }

        Debug.Log($"[FontProbe] TMP默认字体={(TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset.name : "NULL")}");
    }
}
