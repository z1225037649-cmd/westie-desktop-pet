using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WestiePet;

static class MotionChecks
{
    static double time;
    static readonly FieldInfo yaw = typeof(WestieModel).GetField("headYaw", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo tilt = typeof(WestieModel).GetField("headTilt", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo nod = typeof(WestieModel).GetField("headNod", BindingFlags.NonPublic | BindingFlags.Instance);
    static double Angle(WestieModel model, FieldInfo field) { return ((AxisAngleRotation3D)field.GetValue(model)).Angle; }
    static void Step(WestieModel model, string state)
    { time += .042; model.Animate(time, state, time); }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    [STAThread]
    static int Main()
    {
        try
        {
            CheckSizeSlider();
            foreach (double scale in new[] { .4, .5, .6, 1.0, 1.4, 1.6 })
            foreach (double screenLeft in new[] { -1920.0, 0.0 })
            foreach (double x in new[] { 0.0, 700.0, 1400.0 })
            foreach (double y in new[] { 0.0, 450.0, 560.0 })
            foreach (Size text in new[] { new Size(180, 55), new Size(286, 85) })
            {
                Rect area = new Rect(screenLeft, 0, 1920, 1040);
                Rect pet = new Rect(screenLeft + x, y, 306 * scale, 260 * scale);
                Rect dialogue = PetWindow.DialogueBounds(pet, text, area);
                Require(!dialogue.IntersectsWith(pet), "Dialogue overlaps the dog at scale " + scale);
                Require(area.Contains(dialogue), "Dialogue leaves the monitor at scale " + scale);
                if (pet.Top >= text.Height + 14)
                    Require(dialogue.Bottom <= pet.Top - 8, "Dialogue must clear the dog's ears.");
            }
            Console.WriteLine("PASS: dialogue clears the dog at 40%-160%, with long text and monitor edges.");

            Edge(0, -1, 5, 995); Edge(1000, 1, 5, 995);
            Edge(-1920, -1, -1915, -325); Edge(-320, 1, -1915, -325);
            double direction = -1;
            Require(PetWindow.AdvanceWalk(5, ref direction, 1, 5, 4) == 5 && direction == -1, "Narrow monitor must not oscillate.");
            Console.WriteLine("PASS: single bounce at left/right edges, including negative monitor bounds and narrow screens.");

            WestieModel model = new WestieModel();
            for (int i = 0; i < 55; i++) Step(model, "idle");
            double low = 100, high = -100, rollLow = 100, rollHigh = -100;
            for (int i = 0; i < 125; i++)
            {
                Step(model, "idle"); double angle = Angle(model, yaw), roll = Angle(model, tilt);
                low = Math.Min(low, angle); high = Math.Max(high, angle);
                rollLow = Math.Min(rollLow, roll); rollHigh = Math.Max(rollHigh, roll);
            }
            Require(high - low < .01 && rollHigh - rollLow < .01, "Idle eye glances must not shake the skull.");
            Console.WriteLine("PASS: idle yaw/roll range " + (high - low).ToString("F4") + "/" + (rollHigh - rollLow).ToString("F4") + " degrees across two automatic eye glances.");
            double stableYaw = Angle(model, yaw);
            for (int i = 0; i < 30; i++)
            {
                model.LookAt(i % 2 == 0 ? .08 : -.08, i % 2 == 0 ? .05 : -.05, 1); Step(model, "idle");
                Require(Math.Abs(Angle(model, yaw) - stableYaw) < .01, "Cursor dead-zone jitter moved the skull.");
            }
            Console.WriteLine("PASS: small cursor jitter remains inside the head tracking dead zone.");
            double maximum = 0;
            for (int i = 0; i < 40; i++)
            {
                double before = Angle(model, yaw), beforeRoll = Angle(model, tilt), beforeNod = Angle(model, nod);
                model.LookAt(i % 2 == 0 ? 1 : -1, i % 2 == 0 ? 1 : -1, 1); Step(model, "idle");
                double jump = Math.Abs(Angle(model, yaw) - before); maximum = Math.Max(maximum, jump);
                Require(jump <= 12 * .042 + .00001 && Math.Abs(Angle(model, tilt) - beforeRoll) <= 9 * .042 + .00001
                    && Math.Abs(Angle(model, nod) - beforeNod) <= 14 * .042 + .00001, "Head turn exceeded angular speed limits.");
            }
            Console.WriteLine("PASS: abrupt cursor reversals are filtered; maximum yaw step " + maximum.ToString("F4") + " degrees.");
            model.Heading.Angle = 68;
            for (int i = 0; i < 75; i++) Step(model, "walk");
            double walkYaw = Angle(model, yaw);
            for (int i = 0; i < 30; i++)
            {
                model.WalkingPaused = i % 2 == 0; Step(model, "walk");
                Require(Math.Abs(Angle(model, yaw) - walkYaw) < .08, "Hover pause changed the walking head target.");
            }
            Console.WriteLine("PASS: repeated hover pauses preserve walk orientation.");
            model.WalkingPaused = false;
            for (int i = 0; i < 90; i++) Step(model, "play");
            FieldInfo liftField = typeof(WestieModel).GetField("lift", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo legField = typeof(WestieModel).GetField("legs", BindingFlags.NonPublic | BindingFlags.Instance);
            double floorLow = 100, floorHigh = -100;
            for (int i = 0; i < 100; i++)
            {
                Step(model, "play");
                double height = ((TranslateTransform3D)liftField.GetValue(model)).OffsetY;
                floorLow = Math.Min(floorLow, height); floorHigh = Math.Max(floorHigh, height);
                AxisAngleRotation3D[] feet = (AxisAngleRotation3D[])legField.GetValue(model);
                Require(Math.Abs(feet[2].Angle) < .05 && Math.Abs(feet[3].Angle) < .05, "Playing lifted the rear legs.");
                Require(Math.Abs(feet[0].Angle) <= 10.01 && Math.Abs(feet[1].Angle) <= 10.01, "Play paw tap exceeded a small gesture.");
            }
            Require(floorHigh - floorLow < .006, "Playing bounced the whole body off the floor.");
            Console.WriteLine("PASS: playing keeps rear legs planted; body height range " + (floorHigh - floorLow).ToString("F4") + ".");
            CheckInteractions(model);
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: " + error); return 1; }
    }

    static T Field<T>(WestieModel model, string name)
    { return (T)typeof(WestieModel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(model); }

    static void CheckInteractions(WestieModel model)
    {
        model.LookAt(1, 1, 1);
        for (int i = 0; i < 80; i++) Step(model, "idle");
        double low = 100, high = -100, narrowestEye = 1;
        for (int i = 0; i < 86; i++)
        {
            time += .042; model.Animate(time, "pet", i * .042);
            if (i < 18 || i > 68) continue;
            low = Math.Min(low, Angle(model, tilt)); high = Math.Max(high, Angle(model, tilt));
            narrowestEye = Math.Min(narrowestEye, Field<ScaleTransform3D[]>(model, "eyes")[0].ScaleY);
        }
        Require(high - low > 6, "Petting must visibly sway the head, even with cursor tracking enabled.");
        Require(narrowestEye < .3, "Petting must squint contentedly.");
        Console.WriteLine("PASS: petting sways the head " + (high - low).ToString("F1") + " degrees and squints.");

        double jawLow = 10, jawHigh = 0, lowestNod = 0;
        for (int i = 0; i < 108; i++)
        {
            time += .042; model.Animate(time, "feed", i * .042);
            double treat = Field<ScaleTransform3D>(model, "biscuitSize").ScaleX;
            if (i == 15) Require(treat > .99, "Feeding must show a full treat approaching the muzzle.");
            if (i == 55) Require(treat > .45 && treat < .55, "The treat must lose pieces as it is bitten.");
            if (i > 85) Require(treat == 0, "The treat must disappear after being eaten.");
            if (i > 30 && i < 85)
            {
                double jaw = Field<double>(model, "jawOpen");
                jawLow = Math.Min(jawLow, jaw); jawHigh = Math.Max(jawHigh, jaw);
                lowestNod = Math.Max(lowestNod, Angle(model, nod));
            }
        }
        Require(lowestNod > 20 && jawHigh - jawLow > .6, "Feeding must lower the head and visibly chew.");
        Console.WriteLine("PASS: feeding lowers the head, chews, and consumes the treat in stages.");

        time += .042; model.Animate(time, "feed", .5);
        Require(Field<ScaleTransform3D>(model, "biscuitSize").ScaleX > .99, "Repeated feeding must restart the treat.");
        foreach (string state in new[] { "pet", "play", "sleep", "idle" })
        {
            time += .042; model.Animate(time, state, 0);
            Require(Field<ScaleTransform3D>(model, "biscuitSize").ScaleX == 0, "Interrupted feeding left a treat in " + state);
            foreach (ScaleTransform3D crumb in Field<ScaleTransform3D[]>(model, "crumbSizes"))
                Require(crumb.ScaleX == 0, "Interrupted feeding left crumbs in " + state);
        }
        for (int i = 0; i < 100; i++) Step(model, "idle");
        Require(Math.Abs(Field<TranslateTransform3D>(model, "headRest").OffsetY) < .001, "Interaction head pose did not return to idle.");
        Console.WriteLine("PASS: repeated feeding restarts; interruptions clear food and restore the resting pose.");
    }

    static void CheckSizeSlider()
    {
        Application application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window window = new Window { Width = 420, Height = 150, ShowInTaskbar = false, Title = "Slider regression checks" };
        try
        {
            window.Show(); window.Activate();
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            Point pointer = PresentationSource.FromVisual(window).CompositionTarget.TransformFromDevice.Transform(new Point(cursor.X, cursor.Y));
            window.Left = pointer.X - 210; window.Top = pointer.Y - 75;
            foreach (double scale in new[] { 1.0, 1.5 })
            {
                SizeSlider slider = new SizeSlider { Minimum = PetSettings.MinimumSize, Maximum = PetSettings.MaximumSize,
                    Value = 1, SmallChange = .01, LargeChange = .1, Width = 230,
                    VerticalAlignment = VerticalAlignment.Center, LayoutTransform = new ScaleTransform(scale, scale) };
                window.Content = slider; window.UpdateLayout();
                DispatcherFrame frame = new DispatcherFrame();
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
                Dispatcher.PushFrame(frame);
                Track track = (Track)slider.Template.FindName("PART_Track", slider);
                double half = track.Thumb.ActualWidth / 2, travel = track.ActualWidth - half * 2;
                Point grab = track.Thumb.TranslatePoint(new Point(half + 3, track.Thumb.ActualHeight / 2), slider);
                bool began = slider.BeginPointerDrag(grab);
                Require(began && slider.IsMouseCaptured, "Slider did not capture a thumb press: began=" + began
                    + ", visible=" + slider.IsVisible + ", enabled=" + slider.IsEnabled + ", active=" + window.IsActive
                    + ", captured=" + Mouse.Captured + ", thumb=" + track.Thumb.ActualWidth + ", travel=" + travel);
                Require(Math.Abs(slider.Value - 1) < .0001, "Grabbing the edge of the thumb shifted its value.");
                slider.ContinuePointerDrag(new Point(grab.X + 4, grab.Y));
                Require(Math.Abs(slider.Value - (1 + 4 / travel * 1.2)) < .0001, "Small thumb drag jumped or did not move.");
                window.UpdateLayout();
                slider.ContinuePointerDrag(new Point(grab.X - 4, grab.Y));
                Require(Math.Abs(slider.Value - (1 - 4 / travel * 1.2)) < .0001, "Reversing a drag accumulated an offset.");
                slider.EndPointerDrag();
                Require(!slider.IsMouseCaptured, "Slider retained capture after release.");
                window.UpdateLayout();
                Point quarter = track.TranslatePoint(new Point(half + travel * .25, track.ActualHeight / 2), slider);
                Require(slider.BeginPointerDrag(quarter), "Slider did not capture a track press.");
                Require(Math.Abs(slider.Value - .7) < .0001, "Track press did not locate the pointer.");
                slider.ContinuePointerDrag(track.TranslatePoint(new Point(half + travel * .65, -100), slider));
                Require(Math.Abs(slider.Value - 1.18) < .0001, "Track press could not continue dragging outside the row.");
                slider.ContinuePointerDrag(track.TranslatePoint(new Point(-50, 0), slider));
                Require(slider.Value == PetSettings.MinimumSize, "Dragging past the left end did not clamp.");
                slider.ContinuePointerDrag(track.TranslatePoint(new Point(track.ActualWidth + 50, 0), slider));
                Require(slider.Value == PetSettings.MaximumSize, "Dragging past the right end did not clamp.");
                slider.ContinuePointerDrag(quarter);
                Require(Math.Abs(slider.Value - .7) < .0001, "Slider did not recover after dragging beyond an end.");
                Mouse.Capture(null);
                slider.ContinuePointerDrag(grab);
                Require(Math.Abs(slider.Value - .7) < .0001, "Slider changed after losing capture.");
                slider.Value = 1;
                System.Windows.Controls.Slider.IncreaseSmall.Execute(null, slider);
                Require(Math.Abs(slider.Value - 1.01) < .0001, "Keyboard fine adjustment stopped working.");
            }
            Console.WriteLine("PASS: size slider captures thumb/track drags, follows 4px reversals, clamps both ends, releases capture, and preserves keyboard input at 100%/150% layout scale.");
        }
        finally { window.Close(); }
    }

    static void Edge(double left, double direction, double minimum, double maximum)
    {
        int flips = 0;
        for (int i = 0; i < 120; i++)
        {
            double before = direction; left = PetWindow.AdvanceWalk(left, ref direction, .12, minimum, maximum);
            if (before != direction) flips++;
            Require(left >= minimum && left <= maximum, "Walk escaped padded bounds.");
        }
        Require(flips == 1, "An edge must produce exactly one direction reversal, got " + flips);
    }
}
