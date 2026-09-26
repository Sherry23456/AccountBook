using System.Collections.Generic;

/// <summary>
/// 静态分类表（策划案 §4.2）：记账页宫格、明细页行图标、图表排行榜、Excel 导入校验共用。
/// 图标 sprite 由 CategoryIconProvider 按 IconName 注入，本表只存字符串数据。
/// </summary>
public static class CategoryTable
{
    public class CategoryDef
    {
        public string Name;
        public string IconName;
        public int Type;   // RecordType：0=支出，1=收入

        public CategoryDef(string name, string iconName, int type)
        {
            Name = name;
            IconName = iconName;
            Type = type;
        }
    }

    private static readonly List<CategoryDef> all = new List<CategoryDef>()
    {
        // 支出（31 项）
        new CategoryDef("餐饮", "icon_dining", (int)RecordType.Expense),
        new CategoryDef("购物", "icon_shopping", (int)RecordType.Expense),
        new CategoryDef("日用", "icon_daily", (int)RecordType.Expense),
        new CategoryDef("交通", "icon_transport", (int)RecordType.Expense),
        new CategoryDef("蔬菜", "icon_vegetable", (int)RecordType.Expense),
        new CategoryDef("水果", "icon_fruit", (int)RecordType.Expense),
        new CategoryDef("零食", "icon_snack", (int)RecordType.Expense),
        new CategoryDef("运动", "icon_sports", (int)RecordType.Expense),
        new CategoryDef("娱乐", "icon_entertainment", (int)RecordType.Expense),
        new CategoryDef("通讯", "icon_communication", (int)RecordType.Expense),
        new CategoryDef("服饰", "icon_clothing", (int)RecordType.Expense),
        new CategoryDef("住房", "icon_housing", (int)RecordType.Expense),
        new CategoryDef("居家", "icon_home", (int)RecordType.Expense),
        new CategoryDef("长辈", "icon_elder", (int)RecordType.Expense),
        new CategoryDef("社交", "icon_social", (int)RecordType.Expense),
        new CategoryDef("旅行", "icon_travel", (int)RecordType.Expense),
        new CategoryDef("数码", "icon_digital", (int)RecordType.Expense),
        new CategoryDef("医疗", "icon_medical", (int)RecordType.Expense),
        new CategoryDef("书籍", "icon_books", (int)RecordType.Expense),
        new CategoryDef("学习", "icon_study", (int)RecordType.Expense),
        new CategoryDef("礼物", "icon_gift", (int)RecordType.Expense),
        new CategoryDef("办公", "icon_office", (int)RecordType.Expense),
        new CategoryDef("维修", "icon_repair", (int)RecordType.Expense),
        new CategoryDef("捐赠", "icon_donate", (int)RecordType.Expense),
        new CategoryDef("彩票", "icon_lottery", (int)RecordType.Expense),
        new CategoryDef("亲友", "icon_family", (int)RecordType.Expense),
        new CategoryDef("快递", "icon_express", (int)RecordType.Expense),
        new CategoryDef("眼镜", "icon_glasses", (int)RecordType.Expense),
        new CategoryDef("理发", "icon_haircut", (int)RecordType.Expense),
        new CategoryDef("话费", "icon_phonecredit", (int)RecordType.Expense),
        new CategoryDef("其他", "icon_general", (int)RecordType.Expense),
        // 收入（6 项）
        new CategoryDef("工资", "icon_general", (int)RecordType.Income),
        new CategoryDef("兼职", "icon_office", (int)RecordType.Income),
        new CategoryDef("礼金", "icon_giftmoney", (int)RecordType.Income),
        new CategoryDef("红包", "icon_gift", (int)RecordType.Income),
        new CategoryDef("报销", "icon_office", (int)RecordType.Income),
        new CategoryDef("其他", "icon_general", (int)RecordType.Income),
    };

    /// <summary>
    /// 全部分类定义
    /// </summary>
    public static List<CategoryDef> All
    {
        get { return all; }
    }

    /// <summary>
    /// 按类型取分类列表（宫格页签用）
    /// </summary>
    public static List<CategoryDef> GetByType(int recordType)
    {
        List<CategoryDef> result = new List<CategoryDef>();

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Type == recordType)
            {
                result.Add(all[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// 分类名 → 图标名（明细行/排行榜图标用），未找到返回空串
    /// </summary>
    public static string GetIconName(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return string.Empty;
        }

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Name == categoryName)
            {
                return all[i].IconName;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 分类名是否在表中（Excel 导入校验用；不在表的分类归"其他"并记警告）
    /// </summary>
    public static bool Contains(string categoryName)
    {
        return string.IsNullOrEmpty(GetIconName(categoryName)) == false;
    }
}
