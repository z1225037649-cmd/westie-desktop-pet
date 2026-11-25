using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

// Run one immutable application EXE per process:
// WindowPerformanceChecks.exe <application.exe> <output-directory> [frames>=60]
// No application-source references are needed. This deliberately invokes the
// production animation/input primitives, not PetWindow.Tick or its scheduler.
static class WindowPerformanceChecks
{
    static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    const int WarmupFrames = 30;
    const double FramesPerSecond = 30;
    const double WalkSpeed = 27;
    static readonly string[] Scenarios = { "idle", "walk-stationary", "walk-moving-before", "walk-moving-after", "idle-home-open" };
    static readonly List<Sample> Samples = new List<Sample>();
    static readonly List<string> Summary = new List<string>();
    static Application app;
    static Assembly production;
    static Window pet;
    static object model;
    static Action<double, string, double> animate;
    static Action updateHit;
    static DispatcherTimer driver;
    static readonly Stopwatch scenarioClock = new Stopwatch();
    static readonly Process process = Process.GetCurrentProcess();
    static Exception failure;
    static string output, assemblyPath;
    static int frames = 90, scenarioIndex = -1, delivered, renderEvents, measuredRenderStart;
    static double offscreenLeft, previousCallback = double.NaN, measuredStart, measuredLast;
    static TimeSpan cpuStart;
    static long privateStart, workingStart, managedStart;
    static int[] gcStart;

    sealed class Sample
    {
        internal string Scenario;
        internal int Frame, RenderingCallbacks;
        internal double PoseTime, Age, DriverGapMs, Left, LeftMs, AnimateMs, HitMs, TotalMs;
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 2 || args.Length > 3)
        {
            Console.Error.WriteLine("Usage: WindowPerformanceChecks.exe <application.exe> <output-directory> [frames>=60]");
            return 2;
        }
        try
        {
            if (args.Length == 3 && (!int.TryParse(args[2], NumberStyles.Integer, Invariant, out frames) || frames < 60 || frames > 3600))
                throw new ArgumentException("Measured frames per scenario must be between 60 and 3600.");
            assemblyPath = Path.GetFullPath(args[0]);
            output = Path.GetFullPath(args[1]);
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Application assembly was not found.", assemblyPath);
            Directory.CreateDirectory(output);
            production = Assembly.LoadFrom(assemblyPath);
            Summary.Add("Application: " + assemblyPath);
            using (Stream file = File.OpenRead(assemblyPath))
            using (SHA256 hash = SHA256.Create())
                Summary.Add("Application SHA256: " + BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant());
            Summary.Add("Assembly: " + production.FullName);
            Summary.Add("Harness: " + Assembly.GetExecutingAssembly().Location);
            Summary.Add("STA, production PetWindow(verify=true, startQuietly=true, runSelfTest=false); no tray, production timer or user-settings writes.");
            Summary.Add("Each scenario creates a fresh offscreen Westie window at size 0.57; 30 timer-driven warmup frames, then " + frames + " measured frames.");
            Summary.Add("Driver: DispatcherTimer at 30 Hz, Background priority; Application.Run keeps layout/render work pumping between callbacks. No synchronous RenderTargetBitmap readback or per-frame UpdateLayout.");
            Summary.Add("Measured total is synchronous Left setter + production Animate + production UpdateGeneratedHitArea, including synchronous invalidations they cause. Deferred layout/render/compositor work is outside that total.");
            Summary.Add("walk-moving-before sets Left before Animate/hit; walk-moving-after sets Left after Animate/hit. Both move 27 DIPs per second of deterministic pose time, entirely outside all monitors.");
            Summary.Add("Deterministic pose time advances by 1/30 second per delivered callback. Late callbacks are not caught up. Callback gaps and wall-clock rate describe this diagnostic driver, NOT production Tick cadence, GPU presents or visible desktop frame rate.");
            Summary.Add("Windows stay outside all monitors and may be occluded by the compositor. The home case adds the actual production preview window; this is not a full visible-desktop performance test.");
            Summary.Add("Dialogue stays closed in verify mode; production foreground/fullscreen polling, auto-roaming decisions and pointer gestures are not invoked.");
            Summary.Add("Logical processors: " + Environment.ProcessorCount);
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            CompositionTarget.Rendering += CountRendering;
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(BeginScenario));
            app.Run();
            WriteResults();
            if (failure != null) throw failure;
            Console.WriteLine("DIAGNOSTIC complete: " + Path.Combine(output, "window-performance.txt"));
            return 0;
        }
        catch (Exception error)
        {
            if (output != null && Directory.Exists(output))
                File.WriteAllText(Path.Combine(output, "window-performance-error.txt"), error.ToString(), Encoding.UTF8);
            Console.Error.WriteLine("WINDOW_PERFORMANCE_ERROR: " + error);
            return 1;
        }
        finally
        {
            CompositionTarget.Rendering -= CountRendering;
            try { CloseScenario(); }
            catch (Exception cleanup) { Console.Error.WriteLine("WINDOW_PERFORMANCE_CLEANUP: " + cleanup); }
            process.Dispose();
        }
    }

    static void CountRendering(object sender, EventArgs args) { renderEvents++; }

    static void BeginScenario()
    {
        try
        {
            scenarioIndex++;
            if (scenarioIndex == Scenarios.Length) { app.Shutdown(); return; }
            Type settingsType = production.GetType("WestiePet.PetSettings", true);
            Type windowType = production.GetType("WestiePet.PetWindow", true);
            object settings = Activator.CreateInstance(settingsType);
            Set(settings, "Breed", "westie"); Set(settings, "Size", .57);
            Set(settings, "Roam", false); Set(settings, "AutoHideFullscreen", false); Set(settings, "Topmost", false);
            ConstructorInfo constructor = windowType.GetConstructor(Members, null,
                new[] { settingsType, typeof(bool), typeof(bool), typeof(bool) }, null);
            if (constructor == null) throw new MissingMethodException(windowType.FullName, ".ctor(PetSettings,bool,bool,bool)");
            pet = (Window)constructor.Invoke(new object[] { settings, true, true, false });
            pet.ShowActivated = false; pet.ShowInTaskbar = false; pet.Topmost = false;
            // Even the last moving frame remains 200 DIPs left of every monitor.
            offscreenLeft = SystemParameters.VirtualScreenLeft - pet.Width - 200 - (WarmupFrames + frames + 1) * WalkSpeed / FramesPerSecond;
            pet.Left = offscreenLeft; pet.Top = SystemParameters.VirtualScreenTop;
            Set(pet, "stateUntil", double.PositiveInfinity); Set(pet, "nextAction", double.PositiveInfinity);
            model = Get(pet, "model");
            if (Get(model, "generated") == null) throw new InvalidOperationException("Expected the actual generated Westie model.");
            Set(model, "WalkingPaused", false);
            ((AxisAngleRotation3D)Get(model, "Heading")).Angle = IsWalk ? -68 : 52;
            animate = (Action<double, string, double>)Delegate.CreateDelegate(typeof(Action<double, string, double>), model,
                Method(model, "Animate", new[] { typeof(double), typeof(string), typeof(double) }));
            updateHit = (Action)Delegate.CreateDelegate(typeof(Action), pet, Method(pet, "UpdateGeneratedHitArea", Type.EmptyTypes));
            Method(model, "BeginAction", new[] { typeof(string) }).Invoke(model, new object[] { State });
            Set(pet, "mode", State);
            pet.Show(); pet.UpdateLayout();
            if (Scenarios[scenarioIndex] == "idle-home-open")
            {
                Method(pet, "ShowHome", Type.EmptyTypes).Invoke(pet, null);
                Window home = (Window)Get(pet, "home");
                if (home == null || !home.IsVisible) throw new InvalidOperationException("Production ShowHome did not create a visible verification window.");
                home.ShowActivated = false; home.Topmost = false;
                if (home.Left + home.Width > SystemParameters.VirtualScreenLeft)
                    throw new InvalidOperationException("Verification home window was not placed offscreen.");
            }
            delivered = 0; previousCallback = double.NaN;
            scenarioClock.Restart();
            driver = new DispatcherTimer(DispatcherPriority.Background, app.Dispatcher) { Interval = TimeSpan.FromSeconds(1 / FramesPerSecond) };
            driver.Tick += DeliverFrame;
            driver.Start();
        }
        catch (Exception error) { Fail(error); }
    }

    static bool IsWalk { get { return Scenarios[scenarioIndex].StartsWith("walk", StringComparison.Ordinal); } }
    static string State { get { return IsWalk ? "walk" : "idle"; } }

    static void DeliverFrame(object sender, EventArgs args)
    {
        try
        {
            bool measured = delivered >= WarmupFrames;
            if (delivered == WarmupFrames)
            {
                process.Refresh(); cpuStart = process.TotalProcessorTime;
                privateStart = process.PrivateMemorySize64; workingStart = process.WorkingSet64;
                managedStart = GC.GetTotalMemory(false);
                gcStart = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                measuredStart = scenarioClock.Elapsed.TotalSeconds; measuredRenderStart = renderEvents;
            }
            double callback = scenarioClock.Elapsed.TotalSeconds;
            double gap = double.IsNaN(previousCallback) ? 0 : (callback - previousCallback) * 1000;
            previousCallback = callback;
            double age = delivered / FramesPerSecond, time = 20 + age;
            bool moveBefore = Scenarios[scenarioIndex] == "walk-moving-before";
            bool moveAfter = Scenarios[scenarioIndex] == "walk-moving-after";
            double nextLeft = offscreenLeft + (delivered + 1) * WalkSpeed / FramesPerSecond;
            long started = Stopwatch.GetTimestamp();
            if (moveBefore) pet.Left = nextLeft;
            long moved = Stopwatch.GetTimestamp();
            animate(time, State, age);
            long animated = Stopwatch.GetTimestamp();
            updateHit();
            long hitFinished = Stopwatch.GetTimestamp();
            if (moveAfter) pet.Left = nextLeft;
            long finished = Stopwatch.GetTimestamp();
            if (measured)
            {
                Samples.Add(new Sample { Scenario = Scenarios[scenarioIndex], Frame = delivered - WarmupFrames,
                    PoseTime = time, Age = age, DriverGapMs = gap, Left = pet.Left,
                    LeftMs = moveBefore ? Milliseconds(moved - started) : moveAfter ? Milliseconds(finished - hitFinished) : 0,
                    AnimateMs = Milliseconds(animated - moved), HitMs = Milliseconds(hitFinished - animated),
                    TotalMs = Milliseconds(finished - started), RenderingCallbacks = renderEvents - measuredRenderStart });
                measuredLast = callback;
            }
            delivered++;
            if (delivered < WarmupFrames + frames) return;
            driver.Stop(); driver.Tick -= DeliverFrame; driver = null;
            // Allow the last submitted geometry to traverse the dispatcher render
            // work before taking process totals and destroying this scenario.
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(EndScenario));
        }
        catch (Exception error) { Fail(error); }
    }

    static void EndScenario()
    {
        try
        {
            process.Refresh();
            double elapsed = scenarioClock.Elapsed.TotalSeconds - measuredStart;
            double cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
            List<Sample> samples = Samples.FindAll(delegate(Sample sample) { return sample.Scenario == Scenarios[scenarioIndex]; });
            Summary.Add(Scenarios[scenarioIndex] + ": n=" + samples.Count
                + ", Left p50/p95=" + Stats(samples, delegate(Sample s) { return s.LeftMs; })
                + ", Animate p50/p95=" + Stats(samples, delegate(Sample s) { return s.AnimateMs; })
                + ", hit p50/p95=" + Stats(samples, delegate(Sample s) { return s.HitMs; })
                + ", synchronous total p50/p95=" + Stats(samples, delegate(Sample s) { return s.TotalMs; }) + " ms"
                + ", callback gap p50/p95=" + Stats(samples, delegate(Sample s) { return s.DriverGapMs; }) + " ms"
                + ", driver callback rate=" + N((frames - 1) / Math.Max(.001, measuredLast - measuredStart)) + " Hz"
                + ", Rendering events=" + (renderEvents - measuredRenderStart));
            Summary.Add("  Process interval=" + N(elapsed) + " s, CPU=" + N(cpuMs) + " ms ("
                + N(cpuMs / Math.Max(.001, elapsed) / 10) + "% of one core; all process threads), private bytes delta="
                + (process.PrivateMemorySize64 - privateStart) + ", working-set delta=" + (process.WorkingSet64 - workingStart)
                + ", managed retained delta=" + (GC.GetTotalMemory(false) - managedStart) + ", GC0/1/2="
                + (GC.CollectionCount(0) - gcStart[0]) + "/" + (GC.CollectionCount(1) - gcStart[1]) + "/" + (GC.CollectionCount(2) - gcStart[2]));
            Console.WriteLine(Summary[Summary.Count - 2]);
            CloseScenario();
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(BeginScenario));
        }
        catch (Exception error) { Fail(error); }
    }

    static void CloseScenario()
    {
        if (driver != null) { driver.Stop(); driver.Tick -= DeliverFrame; driver = null; }
        if (pet != null) { Window closing = pet; pet = null; closing.Close(); }
        model = null; animate = null; updateHit = null;
    }

    static void Fail(Exception error)
    {
        failure = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
        try { CloseScenario(); }
        catch (Exception cleanup) { failure = new AggregateException(failure, cleanup); }
        app.Shutdown(1);
    }

    static object Get(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Members);
        if (field != null) return field.GetValue(target);
        PropertyInfo property = target.GetType().GetProperty(name, Members);
        if (property != null) return property.GetValue(target, null);
        throw new MissingMemberException(target.GetType().FullName, name);
    }

    static void Set(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, Members);
        if (field != null) { field.SetValue(target, value); return; }
        PropertyInfo property = target.GetType().GetProperty(name, Members);
        if (property != null) { property.SetValue(target, value, null); return; }
        throw new MissingMemberException(target.GetType().FullName, name);
    }

    static MethodInfo Method(object target, string name, Type[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name, Members, null, arguments, null);
        if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
        return method;
    }

    static double Milliseconds(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
    static string N(double value) { return value.ToString("F3", Invariant); }
    static string Stats(List<Sample> samples, Func<Sample, double> select)
    {
        List<double> values = new List<double>(); foreach (Sample sample in samples) values.Add(select(sample)); values.Sort();
        return N(Percentile(values, .5)) + "/" + N(Percentile(values, .95));
    }
    static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return double.NaN;
        double at = (values.Count - 1) * percentile; int low = (int)Math.Floor(at), high = (int)Math.Ceiling(at);
        return values[low] + (values[high] - values[low]) * (at - low);
    }

    static void WriteResults()
    {
        StringBuilder csv = new StringBuilder("scenario,frame,pose_time_s,state_age_s,left_dip,left_setter_ms,animate_ms,hit_ms,synchronous_total_ms,driver_gap_ms,rendering_callbacks_cumulative\r\n");
        foreach (Sample s in Samples)
            csv.AppendFormat(Invariant, "{0},{1},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9:F6},{10}\r\n",
                s.Scenario, s.Frame, s.PoseTime, s.Age, s.Left, s.LeftMs, s.AnimateMs, s.HitMs, s.TotalMs, s.DriverGapMs, s.RenderingCallbacks);
        File.WriteAllText(Path.Combine(output, "window-performance.csv"), csv.ToString(), Encoding.UTF8);
        Summary.Add(failure == null ? "DIAGNOSTIC COMPLETE; no pass/fail performance budget was applied." : "DIAGNOSTIC FAILED; partial measurements retained.");
        File.WriteAllLines(Path.Combine(output, "window-performance.txt"), Summary.ToArray(), Encoding.UTF8);
    }
}
