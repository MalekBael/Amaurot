using System;
using System.IO;
using System.Linq;
using Amaurot.Services;

namespace Amaurot.Services
{
    public class QuestScriptService : BaseScriptService
    {
        public QuestScriptService(SettingsService settingsService, Action<string>? logDebug = null)
            : base(settingsService, logDebug)
        {
        }

        #region Quest-Specific Methods

        public string? FindQuestScript(string questIdString)
        {
            if (string.IsNullOrEmpty(questIdString) || !_settingsService.IsValidSapphireServerPath())
            {
                return null;
            }

            var sapphirePath = _settingsService.Settings.SapphireServerPath;
            var scriptsPath = Path.Combine(sapphirePath, "src", "scripts", "quest");

            if (!Directory.Exists(scriptsPath))
            {
                _logDebug?.Invoke($"Quest scripts directory not found: {scriptsPath}");
                return null;
            }

            var foundScript = TryFindScriptFile(scriptsPath, questIdString);
            if (foundScript != null)
            {
                _logDebug?.Invoke($"Found quest script: {foundScript}");
                return foundScript;
            }

            _logDebug?.Invoke($"Quest script not found for: {questIdString}");
            return null;
        }

        private string? TryFindScriptFile(string scriptsPath, string questIdString)
        {
            try
            {
                var exactMatch = SearchForExactMatch(scriptsPath, questIdString);
                if (exactMatch != null)
                {
                    _logDebug?.Invoke($"Found exact match: {Path.GetFileName(exactMatch)}");
                    return exactMatch;
                }

                var shortenedMatch = SearchForShortenedVersion(scriptsPath, questIdString);
                if (shortenedMatch != null)
                {
                    _logDebug?.Invoke($"Found shortened version match: {Path.GetFileName(shortenedMatch)} for {questIdString}");
                    return shortenedMatch;
                }

                var patternMatch = SearchForPatternMatch(scriptsPath, questIdString);
                if (patternMatch != null)
                {
                    _logDebug?.Invoke($"Found pattern match: {Path.GetFileName(patternMatch)} for {questIdString}");
                    return patternMatch;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error searching for quest script {questIdString}: {ex.Message}");
                return null;
            }
        }

        private string? SearchForExactMatch(string scriptsPath, string questIdString)
        {
            var scriptFileName = $"{questIdString}.cpp";

            var mainScriptPath = Path.Combine(scriptsPath, scriptFileName);
            if (File.Exists(mainScriptPath))
            {
                return mainScriptPath;
            }

            var foundFiles = Directory.GetFiles(scriptsPath, scriptFileName, SearchOption.AllDirectories);
            return foundFiles.Length > 0 ? foundFiles[0] : null;
        }

        private string? SearchForShortenedVersion(string scriptsPath, string questIdString)
        {
            var underscoreIndex = questIdString.LastIndexOf('_');
            if (underscoreIndex == -1)
            {
                return null;
            }

            var basePart = questIdString.Substring(0, underscoreIndex);
            var afterUnderscore = questIdString.Substring(underscoreIndex + 1);

            if (afterUnderscore.All(char.IsDigit) && afterUnderscore.Length >= 3)
            {
                var shortenedFileName = $"{basePart}.cpp";

                var mainScriptPath = Path.Combine(scriptsPath, shortenedFileName);
                if (File.Exists(mainScriptPath))
                {
                    return mainScriptPath;
                }

                var foundFiles = Directory.GetFiles(scriptsPath, shortenedFileName, SearchOption.AllDirectories);
                return foundFiles.Length > 0 ? foundFiles[0] : null;
            }

            return null;
        }

        private string? SearchForPatternMatch(string scriptsPath, string questIdString)
        {
            try
            {
                var allScriptFiles = Directory.GetFiles(scriptsPath, "*.cpp", SearchOption.AllDirectories);

                var questBase = questIdString;
                var underscoreIndex = questIdString.LastIndexOf('_');

                if (underscoreIndex > 0)
                {
                    questBase = questIdString.Substring(0, underscoreIndex);
                }

                var matchingFiles = allScriptFiles.Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    return fileName.StartsWith(questBase, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (matchingFiles.Count > 0)
                {
                    var bestMatch = matchingFiles
                        .OrderBy(f => f.Equals($"{questIdString}.cpp", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(f => Path.GetFileNameWithoutExtension(f).Length)
                        .First();

                    return bestMatch;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error in pattern-based search: {ex.Message}");
                return null;
            }
        }

        public QuestScriptInfo GetQuestScriptInfo(string questIdString)
        {
            var scriptPath = FindQuestScript(questIdString);

            return new QuestScriptInfo
            {
                QuestIdString = questIdString,
                ScriptPath = scriptPath,
                Exists = !string.IsNullOrEmpty(scriptPath),
                CanOpenInVSCode = IsVSCodeAvailable(),
                CanOpenInVisualStudio = IsVisualStudioAvailable()
            };
        }

        #endregion
    }

    public class QuestScriptInfo
    {
        public string QuestIdString { get; set; } = string.Empty;
        public string? ScriptPath { get; set; }
        public bool Exists { get; set; }
        public bool CanOpenInVSCode { get; set; }
        public bool CanOpenInVisualStudio { get; set; }
    }
}