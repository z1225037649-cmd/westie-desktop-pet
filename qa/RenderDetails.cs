using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WestiePet;

static class RenderDetails
{
    [STAThread]
    static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.WriteLine("RENDER_ERROR: " + error.GetType().FullName + ": " + error.Message); Environment.ExitCode = 1; }
    }

    static void Run()
    {
        string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "details");
        Directory.CreateDirectory(output);
        WestieModel model = new WestieModel();
        Console.WriteLine("Connected model triangles: " + model.TriangleCount);
        Grid stage = new Grid { Width = 480, Height = 480,
            Background = new SolidColorBrush(Color.FromRgb(226, 230, 224)) };
        Viewport3D view = new Viewport3D();
        view.Camera = new OrthographicCamera(new Point3D(.36, 1.74, 6), new Vector3D(0, -.18, -6), new Vector3D(0, 1, 0), 1.60);
        view.Children.Add(new ModelVisual3D { Content = model.Scene });
        stage.Children.Add(view);
        stage.Measure(new Size(480, 480)); stage.Arrange(new Rect(0, 0, 480, 480)); stage.UpdateLayout();
        for (int i = 0; i < 45; i++) model.Animate(1.1 + i * .042, "idle", i * .042);
        Save(stage, Path.Combine(output, "face.png"));
        bool quick = Array.IndexOf(Environment.GetCommandLineArgs(), "--quick") >= 0;
        for (int i = 0; i < (quick ? 0 : 48); i++)
        {
            double t = i < 18 ? 5.6 + i / 10.0 : 8.7 + (i - 18) / 10.0;
            model.LookAt(i < 12 ? .8 : i < 24 ? -.7 : .3, i < 24 ? .45 : -.25, .85);
            for (int tick = 0; tick < 3; tick++) model.Animate(t + tick / 30.0, "idle", t + tick / 30.0);
            Save(stage, Path.Combine(output, "look-" + i.ToString("00") + ".png"));
        }
        model.LookAt(0, 0, 0);
        foreach (string state in new string[] { "pet", "sleep" })
        {
            for (int i = 0; i < 45; i++) model.Animate(8 + i * .042, state, i * .042);
            Save(stage, Path.Combine(output, "face-" + state + ".png"));
        }
        stage.Width = 600; stage.Height = 510;
        view.Camera = new OrthographicCamera(new Point3D(0, 1.76, 6), new Vector3D(0, -.66, -6), new Vector3D(0, 1, 0), 2.95);
        stage.Measure(new Size(600, 510)); stage.Arrange(new Rect(0, 0, 600, 510)); stage.UpdateLayout();
        double standingThickness = 0;
        foreach (string state in new string[] { "idle", "walk", "pet", "sleep" })
        {
            model.Heading.Angle = state == "walk" ? -68 : 52;
            for (int i = 0; i < 45; i++) model.Animate(1.1 + i * .042, state, i * .042);
            Save(stage, Path.Combine(output, "full-" + state + ".png"));
            double thickness = TorsoThickness(model);
            if (state == "idle") standingThickness = thickness;
            if (state == "sleep")
            {
                Console.WriteLine("Sleeping / standing torso thickness: " + (thickness / standingThickness).ToString("F3"));
                if (thickness < standingThickness * .90) throw new InvalidOperationException("Sleeping pose flattened the torso.");
            }
        }
        foreach (int angle in new int[] { 90, 150 })
        {
            model.Heading.Angle = angle;
            for (int i = 0; i < 45; i++) model.Animate(13 + i * .042, "sleep", i * .042);
            Save(stage, Path.Combine(output, "sleep-" + angle + ".png"));
        }
        model.Heading.Angle = 0;
        view.Camera = new OrthographicCamera(new Point3D(0, .86, 6), new Vector3D(0, -.25, -6), new Vector3D(0, 1, 0), 1.50);
        for (int i = 0; i < 45; i++) model.Animate(16 + i * .042, "sleep", i * .042);
        Save(stage, Path.Combine(output, "sleep-face-front.png"));
        view.Camera = new OrthographicCamera(new Point3D(0, 1.76, 6), new Vector3D(0, -.66, -6), new Vector3D(0, 1, 0), 2.95);
        model.Heading.Angle = 52;
        for (int i = 0; i < 60; i++) model.Animate(19 + i * .042, "play", i * .042);
        for (int i = 0; i < 24; i++)
        {
            double t = 22 + i / 6.0;
            for (int tick = 0; tick < 4; tick++) model.Animate(t + tick / 24.0, "play", i / 6.0 + tick / 24.0);
            Save(stage, Path.Combine(output, "play-" + i.ToString("00") + ".png"));
        }
        foreach (int angle in new int[] { 90, 150, 180 })
        {
            model.Heading.Angle = angle;
            for (int i = 0; i < 45; i++) model.Animate(13 + i * .042, "idle", i * .042);
            Save(stage, Path.Combine(output, "body-" + angle + ".png"));
        }
        model.Heading.Angle = -68;
        for (int i = 0; i < (quick ? 0 : 24); i++)
        {
            double t = 16 + i / 12.0;
            for (int tick = 0; tick < 2; tick++) model.Animate(t + tick / 24.0, "walk", t + tick / 24.0);
            Save(stage, Path.Combine(output, "walk-" + i.ToString("00") + ".png"));
        }
        Console.WriteLine("Rendered face, blink and three full-body poses. Triangles: " + model.TriangleCount);
        System.Diagnostics.Stopwatch benchmark = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 90; i++) model.Animate(8 + i * .042, "walk", i * .042);
        benchmark.Stop();
        Console.WriteLine("Animation CPU time: " + (benchmark.Elapsed.TotalMilliseconds / 90).ToString("F2") + " ms/frame.");
    }

    static double TorsoThickness(WestieModel model)
    {
        Model3DGroup dog = null;
        foreach (Model3D part in model.Scene.Children) if (part is Model3DGroup) dog = (Model3DGroup)part;
        if (dog == null) throw new InvalidOperationException("Dog geometry not found.");
        Model3DGroup coat = (Model3DGroup)dog.Children[0];
        MeshGeometry3D skin = (MeshGeometry3D)((GeometryModel3D)coat.Children[0]).Geometry;
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        foreach (Point3D point in skin.Positions)
        {
            if (Math.Abs(point.X) > .075 || point.Z < -.65 || point.Z > -.40) continue;
            Point3D visible = dog.Transform.Transform(point);
            low = Math.Min(low, visible.Y); high = Math.Max(high, visible.Y);
        }
        if (double.IsInfinity(low)) throw new InvalidOperationException("Torso cross section not found.");
        return high - low;
    }

    static void Save(Grid stage, string path)
    {
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)stage.Width, (int)stage.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(path)) encoder.Save(stream);
    }
}
