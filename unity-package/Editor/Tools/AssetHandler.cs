using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles MCP commands for managing Unity project assets — search, move, rename, delete, info.
    /// </summary>
    public class AssetHandler : IToolHandler
    {
        public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                try { tcs.SetResult(Execute(paramsJson)); }
                catch (Exception ex) { tcs.SetResult(new CommandResult { success = false, error = ex.Message }); }
            };
            EditorApplication.update += callback;
            return tcs.Task;
        }

        private CommandResult Execute(string paramsJson)
        {
            var p = new ToolParams(paramsJson);
            var action = p.RequireString("action");

            switch (action)
            {
                case "search": return SearchAssets(p);
                case "info": return GetAssetInfo(p);
                case "move": return MoveAsset(p);
                case "rename": return RenameAsset(p);
                case "delete": return DeleteAsset(p);
                case "import": return ImportAsset(p);
                case "create_folder": return CreateFolder(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown asset action: {action}" };
            }
        }

        private CommandResult SearchAssets(ToolParams p)
        {
            var searchTerm = p.GetString("search_term") ?? p.GetString("searchTerm") ?? "";
            var assetType = p.GetString("asset_type") ?? p.GetString("assetType");
            var pageSize = p.GetInt("page_size") ?? p.GetInt("pageSize") ?? 50;
            var cursor = p.GetInt("cursor") ?? 0;

            var filter = searchTerm;
            if (!string.IsNullOrEmpty(assetType))
                filter = $"t:{assetType} {searchTerm}";

            var guids = AssetDatabase.FindAssets(filter);
            var results = new List<Dictionary<string, object>>();

            var startIndex = Math.Min(cursor, guids.Length);
            var endIndex = Math.Min(startIndex + pageSize, guids.Length);

            for (var i = startIndex; i < endIndex; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                results.Add(new Dictionary<string, object>
                {
                    { "path", path },
                    { "name", Path.GetFileName(path) },
                    { "type", asset != null ? asset.GetType().Name : "Unknown" },
                    { "guid", guids[i] }
                });
            }

            var data = new Dictionary<string, object>
            {
                { "results", results },
                { "total", guids.Length },
                { "count", results.Count },
                { "cursor", cursor }
            };

            if (endIndex < guids.Length)
                data["next_cursor"] = endIndex;

            return new CommandResult { success = true, data = data };
        }

        private CommandResult GetAssetInfo(ToolParams p)
        {
            var path = p.RequireString("path");
            var asset = AssetDatabase.LoadMainAssetAtPath(path);

            if (asset == null)
                return new CommandResult { success = false, error = $"Asset not found: {path}" };

            var importer = AssetImporter.GetAtPath(path);
            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            var fileInfo = File.Exists(fullPath) ? new FileInfo(fullPath) : null;

            var info = new Dictionary<string, object>
            {
                { "path", path },
                { "name", asset.name },
                { "type", asset.GetType().Name },
                { "fullType", asset.GetType().FullName },
                { "guid", AssetDatabase.AssetPathToGUID(path) },
                { "instanceId", asset.GetInstanceID() }
            };

            if (fileInfo != null)
            {
                info["size"] = fileInfo.Length;
                info["lastModified"] = fileInfo.LastWriteTimeUtc.ToString("o");
            }

            if (importer != null)
                info["importerType"] = importer.GetType().Name;

            // Get labels
            var labels = AssetDatabase.GetLabels(asset);
            if (labels.Length > 0)
                info["labels"] = new List<object>(labels.Cast<object>());

            // Get dependencies
            var deps = AssetDatabase.GetDependencies(path, false);
            if (deps.Length > 0)
                info["dependencies"] = new List<object>(deps.Cast<object>());

            return new CommandResult { success = true, data = info };
        }

        private CommandResult MoveAsset(ToolParams p)
        {
            var path = p.RequireString("path");
            var newPath = p.RequireString("new_path") ?? p.RequireString("newPath");

            var error = AssetDatabase.MoveAsset(path, newPath);
            if (!string.IsNullOrEmpty(error))
                return new CommandResult { success = false, error = error };

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "from", path },
                    { "to", newPath },
                    { "moved", true }
                }
            };
        }

        private CommandResult RenameAsset(ToolParams p)
        {
            var path = p.RequireString("path");
            var newName = p.RequireString("new_name") ?? p.RequireString("newName");

            var error = AssetDatabase.RenameAsset(path, newName);
            if (!string.IsNullOrEmpty(error))
                return new CommandResult { success = false, error = error };

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "path", path },
                    { "newName", newName },
                    { "renamed", true }
                }
            };
        }

        private CommandResult DeleteAsset(ToolParams p)
        {
            var path = p.RequireString("path");

            if (!AssetDatabase.DeleteAsset(path))
                return new CommandResult { success = false, error = $"Failed to delete asset: {path}" };

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object> { { "deleted", path } }
            };
        }

        private CommandResult ImportAsset(ToolParams p)
        {
            var path = p.RequireString("path");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object> { { "imported", path } }
            };
        }

        private CommandResult CreateFolder(ToolParams p)
        {
            var folderPath = p.RequireString("folder_path") ?? p.RequireString("folderPath");
            var parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/") ?? "Assets";
            var folderName = Path.GetFileName(folderPath);

            if (AssetDatabase.IsValidFolder(folderPath))
                return new CommandResult { success = true, data = new Dictionary<string, object> { { "path", folderPath }, { "already_exists", true } } };

            var guid = AssetDatabase.CreateFolder(parent, folderName);
            if (string.IsNullOrEmpty(guid))
                return new CommandResult { success = false, error = $"Failed to create folder: {folderPath}" };

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "path", folderPath },
                    { "guid", guid },
                    { "created", true }
                }
            };
        }
    }
}
