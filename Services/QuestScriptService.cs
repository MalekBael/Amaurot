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
                _logDebug?.Invoke($"Attempting to open {scriptPath} in VSCode");

                // Strategy 1: Try the 'code' command first
                var processInfo = new ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true  // ✅ ADD: Capture errors
                };

                var process = Process.Start(processInfo);
                if (process != null)
                {
                    process.WaitForExit(3000); // Wait up to 3 seconds
                    if (process.ExitCode == 0)
                    {
                        _logDebug?.Invoke($"✅ Successfully opened {scriptPath} in VSCode via 'code' command");
                        return true;
                    }
                    else
                    {
                        _logDebug?.Invoke($"❌ 'code' command failed with exit code: {process.ExitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"❌ Error running 'code' command: {ex.Message}");
            }

            // Strategy 2: Try common VSCode installation paths
            var vscodeExecutables = new[]
            {
                @"C:\Users\{0}\AppData\Local\Programs\Microsoft VS Code\Code.exe",
                @"C:\Program Files\Microsoft VS Code\Code.exe",
                @"C:\Program Files (x86)\Microsoft VS Code\Code.exe"
            };

            var username = Environment.UserName;
            foreach (var pathTemplate in vscodeExecutables)
            {
                var fullPath = pathTemplate.Contains("{0}")
                    ? string.Format(pathTemplate, username)
                    : pathTemplate;

                if (File.Exists(fullPath))
                {
                    try
                    {
                        _logDebug?.Invoke($"Attempting to open {scriptPath} using VSCode at: {fullPath}");

                        var processInfo = new ProcessStartInfo
                        {
                            FileName = fullPath,
                            Arguments = $"\"{scriptPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        var process = Process.Start(processInfo);
                        if (process != null)
                        {
                            _logDebug?.Invoke($"✅ Successfully opened {scriptPath} in VSCode at: {fullPath}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logDebug?.Invoke($"❌ Error opening VSCode at {fullPath}: {ex.Message}");
                        continue; // Try next path
                    }
                }
            }

            // ✅ REMOVED: The problematic fallback that was opening Visual Studio
            // DO NOT fall back to UseShellExecute = true as it opens the default app (Visual Studio)

            _logDebug?.Invoke($"❌ Failed to open {scriptPath} in VSCode - no working VSCode installation found");
            return false;
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
                _logDebug?.Invoke($"Attempting to open {scriptPath} in Visual Studio");

                // Strategy 1: Try devenv command first
                var vsCommands = new[] { "devenv.exe", "devenv" };

                foreach (var vsCmd in vsCommands)
                {
                    try
                    {
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = vsCmd,
                            Arguments = $"\"{scriptPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true
                        };

                        var process = Process.Start(processInfo);
                        if (process != null)
                        {
                            process.WaitForExit(3000); // Wait up to 3 seconds
                            if (process.ExitCode == 0)
                            {
                                _logDebug?.Invoke($"✅ Successfully opened {scriptPath} in Visual Studio via '{vsCmd}' command");
                                return true;
                            }
                            else
                            {
                                _logDebug?.Invoke($"❌ '{vsCmd}' command failed with exit code: {process.ExitCode}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logDebug?.Invoke($"❌ Error running '{vsCmd}' command: {ex.Message}");
                        continue; // Try next command
                    }
                }

                // Strategy 2: Try common Visual Studio installation paths
                var vsExecutables = new[]
                {
                    @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe",
                    @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe",
                    @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe",
                    @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe",
                    @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe",
                    @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\devenv.exe"
                };

                foreach (var vsPath in vsExecutables)
                {
                    if (File.Exists(vsPath))
                    {
                        try
                        {
                            _logDebug?.Invoke($"Attempting to open {scriptPath} using Visual Studio at: {vsPath}");

                            var processInfo = new ProcessStartInfo
                            {
                                FileName = vsPath,
                                Arguments = $"\"{scriptPath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            var process = Process.Start(processInfo);
                            if (process != null)
                            {
                                _logDebug?.Invoke($"✅ Successfully opened {scriptPath} in Visual Studio at: {vsPath}");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logDebug?.Invoke($"❌ Error opening Visual Studio at {vsPath}: {ex.Message}");
                            continue; // Try next path
                        }
                    }
                }

                // ✅ IMPROVED: Only fall back to shell execute as last resort and be explicit about it
                try
                {
                    _logDebug?.Invoke($"Attempting fallback: opening {scriptPath} with default application");

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = scriptPath,
                        UseShellExecute = true
                    });

                    _logDebug?.Invoke($"✅ Opened {scriptPath} with default application (likely Visual Studio)");
                    return true;
                }
                catch (Exception ex)
                {
                    _logDebug?.Invoke($"❌ Fallback failed: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"❌ Error opening script in Visual Studio: {ex.Message}");
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
                // Strategy 1: Try the 'code' command
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });

                if (process != null)
                {
                    process.WaitForExit(5000); // Increased timeout to 5 seconds
                    if (process.ExitCode == 0)
                    {
                        _logDebug?.Invoke("✅ VSCode detected via 'code --version' command");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"VSCode command check failed: {ex.Message}");
            }

            // Strategy 2: Check common installation paths
            var commonPaths = new[]
            {
                @"C:\Users\{0}\AppData\Local\Programs\Microsoft VS Code\Code.exe",
                @"C:\Program Files\Microsoft VS Code\Code.exe",
                @"C:\Program Files (x86)\Microsoft VS Code\Code.exe"
            };

            var username = Environment.UserName;
            foreach (var pathTemplate in commonPaths)
            {
                var fullPath = pathTemplate.Contains("{0}")
                    ? string.Format(pathTemplate, username)
                    : pathTemplate;

                if (File.Exists(fullPath))
                {
                    _logDebug?.Invoke($"✅ VSCode detected at: {fullPath}");
                    return true;
                }
            }

            // Strategy 3: Check registry (Windows)
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            var displayName = subKey?.GetValue("DisplayName")?.ToString() ?? "";
                            if (displayName.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase))
                            {
                                _logDebug?.Invoke($"✅ VSCode detected in registry: {displayName}");
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"VSCode registry check failed: {ex.Message}");
            }

            _logDebug?.Invoke("❌ VSCode not detected");
            return false;
        }

        /// <summary>
        /// Checks if Visual Studio is available
        /// </summary>
        private bool IsVisualStudioAvailable()
        {
            // Strategy 1: Try devenv command
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
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        process.WaitForExit(5000); // Increased timeout to 5 seconds
                        if (process.ExitCode == 0)
                        {
                            _logDebug?.Invoke($"✅ Visual Studio detected via '{vsExe} /?' command");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logDebug?.Invoke($"Visual Studio command check failed for {vsExe}: {ex.Message}");
                }
            }

            // Strategy 2: Check common installation paths
            var commonPaths = new[]
            {
                @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\devenv.exe"
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    _logDebug?.Invoke($"✅ Visual Studio detected at: {path}");
                    return true;
                }
            }

            // Strategy 3: Check registry for Visual Studio installations
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    var registryPaths = new[]
                    {
                        @"SOFTWARE\Microsoft\VisualStudio",
                        @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio"
                    };

                    foreach (var regPath in registryPaths)
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);
                        if (key != null)
                        {
                            foreach (var subKeyName in key.GetSubKeyNames())
                            {
                                if (subKeyName.Contains(".") && (subKeyName.StartsWith("16.") || subKeyName.StartsWith("17."))) // VS 2019/2022
                                {
                                    using var vsKey = key.OpenSubKey(subKeyName);
                                    var installDir = vsKey?.GetValue("InstallDir")?.ToString();
                                    if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                                    {
                                        _logDebug?.Invoke($"✅ Visual Studio detected in registry: {installDir}");
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Visual Studio registry check failed: {ex.Message}");
            }

            _logDebug?.Invoke("❌ Visual Studio not detected");
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