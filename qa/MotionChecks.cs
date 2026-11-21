using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Media3D;
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

            Require(PetWindow.BoundsFillMonitor(-1920, 0, 0, 1080, -1920, 0, 0, 1080), "Exact full-screen bounds should match.");
            Require(PetWindow.BoundsFillMonitor(-1919, 1, -1, 1079, -1920, 0, 0, 1080), "A two-pixel border tolerance should match.");
            Require(!PetWindow.BoundsFillMonitor(0, 0, 1920, 1040, 0, 0, 1920, 1080), "A maximized window must not count as full screen.");
            Require(!PetWindow.BoundsFillMonitor(0, 0, 0, 1080, 0, 0, 1920, 1080), "An empty window must not count as full screen.");
            Console.WriteLine("PASS: full-screen detection distinguishes borderless apps from maximized and empty windows.");

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
            CheckPlayContact(model);
            CheckPlaySurfaceContact(model);
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

    static T ToyField<T>(TennisToy toy, string name)
    { return (T)typeof(TennisToy).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(toy); }

    static void CheckPlayContact(WestieModel model)
    {
        TennisToy toy = Field<TennisToy>(model, "playToy");
        TranslateTransform3D position = ToyField<TranslateTransform3D>(toy, "position");
        AxisAngleRotation3D spin = ToyField<AxisAngleRotation3D>(toy, "spin");
        ScaleTransform3D visibility = (ScaleTransform3D)toy.Model.Transform;
        AxisAngleRotation3D[] legs = Field<AxisAngleRotation3D[]>(model, "legs");
        for (int i = 0; i < 60; i++) Step(model, "idle");
        model.BeginAction("play");
        time += .02; model.Animate(time, "play", 0);
        Point3D start = new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ);
        double previousZ = start.Z, firstTravel = 0, lateTravel = 0;
        int contactFrame = (int)Math.Round(WestieModel.PlayContactTime / .02);
        for (int i = 1; i <= 240; i++)
        {
            double stateTime = i * .02;
            time += .02; model.Animate(time, "play", stateTime);
            Require(visibility.ScaleX == 1, "Play ball unexpectedly became hidden.");
            double travel = position.OffsetZ - previousZ;
            if (i <= contactFrame)
            {
                Require(Math.Abs(position.OffsetX - start.X) < .000001
                    && Math.Abs(position.OffsetY - start.Y) < .000001
                    && Math.Abs(position.OffsetZ - start.Z) < .000001
                    && Math.Abs(spin.Angle) < .000001, "The visible ball moved or spun before paw contact.");
            }
            else
            {
                Require(travel >= -.000001, "The visible ball rolled backwards during a single push.");
                if (stateTime < WestieModel.PlayContactTime + .25) firstTravel = Math.Max(firstTravel, travel);
                if (stateTime > WestieModel.PlayContactTime + 2.7 && stateTime < WestieModel.PlayContactTime + 3)
                    lateTravel = Math.Max(lateTravel, travel);
            }
            if (i == contactFrame)
                Require(legs[1].Angle < -8.9, "The actual front paw did not reach its push pose when the ball was released.");
            if (stateTime >= 1.3)
                Require(Math.Abs(legs[1].Angle) < .001, "The push paw did not return to the floor.");
            previousZ = position.OffsetZ;
        }
        Require(position.OffsetZ - start.Z > .5 && firstTravel > lateTravel * 3,
            "The visible ball must travel away from the paw and slow before stopping.");
        Point3D stopped = new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ);
        double stoppedSpin = spin.Angle;
        foreach (double stateTime in new[] { 5.0, 7.0, 12.0 })
        {
            time += .02; model.Animate(time, "play", stateTime);
            Require((new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ) - stopped).Length < .000001
                && Math.Abs(spin.Angle - stoppedSpin) < .000001, "The settled visible ball drifted or restarted.");
        }
        foreach (string state in new[] { "idle", "pet", "feed", "sleep" })
        {
            model.BeginAction(state);
            time += .02; model.Animate(time, state, 0);
            Require(visibility.ScaleX == 0, "Interrupted play left the ball visible in " + state);
            model.BeginAction("play");
            time += .02; model.Animate(time, "play", 0);
            Require((new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ) - start).Length < .000001
                && Math.Abs(spin.Angle) < .000001, "Repeated play kept an old ball position or rotation.");
            Require(visibility.ScaleX == (state == "sleep" ? 0 : 1), "Sleep recovery must prepare before showing the ball.");
        }
        for (int i = 0; i < 90; i++) Step(model, "idle");
        model.BeginAction("play");
        time += .02; model.Animate(time, "play", 2);
        Require(position.OffsetZ > start.Z + .1, "Mid-gesture replay setup did not move the ball.");
        model.BeginAction("play");
        time += .02; model.Animate(time, "play", 0);
        Require(Math.Abs(position.OffsetZ - start.Z) < .000001 && Math.Abs(spin.Angle) < .000001,
            "Restarting play during the same state did not reset ball travel and spin.");
        Console.WriteLine("PASS: visible ball waits for the push pose, rolls and slows to rest; replay and interruptions reset it.");
    }

    sealed class ContactSurface
    {
        internal MeshGeometry3D Mesh;
        internal Point3D[] Points;
        internal int[] Indices;
    }

    static void CheckPlaySurfaceContact(WestieModel model)
    {
        SculptedCoat sculpt = Field<SculptedCoat>(model, "sculpt");
        TennisToy toy = Field<TennisToy>(model, "playToy");
        TranslateTransform3D position = ToyField<TranslateTransform3D>(toy, "position");
        Model3DGroup ball = (Model3DGroup)toy.Model.Children[1];
        double radius = 0;
        foreach (GeometryModel3D part in ball.Children)
        foreach (Point3D p in ((MeshGeometry3D)part.Geometry).Positions)
            radius = Math.Max(radius, ((Vector3D)p).Length);
        Require(radius > 0, "The actual ball surface has no radius.");
        List<ContactSurface> surfaces = new List<ContactSurface>();
        foreach (GeometryModel3D part in sculpt.Model.Children)
        {
            MeshGeometry3D mesh = (MeshGeometry3D)part.Geometry;
            ContactSurface surface = new ContactSurface { Mesh = mesh,
                Points = new Point3D[mesh.Positions.Count], Indices = new int[mesh.TriangleIndices.Count] };
            mesh.TriangleIndices.CopyTo(surface.Indices, 0); surfaces.Add(surface);
        }
        foreach (bool fromSleep in new[] { false, true })
        foreach (double interval in new[] { .020, .056 })
        foreach (double phase in interval < .03 ? new[] { 0.0 } : new[] { 0.0, .027 })
        {
            for (int i = 0; i < 90; i++) Step(model, fromSleep ? "sleep" : "idle");
            model.BeginAction("play");
            List<double> samples = new List<double> { 0 };
            double duration = fromSleep ? 3.4 : 2.0;
            for (double sample = interval + phase; sample <= duration; sample += interval) samples.Add(sample);
            samples.Add(duration);
            double previous = 0, minClearance = double.PositiveInfinity, previousClearance = double.PositiveInfinity;
            double releasedAt = double.NaN;
            Point3D start = new Point3D();
            bool appeared = false;
            ScaleTransform3D visibility = (ScaleTransform3D)toy.Model.Transform;
            foreach (double sample in samples)
            {
                time += Math.Max(.002, sample - previous); previous = sample;
                model.Animate(time, "play", sample);
                Point3D center = new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ);
                if (sample == 0) start = center;
                if (visibility.ScaleX < .01) continue;
                double distance = NearestDogSurface(surfaces, center), clearance = distance - radius;
                minClearance = Math.Min(minClearance, clearance);
                string detail = (fromSleep ? "sleep-to-play" : "idle-to-play") + ", interval " + interval.ToString("F3") + ", offset " + phase.ToString("F3")
                    + ", state time " + sample.ToString("F3") + ", clearance " + clearance.ToString("F4");
                Require(clearance >= -.012, "Paw or coat deeply penetrated the actual ball: " + detail);
                if (!appeared)
                {
                    Require(clearance >= .018, "Ball appears overlapped with or touching the paw: " + detail);
                    appeared = true;
                }
                if (center.Z - start.Z > .00005 && double.IsNaN(releasedAt))
                {
                    releasedAt = sample;
                    Require(Math.Min(clearance, previousClearance) <= .025,
                        "Ball was released without actual paw surface contact between samples: " + detail);
                    Require(Field<double>(model, "sleepPose") < .02, "The ball was released before the dog recovered from sleep: " + detail);
                }
                if (!double.IsNaN(releasedAt) && sample >= releasedAt + .8)
                    Require(clearance > .02, "Ball did not separate from the actual paw after release: " + detail);
                previousClearance = clearance;
            }
            Require(appeared && !double.IsNaN(releasedAt), "The dog never completed preparation and pushed the ball.");
            Console.WriteLine("PASS: actual paw/coat surface clears and contacts the ball (" + (fromSleep ? "sleep" : "idle")
                + ", interval " + interval.ToString("F3") + ", offset " + phase.ToString("F3") + ", minimum clearance " + minClearance.ToString("F4") + ").");
        }
    }

    static double NearestDogSurface(List<ContactSurface> surfaces, Point3D center)
    {
        double best = double.PositiveInfinity;
        foreach (ContactSurface surface in surfaces)
        {
            surface.Mesh.Positions.CopyTo(surface.Points, 0);
            Point3D[] points = surface.Points; int[] indices = surface.Indices;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Point3D a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];
                double dx = Math.Max(0, Math.Max(Math.Min(a.X, Math.Min(b.X, c.X)) - center.X, center.X - Math.Max(a.X, Math.Max(b.X, c.X))));
                double dy = Math.Max(0, Math.Max(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - center.Y, center.Y - Math.Max(a.Y, Math.Max(b.Y, c.Y))));
                double dz = Math.Max(0, Math.Max(Math.Min(a.Z, Math.Min(b.Z, c.Z)) - center.Z, center.Z - Math.Max(a.Z, Math.Max(b.Z, c.Z))));
                if (dx * dx + dy * dy + dz * dz >= best) continue;
                best = Math.Min(best, TriangleDistanceSquared(center, a, b, c));
            }
        }
        return Math.Sqrt(best);
    }

    static double TriangleDistanceSquared(Point3D p, Point3D a, Point3D b, Point3D c)
    {
        Vector3D ab = b - a, ac = c - a, ap = p - a;
        double d1 = Vector3D.DotProduct(ab, ap), d2 = Vector3D.DotProduct(ac, ap);
        if (d1 <= 0 && d2 <= 0) return ap.LengthSquared;
        Vector3D bp = p - b;
        double d3 = Vector3D.DotProduct(ab, bp), d4 = Vector3D.DotProduct(ac, bp);
        if (d3 >= 0 && d4 <= d3) return bp.LengthSquared;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).LengthSquared;
        Vector3D cp = p - c;
        double d5 = Vector3D.DotProduct(ab, cp), d6 = Vector3D.DotProduct(ac, cp);
        if (d6 >= 0 && d5 <= d6) return cp.LengthSquared;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).LengthSquared;
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).LengthSquared;
        double sum = va + vb + vc;
        if (Math.Abs(sum) < 1e-25) return Math.Min(ap.LengthSquared, Math.Min(bp.LengthSquared, cp.LengthSquared));
        return (p - (a + ab * (vb / sum) + ac * (vc / sum))).LengthSquared;
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
