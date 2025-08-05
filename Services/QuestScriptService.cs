using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Amaurot.Services;

namespace Amaurot.Services
{
    public class QuestScriptService
    {
        private readonly SettingsService _settingsService;
        private readonly Action<string>? _logDebug;

        public QuestScriptService(SettingsService settingsService, Action<string>? logDebug = null)
        {
            _settingsService = settingsService;
            _logDebug = logDebug;
        }

        /// <summary>
        /// Finds the quest script file for a given quest identifier
        /// Supports both exact matches and shortened versions (e.g., "SubFst005_00028" can match "SubFst005")
        /// </summary>
        /// <param name="questIdString">Quest identifier like "ClsHarv001_00003" or "SubFst005_00028"</param>
        /// <returns>Full path to the script file if found, null otherwise</returns>
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

            // Try to find the script using multiple search strategies
            var foundScript = TryFindScriptFile(scriptsPath, questIdString);
            if (foundScript != null)
            {
                _logDebug?.Invoke($"Found quest script: {foundScript}");
                return foundScript;
            }

            _logDebug?.Invoke($"Quest script not found for: {questIdString}");
            return null;
        }

        /// <summary>
        /// Attempts to find a script file using multiple search strategies
        /// </summary>
        private string? TryFindScriptFile(string scriptsPath, string questIdString)
        {
            try
            {
                // Strategy 1: Exact match search
                var exactMatch = SearchForExactMatch(scriptsPath, questIdString);
                if (exactMatch != null)
                {
                    _logDebug?.Invoke($"Found exact match: {Path.GetFileName(exactMatch)}");
                    return exactMatch;
                }

                // Strategy 2: Shortened version search (e.g., "SubFst005_00028" → "SubFst005")
                var shortenedMatch = SearchForShortenedVersion(scriptsPath, questIdString);
                if (shortenedMatch != null)
                {
                    _logDebug?.Invoke($"Found shortened version match: {Path.GetFileName(shortenedMatch)} for {questIdString}");
                    return shortenedMatch;
                }

                // Strategy 3: Pattern-based search for variations
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

        /// <summary>
        /// Search for exact match: "SubFst005_00028.cpp"
        /// </summary>
        private string? SearchForExactMatch(string scriptsPath, string questIdString)
        {
            var scriptFileName = $"{questIdString}.cpp";

            // Check main directory first
            var mainScriptPath = Path.Combine(scriptsPath, scriptFileName);
            if (File.Exists(mainScriptPath))
            {
                return mainScriptPath;
            }

            // Search in subdirectories
            var foundFiles = Directory.GetFiles(scriptsPath, scriptFileName, SearchOption.AllDirectories);
            return foundFiles.Length > 0 ? foundFiles[0] : null;
        }

        /// <summary>
        /// Search for shortened version: "SubFst005_00028" → "SubFst005.cpp"
        /// </summary>
        private string? SearchForShortenedVersion(string scriptsPath, string questIdString)
        {
            // Look for underscore patterns that might indicate we can shorten the quest ID
            var underscoreIndex = questIdString.LastIndexOf('_');
            if (underscoreIndex == -1)
            {
                return null; // No underscore, can't shorten
            }

            // Extract the base part before the last underscore
            var basePart = questIdString.Substring(0, underscoreIndex);
            var afterUnderscore = questIdString.Substring(underscoreIndex + 1);

            // Only shorten if the part after underscore looks like a numeric suffix
            if (afterUnderscore.All(char.IsDigit) && afterUnderscore.Length >= 3)
            {
                var shortenedFileName = $"{basePart}.cpp";

                // Check main directory first
                var mainScriptPath = Path.Combine(scriptsPath, shortenedFileName);
                if (File.Exists(mainScriptPath))
                {
                    return mainScriptPath;
                }

                // Search in subdirectories
                var foundFiles = Directory.GetFiles(scriptsPath, shortenedFileName, SearchOption.AllDirectories);
                return foundFiles.Length > 0 ? foundFiles[0] : null;
            }

            return null;
        }

        /// <summary>
        /// Search for pattern-based matches (additional flexibility for future quest naming patterns)
        /// </summary>
        private string? SearchForPatternMatch(string scriptsPath, string questIdString)
        {
            try
            {
                // Get all .cpp files in the scripts directory
                var allScriptFiles = Directory.GetFiles(scriptsPath, "*.cpp", SearchOption.AllDirectories);

                // Look for files that start with the same base pattern
                var questBase = questIdString;
                var underscoreIndex = questIdString.LastIndexOf('_');

                if (underscoreIndex > 0)
                {
                    questBase = questIdString.Substring(0, underscoreIndex);
                }

                // Find files that start with the base pattern
                var matchingFiles = allScriptFiles.Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    return fileName.StartsWith(questBase, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (matchingFiles.Count > 0)
                {
                    // Prefer exact matches, then shorter names, then longer names
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

        /// <summary>
        /// Opens a quest script file in Visual Studio Code
        /// </summary>
        /// <param name="scriptPath">Full path to the script file</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool OpenInVSCode(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                return false;
            }

            try
            {
                _logDebug?.Invoke($"Opening {scriptPath} in VSCode");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(processInfo);
                return process != null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error opening script in VSCode: {ex.Message}");

                // Fallback: try opening with shell execute
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = scriptPath,
                        UseShellExecute = true
                    });
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Opens a quest script file in Visual Studio
        /// </summary>
        /// <param name="scriptPath">Full path to the script file</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool OpenInVisualStudio(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                return false;
            }

            try
            {
                _logDebug?.Invoke($"Opening {scriptPath} in Visual Studio");

                // Try common Visual Studio executable names
                var vsExecutables = new[]
                {
                    "devenv.exe",
                    "devenv",
                };

                foreach (var vsExe in vsExecutables)
                {
                    try
                    {
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = vsExe,
                            Arguments = $"\"{scriptPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        var process = Process.Start(processInfo);
                        if (process != null)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Continue to next executable
                        continue;
                    }
                }

                // Fallback: try opening with shell execute
                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error opening script in Visual Studio: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets information about a quest script
        /// </summary>
        /// <param name="questIdString">Quest identifier</param>
        /// <returns>Information about the script file</returns>
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

        /// <summary>
        /// Checks if Visual Studio Code is available
        /// </summary>
        private bool IsVSCodeAvailable()
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });

                if (process != null)
                {
                    process.WaitForExit(3000); // Wait up to 3 seconds
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                // VSCode not available
            }

            return false;
        }

        /// <summary>
        /// Checks if Visual Studio is available
        /// </summary>
        private bool IsVisualStudioAvailable()
        {
            var vsExecutables = new[] { "devenv.exe", "devenv" };

            foreach (var vsExe in vsExecutables)
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = vsExe,
                        Arguments = "/?",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        process.WaitForExit(3000); // Wait up to 3 seconds
                        if (process.ExitCode == 0)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // This VS executable not available
                    continue;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Information about a quest script file
    /// </summary>
    public class QuestScriptInfo
    {
        public string QuestIdString { get; set; } = string.Empty;
        public string? ScriptPath { get; set; }
        public bool Exists { get; set; }
        public bool CanOpenInVSCode { get; set; }
        public bool CanOpenInVisualStudio { get; set; }
    }
}