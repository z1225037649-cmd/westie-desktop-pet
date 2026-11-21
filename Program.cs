using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Xml.Linq;
using Microsoft.Win32;

namespace WestiePet
{
    // Bounded, local diagnostics distinguish a lost window from a process exit.
    // Logging failures must never interfere with desktop input or shutdown.
    internal static class RuntimeLog
    {
        static bool enabled;
        internal static void Start() { enabled = true; Write("start pid=" + System.Diagnostics.Process.GetCurrentProcess().Id); }
        internal static void Write(string message)
        {
            if (!enabled) return;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + " " + message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }
    }

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
        public bool AutoHideFullscreen = true;
        public string Name = "小白";
        public string Breed = "westie";
        public int Affection, Treats;
        static string FileName { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "小白.settings.xml"); } }
        public static PetSettings Load()
        {
            PetSettings s = new PetSettings();
            try
            {
                if (!File.Exists(FileName)) return s;
                XElement root = XElement.Load(FileName);
                s.Left = Number(root, "left", double.NaN); s.Top = Number(root, "top", double.NaN);
                s.Size = ClampSize(Number(root, "size", DefaultSize));
                s.Roam = (string)root.Element("roam") != "false";
                s.Topmost = (string)root.Element("topmost") != "false";
                s.AutoHideFullscreen = (string)root.Element("autoHideFullscreen") != "false";
                s.Name = ReadName((string)root.Element("name"));
                s.Breed = ReadBreed((string)root.Element("breed"));
                s.Affection = ReadCount((string)root.Element("affection"));
                s.Treats = ReadCount((string)root.Element("treats"));
            }
            catch (Exception) { /* A damaged preferences file must never prevent startup. */ }
            return s;
        }
        static double Number(XElement root, string name, double fallback)
        {
            return ReadNumber((string)root.Element(name), fallback);
        }
        internal static double ReadNumber(string value, double fallback)
        {
            double number;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !double.IsInfinity(number) && !double.IsNaN(number) ? number : fallback;
        }
        internal static double ClampSize(double value)
        { return double.IsNaN(value) || double.IsInfinity(value) ? DefaultSize : Math.Max(MinimumSize, Math.Min(MaximumSize, value)); }
        internal static int ReadCount(string value)
        {
            int count;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ? Math.Max(0, count) : 0;
        }
        internal static string ReadName(string value)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) return "小白";
            return name.Length > 12 ? name.Substring(0, 12) : name;
        }
        internal static string ReadBreed(string value)
        { return string.Equals((value ?? "").Trim(), "corgi", StringComparison.OrdinalIgnoreCase) ? "corgi" : "westie"; }
        public void Save()
        {
            try
            {
                XElement root = new XElement("westie", new XAttribute("version", 1),
                    new XElement("left", Left.ToString(CultureInfo.InvariantCulture)),
                    new XElement("top", Top.ToString(CultureInfo.InvariantCulture)),
                    new XElement("size", Size.ToString(CultureInfo.InvariantCulture)),
                    new XElement("roam", Roam ? "true" : "false"),
                    new XElement("topmost", Topmost ? "true" : "false"),
                    new XElement("autoHideFullscreen", AutoHideFullscreen ? "true" : "false"),
                    new XElement("name", ReadName(Name)),
                    new XElement("breed", ReadBreed(Breed)),
                    new XElement("affection", Math.Max(0, Affection).ToString(CultureInfo.InvariantCulture)),
                    new XElement("treats", Math.Max(0, Treats).ToString(CultureInfo.InvariantCulture)));
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
                if (!verify)
                {
                    RuntimeLog.Start();
                    AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                    { RuntimeLog.Write("unhandled terminating=" + e.IsTerminating + " " + e.ExceptionObject); };
                    AppDomain.CurrentDomain.ProcessExit += delegate { RuntimeLog.Write("process-exit"); };
                }
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.Exit += delegate(object sender, ExitEventArgs e) { RuntimeLog.Write("application-exit code=" + e.ApplicationExitCode); };
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    RuntimeLog.Write("dispatcher-exception " + e.Exception);
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
