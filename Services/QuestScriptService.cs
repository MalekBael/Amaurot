using System;
using System.IO;
using System.Linq;
using Amaurot.Services;
using System.Collections.Generic;

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

        public string? FindQuestLuaScript(string questIdString)
        {
            if (string.IsNullOrEmpty(questIdString))
            {
                return null;
            }

            if (_settingsService.IsValidSapphireBuildPath())
            {
                var buildPath = _settingsService.Settings.SapphireBuildPath;
                var generatedPath = Path.Combine(buildPath, "tools", "generated");

                if (Directory.Exists(generatedPath))
                {
                    var luaScript = TryFindLuaInGenerated(generatedPath, questIdString);
                    if (!string.IsNullOrEmpty(luaScript))
                    {
                        return luaScript;
                    }
                }
                else
                {
                    _logDebug?.Invoke($"Generated Lua directory not found: {generatedPath}");
                }
            }

            if (_settingsService.IsValidSapphireServerPath())
            {
                var sapphirePath = _settingsService.Settings.SapphireServerPath;
                var scriptsPath = Path.Combine(sapphirePath, "src", "scripts", "quest");

                if (Directory.Exists(scriptsPath))
                {
                    var luaScript = TryFindLuaInScripts(scriptsPath, questIdString);
                    if (!string.IsNullOrEmpty(luaScript))
                    {
                        return luaScript;
                    }
                }
            }

            _logDebug?.Invoke($"Quest Lua script not found for: {questIdString}");
            return null;
        }

        private string? TryFindLuaInGenerated(string generatedPath, string questIdString)
        {
            try
            {
                _logDebug?.Invoke($"Searching for Lua files in generated folder: {generatedPath}");

                var exactLuaFile = Path.Combine(generatedPath, $"{questIdString}.lua");
                if (File.Exists(exactLuaFile))
                {
                    _logDebug?.Invoke($"Found exact quest Lua script in generated folder: {exactLuaFile}");
                    return exactLuaFile;
                }

                var luaPattern = $"{questIdString}_*.lua";
                var foundLuaFiles = Directory.GetFiles(generatedPath, luaPattern, SearchOption.TopDirectoryOnly);

                if (foundLuaFiles.Length > 0)
                {
                    var luaFile = foundLuaFiles[0];     
                    _logDebug?.Invoke($"Found pattern-matched quest Lua script in generated folder: {luaFile}");
                    return luaFile;
                }

                var baseQuestId = questIdString;
                var underscoreIndex = questIdString.LastIndexOf('_');
                if (underscoreIndex > 0)
                {
                    baseQuestId = questIdString.Substring(0, underscoreIndex);
                }

                var allLuaFiles = Directory.GetFiles(generatedPath, "*.lua", SearchOption.TopDirectoryOnly);
                var matchingLuaFiles = allLuaFiles.Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    return fileName.StartsWith(baseQuestId, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (matchingLuaFiles.Count > 0)
                {
                    var bestMatch = matchingLuaFiles
                        .OrderBy(f => f.Contains(questIdString, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(f => Path.GetFileNameWithoutExtension(f).Length)
                        .First();

                    _logDebug?.Invoke($"Found base-name-matched quest Lua script in generated folder: {bestMatch}");
                    return bestMatch;
                }

                _logDebug?.Invoke($"No Lua files found for {questIdString} in generated folder");
                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error searching for quest Lua script in generated folder {questIdString}: {ex.Message}");
                return null;
            }
        }

        private string? TryFindLuaInScripts(string scriptsPath, string questIdString)
        {
            try
            {
                _logDebug?.Invoke($"Searching for Lua files in scripts folder: {scriptsPath}");

                var luaPattern = $"{questIdString}_*.lua";
                var foundLuaFiles = Directory.GetFiles(scriptsPath, luaPattern, SearchOption.AllDirectories);

                if (foundLuaFiles.Length > 0)
                {
                    var luaFile = foundLuaFiles[0];     
                    _logDebug?.Invoke($"Found quest Lua script in scripts folder: {luaFile}");
                    return luaFile;
                }

                var exactLuaFile = Path.Combine(scriptsPath, $"{questIdString}.lua");
                if (File.Exists(exactLuaFile))
                {
                    _logDebug?.Invoke($"Found exact quest Lua script in scripts folder: {exactLuaFile}");
                    return exactLuaFile;
                }

                var baseQuestId = questIdString;
                var underscoreIndex = questIdString.LastIndexOf('_');
                if (underscoreIndex > 0)
                {
                    baseQuestId = questIdString.Substring(0, underscoreIndex);
                }

                var allLuaFiles = Directory.GetFiles(scriptsPath, "*.lua", SearchOption.AllDirectories);
                var matchingLuaFiles = allLuaFiles.Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    return fileName.StartsWith(baseQuestId, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (matchingLuaFiles.Count > 0)
                {
                    var bestMatch = matchingLuaFiles
                        .OrderBy(f => f.Contains(questIdString, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(f => Path.GetFileNameWithoutExtension(f).Length)
                        .First();

                    _logDebug?.Invoke($"Found pattern-matched quest Lua script in scripts folder: {bestMatch}");
                    return bestMatch;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error searching for quest Lua script in scripts folder {questIdString}: {ex.Message}");
                return null;
            }
        }

        public string? FindQuestLuabScript(string questIdString)
        {
            if (string.IsNullOrEmpty(questIdString))
            {
                return null;
            }

            if (_settingsService.IsValidSapphireBuildPath())
            {
                var buildPath = _settingsService.Settings.SapphireBuildPath;
                var generatedPath = Path.Combine(buildPath, "tools", "generated");

                if (Directory.Exists(generatedPath))
                {
                    var luabScript = TryFindLuabInGenerated(generatedPath, questIdString);
                    if (!string.IsNullOrEmpty(luabScript))
                    {
                        return luabScript;
                    }
                }
            }

            _logDebug?.Invoke($"Quest .luab script not found for: {questIdString}");
            return null;
        }

        private string? TryFindLuabInGenerated(string generatedPath, string questIdString)
        {
            try
            {
                _logDebug?.Invoke($"Searching for .luab files in generated folder: {generatedPath}");

                var exactLuabFile = Path.Combine(generatedPath, $"{questIdString}.luab");
                if (File.Exists(exactLuabFile))
                {
                    _logDebug?.Invoke($"Found exact quest .luab script: {exactLuabFile}");
                    return exactLuabFile;
                }

                var luabPattern = $"{questIdString}_*.luab";
                var foundLuabFiles = Directory.GetFiles(generatedPath, luabPattern, SearchOption.TopDirectoryOnly);

                if (foundLuabFiles.Length > 0)
                {
                    var luabFile = foundLuabFiles[0];
                    _logDebug?.Invoke($"Found pattern-matched quest .luab script: {luabFile}");
                    return luabFile;
                }

                var baseQuestId = questIdString;
                var underscoreIndex = questIdString.LastIndexOf('_');
                if (underscoreIndex > 0)
                {
                    baseQuestId = questIdString.Substring(0, underscoreIndex);
                }

                var allLuabFiles = Directory.GetFiles(generatedPath, "*.luab", SearchOption.TopDirectoryOnly);
                var matchingLuabFiles = allLuabFiles.Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    return fileName.StartsWith(baseQuestId, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (matchingLuabFiles.Count > 0)
                {
                    var bestMatch = matchingLuabFiles
                        .OrderBy(f => f.Contains(questIdString, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(f => Path.GetFileNameWithoutExtension(f).Length)
                        .First();

                    _logDebug?.Invoke($"Found base-name-matched quest .luab script: {bestMatch}");
                    return bestMatch;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error searching for .luab files: {ex.Message}");
                return null;
            }
        }

        public string? DecompileLuabFile(string luabFilePath)
        {
            if (string.IsNullOrEmpty(luabFilePath) || !File.Exists(luabFilePath))
            {
                return null;
            }

            try
            {
                var outputPath = Path.ChangeExtension(luabFilePath, ".decompiled.lua");

                if (TryDecompileWithUnluac(luabFilePath, outputPath))
                {
                    _logDebug?.Invoke($"Successfully decompiled {luabFilePath} using unluac");
                    return outputPath;
                }

                _logDebug?.Invoke($"Failed to decompile {luabFilePath} - unluac not available or failed");
                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error decompiling .luab file {luabFilePath}: {ex.Message}");
                return null;
            }
        }

        private bool TryDecompileWithUnluac(string inputPath, string outputPath)
        {
            try
            {
                var toolsUnluacPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "unluac.jar");

                var unluacPaths = new[]
                {
                    toolsUnluacPath,    
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unluac.jar"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Tools", "unluac.jar"),
                    "unluac.jar"     
                };

                string? unluacPath = unluacPaths.FirstOrDefault(File.Exists);
                if (unluacPath == null)
                {
                    _logDebug?.Invoke($"unluac.jar not found in expected locations. Checked: {string.Join(", ", unluacPaths)}");
                    return false;
                }

                _logDebug?.Invoke($"Using unluac.jar from: {unluacPath}");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = $"-jar \"{unluacPath}\" \"{inputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process == null) return false;

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                process.WaitForExit(10000);    

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    File.WriteAllText(outputPath, output);
                    _logDebug?.Invoke($"Successfully decompiled {Path.GetFileName(inputPath)} using unluac");
                    return true;
                }

                _logDebug?.Invoke($"unluac failed with exit code {process.ExitCode}: {error}");
                return false;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error running unluac: {ex.Message}");
                return false;
            }
        }

        public string[] FindQuestScriptFiles(string questIdString)
        {
            var foundFiles = new List<string>();

            var cppScript = FindQuestScript(questIdString);
            if (!string.IsNullOrEmpty(cppScript))
            {
                foundFiles.Add(cppScript);
            }

            var luaScript = FindQuestLuaScript(questIdString);
            if (!string.IsNullOrEmpty(luaScript))
            {
                foundFiles.Add(luaScript);
            }

            var luabScript = FindQuestLuabScript(questIdString);
            if (!string.IsNullOrEmpty(luabScript))
            {
                var decompiledScript = DecompileLuabFile(luabScript);
                if (!string.IsNullOrEmpty(decompiledScript))
                {
                    foundFiles.Add(decompiledScript);
                    _logDebug?.Invoke($"Added decompiled Lua script: {decompiledScript}");
                }
                else
                {
                    foundFiles.Add(luabScript);
                    _logDebug?.Invoke($"Added compiled .luab script (decompilation failed): {luabScript}");
                }
            }

            _logDebug?.Invoke($"Found {foundFiles.Count} total files for quest {questIdString}: " +
                            $"{string.Join(", ", foundFiles.Select(Path.GetFileName))}");

            return foundFiles.ToArray();
        }

        public QuestScriptInfo GetQuestScriptInfo(string questIdString)
        {
            var scriptPath = FindQuestScript(questIdString);
            var luaPath = FindQuestLuaScript(questIdString);
            var luabPath = FindQuestLuabScript(questIdString);

            string? decompiledLuaPath = null;
            if (!string.IsNullOrEmpty(luabPath))
            {
                decompiledLuaPath = DecompileLuabFile(luabPath);
            }

            bool canSearchForScripts = _settingsService.IsValidSapphireServerPath();
            bool canSearchForLua = _settingsService.IsValidSapphireBuildPath() || _settingsService.IsValidSapphireServerPath();

            return new QuestScriptInfo
            {
                QuestIdString = questIdString,
                ScriptPath = scriptPath,
                LuaScriptPath = luaPath,
                LuabScriptPath = luabPath,
                DecompiledLuaPath = decompiledLuaPath,
                Exists = !string.IsNullOrEmpty(scriptPath),
                HasLuaScript = !string.IsNullOrEmpty(luaPath),
                HasLuabScript = !string.IsNullOrEmpty(luabPath),
                HasDecompiledLua = !string.IsNullOrEmpty(decompiledLuaPath),
                CanOpenInVSCode = IsVSCodeAvailable() && (canSearchForScripts || canSearchForLua),
                CanOpenInVisualStudio = IsVisualStudioAvailable() && (canSearchForScripts || canSearchForLua)
            };
        }

        public string[] FindRelatedQuestScripts(string questIdString)
        {
            if (string.IsNullOrEmpty(questIdString) || !_settingsService.IsValidSapphireServerPath())
            {
                return Array.Empty<string>();
            }

            var foundFiles = new List<string>();
            var sapphirePath = _settingsService.Settings.SapphireServerPath;
            var scriptsPath = Path.Combine(sapphirePath, "src", "scripts", "quest");

            if (!Directory.Exists(scriptsPath))
            {
                return Array.Empty<string>();
            }

            try
            {
                var mainScript = FindQuestScript(questIdString);
                if (!string.IsNullOrEmpty(mainScript))
                {
                    foundFiles.Add(mainScript);
                }

                var baseQuestId = questIdString;
                var underscoreIndex = questIdString.LastIndexOf('_');
                if (underscoreIndex > 0)
                {
                    baseQuestId = questIdString.Substring(0, underscoreIndex);
                }

                var allScriptFiles = Directory.GetFiles(scriptsPath, "*.cpp", SearchOption.AllDirectories);
                var relatedFiles = allScriptFiles.Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    return fileName.StartsWith(baseQuestId, StringComparison.OrdinalIgnoreCase) &&
                           !foundFiles.Contains(file);      
                }).Take(2).ToList();      

                foundFiles.AddRange(relatedFiles);

                if (foundFiles.Count < 3)
                {
                    if (!string.IsNullOrEmpty(mainScript))
                    {
                        var directory = Path.GetDirectoryName(mainScript);
                        if (Directory.Exists(directory))
                        {
                            var siblingFiles = Directory.GetFiles(directory, "*.cpp")
                                .Where(file => !foundFiles.Contains(file))
                                .Take(3 - foundFiles.Count)
                                .ToList();

                            foundFiles.AddRange(siblingFiles);
                        }
                    }
                }

                _logDebug?.Invoke($"Found {foundFiles.Count} related scripts for {questIdString}");
                return foundFiles.ToArray();
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error finding related quest scripts for {questIdString}: {ex.Message}");
                return foundFiles.ToArray();
            }
        }

        public QuestScriptImportInfo CheckForMissingScript(string questIdString)
        {
            var importInfo = new QuestScriptImportInfo
            {
                QuestIdString = questIdString,
                ExistsInRepo = false,
                ExistsInGenerated = false,
                CanImport = false
            };

            var repoScriptPath = FindQuestScript(questIdString);
            importInfo.ExistsInRepo = !string.IsNullOrEmpty(repoScriptPath);
            importInfo.RepoScriptPath = repoScriptPath;

            if (_settingsService.IsValidSapphireBuildPath())
            {
                var buildPath = _settingsService.Settings.SapphireBuildPath;
                var generatedPath = Path.Combine(buildPath, "tools", "generated");

                var generatedScriptPath = TryFindGeneratedScript(generatedPath, questIdString);
                importInfo.ExistsInGenerated = !string.IsNullOrEmpty(generatedScriptPath);
                importInfo.GeneratedScriptPath = generatedScriptPath;

                importInfo.CanImport = importInfo.ExistsInGenerated && !importInfo.ExistsInRepo;
            }

            _logDebug?.Invoke($"Script import check for {questIdString}: " +
                            $"Repo={importInfo.ExistsInRepo}, Generated={importInfo.ExistsInGenerated}, CanImport={importInfo.CanImport}");

            return importInfo;
        }

        private string? TryFindGeneratedScript(string generatedPath, string questIdString)
        {
            try
            {
                if (!Directory.Exists(generatedPath))
                {
                    _logDebug?.Invoke($"Generated scripts directory not found: {generatedPath}");
                    return null;
                }

                _logDebug?.Invoke($"Searching for generated C++ script in: {generatedPath}");

                var exactScriptFile = Path.Combine(generatedPath, $"{questIdString}.cpp");
                if (File.Exists(exactScriptFile))
                {
                    _logDebug?.Invoke($"Found exact generated C++ script: {exactScriptFile}");
                    return exactScriptFile;
                }

                var scriptPattern = $"{questIdString}_*.cpp";
                var foundScriptFiles = Directory.GetFiles(generatedPath, scriptPattern, SearchOption.TopDirectoryOnly);

                if (foundScriptFiles.Length > 0)
                {
                    var scriptFile = foundScriptFiles[0];
                    _logDebug?.Invoke($"Found pattern-matched generated C++ script: {scriptFile}");
                    return scriptFile;
                }

                var baseQuestId = questIdString;
                var underscoreIndex = questIdString.LastIndexOf('_');
                if (underscoreIndex > 0)
                {
                    baseQuestId = questIdString.Substring(0, underscoreIndex);
                }

                var allScriptFiles = Directory.GetFiles(generatedPath, "*.cpp", SearchOption.TopDirectoryOnly);
                var matchingScriptFiles = allScriptFiles.Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    return fileName.StartsWith(baseQuestId, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                if (matchingScriptFiles.Count > 0)
                {
                    var bestMatch = matchingScriptFiles
                        .OrderBy(f => f.Contains(questIdString, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(f => Path.GetFileNameWithoutExtension(f).Length)
                        .First();

                    _logDebug?.Invoke($"Found base-name-matched generated C++ script: {bestMatch}");
                    return bestMatch;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error searching for generated C++ script: {ex.Message}");
                return null;
            }
        }

        public ImportResult ImportQuestScript(string questIdString, string generatedScriptPath)
        {
            try
            {
                if (!File.Exists(generatedScriptPath))
                {
                    return new ImportResult
                    {
                        Success = false,
                        ErrorMessage = $"Generated script file not found: {generatedScriptPath}"
                    };
                }

                if (!_settingsService.IsValidSapphireServerPath())
                {
                    return new ImportResult
                    {
                        Success = false,
                        ErrorMessage = "Sapphire Server path not configured"
                    };
                }

                var sapphirePath = _settingsService.Settings.SapphireServerPath;
                var questScriptsPath = Path.Combine(sapphirePath, "src", "scripts", "quest");

                if (!Directory.Exists(questScriptsPath))
                {
                    return new ImportResult
                    {
                        Success = false,
                        ErrorMessage = $"Quest scripts directory not found: {questScriptsPath}"
                    };
                }

                var targetFileName = $"{questIdString}.cpp";
                var targetFilePath = Path.Combine(questScriptsPath, targetFileName);

                if (File.Exists(targetFilePath))
                {
                    return new ImportResult
                    {
                        Success = false,
                        ErrorMessage = $"Script already exists in repository: {targetFilePath}"
                    };
                }

                File.Copy(generatedScriptPath, targetFilePath);

                lock (_cacheLock)
                {
                    _scriptExistenceCache.Remove(questIdString);
                }

                _logDebug?.Invoke($"Successfully imported quest script: {generatedScriptPath} -> {targetFilePath}");

                return new ImportResult
                {
                    Success = true,
                    ImportedFilePath = targetFilePath,
                    Message = $"Successfully imported {targetFileName} to Sapphire repository"
                };
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error importing quest script: {ex.Message}");
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = $"Import failed: {ex.Message}"
                };
            }
        }

        public QuestScriptInfoExtended GetQuestScriptInfoExtended(string questIdString)
        {
            var basicInfo = GetQuestScriptInfo(questIdString);
            var importInfo = CheckForMissingScript(questIdString);

            return new QuestScriptInfoExtended
            {
                QuestIdString = basicInfo.QuestIdString,
                ScriptPath = basicInfo.ScriptPath,
                LuaScriptPath = basicInfo.LuaScriptPath,
                LuabScriptPath = basicInfo.LuabScriptPath,
                DecompiledLuaPath = basicInfo.DecompiledLuaPath,
                Exists = basicInfo.Exists,
                HasLuaScript = basicInfo.HasLuaScript,
                HasLuabScript = basicInfo.HasLuabScript,
                HasDecompiledLua = basicInfo.HasDecompiledLua,
                CanOpenInVSCode = basicInfo.CanOpenInVSCode,
                CanOpenInVisualStudio = basicInfo.CanOpenInVisualStudio,

                ExistsInRepo = importInfo.ExistsInRepo,
                ExistsInGenerated = importInfo.ExistsInGenerated,
                CanImport = importInfo.CanImport,
                GeneratedScriptPath = importInfo.GeneratedScriptPath,
                RepoScriptPath = importInfo.RepoScriptPath
            };
        }

        public bool HasAnyQuestScripts(string questIdString)
        {
            if (string.IsNullOrEmpty(questIdString))
            {
                _logDebug?.Invoke($"HasAnyQuestScripts: Empty questIdString");
                return false;
            }

            try
            {
                var repoScript = FindQuestScript(questIdString);
                if (!string.IsNullOrEmpty(repoScript))
                {
                    _logDebug?.Invoke($"HasAnyQuestScripts({questIdString}): Found in repo - {Path.GetFileName(repoScript)}");
                    return true;
                }

                var importInfo = CheckForMissingScript(questIdString);
                if (importInfo.CanImport)
                {
                    _logDebug?.Invoke($"HasAnyQuestScripts({questIdString}): Can import from generated - {Path.GetFileName(importInfo.GeneratedScriptPath ?? "unknown")}");
                    return true;
                }

                var luaScript = FindQuestLuaScript(questIdString);
                if (!string.IsNullOrEmpty(luaScript))
                {
                    _logDebug?.Invoke($"HasAnyQuestScripts({questIdString}): Found Lua script - {Path.GetFileName(luaScript)}");
                    return true;
                }

                _logDebug?.Invoke($"HasAnyQuestScripts({questIdString}): No scripts found");
                return false;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"HasAnyQuestScripts({questIdString}): Error - {ex.Message}");
                return false;
            }
        }

        private readonly Dictionary<string, bool> _scriptExistenceCache = new();
        private readonly object _cacheLock = new object();

        public bool HasQuestScriptInRepo(string questIdString)
        {
            if (string.IsNullOrEmpty(questIdString))
                return false;

            lock (_cacheLock)
            {
                if (_scriptExistenceCache.TryGetValue(questIdString, out bool cachedResult))
                {
                    return cachedResult;
                }
            }

            try
            {
                var repoScript = FindQuestScript(questIdString);
                bool hasRepo = !string.IsNullOrEmpty(repoScript);
                
                lock (_cacheLock)
                {
                    _scriptExistenceCache[questIdString] = hasRepo;
                }

                if (hasRepo)
                {
                    _logDebug?.Invoke($"HasQuestScriptInRepo({questIdString}): TRUE - Found {Path.GetFileName(repoScript)}");
                }

                return hasRepo;
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"HasQuestScriptInRepo({questIdString}): Error - {ex.Message}");
                
                lock (_cacheLock)
                {
                    _scriptExistenceCache[questIdString] = false;
                }
                
                return false;
            }
        }

        public void ClearScriptCache()
        {
            lock (_cacheLock)
            {
                _scriptExistenceCache.Clear();
            }
            _logDebug?.Invoke("Script existence cache cleared");
        }

        #endregion
    }

    public class QuestScriptInfo
    {
        public string QuestIdString { get; set; } = string.Empty;
        public string? ScriptPath { get; set; }
        public string? LuaScriptPath { get; set; }
        public string? LuabScriptPath { get; set; }
        public string? DecompiledLuaPath { get; set; }
        public bool Exists { get; set; }
        public bool HasLuaScript { get; set; }
        public bool HasLuabScript { get; set; }
        public bool HasDecompiledLua { get; set; }
        public bool CanOpenInVSCode { get; set; }
        public bool CanOpenInVisualStudio { get; set; }
    }

    public class QuestScriptImportInfo
    {
        public string QuestIdString { get; set; } = string.Empty;
        public bool ExistsInRepo { get; set; }
        public bool ExistsInGenerated { get; set; }
        public bool CanImport { get; set; }
        public string? RepoScriptPath { get; set; }
        public string? GeneratedScriptPath { get; set; }
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public string? ImportedFilePath { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
    }

    public class QuestScriptInfoExtended : QuestScriptInfo
    {
        public bool ExistsInRepo { get; set; }
        public bool ExistsInGenerated { get; set; }
        public bool CanImport { get; set; }
        public string? GeneratedScriptPath { get; set; }
        public string? RepoScriptPath { get; set; }
    }
}