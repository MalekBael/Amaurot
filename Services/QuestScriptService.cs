using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        // ✅ EXISTING: Quest finding logic remains the same
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

        // ✅ EXISTING: Quest finding helper methods remain the same
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

        // ✅ ENHANCED: VSCode opening with URI scheme and cross-platform support
        public bool OpenInVSCode(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                return false;
            }

            _logDebug?.Invoke($"Attempting to open {scriptPath} in VSCode using URI scheme");

            // ✅ ENHANCED: Try URI scheme first (cross-platform)
            if (TryOpenWithVSCodeUri(scriptPath))
            {
                return true;
            }

            // ✅ Fallback to direct process execution if URI fails
            _logDebug?.Invoke("URI scheme failed, falling back to direct process execution");
            return TryOpenWithVSCodeProcess(scriptPath);
        }

        // ✅ ADD: URI scheme support
        private bool TryOpenWithVSCodeUri(string scriptPath)
        {
            try
            {
                string fileUri = ConvertToFileUri(scriptPath);
                string vscodeUri = $"vscode://file/{fileUri}";

                _logDebug?.Invoke($"Opening VSCode with URI: {vscodeUri}");

                var processInfo = new ProcessStartInfo
                {
                    FileName = vscodeUri,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                var process = Process.Start(processInfo);
                if (process != null)
                {
                    _logDebug?.Invoke($"✅ Successfully opened {scriptPath} in VSCode via URI scheme");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"❌ VSCode URI scheme failed: {ex.Message}");
            }

            return false;
        }

        private string ConvertToFileUri(string filePath)
        {
            try
            {
                string absolutePath = Path.GetFullPath(filePath);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return absolutePath.Replace('\\', '/');
                }
                else
                {
                    return absolutePath;
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"❌ Error converting path to URI: {ex.Message}");
                return filePath.Replace('\\', '/');
            }
        }

        // ✅ ADD: Cross-platform VSCode process execution
        private bool TryOpenWithVSCodeProcess(string scriptPath)
        {
            _logDebug?.Invoke("Trying direct VSCode process execution as fallback");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return TryOpenVSCodeLinux(scriptPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TryOpenVSCodeWindows(scriptPath);
            }
            else
            {
                return TryGenericEditorOpen(scriptPath, "code");
            }
        }

        // ✅ ENHANCED: Visual Studio opening with cross-platform support
        public bool OpenInVisualStudio(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                return false;
            }

            _logDebug?.Invoke($"Attempting to open {scriptPath} in Visual Studio");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return OpenInVisualStudioLinux(scriptPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return OpenInVisualStudioWindows(scriptPath);
            }
            else
            {
                return TryGenericEditorOpen(scriptPath, "devenv");
            }
        }

        private bool OpenInVisualStudioLinux(string scriptPath)
        {
            _logDebug?.Invoke("Linux detected: Visual Studio not natively available, trying VSCode URI as alternative");

            // ✅ Try VSCode URI first as best alternative
            if (TryOpenWithVSCodeUri(scriptPath))
            {
                _logDebug?.Invoke("✅ Opened with VSCode URI as Visual Studio alternative");
                return true;
            }

            // ✅ Try other editors as fallback
            var linuxEditorAlternatives = new[]
            {
                ("code", "Visual Studio Code"),
                ("gedit", "Text Editor"),
                ("kate", "Kate Editor")
            };

            foreach (var (command, name) in linuxEditorAlternatives)
            {
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = $"\"{scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var process = Process.Start(processInfo);
                    if (process != null)
                    {
                        _logDebug?.Invoke($"✅ Successfully opened {scriptPath} in {name}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logDebug?.Invoke($"❌ Failed to open with {name}: {ex.Message}");
                    continue;
                }
            }

            return TryXdgOpen(scriptPath);
        }

        private bool OpenInVisualStudioWindows(string scriptPath)
        {
            // Check if running on Wine - prefer URI alternatives
            if (IsRunningOnWine())
            {
                _logDebug?.Invoke("Wine detected: trying VSCode URI as better alternative to Visual Studio");
                
                if (TryOpenWithVSCodeUri(scriptPath))
                {
                    return true;
                }
            }

            // Continue with existing Visual Studio logic...
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
                        process.WaitForExit(3000);
                        if (process.ExitCode == 0)
                        {
                            _logDebug?.Invoke($"✅ Successfully opened {scriptPath} in Visual Studio via '{vsCmd}'");
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
                    _logDebug?.Invoke($"❌ Error with '{vsCmd}': {ex.Message}");
                    continue;
                }
            }

            // ✅ ENHANCED: More comprehensive Visual Studio installation paths
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
                        continue;
                    }
                }
                else
                {
                    _logDebug?.Invoke($"❌ Visual Studio not found at: {vsPath}");
                }
            }

            // ✅ ADD: Try VSCode URI as fallback for Visual Studio button
            _logDebug?.Invoke("Visual Studio not found, trying VSCode URI as fallback");
            if (TryOpenWithVSCodeUri(scriptPath))
            {
                _logDebug?.Invoke("✅ Opened with VSCode URI as Visual Studio fallback");
                return true;
            }

            // ✅ ADD: Final fallback to default application
            try
            {
                _logDebug?.Invoke($"Attempting fallback: opening {scriptPath} with default application");

                Process.Start(new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true
                });

                _logDebug?.Invoke($"✅ Opened {scriptPath} with default application");
                return true;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"❌ Default application fallback failed: {ex.Message}");
                return false;
            }
        }

        // ✅ ADD: All helper methods from InstanceScriptService
        private static bool IsRunningOnWine()
        {
            try
            {
                return Environment.GetEnvironmentVariable("WINEPREFIX") != null ||
                       Environment.GetEnvironmentVariable("WINE") != null ||
                       Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine"));
            }
            catch
            {
                return false;
            }
        }

        private bool TryXdgOpen(string filePath)
        {
            try
            {
                _logDebug?.Invoke($"Trying xdg-open for {filePath}");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                var process = Process.Start(processInfo);
                if (process != null)
                {
                    if (!process.WaitForExit(3000))
                    {
                        _logDebug?.Invoke($"✅ Successfully opened {filePath} with xdg-open (process still running)");
                        return true;
                    }
                    else if (process.ExitCode == 0)
                    {
                        _logDebug?.Invoke($"✅ Successfully opened {filePath} with xdg-open");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"❌ xdg-open failed: {ex.Message}");
            }

            return false;
        }

        private bool TryGenericEditorOpen(string filePath, string command)
        {
            try
            {
                _logDebug?.Invoke($"Trying generic command: {command} for {filePath}");

                var processInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                var process = Process.Start(processInfo);
                if (process != null)
                {
                    if (!process.WaitForExit(2000))
                    {
                        _logDebug?.Invoke($"✅ Successfully started {command} (process still running)");
                        return true;
                    }
                    else if (process.ExitCode == 0)
                    {
                        _logDebug?.Invoke($"✅ Successfully opened {filePath} with {command}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"❌ Generic command {command} failed: {ex.Message}");
            }

            return false;
        }

        private bool TryOpenVSCodeLinux(string scriptPath)
        {
            _logDebug?.Invoke("Detected Linux environment, trying Linux-specific VSCode paths");

            var linuxVSCodeCommands = new[]
            {
                "code",
                "/usr/bin/code",
                "/snap/bin/code",
                "/var/lib/flatpak/exports/bin/com.visualstudio.code",
                "flatpak run com.visualstudio.code",
                "/opt/visual-studio-code/code",
                "/usr/local/bin/code"
            };

            foreach (var vscodeCmd in linuxVSCodeCommands)
            {
                try
                {
                    _logDebug?.Invoke($"Trying Linux VSCode command: {vscodeCmd}");

                    ProcessStartInfo processInfo;
                    
                    if (vscodeCmd.StartsWith("flatpak run"))
                    {
                        processInfo = new ProcessStartInfo
                        {
                            FileName = "flatpak",
                            Arguments = $"run com.visualstudio.code \"{scriptPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true
                        };
                    }
                    else
                    {
                        processInfo = new ProcessStartInfo
                        {
                            FileName = vscodeCmd,
                            Arguments = $"\"{scriptPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true
                        };
                    }

                    var process = Process.Start(processInfo);
                    if (process != null)
                    {
                        if (!process.WaitForExit(3000))
                        {
                            _logDebug?.Invoke($"✅ Successfully started VSCode via '{vscodeCmd}' (process still running)");
                            return true;
                        }
                        else if (process.ExitCode == 0)
                        {
                            _logDebug?.Invoke($"✅ Successfully opened {scriptPath} in VSCode via '{vscodeCmd}'");
                            return true;
                        }
                        else
                        {
                            _logDebug?.Invoke($"❌ '{vscodeCmd}' command failed with exit code: {process.ExitCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logDebug?.Invoke($"❌ Error running '{vscodeCmd}' command: {ex.Message}");
                    continue;
                }
            }

            // Fallback to xdg-open if direct VSCode commands fail
            return TryXdgOpen(scriptPath) || TryGenericEditorOpen(scriptPath, "xdg-open");
        }

        private bool TryOpenVSCodeWindows(string scriptPath)
        {
            _logDebug?.Invoke("Detected Windows environment, trying Windows-specific VSCode paths");

            // Check if running on Wine first
            if (IsRunningOnWine())
            {
                _logDebug?.Invoke("Detected Wine environment, trying Linux-style commands first");
                
                if (TryOpenVSCodeLinux(scriptPath))
                {
                    return true;
                }
                
                _logDebug?.Invoke("Linux commands failed, trying Windows paths through Wine");
            }

            // Standard Windows VSCode detection
            try
            {
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

        // ✅ ENHANCED: Availability detection methods
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

        private bool IsVSCodeAvailable()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return IsVSCodeAvailableLinux();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return IsVSCodeAvailableWindows();
            }
            else
            {
                // macOS or other platforms - basic command check
                return TryGenericEditorOpen("", "code");
            }
        }

        private bool IsVSCodeAvailableLinux()
        {
            _logDebug?.Invoke("Checking VSCode availability on Linux");

            // ✅ ADD: Linux VSCode installation paths
            var linuxVSCodePaths = new[]
            {
                "/usr/bin/code",
                "/usr/local/bin/code",
                "/opt/visual-studio-code/code",
                "/snap/bin/code",
                "/var/lib/flatpak/exports/bin/com.visualstudio.code",
                "/home/{0}/.local/share/flatpak/exports/bin/com.visualstudio.code",
                "/home/{0}/Applications/code.AppImage",
                "/home/{0}/.local/bin/code",
                "/home/{0}/bin/code"
            };

            var username = Environment.UserName;
            foreach (var pathTemplate in linuxVSCodePaths)
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

            // Command-based detection
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

            _logDebug?.Invoke("❌ VSCode not detected on Linux");
            return false;
        }

        private bool IsVSCodeAvailableWindows()
        {
            // ✅ EXISTING: Windows VSCode detection (enhanced)
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

            // ✅ EXISTING: Registry detection
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

            _logDebug?.Invoke("❌ VSCode not detected on Windows");
            return false;
        }

        private bool IsVisualStudioAvailable()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return IsVisualStudioAvailableLinux();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return IsVisualStudioAvailableWindows();
            }
            else
            {
                // macOS or other platforms
                return IsVSCodeAvailable(); // Fallback to VSCode detection
            }
        }

        private bool IsVisualStudioAvailableLinux()
        {
            _logDebug?.Invoke("Checking Visual Studio availability on Linux (alternatives and Wine)");

            // ✅ COPY: Same logic as InstanceScriptService
            var wineVSPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine", "drive_c", "Program Files", "Microsoft Visual Studio", "2022", "Community", "Common7", "IDE", "devenv.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine", "drive_c", "Program Files", "Microsoft Visual Studio", "2022", "Professional", "Common7", "IDE", "devenv.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine", "drive_c", "Program Files", "Microsoft Visual Studio", "2019", "Community", "Common7", "IDE", "devenv.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine", "drive_c", "Program Files (x86)", "Microsoft Visual Studio", "2019", "Community", "Common7", "IDE", "devenv.exe"),
                
                "/home/{0}/.wine/drive_c/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/devenv.exe",
                "/home/{0}/.wine/drive_c/Program Files/Microsoft Visual Studio/2022/Professional/Common7/IDE/devenv.exe",
                "/home/{0}/.wine/drive_c/Program Files (x86)/Microsoft Visual Studio/2019/Community/Common7/IDE/devenv.exe",
            };

            var username = Environment.UserName;
            foreach (var pathTemplate in wineVSPaths)
            {
                var fullPath = pathTemplate.Contains("{0}")
                    ? string.Format(pathTemplate, username)
                    : pathTemplate;

                if (File.Exists(fullPath))
                {
                    _logDebug?.Invoke($"✅ Visual Studio detected in Wine at: {fullPath}");
                    return true;
                }
            }

            // Same Linux alternatives detection as InstanceScriptService...
            var linuxVSAlternatives = new[]
            {
                ("code", "Visual Studio Code"),
                ("/opt/jetbrains/clion/bin/clion.sh", "CLion"),
                ("/usr/local/bin/clion", "CLion"),
                ("/snap/bin/clion", "CLion (Snap)"),
                ("clion", "CLion"),
                ("/usr/bin/qtcreator", "Qt Creator"),
                ("/usr/local/bin/qtcreator", "Qt Creator"),
                ("qtcreator", "Qt Creator"),
                ("/usr/bin/codeblocks", "Code::Blocks"),
                ("/usr/local/bin/codeblocks", "Code::Blocks"),
                ("codeblocks", "Code::Blocks"),
                ("/usr/bin/kdevelop", "KDevelop"),
                ("/usr/local/bin/kdevelop", "KDevelop"),
                ("kdevelop", "KDevelop"),
                ("/usr/bin/nvim", "Neovim"),
                ("/usr/bin/vim", "Vim"),
                ("nvim", "Neovim"),
                ("vim", "Vim"),
                ("/usr/bin/emacs", "Emacs"),
                ("emacs", "Emacs"),
                ("/opt/sublime_text/sublime_text", "Sublime Text"),
                ("/usr/bin/subl", "Sublime Text"),
                ("subl", "Sublime Text"),
                ("/usr/bin/atom", "Atom"),
                ("atom", "Atom"),
                ("/usr/bin/gedit", "GNOME Text Editor"),
                ("/usr/bin/kate", "Kate"),
                ("/usr/bin/nano", "Nano"),
                ("gedit", "GNOME Text Editor"),
                ("kate", "Kate"),
                ("nano", "Nano")
            };

            foreach (var (command, name) in linuxVSAlternatives)
            {
                try
                {
                    if (command.StartsWith("/"))
                    {
                        if (File.Exists(command))
                        {
                            _logDebug?.Invoke($"✅ Alternative IDE detected on Linux: {name} at {command}");
                            return true;
                        }
                    }
                    else
                    {
                        var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "which",
                            Arguments = command,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        });

                        if (process != null)
                        {
                            process.WaitForExit(2000);
                            if (process.ExitCode == 0)
                            {
                                var output = process.StandardOutput.ReadToEnd().Trim();
                                _logDebug?.Invoke($"✅ Alternative IDE detected on Linux: {name} via 'which {command}' -> {output}");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logDebug?.Invoke($"Alternative IDE check failed for {name}: {ex.Message}");
                }
            }

            _logDebug?.Invoke("❌ No Visual Studio alternatives detected on Linux");
            return false;
        }

        private bool IsVisualStudioAvailableWindows()
        {
            // Check Wine environment first
            if (IsRunningOnWine())
            {
                _logDebug?.Invoke("Wine detected: checking Linux alternatives before Windows Visual Studio");
                
                if (IsVisualStudioAvailableLinux())
                {
                    return true;
                }
                
                _logDebug?.Invoke("Linux alternatives not found in Wine, trying Windows Visual Studio detection");
            }

            // ✅ EXISTING: Windows Visual Studio detection
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

            // ✅ EXISTING: Registry detection
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
                                if (subKeyName.Contains(".") && (subKeyName.StartsWith("16.") || subKeyName.StartsWith("17.")))
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

            _logDebug?.Invoke("❌ Visual Studio not detected on Windows");
            return false;
        }
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