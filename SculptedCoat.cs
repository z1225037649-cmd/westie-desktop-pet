using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WestiePet
{
    // One smooth, skinned surface. Primitives define a distance field only;
    // their individual surfaces are never drawn as overlapping body parts.
    internal sealed class SculptedCoat
    {
        internal readonly Model3DGroup Model = new Model3DGroup();
        internal int TriangleCount;
        readonly List<Surface> surfaces = new List<Surface>();
        readonly Random random = new Random(231);
        readonly bool corgi;
        internal const double CorgiHeadWidth = 1.20, CorgiHeadHeight = .97, CorgiHeadDepth = .94, CorgiHeadY = 1.12;
        double MouthCurve { get { return corgi ? 2.2 : 1.20; } }
        readonly double[] legSin = new double[4], legCos = new double[4];
        readonly SolidColorBrush restingMouth = new SolidColorBrush(Color.FromRgb(40, 32, 33));
        static readonly Point3D[] legPivots = { new Point3D(-.245, .64, .24), new Point3D(.245, .64, .24),
            new Point3D(-.212, .75, -.89), new Point3D(.212, .75, -.89) };

        struct Sample
        {
            internal double D;
            internal float H, A, B, C, E, T;
            internal bool Mouth;
            internal Sample(double d, int bone) { D = d; H = bone == 0 ? 1 : 0; A = bone == 1 ? 1 : 0;
                B = bone == 2 ? 1 : 0; C = bone == 3 ? 1 : 0; E = bone == 4 ? 1 : 0; T = bone == 5 ? 1 : 0; Mouth = false; }
        }
        struct Vertex
        {
            internal Point3D P;
            internal Vector3D N;
            internal Point3D RestP;
            internal Vector3D RestN;
            internal Sample W;
            internal float Jaw, Lip, Corner, Snout, Foot, Neck, EyeSocket;
            internal Vertex(Point3D p, Vector3D n, Sample w, SculptedCoat owner)
            {
                P = p; N = n; W = w;
                RestP = p; RestN = n;
                bool eyeSocket = p.Z > .72 && Math.Abs(Math.Abs(p.X) - .125) < .112 && Math.Abs(p.Y - 1.515) < .118;
                EyeSocket = eyeSocket ? 1 : 0;
                bool lipSocket = p.Z > .82 && Math.Abs(p.X) < .18 && Math.Abs(p.Y - 1.115 - owner.MouthCurve * p.X * p.X) < .09;
                if (w.H > .9 && (eyeSocket || lipSocket) && owner.Field(p, false).D < -.00005)
                {
                    RestP = owner.ClosedFacePoint(p, 0); RestN = owner.Normal(RestP, false);
                }
                Jaw = (float)(w.H * Smooth(.73, .88, p.Z) * (1 - Smooth(1.10, 1.24, p.Y)));
                Lip = (float)(w.H * Smooth(.81, .91, p.Z) * (1 - Smooth(.14, .23, Math.Abs(p.X)))
                    * (1 - Smooth(.055, .12, Math.Abs(p.Y - 1.115 - owner.MouthCurve * p.X * p.X))));
                Corner = (float)(Lip * Smooth(.030, .105, Math.Abs(p.X)) * (1 - Smooth(.125, .19, Math.Abs(p.X))));
                Snout = (float)(w.H * Smooth(.76, .93, p.Z) * Smooth(1.14, 1.24, p.Y) * (1 - Smooth(1.32, 1.44, p.Y)));
                Foot = (float)(1 - Smooth(.15, .31, p.Y));
                Neck = (float)((1 - w.H) * Smooth(.78, 1.08, p.Y) * Smooth(-.15, .12, p.Z) * (1 - Smooth(.37, .65, p.Z)));
            }
        }
        sealed class Surface
        {
            internal MeshGeometry3D Mesh;
            internal Vertex[] Bind;
            internal Point3D[] Points;
            internal Vector3D[] Normals;
        }

        internal SculptedCoat(Material skin, Material mouthMaterial, Material[] coat)
            : this(skin, mouthMaterial, coat, true)
        {
        }

        internal SculptedCoat(Material skin, Material mouthMaterial, Material[] coat, bool withCoat, bool corgi = false)
        {
            this.corgi = corgi;
            List<Vertex> vertices = new List<Vertex>(); List<int> indices = new List<int>();
            Extract(vertices, indices);
            RefineFace(vertices, indices, false);
            RefineFace(vertices, indices, false);
            RefineFace(vertices, indices, true);
            RefineFace(vertices, indices, true);
            RefineFace(vertices, indices, false, true);
            CheckConnected(vertices, indices);
            List<int> outer = new List<int>(), inner = new List<int>();
            for (int i = 0; i < indices.Count; i += 3)
            {
                Point3D a = vertices[indices[i]].P, b = vertices[indices[i + 1]].P, c = vertices[indices[i + 2]].P;
                Sample middle = Field(new Point3D((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3));
                List<int> target = middle.Mouth ? inner : outer;
                target.Add(indices[i]); target.Add(indices[i + 1]); target.Add(indices[i + 2]);
            }
            Material corgiCoat = corgi ? CorgiMaterial() : null;
            AddSurface(vertices, outer, corgi ? corgiCoat : skin);
            List<Vertex> lining = new List<Vertex>(); List<int> liningIndices = new List<int>(); Dictionary<int, int> remap = new Dictionary<int, int>();
            foreach (int old in inner)
            {
                int mapped; if (!remap.TryGetValue(old, out mapped)) { mapped = lining.Count; remap.Add(old, mapped); lining.Add(vertices[old]); }
                liningIndices.Add(mapped);
            }
            AddSurface(lining, liningIndices, new DiffuseMaterial(restingMouth));
            if (withCoat) GrowCoat(vertices, indices, corgi ? new[] { corgiCoat, corgiCoat, corgiCoat } : coat);
            if (corgi) Update(Matrix3D.Identity, Matrix3D.Identity, new double[4], 0, 0, 0, 0, 0, 0);
        }

        static double Clamp(double x) { return Math.Max(0, Math.Min(1, x)); }
        static double Smooth(double a, double b, double x) { double t = Clamp((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static Sample Ellipsoid(Point3D p, double x, double y, double z, double rx, double ry, double rz, int bone)
        {
            double dx = p.X - x, dy = p.Y - y, dz = p.Z - z;
            double qx = dx / rx, qy = dy / ry, qz = dz / rz;
            double a = Math.Sqrt(qx * qx + qy * qy + qz * qz);
            double b = Math.Sqrt(qx * qx / (rx * rx) + qy * qy / (ry * ry) + qz * qz / (rz * rz));
            return new Sample(b < .000001 ? -Math.Min(rx, Math.Min(ry, rz)) : a * (a - 1) / b, bone);
        }
        static Sample Union(Sample a, Sample b, double radius)
        {
            double h = Clamp(.5 + .5 * (b.D - a.D) / radius), g = 1 - h;
            Sample s = new Sample(g * b.D + h * a.D - radius * h * g, -1);
            s.H = (float)(a.H * h + b.H * g); s.A = (float)(a.A * h + b.A * g);
            s.B = (float)(a.B * h + b.B * g); s.C = (float)(a.C * h + b.C * g); s.E = (float)(a.E * h + b.E * g); s.T = (float)(a.T * h + b.T * g);
            return s;
        }
        Sample Field(Point3D p)
        { return Field(p, true); }
        Sample Field(Point3D p, bool carveFace)
        {
            Sample body = corgi ? Ellipsoid(p, 0, .700, -.36, .385, .355, .765, -1)
                : Ellipsoid(p, 0, .775, -.34, .345, .345, .825, -1);
            body = Union(body, Ellipsoid(p, 0, corgi ? .860 : 1.095, .245, corgi ? .300 : .255, .305, .270, -1), .145);
            // The rump is part of the torso, so the thighs grow out of a full hip
            // rather than attaching beneath the tapered end of the barrel.
            body = Union(body, Ellipsoid(p, 0, corgi ? .690 : .795, corgi ? -.825 : -.865,
                corgi ? .372 : .323, corgi ? .340 : .299, corgi ? .340 : .332, -1), .14);
            double tailT = Clamp((p.Y - 1.00) / .52);
            for (int i = 0; i < 3; i++)
            {
                double cy = 1.00 + tailT * .52, cz = -1.065 - .20 * tailT + .07 * tailT * tailT;
                double tz = -.20 + .14 * tailT;
                tailT = Clamp(tailT + ((p.Y - cy) * .52 + (p.Z - cz) * tz) / (.52 * .52 + tz * tz));
            }
            double tailY = 1.00 + tailT * .52, tailZ = -1.065 - .20 * tailT + .07 * tailT * tailT;
            double tailDistance = Math.Sqrt(p.X * p.X + (p.Y - tailY) * (p.Y - tailY) + (p.Z - tailZ) * (p.Z - tailZ)) - (.072 - tailT * .051);
            if (corgi)
                body = Union(body, Ellipsoid(p, 0, .87, -1.115, .105, .12, .14, 5), .09);
            else body = Union(body, new Sample(tailDistance, 5), .08);
            for (int i = 0; i < 4; i++)
            {
                double x = i % 2 == 0 ? -.245 : .245;
                Sample leg;
                if (i < 2)
                {
                    leg = Ellipsoid(p, x, .495, .24, .135, .265, .145, i + 1);
                    leg = Union(leg, Ellipsoid(p, x, .265, .248, .115, .190, .124, i + 1), .075);
                    leg = Union(leg, Ellipsoid(p, x, .115, .304, .141, .090, .196, i + 1), .062);
                }
                else
                {
                    x = i % 2 == 0 ? -.212 : .212;
                    // A straight, softly tapered column with the hip blended into the rump.
                    leg = Ellipsoid(p, x, .565, -.89, .143, .292, .166, i + 1);
                    leg = Union(leg, Ellipsoid(p, x, .300, -.89, .111, .205, .120, i + 1), .090);
                    leg = Union(leg, Ellipsoid(p, x, .108, -.836, .126, .084, .176, i + 1), .065);
                }
                body = Union(body, leg, i < 2 ? .112 : .168);
            }
            Sample head;
            if (corgi)
            {
                // A broad fox-shaped skull narrowing through the cheeks into
                // a real muzzle; the terrier's round beard is not reused.
                head = Ellipsoid(p, 0, 1.450, .485, .300, .325, .315, 0);
                head = Union(head, Ellipsoid(p, 0, 1.615, .610, .270, .150, .215, 0), .095);
                head = Union(head, Ellipsoid(p, -.155, 1.330, .650, .143, .173, .190, 0), .10);
                head = Union(head, Ellipsoid(p, .155, 1.330, .650, .143, .173, .190, 0), .10);
                head = Union(head, Ellipsoid(p, 0, 1.265, .815, .160, .128, .220, 0), .085);
                head = Union(head, Ellipsoid(p, -.060, 1.208, .825, .108, .103, .170, 0), .055);
                head = Union(head, Ellipsoid(p, .060, 1.208, .825, .108, .103, .170, 0), .055);
                head = Union(head, Ellipsoid(p, 0, 1.095, .780, .170, .070, .195, 0), .065);
            }
            else
            {
                head = Ellipsoid(p, 0, 1.425, .460, .318, .400, .345, 0);
                head = Union(head, Ellipsoid(p, 0, 1.620, .640, .266, .180, .230, 0), .11);
                head = Union(head, Ellipsoid(p, 0, 1.235, .735, .253, .210, .245, 0), .15);
                head = Union(head, Ellipsoid(p, -.150, 1.218, .685, .142, .190, .195, 0), .12);
                head = Union(head, Ellipsoid(p, .150, 1.218, .685, .142, .190, .195, 0), .12);
                head = Union(head, Ellipsoid(p, 0, 1.090, .730, .207, .078, .210, 0), .085);
            }
            Sample result = Union(body, head, .14);
            // Mouth and eye recesses are cut into the same surface, rather than
            // outlined on top of an unrelated white muzzle or spherical eye ring.
            Point3D mouthPoint = new Point3D(p.X, p.Y - MouthCurve * p.X * p.X, p.Z);
            double mouth = Ellipsoid(mouthPoint, 0, 1.115, .930, corgi ? .116 : .120, corgi ? .036 : .034, .095, 0).D;
            if (carveFace && -mouth > result.D) { result.D = -mouth; result.Mouth = true; }
            for (int side = -1; carveFace && side <= 1; side += 2)
            {
                double socket = Ellipsoid(p, side * .125, 1.515, corgi ? .792 : .820,
                    corgi ? .080 : .099, corgi ? .087 : .104, corgi ? .046 : .055, 0).D;
                if (-socket > result.D) { result.D = -socket; result.Mouth = false; }
            }
            // Keep the upper haunch planted in the rump while the lower leg swings.
            float rear = (float)(1 - Smooth(.47, .86, p.Y));
            result.C *= rear; result.E *= rear;
            return result;
        }
        Vector3D Normal(Point3D p)
        { return Normal(p, true); }
        Vector3D Normal(Point3D p, bool carveFace)
        {
            const double e = .0013;
            Vector3D n = new Vector3D(Field(new Point3D(p.X + e, p.Y, p.Z), carveFace).D - Field(new Point3D(p.X - e, p.Y, p.Z), carveFace).D,
                Field(new Point3D(p.X, p.Y + e, p.Z), carveFace).D - Field(new Point3D(p.X, p.Y - e, p.Z), carveFace).D,
                Field(new Point3D(p.X, p.Y, p.Z + e), carveFace).D - Field(new Point3D(p.X, p.Y, p.Z - e), carveFace).D);
            if (n.Length < .000001) return new Vector3D(0, 1, 0); n.Normalize(); return n;
        }

        // Close the existing facial surface along its front direction. Keeping
        // x/y fixed preserves the lip contour and avoids a second eyelid shell.
        internal Point3D ClosedFacePoint(Point3D p, double offset)
        {
            for (int i = 0; i < 9; i++)
            {
                double distance = Field(p, false).D;
                if (Math.Abs(distance) < .00002) break;
                Vector3D normal = Normal(p, false);
                p.Z -= Math.Max(-.04, Math.Min(.04, distance / Math.Max(.25, normal.Z)));
            }
            p.Z += offset; return p;
        }

        internal static double FaceClosure(double sleepPose)
        {
            double t = Clamp((sleepPose - .45) / .40);
            return t * t * (3 - 2 * t);
        }

        void Extract(List<Vertex> vertices, List<int> triangles)
        {
            const double step = .045;
            const int nx = 29, ny = 43;
            int nz = corgi ? 61 : 57;
            Point3D[] points = new Point3D[nx * ny * nz]; Sample[] fields = new Sample[points.Length];
            for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
            {
                int i = (z * ny + y) * nx + x;
                points[i] = new Point3D(-.63 + x * step, -.045 + y * step, (corgi ? -1.50 : -1.32) + z * step);
                fields[i] = Field(points[i]);
            }
            int[,] tetra = { { 0, 5, 1, 6 }, { 0, 1, 2, 6 }, { 0, 2, 3, 6 }, { 0, 3, 7, 6 }, { 0, 7, 4, 6 }, { 0, 4, 5, 6 } };
            Dictionary<long, int> edges = new Dictionary<long, int>(); int[] cube = new int[8], inside = new int[4], outside = new int[4];
            for (int z = 0; z < nz - 1; z++) for (int y = 0; y < ny - 1; y++) for (int x = 0; x < nx - 1; x++)
            {
                int i = (z * ny + y) * nx + x;
                cube[0] = i; cube[1] = i + 1; cube[2] = i + nx + 1; cube[3] = i + nx;
                cube[4] = i + nx * ny; cube[5] = cube[4] + 1; cube[6] = cube[4] + nx + 1; cube[7] = cube[4] + nx;
                bool negative = false, positive = false;
                for (int c = 0; c < 8; c++) { if (fields[cube[c]].D < 0) negative = true; else positive = true; }
                if (!negative || !positive) continue;
                for (int t = 0; t < 6; t++)
                {
                    int ni = 0, no = 0;
                    for (int k = 0; k < 4; k++) { int c = cube[tetra[t, k]]; if (fields[c].D < 0) inside[ni++] = c; else outside[no++] = c; }
                    if (ni == 0 || ni == 4) continue;
                    if (ni == 1 || ni == 3)
                    {
                        int center = ni == 1 ? inside[0] : outside[0]; int[] other = ni == 1 ? outside : inside;
                        AddTriangle(vertices, triangles, Intersect(center, other[0], points, fields, edges, vertices),
                            Intersect(center, other[1], points, fields, edges, vertices), Intersect(center, other[2], points, fields, edges, vertices));
                    }
                    else
                    {
                        int a = Intersect(inside[0], outside[0], points, fields, edges, vertices), b = Intersect(inside[0], outside[1], points, fields, edges, vertices);
                        int c = Intersect(inside[1], outside[0], points, fields, edges, vertices), d = Intersect(inside[1], outside[1], points, fields, edges, vertices);
                        AddTriangle(vertices, triangles, a, b, c); AddTriangle(vertices, triangles, b, d, c);
                    }
                }
            }
        }
        int Intersect(int a, int b, Point3D[] points, Sample[] fields, Dictionary<long, int> cache, List<Vertex> vertices)
        {
            long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b); int index;
            if (cache.TryGetValue(key, out index)) return index;
            double t = fields[a].D / (fields[a].D - fields[b].D);
            Point3D p = points[a] + (points[b] - points[a]) * t;
            index = vertices.Count; vertices.Add(new Vertex(p, Normal(p), Field(p), this)); cache.Add(key, index); return index;
        }
        static void AddTriangle(List<Vertex> vertices, List<int> triangles, int a, int b, int c)
        {
            Vector3D cross = Vector3D.CrossProduct(vertices[b].P - vertices[a].P, vertices[c].P - vertices[a].P);
            if (cross.LengthSquared < 1e-20) return;
            if (Vector3D.DotProduct(cross, vertices[a].N + vertices[b].N + vertices[c].N) < 0) { int swap = b; b = c; c = swap; }
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }

        static long EdgeKey(int a, int b) { return ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b); }
        static int Root(int[] parent, int i)
        { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        static void CheckConnected(List<Vertex> vertices, List<int> triangles)
        {
            int count = vertices.Count;
            int[] parent = new int[count]; bool[] used = new bool[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                parent[Root(parent, b)] = Root(parent, a); parent[Root(parent, c)] = Root(parent, a);
                used[a] = used[b] = used[c] = true;
            }
            HashSet<int> components = new HashSet<int>();
            for (int i = 0; i < count; i++) if (used[i]) components.Add(Root(parent, i));
            if (components.Count != 1)
            {
                Dictionary<int, int> sizes = new Dictionary<int, int>();
                foreach (int root in components) sizes[root] = 0;
                for (int i = 0; i < count; i++) if (used[i]) sizes[Root(parent, i)]++;
                List<int> roots = new List<int>(components); roots.Sort(delegate(int a, int b) { return sizes[b].CompareTo(sizes[a]); });
                // Projected contour refinement can leave microscopic sliver islands
                // at an intersection. Remove those only; never discard a body part.
                bool tiny = true;
                for (int i = 1; i < roots.Count; i++) if (sizes[roots[i]] > 32) tiny = false;
                if (tiny)
                {
                    int main = roots[0]; int[] remap = new int[count]; for (int i = 0; i < count; i++) remap[i] = -1;
                    List<Vertex> clean = new List<Vertex>(); List<int> faces = new List<int>();
                    for (int i = 0; i < triangles.Count; i += 3)
                    {
                        if (Root(parent, triangles[i]) != main) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int old = triangles[i + k]; if (remap[old] < 0) { remap[old] = clean.Count; clean.Add(vertices[old]); }
                            faces.Add(remap[old]);
                        }
                    }
                    vertices.Clear(); vertices.AddRange(clean); triangles.Clear(); triangles.AddRange(faces); return;
                }
                string detail = "";
                foreach (int root in roots)
                {
                    Point3D p = vertices[root].P;
                    detail += " " + sizes[root] + "@" + p.X.ToString("F2") + "," + p.Y.ToString("F2") + "," + p.Z.ToString("F2");
                }
                throw new InvalidOperationException("Connected surface count: " + components.Count + detail);
            }
        }
        void RefineFace(List<Vertex> vertices, List<int> triangles, bool mouthOnly, bool tailOnly = false)
        {
            HashSet<long> marked = new HashSet<long>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Point3D p = vertices[a].P + ((vertices[b].P - vertices[a].P) + (vertices[c].P - vertices[a].P)) / 3;
                bool selected = tailOnly ? p.Z < -.99 && p.Y > 1.04 && Math.Abs(p.X) < .18
                    : mouthOnly ? p.Z > .90 && Math.Abs(p.Y - 1.115 - 1.20 * p.X * p.X) < .045 && Math.Abs(p.X) < .135
                    : p.Z > .70 && p.Y > 1.04 && p.Y < 1.70 && Math.Abs(p.X) < .31;
                if (selected) { marked.Add(EdgeKey(a, b)); marked.Add(EdgeKey(b, c)); marked.Add(EdgeKey(c, a)); }
            }
            Dictionary<long, int> mids = new Dictionary<long, int>(); List<int> refined = new List<int>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int ab = Midpoint(a, b, marked, mids, vertices), bc = Midpoint(b, c, marked, mids, vertices), ca = Midpoint(c, a, marked, mids, vertices);
                int splits = (ab < 0 ? 0 : 1) + (bc < 0 ? 0 : 1) + (ca < 0 ? 0 : 1);
                if (splits == 0) { AddTriangle(vertices, refined, a, b, c); continue; }
                if (splits == 3)
                {
                    AddTriangle(vertices, refined, a, ab, ca); AddTriangle(vertices, refined, ab, b, bc);
                    AddTriangle(vertices, refined, ca, bc, c); AddTriangle(vertices, refined, ab, bc, ca); continue;
                }
                if (splits == 1)
                {
                    if (ab >= 0) { AddTriangle(vertices, refined, a, ab, c); AddTriangle(vertices, refined, ab, b, c); }
                    else if (bc >= 0) { AddTriangle(vertices, refined, b, bc, a); AddTriangle(vertices, refined, bc, c, a); }
                    else { AddTriangle(vertices, refined, c, ca, b); AddTriangle(vertices, refined, ca, a, b); }
                }
                else if (ca < 0)
                { AddTriangle(vertices, refined, b, bc, ab); AddTriangle(vertices, refined, a, ab, c); AddTriangle(vertices, refined, ab, bc, c); }
                else if (ab < 0)
                { AddTriangle(vertices, refined, c, ca, bc); AddTriangle(vertices, refined, b, bc, a); AddTriangle(vertices, refined, bc, ca, a); }
                else
                { AddTriangle(vertices, refined, a, ab, ca); AddTriangle(vertices, refined, c, ca, b); AddTriangle(vertices, refined, ca, ab, b); }
            }
            triangles.Clear(); triangles.AddRange(refined);
        }
        int Midpoint(int a, int b, HashSet<long> marked, Dictionary<long, int> cache, List<Vertex> vertices)
        {
            long key = EdgeKey(a, b); if (!marked.Contains(key)) return -1;
            int index; if (cache.TryGetValue(key, out index)) return index;
            Point3D p = vertices[a].P + (vertices[b].P - vertices[a].P) * .5;
            for (int i = 0; i < 3; i++) { double distance = Field(p).D; p -= Normal(p) * Math.Max(-.025, Math.Min(.025, distance)); }
            index = vertices.Count; vertices.Add(new Vertex(p, Normal(p), Field(p), this)); cache.Add(key, index); return index;
        }

        void GrowCoat(List<Vertex> skin, List<int> triangles, Material[] materials)
        {
            int count = triangles.Count / 3; double[] cumulative = new double[count]; double area = 0;
            for (int t = 0; t < count; t++)
            {
                Point3D a = skin[triangles[t * 3]].P, b = skin[triangles[t * 3 + 1]].P, c = skin[triangles[t * 3 + 2]].P;
                area += Vector3D.CrossProduct(b - a, c - a).Length * .5; cumulative[t] = area;
            }
            List<Vertex>[] locks = { new List<Vertex>(), new List<Vertex>(), new List<Vertex>() };
            List<int>[] faces = { new List<int>(), new List<int>(), new List<int>() };
            for (int i = 0; i < 6500; i++)
            {
                int t = Array.BinarySearch(cumulative, random.NextDouble() * area); if (t < 0) t = ~t; if (t >= count) t = count - 1;
                Vertex a = skin[triangles[t * 3]], b = skin[triangles[t * 3 + 1]], c = skin[triangles[t * 3 + 2]];
                double u = Math.Sqrt(random.NextDouble()), v = random.NextDouble();
                Point3D root = a.P + (b.P - a.P) * (u * (1 - v)) + (c.P - a.P) * (u * v);
                if (root.Y < .045) continue;
                if (root.Z > .73 && Math.Abs(Math.Abs(root.X) - .125) < (corgi ? .082 : .098) && Math.Abs(root.Y - 1.515) < (corgi ? .090 : .108)) continue;
                if (root.Z > .87 && Math.Abs(root.X) < .143 && Math.Abs(root.Y - 1.115 - MouthCurve * root.X * root.X) < .052) continue;
                if (root.Z > .95 && (root.X * root.X / (.115 * .115) + (root.Y - 1.29) * (root.Y - 1.29) / (.085 * .085)) < 1.15) continue;
                Vector3D n = Normal(root); Sample w = Field(root); if (w.Mouth) continue;
                root -= n * w.D;
                double head = w.H, foot = 1 - Smooth(.14, .38, root.Y), leg = w.A + w.B + w.C + w.E;
                double face = Smooth(.61, .86, root.Z) * head;
                double cheek = Smooth(.13, .25, Math.Abs(root.X)) * (1 - Smooth(1.46, 1.60, root.Y)) * Smooth(1.00, 1.14, root.Y) * head;
                double forehead = Smooth(1.48, 1.64, root.Y) * head;
                double length = .104 * (1 - head) + .105 * head;
                length += cheek * .008;
                length = length * (1 - leg * .30) * (1 - foot * .45) * (1 - face * .40 * (1 - cheek * .85) * (1 - forehead)) * (1 - w.T * .30);
                length *= .65 + random.NextDouble() * .60;
                if (corgi) length *= .40 + .10 * (1 - head);
                Vector3D comb = new Vector3D(root.X * head * .55, -1, -.15 * (1 - head) + head * .12);
                if (root.Y > 1.52) comb = new Vector3D(root.X * .8, -.5, .6);
                comb = comb * (1 - w.T) + new Vector3D(0, 1, -.15) * w.T;
                comb -= n * Vector3D.DotProduct(comb, n);
                if (comb.Length < .0001) comb = new Vector3D(0, 0, -1); comb.Normalize();
                for (int clip = 0; clip < 4; clip++)
                {
                    Point3D tip = root + n * (length * .45) + comb * (length * .86);
                    Point3D warped = new Point3D(tip.X, tip.Y - MouthCurve * tip.X * tip.X, tip.Z);
                    if (Ellipsoid(warped, 0, 1.115, .930, .136, .048, .113, 0).D >= 0) break;
                    length *= .50;
                }
                int material = i % 9 == 0 ? 2 : i % 3 == 0 ? 1 : 0;
                Hair(locks[material], faces[material], root - n * .003, n, comb, length, w, i);
            }
            for (int j = 0; j < materials.Length; j++) AddSurface(locks[j], faces[j], materials[j]);
        }
        void Hair(List<Vertex> vertices, List<int> indices, Point3D root, Vector3D normal, Vector3D comb, double length, Sample weights, int seed)
        {
            Vector3D direction = normal * .30 + comb * .95; direction.Normalize();
            Vector3D u = Vector3D.CrossProduct(direction, new Vector3D(.12, 1, .07));
            if (u.Length < .001) u = Vector3D.CrossProduct(direction, new Vector3D(1, 0, 0)); u.Normalize();
            Vector3D v = Vector3D.CrossProduct(direction, u); v.Normalize(); int start = vertices.Count;
            for (int j = 0; j < 3; j++)
            {
                double t = j / 2.0; double radius = length * .083 * (Math.Pow(1 - t, .8) + .018);
                Point3D center = root + normal * (length * .45 * t) + comb * (length * .86 * t * t)
                    + u * (Math.Sin(t * Math.PI) * length * .10 * Math.Sin(seed));
                for (int k = 0; k < 3; k++)
                {
                    double angle = k * Math.PI * 2 / 3 + seed * .17;
                    Vector3D radial = u * Math.Cos(angle) + v * Math.Sin(angle) * .64;
                    Vector3D n = normal + radial * .14; n.Normalize();
                    vertices.Add(new Vertex(center + radial * radius, n, weights, this));
                }
            }
            for (int j = 0; j < 2; j++) for (int k = 0; k < 3; k++)
            {
                int a = start + j * 3 + k, b = start + j * 3 + (k + 1) % 3, c = a + 3, d = b + 3;
                indices.Add(a); indices.Add(b); indices.Add(c); indices.Add(b); indices.Add(d); indices.Add(c);
            }
        }
        void AddSurface(List<Vertex> vertices, List<int> indices, Material material)
        {
            if (vertices.Count == 0) return;
            Surface surface = new Surface(); surface.Bind = vertices.ToArray(); surface.Points = new Point3D[vertices.Count]; surface.Normals = new Vector3D[vertices.Count];
            for (int i = 0; i < vertices.Count; i++) { surface.Points[i] = vertices[i].P; surface.Normals[i] = vertices[i].N; }
            MeshGeometry3D mesh = new MeshGeometry3D { Positions = new Point3DCollection(surface.Points), Normals = new Vector3DCollection(surface.Normals), TriangleIndices = new Int32Collection(indices) };
            // Coat markings live in bind space, so they follow every bone and blink.
            if (corgi) for (int i = 0; i < vertices.Count; i++)
                mesh.TextureCoordinates.Add(new System.Windows.Point(CorgiWhite(vertices[i].P), .5));
            surface.Mesh = mesh; surfaces.Add(surface); TriangleCount += indices.Count / 3;
            GeometryModel3D model = new GeometryModel3D(mesh, material); model.BackMaterial = material; Model.Children.Add(model);
        }

        static Material CorgiMaterial()
        {
            LinearGradientBrush color = new LinearGradientBrush();
            color.StartPoint = new System.Windows.Point(0, .5); color.EndPoint = new System.Windows.Point(1, .5);
            color.GradientStops.Add(new GradientStop(Color.FromRgb(215, 141, 65), 0));
            color.GradientStops.Add(new GradientStop(Color.FromRgb(230, 168, 94), .20));
            color.GradientStops.Add(new GradientStop(Color.FromRgb(244, 236, 217), .92));
            color.GradientStops.Add(new GradientStop(Color.FromRgb(248, 243, 231), 1));
            color.Freeze(); Material material = new DiffuseMaterial(color); material.Freeze(); return material;
        }

        static double CorgiWhite(Point3D p)
        {
            // Narrow forehead blaze, cream muzzle, chest bib, belly and white socks.
            double head = Smooth(1.03, 1.19, p.Y) * Smooth(.31, .53, p.Z);
            double muzzle = (1 - Smooth(1.35, 1.48, p.Y)) * Smooth(.64, .79, p.Z);
            double blazeWidth = .025 + .026 * (1 - Smooth(1.42, 1.73, p.Y));
            double blaze = (1 - Smooth(blazeWidth, blazeWidth + .025, Math.Abs(p.X))) * Smooth(.68, .78, p.Z);
            double bib = Smooth(-.02, .24, p.Z) * (1 - Smooth(1.15, 1.32, p.Y));
            double collar = Smooth(-.10, .05, p.Z) * (1 - Smooth(.17, .30, p.Z));
            double belly = 1 - Smooth(.43, .58, p.Y);
            double socks = 1 - Smooth(.24, .33, p.Y);
            double body = Math.Max(Math.Max(bib, collar * .95), Math.Max(belly * .9, socks));
            return Clamp(Math.Max(muzzle, blaze) * head + body * (1 - head));
        }

        internal void Update(Matrix3D headMatrix, Matrix3D tailMatrix, double[] legAngles, double breath, double jawOpen, double smile, double mouthRest, double sniff, double sleepPose, double eyeClosure = 0)
        {
            double closure = FaceClosure(sleepPose);
            restingMouth.Color = Color.FromRgb((byte)(40 + 197 * closure), (byte)(32 + 203 * closure), (byte)(33 + 196 * closure));
            for (int i = 0; i < 4; i++) { legSin[i] = Math.Sin(legAngles[i] * Math.PI / 180); legCos[i] = Math.Cos(legAngles[i] * Math.PI / 180); }
            foreach (Surface surface in surfaces)
            {
                for (int i = 0; i < surface.Bind.Length; i++)
                {
                    Vertex vertex = surface.Bind[i]; Point3D bind = vertex.P; Vector3D normal = vertex.N;
                    // Blinks and contented squints close only the eye sockets.
                    // Reuse the continuous face surface and its true normals,
                    // so a closed eye never becomes a separate shaded disk.
                    double localClosure = vertex.EyeSocket > .5f ? Math.Max(closure, eyeClosure) : closure;
                    bind += (vertex.RestP - bind) * localClosure;
                    normal += (vertex.RestN - normal) * localClosure;
                    bind.Y += (1.115 + MouthCurve * bind.X * bind.X - bind.Y) * vertex.Lip * mouthRest * (1 - closure);
                    bind.Y -= vertex.Jaw * jawOpen * .024 * (1 - closure);
                    bind.Y += vertex.Corner * (.003 + smile * .016) * (1 - closure);
                    bind.Y += vertex.Snout * sniff * .0035;
                    bind.Z += vertex.Snout * sniff * .003;
                    Sample w = vertex.W;
                    if (corgi && w.H > .0001)
                    {
                        Point3D shaped = new Point3D(bind.X * CorgiHeadWidth,
                            CorgiHeadY + (bind.Y - 1.345) * CorgiHeadHeight, .45 + (bind.Z - .45) * CorgiHeadDepth);
                        bind += (shaped - bind) * w.H;
                        Vector3D shapedNormal = new Vector3D(normal.X / CorgiHeadWidth,
                            normal.Y / CorgiHeadHeight, normal.Z / CorgiHeadDepth); shapedNormal.Normalize();
                        normal += (shapedNormal - normal) * w.H;
                    }
                    Point3D p = bind; Vector3D n = normal;
                    p.Y -= vertex.Neck * sleepPose * .16;
                    if (w.H > .0001)
                    {
                        p += (headMatrix.Transform(bind) - bind) * w.H;
                        n += (headMatrix.Transform(normal) - normal) * w.H;
                    }
                    if (w.T > .0001)
                    {
                        p += (tailMatrix.Transform(bind) - bind) * w.T;
                        n += (tailMatrix.Transform(normal) - normal) * w.T;
                    }
                    for (int leg = 0; leg < 4; leg++)
                    {
                        double weight = leg == 0 ? w.A : leg == 1 ? w.B : leg == 2 ? w.C : w.E;
                        if (weight < .0001) continue;
                        Point3D legPoint = bind; Vector3D legNormal = normal;
                        if (sleepPose > .0001 && vertex.Foot > .0001)
                        {
                            // Fold the leg while leaving the broad paw resting level on the floor.
                            double fs = -legSin[leg], fc = legCos[leg];
                            double fy = bind.Y - (leg < 2 ? .115 : .108), fz = bind.Z - (leg < 2 ? .304 : -.836);
                            double flatten = sleepPose * vertex.Foot;
                            legPoint.Y += flatten * (fy * (fc - 1) - fz * fs);
                            legPoint.Z += flatten * (fy * fs + fz * (fc - 1));
                            legNormal.Y += flatten * (normal.Y * (fc - 1) - normal.Z * fs);
                            legNormal.Z += flatten * (normal.Y * fs + normal.Z * (fc - 1));
                        }
                        Point3D pivot = legPivots[leg]; double dy = legPoint.Y - pivot.Y, dz = legPoint.Z - pivot.Z;
                        p.Y += weight * (pivot.Y + dy * legCos[leg] - dz * legSin[leg] - bind.Y);
                        p.Z += weight * (pivot.Z + dy * legSin[leg] + dz * legCos[leg] - bind.Z);
                        p.Y += weight * sleepPose * (leg < 2 ? -.040 : -.090);
                        p.Z += weight * sleepPose * (leg < 2 ? .030 : -.035);
                        n.Y += weight * (legNormal.Y * legCos[leg] - legNormal.Z * legSin[leg] - normal.Y);
                        n.Z += weight * (legNormal.Y * legSin[leg] + legNormal.Z * legCos[leg] - normal.Z);
                    }
                    double body = Math.Max(0, 1 - w.H - w.A - w.B - w.C - w.E - w.T);
                    p.X += bind.X * breath * .003 * body; p.Y += (bind.Y - .72) * breath * .006 * body;
                    n.Normalize(); surface.Points[i] = p; surface.Normals[i] = n;
                }
                surface.Mesh.Positions = new Point3DCollection(surface.Points);
                surface.Mesh.Normals = new Vector3DCollection(surface.Normals);
            }
        }
    }
}
