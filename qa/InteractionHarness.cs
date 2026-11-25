using System;
using System.IO;
using System.Windows;
using WestiePet;

// Exercise the production transparent window at the user's 57% size.
// A taskbar entry makes this test window discoverable by Windows UI automation.
// Preferences stay in qa/, separate from the user's settings beside the main exe.
static class InteractionHarness
{
    [STAThread]
    static void Main()
    {
        Application app = new Application();
        RuntimeLog.Start();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "interaction-error.txt"), e.Exception.ToString());
            e.Handled = true; app.Shutdown(1);
        };
        PetWindow pet = new PetWindow(new PetSettings { Size = .57, Roam = false, Left = 60, Top = 200 }, false, true);
        pet.ShowInTaskbar = true; pet.Title = "小白 · 交互验证";
        pet.LocationChanged += delegate
        {
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "interaction-position.txt"),
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + pet.Left + "," + pet.Top + " mouse=" + System.Windows.Input.Mouse.LeftButton + Environment.NewLine);
        };
        string inputLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "interaction-input.txt");
        System.Windows.Input.MouseButtonEventHandler input = delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            File.AppendAllText(inputLog, DateTime.Now.ToString("HH:mm:ss.fff") + " " + e.RoutedEvent.Name + " "
                + e.GetPosition(pet) + " button=" + e.ButtonState + Environment.NewLine);
        };
        pet.AddHandler(System.Windows.Input.Mouse.PreviewMouseDownEvent, input, true);
        pet.AddHandler(System.Windows.Input.Mouse.PreviewMouseUpEvent, input, true);
        app.Run(pet);
    }
}
