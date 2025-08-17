using System;
using System.IO;

namespace Amaurot.Services
{
    public interface IFileDialogService
    {
        string? SelectFolder(string title, string initialPath);
        string? SelectFile(string title, string filter, string initialPath);
        string? SaveFile(string title, string filter, string initialPath, string defaultFileName = "");
    }

    public class CrossPlatformFileDialogService : IFileDialogService
    {
        public string? SelectFolder(string title, string initialPath)
        {
            try
            {
                // Use Windows Forms FolderBrowserDialog for better Wine compatibility
                // Suppress CA1416 because this is intentionally Windows-specific in a Windows-targeted project
#pragma warning disable CA1416 
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = title,
                    SelectedPath = initialPath,
                    ShowNewFolderButton = false
                };

                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    ? dialog.SelectedPath : null;
#pragma warning restore CA1416 
            }
            catch
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = title,
                    InitialDirectory = initialPath,
                    ValidateNames = false,
                    CheckFileExists = false,
                    CheckPathExists = true,
                    FileName = "Select Folder"
                };

                if (dialog.ShowDialog() == true)
                {
                    return Path.GetDirectoryName(dialog.FileName) ?? dialog.FileName;
                }

                return null;
            }
        }

        public string? SelectFile(string title, string filter, string initialPath)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = filter,
                InitialDirectory = initialPath
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? SaveFile(string title, string filter, string initialPath, string defaultFileName = "")
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = title,
                Filter = filter,
                InitialDirectory = initialPath,
                FileName = !string.IsNullOrEmpty(defaultFileName)
                    ? defaultFileName
                    : $"MapEditor_Log_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}