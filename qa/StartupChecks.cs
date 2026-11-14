using System;
using System.IO;
using Microsoft.Win32;
using WestiePet;

// Exercise the production registry operations in an isolated, non-startup key.
static class StartupChecks
{
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    static int Main()
    {
        string testPath = @"Software\WestiePet.StartupChecks." + Guid.NewGuid().ToString("N");
        try
        {
            Require(!StartupRegistration.IsRegistered(null), "A missing Run key must mean disabled.");
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(testPath))
            {
                Require(!StartupRegistration.IsRegistered(key), "Startup must be off by default.");
                key.SetValue("UnrelatedApp", "keep-me");
                string executable = Path.Combine(Path.GetTempPath(), "小白 test folder", "小白-西高地桌面宠物.exe");
                StartupRegistration.UpdateRegistration(key, true, executable);
                Require(StartupRegistration.IsRegistered(key), "Enabling must persist across reads.");
                Require((string)key.GetValue(StartupRegistration.ValueName) == "\"" + executable + "\" --autostart",
                    "Paths containing spaces and Chinese characters must be quoted, with the quiet startup flag.");
                Require(key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.String, "Run command must be REG_SZ.");
                using (RegistryKey reopened = Registry.CurrentUser.OpenSubKey(testPath))
                    Require(StartupRegistration.IsRegistered(reopened), "Restarted apps must see the saved registration.");

                string moved = Path.Combine(Path.GetTempPath(), "moved pet", "小白.exe");
                StartupRegistration.UpdateRegistration(key, true, moved);
                Require(((string)key.GetValue(StartupRegistration.ValueName)).Contains(moved), "Re-enabling must update the executable location.");
                StartupRegistration.UpdateRegistration(key, false, moved);
                StartupRegistration.UpdateRegistration(key, false, moved);
                Require(!StartupRegistration.IsRegistered(key) && key.GetValue(StartupRegistration.ValueName) == null,
                    "Disabling must remove the registration and be safe to repeat.");
                Require((string)key.GetValue("UnrelatedApp") == "keep-me", "Other applications' startup entries must be preserved.");
            }
            using (RegistryKey readOnly = Registry.CurrentUser.OpenSubKey(testPath))
            {
                bool denied = false;
                try { StartupRegistration.UpdateRegistration(readOnly, true, typeof(StartupChecks).Assembly.Location); }
                catch (UnauthorizedAccessException) { denied = true; }
                Require(denied && !StartupRegistration.IsRegistered(readOnly), "Write failures must be reported without enabling startup.");
            }
            Console.WriteLine("PASS: startup defaults off, persists, quotes executable paths, updates moved paths, removes only its own entry, and reports denied writes.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error); return 1; }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testPath, false); }
    }
}
