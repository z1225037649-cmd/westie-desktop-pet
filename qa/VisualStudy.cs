using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WestiePet;

// Runs entirely offscreen: no Application, Window, tray icon, settings or input hooks.
static class VisualStudy
{
    const int Width = 512, Height = 448, FramesPerSecond = 30;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [STAThread]
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error) { Console.WriteLine("VISUAL_STUDY_ERROR: " + error); return 1; }
    }

    static void Run(string[] args)
    {
        string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual-study");
        bool stillsOnly = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--stills-only") stillsOnly = true;
            else if (args[i] == "--output-dir" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
            else throw new ArgumentException("Usage: VisualStudy.exe [--output-dir PATH] [--stills-only]");
        }
        Directory.CreateDirectory(output);
        string[] names = { "front", "side", "threequarter" };
        double[] angles = { 0, 90, 52 };
        foreach (bool natural in new[] { false, true })
        foreach (bool coat in new[] { false, true })
        {
            WestieModel model = new WestieModel(natural, coat);
            Viewport3D view;
            Grid stage = CreateStage(model, Width, Height, false, out view);
            WarmUp(model, "idle", 0, 2);
            string style = natural ? "natural" : "animated";
            for (int i = 0; i < names.Length; i++)
            {
                model.Heading.Angle = angles[i];
                Save(stage, Path.Combine(output, style + "-" + (coat ? "coat" : "base") + "-" + names[i] + ".png"), true);
            }
            if (coat)
            {
                model.Heading.Angle = 52;
                view.Camera = FaceCamera();
                stage.Width = stage.Height = 480;
                Layout(stage);
                Save(stage, Path.Combine(output, "face-" + style + ".png"), false);
            }
            Console.WriteLine(style + " " + (coat ? "coat" : "base") + ": " + model.TriangleCount + " triangles.");
        }

        double walkDuration = Math.PI / 2; // Exactly two cycles of the current 8 rad/s gait.
        if (!stillsOnly)
        {
            RenderClip(output, "idle", 6, 180, 52, 0);
            RenderClip(output, "play", 5, 150, 52, 0);
            RenderClip(output, "walk", walkDuration, 48, -68, 2);
        }
        using (StreamWriter writer = new StreamWriter(Path.Combine(output, "manifest.json"), false, new UTF8Encoding(false)))
        {
            writer.WriteLine("{");
            writer.WriteLine("  \"schema\": 1, \"width\": " + Width + ", \"height\": " + Height + ",");
            writer.WriteLine("  \"rendering\": \"Offscreen WPF model only; transparent PNG; no UI overlays\",");
            writer.WriteLine("  \"styles\": \"Shared body and motion; natural eyes and animated eyes\",");
            writer.WriteLine("  \"sampling\": \"Idle/play: 30 Hz; walk: 48 frames over exactly two cycles\",");
            writer.WriteLine("  \"appActiveUpdateIntervalMilliseconds\": 56,");
            writer.WriteLine("  \"stillsOnly\": " + (stillsOnly ? "true" : "false") + ",");
            writer.WriteLine("  \"clips\": {");
            WriteClip(writer, "idle", 6, 180, true);
            WriteClip(writer, "play", 5, 150, true);
            WriteClip(writer, "walk", walkDuration, 48, false);
            writer.WriteLine("  }");
            writer.WriteLine("}");
        }
        Console.WriteLine("Visual study saved: " + output);
    }

    static void WriteClip(StreamWriter writer, string name, double duration, int count, bool comma)
    {
        writer.WriteLine("    \"" + name + "\": { \"frameCount\": " + count + ", \"durationSeconds\": "
            + duration.ToString("R", Invariant) + " }" + (comma ? "," : ""));
    }

    static void RenderClip(string output, string state, double duration, int frameCount, double angle, double warmStateSeconds)
    {
        WestieModel model = new WestieModel();
        model.Heading.Angle = angle;
        Viewport3D view;
        Grid stage = CreateStage(model, Width, Height, false, out view);
        WarmUp(model, "idle", 0, 2);
        if (warmStateSeconds > 0) WarmUp(model, state, 2, warmStateSeconds);
        model.BeginAction(state);
        double start = 2 + warmStateSeconds, step = duration / frameCount;
        for (int i = 0; i < frameCount; i++)
        {
            double localTime = i * step;
            model.Animate(start + localTime, state, warmStateSeconds + localTime);
            Save(stage, Path.Combine(output, state + "-" + i.ToString("000") + ".png"), true);
        }
        Console.WriteLine(state + ": " + frameCount + " frames / " + duration.ToString("F3", Invariant) + " seconds.");
    }

    static void WarmUp(WestieModel model, string state, double start, double duration)
    {
        int ticks = (int)Math.Round(duration * FramesPerSecond);
        for (int i = 0; i < ticks; i++)
        {
            double t = i / (double)FramesPerSecond;
            model.Animate(start + t, state, t);
        }
    }

    static Grid CreateStage(WestieModel model, int width, int height, bool face, out Viewport3D view)
    {
        Grid stage = new Grid { Width = width, Height = height, Background = Brushes.Transparent };
        view = new Viewport3D();
        view.Camera = face ? FaceCamera() : new OrthographicCamera(new Point3D(0, 1.76, 6),
            new Vector3D(0, -.66, -6), new Vector3D(0, 1, 0), 2.95);
        view.IsHitTestVisible = false;
        view.Children.Add(new ModelVisual3D { Content = model.Scene });
        stage.Children.Add(view);
        Layout(stage);
        return stage;
    }

    static OrthographicCamera FaceCamera()
    {
        return new OrthographicCamera(new Point3D(.36, 1.74, 6), new Vector3D(0, -.18, -6), new Vector3D(0, 1, 0), 1.60);
    }

    static void Layout(Grid stage)
    {
        stage.Measure(new Size(stage.Width, stage.Height));
        stage.Arrange(new Rect(0, 0, stage.Width, stage.Height));
        stage.UpdateLayout();
    }

    static void Save(Grid stage, string path, bool checkFit)
    {
        int width = (int)stage.Width, height = (int)stage.Height;
        RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        bool visible = false, transparent = false;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte alpha = pixels[(y * width + x) * 4 + 3];
            if (alpha == 0) transparent = true;
            if (alpha <= 24) continue;
            visible = true;
            if (checkFit && (x < 2 || y < 2 || x >= width - 2 || y >= height - 2))
                throw new InvalidOperationException("Full-body frame reaches image edge: " + path);
        }
        if (!visible || !transparent) throw new InvalidOperationException("Expected visible model on transparent background: " + path);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(path)) encoder.Save(stream);
    }
}
