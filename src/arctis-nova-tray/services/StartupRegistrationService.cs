using Microsoft.Win32;

namespace ArctisNovaTray.Services;

public interface IStartupRegistrationService
{
    bool IsStartupEnabled { get; }

    void SetStartupEnabled(bool enabled);

    string BuildCommand();
}

/// <summary>
/// Manages opt-in Windows login startup under HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// using --silent so the indicator starts quietly in the system tray when enabled.
/// </summary>
public sealed class RegistryStartupRegistrationService : IStartupRegistrationService
{
    public const string DefaultRunSubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "ArctisNovaTray";

    private readonly string _runSubKeyPath;
    private readonly string _valueName;
    private readonly string? _executablePathOverride;

    public RegistryStartupRegistrationService(
        string runSubKeyPath = DefaultRunSubKeyPath,
        string valueName = DefaultValueName,
        string? executablePathOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runSubKeyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

        _runSubKeyPath = runSubKeyPath;
        _valueName = valueName;
        _executablePathOverride = executablePathOverride;
    }

    public bool IsStartupEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_runSubKeyPath, writable: false);
            return key?.GetValue(_valueName) is string registered &&
                   !string.IsNullOrWhiteSpace(registered);
        }
    }

    public void SetStartupEnabled(bool enabled)
    {
        if (enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(_runSubKeyPath, writable: true)
                ?? throw new InvalidOperationException($"Unable to open registry key 'HKCU\\{_runSubKeyPath}'.");
            key.SetValue(_valueName, BuildCommand(), RegistryValueKind.String);
            return;
        }

        using RegistryKey? existingKey = Registry.CurrentUser.OpenSubKey(_runSubKeyPath, writable: true);
        existingKey?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    public string BuildCommand()
    {
        string? exePath = _executablePathOverride;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            exePath = Environment.ProcessPath;
        }

        if (string.IsNullOrWhiteSpace(exePath))
        {
            exePath = Application.ExecutablePath;
        }

        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new InvalidOperationException("Unable to determine ArctisNovaTray executable path for Windows startup.");
        }

        return $"\"{Path.GetFullPath(exePath)}\" --silent";
    }
}
