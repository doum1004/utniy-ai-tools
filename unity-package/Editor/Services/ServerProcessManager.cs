using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityAITools.Editor.Services
{
    /// <summary>
    /// Manages the MCP server process lifecycle: start, stop, and health monitoring.
    /// Expects a standalone compiled binary (umcpserver / umcpserver.exe).
    /// </summary>
    public class ServerProcessManager
    {
        public enum ServerStatus { Unknown, Running, Stopped }

        public static ServerProcessManager Instance { get; private set; }

        public ServerStatus Status { get; private set; } = ServerStatus.Unknown;
        public bool IsManagedProcess => _serverProcess != null && !_serverProcess.HasExited;

        public event Action OnStatusChanged;

        private Process _serverProcess;
        private CancellationTokenSource _healthCts;
        private string _executablePath;
        private int _healthPort;

        private const string BinaryName = "umcpserver";
        private const string PrefKeyExecutablePath = "UnityAITools_ServerExecutablePath";
        private const string SessionKeyPid = "UnityAITools_ServerPid";
        private const int HealthCheckIntervalMs = 3000;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        static ServerProcessManager()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            if (Instance != null) return;
            Instance = new ServerProcessManager();
            Instance.Setup();
        }

        private void Setup()
        {
            // Clean up legacy pref keys from older versions
            if (EditorPrefs.HasKey("UnityAITools_ServerPath"))
                EditorPrefs.DeleteKey("UnityAITools_ServerPath");
            if (EditorPrefs.HasKey("UnityAITools_ServerRuntime"))
                EditorPrefs.DeleteKey("UnityAITools_ServerRuntime");

            _executablePath = EditorPrefs.GetString(PrefKeyExecutablePath, "");
            _healthPort = 8090;

            if (string.IsNullOrEmpty(_executablePath) || !File.Exists(_executablePath))
                _executablePath = AutoDetectBinary() ?? "";

            ReattachOrphanedProcess();

            EditorApplication.quitting += OnQuitting;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            StartHealthMonitor();
        }

        public void SetExecutablePath(string path)
        {
            _executablePath = path;
            EditorPrefs.SetString(PrefKeyExecutablePath, path);
        }

        public string GetExecutablePath() => _executablePath;

        public bool StartServer()
        {
            if (IsManagedProcess)
            {
                Debug.LogWarning("[UnityAITools] Server process is already running.");
                return false;
            }

            var binary = ResolveExecutable();
            if (binary == null) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = binary,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                _serverProcess = Process.Start(psi);
                if (_serverProcess == null)
                {
                    Debug.LogError("[UnityAITools] Failed to start server process.");
                    return false;
                }

                SessionState.SetInt(SessionKeyPid, _serverProcess.Id);

                _serverProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.Log($"[MCP Server] {e.Data}");
                };
                _serverProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.LogWarning($"[MCP Server] {e.Data}");
                };

                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                Debug.Log($"[UnityAITools] MCP server started (PID {_serverProcess.Id}) — {binary}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityAITools] Failed to start server: {ex.Message}");
                return false;
            }
        }

        public void StopServer()
        {
            if (_serverProcess == null) return;

            try
            {
                if (!_serverProcess.HasExited)
                {
                    KillProcessTree(_serverProcess.Id);
                    Debug.Log("[UnityAITools] MCP server stopped.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAITools] Error stopping server: {ex.Message}");
            }
            finally
            {
                _serverProcess?.Dispose();
                _serverProcess = null;
                SessionState.SetInt(SessionKeyPid, 0);
            }
        }

        /// <summary>
        /// Returns a valid binary path — either the user-configured one or auto-detected.
        /// </summary>
        private string ResolveExecutable()
        {
            if (!string.IsNullOrEmpty(_executablePath) && File.Exists(_executablePath))
                return _executablePath;

            var detected = AutoDetectBinary();
            if (detected != null)
            {
                SetExecutablePath(detected);
                return detected;
            }

            Debug.LogError(
                $"[UnityAITools] Cannot find '{PlatformBinaryName()}'. " +
                "Build it with 'bun run build' in the server/ directory, " +
                "or set the executable path in Window > Unity AI Tools.");
            return null;
        }

        /// <summary>
        /// Searches common locations for the umcpserver binary.
        /// </summary>
        private string AutoDetectBinary()
        {
            var binaryName = PlatformBinaryName();
            var projectRoot = Path.GetDirectoryName(Application.dataPath);

            // Resolve the package source directory (handles file: linked packages)
            string packageSourceDir = null;
            try
            {
                var packageJsonPath = Path.GetFullPath(
                    Path.Combine("Packages", "com.unity-ai-tools", "package.json"));
                if (File.Exists(packageJsonPath))
                    packageSourceDir = Path.GetDirectoryName(packageJsonPath);
            }
            catch { /* ignore */ }

            var candidates = new[]
            {
                // Repo layout: unity-project/ is sibling of server/
                Path.Combine(projectRoot, "..", "server", "dist", binaryName),
                Path.Combine(projectRoot, "server", "dist", binaryName),
                Path.Combine(projectRoot, "..", "unity-ai-tools", "server", "dist", binaryName),
                // Resolve from the package source (file: link goes to unity-package/, server/ is a sibling)
                packageSourceDir != null
                    ? Path.Combine(packageSourceDir, "..", "server", "dist", binaryName)
                    : null,
                // Binary placed directly next to the project
                Path.Combine(projectRoot, binaryName),
                Path.Combine(projectRoot, "..", binaryName),
            };

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    Debug.Log($"[UnityAITools] Auto-detected server binary: {full}");
                    SetExecutablePath(full);
                    return full;
                }
            }

            // Check if it's on the system PATH
            if (TryFindOnPath(binaryName, out var pathResult))
            {
                Debug.Log($"[UnityAITools] Found server binary on PATH: {pathResult}");
                SetExecutablePath(pathResult);
                return pathResult;
            }

            // Log all searched paths to help diagnose
            Debug.LogWarning($"[UnityAITools] Searched for '{binaryName}' in:");
            Debug.LogWarning($"  Application.dataPath = {Application.dataPath}");
            Debug.LogWarning($"  projectRoot = {projectRoot}");
            if (packageSourceDir != null)
                Debug.LogWarning($"  packageSourceDir = {packageSourceDir}");
            foreach (var c in candidates)
            {
                if (c == null) continue;
                Debug.LogWarning($"  {Path.GetFullPath(c)}");
            }

            return null;
        }

        private static string PlatformBinaryName()
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                ? BinaryName + ".exe"
                : BinaryName;
        }

        private static bool TryFindOnPath(string name, out string path)
        {
            path = null;
            try
            {
                var isWindows = Application.platform == RuntimePlatform.WindowsEditor;
                var psi = new ProcessStartInfo
                {
                    FileName = isWindows ? "where" : "which",
                    Arguments = name,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                var output = proc.StandardOutput.ReadLine();
                proc.WaitForExit(3000);
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    path = output.Trim();
                    return true;
                }
            }
            catch { /* ignored */ }
            return false;
        }

        private void ReattachOrphanedProcess()
        {
            var pid = SessionState.GetInt(SessionKeyPid, 0);
            if (pid <= 0) return;

            try
            {
                var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    _serverProcess = proc;
                    Debug.Log($"[UnityAITools] Reattached to existing server process (PID {pid})");
                }
            }
            catch
            {
                SessionState.SetInt(SessionKeyPid, 0);
            }
        }

        private void StartHealthMonitor()
        {
            _healthCts?.Cancel();
            _healthCts = new CancellationTokenSource();
            _ = Task.Run(() => HealthMonitorLoop(_healthCts.Token));
        }

        private async Task HealthMonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HealthCheckIntervalMs, token);
                    var newStatus = await PingHealth();

                    if (newStatus != Status)
                    {
                        Status = newStatus;
                        EditorApplication.delayCall += () => OnStatusChanged?.Invoke();
                    }

                    if (IsManagedProcess && _serverProcess.HasExited)
                    {
                        Debug.LogWarning($"[UnityAITools] MCP server process exited with code {_serverProcess.ExitCode}");
                        _serverProcess.Dispose();
                        _serverProcess = null;
                        SessionState.SetInt(SessionKeyPid, 0);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* health check failure is handled by PingHealth returning Stopped */ }
            }
        }

        private async Task<ServerStatus> PingHealth()
        {
            try
            {
                var response = await Http.GetAsync($"http://localhost:{_healthPort}/health");
                return response.IsSuccessStatusCode ? ServerStatus.Running : ServerStatus.Stopped;
            }
            catch
            {
                return ServerStatus.Stopped;
            }
        }

        private static void KillProcessTree(int pid)
        {
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    var psi = new ProcessStartInfo("taskkill", $"/F /T /PID {pid}")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(5000);
                }
                else
                {
                    // First try killing the process directly
                    try
                    {
                        Process.GetProcessById(pid)?.Kill();
                    }
                    catch
                    {
                        // Fallback: use pkill to find child processes by parent PID
                        var psi = new ProcessStartInfo("pkill", $"-TERM -P {pid}")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(3000);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAITools] KillProcessTree failed: {ex.Message}");
                try
                {
                    Process.GetProcessById(pid)?.Kill();
                }
                catch { /* last resort */ }
            }
        }

        private void OnBeforeAssemblyReload()
        {
            _healthCts?.Cancel();
        }

        private void OnQuitting()
        {
            _healthCts?.Cancel();
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnQuitting;
            StopServer();
        }
    }
}
