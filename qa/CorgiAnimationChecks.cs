using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WestiePet;

// Checks the shipped frame player and decoded pixels, independently of the
// exporter. No real desktop window, settings, tray icon or timer is started.
static class CorgiAnimationChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly string Output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "corgi-animation");
    static readonly List<string> Results = new List<string>();
    static double time = 100;

    [STAThread]
    static int Main()
    {
        Directory.CreateDirectory(Output);
        try
        {
            CheckEmbeddedResource();
            CheckIndexedFrames();
            CheckClips();
            CheckSleepWake();
            CheckRestartAndPause();
            CheckMirrorAndInput();
            File.WriteAllLines(Path.Combine(Output, "result.txt"), Results.ToArray());
            Console.WriteLine("PASS: Corgi animation playback, pose continuity, mirroring and alpha input.");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), error.ToString());
            Console.Error.WriteLine("CORGI_ANIMATION_ERROR: " + error); return 1;
        }
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static void Pass(string message) { Results.Add("PASS: " + message); Console.WriteLine("PASS: " + message); }
    static CorgiSprite Sprite(WestieModel model)
    { return (CorgiSprite)typeof(WestieModel).GetField("artwork", Private).GetValue(model); }
    static T Value<T>(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, Private);
        if (property == null) throw new MissingMemberException(target.GetType().Name, name);
        return (T)Convert.ChangeType(property.GetValue(target, null), typeof(T));
    }
    static void Step(WestieModel model, string state, double age)
    { time += 1.0 / 30; model.Animate(time, state, age); }
    static void Begin(WestieModel model, string state)
    { model.BeginAction(state); Step(model, state, 0); }
    static GeometryModel3D Geometry(Model3D node)
    {
        GeometryModel3D geometry = node as GeometryModel3D; if (geometry != null) return geometry;
        Model3DGroup group = node as Model3DGroup;
        if (group != null) foreach (Model3D child in group.Children)
        { GeometryModel3D found = Geometry(child); if (found != null) return found; }
        return null;
    }
    static BitmapSource Bitmap(CorgiSprite sprite)
    {
        GeometryModel3D geometry = Geometry(sprite.Model);
        Require(geometry != null, "Missing frame billboard.");
        MaterialGroup material = geometry.Material as MaterialGroup;
        Require(material != null, "Missing alpha-compatible frame material.");
        foreach (Material child in material.Children)
        {
            EmissiveMaterial emission = child as EmissiveMaterial;
            ImageBrush brush = emission == null ? null : emission.Brush as ImageBrush;
            if (brush != null && brush.ImageSource is BitmapSource) return (BitmapSource)brush.ImageSource;
        }
        throw new InvalidOperationException("The displayed Corgi frame is not a decoded bitmap.");
    }
    static byte[] Pixels(CorgiSprite sprite)
    {
        BitmapSource bitmap = new FormatConvertedBitmap(Bitmap(sprite), PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }
    static int Difference(byte[] a, byte[] b)
    {
        Require(a.Length == b.Length, "Animation frame size changed."); int count = 0;
        for (int index = 0; index < a.Length; index += 4)
            if (Math.Abs(a[index]-b[index])+Math.Abs(a[index+1]-b[index+1])+Math.Abs(a[index+2]-b[index+2])+
                Math.Abs(a[index+3]-b[index+3]) > 40) count++;
        return count;
    }
    static void Save(CorgiSprite sprite, string name)
    {
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Bitmap(sprite)));
        using (FileStream file = File.Create(Path.Combine(Output, name + ".png"))) encoder.Save(file);
    }
    static void CheckEmbeddedResource()
    {
        string source = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            "assets", "corgi-animation.frames");
        using (Stream embedded = typeof(WestieModel).Assembly.GetManifestResourceStream("WestiePet.CorgiAnimation"))
        using (Stream original = File.OpenRead(source))
        using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
        {
            Require(embedded != null && embedded.Length > 1000000, "Missing embedded animation frames.");
            Require(Convert.ToBase64String(hash.ComputeHash(embedded)) == Convert.ToBase64String(hash.ComputeHash(original)),
                "Embedded animation resource differs from its build input.");
        }
        Pass("embedded animation data matches the exporter output byte for byte");
    }
    static void CheckClips()
    {
        string[] states = { "idle", "blink", "pet", "feed", "play", "sleep", "wake" };
        string[] names = { "Idle", "Blink", "Pet", "Eat", "PlayBall", "Sleep", "Wake" };
        double[] ages = { 1.2, .6, 2, 1.7, 1.5, 5, 2 };
        for (int index = 0; index < states.Length; index++)
        {
            WestieModel model = new WestieModel("corgi"); CorgiSprite sprite = Sprite(model);
            Require(sprite != null && model.IsPreRendered, "Production Corgi must use the real frame sequence.");
            if (states[index] == "wake") { Begin(model, "sleep"); Step(model, "sleep", 5.1); }
            Begin(model, states[index]); byte[] initial = Pixels(sprite); Step(model, states[index], ages[index]);
            Require(string.Equals(Value<string>(sprite, "CurrentClip"), names[index], StringComparison.OrdinalIgnoreCase),
                "State " + states[index] + " selected the wrong clip.");
            int frame = Value<int>(sprite, "CurrentFrame"), count = Value<int>(sprite, "FrameCount");
            Require(count > 1 && frame >= 0 && frame < count, names[index] + " has no valid frame progression.");
            Require(Difference(initial, Pixels(sprite)) > 8, names[index] + " is still a static picture.");
            BitmapSource bitmap = Bitmap(sprite); Require(bitmap.PixelWidth >= 306 && bitmap.PixelHeight >= 260, "Frame resolution is too small.");
            byte[] pixels = Pixels(sprite); int transparent = 0, opaque = 0;
            for (int at = 3; at < pixels.Length; at += 4) { if (pixels[at] == 0) transparent++; if (pixels[at] > 200) opaque++; }
            Require(transparent > 1000 && opaque > 1000, names[index] + " lost its alpha silhouette.");
            Save(sprite, names[index]);
            if (states[index] == "blink") Require(sprite.EyeClosure > .9, "Blink did not fully close the eyes.");
        }
        Pass("all seven real clips produce decoded frame changes with transparent silhouettes");
    }
    static void CheckSleepWake()
    {
        WestieModel model = new WestieModel("corgi"); CorgiSprite sprite = Sprite(model);
        Begin(model, "sleep"); Step(model, "sleep", 5.1); byte[] asleep = Pixels(sprite);
        int terminal = Value<int>(sprite, "CurrentFrame"); Step(model, "sleep", 30);
        Require(Value<int>(sprite, "CurrentFrame") == terminal && Difference(asleep, Pixels(sprite)) == 0,
            "Sleep must hold its terminal pose instead of looping back to sitting.");
        Require(sprite.EyeClosure > .9, "Sleeping eyes reopened.");
        Begin(model, "wake"); Require(Difference(asleep, Pixels(sprite)) <= 8, "Wake starts with a visible pose jump.");
        Step(model, "wake", 4.0); byte[] awakened = Pixels(sprite);
        WestieModel rest = new WestieModel("corgi"); Begin(rest, "idle");
        Require(Difference(awakened, Pixels(Sprite(rest))) <= 12, "Wake does not recover the shared seated rest pose.");
        Require(sprite.EyeClosure < .1, "Wake did not reopen the eyes.");
        Begin(model, "sleep"); Step(model, "sleep", 5.1); asleep = Pixels(sprite);
        Begin(model, "feed");
        Require(Value<bool>(sprite, "IsRecovering") && string.Equals(Value<string>(sprite, "CurrentClip"), "Wake", StringComparison.OrdinalIgnoreCase),
            "An interaction from sleep must play Wake before its own clip.");
        Require(Value<double>(sprite, "ActiveDuration") >= 8.9, "Feed deadline must include the full wake recovery.");
        Require(Difference(asleep, Pixels(sprite)) <= 8, "Sleeping interaction begins with a pose jump.");
        Step(model, "feed", 2); Require(string.Equals(Value<string>(sprite, "CurrentClip"), "Wake", StringComparison.OrdinalIgnoreCase), "Feed began before Wake completed.");
        Step(model, "feed", 4.1); Require(!Value<bool>(sprite, "IsRecovering") && string.Equals(Value<string>(sprite, "CurrentClip"), "Eat", StringComparison.OrdinalIgnoreCase), "Feed did not follow completed Wake.");
        Pass("sleep holds, wake is continuous, and sleeping interactions include recovery in their deadlines");
    }
    static void CheckRestartAndPause()
    {
        WestieModel model = new WestieModel("corgi"); CorgiSprite sprite = Sprite(model);
        Begin(model, "pet"); byte[] first = Pixels(sprite); Step(model, "pet", 2);
        int changed = Value<int>(sprite, "CurrentFrame"); long version = Value<long>(sprite, "FrameVersion");
        Require(changed > 0, "Pet clip did not advance.");
        time += 10; Step(model, "pet", 2);
        Require(Value<int>(sprite, "CurrentFrame") == changed && Value<long>(sprite, "FrameVersion") == version,
            "A frozen action age must not advance due to unrelated global clock time.");
        Begin(model, "pet"); Require(Value<int>(sprite, "CurrentFrame") == 0 && Difference(first, Pixels(sprite)) <= 8,
            "Repeating the same action must restart its clip.");
        Begin(model, "feed"); Step(model, "feed", 1.7);
        Require(string.Equals(Value<string>(sprite, "CurrentClip"), "Eat", StringComparison.OrdinalIgnoreCase), "A changed action reused the prior clip.");
        Require(Value<long>(sprite, "FrameVersion") > version, "A real frame replacement did not refresh its version.");
        Pass("action age controls playback, repeated actions restart, and interruptions select fresh clips");
    }
    static Point3D ScenePoint(CorgiSprite sprite, Point pixel)
    {
        GeometryModel3D geometry = Geometry(sprite.Model); MeshGeometry3D mesh = (MeshGeometry3D)geometry.Geometry;
        BitmapSource bitmap = Bitmap(sprite); Point uv = new Point(pixel.X/bitmap.PixelWidth,pixel.Y/bitmap.PixelHeight);
        for (int at=0;at<mesh.TriangleIndices.Count;at+=3)
        {
            int ia=mesh.TriangleIndices[at],ib=mesh.TriangleIndices[at+1],ic=mesh.TriangleIndices[at+2];
            Point a=mesh.TextureCoordinates[ia],b=mesh.TextureCoordinates[ib],c=mesh.TextureCoordinates[ic];
            double d=(b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);if(Math.Abs(d)<1e-12)continue;
            double wa=((b.Y-c.Y)*(uv.X-c.X)+(c.X-b.X)*(uv.Y-c.Y))/d,wb=((c.Y-a.Y)*(uv.X-c.X)+(a.X-c.X)*(uv.Y-c.Y))/d,wc=1-wa-wb;
            if(wa < -1e-8 || wb < -1e-8 || wc < -1e-8)continue;
            Point3D pa=mesh.Positions[ia],pb=mesh.Positions[ib],pc=mesh.Positions[ic];
            Point3D point=new Point3D(pa.X*wa+pb.X*wb+pc.X*wc,pa.Y*wa+pb.Y*wb+pc.Y*wc,pa.Z*wa+pb.Z*wb+pc.Z*wc);
            return sprite.Model.Transform.Transform(geometry.Transform.Transform(point));
        }
        throw new InvalidOperationException("Pixel is outside frame UVs.");
    }
    static void CheckMirrorAndInput()
    {
        WestieModel model=new WestieModel("corgi");CorgiSprite sprite=Sprite(model);Begin(model,"idle");
        BitmapSource bitmap=Bitmap(sprite);byte[] pixels=Pixels(sprite);Point opaque=new Point();bool found=false;
        for(int y=bitmap.PixelHeight/3;y<bitmap.PixelHeight*2/3&&!found;y++)
        for(int x=bitmap.PixelWidth/3;x<bitmap.PixelWidth*2/3&&!found;x++)
            if(pixels[(y*bitmap.PixelWidth+x)*4+3]>240){opaque=new Point(x+.5,y+.5);found=true;}
        Require(found,"No opaque torso sample in idle frame.");
        Point3D right=new Point3D(),left=new Point3D();
        foreach(double heading in new[]{52.0,-52.0})
        {
            model.Heading.Angle=heading;Step(model,"idle",0);Point3D point=ScenePoint(sprite,opaque);
            Require(model.ArtworkHitTest(point.X,point.Y),"Mirrored alpha sampler missed an opaque body pixel.");
            Point3D empty=ScenePoint(sprite,new Point(2.5,2.5));Require(!model.ArtworkHitTest(empty.X,empty.Y),"Transparent margin intercepted input.");
            if(heading>0)right=point;else left=point;
        }
        Require(Math.Abs(right.X+left.X)<1e-8 && Math.Abs(right.Y-left.Y)<1e-8,"Facing must mirror the same frame consistently.");
        model.Heading.Angle=52;Begin(model,"play");Step(model,"play",2.5);bitmap=Bitmap(sprite);pixels=Pixels(sprite);
        int blue=0;
        for(int y=0;y<bitmap.PixelHeight;y++)for(int x=0;x<bitmap.PixelWidth;x++)
        {
            int at=(y*bitmap.PixelWidth+x)*4;
            if(pixels[at+3]<240||pixels[at]<pixels[at+1]+25||pixels[at+1]<pixels[at+2]+20||pixels[at+2]>110)continue;
            Point3D point=ScenePoint(sprite,new Point(x+.5,y+.5));
            Require(!sprite.IsDogPixel(x,y),"The ball was included in the dog-only input mask.");
            bool nearDog=false;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)nearDog|=sprite.IsDogPixel(x+dx,y+dy);
            if(!nearDog)Require(!model.ArtworkHitTest(point.X,point.Y),"The rendered ball must not intercept a desktop pet click.");blue++;
        }
        Require(blue>30,"The PlayBall contact/roll clip contains no visible ball pixels.");
        Pass("mirrored dog pixels hit, transparent pixels pass through, and the rendered ball is excluded from pet input");
    }

    sealed class IndexedFrame
    {
        internal string Clip;
        internal int Index, PngLength, MaskLength;
        internal long PngOffset, MaskOffset;
    }
    static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    static void CheckIndexedFrames()
    {
        // Independent decoding checks every indexed payload and mask. The
        // source-alpha check catches a bad mask even if the player accepts it.
        using(Stream stream=typeof(WestieModel).Assembly.GetManifestResourceStream("WestiePet.CorgiAnimation"))
        using(BinaryReader reader=new BinaryReader(stream))
        {
            Require(reader.ReadInt32()==0x46414357 && reader.ReadInt32()==1,"Invalid exported frame header.");
            int width=reader.ReadInt32(),height=reader.ReadInt32();double span=reader.ReadDouble(),fps=reader.ReadDouble();
            string sourceHash=reader.ReadString();int clipCount=reader.ReadInt32();
            Require(width>=306 && height>=260 && Finite(span) && span>0 && fps==12,"Invalid frame size/span/rate.");
            Require(sourceHash.Length==64 && sourceHash==CorgiSprite.SourceRigSha256,"Rig provenance hash was lost.");
            HashSet<string> names=new HashSet<string>();List<IndexedFrame> entries=new List<IndexedFrame>();
            for(int clip=0;clip<clipCount;clip++)
            {
                string name=reader.ReadString();double duration=reader.ReadDouble();int count=reader.ReadInt32();
                Require(names.Add(name) && Finite(duration) && duration>0 && count>1,"Invalid or duplicate clip.");
                double last=-1;
                for(int frame=0;frame<count;frame++)
                {
                    double age=reader.ReadDouble();
                    Require(Finite(age) && age>last && age>=0 && age<=duration+.0001,"Frame times must increase inside the clip.");
                    if(frame==0)Require(age==0,"A clip must include its initial pose.");
                    if(frame==count-1)Require(Math.Abs(age-duration)<.0001,"A clip must include its terminal pose.");
                    last=age;
                    for(int field=0;field<6;field++)
                    {
                        float value=reader.ReadSingle();Require(Finite(value),"Non-finite anchor or face pose metadata.");
                        if(field>=4)Require(value>=-.001 && value<=1.001,"Invalid eye or sleep pose metadata.");
                    }
                    int left=reader.ReadInt32(),top=reader.ReadInt32(),right=reader.ReadInt32(),bottom=reader.ReadInt32();
                    Require(left>=0 && top>=0 && right<=width && bottom<=height && left<right && top<bottom,"Invalid dog silhouette bounds.");
                    entries.Add(new IndexedFrame {Clip=name,Index=frame,PngOffset=reader.ReadInt64(),PngLength=reader.ReadInt32(),MaskOffset=reader.ReadInt64(),MaskLength=reader.ReadInt32()});
                }
            }
            Require(names.SetEquals(CorgiSprite.ClipNames) && entries.Count==333 && CorgiSprite.TotalFrameCount==333,"Export must contain all seven clips and 333 frames.");
            long headerEnd=stream.Position;
            foreach(IndexedFrame entry in entries)
            {
                Require(entry.PngOffset>=headerEnd && entry.PngLength>20 && entry.PngOffset+entry.PngLength<=stream.Length,
                    "PNG payload is outside the frame resource.");
                Require(entry.MaskOffset>=headerEnd && entry.MaskLength==(width*height+7)/8 && entry.MaskOffset+entry.MaskLength<=stream.Length,
                    "Dog mask payload is outside the frame resource.");
                stream.Position=entry.PngOffset;byte[] png=reader.ReadBytes(entry.PngLength);
                BitmapSource decoded;
                using(MemoryStream memory=new MemoryStream(png,false))decoded=BitmapFrame.Create(memory,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                Require(decoded.PixelWidth==width && decoded.PixelHeight==height,"An indexed frame has the wrong dimensions.");
                decoded=new FormatConvertedBitmap(decoded,PixelFormats.Bgra32,null,0);
                byte[] pixels=new byte[width*height*4];decoded.CopyPixels(pixels,width*4,0);
                stream.Position=entry.MaskOffset;byte[] mask=reader.ReadBytes(entry.MaskLength);int body=0,empty=0;
                for(int pixel=0;pixel<width*height;pixel++)
                {
                    bool hit=(mask[pixel>>3]&(1<<(pixel&7)))!=0;
                    if(pixels[pixel*4+3]==0){Require(!hit,"Dog mask intercepts a fully transparent pixel.");empty++;}
                    if(hit)body++;
                }
                Require(body>1000 && empty>1000,"An indexed animation frame lost its dog mask or transparent silhouette.");
            }
        }
        Pass("all 333 PNG payloads, pose metadata and dog-only masks independently decode and remain valid");
    }
}
