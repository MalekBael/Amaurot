using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        public bool OpenInVSCode(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                return false;
            }

            _logDebug?.Invoke($"Attempting to open {scriptPath} in VSCode using URI scheme");

            if (TryOpenWithVSCodeUri(scriptPath))
            {
                return true;
            }

            _logDebug?.Invoke("URI scheme failed, falling back to direct process execution");
            return TryOpenWithVSCodeProcess(scriptPath);
        }

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

            if (TryOpenWithVSCodeUri(scriptPath))
            {
                _logDebug?.Invoke("✅ Opened with VSCode URI as Visual Studio alternative");
                return true;
            }

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
            if (IsRunningOnWine())
            {
                _logDebug?.Invoke("Wine detected: trying VSCode URI as better alternative to Visual Studio");
                
                if (TryOpenWithVSCodeUri(scriptPath))
                {
                    return true;
                }
            }

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

            _logDebug?.Invoke("Visual Studio not found, trying VSCode URI as fallback");
            if (TryOpenWithVSCodeUri(scriptPath))
            {
                _logDebug?.Invoke("✅ Opened with VSCode URI as Visual Studio fallback");
                return true;
            }

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

            var linuxVSCodeCommands = new []
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

            return TryXdgOpen(scriptPath) || TryGenericEditorOpen(scriptPath, "xdg-open");
        }

        private bool TryOpenVSCodeWindows(string scriptPath)
        {
            _logDebug?.Invoke("Detected Windows environment, trying Windows-specific VSCode paths");

            if (IsRunningOnWine())
            {
                _logDebug?.Invoke("Detected Wine environment, trying Wine-compatible approaches");
                
                if (TryOpenVSCodeLinux(scriptPath))
                {
                    return true;
                }
                
                _logDebug?.Invoke("Linux VSCode approaches failed, trying Windows paths through Wine");
            }

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
                return IsVSCodeAvailable();     
            }
        }

        private bool IsVisualStudioAvailableLinux()
        {
            _logDebug?.Invoke("Checking Visual Studio availability on Linux (alternatives and Wine)");

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

            var containerizedIDEs = new[]
            {
                ("flatpak list | grep -i 'visual\\|code\\|clion\\|qtcreator'", "Flatpak IDEs"),
                ("snap list | grep -i 'code\\|clion\\|qtcreator'", "Snap IDEs")
            };

            foreach (var (command, description) in containerizedIDEs)
            {
                try
                {
                    var shellCommand = command.Split('|')[0].Trim().Split(' ')[0];    
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = $"-c \"{command}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        process.WaitForExit(3000);
                        if (process.ExitCode == 0)
                        {
                            var output = process.StandardOutput.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(output))
                            {
                                _logDebug?.Invoke($"✅ {description} detected via: {command}");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logDebug?.Invoke($"{description} check failed: {ex.Message}");
                }
            }

            _logDebug?.Invoke("❌ No Visual Studio alternatives detected on Linux");
            return false;
        }

        private bool IsVisualStudioAvailableWindows()
        {
            if (IsRunningOnWine())
            {
                _logDebug?.Invoke("Wine detected: checking Linux alternatives before Windows Visual Studio");
                
                if (IsVisualStudioAvailableLinux())
                {
                    return true;
                }
                
                _logDebug?.Invoke("Linux alternatives not found in Wine, trying Windows Visual Studio detection");
            }

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

            _logDebug?.Invoke("❌ Visual Studio not detected on Windows");
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