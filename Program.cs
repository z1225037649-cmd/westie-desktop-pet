using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Xml.Linq;
using Microsoft.Win32;

namespace WestiePet
{
    internal static class StartupRegistration
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "WestiePet.Mimo.2026";

        // Windows is the source of truth; simply opening the app never registers it.
        internal static bool IsEnabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return IsRegistered(key);
            }
        }

        internal static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                UpdateRegistration(key, enabled, typeof(Program).Assembly.Location);
        }

        internal static bool IsRegistered(RegistryKey key)
        {
            string command = key == null ? null : key.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(command);
        }

        internal static void UpdateRegistration(RegistryKey key, bool enabled, string executable)
        {
            if (key == null) throw new IOException("无法访问当前用户的启动项。");
            if (enabled)
                key.SetValue(ValueName, "\"" + Path.GetFullPath(executable) + "\" --autostart", RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, false);
        }
    }

    public sealed class PetSettings
    {
        public const double MinimumSize = .4, MaximumSize = 1.6, DefaultSize = .6;
        public double Left = double.NaN, Top = double.NaN, Size = DefaultSize;
        public bool Roam = true, Topmost = true;
        public string Name = "小白";
        static string FileName { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "小白.settings.xml"); } }
        public static PetSettings Load()
        {
            PetSettings s = new PetSettings();
            try
            {
                if (!File.Exists(FileName)) return s;
                XElement root = XElement.Load(FileName);
                s.Left = Number(root, "left", double.NaN); s.Top = Number(root, "top", double.NaN);
                s.Size = Math.Max(MinimumSize, Math.Min(MaximumSize, Number(root, "size", DefaultSize)));
                s.Roam = (string)root.Element("roam") != "false";
                s.Topmost = (string)root.Element("topmost") != "false";
            }
            catch (Exception) { /* A damaged preferences file must never prevent startup. */ }
            return s;
        }
        static double Number(XElement root, string name, double fallback)
        {
            double v;
            return double.TryParse((string)root.Element(name), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsInfinity(v) ? v : fallback;
        }
        public void Save()
        {
            try
            {
                XElement root = new XElement("westie", new XAttribute("version", 1),
                    new XElement("left", Left.ToString(CultureInfo.InvariantCulture)),
                    new XElement("top", Top.ToString(CultureInfo.InvariantCulture)),
                    new XElement("size", Size.ToString(CultureInfo.InvariantCulture)),
                    new XElement("roam", Roam ? "true" : "false"),
                    new XElement("topmost", Topmost ? "true" : "false"));
                string temp = FileName + ".tmp";
                root.Save(temp);
                if (File.Exists(FileName)) File.Replace(temp, FileName, null); else File.Move(temp, FileName);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static class Program
    {
        internal static EventWaitHandle OpenRequested;
        [STAThread]
        public static int Main(string[] args)
        {
            bool verify = Array.IndexOf(args, "--self-test") >= 0;
            bool first;
            using (Mutex instance = new Mutex(true, "Local\\WestiePet.Mimo.2026", out first))
            using (EventWaitHandle open = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\WestiePet.Mimo.Open"))
            {
                if (!first && !verify) { open.Set(); return 0; }
                OpenRequested = open;
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), e.Exception.ToString()); } catch { }
                    MessageBox.Show("小白遇到了一点问题。错误已记录到程序旁的 error.log。\n" + e.Exception.Message, "西高地桌面宠物");
                    e.Handled = true; app.Shutdown(1);
                };
                PetWindow pet = new PetWindow(verify ? new PetSettings { Roam = false } : PetSettings.Load(), verify,
                    Array.IndexOf(args, "--autostart") >= 0);
                app.Run(pet);
                return pet.TestFailed ? 1 : 0;
            }
        }
    }
}
