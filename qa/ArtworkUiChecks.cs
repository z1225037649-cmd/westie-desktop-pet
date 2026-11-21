using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WestiePet;

static class ArtworkUiChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, Private).GetValue(value); }
    static void Require(bool condition, string message) { if(!condition) throw new Exception(message); }
    [STAThread]
    static int Main()
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string path = Path.Combine(root, "小白.settings.xml"), temp = path + ".tmp";
        byte[] old = File.Exists(path) ? File.ReadAllBytes(path) : null, oldTemp = File.Exists(temp) ? File.ReadAllBytes(temp) : null;
        Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        PetWindow pet = null;
        try
        {
            string output=Path.Combine(root,"hd-preview"); Directory.CreateDirectory(output);
            PetSettings settings = new PetSettings { Breed="corgi", Roam=true, Name="小白", Size=1 };
            pet = new PetWindow(settings, true, true, false); pet.Show(); pet.UpdateLayout();
            typeof(PetWindow).GetMethod("ShowHome",Private).Invoke(pet,null);
            Window home = Field<Window>(pet,"home"); home.UpdateLayout();
            ComboBox choice = Field<ComboBox>(pet,"breedChoice");
            Require(choice.SelectedIndex==1,"Home must restore HD Corgi selection.");
            Require(!Field<CheckBox>(pet,"roamOption").IsEnabled && settings.Roam,"Corgi must suspend roaming without losing the Westie preference.");
            choice.SelectedIndex=0;
            Require(Field<WestieModel>(pet,"model").Breed=="westie" && Field<CheckBox>(pet,"roamOption").IsEnabled,"Real home SelectionChanged did not switch back to Westie.");
            choice.SelectedIndex=1;
            WestieModel corgi = Field<WestieModel>(pet,"model");
            Require(corgi.IsPreRendered && settings.Breed=="corgi","Real home SelectionChanged did not switch to the HD artwork.");
            Viewport3D preview=Field<Viewport3D>(pet,"homePreview"), desktop=Field<Viewport3D>(pet,"viewport");
            Require(ReferenceEquals(((ModelVisual3D)preview.Children[0]).Content,corgi.Scene)
                && ReferenceEquals(((ModelVisual3D)desktop.Children[0]).Content,corgi.Scene),"Home selector did not synchronize both scenes.");
            for(int i=0;i<40;i++) corgi.Animate(i/30.0,"idle",i/30.0);
            home.UpdateLayout(); Save((FrameworkElement)home.Content,home.Background,Path.Combine(output,"home.png"));
            ScrollViewer scroll=(ScrollViewer)home.Content;
            scroll.ScrollToEnd(); home.UpdateLayout(); Save(scroll,home.Background,Path.Combine(output,"home-settings.png"));
            Require(Field<TextBlock>(pet,"homeModelHint").Text.Contains("2.5D") || Field<TextBlock>(pet,"homeBreedDescription").Text.Contains("2.5D"),"Artwork mode must identify its presentation honestly.");
            Console.WriteLine("PASS: production home selector changes both models and previews; HD roam suspension preserves saved preference; home layout renders.");
            return 0;
        }
        catch(Exception error) { Console.WriteLine("ARTWORK_UI_ERROR: "+error); return 1; }
        finally {
            if(pet!=null)pet.Close(); app.Shutdown(); Restore(path,old); Restore(temp,oldTemp);
        }
    }
    static void Restore(string path,byte[] data) { if(data==null){if(File.Exists(path))File.Delete(path);}else File.WriteAllBytes(path,data); }
    static void Save(FrameworkElement element,Brush background,string path)
    {
        int width=(int)Math.Ceiling(element.ActualWidth),height=(int)Math.Ceiling(element.ActualHeight);
        RenderTargetBitmap bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        DrawingVisual backdrop=new DrawingVisual();
        using(DrawingContext drawing=backdrop.RenderOpen()) drawing.DrawRectangle(background,null,new Rect(0,0,width,height));
        bitmap.Render(backdrop);bitmap.Render(element);
        PngBitmapEncoder png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(FileStream file=File.Create(path))png.Save(file);
    }
}
