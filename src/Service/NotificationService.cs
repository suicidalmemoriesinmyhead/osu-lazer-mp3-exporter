using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OsuLazerAudioExporter.Services;

public static class NotificationService
{
    public static void SendSystemNotification(string title, string message)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notify-send",
                    ArgumentList = { title, message },
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "osascript",
                    ArgumentList = { "-e", $"display notification \"{message}\" with title \"{title}\"" },
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Неблокирующее системное уведомление через PowerShell BalloonTip
                var script = $"Add-Type -AssemblyName System.Windows.Forms; " +
                             $"$n = New-Object System.Windows.Forms.NotifyIcon; " +
                             $"$n.Icon = [System.Drawing.SystemIcons]::Information; " +
                             $"$n.Visible = $true; " +
                             $"$n.ShowBalloonTip(4000, '{title.Replace("'", "")}', '{message.Replace("'", "")}', [System.Windows.Forms.ToolTipIcon]::Info);";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    ArgumentList = { "-NoProfile", "-WindowStyle", "Hidden", "-Command", script },
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        catch
        {
            // Игнорируем отсутствие системного демона уведомлений
        }
    }
}