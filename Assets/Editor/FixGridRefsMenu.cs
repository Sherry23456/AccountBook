using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 修复记账宫格引用（2026-10-05）：CategoryGridItem 预制体被重建后 fileID 全换，
/// RecordPanelUI / BudgetDialogUI 的 categoryItemPrefab 引用悬空——记账页宫格空白的根因。
/// 菜单一键把两处引用重新指到新预制体根组件并存盘。验证稳定后可删除。
/// </summary>
public static class FixGridRefsMenu
{
    private const string PrefabPath = "Assets/Prefabs/CategoryGridItem.prefab";

    [MenuItem("AccountBook/修复记账宫格引用")]
    public static void Fix()
    {
        CategoryGridItem prefab = AssetDatabase.LoadAssetAtPath<CategoryGridItem>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"[FixGridRefs] 无法加载 {PrefabPath}，中止。");
            return;
        }

        int fixedCount = FixRefs<RecordPanelUI>(prefab, "RecordPanelUI");
        fixedCount += FixRefs<BudgetDialogUI>(prefab, "BudgetDialogUI");

        if (Application.isPlaying == false && fixedCount > 0)
        {
            EditorSceneManager.SaveOpenScenes();
        }

        Debug.Log($"[FixGridRefs] 完成：修复 {fixedCount} 处引用{(Application.isPlaying ? "（Play 模式中，未存盘）" : "，场景已存盘")}。");
    }

    /// <summary>
    /// 遍历场内（含未激活）目标组件，把 categoryItemPrefab 重指到预制体根组件
    /// </summary>
    private static int FixRefs<T>(CategoryGridItem prefab, string label) where T : MonoBehaviour
    {
        T[] targets = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
        int count = 0;

        foreach (T target in targets)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty("categoryItemPrefab");

            if (prop == null)
            {
                Debug.LogWarning($"[FixGridRefs] {label}（{target.name}）没有 categoryItemPrefab 字段，跳过。");
                continue;
            }

            if (prop.objectReferenceValue == prefab)
            {
                continue;   // 已正确，无需重指
            }

            Debug.Log($"[FixGridRefs] {label}（{target.name}）：{Describe(prop.objectReferenceValue)} → CategoryGridItem");
            prop.objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            count++;
        }

        return count;
    }

    private static string Describe(Object value)
    {
        return value != null ? value.name : "空/悬空引用";
    }
}
