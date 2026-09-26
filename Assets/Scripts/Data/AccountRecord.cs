using System;

/// <summary>
/// 一条账目记录（策划案 §4.1，字段全部 string/long/int，JsonUtility 兼容）
/// </summary>
[Serializable]
public class AccountRecord
{
    /// <summary>
    /// 主键，导入去重键
    /// </summary>
    public string Id = Guid.NewGuid().ToString();

    /// <summary>
    /// RecordType：0=支出，1=收入
    /// </summary>
    public int Type = (int)RecordType.Expense;

    /// <summary>
    /// 分类名（如"餐饮"），与 CategoryTable 对应
    /// </summary>
    public string Category = string.Empty;

    /// <summary>
    /// 金额，单位：分（1523.38 元存 152338），显示时 ÷100
    /// </summary>
    public long AmountFen = 0;

    /// <summary>
    /// 日期，yyyy-MM-dd，所有周期查询的依据
    /// </summary>
    public string Date = string.Empty;

    /// <summary>
    /// 备注，可为空
    /// </summary>
    public string Note = string.Empty;

    /// <summary>
    /// 创建时间，yyyy-MM-dd HH:mm:ss
    /// </summary>
    public string CreatedAt = string.Empty;
}
