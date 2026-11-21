using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WestiePet;

static class HdRender
{
    [STAThread]
    static int Main()
    {
        try
        {
            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hd-preview");
            Directory.CreateDirectory(output);
            WestieModel pet = new WestieModel("corgi");
            Viewport3D view = PetWindow.CreateViewport(pet);
            Grid stage = new Grid { Width = 640, Height = 600, Background = Brushes.Transparent };
            stage.Children.Add(view); stage.Measure(new Size(640, 600));
            stage.Arrange(new Rect(0, 0, 640, 600)); stage.UpdateLayout();
            double time = 0;
            foreach (string state in new[] { "idle", "pet", "feed", "play", "sleep" })
            {
                for (int i = 0; i < 60; i++) { time += 1.0 / 30; pet.Animate(time, "idle", i / 30.0); }
                for (int i = 0; i < 45; i++) { time += 1.0 / 30; pet.Animate(time, state, i / 30.0); }
                Save(stage, Path.Combine(output, state + ".png"));
            }
            for (int i = 0; i < 60; i++) { time += 1.0 / 30; pet.Animate(time, "idle", i / 30.0); }
            pet.Heading.Angle = -52; pet.Animate(time + .04, "idle", 2.04);
            Save(stage, Path.Combine(output, "left.png"));
            Console.WriteLine("HD artwork renders: " + output); return 0;
        }
        catch(Exception error) { Console.WriteLine(error); return 1; }
    }

    static void Save(Grid stage, string path)
    {
        RenderTargetBitmap image = new RenderTargetBitmap(640, 600, 96, 96, PixelFormats.Pbgra32);
        image.Render(stage); PngBitmapEncoder png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using(FileStream file = File.Create(path)) png.Save(file);
    }
}
