# Security Practices for Amaurot

This document describes the security practices and improvements implemented in the Amaurot codebase.

## Security Improvements (February 2026)

### Overview
A comprehensive security review was conducted focusing on:
- Command injection vulnerabilities
- Path traversal attacks
- Unsafe process execution
- SQL injection (verified as not vulnerable)
- Arbitrary code execution risks

### Critical Fixes

#### 1. Command Injection Prevention

**Problem:** User-controlled file paths were passed to `Process.Start()` with `UseShellExecute = true`, allowing potential command injection.

**Solution:** 
- Created `PathValidator` utility class in `Helpers/PathValidator.cs`
- Validates all user-provided paths before use
- Filters out shell metacharacters (`;`, `|`, `&`, `>`, `<`, etc.)
- Checks for path traversal attempts (`..`, `~`)

**Files Fixed:**
- `Services/SettingsService.cs` - OpenSapphireServerPath(), OpenSapphireBuildPath()
- `Views/SettingsWindow.xaml.cs` - OpenSapphireButton_Click(), OpenSapphireBuildButton_Click()
- `Views/InstanceContentDetailsWindow.xaml.cs` - OpenFileInEditor()
- `Services/BaseScriptService.cs` - All file opening methods
- `MainWindow.xaml.cs` - OpenToolInConsole()

#### 2. Process Execution Security

**Problem:** Using `UseShellExecute = true` with user-controlled paths allows shell interpretation of special characters.

**Solution:**
- Changed to `UseShellExecute = false` wherever possible
- Use explicit commands (e.g., `explorer.exe` on Windows, `xdg-open` on Linux)
- Properly escape and quote arguments
- Validate file paths before execution

**Example (Before):**
```csharp
Process.Start(new ProcessStartInfo
{
    FileName = userPath,  // UNSAFE - could contain malicious content
    UseShellExecute = true
});
```

**Example (After):**
```csharp
if (!PathValidator.IsValidDirectoryPath(userPath))
    return;

Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",  // SAFE - explicit command
    Arguments = $"\"{userPath}\"",
    UseShellExecute = false
});
```

#### 3. Argument Construction Security

**Problem:** Command-line arguments were built using string interpolation without validation.

**Solution:**
- Validate all arguments before concatenation
- Filter out shell metacharacters
- Properly escape quotes in file paths
- Use argument lists instead of string concatenation where possible

**Example (MainWindow.xaml.cs):**
```csharp
// Validate all arguments
var validatedArgs = arguments?.Where(a => 
    !string.IsNullOrWhiteSpace(a) && 
    !a.Contains(';') && 
    !a.Contains('|') && 
    !a.Contains('&')).ToArray() ?? Array.Empty<string>();

// Properly escape
var escapedArgs = string.Join(" ", 
    validatedArgs.Select(a => $"\"{a.Replace("\"", "\"\"")}\""));
```

### SQL Injection Status

**Status:** ✅ NOT VULNERABLE

All SQL queries use:
- Hardcoded query strings (no user input concatenation)
- Parameterized queries via `SQLiteCommand`
- Read-only database connections

**Verified Files:**
- `Services/QuestLocationService.cs`
- `Services/NpcService.cs`

### Security Best Practices

#### For Future Development

1. **Path Validation**
   - Always use `PathValidator.IsValidFilePath()` or `PathValidator.IsValidDirectoryPath()` before using user-provided paths
   - Never trust user input for file paths

2. **Process Execution**
   - Prefer `UseShellExecute = false`
   - Use explicit commands (e.g., `explorer.exe`, not just the path)
   - Validate and escape all arguments
   - Avoid shell metacharacters in arguments

3. **Input Validation**
   - Validate all user input at entry points
   - Use whitelisting (allow known-good) rather than blacklisting
   - Check for path traversal attempts (`..`, `~`)
   - Filter shell metacharacters (`;`, `|`, `&`, `>`, `<`, `` ` ``, `$`, `\n`, `\r`)

4. **Database Queries**
   - Continue using parameterized queries
   - Never concatenate user input into SQL strings
   - Use read-only connections where possible

5. **Error Handling**
   - Log security-related errors
   - Don't expose sensitive information in error messages
   - Fail securely (deny by default)

### PathValidator API

The `PathValidator` class provides these methods:

```csharp
// Basic validation
bool IsValidPath(string? path)
bool IsValidFilePath(string? path)
bool IsValidDirectoryPath(string? path)

// Command-line safety
string? SanitizeForCommandLine(string? path)
string? EscapeForShell(string? path)

// Bulk operations
string[] FilterValidFilePaths(params string[] paths)

// Directory containment check
bool IsWithinDirectory(string? path, string? baseDirectory)
```

### Testing

All security fixes have been verified with:
- CodeQL security scanner - **0 alerts found**
- Manual code review
- Path validation tests

### Security Contact

For security concerns, please open an issue on the GitHub repository.

## Changelog

### 2026-02-17
- ✅ Fixed command injection vulnerabilities in process execution
- ✅ Implemented PathValidator utility class
- ✅ Converted `UseShellExecute = true` to safer alternatives
- ✅ Added input validation for all user-provided paths
- ✅ Verified SQL queries are not vulnerable to injection
- ✅ CodeQL scan: 0 security alerts
