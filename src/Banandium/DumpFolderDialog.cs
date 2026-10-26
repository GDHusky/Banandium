using System.ComponentModel;
using System.Diagnostics;

namespace Banandium;

public static class DumpFolderDialog
{
    public static string? Show()
    {
#if WINDOWS
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select your extracted game dump or romfs folder",
            UseDescriptionForTitle = true
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
#else
        string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        string[] programs = desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase)
            ? new[] { "kdialog", "zenity" } : new[] { "zenity", "kdialog" };
        foreach (string program in programs)
        {
            var start = new ProcessStartInfo(program)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            if (program == "kdialog")
            {
                start.ArgumentList.Add("--getexistingdirectory");
                start.ArgumentList.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                start.ArgumentList.Add("--title");
                start.ArgumentList.Add("Open dump");
            }
            else
            {
                start.ArgumentList.Add("--file-selection");
                start.ArgumentList.Add("--directory");
                start.ArgumentList.Add("--title=Open dump");
            }
            using var process = new Process { StartInfo = start };
            try
            {
                process.Start();
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
            {
                continue;
            }
            string path = process.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
            process.WaitForExit();
            if (process.ExitCode == 1) return null;
            if (process.ExitCode != 0) throw new IOException(program + " could not open the folder picker");
            return path == "" ? null : path;
        }
        throw new IOException("Install zenity or kdialog to use the system folder picker");
#endif
    }
}
