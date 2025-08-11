using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Amaurot.Services;

namespace Amaurot.Services
{
    public class InstanceScriptService
    {
        private readonly SettingsService _settingsService;
        private readonly Action<string>? _logDebug;

        public InstanceScriptService(SettingsService settingsService, Action<string>? logDebug = null)
        {
            _settingsService = settingsService;
            _logDebug = logDebug;
        }

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
                // Clean instance name for searching
                string cleanName = instanceName
                    .Replace("(Hard)", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ", "")
                    .Trim();

                // Try exact ID match first
                var idMatch = SearchForIdMatch(scriptsPath, instanceId);
                if (idMatch != null)
                {
                    _logDebug?.Invoke($"Found ID match: {Path.GetFileName(idMatch)}");
                    return idMatch;
                }

                // Try name-based matches
                var nameMatch = SearchForNameMatch(scriptsPath, cleanName, isHardMode);
                if (nameMatch != null)
                {
                    _logDebug?.Invoke($"Found name match: {Path.GetFileName(nameMatch)} for {instanceName}");
                    return nameMatch;
                }

                // Try pattern-based search
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
                // For Hard mode dungeons, look for *Hard.cpp files
                searchPatterns.AddRange(new[]
                {
                    $"{cleanName}Hard.cpp",
                    $"{cleanName}_Hard.cpp"
                });
            }
            else
            {
                // For normal dungeons, look for exact name match
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
                        // For hard mode, prefer files with "Hard" in the name that match the base name
                        return fileName.Contains("Hard", StringComparison.OrdinalIgnoreCase) &&
                               fileName.StartsWith(cleanName, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        // For normal mode, exclude files with "Hard" in the name
                        return fileName.StartsWith(cleanName, StringComparison.OrdinalIgnoreCase) &&
                               !fileName.Contains("Hard", StringComparison.OrdinalIgnoreCase);
                    }
                }).ToList();

                if (matchingFiles.Count > 0)
                {
                    // Prioritize exact matches
                    var exactMatch = isHardMode
                        ? matchingFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals($"{cleanName}Hard", StringComparison.OrdinalIgnoreCase))
                        : matchingFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(cleanName, StringComparison.OrdinalIgnoreCase));

                    if (exactMatch != null)
                    {
                        return exactMatch;
                    }

                    // Return the best available match
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

        public bool OpenInVSCode(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                return false;
            }

            try
            {
                _logDebug?.Invoke($"Attempting to open {scriptPath} in VSCode");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                var process = Process.Start(processInfo);
                if (process != null)
                {
                    process.WaitForExit(3000);
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
                        continue;
                    }
                }
            }

            _logDebug?.Invoke($"❌ Failed to open {scriptPath} in VSCode - no working VSCode installation found");
            return false;
        }

        public bool OpenInVisualStudio(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                return false;
            }

            try
            {
                _logDebug?.Invoke($"Attempting to open {scriptPath} in Visual Studio");

                var vsCommands = new[] { "devenv.exe", "devenv" };

                foreach (var vsCmd in vsCommands)
                {
                    try
                    {
                        var processInfo = new ProcessStartInfo
                        {
                            FileName = vsCmd,
                            Arguments = $"\"{scriptPath}\"",
                            UseShellExecute = false,        // ✅ REVERT: Use false like QuestScriptService
                            CreateNoWindow = true,          // ✅ REVERT: Use true like QuestScriptService
                            RedirectStandardError = true    // ✅ ADD: Missing property from QuestScriptService
                        };

                        var process = Process.Start(processInfo);
                        if (process != null)
                        {
                            process.WaitForExit(3000);
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
                        continue;
                    }
                }

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
                                UseShellExecute = false,    // ✅ REVERT: Use false like QuestScriptService
                                CreateNoWindow = true       // ✅ REVERT: Use true like QuestScriptService
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
                            continue;
                        }
                    }
                }

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
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });

                if (process != null)
                {
                    process.WaitForExit(5000);
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

            _logDebug?.Invoke("❌ VSCode not detected");
            return false;
        }

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
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        process.WaitForExit(5000);
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

            _logDebug?.Invoke("❌ Visual Studio not detected");
            return false;
        }
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