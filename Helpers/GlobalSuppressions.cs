using System.Diagnostics.CodeAnalysis;

// Global suppression for CA1416 warnings related to Windows-specific APIs
// This application is specifically designed for Windows and uses Wine-compatible services
[assembly: SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
    Justification = "This is a Windows-targeted WPF application that uses cross-platform services internally for Wine compatibility.")]

// Specific suppression for IFileDialogService usage
[assembly: SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
    Scope = "member",
    Target = "~M:Amaurot.SettingsWindow.BrowseButton_Click(System.Object,System.Windows.RoutedEventArgs)",
    Justification = "Uses IFileDialogService which handles platform compatibility internally")]

[assembly: SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
    Scope = "member",
    Target = "~M:Amaurot.SettingsWindow.BrowseSapphireButton_Click(System.Object,System.Windows.RoutedEventArgs)",
    Justification = "Uses IFileDialogService which handles platform compatibility internally")]

[assembly: SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
    Scope = "member",
    Target = "~M:Amaurot.SettingsWindow.BrowseSapphireBuildButton_Click(System.Object,System.Windows.RoutedEventArgs)",
    Justification = "Uses IFileDialogService which handles platform compatibility internally")]

[assembly: SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
    Scope = "member",
    Target = "~M:Amaurot.SettingsWindow.#ctor(Amaurot.Services.SettingsService,System.Action{System.String})",
    Justification = "Constructor uses CrossPlatformFileDialogService which handles platform compatibility internally")]

// Suppression for CrossPlatformFileDialogService usage
[assembly: SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
    Scope = "type",
    Target = "~T:Amaurot.Services.CrossPlatformFileDialogService",
    Justification = "Service designed to handle platform compatibility with Wine support")]