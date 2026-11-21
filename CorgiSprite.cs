using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WestiePet
{
    // High-resolution 2.5D cutout rig. One supplied seated illustration is
    // deformed locally; this is neither a volumetric dog nor a generated frame
    // sequence. In particular, it cannot invent a quadruped walk or lying pose.
    internal sealed class CorgiSprite
    {
        internal readonly Model3DGroup Model = new Model3DGroup();
        internal int TriangleCount;
        internal Rect3D Bounds { get { return Model.Bounds; } }
        internal Point3D Mouth { get { return Mirrored(Deform(702 * sourceScaleX, 845 * sourceScaleY, .13)); } }
        internal Point3D FrontPaw { get { return Mirrored(Deform(925 * sourceScaleX, 1711 * sourceScaleY, .13)); } }

        readonly BitmapSource artwork;
        readonly byte[] alphaPixels;
        readonly int sourceWidth, sourceHeight;
        readonly Int32Rect crop;
        readonly double unit, sourceScaleX, sourceScaleY;
        readonly List<Sheet> sheets = new List<Sheet>();
        readonly ScaleTransform3D facing = new ScaleTransform3D(1, 1, 1);
        double previousTime = double.NaN, sleepPose, mirror = 1;
        double headAngle, nuzzle, nod, tailAngle, earAngle, breath, shuffle, paw;
        double eyeClosure;
        internal double EyeClosure { get { return eyeClosure; } }

        sealed class Sheet
        {
            internal MeshGeometry3D Mesh;
            internal Point[] Source;
            internal Point3D[] Current;
            internal double Depth;
        }

        internal CorgiSprite()
        {
            using (Stream stream = typeof(CorgiSprite).Assembly.GetManifestResourceStream("WestiePet.CorgiArtwork"))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded Corgi artwork: WestiePet.CorgiArtwork.");
                BitmapImage image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream; image.EndInit(); image.Freeze(); artwork = image;
            }
            sourceWidth = artwork.PixelWidth; sourceHeight = artwork.PixelHeight;
            sourceScaleX = sourceWidth / 1920.0; sourceScaleY = sourceHeight / 1920.0;
            BitmapSource straight = new FormatConvertedBitmap(artwork, PixelFormats.Bgra32, null, 0);
            alphaPixels = new byte[sourceWidth * sourceHeight * 4];
            straight.CopyPixels(alphaPixels, sourceWidth * 4, 0);
            crop = AlphaBounds(); unit = 1.90 / crop.Height;
            CroppedBitmap trimmed = new CroppedBitmap(artwork, crop); trimmed.Freeze();
            // The only texture is the unmodified supplied artwork. Eye closure
            // is local geometry compression, with surrounding original eyelid
            // fur stretched continuously into the space left by the aperture.
            AddSheet(new Rect(crop.X, crop.Y, crop.Width, crop.Height), Texture(trimmed), 80, 112, 0);
            Model.Transform = facing;
            Update(0, "idle", 0, -52, false);
        }

        static ImageBrush Texture(BitmapSource source)
        {
            ImageBrush brush = new ImageBrush(source) { Stretch = Stretch.Fill, TileMode = TileMode.None };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            return brush;
        }

        Int32Rect AlphaBounds()
        {
            int left = sourceWidth, top = sourceHeight, right = -1, bottom = -1;
            for (int y = 0; y < sourceHeight; y++)
            for (int x = 0; x < sourceWidth; x++)
            {
                if (alphaPixels[(y * sourceWidth + x) * 4 + 3] < 8) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
            if (right <= left || bottom <= top) throw new InvalidOperationException("Corgi artwork has no visible alpha silhouette.");
            left = Math.Max(0, left - 8); top = Math.Max(0, top - 8);
            right = Math.Min(sourceWidth - 1, right + 8); bottom = Math.Min(sourceHeight - 1, bottom + 8);
            return new Int32Rect(left, top, right - left + 1, bottom - top + 1);
        }

        void AddSheet(Rect source, Brush image, int columns, int rows, double depth)
        {
            SortedSet<double> xs = new SortedSet<double>(), ys = new SortedSet<double>();
            for (int x = 0; x <= columns; x++) xs.Add(source.X + source.Width * x / columns);
            for (int y = 0; y <= rows; y++) ys.Add(source.Y + source.Height * y / rows);
            // Four-source-pixel samples resolve the slanted eye apertures. Merge
            // them into the coarse grid, retaining large cells away from the face.
            for (int x = 540; x <= 900; x += 4)
                if (x * sourceScaleX > source.Left && x * sourceScaleX < source.Right) xs.Add(x * sourceScaleX);
            for (int y = 580; y <= 792; y += 4)
                if (y * sourceScaleY > source.Top && y * sourceScaleY < source.Bottom) ys.Add(y * sourceScaleY);
            double[] xx = new double[xs.Count], yy = new double[ys.Count]; xs.CopyTo(xx); ys.CopyTo(yy);
            int stride = xx.Length;
            Sheet sheet = new Sheet { Mesh = new MeshGeometry3D(), Source = new Point[xx.Length * yy.Length],
                Current = new Point3D[xx.Length * yy.Length], Depth = depth };
            for (int y = 0; y < yy.Length; y++)
            for (int x = 0; x < xx.Length; x++)
            {
                int index = y * stride + x;
                double u = (xx[x] - source.X) / source.Width, v = (yy[y] - source.Y) / source.Height;
                sheet.Source[index] = new Point(xx[x], yy[y]);
                sheet.Current[index] = Rest(sheet.Source[index].X, sheet.Source[index].Y, depth);
                sheet.Mesh.TextureCoordinates.Add(new Point(u, v));
                sheet.Mesh.Normals.Add(new Vector3D(0, 0, 1));
            }
            for (int y = 0; y < yy.Length - 1; y++)
            for (int x = 0; x < xx.Length - 1; x++)
            {
                int a = y * stride + x, b = a + stride;
                // Source y increases downward; keep the visible front normal +Z.
                sheet.Mesh.TriangleIndices.Add(a); sheet.Mesh.TriangleIndices.Add(b); sheet.Mesh.TriangleIndices.Add(a + 1);
                sheet.Mesh.TriangleIndices.Add(a + 1); sheet.Mesh.TriangleIndices.Add(b); sheet.Mesh.TriangleIndices.Add(b + 1);
            }
            sheet.Mesh.Positions = new Point3DCollection(sheet.Current);
            // WPF's emissive color alone contributes RGB but no surface alpha
            // to transparent RenderTargetBitmap/layered windows. A black
            // diffuse pass supplies the same texture alpha without relighting
            // the pre-rendered fur, then emission restores the source color.
            MaterialGroup material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(image) { Color = Colors.Black, AmbientColor = Colors.Black });
            material.Children.Add(new EmissiveMaterial(image));
            GeometryModel3D geometry = new GeometryModel3D(sheet.Mesh, material) { BackMaterial = material };
            Model.Children.Add(geometry); sheets.Add(sheet); TriangleCount += (xx.Length - 1) * (yy.Length - 1) * 2;
        }

        internal void Update(double time, string state, double stateTime, double heading, bool walkingPaused)
        {
            double age = Math.Max(0, stateTime);
            double dt = double.IsNaN(previousTime) || time <= previousTime ? .042 : Math.Min(.10, time - previousTime);
            previousTime = time;
            bool sleeping = state == "sleep", petting = state == "pet", feeding = state == "feed";
            sleepPose += ((sleeping ? 1 : 0) - sleepPose) * (1 - Math.Exp(-dt * 4));
            mirror = heading < 0 ? 1 : -1; facing.ScaleX = mirror;
            double pet = petting ? Smooth(age / .25) * Smooth((3.6 - age) / .45) : 0;
            double feed = feeding ? Smooth(age / .4) * Smooth((4.5 - age) / .5) : 0;
            breath = Math.Sin(time * (sleeping ? 1.7 : 2.0));
            nuzzle = pet * Math.Sin(age * 4.1);
            nod = feed * (1.8 + Math.Sin(age * 6.4) * 1.1);
            headAngle = (Math.Sin(time * .65) * .23 * (1 - sleepPose) + 4.0 * sleepPose + nuzzle * 1.6 + nod) * Math.PI / 180;
            tailAngle = Math.Sin(time * (petting ? 7 : 3.1)) * (sleeping ? .20 : petting ? 3.8 : 1.25) * Math.PI / 180;
            earAngle = Math.Sin(time * 1.1 + .7) * .65 * Math.PI / 180 + pet * .006;
            // A seated illustration only shifts its weight; it does not sprout
            // fabricated walking feet. The window should not roam in this mode.
            shuffle = state == "walk" && !walkingPaused ? Math.Sin(time * 3) * .002 : 0;
            paw = state == "play" && age < 1.3 ? Math.Sin(Math.Min(1, age / 1.3) * Math.PI) * .004 : 0;
            double phase = time % 5.8;
            double blink = phase >= 3.7 && phase <= 3.98 ? Math.Sin((phase - 3.7) / .28 * Math.PI) : 0;
            eyeClosure = Math.Max(Smooth((sleepPose - .15) / .55), Math.Max(pet * .94, blink));
            RefreshGeometry();
        }

        // QA can compare open and closed apertures at the exact same bone pose.
        // This does not advance clocks, change head/ear/tail motion or repaint UVs.
        internal void SetEyeClosure(double closure)
        {
            eyeClosure = double.IsNaN(closure) ? 0 : Math.Max(0, Math.Min(1, closure));
            RefreshGeometry();
        }

        internal void RefreshGeometry()
        {
            foreach (Sheet sheet in sheets)
            {
                for (int i = 0; i < sheet.Source.Length; i++)
                    sheet.Current[i] = Deform(sheet.Source[i].X, sheet.Source[i].Y, sheet.Depth);
                sheet.Mesh.Positions = new Point3DCollection(sheet.Current);
            }
        }

        Point3D Rest(double x, double y, double z)
        { return new Point3D((x - crop.X - crop.Width * .5) * unit, .03 + (crop.Y + crop.Height - y) * unit, z); }

        Point3D Deform(double x, double y, double z)
        {
            double sx = x / sourceScaleX, sy = y / sourceScaleY;
            double visibleX = sx, visibleY = sy;
            if (eyeClosure > 0 && sx >= 540 && sx <= 900 && sy >= 570 && sy <= 790)
            {
                CompressEye(ref visibleX, ref visibleY, 623, 703, 43, 18, -.48);
                CompressEye(ref visibleX, ref visibleY, 780, 663, 55, 22, -.31);
            }
            Point3D p = Rest(visibleX * sourceScaleX, visibleY * sourceScaleY, z);
            double head = 1 - Range(880, 1130, sy);
            double chest = Math.Exp(-Math.Pow((sx - 920) / 360, 2) - Math.Pow((sy - 1280) / 420, 2));
            double fixedFeet = 1 - Range(1510, 1740, sy);
            p.X += breath * .0035 * chest * fixedFeet + shuffle * fixedFeet;
            p.Y += breath * .003 * chest * fixedFeet;
            double nearEar = Range(780, 990, sx) * (1 - Range(460, 670, sy));
            double farEar = (1 - Range(650, 790, sx)) * (1 - Range(610, 760, sy));
            Rotate(ref p, Rest(990 * sourceScaleX, 620 * sourceScaleY, z), earAngle * nearEar);
            Rotate(ref p, Rest(650 * sourceScaleX, 720 * sourceScaleY, z), -earAngle * farEar * .8);
            Rotate(ref p, Rest(845 * sourceScaleX, 995 * sourceScaleY, z), headAngle * head);
            p.X += nuzzle * .009 * head; p.Y -= .045 * sleepPose * head;
            double tail = Range(1290, 1440, sx) * Range(1030, 1240, sy) * (1 - Range(1510, 1670, sy));
            Rotate(ref p, Rest(1450 * sourceScaleX, 1510 * sourceScaleY, z), tailAngle * tail);
            double front = Math.Exp(-Math.Pow((sx - 925) / 100, 2) - Math.Pow((sy - 1685) / 80, 2));
            p.X -= paw * front; p.Y += paw * .5 * front;
            return p;
        }

        void CompressEye(ref double x, ref double y, double centerX, double centerY,
            double halfWidth, double halfHeight, double angle)
        {
            if (eyeClosure <= 0) return;
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            double dx = x - centerX, dy = y - centerY;
            double u = dx * cosine + dy * sine, v = -dx * sine + dy * cosine;
            double across = 1 - Range(halfWidth, halfWidth * 1.35, Math.Abs(u));
            double absolute = Math.Abs(v), outside = halfHeight * 3.0;
            if (across <= 0 || absolute >= outside) return;
            // Full sleep leaves half a percent of the source aperture height.
            // The .94 pet squint reaches roughly 1.5%, without repainting eyes.
            double amount = Smooth(eyeClosure) * across;
            double scale = 1 - .995 * amount;
            double compressed;
            if (absolute <= halfHeight) compressed = absolute * scale;
            else
            {
                // Monotone Hermite continuation: the original upper/lower fur
                // expands smoothly around the squeezed eye, returning to the
                // original position and slope three eye-heights away.
                double span = outside - halfHeight, t = (absolute - halfHeight) / span;
                double t2 = t * t, t3 = t2 * t;
                compressed = (2 * t3 - 3 * t2 + 1) * (halfHeight * scale)
                    + (t3 - 2 * t2 + t) * (span * scale)
                    + (-2 * t3 + 3 * t2) * outside + (t3 - t2) * span;
            }
            double nextV = v < 0 ? -compressed : compressed;
            double shift = nextV - v;
            x -= sine * shift; y += cosine * shift;
        }

        static void Rotate(ref Point3D point, Point3D pivot, double angle)
        {
            double x = point.X - pivot.X, y = point.Y - pivot.Y;
            double sine = Math.Sin(angle), cosine = Math.Cos(angle);
            point.X = pivot.X + x * cosine - y * sine; point.Y = pivot.Y + x * sine + y * cosine;
        }

        Point3D Mirrored(Point3D point) { point.X *= mirror; return point; }
        static double Smooth(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
        static double Range(double low, double high, double value) { return Smooth((value - low) / (high - low)); }

        // A CPU alpha lookup is deliberately independent of WPF 3D ray tests.
        // Three fixed-point steps undo the small mesh deformations sufficiently
        // accurately for the fur silhouette; a 3px neighbourhood keeps wisps usable.
        internal bool HitTest(double worldX, double worldY)
        {
            if (double.IsNaN(worldX) || double.IsNaN(worldY) || double.IsInfinity(worldX) || double.IsInfinity(worldY)) return false;
            worldX *= mirror;
            double x = crop.X + crop.Width * .5 + worldX / unit;
            double y = crop.Y + crop.Height - (worldY - .03) / unit;
            if (x < crop.X - 140 || x > crop.X + crop.Width + 140 || y < crop.Y - 140 || y > crop.Y + crop.Height + 140) return false;
            for (int i = 0; i < 3; i++)
            {
                Point3D guess = Deform(x, y, 0);
                x += (worldX - guess.X) / unit; y -= (worldY - guess.Y) / unit;
            }
            int centerX = (int)Math.Round(x), centerY = (int)Math.Round(y);
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int sampleX = centerX + dx, sampleY = centerY + dy;
                if (sampleX >= 0 && sampleY >= 0 && sampleX < sourceWidth && sampleY < sourceHeight
                    && alphaPixels[(sampleY * sourceWidth + sampleX) * 4 + 3] >= 24) return true;
            }
            return false;
        }
    }
}
