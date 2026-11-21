using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Xml.Linq;
using WestiePet;

// Offscreen model renders and an offscreen verify-only production window.
// Preferences are backed up and restored only beside this executable in qa/.
static class BreedChecks
{
    const int Width = 512, Height = 448;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "breeds");

    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static T Field<T>(object target, string name)
    { return (T)target.GetType().GetField(name, Private).GetValue(target); }
    static void Set(object target, string name, object value)
    { target.GetType().GetField(name, Private).SetValue(target, value); }
    static bool Finite(double value)
    { return !double.IsInfinity(value) && !double.IsNaN(value); }

    [STAThread]
    static int Main()
    {
        try
        {
            Directory.CreateDirectory(Output);
            CheckEmbeddedArtwork();
            CheckRendering();
            CheckCorgiFace();
            CheckSettingsAndSwitching();
            File.WriteAllText(Path.Combine(Output, "result.txt"),
                "PASS: Westie six-state 3D and HD 2.5D Corgi five-state seated rendering; transparent margins; " +
                "exact HD embedded alpha artwork; finite meshes and visible restrained local artwork animation; " +
                "Corgi eye-aperture mesh compression with fixed open-eye regions and unchanged artwork colors; " +
                "instant model and preview switching; unchanged-model reuse; state and personal values preserved; " +
                "Corgi pointer silhouettes at four scales and both headings; real breed menu click handlers; " +
                "saved selection, legacy settings and invalid-model fallback.\r\n");
            Console.WriteLine("PASS: breed rendering, switching and isolated settings: " + Output);
            return 0;
        }
        catch (Exception error) { Console.WriteLine("BREED_CHECKS_ERROR: " + error); return 1; }
    }

    static BitmapSource embeddedArtwork;
    static byte[] embeddedPixels;
    static int artworkWidth, artworkHeight;

    static void CheckEmbeddedArtwork()
    {
        byte[] resource;
        using (Stream stream = typeof(WestieModel).Assembly.GetManifestResourceStream("WestiePet.CorgiArtwork"))
        {
            Require(stream != null, "The HD artwork must be an embedded application resource.");
            using (MemoryStream memory = new MemoryStream()) { stream.CopyTo(memory); resource = memory.ToArray(); }
        }
        using (MemoryStream memory = new MemoryStream(resource))
        {
            BitmapDecoder decoder = BitmapDecoder.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            embeddedArtwork = decoder.Frames[0]; embeddedArtwork.Freeze();
        }
        artworkWidth = embeddedArtwork.PixelWidth; artworkHeight = embeddedArtwork.PixelHeight;
        Require(artworkWidth >= 1024 && artworkHeight >= 1024, "Embedded artwork must preserve HD resolution.");
        embeddedPixels = FacePixels(embeddedArtwork);
        int opaque = 0, transparent = 0, feathered = 0;
        for (int i = 3; i < embeddedPixels.Length; i += 4)
        {
            if (embeddedPixels[i] > 240) opaque++;
            else if (embeddedPixels[i] == 0) transparent++;
            else feathered++;
        }
        Require(opaque > 10000 && transparent > 10000 && feathered > 100, "HD artwork needs a real alpha silhouette and feathered fur edges.");
        string asset = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)), "assets", "corgi-hd.png");
        Require(File.Exists(asset), "The source HD artwork must exist in assets.");
        using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
        using (FileStream stream = File.OpenRead(asset))
            Require(Convert.ToBase64String(hash.ComputeHash(resource)) == Convert.ToBase64String(hash.ComputeHash(stream)),
                "The embedded PNG must be byte-identical to the unedited HD source asset.");
        Console.WriteLine("PASS: exact embedded HD artwork, {0}x{1}, transparent silhouette and antialiased fur.", artworkWidth, artworkHeight);
    }

    static Grid Stage(WestieModel model, out Viewport3D view)
    {
        view = PetWindow.CreateViewport(model);
        Grid stage = new Grid { Width = Width, Height = Height, Background = Brushes.Transparent };
        stage.Children.Add(view); stage.Measure(new Size(Width, Height));
        stage.Arrange(new Rect(0, 0, Width, Height)); stage.UpdateLayout(); return stage;
    }

    static void CheckRendering()
    {
        WestieModel westie = new WestieModel("westie");
        Require(!westie.IsPreRendered, "Westie must retain the existing articulated 3D model.");
        Viewport3D view; Grid stage = Stage(westie, out view); double time = 0;
        Advance(westie, "idle", 2, ref time);
        foreach (KeyValuePair<string, double> angle in new[] {
            new KeyValuePair<string, double>("front", 0),
            new KeyValuePair<string, double>("side", 90),
            new KeyValuePair<string, double>("threequarter", 52) })
        {
            westie.Heading.Angle = angle.Value; Save(stage, "westie-" + angle.Key);
        }
        byte[] idle = null, sleep = null;
        foreach (string state in new[] { "idle", "walk", "pet", "feed", "play", "sleep" })
        {
            westie.Heading.Angle = state == "walk" ? -68 : 52;
            Advance(westie, "idle", 1.8, ref time);
            Advance(westie, state, state == "sleep" ? 2.4 : 1.4, ref time);
            CheckMesh(westie.Scene, "westie-" + state);
            byte[] pixels = Save(stage, "westie-" + state);
            if (state == "idle") idle = pixels;
            if (state == "sleep") sleep = pixels;
            if (state == "walk")
            {
                for (int tick = 0; tick < 6; tick++) { time += 1.0 / 30; westie.Animate(time, state, 1.4 + (tick + 1) / 30.0); }
                Require(Difference(pixels, Save(stage, "westie-walk-next")) > 80, "Westie walking must visibly animate.");
            }
        }
        Require(Difference(idle, sleep) > 1000, "Westie must retain its visible lying sleep pose.");
        Console.WriteLine("PASS: unchanged Westie six-state 3D rendering and walking/sleeping checks.");

        WestieModel corgi = new WestieModel("corgi");
        Require(corgi.IsPreRendered, "HD Corgi must identify its 2.5D artwork presentation.");
        CorgiSprite sprite = Field<CorgiSprite>(corgi, "artwork");
        Require(sprite != null && Field<BitmapSource>(sprite, "artwork").PixelWidth == artworkWidth,
            "The Corgi scene must actually use the embedded HD artwork.");
        GeometryModel3D artworkSurface = sprite.Model.Children[0] as GeometryModel3D;
        MaterialGroup artworkMaterial = artworkSurface == null ? null : artworkSurface.Material as MaterialGroup;
        EmissiveMaterial artworkEmission = artworkMaterial == null || artworkMaterial.Children.Count < 2 ? null
            : artworkMaterial.Children[1] as EmissiveMaterial;
        ImageBrush artworkTexture = artworkEmission == null ? null : artworkEmission.Brush as ImageBrush;
        Require(sprite.Model.Children.Count == 1 && artworkTexture != null && artworkTexture.ImageSource is CroppedBitmap,
            "Corgi eyes must deform the original image mesh; no painted eyelid layers or composite textures are allowed.");
        stage = Stage(corgi, out view); time = 0;
        foreach (double heading in new[] { -52.0, 52.0 })
        {
            corgi.Heading.Angle = heading; Advance(corgi, "idle", 1.2, ref time);
            Save(stage, heading < 0 ? "corgi-facing-left" : "corgi-facing-right");
        }
        foreach (string state in new[] { "idle", "pet", "feed", "play", "sleep" })
        {
            corgi.Heading.Angle = 52; Advance(corgi, "idle", 1.2, ref time);
            double duration = state == "sleep" ? 2.4 : state == "pet" ? 1 : state == "play" ? .6 : 1.4;
            Advance(corgi, state, duration, ref time);
            CheckMesh(corgi.Scene, "corgi-" + state);
            byte[] before = Save(stage, "corgi-" + state);
            MeshGeometry3D surface = (MeshGeometry3D)((GeometryModel3D)sprite.Model.Children[0]).Geometry;
            Point3D[] positions = new Point3D[surface.Positions.Count]; surface.Positions.CopyTo(positions, 0);
            for (int tick = 0; tick < 8; tick++) { time += 1.0 / 30; corgi.Animate(time, state, duration + tick / 30.0); }
            byte[] after = Save(stage, "corgi-" + state + "-next");
            double movement = 0;
            for (int i = 0; i < positions.Length; i++) movement = Math.Max(movement, (positions[i] - surface.Positions[i]).Length);
            Require(movement > .00001 && movement < .12, "Corgi " + state + ": expected restrained local artwork deformation.");
            Require(Difference(before, after) > 8, "Corgi " + state + ": local animation must be visible in actual pixels.");
            Console.WriteLine("Corgi {0}: local movement {1:F5}, changed pixels {2}.", state, movement, Difference(before, after));
        }
        Require(sprite.Bounds.SizeY > 1.5 && sprite.Bounds.SizeY < 2.2, "The seated artwork must retain its intended visible height.");
        Console.WriteLine("PASS: HD 2.5D Corgi facing mirrors and five seated interaction states; no invented quadruped walk or lying frames.");
    }

    static void Advance(WestieModel model, string state, double duration, ref double time)
    {
        model.BeginAction(state);
        int count = (int)Math.Round(duration * 30);
        for (int i = 0; i < count; i++) { time += 1.0 / 30; model.Animate(time, state, i / 30.0); }
    }

    sealed class FaceMetrics
    {
        internal string State;
        internal double Closure;
        internal double[] HeightRatio = new double[2];
        internal int[] Changed = new int[2], OpenDark = new int[2], ClosedDark = new int[2],
            OpenBright = new int[2], ClosedBright = new int[2];
    }

    static Point3D ArtworkPoint(CorgiSprite sprite, Point source)
    {
        Point3D local = (Point3D)typeof(CorgiSprite).GetMethod("Deform", Private)
            .Invoke(sprite, new object[] { source.X, source.Y, 0.0 });
        return sprite.Model.Transform.Transform(local);
    }

    static Point SourcePoint(double x, double y)
    { return new Point(x * artworkWidth / 1920.0, y * artworkHeight / 1920.0); }

    static Point Project(Point3D point, OrthographicCamera camera, double width, double height)
    {
        Vector3D forward = camera.LookDirection; forward.Normalize();
        Vector3D right = Vector3D.CrossProduct(forward, camera.UpDirection); right.Normalize();
        Vector3D up = Vector3D.CrossProduct(right, forward); up.Normalize();
        Vector3D delta = point - camera.Position; double pixels = width / camera.Width;
        return new Point(width * .5 + Vector3D.DotProduct(delta, right) * pixels,
            height * .5 - Vector3D.DotProduct(delta, up) * pixels);
    }

    static HashSet<int> EyePixels(CorgiSprite sprite, OrthographicCamera camera, int eye, bool upperOnly)
    {
        // Always call with closure=0, freezing the original aperture on screen.
        // Measuring the compressed geometry's tiny ROI would hide a failure.
        // The inset polygon excludes the source image's white muzzle fur.
        Rect extent = eye == 0 ? new Rect(582, 675, 84, 58) : new Rect(724, 628, 114, 72);
        Point[] aperture = eye == 0 ? new[] {
            new Point(590,710), new Point(600,701), new Point(619,690), new Point(639,685),
            new Point(656,685), new Point(652,695), new Point(642,708), new Point(627,718),
            new Point(609,722), new Point(596,718)
        } : new[] {
            new Point(734,660), new Point(747,650), new Point(771,641), new Point(795,638),
            new Point(825,638), new Point(819,654), new Point(807,668), new Point(788,679),
            new Point(765,685), new Point(744,681), new Point(734,672)
        };
        HashSet<int> pixels = new HashSet<int>();
        for (int y = (int)extent.Top; y < extent.Bottom; y++)
        for (int x = (int)extent.Left; x < extent.Right; x++)
        {
            if (!InsidePolygon(new Point(x + .5, y + .5), aperture)) continue;
            double angle = eye == 0 ? -.48 : -.31;
            double dx = x + .5 - (eye == 0 ? 623 : 780), dy = y + .5 - (eye == 0 ? 703 : 663);
            double localVertical = -dx * Math.Sin(angle) + dy * Math.Cos(angle);
            // Upper eyelid fur should replace exposed eye white. The lower
            // eyelid may naturally pull white cheek fur upwards, so that fur
            // cannot serve as a detector for a painted white eye-mask defect.
            if (upperOnly && localVertical >= -2.5) continue;
            Point projected = Project(ArtworkPoint(sprite, SourcePoint(x + .5, y + .5)), camera, 512, 512);
            int px = (int)Math.Round(projected.X), py = (int)Math.Round(projected.Y);
            if (px >= 0 && py >= 0 && px < 512 && py < 512) pixels.Add((py * 512 + px) * 4);
        }
        Require(pixels.Count >= (upperOnly ? 30 : 100), "The actual artwork eye aperture must remain visible in the closeup.");
        return pixels;
    }

    static double EyeSpan(CorgiSprite sprite, int eye)
    {
        double x = eye == 0 ? 623 : 780, y = eye == 0 ? 703 : 663;
        double height = eye == 0 ? 18 : 22, angle = eye == 0 ? -.48 : -.31;
        Point a = SourcePoint(x - Math.Sin(angle) * height, y + Math.Cos(angle) * height);
        Point b = SourcePoint(x + Math.Sin(angle) * height, y - Math.Cos(angle) * height);
        return (ArtworkPoint(sprite, a) - ArtworkPoint(sprite, b)).Length;
    }

    static bool InsidePolygon(Point point, Point[] polygon)
    {
        bool inside = false;
        for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
        {
            Point a = polygon[i], b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    static void CheckCorgiFace()
    {
        WestieModel model = new WestieModel("corgi"); model.Heading.Angle = 52;
        CorgiSprite sprite = Field<CorgiSprite>(model, "artwork");
        Viewport3D view = PetWindow.CreateViewport(model);
        File.WriteAllText(Path.Combine(Output, "corgi-face-metrics.csv"),
            "state,eye,eyeClosure,eyeHeightRatio,changedEyePixels,openDark,closedDark,openUpperBright,closedUpperBright\r\n");
        List<FaceMetrics> samples = new List<FaceMetrics>(); double time = 0;
        foreach (string state in new[] { "idle", "pet", "sleep" })
        {
            Advance(model, state, state == "sleep" ? 2.4 : state == "pet" ? 1 : 2, ref time);
            samples.Add(MeasureCorgiFace(view, model, sprite, state));
        }
        // Collect every requested closeup before any coverage assertion so a
        // failed eye always leaves all three states available for visual review.
        foreach (FaceMetrics sample in samples)
        for (int eye = 0; eye < 2; eye++)
        {
            Require(sample.OpenDark[eye] >= 12, "Corgi " + sample.State + ": closeup must include the real source eye.");
            if (sample.State == "idle")
                Require(sample.Closure < .05 && sample.Changed[eye] <= 3 && Math.Abs(sample.HeightRatio[eye] - 1) < .01,
                    "Idle artwork must preserve the original eye texture and aperture geometry.");
            else
            {
                Require(sample.Closure > .8 && sample.Changed[eye] >= 15 && sample.HeightRatio[eye] < .04,
                    "The eye aperture must actually compress to a thin line at the same bone pose.");
                Require(sample.ClosedDark[eye] <= Math.Max(8, sample.OpenDark[eye] * .25),
                    "Corgi " + sample.State + ": original pupil remains visible in the fixed open-eye region.");
                Require(sample.ClosedBright[eye] <= Math.Max(8, sample.OpenBright[eye] * .30),
                    "Corgi " + sample.State + ": exposed upper eye white remains visible after mesh closure.");
            }
        }
        Console.WriteLine("PASS: HD face idle/pet/sleep closeups; true mesh eye compression; fixed open-eye ROI; no repainted eyelids.");
    }

    static FaceMetrics MeasureCorgiFace(Viewport3D view, WestieModel model, CorgiSprite sprite, string state)
    {
        double closure = sprite.EyeClosure;
        FaceMetrics measured = new FaceMetrics { State = state, Closure = closure };
        HashSet<int>[] regions = new HashSet<int>[2], upperRegions = new HashSet<int>[2];
        double[] openHeight = new double[2];
        try
        {
            // SetEyeClosure refreshes vertices only. Bone pose, time, heading,
            // source UVs and source image stay identical across the comparison.
            sprite.SetEyeClosure(0);
            Point3D center = ArtworkPoint(sprite, SourcePoint(700, 725));
            OrthographicCamera camera = new OrthographicCamera(new Point3D(center.X, center.Y, 6),
                new Vector3D(0, 0, -6), new Vector3D(0, 1, 0), .78);
            view.Camera = camera;
            for (int eye = 0; eye < 2; eye++)
            {
                regions[eye] = EyePixels(sprite, camera, eye, false);
                upperRegions[eye] = EyePixels(sprite, camera, eye, true);
                openHeight[eye] = EyeSpan(sprite, eye);
            }
            RenderTargetBitmap openedBitmap = RenderCorgiFace(view);
            byte[] opened = FacePixels(openedBitmap);
            if (state != "idle") SaveFaceBitmap(openedBitmap, "corgi-face-" + state + "-open");

            sprite.SetEyeClosure(closure);
            CheckMesh(model.Scene, "corgi-face-" + state);
            RenderTargetBitmap normal = RenderCorgiFace(view); byte[] pixels = FacePixels(normal);
            SaveFaceBitmap(normal, "corgi-face-" + state);
            for (int eye = 0; eye < 2; eye++)
            {
                measured.HeightRatio[eye] = EyeSpan(sprite, eye) / openHeight[eye];
                foreach (int i in regions[eye])
                {
                    if (PixelDifferent(pixels, opened, i)) measured.Changed[eye]++;
                    if (DeepDark(opened, i)) measured.OpenDark[eye]++;
                    if (DeepDark(pixels, i)) measured.ClosedDark[eye]++;
                }
                foreach (int i in upperRegions[eye])
                {
                    if (EyeBright(opened, i)) measured.OpenBright[eye]++;
                    if (EyeBright(pixels, i)) measured.ClosedBright[eye]++;
                }
                File.AppendAllText(Path.Combine(Output, "corgi-face-metrics.csv"), state + "," + eye + "," +
                    closure.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    measured.HeightRatio[eye].ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                    measured.Changed[eye] + "," + measured.OpenDark[eye] + "," + measured.ClosedDark[eye] + "," +
                    measured.OpenBright[eye] + "," + measured.ClosedBright[eye] + "\r\n");
            }
        }
        finally { sprite.SetEyeClosure(closure); }
        return measured;
    }

    static void SaveFaceBitmap(BitmapSource bitmap, string name)
    {
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(Path.Combine(Output, name + ".png"))) encoder.Save(stream);
    }

    static bool DeepDark(byte[] p, int i)
    { return p[i + 3] >= 224 && p[i + 2] * .2126 + p[i + 1] * .7152 + p[i] * .0722 <= 50; }
    static bool EyeBright(byte[] p, int i)
    { return p[i + 3] >= 224 && p[i + 2] > 215 && p[i + 1] > 195 && p[i] > 135; }
    static bool PixelDifferent(byte[] a, byte[] b, int i)
    { return a[i + 3] >= 224 && (Math.Abs(a[i] - b[i]) > 18 || Math.Abs(a[i + 1] - b[i + 1]) > 18 || Math.Abs(a[i + 2] - b[i + 2]) > 18); }

    static RenderTargetBitmap RenderCorgiFace(Viewport3D view)
    {
        const int size = 512;
        view.Measure(new Size(size, size)); view.Arrange(new Rect(0, 0, size, size)); view.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); return bitmap;
    }

    static byte[] FacePixels(BitmapSource source)
    {
        BitmapSource straight = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        straight.CopyPixels(pixels, source.PixelWidth * 4, 0); return pixels;
    }

    static byte[] Save(Grid stage, string name)
    {
        RenderTargetBitmap bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        byte[] pixels = new byte[Width * Height * 4]; bitmap.CopyPixels(pixels, Width * 4, 0);
        // Keep the exact failing frame available for visual diagnosis.
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(Path.Combine(Output, name + ".png"))) encoder.Save(stream);
        int visible = 0, transparent = 0;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int alpha = pixels[(y * Width + x) * 4 + 3];
            if (alpha == 0) transparent++;
            if (alpha <= 24) continue;
            visible++;
            Require(x >= 3 && y >= 3 && x < Width - 3 && y < Height - 3,
                name + ": model reaches image edge at " + x + "," + y + ".");
        }
        Require(visible > 2000 && transparent > 2000, name + ": expected visible model on a transparent background.");
        return pixels;
    }

    static int WarmPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 200 && pixels[i + 2] > pixels[i + 1] + 15 && pixels[i + 1] > pixels[i] + 15) count++;
        return count;
    }

    static int Difference(byte[] a, byte[] b)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) +
                Math.Abs(a[i + 3] - b[i + 3]) > 40) count++;
        return count;
    }

    static void CheckMesh(Model3D node, string name)
    {
        Rect3D bounds = node.Bounds;
        if (!(node is Light)) Require(!bounds.IsEmpty && Finite(bounds.X) && Finite(bounds.Y) && Finite(bounds.Z)
            && Finite(bounds.SizeX) && Finite(bounds.SizeY) && Finite(bounds.SizeZ), name + ": nonfinite bounds.");
        Model3DGroup group = node as Model3DGroup;
        if (group != null) foreach (Model3D child in group.Children) CheckMesh(child, name);
        GeometryModel3D geometry = node as GeometryModel3D;
        MeshGeometry3D mesh = geometry == null ? null : geometry.Geometry as MeshGeometry3D;
        if (mesh == null) return;
        foreach (Point3D p in mesh.Positions) Require(Finite(p.X) && Finite(p.Y) && Finite(p.Z), name + ": nonfinite mesh point.");
        foreach (Vector3D n in mesh.Normals) Require(Finite(n.X) && Finite(n.Y) && Finite(n.Z), name + ": nonfinite mesh normal.");
    }

    static void CheckSettingsAndSwitching()
    {
        string directory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        Require(string.Equals(Path.GetFileName(directory), "qa", StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(Path.GetDirectoryName(directory), "build.ps1")),
            "Run BreedChecks.exe from the project's qa directory to isolate settings.");
        string path = Path.Combine(directory, "小白.settings.xml"), temp = path + ".tmp";
        byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        byte[] previousTemp = File.Exists(temp) ? File.ReadAllBytes(temp) : null;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            Require(PetSettings.Load().Breed == "westie", "Absent settings must default to Westie.");
            Require(PetSettings.ReadBreed(null) == "westie" && PetSettings.ReadBreed("unknown") == "westie"
                && PetSettings.ReadBreed("corgi") == "corgi", "Breed parser must reject missing and unknown values.");
            new XElement("westie", new XElement("name", "旧设置")).Save(path);
            Require(PetSettings.Load().Breed == "westie" && PetSettings.Load().Name == "旧设置", "Legacy settings must remain compatible.");
            new XElement("westie", new XElement("breed", "invalid")).Save(path);
            Require(PetSettings.Load().Breed == "westie", "Unknown stored model must fall back safely.");
            PetSettings stored = new PetSettings { Breed = "corgi", Name = "柯基朋友", Affection = 7,
                Treats = 9, Size = .57, Left = 123, Top = 234, Roam = false };
            stored.Save();
            PetSettings loaded = PetSettings.Load();
            Require(loaded.Breed == "corgi" && loaded.Name == stored.Name && loaded.Affection == 7 && loaded.Treats == 9
                && loaded.Left == 123 && loaded.Top == 234 && loaded.Size == .57, "Saved Corgi selection must roundtrip with existing settings.");
            Require(new WestieModel("invalid").Breed == "westie", "Direct invalid model construction must fall back safely.");
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            PetWindow pet = new PetWindow(loaded, true, true, false);
            try
            {
                pet.Show(); pet.UpdateLayout();
                // Stay offscreen, never expose a tray icon, and never run a timer.
                pet.Left = SystemParameters.VirtualScreenLeft - pet.Width - 100; pet.Top = 120;
                double left = pet.Left, top = pet.Top, width = pet.Width, height = pet.Height;
                WestieModel first = Field<WestieModel>(pet, "model");
                Require(first.Breed == "corgi", "PetWindow must honor saved breed on construction.");
                CheckCorgiInput(pet, loaded);
                Viewport3D preview = PetWindow.CreateViewport(first);
                Set(pet, "homePreview", preview);
                Set(pet, "mode", "sleep");
                pet.SwitchBreed("corgi");
                Require(ReferenceEquals(first, Field<WestieModel>(pet, "model")), "Selecting the current breed must not rebuild the model.");
                foreach (string breed in new[] { "westie", "corgi", "invalid" })
                {
                    pet.SwitchBreed(breed);
                    WestieModel current = Field<WestieModel>(pet, "model");
                    string expected = breed == "corgi" ? "corgi" : "westie";
                    Require(current.Breed == expected && loaded.Breed == expected, "Switch did not normalize the selected model.");
                    Viewport3D viewport = Field<Viewport3D>(pet, "viewport");
                    Require(ReferenceEquals(((ModelVisual3D)viewport.Children[0]).Content, current.Scene)
                        && ReferenceEquals(((ModelVisual3D)preview.Children[0]).Content, current.Scene), "Desktop and home preview must use the selected scene.");
                    Require(Field<string>(pet, "mode") == "sleep", "Model switching must preserve the current state.");
                    Require(loaded.Name == "柯基朋友" && Field<int>(pet, "affection") == 7 && Field<int>(pet, "treats") == 9
                        && loaded.Affection == 7 && loaded.Treats == 9 && loaded.Size == .57
                        && pet.Left == left && pet.Top == top && pet.Width == width && pet.Height == height,
                        "Switching must preserve name, counters, size and window position.");
                }
                CheckBreedMenu(pet, loaded, preview);
                CheckArtworkRoam(pet, loaded);
                Console.WriteLine("PASS: saved-breed construction, scene/preview synchronization, current-model reuse and personal/state preservation.");
            }
            finally { pet.Close(); app.Shutdown(); }
            Console.WriteLine("PASS: model settings roundtrip and missing/unknown-value fallback in isolated qa directory.");
        }
        finally
        {
            Restore(path, previous); Restore(temp, previousTemp);
        }
    }

    static int SourceAlpha(Point point)
    {
        int x = (int)Math.Round(point.X), y = (int)Math.Round(point.Y);
        return x < 0 || y < 0 || x >= artworkWidth || y >= artworkHeight ? 0 : embeddedPixels[(y * artworkWidth + x) * 4 + 3];
    }

    static void CheckCorgiInput(PetWindow pet, PetSettings settings)
    {
        Viewport3D viewport = Field<Viewport3D>(pet, "viewport");
        SilhouetteHitArea input = Field<SilhouetteHitArea>(pet, "pointerHitArea");
        WestieModel model = Field<WestieModel>(pet, "model");
        CorgiSprite sprite = Field<CorgiSprite>(model, "artwork");
        Require(!viewport.IsHitTestVisible && model.IsPreRendered, "HD Corgi must use the CPU alpha input surface.");
        double width = pet.Width, height = pet.Height;
        double heading = Field<double>(pet, "heading"), target = Field<double>(pet, "targetHeading");
        double until = Field<double>(pet, "stateUntil"); bool autoHide = settings.AutoHideFullscreen;
        // Landmarks refer to the actual unedited HD PNG, not the old SDF shape.
        Point[] solid = { SourcePoint(1000,260), SourcePoint(520,490), SourcePoint(850,800),
            SourcePoint(900,1250), SourcePoint(925,1711), SourcePoint(1490,1370) };
        Point[] empty = { SourcePoint(380,300), SourcePoint(1700,900), SourcePoint(500,1500), SourcePoint(1290,600) };
        foreach (Point point in solid) Require(SourceAlpha(point) > 200, "A named HD artwork landmark must be opaque: " + point);
        foreach (Point point in empty) Require(SourceAlpha(point) < 8, "An HD transparent test landmark must be empty: " + point);
        try
        {
            settings.AutoHideFullscreen = false; Set(pet, "stateUntil", double.PositiveInfinity);
            foreach (bool leftFacing in new[] { false, true })
            {
                double facing = leftFacing ? -52 : 52;
                Set(pet, "heading", facing); Set(pet, "targetHeading", facing);
                typeof(PetWindow).GetMethod("Tick", Private).Invoke(pet, new object[] { null, EventArgs.Empty });
                OrthographicCamera camera = (OrthographicCamera)viewport.Camera;
                foreach (double scale in new[] { .4, .57, 1.0, 1.6 })
                {
                    pet.Width = 320 * scale; pet.Height = 350 * scale; pet.UpdateLayout();
                    foreach (Point source in solid)
                    {
                        Point3D world = ArtworkPoint(sprite, source); Point local = Project(world, camera, 306, 260);
                        Require(model.ArtworkHitTest(world.X, world.Y), "The sprite alpha sampler missed an opaque HD source landmark.");
                        Require(ReferenceEquals(pet.InputHitTest(input.TranslatePoint(local, pet)), input),
                            "HD ear/face/body/paw/tail missed actual input at scale " + scale + ", " + local + ".");
                    }
                    foreach (Point source in empty)
                    {
                        Point3D world = ArtworkPoint(sprite, source); Point local = Project(world, camera, 306, 260);
                        Require(!model.ArtworkHitTest(world.X, world.Y), "The sprite alpha sampler hit a transparent HD source landmark.");
                        Require(!ReferenceEquals(pet.InputHitTest(input.TranslatePoint(local, pet)), input),
                            "Transparent HD artwork margin intercepted desktop input at scale " + scale + ", " + local + ".");
                    }
                }
            }
        }
        finally
        {
            pet.Width = width; pet.Height = height; pet.UpdateLayout();
            Set(pet, "heading", heading); Set(pet, "targetHeading", target); Set(pet, "stateUntil", until);
            model.Heading.Angle = heading; model.Animate(0, "idle", 0);
            typeof(PetWindow).GetMethod("UpdateBreedHitArea", Private).Invoke(pet, null);
            settings.AutoHideFullscreen = autoHide;
        }
        Console.WriteLine("PASS: real PNG alpha input at 40%, 57%, 100%, 160%, both mirrored headings, fur/body/paw/tail and transparent margins; 3D input remains off.");
    }

    static void CheckArtworkRoam(PetWindow pet, PetSettings settings)
    {
        bool roam = settings.Roam, autoHide = settings.AutoHideFullscreen;
        double until = Field<double>(pet, "stateUntil"), next = Field<double>(pet, "nextAction");
        string mode = Field<string>(pet, "mode"); double left = pet.Left, top = pet.Top;
        try
        {
            settings.Roam = true; settings.AutoHideFullscreen = false;
            Set(pet, "mode", "idle"); Set(pet, "stateUntil", double.PositiveInfinity); Set(pet, "nextAction", -1.0);
            typeof(PetWindow).GetMethod("Tick", Private).Invoke(pet, new object[] { null, EventArgs.Empty });
            Require(Field<string>(pet, "mode") == "idle" && pet.Left == left && pet.Top == top && settings.Roam,
                "Seated HD artwork must not roam or erase the saved Westie roaming preference.");
            typeof(PetWindow).GetMethod("Act", Private).Invoke(pet, new object[] { "walk" });
            Require(Field<string>(pet, "mode") != "walk" && settings.Roam, "Manual walk must not slide the seated artwork or clear the preference.");
            pet.SwitchBreed("westie");
            Require(!Field<WestieModel>(pet, "model").IsPreRendered && settings.Roam, "Westie must recover the retained roaming preference.");
            Set(pet, "mode", "walk"); pet.SwitchBreed("corgi");
            Require(Field<string>(pet, "mode") == "idle" && settings.Roam,
                "Switching from a walking Westie must stop the seated artwork without deleting the preference.");
        }
        finally
        {
            settings.Roam = roam; settings.AutoHideFullscreen = autoHide;
            Set(pet, "mode", mode); Set(pet, "stateUntil", until); Set(pet, "nextAction", next);
        }
        Console.WriteLine("PASS: seated 2.5D artwork cannot roam; saved Westie roam preference survives model switches.");
    }

    static void CheckBreedMenu(PetWindow pet, PetSettings settings, Viewport3D preview)
    {
        // Exercise the production handlers rather than duplicating them on a
        // synthetic selector. This selector checks SwitchBreed's control sync.
        ComboBox choice = new ComboBox { ItemsSource = new[] { "西高地", "柯基" },
            SelectedIndex = settings.Breed == "corgi" ? 1 : 0 };
        Set(pet, "breedChoice", choice);
        try
        {
            foreach (string desired in new[] { "corgi", "westie", "corgi" })
            {
                typeof(PetWindow).GetMethod("OpenMenu", Private).Invoke(pet, null);
                ContextMenu menu = Field<ContextMenu>(pet, "petContextMenu");
                Require(menu != null, "The production context menu did not open.");
                // Its owner stays offscreen, and menu tests need no user input.
                menu.IsOpen = false;
                MenuItem models = null;
                foreach (object entry in menu.Items)
                {
                    MenuItem candidate = entry as MenuItem;
                    if (candidate != null && string.Equals(candidate.Header as string, "宠物模型", StringComparison.Ordinal))
                        models = candidate;
                }
                Require(models != null && models.Items.Count == 2, "The context menu needs the two-item 宠物模型 submenu.");
                MenuItem westie = models.Items[0] as MenuItem, corgi = models.Items[1] as MenuItem;
                Require(westie != null && corgi != null && (westie.Header as string) == "西高地"
                    && (corgi.Header as string) == "柯基", "Breed submenu labels or order are wrong.");
                Require(westie.IsCheckable && corgi.IsCheckable
                    && westie.IsChecked == (settings.Breed == "westie")
                    && corgi.IsChecked == (settings.Breed == "corgi"), "Breed menu must indicate the current model.");
                MenuItem selected = desired == "corgi" ? corgi : westie;
                selected.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, selected));
                WestieModel current = Field<WestieModel>(pet, "model");
                Require(current.Breed == desired && settings.Breed == desired,
                    "The real breed menu Click handler did not change the selected model.");
                Require(choice.SelectedIndex == (desired == "corgi" ? 1 : 0), "Menu switching did not synchronize the model selector.");
                Require(ReferenceEquals(((ModelVisual3D)preview.Children[0]).Content, current.Scene),
                    "Menu switching did not synchronize the home model preview.");
                Require(Field<string>(pet, "mode") == "sleep" && settings.Name == "柯基朋友"
                    && Field<int>(pet, "affection") == 7 && Field<int>(pet, "treats") == 9,
                    "Menu switching changed the ongoing state or personal values.");
            }
        }
        finally { Set(pet, "breedChoice", null); }
        Console.WriteLine("PASS: real context-menu breed labels, selected checkmarks and Click handlers switch both models and synchronize selector/preview.");
    }

    static void Restore(string path, byte[] bytes)
    {
        if (bytes == null) { if (File.Exists(path)) File.Delete(path); }
        else File.WriteAllBytes(path, bytes);
    }
}
