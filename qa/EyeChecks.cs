using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WestiePet;

// Compile with WestieModel.cs and SculptedCoat.cs and run on an STA thread.
// The optional reference is the accepted 480px face.png from RenderDetails.
static class EyeChecks
{
    const int CloseSize = 480;
    const int EyeDarkLuma = 90;
    // Measured from docs/images/approved-legacy-face.png for runs outside the repo.
    const int AcceptedReferenceDarkPixels = 1741;
    const double ReferenceTolerance = .15;
    // The eye band in the RenderDetails camera. It includes both eyes and ends above the nose.
    static readonly Int32Rect ReferenceEyeBand = new Int32Rect(210, 210, 145, 80);
    static readonly OrthographicCamera DesktopCamera = new OrthographicCamera(
        new Point3D(0, 1.76, 6), new Vector3D(0, -.66, -6), new Vector3D(0, 1, 0), 2.95);

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string output = Path.Combine(Environment.CurrentDirectory, "qa", "eye-checks");
            string baseline = Path.Combine(Environment.CurrentDirectory, "docs", "images", "approved-legacy-face.png");
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 == args.Length)
                    throw new ArgumentException("Expected a path after " + args[i]);
                if (args[i] == "--output-dir") output = Path.GetFullPath(args[i + 1]);
                else if (args[i] == "--baseline")
                {
                    baseline = Path.GetFullPath(args[i + 1]);
                    if (!File.Exists(baseline)) throw new FileNotFoundException("Missing baseline", baseline);
                }
                else throw new ArgumentException("Unknown option: " + args[i]);
            }

            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "metrics.csv"),
                "sample,darkIrisPixels,changedIrisOrHighlightPixels,referenceBandDarkPixels," +
                "eye0ScleraPixels,eye0IrisPixels,eye0HighlightPixels,eye1ScleraPixels,eye1IrisPixels,eye1HighlightPixels\r\n");
            bool hasReferenceImage = File.Exists(baseline);
            int reference = hasReferenceImage ? CountBaseline(baseline) : AcceptedReferenceDarkPixels;
            int current = CheckView(output, "threequarter", 52, .36);
            CheckView(output, "front", 19, 0); // Cancel the idle head yaw for a head-on face.
            int lower = (int)Math.Ceiling(reference * (1 - ReferenceTolerance));
            int upper = (int)Math.Floor(reference * (1 + ReferenceTolerance));
            string referenceSummary = string.Format("Three-quarter eye band: {0} dark pixels; expected {1}–{2} " +
                "(reference {3}, {4}).", current, lower, upper, reference,
                hasReferenceImage ? baseline : "recorded accepted reference");
            Console.WriteLine(referenceSummary);
            Require(current >= lower && current <= upper, referenceSummary);
            File.WriteAllText(Path.Combine(output, "result.txt"),
                "PASS: visible transparent closeups and 306x260 samples; front and three-quarter " +
                "idle/blink/pet/sleep/wake; visible sclera, iris and highlights in each open eye; " +
                "finite mesh positions and normals; no iris/glint leaks.\r\n" + referenceSummary + "\r\n");
            Console.WriteLine("Eye checks passed: " + output);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("EYE_CHECKS_ERROR: " + error);
            return 1;
        }
    }

    static int CheckView(string output, string viewName, double heading, double cameraX)
    {
        WestieModel model = new WestieModel();
        model.Heading.Angle = heading;
        TranslateTransform3D[] gaze = (TranslateTransform3D[])ModelField("gaze").GetValue(model);
        Material sclera = (Material)ModelField("sclera").GetValue(model);
        Material iris = (Material)ModelField("iris").GetValue(model);
        List<EyeLayer> layers = new List<EyeLayer>();
        FindEyeLayers(model.Scene, gaze, sclera, iris, layers);
        Require(layers.Count == 2, "Expected two groups containing both the iris and its highlights.");
        List<ScenePart> gazeParts = layers.ConvertAll(eye => eye.Gaze);
        Viewport3D view = new Viewport3D();
        view.Children.Add(new ModelVisual3D { Content = model.Scene });

        // Match RenderDetails exactly for the baseline camera and idle sample.
        for (int i = 0; i < 45; i++) model.Animate(1.1 + i * .042, "idle", i * .042);
        ModelField("nextBlink").SetValue(model, double.PositiveInfinity);
        EyePixels idle = Sample(output, viewName + "-idle", view, model, layers, gazeParts, cameraX, false, true);
        Require(idle.Dark >= 100 && idle.Changed >= 150,
            viewName + ": the open iris must be visible before checking closure.");
        CheckOpenEyes(viewName + "-idle", idle.Visible);

        // Start the existing blink animation at a known time and sample at full closure.
        ModelField("blinkStarted").SetValue(model, 3.0);
        ModelField("blinkDuration").SetValue(model, .24);
        model.Animate(3.09, "idle", 1.99);
        EyePixels blink = Sample(output, viewName + "-blink", view, model, layers, gazeParts, cameraX, false, false);

        Animate(model, "pet", 3.5, 1);
        EyePixels pet = Sample(output, viewName + "-pet", view, model, layers, gazeParts, cameraX, false, false);
        Animate(model, "sleep", 4.6, 1.7);
        EyePixels sleep = Sample(output, viewName + "-sleep", view, model, layers, gazeParts, cameraX, true, false);
        Animate(model, "idle", 6.4, .9);
        EyePixels wake = Sample(output, viewName + "-wake", view, model, layers, gazeParts, cameraX, false, true);

        Require(blink.Changed <= 6, viewName + ": iris or highlight leaked during a full blink.");
        Require(sleep.Changed <= 6, viewName + ": iris or highlight leaked during sleep.");
        Require(pet.Changed <= idle.Changed * .25, viewName + ": the eyes did not squint during petting.");
        Require(wake.Dark >= idle.Dark * .70, viewName + ": the eyes did not reopen after sleep.");
        CheckOpenEyes(viewName + "-wake", wake.Visible);
        return idle.BandDark;
    }

    static void Animate(WestieModel model, string state, double start, double duration)
    {
        model.BeginAction(state);
        for (int i = 0; i <= Math.Round(duration * 30); i++)
            model.Animate(start + i / 30.0, state, i / 30.0);
    }

    struct EyePixels
    {
        public int Changed, Dark, BandDark;
        public VisibleEye[] Visible;
    }

    struct VisibleEye
    {
        public int Sclera, Iris, Highlights;
    }

    static void CheckOpenEyes(string sample, VisibleEye[] eyes)
    {
        for (int i = 0; i < eyes.Length; i++)
        {
            VisibleEye eye = eyes[i];
            Require(eye.Sclera >= 35 && eye.Iris >= 20 && eye.Highlights >= 8,
                string.Format("{0}: eye {1} needs visible sclera (35+), iris (20+), and highlights (8+); measured {2}/{3}/{4}.",
                    sample, i, eye.Sclera, eye.Iris, eye.Highlights));
        }
    }

    static EyePixels Sample(string output, string name, Viewport3D view, WestieModel model,
        List<EyeLayer> layers, List<ScenePart> gazeParts, double cameraX, bool asleep, bool measureOpenEyes)
    {
        CheckMesh(model.Scene, name);
        view.Camera = new OrthographicCamera(new Point3D(cameraX, asleep ? .95 : 1.74, 6),
            new Vector3D(0, -.18, -6), new Vector3D(0, 1, 0), 1.60);
        RenderTargetBitmap close = Render(view, CloseSize, CloseSize);
        byte[] pixels = ReadPixels(close);
        Save(close, pixels, Path.Combine(output, name + "-close.png"));

        // Subtract the same pose with the gaze layers detached. This detects even
        // light glints while leaving the eyelids, fur, and shadows in both renders.
        byte[] withoutEyes = RenderWithout(view, gazeParts);
        EyePixels measured = new EyePixels();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (!PixelChanged(pixels, withoutEyes, i)) continue;
            measured.Changed++;
            if (Dark(pixels, i)) measured.Dark++;
        }
        measured.BandDark = CountDarkBand(pixels);
        string openMetrics = ",,,,,,";
        if (measureOpenEyes)
        {
            // Hide only one layer of one eye. Fur, lids and the other eye stay in
            // place, so a covered surface or a nearby tuft cannot add to its count.
            measured.Visible = new VisibleEye[layers.Count];
            openMetrics = "";
            for (int i = 0; i < layers.Count; i++)
            {
                EyeLayer layer = layers[i];
                VisibleEye visible = new VisibleEye
                {
                    Sclera = CountChanges(pixels, RenderWithout(view, layer.Sclera)),
                    Iris = CountChanges(pixels, RenderWithout(view, layer.Iris)),
                    Highlights = CountChanges(pixels, RenderWithout(view, layer.Highlights))
                };
                measured.Visible[i] = visible;
                openMetrics += string.Format(",{0},{1},{2}", visible.Sclera, visible.Iris, visible.Highlights);
                Console.WriteLine("{0} eye {1}: {2} sclera pixels, {3} iris pixels, {4} highlight pixels.",
                    name, i, visible.Sclera, visible.Iris, visible.Highlights);
            }
        }
        File.AppendAllText(Path.Combine(output, "metrics.csv"),
            name + "," + measured.Dark + "," + measured.Changed + "," + measured.BandDark + openMetrics + "\r\n");
        Console.WriteLine("{0}: {1} dark iris pixels, {2} changed iris or highlight pixels, {3} dark in eye band.",
            name, measured.Dark, measured.Changed, measured.BandDark);

        view.Camera = DesktopCamera;
        RenderTargetBitmap desktop = Render(view, 306, 260);
        Save(desktop, ReadPixels(desktop), Path.Combine(output, name + "-desktop.png"));
        return measured;
    }

    sealed class EyeLayer
    {
        public ScenePart Gaze;
        public List<ScenePart> Sclera, Iris, Highlights;
    }

    sealed class ScenePart
    {
        public readonly Model3DGroup Parent;
        public readonly Model3D Content;

        public ScenePart(Model3DGroup parent, Model3D content)
        {
            Parent = parent;
            Content = content;
        }
    }

    static void FindEyeLayers(Model3DGroup parent, TranslateTransform3D[] gaze, Material sclera, Material iris,
        List<EyeLayer> layers)
    {
        foreach (Model3D part in parent.Children)
        {
            Model3DGroup child = part as Model3DGroup;
            if (child == null) continue;
            Transform3DGroup transform = child.Transform as Transform3DGroup;
            if (transform != null && Array.Exists(gaze, movement => transform.Children.Contains(movement)))
            {
                EyeLayer eye = new EyeLayer
                {
                    Gaze = new ScenePart(parent, child),
                    Sclera = FindMaterials(parent, material => ReferenceEquals(material, sclera)),
                    Iris = FindMaterials(child, material => ReferenceEquals(material, iris)),
                    Highlights = FindMaterials(child, IsHighlightMaterial)
                };
                Require(eye.Sclera.Count > 0 && eye.Iris.Count > 0 && eye.Highlights.Count > 0,
                    "Each eye must have a sclera, an iris, and highlight geometry.");
                layers.Add(eye);
            }
            else FindEyeLayers(child, gaze, sclera, iris, layers);
        }
    }

    static List<ScenePart> FindMaterials(Model3DGroup parent, Predicate<Material> match)
    {
        List<ScenePart> parts = new List<ScenePart>();
        foreach (Model3D child in parent.Children)
        {
            GeometryModel3D geometry = child as GeometryModel3D;
            if (geometry != null && match(geometry.Material)) parts.Add(new ScenePart(parent, geometry));
        }
        return parts;
    }

    static bool IsHighlightMaterial(Material material)
    {
        // Among the geometry that follows gaze, the light diffuse materials are
        // the glints. The dark pupil and the iris use their own dark materials.
        DiffuseMaterial diffuse = material as DiffuseMaterial;
        SolidColorBrush brush = diffuse == null ? null : diffuse.Brush as SolidColorBrush;
        return brush != null && brush.Color.R >= 220 && brush.Color.G >= 220 && brush.Color.B >= 220;
    }

    static byte[] RenderWithout(Viewport3D view, List<ScenePart> parts)
    {
        int[] indices = new int[parts.Count];
        int removed = 0;
        try
        {
            for (; removed < parts.Count; removed++)
            {
                ScenePart part = parts[removed];
                indices[removed] = part.Parent.Children.IndexOf(part.Content);
                part.Parent.Children.RemoveAt(indices[removed]);
            }
            return ReadPixels(Render(view, CloseSize, CloseSize));
        }
        finally
        {
            // Reverse removal order to restore original indices in shared groups.
            for (int i = removed - 1; i >= 0; i--)
                parts[i].Parent.Children.Insert(indices[i], parts[i].Content);
        }
    }

    static int CountChanges(byte[] pixels, byte[] withoutLayer)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (PixelChanged(pixels, withoutLayer, i)) count++;
        return count;
    }

    static bool PixelChanged(byte[] pixels, byte[] withoutLayer, int i)
    {
        return pixels[i + 3] >= 224 && (Math.Abs(pixels[i] - withoutLayer[i]) > 18
            || Math.Abs(pixels[i + 1] - withoutLayer[i + 1]) > 18
            || Math.Abs(pixels[i + 2] - withoutLayer[i + 2]) > 18);
    }

    static FieldInfo ModelField(string name)
    {
        FieldInfo field = typeof(WestieModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(typeof(WestieModel).FullName, name);
        return field;
    }

    static void CheckMesh(Model3D scene, string name)
    {
        Model3DGroup group = scene as Model3DGroup;
        if (group != null)
        {
            foreach (Model3D part in group.Children) CheckMesh(part, name);
            return;
        }
        GeometryModel3D geometry = scene as GeometryModel3D;
        MeshGeometry3D mesh = geometry == null ? null : geometry.Geometry as MeshGeometry3D;
        if (mesh == null) return;
        foreach (Point3D vertex in mesh.Positions)
            if (!Finite(vertex.X) || !Finite(vertex.Y) || !Finite(vertex.Z))
                throw new InvalidOperationException(name + ": non-finite mesh vertex.");
        foreach (Vector3D normal in mesh.Normals)
            if (!Finite(normal.X) || !Finite(normal.Y) || !Finite(normal.Z))
                throw new InvalidOperationException(name + ": non-finite mesh normal.");
    }

    static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

    static RenderTargetBitmap Render(Viewport3D view, int width, int height)
    {
        Size size = new Size(width, height);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        return bitmap;
    }

    static byte[] ReadPixels(BitmapSource source)
    {
        BitmapSource converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] bytes = new byte[source.PixelWidth * source.PixelHeight * 4];
        converted.CopyPixels(bytes, source.PixelWidth * 4, 0);
        return bytes;
    }

    static int CountBaseline(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        {
            BitmapFrame source = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Require(source.PixelWidth == CloseSize && source.PixelHeight == CloseSize, "Expected a 480px square baseline.");
            int dark = CountDarkBand(ReadPixels(source));
            Require(dark > 0, "The baseline has no dark pixels in the expected eye band.");
            return dark;
        }
    }

    static int CountDarkBand(byte[] pixels)
    {
        int count = 0;
        for (int y = ReferenceEyeBand.Y; y < ReferenceEyeBand.Y + ReferenceEyeBand.Height; y++)
        for (int x = ReferenceEyeBand.X; x < ReferenceEyeBand.X + ReferenceEyeBand.Width; x++)
            if (Dark(pixels, (y * CloseSize + x) * 4)) count++;
        return count;
    }

    static bool Dark(byte[] pixels, int i)
    {
        return pixels[i + 3] >= 224 && (77 * pixels[i + 2] + 150 * pixels[i + 1] + 29 * pixels[i]) < EyeDarkLuma * 256;
    }

    static void Save(RenderTargetBitmap bitmap, byte[] pixels, string path)
    {
        bool visible = false, transparent = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            visible |= pixels[i] >= 224;
            transparent |= pixels[i] == 0;
        }
        Require(visible && transparent, "Expected a visible dog and a transparent background: " + path);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(path)) encoder.Save(stream);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
