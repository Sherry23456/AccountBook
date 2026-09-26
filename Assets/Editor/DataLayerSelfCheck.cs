using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 步骤 01 验收：数据层自检（编辑器菜单可重跑）。
/// 走真实 JsonFileService → Application.persistentDataPath/account_records.json，
/// 依次验证 增/查/改/月查询/区间查询/月汇总/OnDataChanged/删，结束后清理测试文件。
/// </summary>
public static class DataLayerSelfCheck
{
    private const string MenuItemPath = "AccountBook/02-数据层自检";
    private const string FileName = "account_records.json";

    [MenuItem(MenuItemPath)]
    private static void RunFromMenu()
    {
        Run();
    }

    /// <summary>
    /// 执行数据层自检，返回全部通过与否（结果逐条打 [SelfCheck] 日志）
    /// </summary>
    public static bool Run()
    {
        bool allPass = true;
        GameObject host = new GameObject("~SelfCheckHost");
        host.hideFlags = HideFlags.HideAndDontSave;

        try
        {
            JsonFileService jsonFileService = host.AddComponent<JsonFileService>();
            AccountRepository repository = host.AddComponent<AccountRepository>();
            AccountManager accountManager = host.AddComponent<AccountManager>();

            WireReference(repository, "jsonFileService", jsonFileService);
            WireReference(accountManager, "accountRepository", repository);

            Debug.Log($"[SelfCheck] 测试文件路径：{jsonFileService.GetFullPath(FileName)}");

            // 0) 干净起点
            jsonFileService.DeleteFile(FileName);

            string today = DateTime.Now.ToString("yyyy-MM-dd");
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string month = DateTime.Now.ToString("yyyy-MM");

            // 1) 新增 3 笔
            AccountRecord lunch = NewRecord((int)RecordType.Expense, "餐饮", 1030, today, "午饭", now);
            AccountRecord shopping = NewRecord((int)RecordType.Expense, "购物", 20000, today, "日用品", now);
            AccountRecord salary = NewRecord((int)RecordType.Income, "工资", 600000, today, "9月工资", now);

            accountManager.AddRecord(lunch);
            accountManager.AddRecord(shopping);
            accountManager.AddRecord(salary);

            bool fileExists = jsonFileService.Exists(FileName);
            string jsonText = fileExists ? File.ReadAllText(jsonFileService.GetFullPath(FileName)) : string.Empty;
            bool fieldsComplete = jsonText.Contains("Records") &&
                                  jsonText.Contains(lunch.Id) &&
                                  jsonText.Contains("AmountFen") &&
                                  jsonText.Contains("餐饮") &&
                                  jsonText.Contains("CreatedAt");
            allPass &= Check("AddRecord 后 JSON 落盘且字段完整", fileExists && fieldsComplete);

            // 2) 月查询
            List<AccountRecord> monthRecords = accountManager.GetRecordsByMonth(month);
            allPass &= Check($"GetRecordsByMonth({month}) == 3", monthRecords.Count == 3);

            // 3) 修改
            lunch.Note = "午饭(已改)";
            bool updateOk = accountManager.UpdateRecord(lunch);
            List<AccountRecord> afterUpdate = repository.LoadAllRecords();
            AccountRecord updated = afterUpdate.Find(r => r.Id == lunch.Id);
            allPass &= Check("UpdateRecord 按 Id 替换生效", updateOk && updated != null && updated.Note == "午饭(已改)");

            // 4) 区间查询（字符串比较，含首尾）
            List<AccountRecord> rangeRecords = repository.GetRecordsInRange(month + "-01", month + "-31");
            allPass &= Check("GetRecordsInRange 含首尾", rangeRecords.Count == 3);

            // 5) 月汇总
            accountManager.GetMonthSummary(month, out long incomeFen, out long expenseFen);
            allPass &= Check("GetMonthSummary 收入=600000分 支出=21030分",
                incomeFen == 600000 && expenseFen == 21030);

            // 6) OnDataChanged 事件
            int eventCount = 0;
            Action listener = () => eventCount++;
            accountManager.OnDataChanged += listener;

            AccountRecord extra = NewRecord((int)RecordType.Expense, "零食", 500, today, "奶茶", now);
            accountManager.AddRecord(extra);
            bool deleteOk = accountManager.DeleteRecord(extra.Id);
            accountManager.OnDataChanged -= listener;

            allPass &= Check("OnDataChanged 在增/删后各触发一次", deleteOk && eventCount == 2);

            // 7) 清理：删光 + 删文件
            accountManager.DeleteRecord(lunch.Id);
            accountManager.DeleteRecord(shopping.Id);
            accountManager.DeleteRecord(salary.Id);
            List<AccountRecord> afterDelete = repository.LoadAllRecords();
            allPass &= Check("DeleteRecordById 清空后 LoadAllRecords == 0", afterDelete.Count == 0);

            jsonFileService.DeleteFile(FileName);
            allPass &= Check("测试文件已清理", jsonFileService.Exists(FileName) == false);
        }
        catch (Exception ex)
        {
            Debug.LogError("[SelfCheck] 异常：" + ex);
            allPass = false;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }

        return allPass;
    }

    private static AccountRecord NewRecord(int type, string category, long amountFen, string date, string note, string createdAt)
    {
        AccountRecord record = new AccountRecord();
        record.Type = type;
        record.Category = category;
        record.AmountFen = amountFen;
        record.Date = date;
        record.Note = note;
        record.CreatedAt = createdAt;
        return record;
    }

    private static void WireReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
    {
        SerializedObject so = new SerializedObject(target);
        so.FindProperty(fieldName).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool Check(string title, bool pass)
    {
        Debug.Log($"[SelfCheck] {(pass ? "PASS" : "FAIL")} - {title}");
        return pass;
    }
}
