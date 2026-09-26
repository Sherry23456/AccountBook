using System;
using System.Collections.Generic;
using UnityEngine;

public class AccountRepository : MonoBehaviour
{
    [Serializable]
    private class AccountRecordCollection
    {
        public List<AccountRecord> Records = new List<AccountRecord>();
    }

    [Header("Dependencies")]
    [SerializeField] private JsonFileService jsonFileService;

    [Header("Storage Settings")]
    [SerializeField] private string fileName = "account_records.json";

    private void Awake()
    {
        TryInitializeDependencies();
    }

    /// <summary>
    /// 外部引用为空时自动查找
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (jsonFileService == null)
        {
            jsonFileService = FindFirstObjectByType<JsonFileService>();
        }
    }

    /// <summary>
    /// 全部 记录 读取
    /// </summary>
    public List<AccountRecord> LoadAllRecords()
    {
        TryInitializeDependencies();

        if (jsonFileService == null)
        {
            Debug.LogWarning("[AccountRepository] JsonFileService 引用缺失.");
            return new List<AccountRecord>();
        }

        AccountRecordCollection collection =
            jsonFileService.LoadFromJson<AccountRecordCollection>(fileName);

        if (collection == null || collection.Records == null)
        {
            return new List<AccountRecord>();
        }

        return new List<AccountRecord>(collection.Records);
    }

    /// <summary>
    /// 全部 保存记录
    /// </summary>
    public void SaveAllRecords(List<AccountRecord> records)
    {
        TryInitializeDependencies();

        if (jsonFileService == null)
        {
            Debug.LogWarning("[AccountRepository] JsonFileService 引用缺失.");
            return;
        }

        AccountRecordCollection collection = new AccountRecordCollection();

        if (records != null)
        {
            collection.Records = new List<AccountRecord>(records);
        }

        jsonFileService.SaveToJson(fileName, collection);
    }

    /// <summary>
    /// 新增保存单条记录
    /// </summary>
    public void AddRecord(AccountRecord record)
    {
        if (record == null)
        {
            Debug.LogWarning("[AccountRepository] 要保存的 record 为 null。");
            return;
        }

        List<AccountRecord> records = LoadAllRecords();
        records.Add(record);
        SaveAllRecords(records);
    }

    /// <summary>
    /// 按 Id 替换修改单条记录
    /// </summary>
    public bool UpdateRecord(AccountRecord updatedRecord)
    {
        if (updatedRecord == null)
        {
            Debug.LogWarning("[AccountRepository] 要修改的 record 为 null。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(updatedRecord.Id))
        {
            Debug.LogWarning("[AccountRepository] 要修改的 record 的 Id 为空。");
            return false;
        }

        List<AccountRecord> records = LoadAllRecords();

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null)
            {
                continue;
            }

            if (record.Id == updatedRecord.Id)
            {
                records[i] = updatedRecord;
                SaveAllRecords(records);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 按 ID 删除单条记录
    /// </summary>
    public bool DeleteRecordById(string recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            Debug.LogWarning("[AccountRepository] 要删除的 recordId 为空。");
            return false;
        }

        List<AccountRecord> records = LoadAllRecords();

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null)
            {
                continue;
            }

            if (record.Id == recordId)
            {
                records.RemoveAt(i);
                SaveAllRecords(records);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取某月记录（yyyyMM，Date 前缀匹配）
    /// </summary>
    public List<AccountRecord> GetRecordsByMonth(string yyyyMM)
    {
        List<AccountRecord> allRecords = LoadAllRecords();
        List<AccountRecord> monthRecords = new List<AccountRecord>();

        if (string.IsNullOrWhiteSpace(yyyyMM))
        {
            return monthRecords;
        }

        for (int i = 0; i < allRecords.Count; i++)
        {
            AccountRecord record = allRecords[i];

            if (record == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(record.Date) &&
                record.Date.StartsWith(yyyyMM))
            {
                monthRecords.Add(record);
            }
        }

        return monthRecords;
    }

    /// <summary>
    /// 获取日期区间记录（含首尾，字符串比较；周期导出/图表共用）
    /// </summary>
    public List<AccountRecord> GetRecordsInRange(string startDate, string endDate)
    {
        List<AccountRecord> allRecords = LoadAllRecords();
        List<AccountRecord> rangeRecords = new List<AccountRecord>();

        if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
        {
            return rangeRecords;
        }

        for (int i = 0; i < allRecords.Count; i++)
        {
            AccountRecord record = allRecords[i];

            if (record == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.Date))
            {
                continue;
            }

            if (record.Date.CompareTo(startDate) >= 0 &&
                record.Date.CompareTo(endDate) <= 0)
            {
                rangeRecords.Add(record);
            }
        }

        return rangeRecords;
    }
}
