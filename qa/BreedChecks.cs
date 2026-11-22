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
            CheckEmbeddedFrames();
            CheckRendering();
            CheckSettingsAndSwitching();
            File.WriteAllText(Path.Combine(Output, "result.txt"),
                "PASS: generated Westie 3D and frame-animated Corgi rendering; embedded animation resource; " +
                "fixed billboard and real frame changes; transparent margins and mirrored alpha input; " +
                "model/preview switching, current-model reuse, preserved state/settings and roaming preference.\r\n");
            Console.WriteLine("PASS: breed rendering, switching and isolated settings: " + Output);
            return 0;
        }
        catch (Exception error) { Console.WriteLine("BREED_CHECKS_ERROR: " + error); return 1; }
    }

    static void CheckEmbeddedFrames()
    {
        string source = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            "assets", "corgi-animation.frames");
        using (Stream resource = typeof(WestieModel).Assembly.GetManifestResourceStream("WestiePet.CorgiAnimation"))
        using (Stream file = File.OpenRead(source))
        using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
        {
            Require(resource != null && resource.Length > 1000000, "Missing embedded Corgi animation sequence.");
            Require(Convert.ToBase64String(hash.ComputeHash(resource)) == Convert.ToBase64String(hash.ComputeHash(file)),
                "Embedded animation differs from the exported build input.");
        }
        Console.WriteLine("PASS: byte-identical embedded Corgi animation resource.");
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
        Require(!westie.IsPreRendered, "Westie must use the generated volumetric 3D model.");
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
        Console.WriteLine("PASS: generated Westie six-state 3D rendering and walking/sleeping checks.");

        foreach (string state in new[] { "idle", "pet", "feed", "play", "sleep", "wake" })
        {
            WestieModel corgi = new WestieModel("corgi");
            Require(corgi.IsPreRendered, "Corgi must identify its pre-rendered animation presentation.");
            CorgiSprite sprite = Field<CorgiSprite>(corgi, "artwork");
            Require(sprite != null, "Production Corgi lacks its frame player.");
            MeshGeometry3D surface = FrameGeometry(sprite).Geometry as MeshGeometry3D;
            Require(surface != null && surface.Positions.Count <= 8 && surface.TriangleIndices.Count <= 12,
                "Corgi must play frames on a fixed billboard instead of deforming the source-image mesh.");
            Point3D[] positions = new Point3D[surface.Positions.Count]; surface.Positions.CopyTo(positions, 0);
            stage = Stage(corgi, out view); time = 0;
            if (state == "wake") Advance(corgi, "sleep", 5.1, ref time);
            corgi.BeginAction(state);
            double age = state == "idle" ? 1.2 : state == "feed" ? 1.6 : state == "play" ? 1.3 : 1.8;
            for (double step = 0; step <= age; step += 1.0 / 30)
            { time += 1.0 / 30; corgi.Animate(time, state, step); }
            byte[] before = Save(stage, "corgi-" + state);
            for (int tick = 1; tick <= 9; tick++)
            { time += 1.0 / 30; corgi.Animate(time, state, age + tick / 30.0); }
            byte[] after = Save(stage, "corgi-" + state + "-next"); CheckMesh(corgi.Scene, "corgi-" + state);
            Require(Difference(before, after) > 8, "Corgi " + state + ": frame changes must be visible.");
            for (int vertex = 0; vertex < positions.Length; vertex++)
                Require((positions[vertex] - surface.Positions[vertex]).Length < 1e-10, "Frame playback distorted its billboard.");
            BitmapSource frame = FrameBitmap(sprite);
            Require(frame.PixelWidth >= 306 && frame.PixelHeight >= 260, "Animation resolution must cover the desktop viewport.");
        }
        WestieModel facing = new WestieModel("corgi"); stage = Stage(facing, out view); time = 0;
        Advance(facing, "idle", 1.2, ref time);
        foreach (double heading in new[] { -52.0, 52.0 })
        { facing.Heading.Angle = heading; facing.Animate(time, "idle", 1.2); Save(stage, heading < 0 ? "corgi-facing-left" : "corgi-facing-right"); }
        Console.WriteLine("PASS: Corgi six-state frame playback, fixed billboard and facing mirrors.");
    }

    static void Advance(WestieModel model, string state, double duration, ref double time)
    {
        model.BeginAction(state);
        int count = (int)Math.Round(duration * 30);
        for (int i = 0; i < count; i++) { time += 1.0 / 30; model.Animate(time, state, i / 30.0); }
    }

    static GeometryModel3D FrameGeometry(CorgiSprite sprite)
    { GeometryModel3D result = FindGeometry(sprite.Model); Require(result != null, "Missing Corgi frame billboard."); return result; }
    static GeometryModel3D FindGeometry(Model3D node)
    {
        GeometryModel3D geometry = node as GeometryModel3D; if (geometry != null) return geometry;
        Model3DGroup group = node as Model3DGroup;
        if (group != null) foreach (Model3D child in group.Children)
        { GeometryModel3D found = FindGeometry(child); if (found != null) return found; }
        return null;
    }
    static BitmapSource FrameBitmap(CorgiSprite sprite)
    {
        MaterialGroup group = FrameGeometry(sprite).Material as MaterialGroup;
        Require(group != null, "Frame needs alpha-compatible diffuse/emissive material.");
        foreach (Material child in group.Children)
        {
            EmissiveMaterial emission = child as EmissiveMaterial; ImageBrush image = emission == null ? null : emission.Brush as ImageBrush;
            if (image != null && image.ImageSource is BitmapSource) return (BitmapSource)image.ImageSource;
        }
        throw new InvalidOperationException("Missing decoded animation frame.");
    }
    static byte[] FramePixels(BitmapSource bitmap)
    {
        BitmapSource source = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[source.PixelWidth * source.PixelHeight * 4]; source.CopyPixels(pixels, source.PixelWidth * 4, 0); return pixels;
    }
    static Point3D FramePoint(CorgiSprite sprite, Point pixel)
    {
        GeometryModel3D geometry = FrameGeometry(sprite); MeshGeometry3D mesh = (MeshGeometry3D)geometry.Geometry;
        BitmapSource image = FrameBitmap(sprite); Point uv = new Point(pixel.X/image.PixelWidth,pixel.Y/image.PixelHeight);
        for (int index=0;index<mesh.TriangleIndices.Count;index+=3)
        {
            int ia=mesh.TriangleIndices[index],ib=mesh.TriangleIndices[index+1],ic=mesh.TriangleIndices[index+2];
            Point a=mesh.TextureCoordinates[ia],b=mesh.TextureCoordinates[ib],c=mesh.TextureCoordinates[ic];
            double d=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(Math.Abs(d)<1e-12)continue;
            double wa=((b.Y-c.Y)*(uv.X-c.X)+(c.X-b.X)*(uv.Y-c.Y))/d;
            double wb=((c.Y-a.Y)*(uv.X-c.X)+(a.X-c.X)*(uv.Y-c.Y))/d,wc=1-wa-wb;
            if(wa < -1e-8 || wb < -1e-8 || wc < -1e-8)continue;
            Point3D pa=mesh.Positions[ia],pb=mesh.Positions[ib],pc=mesh.Positions[ic];
            Point3D value=new Point3D(pa.X*wa+pb.X*wb+pc.X*wc,pa.Y*wa+pb.Y*wb+pc.Y*wc,pa.Z*wa+pb.Z*wb+pc.Z*wc);
            return sprite.Model.Transform.Transform(geometry.Transform.Transform(value));
        }
        throw new InvalidOperationException("Frame point is outside billboard UVs.");
    }
    static Point Project(Point3D point, OrthographicCamera camera, double width, double height)
    {
        Vector3D forward=camera.LookDirection;forward.Normalize();Vector3D right=Vector3D.CrossProduct(forward,camera.UpDirection);right.Normalize();
        Vector3D up=Vector3D.CrossProduct(right,forward);up.Normalize();Vector3D delta=point-camera.Position;double pixels=width/camera.Width;
        return new Point(width*.5+Vector3D.DotProduct(delta,right)*pixels,height*.5-Vector3D.DotProduct(delta,up)*pixels);
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
                CheckCorgiRoam(pet, loaded);
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

    static void CheckCorgiInput(PetWindow pet, PetSettings settings)
    {
        Viewport3D viewport=Field<Viewport3D>(pet,"viewport");SilhouetteHitArea input=Field<SilhouetteHitArea>(pet,"pointerHitArea");
        WestieModel model=Field<WestieModel>(pet,"model");CorgiSprite sprite=Field<CorgiSprite>(model,"artwork");
        Require(!viewport.IsHitTestVisible && model.IsPreRendered,"Animated Corgi must use CPU alpha input.");
        model.BeginAction("idle");model.Animate(0,"idle",0);BitmapSource frame=FrameBitmap(sprite);byte[] pixels=FramePixels(frame);
        List<Point> solid=new List<Point>(),empty=new List<Point>();
        for(int row=frame.PixelHeight/8;row<frame.PixelHeight*7/8;row+=Math.Max(1,frame.PixelHeight/8))
        {
            int bestStart=0,bestLength=0,start=0,length=0;
            for(int x=0;x<frame.PixelWidth;x++)
            {
                if(pixels[(row*frame.PixelWidth+x)*4+3]>240)
                {if(length==0)start=x;length++;if(length>bestLength){bestStart=start;bestLength=length;}}else length=0;
            }
            if(bestLength>12)solid.Add(new Point(bestStart+bestLength/2+.5,row+.5));
        }
        foreach(Point point in new[]{new Point(3.5,3.5),new Point(frame.PixelWidth-3.5,3.5),
            new Point(3.5,frame.PixelHeight-3.5),new Point(frame.PixelWidth-3.5,frame.PixelHeight-3.5)})
            if(pixels[((int)point.Y*frame.PixelWidth+(int)point.X)*4+3]<8)empty.Add(point);
        Require(solid.Count>=4 && empty.Count==4,"Idle frame needs opaque anatomy rows and transparent margins.");
        double width=pet.Width,height=pet.Height,heading=Field<double>(pet,"heading"),target=Field<double>(pet,"targetHeading");
        double until=Field<double>(pet,"stateUntil");bool autoHide=settings.AutoHideFullscreen;
        try
        {
            settings.AutoHideFullscreen=false;Set(pet,"stateUntil",double.PositiveInfinity);
            foreach(double facing in new[]{-52.0,52.0})
            {
                Set(pet,"heading",facing);Set(pet,"targetHeading",facing);model.Heading.Angle=facing;model.Animate(0,"idle",0);
                typeof(PetWindow).GetMethod("UpdateBreedHitArea",Private).Invoke(pet,null);
                OrthographicCamera camera=(OrthographicCamera)viewport.Camera;
                foreach(double scale in new[]{.4,.57,1.0,1.6})
                {
                    pet.Width=320*scale;pet.Height=350*scale;pet.UpdateLayout();
                    foreach(Point source in solid)
                    {
                        Point3D world=FramePoint(sprite,source);Point local=Project(world,camera,306,260);
                        Require(model.ArtworkHitTest(world.X,world.Y),"Frame sampler missed an opaque dog pixel.");
                        Require(ReferenceEquals(pet.InputHitTest(input.TranslatePoint(local,pet)),input),"Corgi silhouette missed input at "+scale+", "+local+".");
                    }
                    foreach(Point source in empty)
                    {
                        Point3D world=FramePoint(sprite,source);Point local=Project(world,camera,306,260);
                        Require(!model.ArtworkHitTest(world.X,world.Y),"Frame sampler intercepted a transparent pixel.");
                        Require(!ReferenceEquals(pet.InputHitTest(input.TranslatePoint(local,pet)),input),"Transparent margin intercepted input at "+scale+", "+local+".");
                    }
                }
            }
        }
        finally
        {
            pet.Width=width;pet.Height=height;pet.UpdateLayout();Set(pet,"heading",heading);Set(pet,"targetHeading",target);Set(pet,"stateUntil",until);
            model.Heading.Angle=heading;model.Animate(0,"idle",0);typeof(PetWindow).GetMethod("UpdateBreedHitArea",Private).Invoke(pet,null);
            settings.AutoHideFullscreen=autoHide;
        }
        Console.WriteLine("PASS: actual frame alpha input at four scales and both headings; transparent margins pass through.");
    }

    static void CheckCorgiRoam(PetWindow pet, PetSettings settings)
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
                "Frame-animated Corgi must not roam or erase the saved Westie roaming preference.");
            typeof(PetWindow).GetMethod("Act", Private).Invoke(pet, new object[] { "walk" });
            Require(Field<string>(pet, "mode") != "walk" && settings.Roam, "Manual walk must not slide the frame-animated Corgi or clear the preference.");
            pet.SwitchBreed("westie");
            Require(!Field<WestieModel>(pet, "model").IsPreRendered && settings.Roam, "Westie must recover the retained roaming preference.");
            Set(pet, "mode", "walk"); pet.SwitchBreed("corgi");
            Require(Field<string>(pet, "mode") == "idle" && settings.Roam,
                "Switching from a walking Westie must stop the frame-animated Corgi without deleting the preference.");
        }
        finally
        {
            settings.Roam = roam; settings.AutoHideFullscreen = autoHide;
            Set(pet, "mode", mode); Set(pet, "stateUntil", until); Set(pet, "nextAction", next);
        }
        Console.WriteLine("PASS: Frame-animated Corgi cannot roam; saved Westie roam preference survives model switches.");
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
