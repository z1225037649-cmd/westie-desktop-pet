using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WestiePet;

// These checks exercise the generated model that the production selector shows.
// Old sculpted-model reflection checks are not substitutes for this suite.
static class GeneratedChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated");
    static readonly List<string> Results = new List<string>();
    static readonly double[] SleepSamples = { .125, .25, .375, .50, .625, .75, 1.0, 1.5, 2.4 };
    static double time = 20;

    [STAThread]
    static int Main(string[] args)
    {
        Directory.CreateDirectory(Output);
        try
        {
            WestieModel model = new WestieModel("westie");
            GeneratedWestie generated = Field<GeneratedWestie>(model, "generated");
            Require(generated != null && Contains(model.Scene, generated.Model), "The production Westie scene does not contain the generated model.");
            Require(!model.IsPreRendered, "The generated dog must be a volumetric 3D model.");
            if (args.Length > 0 && args[0] == "--sleep-neck-view")
            {
                RenderSleepNeckDiagnostic(model, generated);
                return 0;
            }
            if (args.Length > 0 && args[0] == "--sleep-view")
            {
                model.Heading.Angle = 0;
                Advance(model, "idle", 1.8, .042);
                Advance(model, "sleep", 2.4, .042);
                foreach (KeyValuePair<string, double> angle in new[] {
                    new KeyValuePair<string, double>("front", 0),
                    new KeyValuePair<string, double>("threequarter", 52),
                    new KeyValuePair<string, double>("side", 90) })
                {
                    model.Heading.Angle = angle.Value; Step(model, "sleep", 2.4, .002);
                    Save(Render(PetWindow.CreateViewport(model), 306, 260), "sleep-view-" + angle.Key + "-306");
                    Save(Render(PetWindow.CreateViewport(model), 512, 448), "sleep-view-" + angle.Key + "-512");
                }
                string note = "DIAGNOSTIC: actual generated model after sleep 2.4s, front/52/90-degree full-body views at 306x260 and 512x448.\r\n"
                    + "No regression suite or appearance approval was performed in this diagnostic mode.\r\n"
                    + "Bounds=" + generated.Bounds + ", HeadNod=" + generated.HeadNod + ", Mouth=" + generated.Mouth + "\r\n";
                File.WriteAllText(Path.Combine(Output, "sleep-view-diagnostic.txt"), note);
                Console.WriteLine(note + "DIAGNOSTIC output: " + Output);
                return 0;
            }
            if (args.Length > 0 && args[0] == "--sleep-check")
            {
                CheckSleepFeet(model, generated);
                return 0;
            }
            CheckResource("WestiePet.GeneratedMesh", "westie-generated.mesh");
            CheckResource("WestiePet.GeneratedTexture", "westie-generated.png");
            Require(generated.VertexCount > 10000 && generated.TriangleCount > 10000 && model.TriangleCount <= 200000,
                "Generated geometry is missing or exceeds the real-time 200,000-triangle budget.");
            Pass("embedded generated mesh/texture, actual production scene, " + generated.VertexCount + " vertices / " + generated.TriangleCount + " dog triangles");
            if (args.Length > 0 && args[0] == "--inspect")
            {
                Advance(model, "sleep", 2.4, .042);
                model.Heading.Angle = 0; Step(model, "sleep", 2.4, .002);
                Console.WriteLine("SLEEP BOUNDS: " + generated.Bounds);
                Point3D[] points = generated.GetSkinnedPositions(0); Point3D lowest = points[0];
                foreach (Point3D p in points) if (p.Y < lowest.Y) lowest = p;
                Console.WriteLine("LOWEST: " + lowest);
                Save(Render(PetWindow.CreateViewport(model), 306, 260), "inspect-sleep");
                CheckEyes(model, generated);
                CheckMotion(model, generated);
                CheckSleepFeet(model, generated);
                CheckFood(model, generated);
                CheckPlay(model, generated);
                CheckPerformance(model);
                CheckInput();
                return 0;
            }
            CheckRenders(model, generated);
            CheckEyes(model, generated);
            CheckMotion(model, generated);
            CheckSleepFeet(model, generated);
            CheckFood(model, generated);
            CheckPlay(model, generated);
            CheckPerformance(model);
            CheckInput();
            File.WriteAllLines(Path.Combine(Output, "result.txt"), Results.ToArray());
            string previousError = Path.Combine(Output, "error.txt");
            if (File.Exists(previousError)) File.Delete(previousError);
            Console.WriteLine("PASS: generated production model checks: " + Output);
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), error.ToString());
            Console.Error.WriteLine("GENERATED_CHECKS_ERROR: " + error);
            return 1;
        }
    }

    static T Field<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Private);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        return (T)field.GetValue(target);
    }
    static void Set(object target, string name, object value)
    { target.GetType().GetField(name, Private).SetValue(target, value); }
    static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    static void Pass(string message) { Results.Add("PASS: " + message); Console.WriteLine("PASS: " + message); }
    static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    static bool Contains(Model3D scene, Model3D wanted)
    {
        if (ReferenceEquals(scene, wanted)) return true;
        Model3DGroup group = scene as Model3DGroup;
        if (group != null) foreach (Model3D child in group.Children) if (Contains(child, wanted)) return true;
        return false;
    }
    static void CheckResource(string resource, string sourceName)
    {
        string source = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)), "assets", sourceName);
        using (Stream embedded = typeof(WestieModel).Assembly.GetManifestResourceStream(resource))
        using (Stream original = File.OpenRead(source))
        using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
        {
            Require(embedded != null && embedded.Length > 1000, "Missing embedded resource " + resource);
            string embeddedHash = Convert.ToBase64String(sha.ComputeHash(embedded));
            Require(embeddedHash == Convert.ToBase64String(sha.ComputeHash(original)), "Embedded resource differs from build input: " + resource);
        }
    }
    static void Advance(WestieModel model, string state, double duration, double interval)
    {
        model.BeginAction(state);
        for (double age = 0; age < duration; age += interval) { time += interval; model.Animate(time, state, age); }
        time += interval; model.Animate(time, state, duration);
    }
    static void Step(WestieModel model, string state, double age, double interval)
    { time += interval; model.Animate(time, state, age); }

    static void CheckRenders(WestieModel model, GeneratedWestie generated)
    {
        Viewport3D view = PetWindow.CreateViewport(model);
        foreach (string state in new[] { "idle", "walk", "pet", "feed", "play", "sleep" })
        {
            Advance(model, "idle", 1.8, .042);
            Advance(model, state, state == "sleep" ? 2.4 : 1.1, .042);
            foreach (double angle in new[] { 0.0, -52.0, 52.0, -68.0, 68.0, 90.0 })
            {
                model.Heading.Angle = angle; Step(model, state, state == "sleep" ? 2.4 : 1.1, .002);
                CheckMesh(model.Scene, state);
                foreach (Size size in new[] { new Size(306, 260), new Size(512, 448) })
                {
                    string name = state + "-" + angle.ToString("0", CultureInfo.InvariantCulture) + "-" + size.Width;
                    RenderTargetBitmap bitmap = Render(view, (int)size.Width, (int)size.Height);
                    Save(bitmap, name);
                    RequireMargins(Pixels(bitmap), (int)size.Width, (int)size.Height, name);
                }
            }
        }
        Pass("six production states at front, both 52/68-degree headings and side, desktop/home resolutions, finite meshes and transparent full-body margins");
    }

    static void CheckEyes(WestieModel model, GeneratedWestie generated)
    {
        File.WriteAllText(Path.Combine(Output, "eyes.csv"), "view,state,eye,closure,openDark,closedDark,openGlints,closedGlints,changedPixels\r\n");
        foreach (double heading in new[] { 8.0, 40.0 })
        {
            model.Heading.Angle = heading;
            Advance(model, "idle", 2, .042);
            Set(generated, "nextBlink", double.PositiveInfinity);
            generated.SetEyeClosure(0);
            EyeSample(model, generated, "idle", heading, false);
            Set(generated, "blinkStarted", time);
            Set(generated, "blinkDuration", .24);
            Step(model, "idle", 2.09, .09);
            Require(generated.EyeClosure > .98, "Forced production blink did not reach closure.");
            EyeSample(model, generated, "blink", heading, true);
            Advance(model, "pet", 1.0, .042);
            Require(generated.EyeClosure > .8, "Petting did not squint.");
            EyeSample(model, generated, "pet", heading, true);
            Advance(model, "sleep", 2.4, .042);
            Require(generated.EyeClosure > .98, "Sleeping did not close the eyes.");
            EyeSample(model, generated, "sleep", heading, true);
            Advance(model, "idle", 2.4, .042);
            generated.SetEyeClosure(0);
            EyeSample(model, generated, "wake", heading, false);
        }
        Pass("actual textured-eye pixels at two views: idle, full blink, petting, sleep and wake; same-bone-pose open comparison; no tall exposed pupil/glint patch after closure");
    }
    static void EyeSample(WestieModel model, GeneratedWestie generated, string state, double heading, bool closed)
    {
        double closure = generated.EyeClosure;
        const int size = 600;
        Point3D[] centers = generated.EyeCenters;
        Point3D mid = new Point3D((centers[0].X + centers[1].X) / 2, (centers[0].Y + centers[1].Y) / 2,
            (centers[0].Z + centers[1].Z) / 2);
        Matrix3D headWorld = Field<Matrix3D[]>(generated, "bones")[1];
        headWorld.Append(Field<RotateTransform3D>(generated, "heading").Value);
        // The original 8/40-degree views with the resting -8-degree head yaw
        // observed the eye plane at 0/32 degrees. Preserve those relative views
        // while following the actual cumulative head/neck pitch, yaw and roll.
        double relativeYaw = (heading - 8) * Math.PI / 180;
        Vector3D eyeFacing = headWorld.Transform(new Vector3D(-Math.Sin(relativeYaw), 0, Math.Cos(relativeYaw)));
        Vector3D eyeUp = headWorld.Transform(new Vector3D(0, 1, 0));
        OrthographicCamera camera = new OrthographicCamera(mid + eyeFacing * 6, eyeFacing * -6,
            eyeUp, .9);
        Viewport3D view = PetWindow.CreateViewport(model); view.Camera = camera;
        try
        {
            generated.SetEyeClosure(0);
            RenderTargetBitmap openedBitmap = Render(view, size, size); byte[] opened = Pixels(openedBitmap);
            Save(openedBitmap, "eye-" + heading + "-" + state + "-open");
            generated.SetEyeClosure(closure);
            RenderTargetBitmap normal = Render(view, size, size); byte[] pixels = Pixels(normal);
            Save(normal, "eye-" + heading + "-" + state);
            for (int eye = 0; eye < 2; eye++)
            {
                Point c = Project(centers[eye], camera, size, size);
                // The fixed open-eye region excludes the nose and brows. Bounds
                // come from the actual anatomical anchor, not the old screenshot.
                int rx = (int)Math.Ceiling(.063 / camera.Width * size), ry = (int)Math.Ceiling(.047 / camera.Width * size);
                Int32Rect roi = new Int32Rect((int)c.X - rx, (int)c.Y - ry, rx * 2 + 1, ry * 2 + 1);
                Require(roi.X >= 0 && roi.Y >= 0 && roi.X + roi.Width < size && roi.Y + roi.Height < size, "Eye ROI escaped closeup.");
                int openDark = 0, normalDark = 0, changed = 0;
                for (int y = roi.Y; y < roi.Y + roi.Height; y++)
                for (int x = roi.X; x < roi.X + roi.Width; x++)
                {
                    int i = (y * size + x) * 4;
                    if (Dark(opened, i)) openDark++;
                    if (Dark(pixels, i)) normalDark++;
                    if (Math.Abs(opened[i] - pixels[i]) + Math.Abs(opened[i + 1] - pixels[i + 1]) + Math.Abs(opened[i + 2] - pixels[i + 2]) > 50) changed++;
                }
                int openGlints = Glints(opened, size, roi), normalGlints = Glints(pixels, size, roi);
                File.AppendAllText(Path.Combine(Output, "eyes.csv"), heading + "," + state + "," + eye + "," + closure + "," + openDark + "," + normalDark + "," + openGlints + "," + normalGlints + "," + changed + "\r\n");
                Require(openDark >= 20, state + ": actual open eye " + eye + " is obscured or missing in anchor ROI (" + openDark + ").");
                Require(openGlints >= 1, state + ": eye " + eye + " has no readable highlight inside the actual pupil.");
                if (closed)
                {
                    Require(normalDark <= Math.Max(8, openDark * .25), state + ": eye " + eye + " has exposed textured pupil after closure (" + normalDark + "/" + openDark + ").");
                    Require(changed >= openDark * .50, state + ": mesh eye closure did not change the visible pupil.");
                    Require(normalGlints <= Math.Max(2, openGlints * .25), state + ": eye glint leaked after closure.");
                }
                else Require(normalDark >= openDark * .90, state + ": open eye did not restore visible pupil.");
            }
        }
        finally { generated.SetEyeClosure(closure); }
    }
    static bool Dark(byte[] pixels, int i)
    { return pixels[i + 3] >= 224 && pixels[i + 2] * .2126 + pixels[i + 1] * .7152 + pixels[i] * .0722 <= 70; }
    static int Glints(byte[] pixels, int width, Int32Rect roi)
    {
        int count = 0;
        for (int y = roi.Y + 5; y < roi.Y + roi.Height - 5; y++)
        for (int x = roi.X + 5; x < roi.X + roi.Width - 5; x++)
        {
            int i = (y * width + x) * 4;
            if (pixels[i + 3] < 224 || pixels[i] < 185 || pixels[i + 1] < 185 || pixels[i + 2] < 185) continue;
            // A glint sits inside a dark eye; nearby white fur does not.
            bool left = false, right = false, up = false, down = false;
            for (int n = 1; n <= 5; n++)
            { left |= Dark(pixels, i - n * 4); right |= Dark(pixels, i + n * 4); up |= Dark(pixels, i - n * width * 4); down |= Dark(pixels, i + n * width * 4); }
            if (left && right && up && down) count++;
        }
        return count;
    }

    static void CheckMotion(WestieModel model, GeneratedWestie generated)
    {
        model.Heading.Angle = 0; model.WalkingPaused = false;
        Advance(model, "idle", 2.4, .042);
        Point3D[] rest = generated.GetSkinnedPositions(0);
        Rect3D standing = generated.Bounds;
        Advance(model, "walk", .23, .020);
        Point3D[] a = generated.GetSkinnedPositions(0);
        for (double age = .25; age <= .43; age += .02) Step(model, "walk", age, .02);
        Point3D[] b = generated.GetSkinnedPositions(0);
        Vector3D[] legTravel = new Vector3D[4]; int[] count = new int[4];
        // Select the four actual foot regions in the unposed surface, then
        // compare the identical vertices after a fraction of a gait cycle.
        for (int i = 0; i < rest.Length; i++)
        {
            Point3D p = rest[i];
            if (p.Y > .35 || Math.Abs(p.X) < .08) continue;
            int group = (p.Z > .05 ? 0 : 2) + (p.X > 0 ? 0 : 1);
            legTravel[group] += b[i] - a[i]; count[group]++;
        }
        for (int i = 0; i < 4; i++)
        {
            Require(count[i] > 10, "The actual four-foot surface sample is missing foot " + i + ".");
            legTravel[i] /= count[i];
            Require(legTravel[i].Length > .012, "Actual foot vertices did not walk: " + i + ", " + legTravel[i]);
        }
        Require(Vector3D.DotProduct(legTravel[0], legTravel[1]) < 0 && Vector3D.DotProduct(legTravel[2], legTravel[3]) < 0,
            "The actual left and right foot vertices do not alternate.");
        model.WalkingPaused = true;
        Point3D[] paused = generated.GetSkinnedPositions(0);
        for (int i = 0; i < 10; i++) Step(model, "walk", .45 + i * .042, .042);
        Point3D[] afterPause = generated.GetSkinnedPositions(0);
        double maximum = 0;
        for (int i = 0; i < paused.Length; i++) if (rest[i].Y < .30) maximum = Math.Max(maximum, (paused[i] - afterPause[i]).Length);
        Require(maximum < .006, "Hover pause did not hold actual foot pose (" + maximum + ").");
        model.WalkingPaused = false;
        Advance(model, "sleep", 2.4, .042);
        Require(standing.SizeY - generated.Bounds.SizeY > .20, "Actual sleeping mesh did not lie noticeably lower.");
        Advance(model, "idle", 2.4, .042);
        Require(Math.Abs(generated.Bounds.SizeY - standing.SizeY) < .04, "Actual mesh failed to recover standing height after waking.");
        Pass("four actual foot regions move in alternating pairs, hover pause holds feet, sleep visibly lowers the mesh and wake restores its height");
    }

    static void CheckSleepFeet(WestieModel model, GeneratedWestie generated)
    {
        model.Heading.Angle = 0; model.WalkingPaused = false;
        Advance(model, "idle", 2.4, .042);
        Point3D[] rest = generated.GetSkinnedPositions(0);
        List<int> feet = new List<int>(), frontFeet = new List<int>();
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i].Y >= .15 || Math.Abs(rest[i].X) <= .10) continue;
            feet.Add(i);
            if (rest[i].Z > .05) frontFeet.Add(i);
        }
        Require(feet.Count > 40 && frontFeet.Count > 20, "Sleeping-foot regression could not locate the actual standing paw vertices.");
        MeshGeometry3D mesh = (MeshGeometry3D)((GeometryModel3D)generated.Model.Children[0]).Geometry;
        int[] triangles = new int[mesh.TriangleIndices.Count]; mesh.TriangleIndices.CopyTo(triangles, 0);
        List<int> headTriangles = new List<int>(), padTriangles = new List<int>();
        List<int> frontFootTriangles = new List<int>(), chinTriangles = new List<int>();
        HashSet<int> frontFootVertices = new HashSet<int>(frontFeet);
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Point3D a = rest[triangles[t]], b = rest[triangles[t + 1]], c = rest[triangles[t + 2]];
            if (Math.Min(a.Y, Math.Min(b.Y, c.Y)) > 1.05 && Math.Max(a.Z, Math.Max(b.Z, c.Z)) > .35)
            {
                headTriangles.Add(t);
                if ((a.Y + b.Y + c.Y) / 3 < 1.36 && Math.Min(a.Z, Math.Min(b.Z, c.Z)) > .65) chinTriangles.Add(t);
            }
            if (frontFootVertices.Contains(triangles[t]) || frontFootVertices.Contains(triangles[t + 1]) || frontFootVertices.Contains(triangles[t + 2]))
                frontFootTriangles.Add(t);
            if (Math.Max(a.Y, Math.Max(b.Y, c.Y)) < .055
                && Math.Min(Math.Abs(a.X), Math.Min(Math.Abs(b.X), Math.Abs(c.X))) > .10)
            {
                Vector3D normal = Vector3D.CrossProduct(b - a, c - a);
                if (normal.LengthSquared > 1e-18 && Math.Abs(normal.Y) / normal.Length > .65) padTriangles.Add(t);
            }
        }
        Require(headTriangles.Count > 20 && padTriangles.Count > 10, "The actual muzzle and underside paw surface samples are missing.");
        Require(frontFootTriangles.Count > 20 && chinTriangles.Count > 20, "Actual front-paw and lower muzzle/chin triangle samples are missing.");
        List<SleepEdge> ankleEdges = SleepAnkleEdges(rest, triangles);
        Require(ankleEdges.Count > 20, "The standing ankle skin has no measurable triangle edges.");
        SleepCollisionAudit collisionAudit = CaptureSleepCollisionAudit(generated, rest, triangles);
        File.WriteAllText(Path.Combine(Output, "sleep-transition.csv"),
            "state,age,footVertices,lowestFootY,frontFootHeadGap,headPawIntersections,ankleEdges,maxEdgeRatio,p95EdgeRatio,worstVertexA,worstVertexB\r\n");
        CheckSleepTransition(model, generated, "sleep", rest, feet, frontFootTriangles, triangles, headTriangles, ankleEdges, collisionAudit);
        Point3D[] asleep = generated.GetSkinnedPositions(0);
        int onFloor = 0; double floor = double.PositiveInfinity, maximum = double.NegativeInfinity;
        List<string> measurements = new List<string> { "vertex,restX,restY,restZ,sleepX,sleepY,sleepZ" };
        foreach (int i in feet)
        {
            Point3D p = asleep[i]; floor = Math.Min(floor, p.Y); maximum = Math.Max(maximum, p.Y);
            if (p.Y < .20) onFloor++;
            measurements.Add(i + "," + rest[i].X.ToString(CultureInfo.InvariantCulture) + "," + rest[i].Y.ToString(CultureInfo.InvariantCulture)
                + "," + rest[i].Z.ToString(CultureInfo.InvariantCulture) + "," + p.X.ToString(CultureInfo.InvariantCulture)
                + "," + p.Y.ToString(CultureInfo.InvariantCulture) + "," + p.Z.ToString(CultureInfo.InvariantCulture));
        }
        File.WriteAllLines(Path.Combine(Output, "sleep-foot-vertices.csv"), measurements.ToArray());
        Save(Render(PetWindow.CreateViewport(model), 306, 260), "sleep-feet-grounded");
        Require(floor >= -.015, "Sleeping paw vertices penetrate the floor: lowest Y=" + floor + ".");
        Require(onFloor >= feet.Count * .90, "Sleeping paws rise into the chest/muzzle: " + onFloor + "/" + feet.Count
            + " of the same standing foot vertices are below Y=.20, maximum=" + maximum + ".");
        int flatPads = 0;
        foreach (int t in padTriangles)
        {
            Vector3D original = Vector3D.CrossProduct(rest[triangles[t + 1]] - rest[triangles[t]], rest[triangles[t + 2]] - rest[triangles[t]]);
            Vector3D sleeping = Vector3D.CrossProduct(asleep[triangles[t + 1]] - asleep[triangles[t]], asleep[triangles[t + 2]] - asleep[triangles[t]]);
            if (sleeping.LengthSquared < 1e-18) continue;
            original.Normalize(); sleeping.Normalize();
            // Use triangle geometry, not renderer normals: the footpad itself
            // must retain its downward-facing stance rather than stand upright.
            if (Vector3D.DotProduct(original, sleeping) > .70 && Math.Abs(sleeping.Y) > .55) flatPads++;
        }
        Require(flatPads >= padTriangles.Count * .85, "Sleeping paw undersides turned toward the chest instead of remaining flat: "
            + flatPads + "/" + padTriangles.Count + " underside triangles retain their ground-facing orientation.");
        SleepSurfaceContact headContact = SleepingSurfaceContact(asleep, triangles, frontFootTriangles, headTriangles);
        SleepSurfaceContact chinContact = SleepingSurfaceContact(asleep, triangles, frontFootTriangles, chinTriangles);
        double clearance = headContact.Gap;
        File.WriteAllText(Path.Combine(Output, "sleep-head-contact.txt"), "actualHeadToFrontPawGap=" + headContact.Gap
            + ", headPawIntersections=" + headContact.Intersections + ", actualMuzzleChinToFrontPawGap=" + chinContact.Gap
            + ", muzzleChinPawIntersections=" + chinContact.Intersections + "\r\n");
        Require(headContact.Intersections == 0 && clearance > .005,
            "Actual sleeping head and front-paw surfaces intersect or lose their small safety gap: gap=" + clearance + ", intersections=" + headContact.Intersections + ".");
        // A relaxed raised head keeps the ribcage and shoulder volume intact.
        // The former .12 target forced the chest/forelegs into a flattened sheet.
        Require(chinContact.Gap < .30, "The resting head is detached from the supported forelegs: gap=" + chinContact.Gap + ".");
        Point3D sleepingMouth = generated.Mouth;
        CheckSleepTransition(model, generated, "idle", rest, feet, frontFootTriangles, triangles, headTriangles, ankleEdges, collisionAudit);
        Point3D[] awake = generated.GetSkinnedPositions(0); double returnError = 0;
        foreach (int i in feet) returnError = Math.Max(returnError, (awake[i] - rest[i]).Length);
        Require(returnError < .015, "The flattened sleep feet did not blend back to their actual standing vertices: " + returnError + ".");
        CheckSleepingLegSupport(generated, rest, asleep, awake, sleepingMouth, triangles, headTriangles);
        CheckSleepVolume(rest, asleep);
        File.WriteAllText(Path.Combine(Output, "sleep-feet.txt"), "footVertices=" + feet.Count + ", grounded=" + onFloor
            + ", lowestY=" + floor + ", highestY=" + maximum + ", flatUndersideTriangles=" + flatPads + "/" + padTriangles.Count
            + ", actualFrontPawToHeadTriangleGap=" + clearance + ", headPawIntersections=" + headContact.Intersections
            + ", actualMuzzleChinToFrontPawGap=" + chinContact.Gap + ", wakeReturnError=" + returnError + "\r\n");
        Pass("same actual foot vertices stay on the floor during sleep, underside triangles stay flat, front paws clear head/muzzle geometry, and waking restores standing feet");
        Pass("nine samples in each sleep/wake transition preserve paw-floor and actual muzzle clearances; standing ankle-skin edge stretch remains below 6x");
        Pass("actual head/neck nonadjacent triangle pairs introduce no intersections beyond the source bind baseline during sleep and wake");
        Pass("actual chest/foreleg skin has no new crossings deeper than .002 world units; raw pairs and measured depths remain in sleep-chest-crossings.csv");
    }

    static void CheckSleepingLegSupport(GeneratedWestie generated, Point3D[] rest, Point3D[] asleep, Point3D[] awake,
        Point3D sleepingMouth, int[] triangles, List<int> headTriangles)
    {
        // Identify the real skin vertices by their immutable exported weights.
        // A bone-angle scalar cannot prove the visible thigh/shin also folded.
        System.Collections.IList surfaces = Field<System.Collections.IList>(generated, "surfaces");
        Array bind = Field<Array>(surfaces[0], "Bind");
        Type vertexType = bind.GetType().GetElementType();
        FieldInfo[] boneFields = new FieldInfo[4], weightFields = new FieldInfo[4];
        for (int influence = 0; influence < 4; influence++)
        {
            boneFields[influence] = vertexType.GetField("B" + influence, Private);
            weightFields[influence] = vertexType.GetField("W" + influence, Private);
        }
        List<int>[] middle = new List<int>[4], paws = new List<int>[4];
        for (int leg = 0; leg < 4; leg++) { middle[leg] = new List<int>(); paws[leg] = new List<int>(); }
        List<int> abdomen = new List<int>();
        for (int i = 0; i < bind.Length; i++)
        {
            object vertex = bind.GetValue(i); int dominant = -1; double strongest = 0;
            for (int influence = 0; influence < 4; influence++)
            {
                double weight = (double)weightFields[influence].GetValue(vertex);
                if (weight <= strongest) continue;
                strongest = weight; dominant = (byte)boneFields[influence].GetValue(vertex);
            }
            if (strongest <= .55) continue;
            Point3D p = rest[i];
            if (dominant >= 2 && dominant <= 5)
            {
                if (p.Y > .30 && p.Y < .60) middle[dominant - 2].Add(i);
                if (p.Y < .15) paws[dominant - 2].Add(i);
            }
            if (dominant == 0 && p.Y > .45 && p.Y < .70 && Math.Abs(p.X) < .25 && p.Z > -.45 && p.Z < .10)
                abdomen.Add(i);
        }
        Require(abdomen.Count > 50, "The actual lower abdomen skin sample is missing.");
        Point3D standingBelly = Centroid(rest, abdomen), sleepingBelly = Centroid(asleep, abdomen);
        double bellyFloor = double.PositiveInfinity, headFloor = double.PositiveInfinity;
        foreach (int i in abdomen) bellyFloor = Math.Min(bellyFloor, asleep[i].Y);
        foreach (int t in headTriangles)
        for (int corner = 0; corner < 3; corner++) headFloor = Math.Min(headFloor, asleep[triangles[t + corner]].Y);
        List<string> report = new List<string> {
            "leg,middleVertices,pawVertices,standingMidY,sleepingMidY,standingVerticalFraction,sleepingVerticalFraction,sleepingBellyY,wakeMidError"
        };
        string problem = null;
        for (int leg = 0; leg < 4; leg++)
        {
            Require(middle[leg].Count > 50 && paws[leg].Count > 30, "The actual mid-leg/paw samples are missing leg " + leg + ".");
            Point3D standMiddle = Centroid(rest, middle[leg]), standPaw = Centroid(rest, paws[leg]);
            Point3D lieMiddle = Centroid(asleep, middle[leg]), liePaw = Centroid(asleep, paws[leg]);
            Vector3D standingAxis = standMiddle - standPaw, sleepingAxis = lieMiddle - liePaw;
            double standingVertical = Math.Abs(standingAxis.Y) / standingAxis.Length;
            double sleepingVertical = Math.Abs(sleepingAxis.Y) / sleepingAxis.Length;
            double wakeError = 0;
            foreach (int i in middle[leg]) wakeError = Math.Max(wakeError, (awake[i] - rest[i]).Length);
            report.Add(leg + "," + middle[leg].Count + "," + paws[leg].Count + "," + standMiddle.Y.ToString(CultureInfo.InvariantCulture)
                + "," + lieMiddle.Y.ToString(CultureInfo.InvariantCulture) + "," + standingVertical.ToString(CultureInfo.InvariantCulture)
                + "," + sleepingVertical.ToString(CultureInfo.InvariantCulture) + "," + sleepingBelly.Y.ToString(CultureInfo.InvariantCulture)
                + "," + wakeError.ToString(CultureInfo.InvariantCulture));
            // A resting limb extends along the ground; a vertical post below a
            // lowered torso still fails, even when its footpad is perfectly flat.
            if (!Finite(sleepingVertical) || sleepingVertical >= .70 || standingVertical - sleepingVertical <= .15)
                problem = "Sleeping leg " + leg + " remains a standing support column: actual mid-leg-to-paw vertical fraction "
                    + sleepingVertical + " versus standing " + standingVertical + ".";
            else if (lieMiddle.Y > sleepingBelly.Y + .15 || standMiddle.Y - lieMiddle.Y < .10)
                problem = "Sleeping leg " + leg + " keeps its elbow/knee above the lowered belly: middle Y=" + lieMiddle.Y
                    + ", belly Y=" + sleepingBelly.Y + ", standing middle Y=" + standMiddle.Y + ".";
            else if (wakeError >= .02)
                problem = "The folded actual mid-leg vertices fail to return after waking: leg " + leg + ", error=" + wakeError + ".";
        }
        File.WriteAllLines(Path.Combine(Output, "sleep-leg-support.csv"), report.ToArray());
        File.WriteAllText(Path.Combine(Output, "sleep-body-support.txt"), "standingBelly=" + standingBelly + ", sleepingBelly=" + sleepingBelly
            + ", lowestAbdomenY=" + bellyFloor + ", lowestActualHeadY=" + headFloor + ", sleepingMouth=" + sleepingMouth + "\r\n");
        Require(problem == null, problem);
        Require(standingBelly.Y - sleepingBelly.Y > .20 && sleepingBelly.Y < .35,
            "The actual lower abdomen does not rest near the floor: standing=" + standingBelly.Y + ", sleeping=" + sleepingBelly.Y + ".");
        Require(bellyFloor >= -.015 && headFloor >= -.015 && sleepingMouth.Y >= .08,
            "The fully lowered torso/head penetrates the floor or loses muzzle clearance: abdomen=" + bellyFloor
            + ", actual head=" + headFloor + ", mouth=" + sleepingMouth.Y + ".");
        Pass("all four actual middle-leg regions fold along the floor, elbows/knees lower with the resting abdomen, head/muzzle clear the ground, and mid-leg vertices recover on wake");
    }
    static Point3D Centroid(Point3D[] positions, List<int> indices)
    {
        Vector3D sum = new Vector3D();
        foreach (int i in indices) sum += (Vector3D)positions[i];
        sum /= indices.Count; return (Point3D)sum;
    }

    static void CheckSleepVolume(Point3D[] rest, Point3D[] asleep)
    {
        List<int> chest = new List<int>();
        for (int i = 0; i < rest.Length; i++)
        {
            Point3D p = rest[i];
            if (Math.Abs(p.X) < .22 && p.Y > .45 && p.Y < .90 && p.Z > .12 && p.Z < .65) chest.Add(i);
        }
        Require(chest.Count > 200, "Actual chest volume samples are missing.");
        // The covariance ellipsoid measures all three spatial axes and is
        // invariant to a rigid rotation; a bent neck is not mistaken for a
        // flattened chest simply because its projected height changes.
        double ratio = SpatialVolume(asleep, chest) / SpatialVolume(rest, chest);
        File.WriteAllText(Path.Combine(Output, "sleep-volume.txt"), "actualChestVertices=" + chest.Count
            + ", spatialVolumeRatio=" + ratio.ToString(CultureInfo.InvariantCulture)
            + ", minimum=.55, priorFlattenedBuild=.23277\r\n");
        Require(Finite(ratio) && ratio > .55, "The actual sleeping chest collapses into a thin sheet: volume ratio=" + ratio);
        Pass("actual chest retains spatial volume through the resting pose; covariance-volume ratio=" + ratio.ToString("F3", CultureInfo.InvariantCulture));
    }
    static double SpatialVolume(Point3D[] positions, List<int> indices)
    {
        Point3D center = Centroid(positions, indices);
        double xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
        foreach (int i in indices)
        {
            Vector3D d = positions[i] - center;
            xx += d.X * d.X; yy += d.Y * d.Y; zz += d.Z * d.Z;
            xy += d.X * d.Y; xz += d.X * d.Z; yz += d.Y * d.Z;
        }
        return Math.Sqrt(Math.Max(0, xx * (yy * zz - yz * yz) - xy * (xy * zz - xz * yz) + xz * (xy * yz - xz * yy)));
    }

    sealed class SleepEdge { internal int A, B; internal double Length; }
    static List<SleepEdge> SleepAnkleEdges(Point3D[] rest, int[] triangles)
    {
        List<SleepEdge> edges = new List<SleepEdge>(); HashSet<long> seen = new HashSet<long>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            bool ankle = false;
            for (int corner = 0; corner < 3; corner++)
            {
                Point3D p = rest[triangles[t + corner]];
                ankle |= p.Y >= .18 && p.Y <= .36 && Math.Abs(p.X) > .10;
            }
            if (!ankle) continue;
            // Include adjacent edges of a triangle crossing the ankle band,
            // so a fold exactly at the band's boundary is still measured.
            for (int corner = 0; corner < 3; corner++)
            {
                int a = triangles[t + corner], b = triangles[t + (corner + 1) % 3];
                int minimum = Math.Min(a, b), maximum = Math.Max(a, b);
                long key = ((long)minimum << 32) | (uint)maximum;
                if (!seen.Add(key)) continue;
                double length = (rest[a] - rest[b]).Length;
                if (length > .0005) edges.Add(new SleepEdge { A = a, B = b, Length = length });
            }
        }
        return edges;
    }
    static void CheckSleepTransition(WestieModel model, GeneratedWestie generated, string state, Point3D[] rest,
        List<int> feet, List<int> frontFootTriangles, int[] triangles, List<int> headTriangles, List<SleepEdge> ankleEdges,
        SleepCollisionAudit collisionAudit)
    {
        model.BeginAction(state); double previousAge = 0;
        foreach (double targetAge in SleepSamples)
        {
            // Continue one real transition in chronological order; restarting
            // the pose at every sample would conceal the actual blended path.
            while (previousAge + .042 < targetAge)
            { previousAge += .042; Step(model, state, previousAge, .042); }
            double remaining = targetAge - previousAge;
            if (remaining > .000001) Step(model, state, targetAge, remaining);
            previousAge = targetAge;
            Point3D[] pose = generated.GetSkinnedPositions(0);
            AuditSleepingHeadNeck(pose, state == "sleep" ? "sleep" : "wake", targetAge, collisionAudit, true);
            double floor = double.PositiveInfinity;
            foreach (int foot in feet) floor = Math.Min(floor, pose[foot].Y);
            SleepSurfaceContact contact = SleepingSurfaceContact(pose, triangles, frontFootTriangles, headTriangles);
            double clearance = contact.Gap;
            double largest = 0; SleepEdge worst = null; List<double> ratios = new List<double>(ankleEdges.Count);
            foreach (SleepEdge edge in ankleEdges)
            {
                double ratio = (pose[edge.A] - pose[edge.B]).Length / edge.Length;
                ratios.Add(ratio);
                if (ratio > largest) { largest = ratio; worst = edge; }
            }
            ratios.Sort(); double p95 = ratios[(int)Math.Ceiling(ratios.Count * .95) - 1];
            string action = state == "sleep" ? "sleep" : "wake";
            File.AppendAllText(Path.Combine(Output, "sleep-transition.csv"), action + "," + targetAge.ToString(CultureInfo.InvariantCulture)
                + "," + feet.Count + "," + floor.ToString(CultureInfo.InvariantCulture) + "," + clearance.ToString(CultureInfo.InvariantCulture)
                + "," + contact.Intersections + "," + ankleEdges.Count + "," + largest.ToString(CultureInfo.InvariantCulture) + "," + p95.ToString(CultureInfo.InvariantCulture)
                + "," + (worst == null ? -1 : worst.A) + "," + (worst == null ? -1 : worst.B) + "\r\n");
            string detail = action + " age=" + targetAge + ", lowest foot=" + floor + ", foot/head gap=" + clearance
                + ", intersections=" + contact.Intersections + ", ankle max edge ratio=" + largest;
            Require(floor >= -.015, "Actual foot vertices pass below the floor during the blended transition: " + detail);
            Require(contact.Intersections == 0 && clearance > .005,
                "Actual front-paw and head surfaces intersect or lose their small safety gap during the blended transition: " + detail);
            Require(Finite(largest) && largest < 6, "Ankle skin severely stretches during the blended transition: " + detail);
        }
    }
    sealed class SleepSurfaceContact { internal double Gap; internal int Intersections; }
    sealed class SleepTriangle
    {
        internal Point3D A, B, C;
        internal int IndexA, IndexB, IndexC, Offset;
        internal double MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
    }
    static List<SleepTriangle> SleepTriangleSurface(Point3D[] pose, int[] indices, List<int> offsets)
    {
        List<SleepTriangle> result = new List<SleepTriangle>(offsets.Count);
        foreach (int t in offsets)
        {
            Point3D a = pose[indices[t]], b = pose[indices[t + 1]], c = pose[indices[t + 2]];
            result.Add(new SleepTriangle { A = a, B = b, C = c,
                IndexA = indices[t], IndexB = indices[t + 1], IndexC = indices[t + 2], Offset = t,
                MinX = Math.Min(a.X, Math.Min(b.X, c.X)), MaxX = Math.Max(a.X, Math.Max(b.X, c.X)),
                MinY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), MaxY = Math.Max(a.Y, Math.Max(b.Y, c.Y)),
                MinZ = Math.Min(a.Z, Math.Min(b.Z, c.Z)), MaxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z)) });
        }
        return result;
    }

    sealed class SleepCollisionAudit
    {
        internal int[] Indices;
        internal List<int> Head = new List<int>(), Neck = new List<int>(), Tail = new List<int>(), Back = new List<int>();
        internal HashSet<long> BindHeadNeckPairs, BindTailBackPairs;
        internal List<int> Chest = new List<int>();
        internal HashSet<long> BindChestPairs;
    }
    static void RenderSleepNeckDiagnostic(WestieModel model, GeneratedWestie generated)
    {
        model.Heading.Angle = 0; model.WalkingPaused = false;
        Advance(model, "idle", 2.4, .042);
        Point3D[] standing = generated.GetSkinnedPositions(0);
        MeshGeometry3D mesh = (MeshGeometry3D)((GeometryModel3D)generated.Model.Children[0]).Geometry;
        int[] indices = new int[mesh.TriangleIndices.Count]; mesh.TriangleIndices.CopyTo(indices, 0);
        SleepCollisionAudit audit = CaptureSleepCollisionAudit(generated, standing, indices);
        HashSet<int> neckVertices = new HashSet<int>();
        foreach (int t in audit.Neck)
        for (int corner = 0; corner < 3; corner++) neckVertices.Add(indices[t + corner]);
        List<int> neckPoints = new List<int>(neckVertices);
        foreach (string state in new[] { "sleep", "idle" })
        {
            string name = state == "sleep" ? "sleep" : "wake";
            model.BeginAction(state); double previousAge = 0;
            foreach (double age in SleepSamples)
            {
                while (previousAge + .042 < age)
                { previousAge += .042; Step(model, state, previousAge, .042); }
                double remaining = age - previousAge;
                if (remaining > .000001) Step(model, state, age, remaining);
                previousAge = age;
                AuditSleepingHeadNeck(generated.GetSkinnedPositions(0), name, age, audit, false);
                string sample = "sleep-neck-view-" + name + "-" + age.ToString("0.00", CultureInfo.InvariantCulture);
                SaveSleepNeckCloseup(model, generated, neckPoints, sample + "-front-close");
                if (age < 2.39) continue;
                foreach (KeyValuePair<string, double> angle in new[] {
                    new KeyValuePair<string, double>("front", 0),
                    new KeyValuePair<string, double>("threequarter", 52),
                    new KeyValuePair<string, double>("side", 90) })
                {
                    model.Heading.Angle = angle.Value; Step(model, state, age, .002);
                    Save(Render(PetWindow.CreateViewport(model), 512, 448), sample + "-" + angle.Key + "-full");
                    if (angle.Value != 0) SaveSleepNeckCloseup(model, generated, neckPoints, sample + "-" + angle.Key + "-close");
                }
                model.Heading.Angle = 0; Step(model, state, age, .002);
            }
        }
        string note = "DIAGNOSTIC: source bind baseline and nine production samples per sleep/wake transition.\r\n"
            + "All eighteen samples use enforce=false; discovered intersections do not abort this diagnostic.\r\n"
            + "CSV: sleep-head-neck-crossings.csv, sleep-head-neck-bind-pairs.csv, sleep-head-neck-new-pairs.csv, sleep-chest-crossings.csv.\r\n"
            + "Images: all samples have neck closeups; terminal sleep and wake have normal full-body front/52/90-degree views.\r\n"
            + "This is not a regression PASS or appearance approval. All normal regression assertions remain active outside this mode.\r\n";
        File.WriteAllText(Path.Combine(Output, "sleep-neck-view-diagnostic.txt"), note);
        Console.WriteLine(note + File.ReadAllText(Path.Combine(Output, "sleep-head-neck-crossings.csv")) + "DIAGNOSTIC output: " + Output);
    }
    static void SaveSleepNeckCloseup(WestieModel model, GeneratedWestie generated, List<int> neckPoints, string name)
    {
        Point3D target = Centroid(generated.GetSkinnedPositions(0), neckPoints);
        Viewport3D view = PetWindow.CreateViewport(model);
        view.Camera = new OrthographicCamera(target + new Vector3D(0, .15, 6), new Vector3D(0, -.15, -6), new Vector3D(0, 1, 0), 1.5);
        Save(Render(view, 600, 480), name);
    }
    static SleepCollisionAudit CaptureSleepCollisionAudit(GeneratedWestie generated, Point3D[] standing, int[] indices)
    {
        System.Collections.IList surfaces = Field<System.Collections.IList>(generated, "surfaces");
        Array bind = Field<Array>(surfaces[0], "Bind"); Type vertexType = bind.GetType().GetElementType();
        FieldInfo position = vertexType.GetField("Position", Private);
        FieldInfo[] bones = new FieldInfo[4], weights = new FieldInfo[4];
        for (int i = 0; i < 4; i++) { bones[i] = vertexType.GetField("B" + i, Private); weights[i] = vertexType.GetField("W" + i, Private); }
        Point3D[] original = new Point3D[bind.Length]; bool[] distalTail = new bool[bind.Length], rearBody = new bool[bind.Length];
        for (int i = 0; i < bind.Length; i++)
        {
            object vertex = bind.GetValue(i); Point3D p = (Point3D)position.GetValue(vertex); original[i] = p;
            int dominant = -1; double strongest = 0;
            for (int influence = 0; influence < 4; influence++)
            {
                double weight = (double)weights[influence].GetValue(vertex);
                if (weight <= strongest) continue;
                strongest = weight; dominant = (byte)bones[influence].GetValue(vertex);
            }
            distalTail[i] = strongest > .55 && dominant == 6 && p.Z < -.95;
            rearBody[i] = strongest > .55 && dominant == 0 && p.Y > .65 && p.Y < 1.20 && p.Z < -.30;
        }
        SleepCollisionAudit audit = new SleepCollisionAudit { Indices = indices };
        for (int t = 0; t < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            Point3D center = new Point3D((original[a].X + original[b].X + original[c].X) / 3,
                (original[a].Y + original[b].Y + original[c].Y) / 3, (original[a].Z + original[b].Z + original[c].Z) / 3);
            if (center.Y > 1.20 && center.Z > .35) audit.Head.Add(t);
            if (center.Y >= .85 && center.Y <= 1.15 && center.Z >= .20 && center.Z <= .75) audit.Neck.Add(t);
            if (center.Y >= .30 && center.Y <= 1.20 && center.Z >= .05 && center.Z <= .85) audit.Chest.Add(t);
            if ((distalTail[a] ? 1 : 0) + (distalTail[b] ? 1 : 0) + (distalTail[c] ? 1 : 0) >= 2) audit.Tail.Add(t);
            if ((rearBody[a] ? 1 : 0) + (rearBody[b] ? 1 : 0) + (rearBody[c] ? 1 : 0) >= 2) audit.Back.Add(t);
        }
        Require(audit.Head.Count > 20 && audit.Neck.Count > 20, "The actual source head/neck triangle regions are missing.");
        audit.BindHeadNeckPairs = SleepNonAdjacentCrossings(original, indices, audit.Head, audit.Neck);
        audit.BindChestPairs = SleepNonAdjacentCrossings(original, indices, audit.Chest, audit.Chest);
        File.WriteAllText(Path.Combine(Output, "sleep-chest-crossings.csv"), "sample,age,sourceBindPairs,currentPairs,introducedPairs,visibleCrossings,maxCrossingDepth\r\n");
        File.WriteAllText(Path.Combine(Output, "sleep-chest-new-pairs.csv"), "sample,age,firstTriangle,secondTriangle\r\n");
        File.WriteAllText(Path.Combine(Output, "sleep-head-neck-crossings.csv"),
            "sample,age,sourceBindPairs,currentPairs,introducedPairs,headTriangles,neckTriangles\r\n"
            + "source-bind,0," + audit.BindHeadNeckPairs.Count + "," + audit.BindHeadNeckPairs.Count + ",0," + audit.Head.Count + "," + audit.Neck.Count + "\r\n");
        List<string> baseline = new List<string> { "headTriangle,neckTriangle" };
        foreach (long pair in audit.BindHeadNeckPairs) baseline.Add((pair >> 32) + "," + (uint)pair);
        File.WriteAllLines(Path.Combine(Output, "sleep-head-neck-bind-pairs.csv"), baseline.ToArray());
        File.WriteAllText(Path.Combine(Output, "sleep-head-neck-new-pairs.csv"), "sample,age,headTriangle,neckTriangle\r\n");
        File.WriteAllText(Path.Combine(Output, "sleep-tail-diagnostic.csv"), "sample,age,tailTriangles,backTriangles,sourceBindPairs,currentPairs,introducedPairs,actualSurfaceGap\r\n");
        if (audit.Tail.Count > 10 && audit.Back.Count > 10)
        {
            audit.BindTailBackPairs = SleepNonAdjacentCrossings(original, indices, audit.Tail, audit.Back);
            ReportSleepTail(original, "source-bind", 0, audit);
        }
        else File.WriteAllText(Path.Combine(Output, "sleep-tail-diagnostic.txt"),
            "DIAGNOSTIC: reliable strong-weight distal-tail/rear-body regions unavailable; no tail assertion was made.\r\n");
        // Report the animated standing rig separately from the raw source bind
        // mesh. Neither report silently replaces the original collision baseline.
        AuditSleepingHeadNeck(standing, "standing", 0, audit, false);
        Console.WriteLine("Head/neck source bind intersections=" + audit.BindHeadNeckPairs.Count + ", head/neck triangles=" + audit.Head.Count + "/" + audit.Neck.Count);
        return audit;
    }
    static void AuditSleepingHeadNeck(Point3D[] pose, string sample, double age, SleepCollisionAudit audit, bool enforce)
    {
        if (!enforce) using (BinaryWriter writer = new BinaryWriter(File.Create(Path.Combine(Output,
            "chest-pose-" + sample + "-" + age.ToString("0.00", CultureInfo.InvariantCulture) + ".bin"))))
            foreach (Point3D point in pose) { writer.Write(point.X); writer.Write(point.Y); writer.Write(point.Z); }
        HashSet<long> chestPairs = SleepNonAdjacentCrossings(pose, audit.Indices, audit.Chest, audit.Chest);
        HashSet<long> newChestPairs = new HashSet<long>(chestPairs); newChestPairs.ExceptWith(audit.BindChestPairs);
        int visibleCrossings = 0; double maxCrossingDepth = 0;
        foreach (long pair in newChestPairs)
        {
            int a = (int)(pair >> 32) * 3, b = (int)(uint)pair * 3;
            double depth = Math.Min(SleepPlaneCrossingDepth(pose, audit.Indices, a, b), SleepPlaneCrossingDepth(pose, audit.Indices, b, a));
            maxCrossingDepth = Math.Max(maxCrossingDepth, depth);
            // The reduced sculpt has UV seams and subpixel surface details.
            // Keep every raw pair in the report; reject penetrations over .002
            // world units (under half a pixel in the 512px review render).
            if (depth > .002) visibleCrossings++;
        }
        File.AppendAllText(Path.Combine(Output, "sleep-chest-crossings.csv"), sample + "," + age.ToString(CultureInfo.InvariantCulture)
            + "," + audit.BindChestPairs.Count + "," + chestPairs.Count + "," + newChestPairs.Count + "," + visibleCrossings
            + "," + maxCrossingDepth.ToString(CultureInfo.InvariantCulture) + "\r\n");
        List<string> chestRows = new List<string>();
        foreach (long pair in newChestPairs) chestRows.Add(sample + "," + age.ToString(CultureInfo.InvariantCulture)
            + "," + (pair >> 32) + "," + (uint)pair);
        File.AppendAllLines(Path.Combine(Output, "sleep-chest-new-pairs.csv"), chestRows.ToArray());
        if (enforce) Require(visibleCrossings == 0, "Chest surface folds through itself during " + sample + " at " + age + "s: "
            + visibleCrossings + " crossings over .002 world units, maximum depth=" + maxCrossingDepth + ".");
        HashSet<long> pairs = SleepNonAdjacentCrossings(pose, audit.Indices, audit.Head, audit.Neck);
        HashSet<long> introduced = new HashSet<long>(pairs); introduced.ExceptWith(audit.BindHeadNeckPairs);
        File.AppendAllText(Path.Combine(Output, "sleep-head-neck-crossings.csv"), sample + "," + age.ToString(CultureInfo.InvariantCulture)
            + "," + audit.BindHeadNeckPairs.Count + "," + pairs.Count + "," + introduced.Count + "," + audit.Head.Count + "," + audit.Neck.Count + "\r\n");
        foreach (long pair in introduced)
            File.AppendAllText(Path.Combine(Output, "sleep-head-neck-new-pairs.csv"), sample + "," + age.ToString(CultureInfo.InvariantCulture)
                + "," + (pair >> 32) + "," + (uint)pair + "\r\n");
        if (age >= 2.39 || sample == "standing") ReportSleepTail(pose, sample, age, audit);
        if (enforce) Require(introduced.Count == 0, "Actual nonadjacent head/neck triangles introduce intersections during "
            + sample + " at " + age + "s: source bind=" + audit.BindHeadNeckPairs.Count + ", current=" + pairs.Count + ", new pairs=" + introduced.Count + ".");
    }
    static double SleepPlaneCrossingDepth(Point3D[] pose, int[] indices, int triangle, int plane)
    {
        Point3D origin = pose[indices[plane]];
        Vector3D normal = Vector3D.CrossProduct(pose[indices[plane + 1]] - origin, pose[indices[plane + 2]] - origin);
        if (normal.LengthSquared < 1e-18) return 0;
        normal.Normalize(); double lower = double.PositiveInfinity, upper = double.NegativeInfinity;
        for (int corner = 0; corner < 3; corner++)
        {
            double distance = Vector3D.DotProduct(pose[indices[triangle + corner]] - origin, normal);
            lower = Math.Min(lower, distance); upper = Math.Max(upper, distance);
        }
        return Math.Max(0, Math.Min(upper, -lower));
    }
    static void ReportSleepTail(Point3D[] pose, string sample, double age, SleepCollisionAudit audit)
    {
        if (audit.BindTailBackPairs == null) return;
        HashSet<long> pairs = SleepNonAdjacentCrossings(pose, audit.Indices, audit.Tail, audit.Back);
        HashSet<long> introduced = new HashSet<long>(pairs); introduced.ExceptWith(audit.BindTailBackPairs);
        SleepSurfaceContact contact = SleepingSurfaceContact(pose, audit.Indices, audit.Tail, audit.Back);
        File.AppendAllText(Path.Combine(Output, "sleep-tail-diagnostic.csv"), sample + "," + age.ToString(CultureInfo.InvariantCulture)
            + "," + audit.Tail.Count + "," + audit.Back.Count + "," + audit.BindTailBackPairs.Count + "," + pairs.Count
            + "," + introduced.Count + "," + contact.Gap.ToString(CultureInfo.InvariantCulture) + "\r\n");
        // This diagnoses actual geometry. Tail curvature/softness still needs
        // visual review; do not turn a coarse cosmetic expectation into a test.
    }
    static long SleepCollisionCell(int x, int y, int z)
    { return ((long)(x + 1024) << 42) | ((long)(y + 1024) << 21) | (uint)(z + 1024); }
    static HashSet<long> SleepNonAdjacentCrossings(Point3D[] pose, int[] indices, List<int> first, List<int> second)
    {
        const double cellSize = .08, epsilon = 1e-8;
        List<SleepTriangle> head = SleepTriangleSurface(pose, indices, first), neck = SleepTriangleSurface(pose, indices, second);
        Dictionary<long, List<SleepTriangle>> cells = new Dictionary<long, List<SleepTriangle>>();
        foreach (SleepTriangle b in neck)
        for (int x = (int)Math.Floor((b.MinX - epsilon) / cellSize); x <= (int)Math.Floor((b.MaxX + epsilon) / cellSize); x++)
        for (int y = (int)Math.Floor((b.MinY - epsilon) / cellSize); y <= (int)Math.Floor((b.MaxY + epsilon) / cellSize); y++)
        for (int z = (int)Math.Floor((b.MinZ - epsilon) / cellSize); z <= (int)Math.Floor((b.MaxZ + epsilon) / cellSize); z++)
        {
            long cell = SleepCollisionCell(x, y, z); List<SleepTriangle> bucket;
            if (!cells.TryGetValue(cell, out bucket)) { bucket = new List<SleepTriangle>(); cells.Add(cell, bucket); }
            bucket.Add(b);
        }
        HashSet<long> seen = new HashSet<long>(), crossings = new HashSet<long>();
        foreach (SleepTriangle a in head)
        for (int x = (int)Math.Floor((a.MinX - epsilon) / cellSize); x <= (int)Math.Floor((a.MaxX + epsilon) / cellSize); x++)
        for (int y = (int)Math.Floor((a.MinY - epsilon) / cellSize); y <= (int)Math.Floor((a.MaxY + epsilon) / cellSize); y++)
        for (int z = (int)Math.Floor((a.MinZ - epsilon) / cellSize); z <= (int)Math.Floor((a.MaxZ + epsilon) / cellSize); z++)
        {
            List<SleepTriangle> candidates;
            if (!cells.TryGetValue(SleepCollisionCell(x, y, z), out candidates)) continue;
            foreach (SleepTriangle b in candidates)
            {
                if (ReferenceEquals(first, second) && a.Offset >= b.Offset) continue;
                if (a.MinX > b.MaxX + epsilon || b.MinX > a.MaxX + epsilon || a.MinY > b.MaxY + epsilon
                    || b.MinY > a.MaxY + epsilon || a.MinZ > b.MaxZ + epsilon || b.MinZ > a.MaxZ + epsilon) continue;
                long pair = ((long)(a.Offset / 3) << 32) | (uint)(b.Offset / 3);
                if (!seen.Add(pair) || SleepTrianglesShareVertex(a, b)) continue;
                if (SleepTrianglesCross(a, b)) crossings.Add(pair);
            }
        }
        return crossings;
    }
    static bool SleepTrianglesShareVertex(SleepTriangle a, SleepTriangle b)
    {
        return a.IndexA == b.IndexA || a.IndexA == b.IndexB || a.IndexA == b.IndexC
            || a.IndexB == b.IndexA || a.IndexB == b.IndexB || a.IndexB == b.IndexC
            || a.IndexC == b.IndexA || a.IndexC == b.IndexB || a.IndexC == b.IndexC
            // UV islands have distinct indices at the same geometric vertex.
            || (a.A - b.A).LengthSquared < 1e-16 || (a.A - b.B).LengthSquared < 1e-16 || (a.A - b.C).LengthSquared < 1e-16
            || (a.B - b.A).LengthSquared < 1e-16 || (a.B - b.B).LengthSquared < 1e-16 || (a.B - b.C).LengthSquared < 1e-16
            || (a.C - b.A).LengthSquared < 1e-16 || (a.C - b.B).LengthSquared < 1e-16 || (a.C - b.C).LengthSquared < 1e-16;
    }
    static SleepSurfaceContact SleepingSurfaceContact(Point3D[] pose, int[] indices, List<int> first, List<int> second)
    {
        List<SleepTriangle> paws = SleepTriangleSurface(pose, indices, first), heads = SleepTriangleSurface(pose, indices, second);
        double best = double.PositiveInfinity; int intersections = 0;
        foreach (SleepTriangle a in paws)
        foreach (SleepTriangle b in heads)
        {
            double dx = Math.Max(0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            double dy = Math.Max(0, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
            double dz = Math.Max(0, Math.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
            double boxes = dx * dx + dy * dy + dz * dz;
            bool overlap = boxes <= 1e-16;
            if (overlap && SleepTrianglesCross(a, b)) { intersections++; best = 0; continue; }
            if (!overlap && boxes >= best) continue;
            double distance = SleepTriangleDistanceSquared(a, b);
            // Vertex/face and edge/edge distances also cover coplanar overlap
            // and contact. A zero-distance pair cannot satisfy a positive gap.
            if (overlap && distance <= 1e-18) intersections++;
            best = Math.Min(best, distance);
        }
        return new SleepSurfaceContact { Gap = Math.Sqrt(best), Intersections = intersections };
    }
    static bool SleepTrianglesCross(SleepTriangle a, SleepTriangle b)
    {
        return SleepSegmentTriangle(a.A, a.B, b) || SleepSegmentTriangle(a.B, a.C, b) || SleepSegmentTriangle(a.C, a.A, b)
            || SleepSegmentTriangle(b.A, b.B, a) || SleepSegmentTriangle(b.B, b.C, a) || SleepSegmentTriangle(b.C, b.A, a);
    }
    static bool SleepSegmentTriangle(Point3D start, Point3D end, SleepTriangle triangle)
    {
        Vector3D direction = end - start, edge1 = triangle.B - triangle.A, edge2 = triangle.C - triangle.A;
        Vector3D cross = Vector3D.CrossProduct(direction, edge2);
        double determinant = Vector3D.DotProduct(edge1, cross);
        if (Math.Abs(determinant) < 1e-12) return false;
        double inverse = 1 / determinant;
        Vector3D from = start - triangle.A;
        double u = Vector3D.DotProduct(from, cross) * inverse;
        if (u < -1e-8 || u > 1 + 1e-8) return false;
        Vector3D q = Vector3D.CrossProduct(from, edge1);
        double v = Vector3D.DotProduct(direction, q) * inverse;
        if (v < -1e-8 || u + v > 1 + 1e-8) return false;
        double along = Vector3D.DotProduct(edge2, q) * inverse;
        return along >= -1e-8 && along <= 1 + 1e-8;
    }
    static double SleepTriangleDistanceSquared(SleepTriangle a, SleepTriangle b)
    {
        double best = Math.Min(TriangleDistanceSquared(a.A, b.A, b.B, b.C), Math.Min(TriangleDistanceSquared(a.B, b.A, b.B, b.C), TriangleDistanceSquared(a.C, b.A, b.B, b.C)));
        best = Math.Min(best, Math.Min(TriangleDistanceSquared(b.A, a.A, a.B, a.C), Math.Min(TriangleDistanceSquared(b.B, a.A, a.B, a.C), TriangleDistanceSquared(b.C, a.A, a.B, a.C))));
        best = Math.Min(best, SleepEdgeDistanceSquared(a.A, a.B, b.A, b.B));
        best = Math.Min(best, SleepEdgeDistanceSquared(a.A, a.B, b.B, b.C));
        best = Math.Min(best, SleepEdgeDistanceSquared(a.A, a.B, b.C, b.A));
        best = Math.Min(best, SleepEdgeDistanceSquared(a.B, a.C, b.A, b.B));
        best = Math.Min(best, SleepEdgeDistanceSquared(a.B, a.C, b.B, b.C));
        best = Math.Min(best, SleepEdgeDistanceSquared(a.B, a.C, b.C, b.A));
        best = Math.Min(best, SleepEdgeDistanceSquared(a.C, a.A, b.A, b.B));
        best = Math.Min(best, SleepEdgeDistanceSquared(a.C, a.A, b.B, b.C));
        return Math.Min(best, SleepEdgeDistanceSquared(a.C, a.A, b.C, b.A));
    }
    static double SleepEdgeDistanceSquared(Point3D a0, Point3D a1, Point3D b0, Point3D b1)
    {
        Vector3D u = a1 - a0, v = b1 - b0, w = a0 - b0;
        double aa = u.LengthSquared, bb = Vector3D.DotProduct(u, v), cc = v.LengthSquared;
        double dd = Vector3D.DotProduct(u, w), ee = Vector3D.DotProduct(v, w), s = 0, t = 0;
        if (aa <= 1e-20 && cc <= 1e-20) return w.LengthSquared;
        if (aa <= 1e-20) t = Math.Max(0, Math.Min(1, ee / cc));
        else if (cc <= 1e-20) s = Math.Max(0, Math.Min(1, -dd / aa));
        else
        {
            double denominator = aa * cc - bb * bb;
            if (denominator > 1e-20) s = Math.Max(0, Math.Min(1, (bb * ee - cc * dd) / denominator));
            t = (bb * s + ee) / cc;
            if (t < 0) { t = 0; s = Math.Max(0, Math.Min(1, -dd / aa)); }
            else if (t > 1) { t = 1; s = Math.Max(0, Math.Min(1, (bb - dd) / aa)); }
        }
        return (w + u * s - v * t).LengthSquared;
    }

    static void CheckFood(WestieModel model, GeneratedWestie generated)
    {
        model.Heading.Angle = 0;
        Advance(model, "idle", 2.4, .042);
        ScaleTransform3D size = Field<ScaleTransform3D>(model, "biscuitSize");
        TranslateTransform3D position = Field<TranslateTransform3D>(model, "biscuitPosition");
        double jawMotion = 0, previousJaw = 0, minMouthDistance = double.PositiveInfinity;
        Point3D[] before = generated.GetSkinnedPositions(0);
        for (int i = 0; i <= 108; i++)
        {
            double age = i * .042; Step(model, "feed", age, .042);
            if (i == 15) Require(size.ScaleX > .99, "Feeding did not show a full approaching biscuit.");
            if (i == 55) Require(size.ScaleX > .45 && size.ScaleX < .55, "The biscuit did not lose pieces in stages.");
            if (i > 85) Require(size.ScaleX == 0, "The eaten biscuit remains visible.");
            if (age > .9 && age < 3.5)
            {
                Point3D biscuit = new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ);
                minMouthDistance = Math.Min(minMouthDistance, (biscuit - generated.Mouth).Length);
                double jaw = Field<double>(generated, "jawAngle");
                jawMotion += Math.Abs(jaw - previousJaw); previousJaw = jaw;
            }
        }
        Require(minMouthDistance < .16, "The production biscuit never reaches the actual generated mouth.");
        Require(jawMotion > 30, "The production jaw does not chew through feeding.");
        // Compare actual lower-muzzle vertices on two chew phases, with the
        // same stable head pose. A moving scalar without mesh change fails.
        model.BeginAction("feed");
        for (int i = 0; i < 30; i++) Step(model, "feed", 1.06, .042);
        Point3D[] jawA = generated.GetSkinnedPositions(0);
        for (int i = 0; i < 30; i++) Step(model, "feed", 1.37, .042);
        Point3D[] jawB = generated.GetSkinnedPositions(0);
        double visibleJaw = 0;
        for (int i = 0; i < before.Length; i++)
            if (before[i].Y > 1.05 && before[i].Y < 1.48 && before[i].Z > .50)
                visibleJaw = Math.Max(visibleJaw, (jawA[i] - jawB[i]).Length);
        Require(visibleJaw > .012, "Actual muzzle vertices did not change during chewing.");
        model.BeginAction("feed"); Step(model, "feed", .5, .042);
        Require(size.ScaleX > .99, "Repeated feed did not restore the full biscuit.");
        foreach (string state in new[] { "pet", "play", "sleep", "idle" })
        {
            model.BeginAction(state); Step(model, state, 0, .042);
            Require(size.ScaleX == 0, "Interrupted feeding left a biscuit in " + state);
            foreach (ScaleTransform3D crumb in Field<ScaleTransform3D[]>(model, "crumbSizes"))
                Require(crumb.ScaleX == 0, "Interrupted feeding left crumbs in " + state);
        }
        Pass("production biscuit approaches the real mouth, is consumed and replays, actual muzzle vertices chew, every interruption clears food and crumbs");
    }

    sealed class Surface { internal MeshGeometry3D Mesh; internal Transform3D Transform; internal int[] Indices; internal Point3D[] Points; }
    static void Collect(Model3D model, Transform3D parent, List<Surface> output)
    {
        Transform3DGroup transform = new Transform3DGroup(); transform.Children.Add(model.Transform); transform.Children.Add(parent);
        Model3DGroup group = model as Model3DGroup;
        if (group != null) { foreach (Model3D child in group.Children) Collect(child, transform, output); return; }
        GeometryModel3D geometry = model as GeometryModel3D;
        MeshGeometry3D mesh = geometry == null ? null : geometry.Geometry as MeshGeometry3D;
        if (mesh == null) return;
        int[] indices = new int[mesh.TriangleIndices.Count]; mesh.TriangleIndices.CopyTo(indices, 0);
        output.Add(new Surface { Mesh = mesh, Transform = transform, Indices = indices, Points = new Point3D[mesh.Positions.Count] });
    }
    static double SurfaceDistance(List<Surface> surfaces, Point3D center)
    {
        double best = double.PositiveInfinity;
        foreach (Surface surface in surfaces)
        {
            surface.Mesh.Positions.CopyTo(surface.Points, 0); surface.Transform.Transform(surface.Points);
            Point3D[] p = surface.Points; int[] indices = surface.Indices;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Point3D a = p[indices[i]], b = p[indices[i + 1]], c = p[indices[i + 2]];
                double dx = Math.Max(0, Math.Max(Math.Min(a.X, Math.Min(b.X, c.X)) - center.X, center.X - Math.Max(a.X, Math.Max(b.X, c.X))));
                double dy = Math.Max(0, Math.Max(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - center.Y, center.Y - Math.Max(a.Y, Math.Max(b.Y, c.Y))));
                double dz = Math.Max(0, Math.Max(Math.Min(a.Z, Math.Min(b.Z, c.Z)) - center.Z, center.Z - Math.Max(a.Z, Math.Max(b.Z, c.Z))));
                if (dx * dx + dy * dy + dz * dz >= best) continue;
                best = Math.Min(best, TriangleDistanceSquared(center, a, b, c));
            }
        }
        return Math.Sqrt(best);
    }
    static void CheckPlay(WestieModel model, GeneratedWestie generated)
    {
        TennisToy toy = Field<TennisToy>(model, "playToy");
        TranslateTransform3D position = Field<TranslateTransform3D>(toy, "position");
        AxisAngleRotation3D spin = Field<AxisAngleRotation3D>(toy, "spin");
        ScaleTransform3D visibility = (ScaleTransform3D)toy.Model.Transform;
        double radius = 0;
        foreach (GeometryModel3D part in ((Model3DGroup)toy.Model.Children[1]).Children)
        foreach (Point3D p in ((MeshGeometry3D)part.Geometry).Positions) radius = Math.Max(radius, ((Vector3D)p).Length);
        Require(radius > .1, "The production ball has no real radius.");
        List<Surface> surfaces = new List<Surface>(); Collect(generated.Model, Transform3D.Identity, surfaces);
        File.WriteAllText(Path.Combine(Output, "contact.csv"), "fromSleep,interval,phase,age,clearance,travel\r\n");
        foreach (bool fromSleep in new[] { false, true })
        foreach (double interval in new[] { .020, .056 })
        foreach (double phase in interval < .03 ? new[] { 0.0 } : new[] { 0.0, .027 })
        {
            model.Heading.Angle = 52; Advance(model, fromSleep ? "sleep" : "idle", 2.4, .042);
            model.BeginAction("play");
            List<double> ages = new List<double> { 0 };
            for (double age = interval + phase; age <= (fromSleep ? 3.4 : 2.0); age += interval) ages.Add(age);
            double previousAge = 0, previousClearance = double.PositiveInfinity, release = double.NaN, firstTravel = 0, lateTravel = 0, previousTravel = 0;
            Point3D start = new Point3D(); bool appeared = false;
            foreach (double age in ages)
            {
                Step(model, "play", age, Math.Max(.002, age - previousAge)); previousAge = age;
                Point3D center = new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ);
                if (age == 0) start = center;
                if (visibility.ScaleX < .01)
                { Require(fromSleep && generated.SleepPose > .005, "Ball was hidden without sleep recovery."); continue; }
                double clearance = SurfaceDistance(surfaces, center) - radius, travel = (center - start).Length;
                string detail = "sleep=" + fromSleep + ", dt=" + interval + ", phase=" + phase + ", age=" + age + ", clearance=" + clearance;
                File.AppendAllText(Path.Combine(Output, "contact.csv"), fromSleep + "," + interval + "," + phase + "," + age + "," + clearance + "," + travel + "\r\n");
                Require(clearance >= -.012, "The actual dog surface penetrated the ball deeply: " + detail);
                if (!appeared) { Require(clearance >= .018, "Ball first appears touching the dog: " + detail); appeared = true; }
                if (travel > .00005 && double.IsNaN(release))
                {
                    release = age;
                    Require(Math.Min(clearance, previousClearance) <= .025, "Ball released without actual surface contact: " + detail);
                    Require(generated.SleepPose < .02, "Ball released before standing up: " + detail);
                }
                if (double.IsNaN(release)) Require(Math.Abs(spin.Angle) < .000001 && travel < .000001, "Ball moved/spun before contact.");
                else
                {
                    Require(travel >= previousTravel - .000001, "Ball rolled back during one push.");
                    if (age < release + .25) firstTravel = Math.Max(firstTravel, travel - previousTravel);
                    if (age >= release + .8) Require(clearance > .02, "Ball failed to separate from actual paw: " + detail);
                }
                previousTravel = travel; previousClearance = clearance;
            }
            Require(appeared && !double.IsNaN(release), "Dog did not stand up and complete the actual paw push.");
            // Continue to rest so slowing, final position and replay are checked
            // against the same visible production toy as surface contact.
            for (double age = previousAge + interval; age < 6; age += interval)
            {
                Step(model, "play", age, interval);
                double travel = (new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ) - start).Length;
                if (age > release + 2.7 && age < release + 3) lateTravel = Math.Max(lateTravel, travel - previousTravel);
                previousTravel = travel;
            }
            Require(previousTravel > .5 && firstTravel > lateTravel * 3, "The production ball did not travel and slow before settling.");
            Point3D stopped = new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ); double stoppedSpin = spin.Angle;
            Step(model, "play", 12, interval);
            Require((new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ) - stopped).Length < .000001 && Math.Abs(spin.Angle - stoppedSpin) < .000001,
                "Settled production ball drifted or restarted.");
            model.BeginAction("play"); Step(model, "play", 0, interval);
            Require((new Point3D(position.OffsetX, position.OffsetY, position.OffsetZ) - start).Length < .000001 && Math.Abs(spin.Angle) < .000001,
                "Repeated play retained old ball travel/spin.");
            foreach (string state in new[] { "idle", "pet", "feed", "sleep" })
            { model.BeginAction(state); Step(model, state, 0, interval); Require(visibility.ScaleX == 0, "Interrupted play left the ball visible in " + state); }
        }
        Pass("actual textured/fur mesh to real ball surface contact at 20ms, 56ms and 27ms phase offset; no deep intersection; sleep recovery, causality, separation, slowing, replay and interruption");
    }
    static double TriangleDistanceSquared(Point3D p, Point3D a, Point3D b, Point3D c)
    {
        Vector3D ab = b - a, ac = c - a, ap = p - a;
        double d1 = Vector3D.DotProduct(ab, ap), d2 = Vector3D.DotProduct(ac, ap);
        if (d1 <= 0 && d2 <= 0) return ap.LengthSquared;
        Vector3D bp = p - b; double d3 = Vector3D.DotProduct(ab, bp), d4 = Vector3D.DotProduct(ac, bp);
        if (d3 >= 0 && d4 <= d3) return bp.LengthSquared;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).LengthSquared;
        Vector3D cp = p - c; double d5 = Vector3D.DotProduct(ab, cp), d6 = Vector3D.DotProduct(ac, cp);
        if (d6 >= 0 && d5 <= d6) return cp.LengthSquared;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).LengthSquared;
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).LengthSquared;
        double sum = va + vb + vc;
        if (Math.Abs(sum) < 1e-25) return Math.Min(ap.LengthSquared, Math.Min(bp.LengthSquared, cp.LengthSquared));
        return (p - (a + ab * (vb / sum) + ac * (vc / sum))).LengthSquared;
    }

    static void CheckPerformance(WestieModel model)
    {
        List<double> timings = new List<double>();
        Advance(model, "idle", 2.4, .042);
        for (int i = 0; i < 80; i++)
        {
            Stopwatch watch = Stopwatch.StartNew(); Step(model, "walk", i * .042, .042); watch.Stop();
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }
        timings.Sort(); double median = timings[timings.Count / 2], p95 = timings[(int)(timings.Count * .95)];
        File.WriteAllText(Path.Combine(Output, "performance.txt"), "production Animate median=" + median + "ms, p95=" + p95 + "ms\r\n");
        Require(median < 56 && p95 < 100, "Generated model updates exceed the production animation budget: median " + median + "ms, p95 " + p95 + "ms.");
        Pass("production Animate median=" + median.ToString("F2") + "ms, p95=" + p95.ToString("F2") + "ms");
    }

    static void CheckInput()
    {
        Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        PetWindow pet = new PetWindow(new PetSettings { Breed = "westie", Size = .57, Roam = false, AutoHideFullscreen = false }, true, true, false);
        Exception failure = null;
        pet.Loaded += delegate
        {
            pet.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
            {
                try
                {
                    Viewport3D view = Field<Viewport3D>(pet, "viewport");
                    SilhouetteHitArea input = Field<SilhouetteHitArea>(pet, "pointerHitArea");
                    WestieModel model = Field<WestieModel>(pet, "model");
                    Require(!view.IsHitTestVisible, "Animated 3D mesh reentered desktop pointer hit testing.");
                    Set(pet, "stateUntil", double.PositiveInfinity); Set(pet, "nextAction", double.PositiveInfinity);
                    File.WriteAllText(Path.Combine(Output, "input-coverage.csv"), "pose,age,heading,scale,opaque,hitOpaque,empty,hitEmpty\r\n");
                    foreach (double angle in new[] { -52.0, 52.0 })
                    {
                        Set(pet, "heading", angle); Set(pet, "targetHeading", angle);
                        model.Heading.Angle = angle;
                        foreach (KeyValuePair<string, double> pose in new[] {
                            new KeyValuePair<string, double>("idle", 1.0),
                            new KeyValuePair<string, double>("pet", 1.0),
                            new KeyValuePair<string, double>("pet", 2.2),
                            new KeyValuePair<string, double>("feed", 1.25),
                            new KeyValuePair<string, double>("walk", .12),
                            new KeyValuePair<string, double>("walk", .31),
                            new KeyValuePair<string, double>("walk", .60),
                            new KeyValuePair<string, double>("play", .67),
                            new KeyValuePair<string, double>("sleep", 2.4),
                            new KeyValuePair<string, double>("idle", .45),
                            new KeyValuePair<string, double>("idle", 2.4) })
                        {
                        Advance(model, pose.Key, pose.Value, .042);
                        typeof(PetWindow).GetMethod("UpdateGeneratedHitArea", Private).Invoke(pet, null);
                        Viewport3D reference = PetWindow.CreateViewport(model);
                        Model3DGroup silhouette = new Model3DGroup(); silhouette.Children.Add(model.GeneratedPet.Model);
                        foreach (Model3D part in model.Scene.Children) if (part is Light) silhouette.Children.Add(part);
                        reference.Children.Clear(); reference.Children.Add(new ModelVisual3D { Content = silhouette });
                        byte[] pixels = Pixels(Render(reference, 306, 260));
                        foreach (double scale in new[] { .4, .57, 1.0, 1.6 })
                        {
                            pet.Width = 320 * scale; pet.Height = 350 * scale; pet.UpdateLayout();
                            int opaque = 0, hitOpaque = 0, empty = 0, hitEmpty = 0;
                            for (int y = 8; y < 252; y += 12)
                            for (int x = 8; x < 298; x += 12)
                            {
                                bool solid = UniformAlpha(pixels, 306, x, y, 4, true), blank = UniformAlpha(pixels, 306, x, y, 7, false);
                                if (!solid && !blank) continue;
                                bool hit = ReferenceEquals(pet.InputHitTest(input.TranslatePoint(new Point(x, y), pet)), input);
                                if (solid) { opaque++; if (hit) hitOpaque++; }
                                if (blank) { empty++; if (hit) hitEmpty++; }
                            }
                            File.AppendAllText(Path.Combine(Output, "input-coverage.csv"), pose.Key + "," + pose.Value.ToString(CultureInfo.InvariantCulture) + "," + angle.ToString(CultureInfo.InvariantCulture) + "," + scale.ToString(CultureInfo.InvariantCulture) + "," + opaque + "," + hitOpaque + "," + empty + "," + hitEmpty + "\r\n");
                            Require(opaque > 20 && hitOpaque >= opaque * .95, "New dog silhouette misses opaque production mesh at " + pose.Key + "/" + angle + "/" + scale + ": " + hitOpaque + "/" + opaque);
                            Require(empty > 20 && hitEmpty <= empty * .10, "New silhouette blocks transparent desktop space at " + pose.Key + "/" + angle + "/" + scale + ": " + hitEmpty + "/" + empty);
                        }
                        }
                    }
                    Set(pet, "heading", -68.0); Set(pet, "targetHeading", 52.0);
                    typeof(PetWindow).GetMethod("SetState", Private).Invoke(pet, new object[] { "play", 6.0 });
                    Require(Math.Abs(Field<double>(pet, "targetHeading") + 68) < .00001,
                        "Playing from a walk retained an active turn, which would move the ball before contact.");
                }
                catch (Exception error) { failure = error; }
                finally { pet.Close(); app.Shutdown(); }
            }));
        };
        pet.Show(); app.Run();
        if (failure != null) throw failure;
        Pass("production 2D pointer covers idle/pet/feed/walk/play/sleep and waking mesh at four scales and both headings, clears empty margins, and freezes play heading; verify-only offscreen window, no real settings writes");
    }
    static bool UniformAlpha(byte[] pixels, int width, int x, int y, int radius, bool solid)
    {
        for (int dy = -radius; dy <= radius; dy += radius)
        for (int dx = -radius; dx <= radius; dx += radius)
        { int alpha = pixels[((y + dy) * width + x + dx) * 4 + 3]; if (solid ? alpha < 240 : alpha > 0) return false; }
        return true;
    }

    static void CheckMesh(Model3D scene, string name)
    {
        Model3DGroup group = scene as Model3DGroup;
        if (group != null) { foreach (Model3D part in group.Children) CheckMesh(part, name); return; }
        GeometryModel3D geometry = scene as GeometryModel3D; MeshGeometry3D mesh = geometry == null ? null : geometry.Geometry as MeshGeometry3D;
        if (mesh == null) return;
        foreach (Point3D p in mesh.Positions) Require(Finite(p.X) && Finite(p.Y) && Finite(p.Z), name + ": non-finite vertex.");
        foreach (Vector3D n in mesh.Normals) Require(Finite(n.X) && Finite(n.Y) && Finite(n.Z), name + ": non-finite normal.");
        foreach (int index in mesh.TriangleIndices) Require(index >= 0 && index < mesh.Positions.Count, name + ": invalid triangle index.");
        Require(mesh.TriangleIndices.Count % 3 == 0, name + ": incomplete triangle.");
    }
    static RenderTargetBitmap Render(Viewport3D view, int width, int height)
    {
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view); return bitmap;
    }
    static byte[] Pixels(BitmapSource bitmap)
    {
        FormatConvertedBitmap converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }
    static void RequireMargins(byte[] pixels, int width, int height, string name)
    {
        int visible = 0, left = width, top = height, right = 0, bottom = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            if (pixels[(y * width + x) * 4 + 3] < 32) continue;
            visible++; left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        Require(visible > width * height * .05, name + ": dog is invisible or too small.");
        Require(left >= 2 && top >= 2 && right < width - 2 && bottom < height - 2,
            name + ": actual dog/prop clipped at viewport edge " + left + "," + top + "," + right + "," + bottom);
    }
    static void Save(BitmapSource bitmap, string name)
    {
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (Stream stream = File.Create(Path.Combine(Output, name + ".png"))) encoder.Save(stream);
    }
    static Point Project(Point3D world, OrthographicCamera camera, double width, double height)
    {
        Vector3D forward = camera.LookDirection; forward.Normalize();
        Vector3D right = Vector3D.CrossProduct(forward, camera.UpDirection); right.Normalize();
        Vector3D up = Vector3D.CrossProduct(right, forward); up.Normalize();
        Vector3D relative = world - camera.Position;
        return new Point(width / 2 + Vector3D.DotProduct(relative, right) / camera.Width * width,
            height / 2 - Vector3D.DotProduct(relative, up) / camera.Width * width);
    }
}
