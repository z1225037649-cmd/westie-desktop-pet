using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WestiePet;

// Standalone STA regression for the complete production animation/input update.
// Compile with the application sources, or reference a previous application
// assembly to measure an immutable baseline with this same harness.
static class GeneratedFrameChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static readonly string Output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated");
    static readonly Func<long> AllocatedBytes = CreateAllocatedCounter();
    static readonly List<Sample> Samples = new List<Sample>();
    static readonly List<string> Summary = new List<string>();
    const double CadenceMinimumGapMs = 20;
    const double CadenceGapToleranceMs = .001; // One microsecond for timestamp subtraction rounding.

    sealed class Sample
    {
        internal string Scenario;
        internal int Frame;
        internal double Heading, Age, AnimateMs, HitAreaMs;
        internal long Allocated;
        internal double TotalMs { get { return AnimateMs + HitAreaMs; } }
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool baseline = Array.IndexOf(args, "--baseline") >= 0;
        Directory.CreateDirectory(Output);
        string prefix = baseline ? "frame-baseline" : "frame-performance";
        Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        PetWindow pet = null;
        Exception failure = null;
        try
        {
            // verify suppresses the tray, live timer, real preference writes,
            // screen placement and automatic SelfTest. The real visual tree is
            // still attached, matching production invalidation costs.
            pet = new PetWindow(new PetSettings { Breed = "westie", Size = .57,
                Roam = false, AutoHideFullscreen = false }, true, true, false);
            pet.Loaded += delegate
            {
                pet.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
                {
                    Action<Exception> finish = delegate(Exception error) { failure = error; pet.Close(); app.Shutdown(); };
                    try
                    {
                        Measure(pet, baseline);
                        if (!baseline) MeasureControls(pet);
                        if (baseline) finish(null);
                        else StartCadenceCheck(pet, finish);
                    }
                    catch (Exception error) { finish(error); }
                }));
            };
            pet.Show(); app.Run();
            WriteResults(prefix);
            if (failure != null) throw failure;
            if (!baseline)
            {
                CheckBudget(Samples, "all transitions");
                string[] scenarios = { "idle", "pet", "pet-to-idle", "feed", "play", "walk", "sleep", "wake", "awake-play" };
                foreach (string scenario in scenarios) CheckBudget(Samples.FindAll(s => s.Scenario == scenario), scenario);
                if (AllocatedBytes != null)
                {
                    long allocated = 0; foreach (Sample sample in Samples) allocated += sample.Allocated;
                    Require(allocated / (double)Samples.Count < 3 * 1024 * 1024,
                        "Complete frame allocates over 3 MiB on average; animation buffers exceed the reduced geometry budget.");
                }
            }
            Console.WriteLine((baseline ? "BASELINE recorded" : "PASS: cold complete-frame budgets") + ": " + Path.Combine(Output, prefix + ".txt"));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(Output, prefix + "-error.txt"), error.ToString());
            Console.Error.WriteLine("GENERATED_FRAME_CHECKS_ERROR: " + error);
            return 1;
        }
    }

    static void Measure(PetWindow pet, bool baseline)
    {
        WestieModel model = Field<WestieModel>(pet, "model");
        object generated = Field<object>(model, "generated");
        Require(generated != null && !model.IsPreRendered, "The production scene must be the generated 3D dog.");
        Require(!Field<Viewport3D>(pet, "viewport").IsHitTestVisible,
            "The production viewport must not route pointer events through its animated triangles.");
        MethodInfo hitMethod = typeof(PetWindow).GetMethod("UpdateGeneratedHitArea", Private);
        Require(hitMethod != null, "Production hit-area update is missing.");
        Action updateHit = (Action)Delegate.CreateDelegate(typeof(Action), pet, hitMethod);
        FieldInfo hitKey = typeof(PetWindow).GetField("generatedHitKey", Private);
        FieldInfo hitMasks = typeof(PetWindow).GetField("generatedHitMasks", Private);
        Set(pet, "stateUntil", double.PositiveInfinity); Set(pet, "nextAction", double.PositiveInfinity);
        double time = 20;
        // Warm only the JIT/geometry paths. Older implementations have their
        // input key and mask cache invalidated for every recorded frame. The
        // new point projection path updates every call without such a cache.
        for (int i = 0; i < 8; i++)
        { time += .056; model.Animate(time, "idle", i * .056); ColdInput(pet, hitKey, hitMasks); updateHit(); }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int[] collections = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
        long retained = GC.GetTotalMemory(false);
        Stopwatch animate = new Stopwatch(), hit = new Stopwatch();
        string[] states = { "idle", "pet", "idle", "feed", "play", "walk", "sleep", "idle", "play" };
        string[] names = { "idle", "pet", "pet-to-idle", "feed", "play", "walk", "sleep", "wake", "awake-play" };
        MethodInfo beginAction = typeof(WestieModel).GetMethod("BeginAction", Private);
        Require(beginAction != null, "The production action reset is missing.");
        const int frames = 28;
        for (int scenario = 0; scenario < states.Length; scenario++)
        {
            beginAction.Invoke(model, new object[] { states[scenario] });
            for (int frame = 0; frame < frames; frame++)
            {
                double age = frame * .056;
                // Alternate continuous sweeps in both directions instead of
                // sampling the same cached 52-degree idle silhouette.
                double heading = -68 + 136 * frame / (frames - 1.0);
                if (scenario % 2 != 0) heading = -heading;
                model.Heading.Angle = heading;
                model.LookAt(Math.Sin(frame * .37), Math.Cos(frame * .23), .85);
                model.WalkingPaused = states[scenario] == "walk" && frame >= 20 && frame < 24;
                ColdInput(pet, hitKey, hitMasks);
                long allocatedBefore = AllocatedBytes == null ? 0 : AllocatedBytes();
                time += .056;
                animate.Restart(); model.Animate(time, states[scenario], age); animate.Stop();
                hit.Restart(); updateHit(); hit.Stop();
                long allocatedAfter = AllocatedBytes == null ? 0 : AllocatedBytes();
                Samples.Add(new Sample { Scenario = names[scenario], Frame = frame, Age = age,
                    Heading = heading, AnimateMs = animate.Elapsed.TotalMilliseconds,
                    HitAreaMs = hit.Elapsed.TotalMilliseconds, Allocated = allocatedAfter - allocatedBefore });
            }
            Console.WriteLine((baseline ? "BASELINE " : "MEASURED ") + names[scenario] + ": " + Statistics(Samples.FindAll(s => s.Scenario == names[scenario])));
        }
        Summary.Add("Production assembly: " + typeof(WestieModel).Assembly.Location);
        Summary.Add("Complete core frame = WestieModel.Animate + production UpdateGeneratedHitArea on the attached verify-only visual tree.");
        Summary.Add("252 frames across idle/pet/feed/play/walk/sleep and wake transitions. Legacy input key/mask cache invalidated when present; the new reusable projection-mask implementation updates on every call without a warm-cache shortcut.");
        Summary.Add("No RenderTargetBitmap call in this harness: any raster readback cost measured here comes from the production input path itself.");
        Summary.Add("Budget: median <25ms, p95 <50ms for all frames and each action; maximum recorded for review.");
        Summary.Add("GC collections during measured sequence: Gen0=" + (GC.CollectionCount(0) - collections[0])
            + ", Gen1=" + (GC.CollectionCount(1) - collections[1]) + ", Gen2=" + (GC.CollectionCount(2) - collections[2]));
        Summary.Add("Managed retained-memory delta (not total allocation): " + (GC.GetTotalMemory(false) - retained) + " bytes.");
        Summary.Add(AllocatedBytes == null
            ? "Per-thread allocation counter unavailable on this .NET Framework; retained-memory delta and GC counts reported without treating them as allocation totals."
            : "Per-core-frame allocated bytes read from GC.GetAllocatedBytesForCurrentThread; CSV/report formatting and cache invalidation are outside this measurement.");
    }

    static void MeasureControls(PetWindow pet)
    {
        typeof(PetWindow).GetMethod("ShowHome", Private).Invoke(pet, null);
        Viewport3D preview = Field<Viewport3D>(pet, "homePreview");
        Require(preview != null && !preview.IsHitTestVisible, "Home preview must not hit-test the animated 3D triangle mesh.");
        Stopwatch watch = Stopwatch.StartNew();
        Window home = Field<Window>(pet, "home");
        for (int i = 0; i < 1000; i++) home.InputHitTest(new Point(100 + i % 170, 100 + i % 110));
        watch.Stop(); double hitMs = watch.Elapsed.TotalMilliseconds;
        Require(hitMs < 150, "Home pointer routing blocks on 3D geometry: " + hitMs + "ms / 1000 points.");
        pet.SwitchBreed("corgi"); // Exclude first construction from cached toggles.
        List<double> switches = new List<double>();
        for (int i = 0; i < 6; i++)
        { watch.Restart(); pet.SwitchBreed(i % 2 == 0 ? "westie" : "corgi"); watch.Stop(); switches.Add(watch.Elapsed.TotalMilliseconds); }
        switches.Sort();
        Require(switches[switches.Count - 1] < 150, "Cached model switching synchronously rebuilds too many frames: " + switches[switches.Count - 1] + "ms.");
        pet.SwitchBreed("westie"); home.Close();
        string controls = "UI controls: 1000 home pointer probes=" + N(hitMs) + "ms; cached model switch median/max="
            + N(Percentile(switches, .5)) + "/" + N(switches[switches.Count - 1]) + "ms.";
        Summary.Add(controls); Console.WriteLine(controls);
    }

    static void ColdInput(PetWindow pet, FieldInfo key, FieldInfo masks)
    {
        if (key != null) key.SetValue(pet, null);
        if (masks == null) return;
        IDictionary dictionary = masks.GetValue(pet) as IDictionary;
        if (dictionary != null) dictionary.Clear();
    }
    static void StartCadenceCheck(PetWindow pet, Action<Exception> finish)
    {
        MethodInfo rendering = typeof(PetWindow).GetMethod("RenderGeneratedFrame", Private);
        MethodInfo ticking = typeof(PetWindow).GetMethod("Tick", Private);
        Require(rendering != null && ticking != null, "The production rendering callback and timer path must both exist.");
        EventHandler render = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), pet, rendering);
        EventHandler tick = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), pet, ticking);
        WestieModel model = Field<WestieModel>(pet, "model");
        object generated = Field<object>(model, "generated");
        FieldInfo previousTime = generated.GetType().GetField("previousTime", Private);
        Require(previousTime != null, "Cadence checks must observe actual model update time, not callback counts.");
        typeof(PetWindow).GetMethod("SetState", Private).Invoke(pet, new object[] { "idle", 0.0 });
        Set(pet, "stateUntil", double.PositiveInfinity); Set(pet, "nextAction", double.PositiveInfinity);
        model.WalkingPaused = false;
        Stopwatch elapsed = Stopwatch.StartNew();
        List<double> actualUpdates = new List<double>();
        int callbacks = 0, multipleUpdateGroups = 0;
        double multipleGroupTotalMs = 0, multipleGroupMaxMs = 0;
        List<string> multipleGroupRows = new List<string> { "driverGroup,acceptedUpdates,groupElapsedMs,firstUpdateTime,lastUpdateTime" };
        double observed = (double)previousTime.GetValue(generated);
        DispatcherTimer driver = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(5) };
        Action observe = delegate
        {
            double now = (double)previousTime.GetValue(generated);
            if (now == observed) return;
            observed = now; actualUpdates.Add(now);
        };
        driver.Tick += delegate
        {
            try
            {
                // Observe any genuine CompositionTarget.Rendering update since
                // the last driver event before starting the interleaved burst.
                observe();
                int before = actualUpdates.Count;
                long groupStarted = Stopwatch.GetTimestamp();
                render(null, EventArgs.Empty); observe(); callbacks++;
                tick(null, EventArgs.Empty); observe(); callbacks++;
                render(null, EventArgs.Empty); observe(); callbacks++;
                double groupElapsedMs = (Stopwatch.GetTimestamp() - groupStarted) * 1000.0 / Stopwatch.Frequency;
                int accepted = actualUpdates.Count - before;
                if (accepted > 1)
                {
                    // A serial driver group may itself span another deadline.
                    // Record that cost; actual update-time gaps decide whether
                    // the second accepted frame was an illegal duplicate.
                    multipleUpdateGroups++; multipleGroupTotalMs += groupElapsedMs;
                    multipleGroupMaxMs = Math.Max(multipleGroupMaxMs, groupElapsedMs);
                    multipleGroupRows.Add((callbacks / 3) + "," + accepted + "," + N(groupElapsedMs)
                        + "," + actualUpdates[before].ToString("F6", Invariant)
                        + "," + actualUpdates[actualUpdates.Count - 1].ToString("F6", Invariant));
                }
                if (elapsed.Elapsed.TotalSeconds < 2) return;
                driver.Stop();
                double seconds = elapsed.Elapsed.TotalSeconds, hz = actualUpdates.Count / seconds;
                List<double> gaps = new List<double>();
                for (int i = 1; i < actualUpdates.Count; i++) gaps.Add((actualUpdates[i] - actualUpdates[i - 1]) * 1000);
                gaps.Sort();
                int duplicateUpdates = gaps.FindAll(gap => gap + CadenceGapToleranceMs < CadenceMinimumGapMs).Count;
                string detail = "verify-only real-time cadence: duration=" + N(seconds) + "s, driver/render/timer calls=" + callbacks
                    + ", actual model updates=" + actualUpdates.Count + ", observed update rate=" + N(hz)
                    + "Hz, actual updates below20ms=" + duplicateUpdates
                    + ", multi-update driver groups=" + multipleUpdateGroups + ", multi-update groups total/max elapsed="
                    + N(multipleGroupTotalMs) + "/" + N(multipleGroupMaxMs)
                    + "ms, actual update-gap min/median/p95/max=" + (gaps.Count == 0 ? "none" : N(gaps[0]) + "/" + N(Percentile(gaps, .5))
                        + "/" + N(Percentile(gaps, .95)) + "/" + N(gaps[gaps.Count - 1])) + "ms.";
                Summary.Add(detail);
                Summary.Add("Duplicate detection uses adjacent actual update gaps below20ms with only a 0.001ms timestamp-rounding tolerance; a driver group accepting two legally spaced frames is diagnostic, not a failure.");
                Summary.Add("Cadence counts actual GeneratedWestie.Update timestamps under interleaved rendering/timer callbacks. It is not a GPU present, display refresh or compositor frame count.");
                File.WriteAllText(Path.Combine(Output, "frame-cadence.txt"), detail
                    + "\r\nThis is production model-update cadence, not GPU/display present count.\r\n");
                File.WriteAllLines(Path.Combine(Output, "frame-cadence-multi-groups.csv"), multipleGroupRows.ToArray());
                Console.WriteLine(detail);
                Require(duplicateUpdates == 0, "Adjacent rendering/timer updates violate the actual 20ms deadline: " + detail);
                Require(hz >= 20 && hz <= 35, "Shared frame deadline did not produce the expected 20–35Hz model-update cadence: " + detail);
                Require(gaps.Count > 20 && gaps[0] + CadenceGapToleranceMs >= CadenceMinimumGapMs,
                    "Adjacent rendering/timer callbacks bypassed the shared frame deadline: " + detail);
                finish(null);
            }
            catch (Exception error) { driver.Stop(); finish(error); }
        };
        driver.Start();
    }
    static void WriteResults(string prefix)
    {
        List<string> csv = new List<string> { "scenario,frame,age,heading,animateMs,hitAreaMs,totalMs,allocatedBytes" };
        foreach (Sample sample in Samples)
            csv.Add(sample.Scenario + "," + sample.Frame + "," + N(sample.Age) + "," + N(sample.Heading)
                + "," + N(sample.AnimateMs) + "," + N(sample.HitAreaMs) + "," + N(sample.TotalMs) + "," + sample.Allocated);
        File.WriteAllLines(Path.Combine(Output, prefix + ".csv"), csv.ToArray());
        if (Samples.Count != 0)
        {
            Summary.Add("ALL: " + Statistics(Samples));
            foreach (string scenario in new[] { "idle", "pet", "pet-to-idle", "feed", "play", "walk", "sleep", "wake", "awake-play" })
                Summary.Add(scenario + ": " + Statistics(Samples.FindAll(s => s.Scenario == scenario)));
        }
        File.WriteAllLines(Path.Combine(Output, prefix + ".txt"), Summary.ToArray());
    }
    static string Statistics(List<Sample> samples)
    {
        if (samples.Count == 0) return "no samples";
        List<double> total = new List<double>(), animated = new List<double>(), hit = new List<double>();
        long bytes = 0;
        foreach (Sample sample in samples) { total.Add(sample.TotalMs); animated.Add(sample.AnimateMs); hit.Add(sample.HitAreaMs); bytes += sample.Allocated; }
        total.Sort(); animated.Sort(); hit.Sort();
        return "count=" + samples.Count + ", total median/p95/max=" + N(Percentile(total, .5)) + "/" + N(Percentile(total, .95)) + "/" + N(total[total.Count - 1])
            + "ms, Animate median=" + N(Percentile(animated, .5)) + "ms, input median=" + N(Percentile(hit, .5))
            + "ms, mean core allocation=" + (AllocatedBytes == null ? "unavailable" : N(bytes / (double)samples.Count) + " bytes");
    }
    static void CheckBudget(List<Sample> samples, string name)
    {
        Require(samples.Count >= 20, name + ": insufficient cold pose frames.");
        List<double> total = samples.ConvertAll(s => s.TotalMs); total.Sort();
        Require(Percentile(total, .5) < 25 && Percentile(total, .95) < 50,
            name + ": complete cold-frame budget exceeded: " + Statistics(samples));
    }
    static double Percentile(List<double> sorted, double percentile)
    { return sorted[Math.Max(0, Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * percentile) - 1))]; }
    static string N(double value) { return value.ToString("F3", Invariant); }
    static Func<long> CreateAllocatedCounter()
    {
        MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Static | BindingFlags.Public);
        return method == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    static T Field<T>(object target, string name)
    { return (T)target.GetType().GetField(name, Private).GetValue(target); }
    static void Set(object target, string name, object value)
    { target.GetType().GetField(name, Private).SetValue(target, value); }
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
