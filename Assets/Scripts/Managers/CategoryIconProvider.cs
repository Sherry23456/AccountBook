using System.Collections.Generic;
using UnityEngine;

public class CategoryIconProvider : MonoBehaviour
{
    [Header("Icon Sources")]
    [SerializeField] private List<Sprite> sprites = new List<Sprite>();

    private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    private void Awake()
    {
        BuildCache();
    }

    /// <summary>
    /// 用注入的 sprite 列表建立 图标名 → sprite 缓存
    /// </summary>
    private void BuildCache()
    {
        spriteCache.Clear();

        for (int i = 0; i < sprites.Count; i++)
        {
            Sprite sprite = sprites[i];

            if (sprite == null)
            {
                continue;
            }

            spriteCache[sprite.name] = sprite;
        }
    }

    /// <summary>
    /// 按图标名取 sprite（如 icon_dining），未找到返回 null
    /// </summary>
    public Sprite GetSprite(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        if (spriteCache.Count == 0)
        {
            BuildCache();
        }

        if (spriteCache.TryGetValue(iconName, out Sprite sprite))
        {
            return sprite;
        }

        return null;
    }
}
