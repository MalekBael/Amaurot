using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Amaurot.Helpers
{
    /// <summary>
    /// Provides utilities for validating and sanitizing file paths to prevent security vulnerabilities
    /// such as path traversal attacks and command injection.
    /// </summary>
    public static class PathValidator
    {
        /// <summary>
        /// Validates that a file path is safe to use (no path traversal attempts, special characters, etc.)
        /// </summary>
        /// <param name="path">The path to validate</param>
        /// <returns>True if the path is valid and safe, false otherwise</returns>
        public static bool IsValidPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // Get the full path - this will throw if the path is invalid
                string fullPath = Path.GetFullPath(path);

                // Check for path traversal attempts
                if (path.Contains("..") || path.Contains("~"))
                    return false;

                // Check for invalid characters
                char[] invalidChars = Path.GetInvalidPathChars();
                if (path.IndexOfAny(invalidChars) >= 0)
                    return false;

                // Additional security: check for shell metacharacters that could be used for injection
                // These are particularly dangerous when paths are passed to shell commands
                // On Windows, these characters should not appear in valid paths
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    char[] dangerousChars = new[] { ';', '|', '&', '>', '<', '`', '$', '\n', '\r' };
                    if (path.IndexOfAny(dangerousChars) >= 0)
                        return false;
                }
                else
                {
                    // On Unix-like systems, only check for the most dangerous characters
                    // Note: ; is a valid filename character on Unix
                    char[] dangerousChars = new[] { '|', '&', '>', '<', '`', '$', '\n', '\r', '\0' };
                    if (path.IndexOfAny(dangerousChars) >= 0)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates that a file exists and the path is safe
        /// </summary>
        /// <param name="path">The file path to validate</param>
        /// <returns>True if the file exists and path is safe, false otherwise</returns>
        public static bool IsValidFilePath(string? path)
        {
            return IsValidPath(path) && File.Exists(path);
        }

        /// <summary>
        /// Validates that a directory exists and the path is safe
        /// </summary>
        /// <param name="path">The directory path to validate</param>
        /// <returns>True if the directory exists and path is safe, false otherwise</returns>
        public static bool IsValidDirectoryPath(string? path)
        {
            return IsValidPath(path) && Directory.Exists(path);
        }

        /// <summary>
        /// Sanitizes a file path for use in command-line arguments.
        /// Returns null if the path is invalid or unsafe.
        /// </summary>
        /// <param name="path">The path to sanitize</param>
        /// <returns>Sanitized path or null if invalid</returns>
        public static string? SanitizeForCommandLine(string? path)
        {
            if (!IsValidPath(path))
                return null;

            try
            {
                // Get the full absolute path
                string fullPath = Path.GetFullPath(path!);
                
                // Return the full path - it will be properly escaped by ProcessStartInfo
                // when UseShellExecute is false
                return fullPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Escapes a file path for safe use in shell commands (when UseShellExecute=true is unavoidable)
        /// </summary>
        /// <param name="path">The path to escape</param>
        /// <returns>Escaped path suitable for shell execution, or null if path is invalid</returns>
        public static string? EscapeForShell(string? path)
        {
            if (!IsValidPath(path))
                return null;

            try
            {
                string fullPath = Path.GetFullPath(path!);
                
                // For Windows, quote the path and escape any quotes inside using backslash
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    // Escape any existing quotes with backslash (Windows cmd.exe style)
                    // Note: for cmd.exe, quotes within quotes need to be escaped as \"
                    fullPath = fullPath.Replace("\"", "\\\"");
                    return $"\"{fullPath}\"";
                }
                else
                {
                    // For Unix-like systems, escape special characters
                    var escaped = new StringBuilder();
                    foreach (char c in fullPath)
                    {
                        if (c == '\'' || c == '\"' || c == '\\' || c == ' ' || c == '$' || c == '`')
                        {
                            escaped.Append('\\');
                        }
                        escaped.Append(c);
                    }
                    return escaped.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validates multiple file paths
        /// </summary>
        /// <param name="paths">Array of paths to validate</param>
        /// <returns>Array of valid paths only</returns>
        public static string[] FilterValidFilePaths(params string[] paths)
        {
            return paths?.Where(p => IsValidFilePath(p)).ToArray() ?? Array.Empty<string>();
        }

        /// <summary>
        /// Checks if a path is within an allowed base directory (prevents directory traversal)
        /// </summary>
        /// <param name="path">The path to check</param>
        /// <param name="baseDirectory">The base directory that the path must be within</param>
        /// <returns>True if path is within the base directory, false otherwise</returns>
        public static bool IsWithinDirectory(string? path, string? baseDirectory)
        {
            if (!IsValidPath(path) || !IsValidPath(baseDirectory))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(path!);
                string fullBaseDir = Path.GetFullPath(baseDirectory!);

                // Ensure base directory ends with separator
                if (!fullBaseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    fullBaseDir += Path.DirectorySeparatorChar;

                return fullPath.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
