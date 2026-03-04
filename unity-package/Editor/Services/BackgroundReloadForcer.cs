using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityAITools.Editor.Services
{
    /// <summary>
    /// Forces Unity to complete pending domain reloads even when the Editor window
    /// is not focused. Without this, Unity shows "Reloading Domain…" but defers
    /// the actual reload until the user alt-tabs back.
    ///
    /// Runs a background thread (independent of EditorApplication.update) that
    /// periodically calls UnlockReloadAssemblies, RequestScriptReload, and
    /// QueuePlayerLoopUpdate to push the reload through.
    /// </summary>
    [InitializeOnLoad]
    public static class BackgroundReloadForcer
    {
        private static CancellationTokenSource _cts;
        private const int TickMs = 1000;

        static BackgroundReloadForcer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
            StartMonitor();
        }

        private static void OnBeforeReload()
        {
            _cts?.Cancel();
        }

        private static void OnAfterReload()
        {
            StartMonitor();
        }

        private static void StartMonitor()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            Task.Run(() => MonitorLoop(_cts.Token));
        }

        private static async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TickMs, token);

                    if (UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                        continue;

                    if (!EditorApplication.isCompiling)
                        continue;

                    // Unity is compiling in the background — force the reload through.
                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

                    // UnlockReloadAssemblies unblocks any deferred reload.
                    // Must run on main thread — delayCall queues it there.
                    // The QueuePlayerLoopUpdate above ensures delayCall gets processed.
                    EditorApplication.delayCall += () =>
                    {
                        if (!EditorApplication.isCompiling) return;
                        EditorApplication.UnlockReloadAssemblies();
                        EditorUtility.RequestScriptReload();
                    };
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Swallow — this utility must never crash the editor
                }
            }
        }
    }
}
