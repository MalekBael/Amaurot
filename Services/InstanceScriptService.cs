using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Amaurot.Services;

namespace Amaurot.Services
{
    public class InstanceScriptService : BaseScriptService
    {
        public InstanceScriptService(SettingsService settingsService, Action<string>? logDebug = null)
            : base(settingsService, logDebug)
        {
        }

        #region Instance-Specific Methods

        public string? FindInstanceScript(string instanceName, uint instanceId, bool isHardMode = false)
        {
            if (string.IsNullOrEmpty(instanceName) || !_settingsService.IsValidSapphireServerPath())
            {
                return null;
            }

            var sapphirePath = _settingsService.Settings.SapphireServerPath;
            var instanceScriptPaths = new[]
            {
                Path.Combine(sapphirePath, "src", "scripts", "instances"),
                Path.Combine(sapphirePath, "src", "scripts", "instances", "dungeons"),
                Path.Combine(sapphirePath, "src", "scripts", "instances", "raids"),
                Path.Combine(sapphirePath, "src", "scripts", "instances", "trials"),
                Path.Combine(sapphirePath, "src", "scripts", "instances", "guildhests"),
                Path.Combine(sapphirePath, "src", "scripts", "instances", "pvp"),
                Path.Combine(sapphirePath, "src", "scripts", "instances", "questbattles")
            };

            foreach (var scriptsPath in instanceScriptPaths)
            {
                if (!Directory.Exists(scriptsPath))
                {
                    continue;
                }

                var foundScript = TryFindInstanceScriptFile(scriptsPath, instanceName, instanceId, isHardMode);
                if (foundScript != null)
                {
                    _logDebug?.Invoke($"Found instance script: {foundScript}");
                    return foundScript;
                }
            }

            _logDebug?.Invoke($"Instance script not found for: {instanceName} (ID: {instanceId}, Hard: {isHardMode})");
            return null;
        }

        private string? TryFindInstanceScriptFile(string scriptsPath, string instanceName, uint instanceId, bool isHardMode)
        {
            try
            {
                string cleanName = instanceName
                    .Replace("(Hard)", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ", "")
                    .Trim();

                var idMatch = SearchForIdMatch(scriptsPath, instanceId);
                if (idMatch != null)
                {
                    _logDebug?.Invoke($"Found ID match: {Path.GetFileName(idMatch)}");
                    return idMatch;
                }

                var nameMatch = SearchForNameMatch(scriptsPath, cleanName, isHardMode);
                if (nameMatch != null)
                {
                    _logDebug?.Invoke($"Found name match: {Path.GetFileName(nameMatch)} for {instanceName}");
                    return nameMatch;
                }

                var patternMatch = SearchForInstancePatternMatch(scriptsPath, cleanName, isHardMode);
                if (patternMatch != null)
                {
                    _logDebug?.Invoke($"Found pattern match: {Path.GetFileName(patternMatch)} for {instanceName}");
                    return patternMatch;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error searching for instance script {instanceName}: {ex.Message}");
                return null;
            }
        }

        private string? SearchForIdMatch(string scriptsPath, uint instanceId)
        {
            var scriptFileName = $"{instanceId}.cpp";

            var mainScriptPath = Path.Combine(scriptsPath, scriptFileName);
            if (File.Exists(mainScriptPath))
            {
                return mainScriptPath;
            }

            var foundFiles = Directory.GetFiles(scriptsPath, scriptFileName, SearchOption.AllDirectories);
            return foundFiles.Length > 0 ? foundFiles[0] : null;
        }

        private string? SearchForNameMatch(string scriptsPath, string cleanName, bool isHardMode)
        {
            var searchPatterns = new List<string>();

            if (isHardMode)
            {
                searchPatterns.AddRange(new[]
                {
                    $"{cleanName}Hard.cpp",
                    $"{cleanName}_Hard.cpp"
                });
            }
            else
            {
                searchPatterns.Add($"{cleanName}.cpp");
            }

            foreach (var pattern in searchPatterns)
            {
                var mainScriptPath = Path.Combine(scriptsPath, pattern);
                if (File.Exists(mainScriptPath))
                {
                    return mainScriptPath;
                }

                var foundFiles = Directory.GetFiles(scriptsPath, pattern, SearchOption.AllDirectories);
                if (foundFiles.Length > 0)
                {
                    return foundFiles[0];
                }
            }

            return null;
        }

        private string? SearchForInstancePatternMatch(string scriptsPath, string cleanName, bool isHardMode)
        {
            try
            {
                var allScriptFiles = Directory.GetFiles(scriptsPath, "*.cpp", SearchOption.AllDirectories);

                var matchingFiles = allScriptFiles.Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);

                    if (isHardMode)
                    {
                        return fileName.Contains("Hard", StringComparison.OrdinalIgnoreCase) &&
                               fileName.StartsWith(cleanName, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        return fileName.StartsWith(cleanName, StringComparison.OrdinalIgnoreCase) &&
                               !fileName.Contains("Hard", StringComparison.OrdinalIgnoreCase);
                    }
                }).ToList();

                if (matchingFiles.Count > 0)
                {
                    var exactMatch = isHardMode
                        ? matchingFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals($"{cleanName}Hard", StringComparison.OrdinalIgnoreCase))
                        : matchingFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(cleanName, StringComparison.OrdinalIgnoreCase));

                    if (exactMatch != null)
                    {
                        return exactMatch;
                    }

                    return matchingFiles
                        .OrderBy(f => Path.GetFileNameWithoutExtension(f).Length)
                        .First();
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error in instance pattern-based search: {ex.Message}");
                return null;
            }
        }

        public InstanceScriptInfo GetInstanceScriptInfo(string instanceName, uint instanceId, bool isHardMode = false)
        {
            var scriptPath = FindInstanceScript(instanceName, instanceId, isHardMode);

            return new InstanceScriptInfo
            {
                InstanceName = instanceName,
                InstanceId = instanceId,
                IsHardMode = isHardMode,
                ScriptPath = scriptPath,
                Exists = !string.IsNullOrEmpty(scriptPath),
                CanOpenInVSCode = IsVSCodeAvailable(),
                CanOpenInVisualStudio = IsVisualStudioAvailable()
            };
        }

        #endregion
    }

    public class InstanceScriptInfo
    {
        public string InstanceName { get; set; } = string.Empty;
        public uint InstanceId { get; set; }
        public bool IsHardMode { get; set; }
        public string? ScriptPath { get; set; }
        public bool Exists { get; set; }
        public bool CanOpenInVSCode { get; set; }
        public bool CanOpenInVisualStudio { get; set; }
    }
}