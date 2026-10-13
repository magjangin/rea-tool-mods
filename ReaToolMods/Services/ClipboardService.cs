using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace ReaToolMods.Services;

public static class ClipboardService
{
    /// <summary>
    /// Avalonia 클립보드로 복사하고, 실패하면 Windows clip 명령으로 대체합니다.
    /// </summary>
    public static async Task<bool> SetTextAsync(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: { } clipboard })
            {
                await clipboard.SetTextAsync(text);
                return true;
            }
        }
        catch { }

        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var psi = new ProcessStartInfo("cmd", "/c clip")
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.StandardInput.WriteAsync(text);
            proc.StandardInput.Close();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
