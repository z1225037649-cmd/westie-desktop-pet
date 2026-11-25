using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WestiePet
{
    // The user's generated volumetric dog is skinned in bind space. Both its
    // textured surface and the added fur geometry follow the same bone poses.
    internal sealed class GeneratedWestie
    {
        internal readonly Model3DGroup Model = new Model3DGroup();
        internal int TriangleCount { get; private set; }
        internal Rect3D Bounds { get { return heading.TransformBounds(bindBounds); } }
        internal int HitPointCount { get { return hitVertices.Length; } }
        internal double EyeClosure { get { return eyeClosure; } }
        internal double SleepPose { get { return sleepPose; } }
        internal double PlayActionTime { get; private set; }
        internal bool PlayReady { get; private set; }
        internal double HeadYaw { get { return headYaw; } }
        internal double HeadTilt { get { return headTilt; } }
        internal double HeadNod { get { return headNod; } }
        internal double LimbPhase { get { return walkPhase; } }
        internal double TailAngle { get { return tailAngle; } }
        internal Point3D Mouth { get { return World(DeformAnchor(anchors[0], 7)); } }
        internal Point3D FrontPaw { get { return World(DeformAnchor(anchors[1], 2)); } }
        internal Point3D RawFrontPaw { get { return DeformAnchor(anchors[1], 2); } }
        // Contact is a bind-space paw transformed to the fixed release pose.
        // It stays stationary while the actual paw lifts towards the ball.
        internal Point3D PlayContactPaw
        {
            get
            {
                Point3D p = Pose(pivots[2], new Vector3D(PawGesture(.72), 0, 0), new Vector3D()).Transform(anchors[1]);
                return World(p);
            }
        }
        internal Point3D[] EyeCenters { get { return new[] { World(DeformAnchor(anchors[2], 1)), World(DeformAnchor(anchors[3], 1)) }; } }
        internal int VertexCount { get; private set; }

        readonly List<Surface> surfaces = new List<Surface>();
        readonly int[] hitVertices;
        readonly SleepSegment[,] sleepSegments = new SleepSegment[4, 2];
        Rect3D bindBounds;
        readonly Matrix3D[] bones = new Matrix3D[9];
        readonly Matrix3D[] cervicalPoses = new Matrix3D[257];
        readonly Point3D[] pivots = new Point3D[9];
        readonly Point3D[] anchors = new Point3D[6];
        readonly ScaleTransform3D glintVisibility = new ScaleTransform3D(1, 1, 1);
        readonly AxisAngleRotation3D facing = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 52);
        readonly RotateTransform3D heading;
        double previousTime = double.NaN, previousStateTime, sleepPose, rootLift;
        double eyeClosure, headYaw = -8, headTilt, headNod = -1;
        double lookX, lookY, attentionStrength, targetX, targetY;
        double nextBlink = 3.2, blinkStarted = -10, blinkDuration = .24;
        double walkPhase, tailAngle, jawAngle;
        double playStartTime;
        bool playRecovering;
        bool deferGeometry;
        string previousState, pendingAction;
        readonly Random random = new Random(199);

        struct Vertex
        {
            internal Point3D Position;
            internal Vector3D Normal;
            internal byte B0, B1, B2, B3;
            internal double W0, W1, W2, W3;
            internal Point3D SleepPosition;
            internal Vector3D SleepNormal;
            internal double CervicalWeight;
            internal ushort CervicalPoseIndex;
        }

        sealed class Surface
        {
            internal Vertex[] Bind;
            internal Point3D[] Positions;
            internal Vector3D[] Normals;
            internal MeshGeometry3D Mesh;
        }

        struct SleepSegment
        {
            internal Point3D RestA, RestB, TargetA, TargetB;
            internal double Cos, Sin;
        }
        struct SleepFrame
        {
            internal Point3D Rest, Target;
            internal double Cos, Sin, NormalYScale;
        }

        internal GeneratedWestie()
        {
            heading = new RotateTransform3D(facing);
            Model.Transform = heading;
            for (int i = 0; i < bones.Length; i++) bones[i] = Matrix3D.Identity;
            Material skin = SkinMaterial();
            // Fur samples the root's coat color so short hairs do not become
            // detached bright flecks against the textured sculpted surface.
            Material fur = ((MaterialGroup)skin).Children[0];
            using (Stream stream = typeof(GeneratedWestie).Assembly.GetManifestResourceStream("WestiePet.GeneratedMesh"))
            {
                if (stream == null) throw new InvalidOperationException("Missing generated Westie mesh resource.");
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != 0x31505747 || reader.ReadInt32() != 1)
                        throw new InvalidDataException("Unsupported generated Westie mesh format.");
                    if (reader.ReadInt32() != bones.Length) throw new InvalidDataException("Generated Westie bone count is invalid.");
                    for (int i = 0; i < pivots.Length; i++) pivots[i] = ReadPoint(reader);
                    if (reader.ReadInt32() != 6) throw new InvalidDataException("Generated Westie anchor count is invalid.");
                    for (int i = 0; i < anchors.Length; i++) anchors[i] = ReadPoint(reader);
                    int count = reader.ReadInt32();
                    if (count < 1 || count > 16) throw new InvalidDataException("Generated Westie surface count is invalid.");
                    for (int i = 0; i < count; i++) LoadSurface(reader, skin, fur);
                    if (stream.Position != stream.Length) throw new InvalidDataException("Trailing data in generated Westie mesh.");
                }
            }
            hitVertices = SelectHitVertices(surfaces[0].Bind);
            CreateSleepingLegs();
            AddEyeGlints();
            CacheSleepingBody();
            CacheCervicalJoint();
            Update(0, "idle", 0, 52, false);
        }

        static int[] SelectHitVertices(Vertex[] vertices)
        {
            // One actual skin vertex per small bind-space cell. Reusing the
            // deformed skin positions avoids a second rig or an offscreen draw.
            HashSet<long> cells = new HashSet<long>(); List<int> selected = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Point3D p = vertices[i].Position;
                long x = (long)Math.Floor(p.X / .028) + 1024;
                long y = (long)Math.Floor(p.Y / .028) + 1024;
                long z = (long)Math.Floor(p.Z / .028) + 1024;
                long cell = (x << 42) | (y << 21) | z;
                if (cells.Add(cell)) selected.Add(i);
            }
            return selected.ToArray();
        }

        internal void CopyHitPoints(Point3D[] destination)
        {
            if (destination == null || destination.Length != hitVertices.Length)
                throw new ArgumentException("Hit point buffer has the wrong length.");
            Point3D[] positions = surfaces[0].Positions;
            for (int i = 0; i < hitVertices.Length; i++) destination[i] = positions[hitVertices[i]];
            heading.Transform(destination);
        }

        void AddEyeGlints()
        {
            List<Vertex> vertices = new List<Vertex>(); List<int> indices = new List<int>();
            for (int eye = 2; eye <= 3; eye++)
            {
                Point3D center = anchors[eye] + new Vector3D(-.018, .012, .038);
                int start = vertices.Count;
                const int rows = 6, columns = 10;
                for (int row = 0; row <= rows; row++) for (int col = 0; col <= columns; col++)
                {
                    double latitude = Math.PI * row / rows, longitude = Math.PI * 2 * col / columns;
                    Vector3D normal = new Vector3D(Math.Sin(latitude) * Math.Cos(longitude), Math.Cos(latitude), Math.Sin(latitude) * Math.Sin(longitude));
                    vertices.Add(new Vertex { Position = center + normal * .0045, Normal = normal, B0 = 1, W0 = 1 });
                }
                for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++)
                {
                    int a = start + row * (columns + 1) + col, b = a + columns + 1;
                    indices.Add(a); indices.Add(b); indices.Add(a + 1);
                    indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
                }
            }
            Surface surface = new Surface { Bind = vertices.ToArray(), Positions = new Point3D[vertices.Count], Normals = new Vector3D[vertices.Count], Mesh = new MeshGeometry3D() };
            surface.Mesh.TriangleIndices = new Int32Collection(indices); surface.Mesh.TriangleIndices.Freeze();
            Material glint = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(245, 246, 244))); glint.Freeze();
            Model.Children.Add(new GeometryModel3D(surface.Mesh, glint) { BackMaterial = glint, Transform = glintVisibility });
            surfaces.Add(surface); TriangleCount += indices.Count / 3; VertexCount += vertices.Count;
        }

        static Point3D ReadPoint(BinaryReader reader)
        {
            double x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
            if (!Finite(x) || !Finite(y) || !Finite(z)) throw new InvalidDataException("Non-finite generated Westie coordinate.");
            return new Point3D(x, y, z);
        }

        void LoadSurface(BinaryReader reader, Material skin, Material fur)
        {
            int material = reader.ReadInt32(), vertexCount = reader.ReadInt32(), indexCount = reader.ReadInt32();
            if (material < 0 || material > 1 || vertexCount < 3 || vertexCount > 1000000
                || indexCount < 3 || indexCount > 6000000 || indexCount % 3 != 0)
                throw new InvalidDataException("Invalid generated Westie surface dimensions.");
            Surface surface = new Surface { Bind = new Vertex[vertexCount], Positions = new Point3D[vertexCount],
                Normals = new Vector3D[vertexCount], Mesh = new MeshGeometry3D() };
            Point[] uv = new Point[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                Vertex vertex = new Vertex();
                vertex.Position = ReadPoint(reader);
                Point3D normal = ReadPoint(reader); vertex.Normal = new Vector3D(normal.X, normal.Y, normal.Z);
                double u = reader.ReadSingle(), v = reader.ReadSingle();
                if (!Finite(u) || !Finite(v)) throw new InvalidDataException("Non-finite generated Westie UV.");
                uv[i] = new Point(u, v);
                vertex.B0 = ReadBone(reader); vertex.W0 = ReadWeight(reader);
                vertex.B1 = ReadBone(reader); vertex.W1 = ReadWeight(reader);
                vertex.B2 = ReadBone(reader); vertex.W2 = ReadWeight(reader);
                vertex.B3 = ReadBone(reader); vertex.W3 = ReadWeight(reader);
                double total = vertex.W0 + vertex.W1 + vertex.W2 + vertex.W3;
                if (total < .0001) throw new InvalidDataException("Generated Westie has an unweighted vertex.");
                vertex.W0 /= total; vertex.W1 /= total; vertex.W2 /= total; vertex.W3 /= total;
                surface.Bind[i] = vertex; surface.Positions[i] = vertex.Position; surface.Normals[i] = vertex.Normal;
            }
            int[] triangles = new int[indexCount];
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = reader.ReadInt32();
                if (triangles[i] < 0 || triangles[i] >= vertexCount) throw new InvalidDataException("Generated Westie triangle index is out of range.");
            }
            surface.Mesh.Positions = new Point3DCollection(surface.Positions);
            surface.Mesh.Normals = new Vector3DCollection(surface.Normals);
            surface.Mesh.TextureCoordinates = new PointCollection(uv);
            surface.Mesh.TextureCoordinates.Freeze();
            surface.Mesh.TriangleIndices = new Int32Collection(triangles); surface.Mesh.TriangleIndices.Freeze();
            Material surfaceMaterial = material == 0 ? skin : fur;
            Model.Children.Add(new GeometryModel3D(surface.Mesh, surfaceMaterial) { BackMaterial = surfaceMaterial });
            surfaces.Add(surface); TriangleCount += indexCount / 3; VertexCount += vertexCount;
        }

        static byte ReadBone(BinaryReader reader)
        {
            byte result = reader.ReadByte();
            if (result >= 9) throw new InvalidDataException("Generated Westie bone index is invalid.");
            return result;
        }

        static double ReadWeight(BinaryReader reader)
        {
            double result = reader.ReadSingle();
            if (!Finite(result) || result < 0) throw new InvalidDataException("Generated Westie bone weight is invalid.");
            return result;
        }

        static Material SkinMaterial()
        {
            BitmapImage image = new BitmapImage();
            using (Stream stream = typeof(GeneratedWestie).Assembly.GetManifestResourceStream("WestiePet.GeneratedTexture"))
            {
                if (stream == null) throw new InvalidOperationException("Missing generated Westie texture resource.");
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            }
            ImageBrush brush = new ImageBrush(image) { Stretch = Stretch.Fill, TileMode = TileMode.None };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality); brush.Freeze();
            MaterialGroup result = new MaterialGroup();
            result.Children.Add(new DiffuseMaterial(brush));
            result.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(19, 20, 21)), 25));
            result.Freeze(); return result;
        }

        internal void BeginAction(string state)
        {
            pendingAction = state;
            if (state == "play") { PlayReady = false; PlayActionTime = 0; }
        }

        internal void LookAt(double x, double y, double strength)
        {
            targetX = Finite(x) ? Clamp(x, -1, 1) : 0;
            targetY = Finite(y) ? Clamp(y, -1, 1) : 0;
            attentionStrength = Finite(strength) ? Clamp(strength, 0, 1) : 0;
        }

        internal void Update(double time, string state, double stateTime, double headingAngle, bool walkingPaused)
        {
            if (!Finite(time)) time = 0;
            if (!Finite(stateTime)) stateTime = 0;
            stateTime = Math.Max(0, stateTime);
            bool resetClock = double.IsNaN(previousTime) || time < previousTime;
            double dt = resetClock || time <= previousTime ? .042 : Math.Min(.10, time - previousTime);
            if (resetClock) { nextBlink = time + 3.2; blinkStarted = time - 10; }
            previousTime = time;
            bool sleeping = state == "sleep", playing = state == "play", petting = state == "pet", feeding = state == "feed";
            bool restarting = previousState != state || stateTime < previousStateTime || pendingAction == state;
            if (playing && restarting)
            {
                playRecovering = sleepPose > .01; playStartTime = 0;
            }
            if (pendingAction == state) pendingAction = null;
            previousState = state; previousStateTime = stateTime;
            sleepPose = Approach(sleepPose, sleeping ? 1 : 0, dt, 4.0);
            if (playing && playRecovering && stateTime >= 1.2 && sleepPose < .008)
            { playRecovering = false; playStartTime = stateTime; }
            PlayReady = playing && !playRecovering;
            PlayActionTime = PlayReady ? Math.Max(0, stateTime - playStartTime) : 0;

            bool walking = state == "walk" && sleepPose < .01;
            bool moving = walking && !walkingPaused;
            if (moving) walkPhase += dt * 7.4;
            double pet = petting ? Smooth(stateTime / .28) * Smooth((3.6 - stateTime) / .50) : 0;
            double feed = feeding ? Smooth(stateTime / .55) * Smooth((4.5 - stateTime) / .55) : 0;
            double chew = feeding && stateTime > .90 && stateTime < 3.95
                ? (.5 + .5 * Math.Sin((stateTime - .90) * Math.PI * 2 / .62)) : 0;
            double attention = sleeping || feeding || playing || petting ? 0 : attentionStrength;
            lookX = Approach(lookX, DeadZone(targetX) * attention, dt, 2.4);
            lookY = Approach(lookY, DeadZone(targetY) * attention, dt, 2.4);
            double playPrepare = PlayReady ? Smooth(PlayActionTime / .25) * (1 - Smooth((PlayActionTime - .80) / .55)) : 0;
            headYaw = sleeping ? Approach(headYaw, 0, dt, 24)
                : MoveAngle(headYaw, (-8 + lookX * 9 + pet * Math.Sin(stateTime * 4.4) * 7) * (1 - sleepPose), dt, petting ? 60 : 12);
            headTilt = sleeping ? Approach(headTilt, 0, dt, 24)
                : MoveAngle(headTilt, (pet * (-4 + Math.Sin(stateTime * 4.4) * 10) - lookX * 3) * (1 - sleepPose), dt, petting ? 65 : 9);
            headNod = MoveAngle(headNod, -1 - lookY * 5 + feed * (18 + chew * 5) + sleepPose * 19 + playPrepare * 5, dt,
                feeding || sleeping || sleepPose > .001 ? 55 : 14);
            if (!sleeping && time >= nextBlink)
            {
                blinkStarted = time; blinkDuration = .22 + random.NextDouble() * .04;
                nextBlink = time + 3.0 + random.NextDouble() * 3.1;
            }
            double blink = Blink((time - blinkStarted) / blinkDuration);
            eyeClosure = Math.Max(Smooth((sleepPose - .12) / .55), Math.Max(pet * .985, blink));
            jawAngle = feeding ? feed * (2 + chew * 12) : pet * .6;
            tailAngle = Approach(tailAngle, Math.Sin((petting || feeding ? stateTime : time) * (petting ? 11 : playing ? 8 : 4))
                * (petting ? 22 : playing ? 14 : feeding ? 13 : moving ? 8 : 3) * (1 - sleepPose), dt, 12);
            rootLift = -.30 * sleepPose + (moving ? (1 - Math.Cos(walkPhase * 2)) * .004 : Math.Sin(time * 2.0) * .0010) * (1 - sleepPose);

            double breath = Math.Sin(time * (sleeping ? 1.7 : 2.0)) * .0025;
            bones[0] = Matrix3D.Identity;
            bones[0].ScaleAt(new Vector3D(1 + breath * .5, 1 + breath, 1), new Point3D(0, .65, -.1));
            bones[0].OffsetY -= .015 * sleepPose;
            double neckNod = 45 * sleepPose;
            bones[8] = Pose(pivots[8], new Vector3D(neckNod, 0, 0), new Vector3D(0, -.08 * sleepPose, 0));
            Vector3D headRotation = new Vector3D(headNod - neckNod, headYaw, headTilt);
            double neckRadians = neckNod * Math.PI / 180, headClearance = .08 * sleepPose;
            Vector3D headOffset = new Vector3D(pet * Math.Sin(stateTime * 4.4) * .018,
                -feed * .040 + Math.Sin(neckRadians) * headClearance,
                feed * .025 + Math.Cos(neckRadians) * headClearance);
            bones[1] = Pose(pivots[1], headRotation, headOffset);
            bones[1].Append(bones[8]);
            for (int i = 0; i < cervicalPoses.Length; i++)
            {
                double amount = i / 256.0;
                cervicalPoses[i] = Pose(pivots[1], headRotation * amount, headOffset * amount);
                cervicalPoses[i].Append(bones[8]);
            }
            for (int i = 0; i < 4; i++)
            {
                double angle = walking ? Math.Sin(walkPhase + (i == 0 || i == 3 ? 0 : Math.PI)) * (i < 2 ? 21 : 18) : 0;
                if (i == 0 && PlayReady) angle = PawGesture(PlayActionTime);
                angle *= 1 - sleepPose;
                bones[i + 2] = Pose(pivots[i + 2], new Vector3D(angle, 0, 0), new Vector3D());
            }
            // Keep the short tail in its standing pose while the dog lies down.
            // The wag fades out; no sleep bend or root offset is applied.
            bones[6] = Pose(pivots[6], new Vector3D(0, 0, tailAngle), new Vector3D());
            bones[7] = Pose(pivots[7], new Vector3D(-jawAngle, 0, 0), new Vector3D());
            bones[7].Append(bones[1]);
            facing.Angle = Finite(headingAngle) ? headingAngle : 52;
            if (!deferGeometry) RefreshGeometry();
        }

        internal void Settle(double time, string state, double age, double headingAngle, bool walkingPaused)
        {
            // Advance easing without rebuilding/uploading the entire mesh 36
            // times inside the model selector's synchronous Click handler.
            // The caller must Animate before displaying or reading geometry;
            // that final update also synchronizes the food and ball props.
            deferGeometry = true;
            try { for (int i = 0; i < 36; i++) Update(time - (35 - i) / 30.0, state, age, headingAngle, walkingPaused); }
            finally { deferGeometry = false; }
        }

        internal void SetEyeClosure(double closure)
        {
            eyeClosure = Finite(closure) ? Clamp(closure, 0, 1) : 0;
            RefreshGeometry();
        }

        internal Point3D GetSkinnedPoint(int surfaceIndex, int vertexIndex)
        {
            return World(surfaces[surfaceIndex].Positions[vertexIndex]);
        }

        internal Point3D[] GetSkinnedPositions(int surfaceIndex)
        {
            Point3D[] result = (Point3D[])surfaces[surfaceIndex].Positions.Clone();
            for (int i = 0; i < result.Length; i++) result[i] = World(result[i]);
            return result;
        }

        void RefreshGeometry()
        {
            glintVisibility.ScaleX = glintVisibility.ScaleY = glintVisibility.ScaleZ = eyeClosure > .85 ? 0 : 1;
            double minX = double.PositiveInfinity, minY = minX, minZ = minX;
            double maxX = double.NegativeInfinity, maxY = maxX, maxZ = maxX;
            foreach (Surface surface in surfaces)
            {
                for (int i = 0; i < surface.Bind.Length; i++)
                {
                    Vertex vertex = surface.Bind[i];
                    Point3D bind = vertex.Position;
                    CloseEyes(ref bind);
                    double x = 0, y = 0, z = 0, nx = 0, ny = 0, nz = 0;
                    double legBlend = sleepPose;
                    Accumulate(bind, vertex.Normal, ref bones[vertex.B0], MovingWeight(vertex.B0, vertex.W0, legBlend, vertex.CervicalWeight), ref x, ref y, ref z, ref nx, ref ny, ref nz);
                    Accumulate(bind, vertex.Normal, ref bones[vertex.B1], MovingWeight(vertex.B1, vertex.W1, legBlend, vertex.CervicalWeight), ref x, ref y, ref z, ref nx, ref ny, ref nz);
                    Accumulate(bind, vertex.Normal, ref bones[vertex.B2], MovingWeight(vertex.B2, vertex.W2, legBlend, vertex.CervicalWeight), ref x, ref y, ref z, ref nx, ref ny, ref nz);
                    Accumulate(bind, vertex.Normal, ref bones[vertex.B3], MovingWeight(vertex.B3, vertex.W3, legBlend, vertex.CervicalWeight), ref x, ref y, ref z, ref nx, ref ny, ref nz);
                    if (vertex.CervicalWeight > 0)
                        Accumulate(bind, vertex.Normal, ref cervicalPoses[vertex.CervicalPoseIndex], vertex.CervicalWeight, ref x, ref y, ref z, ref nx, ref ny, ref nz);
                    // The root lowering is shared by skin, fur and landmarks.
                    x += vertex.SleepPosition.X * legBlend; y += vertex.SleepPosition.Y * legBlend; z += vertex.SleepPosition.Z * legBlend;
                    nx += vertex.SleepNormal.X * legBlend; ny += vertex.SleepNormal.Y * legBlend; nz += vertex.SleepNormal.Z * legBlend;
                    y += rootLift;
                    // The resting chest and coat meet the same floor as the
                    // paw pads while the body settles down.
                    if (sleepPose > .000001 && y < .025) y = .025;
                    surface.Positions[i] = new Point3D(x, y, z);
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
                    Vector3D normal = new Vector3D(nx, ny, nz);
                    if (normal.LengthSquared > .00000001) normal.Normalize();
                    surface.Normals[i] = normal;
                }
                Point3DCollection positions = new Point3DCollection(surface.Positions); positions.Freeze();
                Vector3DCollection normals = new Vector3DCollection(surface.Normals); normals.Freeze();
                surface.Mesh.Positions = positions;
                surface.Mesh.Normals = normals;
            }
            bindBounds = new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
        }

        void CreateSleepingLegs()
        {
            for (int leg = 0; leg < 4; leg++)
            {
                bool front = leg < 2;
                double side = leg % 2 == 0 ? 1 : -1;
                double x = pivots[leg + 2].X;
                Point3D a = new Point3D(x, front ? .75 : .78, front ? .27 : -.78);
                Point3D b = new Point3D(x, front ? .42 : .47, front ? .33 : -.83);
                Point3D c = new Point3D(x, front ? .18 : .20, front ? .44 : -.90);
                // Targets include the eventual root lowering. Upper and lower
                // limbs bend independently; feet retain their ground-facing pose.
                Point3D ta = a + new Vector3D(0, -.015, 0);
                Point3D tb = new Point3D(x + side * .09, .46, front ? .38 : -.67);
                Point3D tc = new Point3D(x + side * .04, .30 + .03 + (c.Y - .03) * .85, c.Z + (front ? .38 : -.03));
                sleepSegments[leg, 0] = MakeSleepSegment(a, b, ta, tb);
                sleepSegments[leg, 1] = MakeSleepSegment(b, c, tb, tc);
            }
        }

        static SleepSegment MakeSleepSegment(Point3D a, Point3D b, Point3D ta, Point3D tb)
        {
            Vector3D rest = b - a, target = tb - ta;
            double rl = Math.Sqrt(rest.Y * rest.Y + rest.Z * rest.Z);
            double tl = Math.Sqrt(target.Y * target.Y + target.Z * target.Z);
            return new SleepSegment { RestA = a, RestB = b, TargetA = ta, TargetB = tb,
                Cos = (rest.Y * target.Y + rest.Z * target.Z) / (rl * tl),
                Sin = (rest.Y * target.Z - rest.Z * target.Y) / (rl * tl) };
        }

        SleepFrame SegmentFrame(int leg, int segment, double height)
        {
            SleepSegment s = sleepSegments[leg, segment];
            double t = (s.RestA.Y - height) / (s.RestA.Y - s.RestB.Y);
            return new SleepFrame { Rest = s.RestA + (s.RestB - s.RestA) * t,
                Target = s.TargetA + (s.TargetB - s.TargetA) * t,
                Cos = s.Cos, Sin = s.Sin, NormalYScale = 1 };
        }

        SleepFrame FootFrame(int leg, double height)
        {
            SleepSegment s = sleepSegments[leg, 1];
            return new SleepFrame { Rest = new Point3D(s.RestB.X, height, s.RestB.Z),
                Target = new Point3D(s.TargetB.X, s.TargetB.Y + (height - s.RestB.Y) * .85, s.TargetB.Z),
                Cos = 1, Sin = 0, NormalYScale = .85 };
        }

        static SleepFrame BlendSleepFrames(SleepFrame a, SleepFrame b, double amount)
        {
            double c = a.Cos + (b.Cos - a.Cos) * amount, s = a.Sin + (b.Sin - a.Sin) * amount;
            double length = Math.Sqrt(c * c + s * s);
            // Normalize the joint rotation so bends do not collapse the limb
            // into a thin strip when its two segment directions differ.
            return new SleepFrame { Rest = a.Rest + (b.Rest - a.Rest) * amount,
                Target = a.Target + (b.Target - a.Target) * amount,
                Cos = c / length, Sin = s / length,
                NormalYScale = a.NormalYScale + (b.NormalYScale - a.NormalYScale) * amount };
        }

        void SleepingLegPoint(int leg, Point3D point, Vector3D normal, out Point3D posed, out Vector3D posedNormal)
        {
            if (leg >= 0) SleepingSegmentPoint(leg, point, normal, out posed, out posedNormal);
            else SleepingBodyPoint(point, normal, out posed, out posedNormal);
        }

        static void SleepingBodyPoint(Point3D point, Vector3D normal, out Point3D posed, out Vector3D posedNormal)
        {
            // Preserve the ribcage volume. The limbs bend around it instead
            // of compressing the whole chest/abdomen to make the feet fit.
            posed = point + new Vector3D(0, -.015, 0);
            posedNormal = normal;
        }

        void SleepingSegmentPoint(int leg, Point3D point, Vector3D normal, out Point3D posed, out Vector3D posedNormal)
        {
            double knee = sleepSegments[leg, 0].RestB.Y, ankle = sleepSegments[leg, 1].RestB.Y;
            const double band = .10;
            SleepFrame frame;
            if (point.Y > knee + band) frame = SegmentFrame(leg, 0, point.Y);
            else if (point.Y >= knee - band)
                frame = BlendSleepFrames(SegmentFrame(leg, 1, point.Y), SegmentFrame(leg, 0, point.Y), Smooth((point.Y - knee + band) / (band * 2)));
            else if (point.Y > ankle + .035) frame = SegmentFrame(leg, 1, point.Y);
            else if (point.Y >= ankle - .035)
                frame = BlendSleepFrames(FootFrame(leg, point.Y), SegmentFrame(leg, 1, point.Y), Smooth((point.Y - ankle + .035) / .070));
            else frame = FootFrame(leg, point.Y);
            Vector3D offset = point - frame.Rest;
            posed = frame.Target + new Vector3D(offset.X,
                frame.Cos * offset.Y - frame.Sin * offset.Z, frame.Sin * offset.Y + frame.Cos * offset.Z);
            posedNormal = new Vector3D(normal.X,
                frame.Cos * normal.Y / frame.NormalYScale - frame.Sin * normal.Z,
                frame.Sin * normal.Y / frame.NormalYScale + frame.Cos * normal.Z);
            if (posed.Y < .325) { posed.Y = .325; posedNormal = new Vector3D(0, -1, 0); }
        }

        static double MovingWeight(byte bone, double weight, double legBlend, double cervicalWeight)
        {
            if (cervicalWeight > 0 && (bone == 1 || bone == 8)) return 0;
            return (bone == 0 || bone >= 2 && bone <= 5) && weight >= .000001 ? weight * (1 - legBlend) : weight;
        }

        void CacheCervicalJoint()
        {
            // Interpolate joint rotation rather than linearly collapsing the
            // head and neck matrices into each other at a deeply bent joint.
            foreach (Surface surface in surfaces)
            for (int i = 0; i < surface.Bind.Length; i++)
            {
                Vertex vertex = surface.Bind[i]; double head = 0, neck = 0;
                AddCervicalWeight(vertex.B0, vertex.W0, ref head, ref neck);
                AddCervicalWeight(vertex.B1, vertex.W1, ref head, ref neck);
                AddCervicalWeight(vertex.B2, vertex.W2, ref head, ref neck);
                AddCervicalWeight(vertex.B3, vertex.W3, ref head, ref neck);
                if (head < .000001 || neck < .000001) continue;
                vertex.CervicalWeight = head + neck;
                vertex.CervicalPoseIndex = (ushort)Math.Round(head / vertex.CervicalWeight * 256);
                surface.Bind[i] = vertex;
            }
        }

        static void AddCervicalWeight(byte bone, double weight, ref double head, ref double neck)
        { if (bone == 1) head += weight; else if (bone == 8) neck += weight; }

        void CacheSleepingBody()
        {
            // The resting target is fixed in bind space. Compute it once,
            // then only blend its weighted contribution during animation.
            foreach (Surface surface in surfaces)
            for (int i = 0; i < surface.Bind.Length; i++)
            {
                Vertex vertex = surface.Bind[i];
                CacheSleepingBone(ref vertex, vertex.B0, vertex.W0);
                CacheSleepingBone(ref vertex, vertex.B1, vertex.W1);
                CacheSleepingBone(ref vertex, vertex.B2, vertex.W2);
                CacheSleepingBone(ref vertex, vertex.B3, vertex.W3);
                surface.Bind[i] = vertex;
            }
        }

        void CacheSleepingBone(ref Vertex vertex, byte bone, double weight)
        {
            if ((bone != 0 && (bone < 2 || bone > 5)) || weight < .000001) return;
            Point3D target; Vector3D targetNormal;
            SleepingLegPoint(bone - 2, vertex.Position, vertex.Normal, out target, out targetNormal);
            vertex.SleepPosition += new Vector3D(target.X, target.Y, target.Z) * weight;
            vertex.SleepNormal += targetNormal * weight;
        }

        void CloseEyes(ref Point3D point)
        {
            if (eyeClosure <= 0 || point.Z < .665 || point.Y < 1.285 || point.Y > 1.565) return;
            for (int i = 2; i <= 3; i++)
            {
                Point3D eye = anchors[i];
                double dx = Math.Abs(point.X - eye.X), v = point.Y - eye.Y;
                double across = 1 - Smooth((dx - .035) / .035);
                if (across <= 0 || Math.Abs(v) >= .128) continue;
                double depth = Smooth((point.Z - .665) / .045);
                double amount = Smooth(eyeClosure) * across * depth;
                double scale = 1 - .998 * amount, absolute = Math.Abs(v), compressed;
                const double aperture = .044, outside = .128;
                if (absolute <= aperture) compressed = absolute * scale;
                else
                {
                    double span = outside - aperture, t = (absolute - aperture) / span, t2 = t * t, t3 = t2 * t;
                    compressed = (2 * t3 - 3 * t2 + 1) * (aperture * scale)
                        + (t3 - 2 * t2 + t) * (span * scale)
                        + (-2 * t3 + 3 * t2) * outside + (t3 - t2) * span;
                }
                point.Y = eye.Y + (v < 0 ? -compressed : compressed);
            }
        }

        Point3D DeformAnchor(Point3D point, int bone)
        {
            Point3D result = bones[bone].Transform(point);
            if (sleepPose >= .000001 && bone >= 2 && bone <= 5)
            {
                Point3D target; Vector3D unused;
                SleepingLegPoint(bone - 2, point, new Vector3D(), out target, out unused);
                result += (target - result) * sleepPose;
            }
            result.Y += rootLift;
            return result;
        }

        Point3D World(Point3D point) { return heading.Transform(point); }

        static Matrix3D Pose(Point3D pivot, Vector3D degrees, Vector3D offset)
        {
            Matrix3D matrix = Matrix3D.Identity;
            matrix.RotateAt(new Quaternion(new Vector3D(1, 0, 0), degrees.X), pivot);
            matrix.RotateAt(new Quaternion(new Vector3D(0, 1, 0), degrees.Y), pivot);
            matrix.RotateAt(new Quaternion(new Vector3D(0, 0, 1), degrees.Z), pivot);
            matrix.Translate(offset); return matrix;
        }

        static void Accumulate(Point3D p, Vector3D n, ref Matrix3D m, double weight,
            ref double x, ref double y, ref double z, ref double nx, ref double ny, ref double nz)
        {
            if (weight <= 0) return;
            x += (p.X * m.M11 + p.Y * m.M21 + p.Z * m.M31 + m.OffsetX) * weight;
            y += (p.X * m.M12 + p.Y * m.M22 + p.Z * m.M32 + m.OffsetY) * weight;
            z += (p.X * m.M13 + p.Y * m.M23 + p.Z * m.M33 + m.OffsetZ) * weight;
            nx += (n.X * m.M11 + n.Y * m.M21 + n.Z * m.M31) * weight;
            ny += (n.X * m.M12 + n.Y * m.M22 + n.Z * m.M32) * weight;
            nz += (n.X * m.M13 + n.Y * m.M23 + n.Z * m.M33) * weight;
        }

        internal static double PawGesture(double age)
        {
            if (age <= .22 || age >= 1.30) return 0;
            if (age <= .72) return -17 * Smooth((age - .22) / .50);
            if (age < .84) return -17 - 2 * Smooth((age - .72) / .12);
            return -19 * (1 - Smooth((age - .84) / .46));
        }

        static double MoveAngle(double current, double target, double dt, double speed)
        { return current + Clamp((target - current) * (1 - Math.Exp(-dt * 4)), -speed * dt, speed * dt); }
        static double Approach(double current, double target, double dt, double speed)
        { return current + (target - current) * (1 - Math.Exp(-dt * speed)); }
        static double DeadZone(double value)
        { return Math.Abs(value) < .18 ? 0 : Math.Sign(value) * (Math.Abs(value) - .18) / .82; }
        static double Blink(double age)
        { return age <= 0 || age >= 1 ? 0 : age < .34 ? Smooth(age / .34) : age < .44 ? 1 : 1 - Smooth((age - .44) / .56); }
        static double Smooth(double value) { double t = Clamp(value, 0, 1); return t * t * (3 - 2 * t); }
        static double Clamp(double value, double low, double high) { return Math.Max(low, Math.Min(high, value)); }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
