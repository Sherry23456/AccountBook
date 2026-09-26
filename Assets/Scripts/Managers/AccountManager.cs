using System;
using System.Collections.Generic;
using UnityEngine;

public class AccountManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private AccountRepository accountRepository;

    /// <summary>
    /// 数据变化通知：增/删/改成功后 Invoke，明细/图表/汇总订阅刷新
    /// </summary>
    public event Action OnDataChanged;

    private void Awake()
    {
        TryInitializeDependencies();
    }

    /// <summary>
    /// 外部引用为空时自动查找
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (accountRepository == null)
        {
            accountRepository = FindFirstObjectByType<AccountRepository>();
        }
    }

    /// <summary>
    /// 新增一笔记录，成功后触发 OnDataChanged
    /// </summary>
    public bool AddRecord(AccountRecord record)
    {
        if (record == null)
        {
            Debug.LogWarning("[AccountManager] 要新增的 record 为 null。");
            return false;
        }

        TryInitializeDependencies();

        if (accountRepository == null)
        {
            Debug.LogWarning("[AccountManager] AccountRepository 引用缺失.");
            return false;
        }

        accountRepository.AddRecord(record);
        OnDataChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 修改一笔记录（按 Id 替换），成功后触发 OnDataChanged
    /// </summary>
    public bool UpdateRecord(AccountRecord record)
    {
        if (record == null)
        {
            Debug.LogWarning("[AccountManager] 要修改的 record 为 null。");
            return false;
        }

        TryInitializeDependencies();

        if (accountRepository == null)
        {
            Debug.LogWarning("[AccountManager] AccountRepository 引用缺失.");
            return false;
        }

        bool success = accountRepository.UpdateRecord(record);

        if (success)
        {
            OnDataChanged?.Invoke();
        }

        return success;
    }

    /// <summary>
    /// 删除一笔记录（按 Id），成功后触发 OnDataChanged
    /// </summary>
    public bool DeleteRecord(string recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            Debug.LogWarning("[AccountManager] 要删除的 recordId 为空。");
            return false;
        }

        TryInitializeDependencies();

        if (accountRepository == null)
        {
            Debug.LogWarning("[AccountManager] AccountRepository 引用缺失.");
            return false;
        }

        bool success = accountRepository.DeleteRecordById(recordId);

        if (success)
        {
            OnDataChanged?.Invoke();
        }

        return success;
    }

    /// <summary>
    /// 某月记录查询（yyyyMM，转传仓储）
    /// </summary>
    public List<AccountRecord> GetRecordsByMonth(string yyyyMM)
    {
        TryInitializeDependencies();

        if (accountRepository == null)
        {
            Debug.LogWarning("[AccountManager] AccountRepository 引用缺失.");
            return new List<AccountRecord>();
        }

        return accountRepository.GetRecordsByMonth(yyyyMM);
    }

    /// <summary>
    /// 某月收支汇总（yyyyMM，金额单位：分）
    /// </summary>
    public void GetMonthSummary(string yyyyMM, out long incomeFen, out long expenseFen)
    {
        incomeFen = 0;
        expenseFen = 0;

        List<AccountRecord> records = GetRecordsByMonth(yyyyMM);

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null)
            {
                continue;
            }

            if (record.Type == (int)RecordType.Income)
            {
                incomeFen += record.AmountFen;
            }
            else
            {
                expenseFen += record.AmountFen;
            }
        }
    }
}
