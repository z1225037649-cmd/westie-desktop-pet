using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WestiePet
{
    // Actual skeleton/morph renders replace the former locally deformed image.
    // Food/ball share the frame camera, while input samples dog-only masks.
    internal sealed class CorgiSprite
    {
        const double CenterY = 1.10;
        internal readonly Model3DGroup Model = new Model3DGroup();
        internal int TriangleCount { get { return 2; } }
        readonly ImageBrush texture = new ImageBrush { Stretch = Stretch.Fill };
        readonly ScaleTransform3D facing = new ScaleTransform3D(1, 1, 1);
        readonly List<Segment> performance = new List<Segment>();
        string requestedState = "idle";
        double previousAge, clipAge, mirror = 1;
        bool explicitlyStarted;
        CorgiFrames.Clip currentClip;
        CorgiFrames.Info currentInfo;
        CorgiFrames.Frame currentFrame;
        sealed class Segment { internal CorgiFrames.Clip Clip; internal double Start; internal bool Recovery; }
        internal string CurrentClip { get { return currentClip.Name; } }
        internal int CurrentFrame { get { return currentInfo.Index; } }
        internal int FrameCount { get { return currentClip.Frames.Length; } }
        internal int FrameVersion { get; private set; }
        internal double ActiveDuration { get; private set; }
        internal bool IsRecovering { get; private set; }
        internal double EyeClosure { get { return currentInfo.EyeClosure; } }
        internal double SleepPose { get { return currentInfo.SleepPose; } }
        internal BitmapSource CurrentBitmap { get { return currentFrame.Bitmap; } }
        internal double ClipTime { get { return clipAge; } }
        internal double PlayActionTime { get { return CurrentClip == "PlayBall" ? clipAge : 0; } }
        internal bool PlayReady { get { return CurrentClip == "PlayBall"; } }
        internal Point3D Mouth { get { return ImagePointToScene(currentInfo.Mouth.X, currentInfo.Mouth.Y); } }
        internal Point3D FrontPaw { get { return ImagePointToScene(currentInfo.Paw.X, currentInfo.Paw.Y); } }
        internal static string SourceRigSha256 { get { return CorgiFrames.SourceHash; } }
        internal static int TotalFrameCount { get { return CorgiFrames.FrameCount; } }
        internal static string[] ClipNames { get { return new[] { "Idle", "Blink", "Pet", "Eat", "PlayBall", "Sleep", "Wake" }; } }
        internal CorgiSprite()
        {
            double half = CorgiFrames.Span / 2;
            MeshGeometry3D mesh = new MeshGeometry3D {
                Positions = new Point3DCollection { new Point3D(-half, CenterY-half, 0), new Point3D(half, CenterY-half, 0), new Point3D(half, CenterY+half, 0), new Point3D(-half, CenterY+half, 0) },
                TextureCoordinates = new PointCollection { new Point(0,1), new Point(1,1), new Point(1,0), new Point(0,0) },
                Normals = new Vector3DCollection { new Vector3D(0,0,1), new Vector3D(0,0,1), new Vector3D(0,0,1), new Vector3D(0,0,1) },
                TriangleIndices = new Int32Collection { 0,1,2,0,2,3 } };
            mesh.Freeze(); RenderOptions.SetBitmapScalingMode(texture, BitmapScalingMode.HighQuality);
            MaterialGroup material = new MaterialGroup();
            // Black diffuse preserves alpha; emission retains the baked lighting.
            material.Children.Add(new DiffuseMaterial(texture) { Color=Colors.Black, AmbientColor=Colors.Black });
            material.Children.Add(new EmissiveMaterial(texture));
            Model.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial=material }); Model.Transform=facing;
            performance.Add(new Segment { Clip=CorgiFrames.Clips["Idle"] }); Select(CorgiFrames.Clips["Idle"],0,false);
        }
        internal Point3D ImagePointToScene(double x,double y)
        { return new Point3D(mirror*(x/CorgiFrames.Width-.5)*CorgiFrames.Span, CenterY+(.5-y/CorgiFrames.Height)*CorgiFrames.Span,0); }
        internal Rect3D Bounds {
            get {
                Point3D a=ImagePointToScene(currentInfo.Bounds.Left,currentInfo.Bounds.Top), b=ImagePointToScene(currentInfo.Bounds.Right,currentInfo.Bounds.Bottom);
                return new Rect3D(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),0,Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y),0);
            }
        }
        static string ClipFor(string state)
        { switch(state) { case "pet":return "Pet"; case "feed":return "Eat"; case "play":return "PlayBall"; case "sleep":return "Sleep"; case "blink":return "Blink"; default:return "Idle"; } }
        internal void BeginAction(string state)
        {
            state=state??"idle"; double sleep=currentInfo==null?0:SleepPose;
            double oldTime=clipAge; string oldClip=currentClip==null?"Idle":CurrentClip; performance.Clear();
            if(state!="sleep" && (sleep>.0001 || oldClip=="Wake")) {
                CorgiFrames.Clip wake=CorgiFrames.Clips["Wake"]; double start=0,distance=double.MaxValue;
                if(oldClip=="Wake") start=oldTime;
                else foreach(CorgiFrames.Info frame in wake.Frames) {
                    double delta=Math.Abs(frame.SleepPose-sleep); if(delta<distance) { start=frame.Time;distance=delta; }
                }
                performance.Add(new Segment { Clip=wake,Start=start,Recovery=true });
            }
            CorgiFrames.Clip target=CorgiFrames.Clips[ClipFor(state)];
            performance.Add(new Segment { Clip=target,Start=state=="sleep"&&oldClip=="Sleep"?oldTime:0 });
            ActiveDuration=0; foreach(Segment segment in performance) if(segment.Clip.Name!="Idle") ActiveDuration+=segment.Clip.Duration-segment.Start;
            requestedState=state; previousAge=0; explicitlyStarted=true;
            Segment first=performance[0]; Select(first.Clip,first.Start,first.Recovery);
        }
        internal void Update(double time,string state,double stateTime,double heading,bool walkingPaused)
        {
            state=state??"idle";double age=Finite(stateTime)?Math.Max(0,stateTime):0;
            if(!explicitlyStarted && (state!=requestedState || age+.0001<previousAge)) BeginAction(state);
            explicitlyStarted=false;previousAge=age;
            double nextMirror=heading<0?-1:1;if(nextMirror!=mirror) { mirror=nextMirror;facing.ScaleX=mirror;FrameVersion++; }
            double remaining=age;
            foreach(Segment segment in performance) {
                double length=segment.Clip.Duration-segment.Start;
                if(remaining<=length || segment.Clip.Name=="Sleep") { Select(segment.Clip,Math.Min(segment.Clip.Duration,segment.Start+remaining),segment.Recovery);return; }
                if(segment.Clip.Name=="Idle") { Select(segment.Clip,remaining%segment.Clip.Duration,false);return; }
                remaining-=length;
            }
            CorgiFrames.Clip idle=CorgiFrames.Clips["Idle"];Select(idle,remaining%idle.Duration,false);
        }
        void Select(CorgiFrames.Clip clip,double age,bool recovery)
        {
            age=Math.Max(0,Math.Min(clip.Duration,age));int index=0;double best=double.MaxValue;
            for(int i=0;i<clip.Frames.Length;i++) { double distance=Math.Abs(clip.Frames[i].Time-age);if(distance<best) { index=i;best=distance; } }
            CorgiFrames.Info info=clip.Frames[index];clipAge=age;IsRecovering=recovery;
            if(currentInfo!=info) { currentClip=clip;currentInfo=info;currentFrame=CorgiFrames.Get(info);texture.ImageSource=currentFrame.Bitmap;FrameVersion++; }
        }
        internal bool IsDogPixel(int x,int y)
        {
            if(x<0 || y<0 || x>=CorgiFrames.Width || y>=CorgiFrames.Height)return false;
            int bit=y*CorgiFrames.Width+x;return (currentFrame.HitBits[bit>>3]&(1<<(bit&7)))!=0;
        }
        internal bool HitTest(double worldX,double worldY)
        {
            if(!Finite(worldX)||!Finite(worldY))return false;
            int x=(int)Math.Floor((worldX*mirror/CorgiFrames.Span+.5)*CorgiFrames.Width), y=(int)Math.Floor((.5-(worldY-CenterY)/CorgiFrames.Span)*CorgiFrames.Height);
            if(x<0||y<0||x>=CorgiFrames.Width||y>=CorgiFrames.Height)return false;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)if(IsDogPixel(x+dx,y+dy))return true;
            return false;
        }
        static bool Finite(double value) { return !double.IsNaN(value)&&!double.IsInfinity(value); }
    }
}
