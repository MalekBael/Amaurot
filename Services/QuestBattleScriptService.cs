using System;
using System.IO;
using System.Linq;
using Amaurot.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using QuestBattleInfo = Amaurot.Services.Entities.QuestBattleInfo;

namespace Amaurot.Services
{
    public class QuestBattleScriptService : BaseScriptService
    {
        public QuestBattleScriptService(SettingsService settingsService)
            : base(settingsService)
        {
        }

        /// <summary>
        /// Loads all Quest Battle scripts from the Sapphire repository
        /// </summary>
        public async Task<List<QuestBattleInfo>> LoadQuestBattlesFromScriptsAsync()
        {
            return await Task.Run(() => LoadQuestBattlesFromScripts());
        }

        private List<QuestBattleInfo> LoadQuestBattlesFromScripts()
        {
            var questBattles = new List<QuestBattleInfo>();

            try
            {
                DebugModeManager.LogDebug("🎮 Loading Quest Battles from Sapphire repository scripts...");

                if (!_settingsService.IsValidSapphireServerPath())
                {
                    DebugModeManager.LogError("❌ Sapphire Server path not configured - cannot load Quest Battle scripts");
                    return questBattles;
                }

                var sapphirePath = _settingsService.Settings.SapphireServerPath;
                var questBattleScriptsPath = Path.Combine(sapphirePath, "src", "scripts", "instances", "questbattles");

                if (!Directory.Exists(questBattleScriptsPath))
                {
                    DebugModeManager.LogWarning($"⚠️ Quest Battle scripts directory not found: {questBattleScriptsPath}");
                    return questBattles;
                }

                DebugModeManager.LogDebug($"📁 Scanning Quest Battle scripts in: {questBattleScriptsPath}");

                var scriptFiles = Directory.GetFiles(questBattleScriptsPath, "*.cpp", SearchOption.AllDirectories);
                DebugModeManager.LogDebug($"📄 Found {scriptFiles.Length} Quest Battle script files");

                foreach (var scriptFile in scriptFiles)
                {
                    try
                    {
                        var questBattleInfo = CreateQuestBattleInfoFromScript(scriptFile);
                        if (questBattleInfo != null)
                        {
                            questBattles.Add(questBattleInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugModeManager.LogError($"Error processing Quest Battle script {scriptFile}: {ex.Message}");
                    }
                }

                questBattles = questBattles.OrderBy(qb => qb.QuestBattleName).ToList();
                DebugModeManager.LogDataLoading("Quest Battle Scripts", questBattles.Count, "from Sapphire repository");

                // Log first few results
                if (questBattles.Count > 0)
                {
                    DebugModeManager.LogDebug("✅ First few Quest Battles found:");
                    foreach (var qb in questBattles.Take(5))
                    {
                        DebugModeManager.LogDebug($"  🗡️ {qb.QuestBattleName} (Script: {Path.GetFileName(qb.Source)})");
                    }
                }
                else
                {
                    DebugModeManager.LogWarning("⚠️ No Quest Battle scripts found in repository");
                }
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"❌ Error loading Quest Battle scripts: {ex.Message}");
            }

            return questBattles;
        }

        private QuestBattleInfo? CreateQuestBattleInfoFromScript(string scriptFilePath)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(scriptFilePath);
                var fileNameWithExt = Path.GetFileName(scriptFilePath);

                // Try to extract ID from filename if it starts with a number
                uint questBattleId = 0;
                string questBattleName = fileName;

                // Check if filename starts with a number (e.g., "123_SomeName.cpp")
                var parts = fileName.Split('_');
                if (parts.Length > 0 && uint.TryParse(parts[0], out var parsedId))
                {
                    questBattleId = parsedId;
                    questBattleName = parts.Length > 1 ? string.Join("_", parts.Skip(1)) : fileName;
                }
                else
                {
                    // Generate ID from filename hash if no numeric ID found
                    questBattleId = (uint)Math.Abs(fileName.GetHashCode());
                }

                // Clean up the quest battle name for display
                questBattleName = CleanQuestBattleName(questBattleName);

                // Try to extract additional metadata from the script file
                var scriptMetadata = ExtractScriptMetadata(scriptFilePath);

                var questBattleInfo = new QuestBattleInfo
                {
                    Id = questBattleId,
                    Name = questBattleName,
                    QuestBattleName = questBattleName,
                    TerritoryId = scriptMetadata.TerritoryId,
                    TerritoryName = scriptMetadata.TerritoryName,
                    MapId = scriptMetadata.MapId,
                    MapX = 0, // Scripts don't contain coordinate data
                    MapY = 0,
                    MapZ = 0,
                    LayerName = fileName,
                    AssetType = "Script",
                    Source = fileNameWithExt,
                    IconId = 61806, // Quest Battle icon
                    IconPath = "ui/icon/061000/061806.tex"
                };

                return questBattleInfo;
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error creating QuestBattleInfo from script {scriptFilePath}: {ex.Message}");
                return null;
            }
        }

        private string CleanQuestBattleName(string rawName)
        {
            // Convert CamelCase to readable format with multiple passes
            var cleaned = rawName;

            // Pass 1: Handle lowercase followed by uppercase (standard camelCase)
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "([a-z])([A-Z])", "$1 $2");

            // Pass 2: Handle uppercase followed by uppercase+lowercase (PascalCase new words)
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "([A-Z])([A-Z][a-z])", "$1 $2");

            // Pass 3: Handle sequences of lowercase words followed by uppercase
            // This catches cases like "againstthe" + "Shadow" -> "against the" + " Shadow"
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"([a-z])([A-Z])", "$1 $2");

            // Pass 4: Handle remaining adjacent words that might have been missed
            // Look for word boundaries where lowercase meets uppercase
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"([a-z]{2,})([A-Z][a-z])", "$1 $2");

            // Remove common prefixes/suffixes
            cleaned = cleaned
                .Replace("QuestBattle", "")
                .Replace("QB_", "")
                .Replace("_", " ")
                .Trim();

            // Clean up multiple spaces
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");

            // Capitalize first letter of each word for proper title case
            if (!string.IsNullOrEmpty(cleaned))
            {
                var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < words.Length; i++)
                {
                    if (words[i].Length > 0)
                    {
                        words[i] = char.ToUpper(words[i][0]) +
                                  (words[i].Length > 1 ? words[i].Substring(1).ToLower() : "");
                    }
                }
                cleaned = string.Join(" ", words);
            }

            return string.IsNullOrEmpty(cleaned) ? rawName : cleaned;
        }

        private ScriptMetadata ExtractScriptMetadata(string scriptFilePath)
        {
            var metadata = new ScriptMetadata();

            try
            {
                // Read first few lines of the script to extract metadata
                var lines = File.ReadLines(scriptFilePath).Take(20).ToArray();

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    // Look for territory ID comments like: // Territory: 123
                    if (trimmedLine.StartsWith("//") && trimmedLine.Contains("Territory"))
                    {
                        var territoryPart = trimmedLine.Substring(trimmedLine.IndexOf("Territory")).Replace("Territory", "").Trim(':').Trim();
                        if (uint.TryParse(territoryPart.Split(' ')[0], out var territoryId))
                        {
                            metadata.TerritoryId = territoryId;
                        }
                    }

                    // Look for zone/map comments
                    if (trimmedLine.StartsWith("//") && (trimmedLine.Contains("Zone:") || trimmedLine.Contains("Map:")))
                    {
                        var zonePart = trimmedLine.Substring(2).Replace("Zone:", "").Replace("Map:", "").Trim();
                        metadata.TerritoryName = zonePart;
                    }

                    // Look for instance content ID
                    if (trimmedLine.Contains("InstanceContentId") || trimmedLine.Contains("setInstance"))
                    {
                        // Extract numeric value from code like: setInstance(123);
                        var numbers = System.Text.RegularExpressions.Regex.Matches(trimmedLine, @"\d+");
                        if (numbers.Count > 0 && uint.TryParse(numbers[0].Value, out var mapId))
                        {
                            metadata.MapId = mapId;
                        }
                    }
                }

                // Set default territory name if not found
                if (string.IsNullOrEmpty(metadata.TerritoryName))
                {
                    metadata.TerritoryName = "Unknown Territory";
                }
            }
            catch (Exception ex)
            {
                DebugModeManager.LogDebug($"Could not extract metadata from script {scriptFilePath}: {ex.Message}");
            }

            return metadata;
        }

        /// <summary>
        /// Finds a specific Quest Battle script file
        /// </summary>
        public string? FindQuestBattleScript(string questBattleName, uint? questBattleId = null)
        {
            if (!_settingsService.IsValidSapphireServerPath())
            {
                return null;
            }

            var sapphirePath = _settingsService.Settings.SapphireServerPath;
            var questBattleScriptsPath = Path.Combine(sapphirePath, "src", "scripts", "instances", "questbattles");

            if (!Directory.Exists(questBattleScriptsPath))
            {
                return null;
            }

            try
            {
                var scriptFiles = Directory.GetFiles(questBattleScriptsPath, "*.cpp", SearchOption.AllDirectories);

                // Try ID match first if provided
                if (questBattleId.HasValue)
                {
                    var idMatch = scriptFiles.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).StartsWith(questBattleId.Value.ToString()));
                    if (idMatch != null)
                    {
                        return idMatch;
                    }
                }

                // Try name match
                var cleanName = questBattleName.Replace(" ", "").Replace("_", "");
                var nameMatch = scriptFiles.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Contains(cleanName, StringComparison.OrdinalIgnoreCase));

                return nameMatch;
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error finding Quest Battle script: {ex.Message}");
                return null;
            }
        }

        #region Enhanced Quest Battle Script Management

        /// <summary>
        /// Gets enhanced script information for a quest battle
        /// </summary>
        public QuestBattleScriptInfoExtended GetQuestBattleScriptInfoExtended(string questBattleName, uint? questBattleId = null)
        {
            var scriptPath = FindQuestBattleScript(questBattleName, questBattleId);
            
            bool canSearchForScripts = _settingsService.IsValidSapphireServerPath();

            return new QuestBattleScriptInfoExtended
            {
                QuestBattleName = questBattleName,
                QuestBattleId = questBattleId,
                ScriptPath = scriptPath,
                Exists = !string.IsNullOrEmpty(scriptPath),
                CanOpenInVSCode = IsVSCodeAvailable() && canSearchForScripts,
                CanOpenInVisualStudio = IsVisualStudioAvailable() && canSearchForScripts
            };
        }

        /// <summary>
        /// Finds all script files related to a quest battle (C++ only for now)
        /// </summary>
        public string[] FindQuestBattleScriptFiles(string questBattleName, uint? questBattleId = null)
        {
            var foundFiles = new List<string>();

            var cppScript = FindQuestBattleScript(questBattleName, questBattleId);
            if (!string.IsNullOrEmpty(cppScript))
            {
                foundFiles.Add(cppScript);
            }

            DebugModeManager.LogDebug($"Found {foundFiles.Count} files for quest battle '{questBattleName}': " +
                            $"{string.Join(", ", foundFiles.Select(Path.GetFileName))}");

            return foundFiles.ToArray();
        }

        /// <summary>
        /// Checks if a quest battle has any scripts available
        /// </summary>
        public bool HasQuestBattleScript(string questBattleName, uint? questBattleId = null)
        {
            if (string.IsNullOrEmpty(questBattleName))
            {
                DebugModeManager.LogDebug($"HasQuestBattleScript: Empty questBattleName");
                return false;
            }

            try
            {
                var repoScript = FindQuestBattleScript(questBattleName, questBattleId);
                if (!string.IsNullOrEmpty(repoScript))
                {
                    DebugModeManager.LogDebug($"HasQuestBattleScript({questBattleName}): Found in repo - {Path.GetFileName(repoScript)}");
                    return true;
                }

                DebugModeManager.LogDebug($"HasQuestBattleScript({questBattleName}): No scripts found");
                return false;
            }
            catch (Exception ex)
            {
                DebugModeManager.LogDebug($"HasQuestBattleScript({questBattleName}): Error - {ex.Message}");
                return false;
            }
        }

        #endregion Enhanced Quest Battle Script Management

        private class ScriptMetadata
        {
            public uint TerritoryId { get; set; } = 0;
            public string TerritoryName { get; set; } = string.Empty;
            public uint MapId { get; set; } = 0;
        }
    }

    /// <summary>
    /// Enhanced script information for quest battles
    /// </summary>
    public class QuestBattleScriptInfoExtended
    {
        public string QuestBattleName { get; set; } = string.Empty;
        public uint? QuestBattleId { get; set; }
        public string? ScriptPath { get; set; }
        public bool Exists { get; set; }
        public bool CanOpenInVSCode { get; set; }
        public bool CanOpenInVisualStudio { get; set; }
    }
}