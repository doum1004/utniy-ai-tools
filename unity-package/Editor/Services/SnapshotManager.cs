using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityAITools.Editor.Services
{
    /// <summary>
    /// Manages project snapshots for reverting AI/MCP changes.
    /// Uses git stash if the project is in a git repo, otherwise
    /// copies modified files to a temp backup directory.
    /// </summary>
    public class SnapshotManager
    {
        public static SnapshotManager Instance { get; private set; }

        public bool HasSnapshot => !string.IsNullOrEmpty(_lastSnapshotId);
        public string LastSnapshotTime => _lastSnapshotTime?.ToString("HH:mm:ss") ?? "";
        public string LastSnapshotId => _lastSnapshotId ?? "";
        public string LastSnapshotNote => _lastSnapshotNote ?? "";
        public bool IsGitRepo { get; private set; }

        public event Action OnStatusChanged;

        private string _projectRoot;
        private string _lastSnapshotId;
        private DateTime? _lastSnapshotTime;
        private string _lastSnapshotNote;
        private string _backupDir;

        static SnapshotManager()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            if (Instance != null) return;
            Instance = new SnapshotManager();
            Instance.Setup();
        }

        private void Setup()
        {
            _projectRoot = Path.GetDirectoryName(Application.dataPath);
            _backupDir = Path.Combine(_projectRoot, "Temp", "UnityAITools", "snapshots");
            IsGitRepo = Directory.Exists(Path.Combine(_projectRoot, ".git")) ||
                        Directory.Exists(Path.Combine(Path.GetDirectoryName(_projectRoot) ?? "", ".git"));
        }

        public async Task<(bool success, string message)> CreateSnapshotAsync(string note = null)
        {
            _lastSnapshotNote = note;
            if (IsGitRepo)
                return await CreateGitSnapshotAsync();
            return await CreateFileSnapshotAsync();
        }

        public async Task<(bool success, string message)> RevertToSnapshotAsync()
        {
            if (!HasSnapshot)
                return (false, "No snapshot to revert to");

            if (IsGitRepo)
                return await RevertGitSnapshotAsync();
            return await RevertFileSnapshotAsync();
        }

        private async Task<(bool success, string message)> CreateGitSnapshotAsync()
        {
            // Record current HEAD as the snapshot point.
            // Revert will restore all tracked files to HEAD state and remove untracked files.
            var hashResult = await RunGitAsync("rev-parse HEAD");
            if (!hashResult.success)
                return (false, $"git rev-parse failed: {hashResult.output}");

            _lastSnapshotId = hashResult.output.Trim();
            _lastSnapshotTime = DateTime.Now;

            NotifyChanged();
            Debug.Log($"[UnityAITools] Git snapshot created at HEAD {_lastSnapshotId.Substring(0, 8)}");
            return (true, $"Snapshot created at {_lastSnapshotId.Substring(0, 8)}");
        }

        private async Task<(bool success, string message)> RevertGitSnapshotAsync()
        {
            // Reset index to HEAD (unstage everything)
            await RunGitAsync("reset HEAD");

            // Discard all changes to tracked files
            var result = await RunGitAsync("checkout -- .");
            if (!result.success)
                return (false, $"git checkout failed: {result.output}");

            // Remove untracked files (but not ignored ones)
            await RunGitAsync("clean -fd");

            // AssetDatabase must be called from the main thread
            EditorApplication.delayCall += () => AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log("[UnityAITools] Reverted to git snapshot");

            _lastSnapshotId = null;
            _lastSnapshotTime = null;
            _lastSnapshotNote = null;
            NotifyChanged();
            return (true, "Reverted to snapshot");
        }

        private async Task<(bool success, string message)> CreateFileSnapshotAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var snapshotDir = Path.Combine(_backupDir, timestamp);

                    if (Directory.Exists(snapshotDir))
                        Directory.Delete(snapshotDir, true);
                    Directory.CreateDirectory(snapshotDir);

                    var assetsDir = Application.dataPath;
                    var projectSettingsDir = Path.Combine(_projectRoot, "ProjectSettings");

                    int fileCount = 0;
                    fileCount += CopyDirectory(assetsDir, Path.Combine(snapshotDir, "Assets"));
                    if (Directory.Exists(projectSettingsDir))
                        fileCount += CopyDirectory(projectSettingsDir, Path.Combine(snapshotDir, "ProjectSettings"));

                    _lastSnapshotId = timestamp;
                    _lastSnapshotTime = DateTime.Now;
                    NotifyChanged();

                    Debug.Log($"[UnityAITools] File snapshot created: {fileCount} files backed up to {snapshotDir}");
                    return (true, $"Snapshot created ({fileCount} files)");
                }
                catch (Exception ex)
                {
                    return (false, $"Snapshot failed: {ex.Message}");
                }
            });
        }

        private async Task<(bool success, string message)> RevertFileSnapshotAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var snapshotDir = Path.Combine(_backupDir, _lastSnapshotId);
                    if (!Directory.Exists(snapshotDir))
                        return (false, "Snapshot directory not found");

                    var assetsBackup = Path.Combine(snapshotDir, "Assets");
                    var settingsBackup = Path.Combine(snapshotDir, "ProjectSettings");

                    int fileCount = 0;
                    if (Directory.Exists(assetsBackup))
                        fileCount += CopyDirectory(assetsBackup, Application.dataPath);
                    if (Directory.Exists(settingsBackup))
                        fileCount += CopyDirectory(settingsBackup, Path.Combine(_projectRoot, "ProjectSettings"));

                    _lastSnapshotId = null;
                    _lastSnapshotTime = null;
                    _lastSnapshotNote = null;
                    NotifyChanged();

                    Debug.Log($"[UnityAITools] Reverted from file snapshot: {fileCount} files restored");
                    return (true, $"Reverted ({fileCount} files restored)");
                }
                catch (Exception ex)
                {
                    return (false, $"Revert failed: {ex.Message}");
                }
            });
        }

        private static int CopyDirectory(string sourceDir, string destDir)
        {
            int count = 0;
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
                count++;
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName == ".git" || dirName == "Library" || dirName == "Temp" || dirName == "Obj")
                    continue;
                count += CopyDirectory(dir, Path.Combine(destDir, dirName));
            }

            return count;
        }

        private async Task<(bool success, string output)> RunGitAsync(string args)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("git", args)
                    {
                        WorkingDirectory = _projectRoot,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null) return (false, "Failed to start git");

                    var stdout = proc.StandardOutput.ReadToEnd();
                    var stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);

                    var output = string.IsNullOrEmpty(stdout) ? stderr : stdout;
                    return (proc.ExitCode == 0, output.Trim());
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });
        }

        private void NotifyChanged()
        {
            EditorApplication.delayCall += () => OnStatusChanged?.Invoke();
        }
    }
}
