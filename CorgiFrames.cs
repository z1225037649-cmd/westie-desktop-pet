using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WestiePet
{
    // Indexed embedded PNGs are decoded on demand. A bounded cache avoids
    // keeping all 333 decoded frames in memory when models are switched.
    internal static class CorgiFrames
    {
        internal sealed class Clip { internal string Name; internal double Duration; internal Info[] Frames; }
        internal sealed class Info
        {
            internal int Key, Index;
            internal double Time, EyeClosure, SleepPose;
            internal Point Mouth, Paw;
            internal Rect Bounds;
            internal long PngOffset, MaskOffset;
            internal int PngLength, MaskLength;
        }
        internal sealed class Frame { internal BitmapSource Bitmap; internal byte[] HitBits; }
        internal static readonly Dictionary<string, Clip> Clips = new Dictionary<string, Clip>();
        internal static readonly int Width, Height, FrameCount;
        internal static readonly double Span, Fps;
        internal static readonly string SourceHash;
        static readonly Dictionary<int, Frame> cache = new Dictionary<int, Frame>();
        static readonly Queue<int> order = new Queue<int>();
        static CorgiFrames()
        {
            using (Stream stream = Open()) using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != 0x46414357 || reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported Corgi animation resource.");
                Width = reader.ReadInt32(); Height = reader.ReadInt32(); Span = reader.ReadDouble(); Fps = reader.ReadDouble(); SourceHash = reader.ReadString();
                if (Width < 128 || Height < 128 || Width > 2048 || Height > 2048 || !Finite(Span) || Span <= 0 || !Finite(Fps) || Fps < 1 || Fps > 60 || SourceHash.Length != 64)
                    throw new InvalidDataException("Invalid Corgi animation header.");
                int count = reader.ReadInt32(), key = 0;
                if (count < 1 || count > 32) throw new InvalidDataException("Invalid Corgi clip count.");
                for (int c = 0; c < count; c++)
                {
                    Clip clip = new Clip { Name = reader.ReadString(), Duration = reader.ReadDouble() }; int frames = reader.ReadInt32();
                    if (frames < 2 || frames > 1000 || !Finite(clip.Duration) || clip.Duration <= 0) throw new InvalidDataException("Invalid Corgi clip timeline.");
                    clip.Frames = new Info[frames]; double last = -1;
                    for (int i = 0; i < frames; i++)
                    {
                        Info info = new Info { Key = key++, Index = i, Time = reader.ReadDouble() };
                        info.Mouth = new Point(reader.ReadSingle(), reader.ReadSingle()); info.Paw = new Point(reader.ReadSingle(), reader.ReadSingle());
                        info.EyeClosure = reader.ReadSingle(); info.SleepPose = reader.ReadSingle();
                        int left = reader.ReadInt32(), top = reader.ReadInt32(), right = reader.ReadInt32(), bottom = reader.ReadInt32();
                        if (left < 0 || top < 0 || right > Width || bottom > Height || right <= left || bottom <= top) throw new InvalidDataException("Invalid Corgi silhouette bounds.");
                        info.Bounds = new Rect(left, top, right - left, bottom - top);
                        info.PngOffset = reader.ReadInt64(); info.PngLength = reader.ReadInt32(); info.MaskOffset = reader.ReadInt64(); info.MaskLength = reader.ReadInt32();
                        if (!Finite(info.Time) || info.Time < last || info.Time < 0 || info.Time > clip.Duration + .0001
                            || info.PngLength < 20 || info.PngLength > 4000000 || info.PngOffset < 0 || info.PngOffset + info.PngLength > stream.Length
                            || info.MaskLength != (Width * Height + 7) / 8 || info.MaskOffset < 0 || info.MaskOffset + info.MaskLength > stream.Length)
                            throw new InvalidDataException("Invalid Corgi frame data.");
                        last = info.Time; clip.Frames[i] = info;
                    }
                    Clips.Add(clip.Name, clip);
                }
                FrameCount = key;
                foreach (string name in CorgiSprite.ClipNames) if (!Clips.ContainsKey(name)) throw new InvalidDataException("Missing Corgi action: " + name);
            }
        }
        static Stream Open()
        {
            Stream stream = typeof(CorgiFrames).Assembly.GetManifestResourceStream("WestiePet.CorgiAnimation");
            if (stream == null) throw new InvalidOperationException("Missing embedded Corgi animation resource."); return stream;
        }
        internal static Frame Get(Info info)
        {
            lock (cache)
            {
                Frame result; if (cache.TryGetValue(info.Key, out result)) return result;
                using (Stream stream = Open()) using (BinaryReader reader = new BinaryReader(stream))
                {
                    stream.Position = info.PngOffset; byte[] png = reader.ReadBytes(info.PngLength);
                    if (png.Length != info.PngLength) throw new EndOfStreamException();
                    BitmapSource bitmap;
                    using (MemoryStream image = new MemoryStream(png, false)) bitmap = BitmapFrame.Create(image, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (bitmap.PixelWidth != Width || bitmap.PixelHeight != Height) throw new InvalidDataException("Corgi frame dimensions differ from its index.");
                    bitmap.Freeze(); stream.Position = info.MaskOffset;
                    byte[] mask = reader.ReadBytes(info.MaskLength); if (mask.Length != info.MaskLength) throw new EndOfStreamException();
                    result = new Frame { Bitmap = bitmap, HitBits = mask };
                }
                while (cache.Count >= 24) cache.Remove(order.Dequeue());
                cache.Add(info.Key, result); order.Enqueue(info.Key); return result;
            }
        }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
