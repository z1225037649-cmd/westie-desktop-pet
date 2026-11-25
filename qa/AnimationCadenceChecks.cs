using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WestiePet;

// Compile with production sources and /main:AnimationCadenceChecks.
// All windows use verify mode and remain outside every monitor. The native
// messages below exercise only this process's verification home window; they
// do not perform a physical title-bar drag or measure compositor/GPU presents.
static class AnimationCadenceChecks
{
    const int EnterMove = 0x0231, ExitMove = 0x0232;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static readonly List<string> Summary = new List<string>();
    static readonly List<string> Rows = new List<string> {
        "breed,phase,clock_s,phase_age_s,ui_update,model_update,effect_update,sleep_pose,effect_top,corgi_frame" };
    static readonly string[] DeadlineNames = { "stateStart", "stateUntil", "bubbleUntil", "nextAction" };
    static Application app;
    static PetWindow pet;
    static WestieModel model;
    static TextBlock effect;
    static Stopwatch productionClock;
    static DispatcherTimer driver;
    static EventHandler render, tick;
    static HomeHook homeHook;
    static FieldInfo modelTimestamp;
    static object animatedModel;
    static Exception failure;
    static string output;
    static bool corgi, repeatedEnter;
    static int groups;
    static Phase phase;
    static double phaseStart, phaseDuration, seenTick, seenModel, seenTop;
    static double startPose, previousPose, maxPoseStep;
    static int positivePoseSteps, negativePoseSteps, effectChanges, frameChanges, seenFrame;
    static readonly List<double> UiTimes = new List<double>(), ModelTimes = new List<double>();
    static double pausedModel, pausedTick, pausedTop, pausedOpacity, pauseStarted, remainingAction;
    static int pausedFrame;
    static object pausedGeometry;
    static double[] pausedDeadlines;

    enum Phase { Warmup, SleepDescent, SettledSleep, Wake, HomePrepare, HomePause, HomeResume }
    delegate IntPtr HomeHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 1) { Console.Error.WriteLine("Usage: AnimationCadenceChecks.exe [output-directory]"); return 2; }
        output = Path.GetFullPath(args.Length == 1 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "animation-cadence"));
        try
        {
            Directory.CreateDirectory(output);
            Summary.Add("Production animation cadence and home-move regression; STA, verify=true, runSelfTest=false, offscreen windows.");
            Summary.Add("A 5ms DispatcherTimer interleaves the production rendering and service-timer callbacks. Counts observe actual model timestamps, accepted Tick timestamps and changed overlay positions.");
            Summary.Add("The synthetic WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE messages exercise our own HwndSource hook, not a physical native drag. Rates are application update cadence, not GPU/desktop frame rate.");
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate {
                try { CreatePet(false); }
                catch (Exception error) { Finish(error); }
            }));
            app.Run();
            WriteResults();
            if (failure != null) throw failure;
            Console.WriteLine("PASS: animation cadence and home-move regression: " + Path.Combine(output, "animation-cadence.txt"));
            return 0;
        }
        catch (Exception error)
        {
            if (Directory.Exists(output)) File.WriteAllText(Path.Combine(output, "animation-cadence-error.txt"), error.ToString(), Encoding.UTF8);
            Console.Error.WriteLine("ANIMATION_CADENCE_ERROR: " + error);
            return 1;
        }
        finally
        {
            if (driver != null) driver.Stop();
            if (pet != null) { try { pet.Close(); } catch (Exception cleanup) { Console.Error.WriteLine(cleanup); } }
        }
    }

    static string Breed { get { return corgi ? "corgi" : "westie"; } }
    static double Now { get { return productionClock.Elapsed.TotalSeconds; } }
    static double Pose { get { return corgi ? model.CorgiPet.SleepPose : model.GeneratedPet.SleepPose; } }
    static double ModelStamp { get { return (double)modelTimestamp.GetValue(animatedModel); } }
    static int Frame { get { return corgi ? model.CorgiPet.FrameVersion : -1; } }
    static object GeometryToken
    {
        get
        {
            if (corgi) return model.CorgiPet.CurrentBitmap;
            return ((MeshGeometry3D)((GeometryModel3D)model.GeneratedPet.Model.Children[0]).Geometry).Positions;
        }
    }

    static void CreatePet(bool useCorgi)
    {
        corgi = useCorgi;
        pet = new PetWindow(new PetSettings { Breed = Breed, Size = .57, Roam = false,
            Topmost = false, AutoHideFullscreen = false }, true, true, false);
        pet.ShowActivated = false; pet.ShowInTaskbar = false;
        pet.Show(); pet.UpdateLayout();
        Require(pet.Left + pet.Width < SystemParameters.VirtualScreenLeft, "Verification pet is not offscreen.");
        model = Field<WestieModel>(pet, "model");
        animatedModel = corgi ? (object)model.CorgiPet : model.GeneratedPet;
        modelTimestamp = animatedModel.GetType().GetField(corgi ? "previousAge" : "previousTime", Private);
        Require(modelTimestamp != null, "Actual animation timestamp is unavailable.");
        productionClock = Field<Stopwatch>(pet, "clock");
        effect = Field<TextBlock>(pet, "effect");
        render = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), pet, Method("RenderGeneratedFrame"));
        tick = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), pet, Method("Tick"));
        homeHook = (HomeHook)Delegate.CreateDelegate(typeof(HomeHook), pet, Method("HomeWindowMessage"));
        Require(!Field<DispatcherTimer>(pet, "timer").IsEnabled && !Field<bool>(pet, "generatedRenderingAttached"),
            "Verification mode unexpectedly enabled production scheduling.");
        StartPhase(Phase.Warmup, .60);
        driver = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(5) };
        driver.Tick += Drive; driver.Start();
    }

    static void StartPhase(Phase next, double duration)
    {
        phase = next; phaseDuration = duration; phaseStart = Now; groups = 0;
        UiTimes.Clear(); ModelTimes.Clear(); effectChanges = frameChanges = positivePoseSteps = negativePoseSteps = 0;
        maxPoseStep = 0; startPose = previousPose = Pose;
        seenTick = Field<double>(pet, "lastTick"); seenModel = ModelStamp; seenTop = Canvas.GetTop(effect); seenFrame = Frame;
    }

    static void Drive(object sender, EventArgs args)
    {
        try
        {
            // Alternate which production entry point reaches each deadline
            // first, then try a third immediate callback to expose duplicates.
            if ((groups++ & 1) == 0) { render(null, EventArgs.Empty); Observe(); tick(null, EventArgs.Empty); Observe(); }
            else { tick(null, EventArgs.Empty); Observe(); render(null, EventArgs.Empty); Observe(); }
            render(null, EventArgs.Empty); Observe();
            if (phase == Phase.HomePause && !repeatedEnter && Now - phaseStart > .40)
            {
                repeatedEnter = true;
                SendHome(EnterMove);
                Require(Field<double>(pet, "homeMoveStarted") == pauseStarted, "Repeated move-enter reset the pause origin.");
            }
            if (Now - phaseStart >= phaseDuration) EndPhase();
        }
        catch (Exception error) { Finish(error); }
    }

    static void Observe()
    {
        double accepted = Field<double>(pet, "lastTick"), stamp = ModelStamp, top = Canvas.GetTop(effect), pose = Pose;
        bool uiChanged = !accepted.Equals(seenTick), modelChanged = !stamp.Equals(seenModel), effectChanged = !top.Equals(seenTop);
        if (uiChanged) { UiTimes.Add(accepted); seenTick = accepted; }
        if (modelChanged)
        {
            ModelTimes.Add(accepted); seenModel = stamp;
            double step = pose - previousPose;
            if (step > .000001) positivePoseSteps++;
            if (step < -.000001) negativePoseSteps++;
            maxPoseStep = Math.Max(maxPoseStep, Math.Abs(step)); previousPose = pose;
        }
        if (effectChanged) { effectChanges++; seenTop = top; }
        if (Frame != seenFrame) { frameChanges++; seenFrame = Frame; }
        if (phase == Phase.HomePause)
        {
            Require(stamp.Equals(pausedModel) && accepted.Equals(pausedTick) && Frame == pausedFrame,
                Breed + " animation advanced during home move.");
            Require(ReferenceEquals(GeometryToken, pausedGeometry), Breed + " geometry/bitmap was replaced during home move.");
            Require(top.Equals(pausedTop) && effect.Opacity.Equals(pausedOpacity), "Overlay changed during home move.");
            Require(Field<string>(pet, "mode") == "feed", "Timed action expired during home move.");
            for (int i = 0; i < DeadlineNames.Length; i++)
                Require(Field<double>(pet, DeadlineNames[i]).Equals(pausedDeadlines[i]), "Deadline changed before home move ended: " + DeadlineNames[i]);
        }
        if (uiChanged || modelChanged || effectChanged)
            Rows.Add(string.Format(Invariant, "{0},{1},{2:F6},{3:F6},{4},{5},{6},{7:F6},{8:F6},{9}",
                Breed, phase, Now, Now - phaseStart, uiChanged ? 1 : 0, modelChanged ? 1 : 0,
                effectChanged ? 1 : 0, pose, top, corgi ? model.CorgiPet.CurrentFrame : -1));
    }

    static void EndPhase()
    {
        double seconds = Now - phaseStart;
        Summary.Add(string.Format(Invariant, "{0}/{1}: {2:F3}s, driver groups={3}, UI={4} ({5:F2}Hz), model={6} ({7:F2}Hz), effect changes={8} ({9:F2}Hz), bitmap changes={10}, sleep pose={11:F6}->{12:F6}, max pose step={13:F6}.",
            Breed, phase, seconds, groups, UiTimes.Count, UiTimes.Count / seconds, ModelTimes.Count, ModelTimes.Count / seconds,
            effectChanges, effectChanges / seconds, frameChanges, startPose, Pose, maxPoseStep));
        Console.WriteLine(Summary[Summary.Count - 1]);
        CheckNoDuplicateUpdates(UiTimes, "UI"); CheckNoDuplicateUpdates(ModelTimes, "model");
        switch (phase)
        {
            case Phase.Warmup:
                Act("sleep"); StartPhase(Phase.SleepDescent, corgi ? 1.60 : 3.15); break;
            case Phase.SleepDescent:
                CheckRate(UiTimes.Count, seconds, 20, 35, "sleep UI");
                CheckRate(ModelTimes.Count, seconds, 20, 35, "sleep transition model");
                CheckRate(effectChanges, seconds, 20, 35, "sleep overlay");
                Require(negativePoseSteps == 0 && positivePoseSteps >= (corgi ? 4 : 20), "Sleep pose did not progress through successive intermediate poses.");
                if (corgi) { Require(frameChanges >= 8, "Corgi sleep animation did not advance its baked frames."); Act("wake"); StartPhase(Phase.Wake, 1.30); }
                else
                {
                    Require(Pose > .999 && maxPoseStep < .40, "Westie sleep transition did not settle smoothly.");
                    StartPhase(Phase.SettledSleep, 2.10);
                }
                break;
            case Phase.SettledSleep:
                CheckRate(UiTimes.Count, seconds, 20, 35, "settled sleep UI");
                CheckRate(effectChanges, seconds, 20, 35, "settled sleep overlay");
                CheckRate(ModelTimes.Count, seconds, 5, 8, "settled sleep model");
                Require(ModelTimes.Count * 2 < effectChanges && effect.Visibility == Visibility.Visible, "Settled sleep did not separate mesh and visible overlay cadence.");
                Act("wake"); StartPhase(Phase.Wake, 1.30); break;
            case Phase.Wake:
                CheckRate(UiTimes.Count, seconds, 20, 35, "wake UI");
                CheckRate(ModelTimes.Count, seconds, 20, 35, "wake model");
                Require(negativePoseSteps > 0, "Wake did not reduce the sleep pose.");
                if (!corgi) Require(Pose < .05, "Westie wake remained in its sleeping pose.");
                Method("ShowHome").Invoke(pet, null); CheckHome();
                Act("feed"); StartPhase(Phase.HomePrepare, .30); break;
            case Phase.HomePrepare:
                BeginHomePause(); break;
            case Phase.HomePause:
                Require(groups >= 10 && UiTimes.Count == 0 && ModelTimes.Count == 0 && effectChanges == 0, "Home move did not suppress actual updates.");
                EndHomePause(); StartPhase(Phase.HomeResume, .35); break;
            case Phase.HomeResume:
                Require(ModelTimes.Count >= 3 && UiTimes.Count >= 3 && !ReferenceEquals(GeometryToken, pausedGeometry), "Animation did not resume after home move.");
                Require(Field<string>(pet, "mode") == "feed" && Field<double>(pet, "stateUntil") > Now, "Dragging consumed the timed action's remaining duration.");
                CheckCloseReopen();
                driver.Stop(); driver.Tick -= Drive; driver = null;
                pet.Close(); pet = null;
                if (corgi) Finish(null);
                else app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate {
                    try { CreatePet(true); } catch (Exception error) { Finish(error); }
                }));
                break;
        }
    }

    static void BeginHomePause()
    {
        double now = Now;
        // Pause longer than the remaining deadline, then verify the original
        // action still has its remaining time after release.
        Set("stateUntil", now + .70); Set("bubbleUntil", now + .75); Set("nextAction", now + .80);
        SendHome(EnterMove);
        Require(Field<bool>(pet, "homeMoving"), "Native move-enter did not reach the registered home hook.");
        Require(!Field<bool>(pet, "generatedRenderingAttached"), "Home move retained the rendering subscription.");
        Require(Field<DispatcherTimer>(pet, "timer").Interval == TimeSpan.FromMilliseconds(100), "Home move did not select the service cadence.");
        pauseStarted = Field<double>(pet, "homeMoveStarted");
        pausedDeadlines = new double[DeadlineNames.Length];
        for (int i = 0; i < DeadlineNames.Length; i++) pausedDeadlines[i] = Field<double>(pet, DeadlineNames[i]);
        remainingAction = pausedDeadlines[1] - pauseStarted;
        pausedModel = ModelStamp; pausedTick = Field<double>(pet, "lastTick"); pausedFrame = Frame;
        pausedGeometry = GeometryToken; pausedTop = Canvas.GetTop(effect); pausedOpacity = effect.Opacity;
        CheckHandled(EnterMove, false); CheckHandled(EnterMove, true);
        repeatedEnter = false; StartPhase(Phase.HomePause, 1.20);
    }

    static void EndHomePause()
    {
        double before = Now;
        SendHome(ExitMove);
        double after = Now, shift = Field<double>(pet, "stateStart") - pausedDeadlines[0];
        Require(!Field<bool>(pet, "homeMoving"), "Native move-exit left the home paused.");
        Require(shift >= before - pauseStarted - .001 && shift <= after - pauseStarted + .001, "Pause compensation does not equal the actual move duration.");
        for (int i = 1; i < DeadlineNames.Length; i++)
            Require(Math.Abs(Field<double>(pet, DeadlineNames[i]) - pausedDeadlines[i] - shift) < .00001,
                "Pause compensation differs for " + DeadlineNames[i]);
        Require(Math.Abs(Field<double>(pet, "stateUntil") - after - remainingAction) < .05, "Remaining action time was not preserved.");
        Require(Field<DispatcherTimer>(pet, "timer").Interval == TimeSpan.FromMilliseconds(33), "Move-exit did not restore the animation timer cadence.");
        CheckHandled(ExitMove, false); CheckHandled(ExitMove, true); CheckHandled(0, false); CheckHandled(0, true);
        Require(Math.Abs(Field<double>(pet, "stateStart") - pausedDeadlines[0] - shift) < .00001, "Repeated move-exit compensated the pause twice.");
        Summary.Add("PASS: " + Breed + " move pause preserved model, overlay and all four deadlines; compensated " + shift.ToString("F3", Invariant) + "s without swallowing native messages.");
    }

    static void CheckCloseReopen()
    {
        Window oldHome = Field<Window>(pet, "home");
        SendHome(EnterMove); oldHome.Close();
        Require(!Field<bool>(pet, "homeMoving") && Field<Window>(pet, "home") == null && Field<HwndSource>(pet, "homeSource") == null,
            "Closing during a move leaked pause state or the home source.");
        Method("ShowHome").Invoke(pet, null); CheckHome();
        Require(!ReferenceEquals(oldHome, Field<Window>(pet, "home")), "Reopening reused the closed home window.");
        SendHome(EnterMove); Require(Field<bool>(pet, "homeMoving"), "Reopened home did not register its native hook.");
        SendHome(ExitMove); Require(!Field<bool>(pet, "homeMoving"), "Reopened home could not leave move mode.");
        Field<Window>(pet, "home").Close();
        Require(!Field<DispatcherTimer>(pet, "timer").IsEnabled, "Verification lifecycle enabled the production timer.");
        Summary.Add("PASS: " + Breed + " close-during-move and reopen clear state and attach a working hook to the new window.");
    }

    static void CheckHome()
    {
        Window home = Field<Window>(pet, "home");
        Require(home != null && home.IsVisible && home.Left + home.Width < SystemParameters.VirtualScreenLeft,
            "Verification home is missing or not offscreen.");
        Require(Field<HwndSource>(pet, "homeSource") != null, "Home SourceInitialized did not register its native source.");
    }
    static void SendHome(int message)
    {
        CheckHome();
        SendMessage(new WindowInteropHelper(Field<Window>(pet, "home")).Handle, message, IntPtr.Zero, IntPtr.Zero);
    }
    static void CheckHandled(int message, bool original)
    {
        bool handled = original;
        homeHook(new WindowInteropHelper(Field<Window>(pet, "home")).Handle, message, IntPtr.Zero, IntPtr.Zero, ref handled);
        Require(handled == original, "Home hook changed handled for message " + message + ".");
    }
    static void CheckNoDuplicateUpdates(List<double> times, string label)
    {
        for (int i = 1; i < times.Count; i++)
            Require(times[i] - times[i - 1] >= .020 - .000001, Breed + "/" + phase + " duplicated " + label + " updates within 20ms.");
    }
    static void CheckRate(int count, double seconds, double minimum, double maximum, string label)
    {
        double rate = count / seconds;
        Require(rate >= minimum && rate <= maximum, Breed + " " + label + " expected " + minimum + "-" + maximum + "Hz, observed " + rate.ToString("F2", Invariant) + "Hz.");
    }
    static void Act(string action) { Method("Act").Invoke(pet, new object[] { action }); }
    static MethodInfo Method(string name)
    {
        MethodInfo method = typeof(PetWindow).GetMethod(name, Private);
        if (method == null) throw new MissingMethodException(typeof(PetWindow).FullName, name);
        return method;
    }
    static T Field<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Private);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        return (T)field.GetValue(target);
    }
    static void Set(string name, double value) { typeof(PetWindow).GetField(name, Private).SetValue(pet, value); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Finish(Exception error)
    {
        if (driver != null) { driver.Stop(); driver.Tick -= Drive; driver = null; }
        failure = error is TargetInvocationException && error.InnerException != null ? error.InnerException : error;
        if (pet != null)
        {
            PetWindow closing = pet; pet = null;
            try { closing.Close(); } catch (Exception cleanup) { failure = failure == null ? cleanup : new AggregateException(failure, cleanup); }
        }
        app.Shutdown(failure == null ? 0 : 1);
    }
    static void WriteResults()
    {
        Summary.Add(failure == null ? "PASS: all animation cadence and home-move checks." : "FAIL: " + failure.Message);
        File.WriteAllLines(Path.Combine(output, "animation-cadence.txt"), Summary.ToArray(), Encoding.UTF8);
        File.WriteAllLines(Path.Combine(output, "animation-cadence.csv"), Rows.ToArray(), Encoding.UTF8);
    }
}
