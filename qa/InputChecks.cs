using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WestiePet;

static class InputChecks
{
    static T Field<T>(object target, string name)
    { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    static void Set(object target, string name, object value)
    { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    static void BeginGesture(PetWindow pet, Point origin)
    {
        Set(pet, "pressed", true); Set(pet, "dragging", false);
        Set(pet, "dragPoint", origin); Set(pet, "dragLeft", pet.Left); Set(pet, "dragTop", pet.Top);
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool baseline = Array.IndexOf(args, "--baseline") >= 0;
        int result = 0;
        Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        PetWindow pet = new PetWindow(new PetSettings { Size = .57, Roam = false }, true, true, false);
        // Exercise the real transparent input surface without the image-rendering self-test.
        pet.Loaded += delegate
        {
            pet.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
            {
                try
                {
                    Viewport3D viewport = Field<Viewport3D>(pet, "viewport");
                    SilhouetteHitArea input = Field<SilhouetteHitArea>(pet, "pointerHitArea");
                    Console.WriteLine("Viewport3D.IsHitTestVisible=" + viewport.IsHitTestVisible);
                    if (!baseline && viewport.IsHitTestVisible)
                        throw new Exception("The desktop dog must route pointer input through the 2D surface, not the animated 3D mesh.");
                    foreach (double scale in new[] { .4, .57, 1.0, 1.6 })
                    {
                        pet.Width = 320 * scale; pet.Height = 350 * scale; pet.UpdateLayout();
                        Stopwatch watch = Stopwatch.StartNew();
                        Point[] silhouettePoints = { new Point(60,170), new Point(85,160), new Point(110,150), new Point(135,150),
                            new Point(160,150), new Point(185,145), new Point(210,145), new Point(240,140), new Point(220,180), new Point(80,200) };
                        for (int sample = 0; sample < 30; sample++)
                        {
                            Point local = silhouettePoints[sample % silhouettePoints.Length];
                            Point point = input.TranslatePoint(local, pet);
                            IInputElement hit = pet.InputHitTest(point);
                            if (!baseline && !ReferenceEquals(hit, input))
                                throw new Exception("Pointer bypassed the 2D input surface at " + scale + ": " + hit);
                        }
                        IInputElement emptySpace = pet.InputHitTest(input.TranslatePoint(new Point(5, 10), pet));
                        if (!baseline && ReferenceEquals(emptySpace, input))
                            throw new Exception("The empty margin around the dog must not intercept desktop input.");
                        Console.WriteLine("PASS: 30 pointer hit tests at " + scale + " in " + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms.");
                    }
                    if (!baseline)
                    {
                        MethodInfo finish = typeof(PetWindow).GetMethod("FinishPointerInteraction", BindingFlags.Instance | BindingFlags.NonPublic);
                        Point release = new Point(123, 456);
                        // Supply deterministic event coordinates. Reading the physical
                        // cursor or capturing it off screen makes checks depend on user input.
                        // Native down/move/up capture is covered by InteractionHarness.
                        BeginGesture(pet, release);
                        finish.Invoke(pet, new object[] { release });
                        if (Field<int>(pet, "affection") != 1 || Field<bool>(pet, "pressed"))
                            throw new Exception("Click failed to pet the dog or left pointer capture stuck.");
                        Console.WriteLine("PASS: click completion pets the dog and clears the gesture state.");
                        BeginGesture(pet, release - new Vector(50, 30));
                        double originalLeft = pet.Left;
                        finish.Invoke(pet, new object[] { release });
                        if (Field<int>(pet, "affection") != 1 || Field<bool>(pet, "pressed") || pet.Left == originalLeft)
                            throw new Exception("A drag without an intermediate move event was treated as petting or did not move.");
                        // Restore the off-screen location before the next render pass.
                        pet.Left = SystemParameters.VirtualScreenLeft - pet.Width - 100;
                        Console.WriteLine("PASS: down/up-only drag uses the release position without triggering petting.");
                        input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                            { RoutedEvent = UIElement.MouseRightButtonUpEvent });
                        if (!Field<bool>(pet, "menuOpen")) throw new Exception("Right click failed to open the context menu.");
                        ContextMenu openMenu = Field<ContextMenu>(pet, "petContextMenu");
                        if (openMenu != null) openMenu.IsOpen = false;
                        Console.WriteLine("PASS: right click reaches the production context menu handler.");
                    }
                }
                catch (Exception error) { Console.WriteLine("FAIL: " + error); result = 1; }
                finally { pet.Close(); app.Shutdown(); Environment.Exit(result); }
            }));
        };
        app.Run(pet);
        return result;
    }
}
