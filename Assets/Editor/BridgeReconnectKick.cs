using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MCP 桥重连引导（步骤02）：新开的编辑器实例若从未启动过 MCP 会话，插件不会主动连上
/// python 桥（127.0.0.1:8080），表现为 ZCode 侧 "no_unity_session"。
/// 本脚本在每次编译完成后反射调用 MCPServiceLocator.Bridge.StartAsync() 拉起插件会话（每编辑器会话一次）。
/// 桥自动重连稳定后可删除。
/// </summary>
public static class BridgeReconnectKick
{
    private const string SessionKey = "AccountBook.BridgeKick.Ran.v3";   // v3：桥会话丢失后换键重拉（每次改键触发一次重连）

    [InitializeOnLoadMethod]
    private static void Kick()
    {
        if (SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        SessionState.SetBool(SessionKey, true);

        EditorApplication.delayCall += () =>
        {
            try
            {
                Assembly mcpAssembly = null;

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == "MCPForUnity.Editor")
                    {
                        mcpAssembly = assembly;
                        break;
                    }
                }

                if (mcpAssembly == null)
                {
                    Debug.LogWarning("[BridgeKick] 未找到 MCPForUnity.Editor 程序集。");
                    return;
                }

                Type locatorType = mcpAssembly.GetType("MCPForUnity.Editor.Services.MCPServiceLocator");
                PropertyInfo bridgeProperty = locatorType.GetProperty("Bridge", BindingFlags.Public | BindingFlags.Static);
                object bridge = bridgeProperty.GetValue(null);
                PropertyInfo isRunningProperty = bridge.GetType().GetProperty("IsRunning");
                bool isRunning = (bool)isRunningProperty.GetValue(bridge);

                if (isRunning)
                {
                    Debug.Log("[BridgeKick] MCP 桥已在运行，无需重连。");
                    return;
                }

                MethodInfo startAsyncMethod = bridge.GetType().GetMethod("StartAsync", Type.EmptyTypes);
                Task startTask = (Task)startAsyncMethod.Invoke(bridge, null);
                Debug.Log("[BridgeKick] 已调用 Bridge.StartAsync()，等待连接...");

                startTask.ContinueWith(t =>
                {
                    bool ok = false;

                    try
                    {
                        ok = t.Status == TaskStatus.RanToCompletion &&
                             (bool)t.GetType().GetProperty("Result").GetValue(t);
                    }
                    catch
                    {
                        // 结果读取失败不影响主流程
                    }

                    Debug.Log($"[BridgeKick] Bridge.StartAsync 完成：ok={ok}, status={t.Status}");
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BridgeKick] 拉桥失败：" + ex.Message);
            }
        };
    }
}
