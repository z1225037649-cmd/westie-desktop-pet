using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace WestiePet
{
    public sealed class PetWindow : Window
    {
        readonly PetSettings settings;
        readonly bool verify;
        readonly WestieModel model = new WestieModel();
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random random = new Random();
        readonly Grid design = new Grid { Width = 320, Height = 350 };
        readonly TextBlock bubbleText = new TextBlock();
        readonly Border bubble = new Border();
        readonly Border toolbar = new Border();
        readonly Popup quickActions = new Popup();
        readonly TextBlock effect = new TextBlock();
        readonly Canvas effects = new Canvas { IsHitTestVisible = false };
        readonly Ellipse shadow = new Ellipse();
        readonly Viewport3D viewport;
        readonly Border petHitArea = new Border();
        Forms.NotifyIcon tray;
        Drawing.Icon trayIcon;
        Window home;
        Viewport3D homePreview;
        TextBlock homeStatus;
        string mode = "idle";
        double stateStart, stateUntil, bubbleUntil = 9, nextAction = 22, lastTick;
        double heading = 52, targetHeading = 52, walkDirection = -1;
        double toolbarHideAt;
        bool pressed, dragging, closing, menuOpen;
        Point dragPoint;
        double dragLeft, dragTop;
        int affection, treats;
        public bool TestFailed { get; private set; }
        static readonly Brush Ink = Brush("#2B443D"), Muted = Brush("#78867D"), Sage = Brush("#6B927E"), Paper = Brush("#FCFBF6");

        public PetWindow(PetSettings settings, bool verify)
        {
            this.settings = settings; this.verify = verify;
            Title = "小白 · 西高地桌面宠物";
            Icon = HomeWindowIcon;
            Width = 320 * settings.Size; Height = 350 * settings.Size;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = settings.Topmost;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            Content = new Viewbox { Child = design, Stretch = Stretch.Uniform };

            // Only the visible dog and controls accept mouse input; the rest is transparent.
            shadow.Width = 210; shadow.Height = 27; shadow.HorizontalAlignment = HorizontalAlignment.Center;
            shadow.VerticalAlignment = VerticalAlignment.Top; shadow.Margin = new Thickness(0, 267, 0, 0);
            shadow.Fill = new RadialGradientBrush(Color.FromArgb(62, 36, 47, 40), Colors.Transparent);
            shadow.IsHitTestVisible = false; design.Children.Add(shadow);
            viewport = CreateViewport(model);
            viewport.Width = 306; viewport.Height = 260;
            viewport.HorizontalAlignment = HorizontalAlignment.Center;
            viewport.VerticalAlignment = VerticalAlignment.Top;
            viewport.Cursor = Cursors.Hand;
            // A nearly transparent 2D input surface prevents fine fur and 3D backfaces
            // from passing pointer input through the layered desktop window.
            petHitArea.Width = 306; petHitArea.Height = 260; petHitArea.CornerRadius = new CornerRadius(65);
            petHitArea.HorizontalAlignment = HorizontalAlignment.Center; petHitArea.VerticalAlignment = VerticalAlignment.Top;
            petHitArea.Margin = new Thickness(0, 40, 0, 0); petHitArea.Cursor = Cursors.Hand;
            petHitArea.Background = new SolidColorBrush(Color.FromArgb(1, 248, 248, 241));
            petHitArea.Child = viewport; design.Children.Add(petHitArea);
            petHitArea.MouseLeftButtonDown += BeginDrag;
            petHitArea.MouseMove += MoveDrag;
            petHitArea.MouseLeftButtonUp += EndDrag;
            petHitArea.LostMouseCapture += delegate { pressed = false; dragging = false; };
            petHitArea.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e) { OpenMenu(); e.Handled = true; };
            petHitArea.MouseWheel += delegate(object s, MouseWheelEventArgs e) { ChangeSize(settings.Size + (e.Delta > 0 ? .1 : -.1)); e.Handled = true; };

            bubble.Background = Paper; bubble.CornerRadius = new CornerRadius(17, 17, 17, 4);
            bubble.BorderBrush = Brush("#E4E7DE"); bubble.BorderThickness = new Thickness(1);
            bubble.Padding = new Thickness(15, 10, 15, 10); bubble.MaxWidth = 286;
            bubble.HorizontalAlignment = HorizontalAlignment.Center; bubble.VerticalAlignment = VerticalAlignment.Top;
            bubble.Margin = new Thickness(0, 4, 0, 0); bubble.IsHitTestVisible = false;
            bubbleText.Text = "你好呀，我是小白。右键看看我的小窝！";
            bubbleText.FontSize = 12; bubbleText.Foreground = Ink; bubbleText.TextWrapping = TextWrapping.Wrap;
            bubble.Child = bubbleText; design.Children.Add(bubble);

            effects.Width = 320; effects.Height = 350;
            effect.FontFamily = new FontFamily("Segoe UI Symbol"); effect.FontSize = 29;
            effect.Foreground = Brush("#D98D87"); effect.Text = "♥"; effect.Visibility = Visibility.Hidden;
            Canvas.SetLeft(effect, 222); Canvas.SetTop(effect, 100); effects.Children.Add(effect);
            design.Children.Add(effects);

            toolbar.CornerRadius = new CornerRadius(19); toolbar.Background = Brush("#ECF1E9");
            toolbar.BorderBrush = Brush("#D6E0D2"); toolbar.BorderThickness = new Thickness(1);
            toolbar.Padding = new Thickness(5, 3, 5, 3); toolbar.HorizontalAlignment = HorizontalAlignment.Center;
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(SmallButton("摸摸", "摸摸小白 (空格)", delegate { Act("pet"); }));
            actions.Children.Add(SmallButton("喂食", "给一块小饼干 (F)", delegate { Act("feed"); }));
            actions.Children.Add(SmallButton("玩球", "一起玩球 (B)", delegate { Act("play"); }));
            actions.Children.Add(SmallButton("小窝", "打开小白的小窝", ShowHome));
            foreach (Button button in actions.Children) { button.MinWidth = 54; button.MinHeight = 34; }
            toolbar.Child = actions;
            // A separate popup keeps its hit area and text size even at 70% pet size.
            // The grace period lets the pointer cross transparent space to the buttons.
            quickActions.Child = toolbar; quickActions.PlacementTarget = petHitArea;
            quickActions.Placement = PlacementMode.Bottom; quickActions.StaysOpen = true;
            quickActions.AllowsTransparency = true;
            MouseEnter += delegate { ShowQuickActions(); };
            MouseLeave += delegate { toolbarHideAt = clock.Elapsed.TotalSeconds + 1.5; };
            toolbar.MouseEnter += delegate { toolbarHideAt = double.PositiveInfinity; };
            toolbar.MouseLeave += delegate { toolbarHideAt = clock.Elapsed.TotalSeconds + 1.5; };
            toolbar.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Escape) { quickActions.IsOpen = false; e.Handled = true; } };
            KeyDown += HandleKey;
            Loaded += delegate
            {
                PositionInitially();
                if (verify) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(SelfTest));
                else { CreateTray(); timer.Start(); ShowHome(); }
            };
            timer.Interval = TimeSpan.FromMilliseconds(42);
            timer.Tick += Tick;
            Closing += delegate
            {
                closing = true; timer.Stop();
                quickActions.IsOpen = false;
                if (home != null) home.Close();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (trayIcon != null) trayIcon.Dispose();
                if (!verify) { settings.Left = Left; settings.Top = Top; settings.Save(); }
            };
        }

        void ShowQuickActions()
        {
            if (verify || !IsLoaded || closing || pressed || menuOpen || (home != null && home.IsMouseOver)) return;
            toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            quickActions.HorizontalOffset = (Width - toolbar.DesiredSize.Width) / 2 - 7 * settings.Size;
            quickActions.VerticalOffset = -8 * settings.Size;
            quickActions.IsOpen = true;
            toolbarHideAt = clock.Elapsed.TotalSeconds + 1.5;
        }

        bool PointerInQuickActionArea()
        {
            if (!quickActions.IsOpen || !toolbar.IsVisible) return false;
            PresentationSource source = PresentationSource.FromVisual(toolbar);
            if (source == null) return false;
            Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
            Point start = fromDevice.Transform(toolbar.PointToScreen(new Point(0, 0)));
            Point end = fromDevice.Transform(toolbar.PointToScreen(new Point(toolbar.ActualWidth, toolbar.ActualHeight)));
            Rect area = Rect.Union(new Rect(Left, Top, Width, Height), new Rect(start, end));
            area.Inflate(8, 8);
            return area.Contains(CursorPosition());
        }

        static Color BrushColor(string value) { return (Color)ColorConverter.ConvertFromString(value); }
        static Brush Brush(string value) { var b = new SolidColorBrush(BrushColor(value)); b.Freeze(); return b; }
        static readonly ImageSource HomeWindowIcon = LoadHomeWindowIcon();
        static ImageSource LoadHomeWindowIcon()
        {
            using (Stream stream = typeof(PetWindow).Assembly.GetManifestResourceStream("WestiePet.HomeIcon"))
            {
                if (stream == null) return null;
                BitmapFrame icon = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                icon.Freeze();
                return icon;
            }
        }
        public static Viewport3D CreateViewport(WestieModel dog)
        {
            Viewport3D view = new Viewport3D();
            view.Camera = new OrthographicCamera(new Point3D(0, 1.76, 6), new Vector3D(0, -.66, -6), new Vector3D(0, 1, 0), 2.95);
            view.Children.Add(new ModelVisual3D { Content = dog.Scene });
            RenderOptions.SetEdgeMode(view, EdgeMode.Unspecified);
            return view;
        }
        static Button SmallButton(string text, string tip, Action action)
        {
            Button b = new Button { Content = text, ToolTip = tip, FontSize = 12, Foreground = Ink,
                Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            b.Template = ButtonTemplate();
            b.Click += delegate { action(); }; return b;
        }
        static ControlTemplate ButtonTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
            border.AppendChild(content);
            ControlTemplate t = new ControlTemplate(typeof(Button)); t.VisualTree = border;
            Trigger hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Button.BackgroundProperty, Brush("#DCE6D7"))); t.Triggers.Add(hover);
            Trigger down = new Trigger { Property = Button.IsPressedProperty, Value = true };
            down.Setters.Add(new Setter(Button.OpacityProperty, .65)); t.Triggers.Add(down);
            return t;
        }

        public static Point ClampToWorkArea(double left, double top, double width, double height, Rect area)
        {
            return new Point(Math.Max(area.Left, Math.Min(left, area.Right - width)), Math.Max(area.Top, Math.Min(top, area.Bottom - height)));
        }
        Rect CurrentArea()
        {
            Forms.Screen screen = Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Drawing.Rectangle a = screen.WorkingArea;
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix fromDevice = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            Point p = fromDevice.Transform(new Point(a.Left, a.Top));
            Point end = fromDevice.Transform(new Point(a.Right, a.Bottom));
            return new Rect(p, end);
        }
        void PositionInitially()
        {
            Rect area = CurrentArea();
            double left = double.IsNaN(settings.Left) ? area.Right - Width - 70 : settings.Left;
            double top = double.IsNaN(settings.Top) ? area.Bottom - Height - 10 : settings.Top;
            // Saved positions may belong to a monitor that has since been disconnected.
            bool onScreen = false;
            foreach (Forms.Screen monitor in Forms.Screen.AllScreens)
            {
                PresentationSource src = PresentationSource.FromVisual(this);
                Matrix matrix = src == null ? Matrix.Identity : src.CompositionTarget.TransformFromDevice;
                Point p = matrix.Transform(new Point(monitor.WorkingArea.Left, monitor.WorkingArea.Top));
                Point q = matrix.Transform(new Point(monitor.WorkingArea.Right, monitor.WorkingArea.Bottom));
                if (new Rect(p, q).Contains(new Point(left + Width / 2, top + Height / 2))) { onScreen = true; break; }
            }
            if (!onScreen) { left = area.Right - Width - 70; top = area.Bottom - Height - 10; }
            Left = left; Top = top; ClampPosition();
        }
        void ClampPosition()
        {
            Point point = ClampToWorkArea(Left, Top, Width, Height, CurrentArea()); Left = point.X; Top = point.Y;
        }
        Point CursorPosition()
        {
            Drawing.Point p = Forms.Cursor.Position;
            PresentationSource src = PresentationSource.FromVisual(this);
            return src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
        }
        void BeginDrag(object sender, MouseButtonEventArgs e)
        {
            quickActions.IsOpen = false;
            pressed = true; dragging = false; dragPoint = CursorPosition(); dragLeft = Left; dragTop = Top;
            petHitArea.CaptureMouse(); e.Handled = true;
        }
        void MoveDrag(object sender, MouseEventArgs e)
        {
            if (!pressed) return;
            if (e.LeftButton != MouseButtonState.Pressed) { FinishPointerInteraction(); return; }
            Point p = CursorPosition(); Vector delta = p - dragPoint;
            if (!dragging && delta.Length > 4) { dragging = true; Say("被你抱起来啦～", 2); }
            if (dragging) { Left = dragLeft + delta.X; Top = dragTop + delta.Y; }
        }
        void EndDrag(object sender, MouseButtonEventArgs e)
        {
            if (!pressed) return;
            FinishPointerInteraction(); e.Handled = true;
        }
        void FinishPointerInteraction()
        {
            bool moved = dragging; pressed = false; dragging = false; petHitArea.ReleaseMouseCapture();
            if (moved) { ClampPosition(); SetState("idle", 0); nextAction = clock.Elapsed.TotalSeconds + 20; }
            else Act("pet");
            ShowQuickActions();
        }
        void ChangeSize(double size)
        {
            quickActions.IsOpen = false;
            double oldHeight = Height, oldWidth = Width;
            settings.Size = Math.Round(Math.Max(.7, Math.Min(1.6, size)), 2);
            Width = 320 * settings.Size; Height = 350 * settings.Size;
            Left -= (Width - oldWidth) / 2; Top -= Height - oldHeight;
            ClampPosition(); settings.Save();
        }
        void Say(string text, double seconds)
        {
            bubbleText.Text = text; bubble.Visibility = Visibility.Visible; bubbleUntil = clock.Elapsed.TotalSeconds + seconds;
        }
        void SetState(string state, double seconds)
        {
            mode = state; stateStart = clock.Elapsed.TotalSeconds;
            stateUntil = seconds <= 0 ? double.PositiveInfinity : stateStart + seconds;
            targetHeading = state == "walk" ? (walkDirection < 0 ? -68 : 68) : 52;
            if (homeStatus != null) homeStatus.Text = StatusText();
        }
        string StatusText()
        {
            string state = mode == "sleep" ? "正在做一个饼干味的梦" : mode == "walk" ? "正在认真巡视桌面" : mode == "feed" ? "咔嚓咔嚓，真好吃" : mode == "play" ? "今天也要开心玩耍" : mode == "pet" ? "被摸摸了，心情很好" : "今天也在这里陪你";
            return state + "\n摸摸 " + affection + " 次  ·  小饼干 " + treats + " 块";
        }
        void Act(string action)
        {
            nextAction = clock.Elapsed.TotalSeconds + 22;
            if (action == "pet") { affection++; SetState("pet", 3.6); Say(new[] { "嘿嘿，再摸一下嘛。", "最喜欢和你待在一起了。", "尾巴已经藏不住开心啦！" }[random.Next(3)], 3.6); }
            else if (action == "feed") { treats++; SetState("feed", 4.5); Say("咔嚓咔嚓……小饼干真好吃！", 4.5); }
            else if (action == "play") { SetState("play", 6); Say("接住！这次换你来捡球～", 5); }
            else if (action == "sleep") { SetState("sleep", 0); Say("我眯一会儿，你忙你的。 z Z", 5); }
            else if (action == "wake") { SetState("idle", 0); Say("醒啦！今天有什么好玩的？", 4); }
            else if (action == "walk") { walkDirection = random.Next(2) == 0 ? -1 : 1; SetState("walk", 6); Say("出发，巡视一下我的领地！", 3); }
            if (homeStatus != null) homeStatus.Text = StatusText();
        }
        internal static double AdvanceWalk(double left, ref double direction, double distance, double minimum, double maximum)
        {
            if (maximum <= minimum) return minimum;
            double next = left + direction * distance;
            // Bounce only against the edge being approached. A clamped position
            // at the opposite edge must not reverse direction again next frame.
            if (direction < 0 && next <= minimum) { direction = 1; return minimum; }
            if (direction > 0 && next >= maximum) { direction = -1; return maximum; }
            return Math.Max(minimum, Math.Min(maximum, next));
        }

        void Tick(object sender, EventArgs e)
        {
            if (Program.OpenRequested != null && Program.OpenRequested.WaitOne(0)) ShowHome();
            double t = clock.Elapsed.TotalSeconds, dt = Math.Min(.08, t - lastTick); lastTick = t;
            if (pressed && (Forms.Control.MouseButtons & Forms.MouseButtons.Left) == Forms.MouseButtons.None)
                FinishPointerInteraction();
            if (!pressed && t >= stateUntil) SetState("idle", 0);
            if (!pressed && IsMouseOver && !quickActions.IsOpen) ShowQuickActions();
            if (quickActions.IsOpen)
            {
                if (PointerInQuickActionArea() || toolbar.IsMouseCaptureWithin)
                    toolbarHideAt = t + 1.5;
                else if (t >= toolbarHideAt) quickActions.IsOpen = false;
            }
            bool blocked = pressed || IsMouseOver || quickActions.IsOpen || (home != null && home.IsVisible);
            if (mode == "walk" && !blocked)
            {
                Rect area = CurrentArea();
                double minimum = area.Left + 5, maximum = area.Right - Width - 5;
                if (maximum <= minimum)
                {
                    SetState("idle", 0); nextAction = t + 24;
                }
                else
                {
                    Left = AdvanceWalk(Left, ref walkDirection, 27 * dt, minimum, maximum);
                    targetHeading = walkDirection < 0 ? -68 : 68;
                }
            }
            if (settings.Roam && mode == "idle" && !blocked && t > nextAction)
            {
                nextAction = t + 24 + random.Next(18);
                walkDirection = random.Next(2) == 0 ? -1 : 1; SetState("walk", 4 + random.Next(5));
            }
            double turn = (targetHeading - heading) * (1 - Math.Exp(-dt * 4));
            heading += Math.Max(-70 * dt, Math.Min(70 * dt, turn)); model.Heading.Angle = heading;
            double attentionX, attentionY;
            if (home != null && home.IsVisible && homePreview != null)
            {
                Point cursor = Mouse.GetPosition(homePreview);
                attentionX = (cursor.X - homePreview.ActualWidth * .63) / 170;
                attentionY = (homePreview.ActualHeight * .43 - cursor.Y) / 180;
            }
            else
            {
                Point cursor = CursorPosition();
                attentionX = (cursor.X - Left - Width * .63) / (Width * 1.5);
                attentionY = (Top + Height * .45 - cursor.Y) / (Height * 1.5);
            }
            double proximity = Math.Max(0, Math.Min(1, 1.45 - Math.Sqrt(attentionX * attentionX + attentionY * attentionY) * .55));
            model.LookAt(attentionX, attentionY, proximity);
            model.WalkingPaused = blocked && mode == "walk";
            model.Animate(t, mode, t - stateStart);
            UpdateEffects(t);
            if (t > bubbleUntil) bubble.Visibility = Visibility.Hidden;
        }
        void UpdateEffects(double t)
        {
            bool heart = mode == "pet", sleep = mode == "sleep", feed = mode == "feed";
            effect.Visibility = heart || sleep || feed ? Visibility.Visible : Visibility.Hidden;
            effect.Text = sleep ? "z Z" : feed ? "✦" : "♥";
            effect.FontSize = sleep ? 21 : 29;
            effect.Foreground = sleep ? Sage : feed ? Brush("#C4A165") : Brush("#D98D87");
            Canvas.SetTop(effect, 100 - (t * 18 % 26)); effect.Opacity = .6 + Math.Sin(t * 3) * .3;
        }
        void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                pressed = false; dragging = false; petHitArea.ReleaseMouseCapture(); quickActions.IsOpen = false;
                e.Handled = true; return;
            }
            if (e.Key == Key.Space) Act("pet");
            else if (e.Key == Key.F) Act("feed");
            else if (e.Key == Key.B) Act("play");
            else if (e.Key == Key.S) Act(mode == "sleep" ? "wake" : "sleep");
            else if (e.Key == Key.R) { settings.Roam = !settings.Roam; settings.Save(); Say(settings.Roam ? "好耶，可以自由散步啦。" : "我会乖乖待在这里。", 3); }
            else return;
            e.Handled = true;
        }
        void AddMenu(ContextMenu menu, string title, Action action)
        {
            MenuItem item = new MenuItem { Header = title, Padding = new Thickness(12, 7, 12, 7) };
            item.Click += delegate { action(); }; menu.Items.Add(item);
        }
        void OpenMenu()
        {
            quickActions.IsOpen = false; menuOpen = true;
            ContextMenu menu = new ContextMenu { FontFamily = FontFamily, FontSize = 13, Background = Paper, Foreground = Ink, Padding = new Thickness(5) };
            menu.Closed += delegate { menuOpen = false; };
            AddMenu(menu, "小白的小窝…", ShowHome);
            menu.Items.Add(new Separator());
            AddMenu(menu, "摸摸脑袋     空格", delegate { Act("pet"); });
            AddMenu(menu, "喂块饼干     F", delegate { Act("feed"); });
            AddMenu(menu, "一起玩球     B", delegate { Act("play"); });
            AddMenu(menu, mode == "sleep" ? "叫醒小白     S" : "睡个午觉     S", delegate { Act(mode == "sleep" ? "wake" : "sleep"); });
            AddMenu(menu, "散步一圈", delegate { Act("walk"); });
            menu.Items.Add(new Separator());
            AddMenu(menu, (settings.Roam ? "✓ " : "   ") + "自由散步", delegate { settings.Roam = !settings.Roam; if (!settings.Roam && mode == "walk") SetState("idle", 0); settings.Save(); });
            AddMenu(menu, (settings.Topmost ? "✓ " : "   ") + "始终在最上面", delegate { Topmost = settings.Topmost = !settings.Topmost; settings.Save(); });
            AddMenu(menu, "大一点    滚轮向上", delegate { ChangeSize(settings.Size + .1); });
            AddMenu(menu, "小一点    滚轮向下", delegate { ChangeSize(settings.Size - .1); });
            AddMenu(menu, "回到屏幕右下角", ResetPosition);
            menu.Items.Add(new Separator());
            AddMenu(menu, "让小白回家 · 退出", Close);
            menu.PlacementTarget = viewport; menu.IsOpen = true;
        }
        void ResetPosition()
        {
            Rect area = CurrentArea(); Left = area.Right - Width - 30; Top = area.Bottom - Height - 10;
            ClampPosition(); SetState("idle", 0); Say("我回来啦！", 3);
        }

        void CreateTray()
        {
            tray = new Forms.NotifyIcon();
            using (Stream stream = typeof(PetWindow).Assembly.GetManifestResourceStream("WestiePet.HomeIcon"))
            using (Drawing.Icon source = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize))
            {
                trayIcon = (Drawing.Icon)source.Clone();
                tray.Icon = trayIcon;
            }
            tray.Text = "小白 · 西高地桌面宠物";
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开小白的小窝", null, delegate { Dispatcher.BeginInvoke(new Action(ShowHome)); });
            menu.Items.Add("找回小白", null, delegate { Dispatcher.BeginInvoke(new Action(ResetPosition)); });
            menu.Items.Add("退出", null, delegate { Dispatcher.BeginInvoke(new Action(Close)); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ShowHome)); };
            tray.Visible = true;
        }

        void ShowHome()
        {
            quickActions.IsOpen = false;
            if (home != null) { home.Activate(); return; }
            home = new Window { Title = "小白的小窝", Icon = HomeWindowIcon, Width = 414, Height = 600, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brush("#F8F8F1"),
                FontFamily = FontFamily, Foreground = Ink, Topmost = settings.Topmost };
            StackPanel page = new StackPanel { Margin = new Thickness(28, 21, 28, 20) };
            home.Content = page;
            page.Children.Add(new TextBlock { Text = "A LITTLE COMPANION", FontSize = 10, Foreground = Sage, FontWeight = FontWeights.SemiBold });
            page.Children.Add(new TextBlock { Text = "小白的小窝", FontSize = 27, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 3) });
            page.Children.Add(new TextBlock { Text = "一只西高地，陪你慢慢来。", FontSize = 12, Foreground = Muted });
            Grid stage = new Grid { Height = 212, Margin = new Thickness(0, 8, 0, 0) };
            Ellipse floor = new Ellipse { Width = 130, Height = 19, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12),
                Fill = new RadialGradientBrush(Color.FromArgb(48, 66, 88, 69), Colors.Transparent) };
            stage.Children.Add(floor);
            Viewport3D preview = CreateViewport(model);
            homePreview = preview;
            preview.Width = 286; preview.Height = 212;
            ((OrthographicCamera)preview.Camera).Width = 3.20;
            stage.Children.Add(preview); page.Children.Add(stage);
            homeStatus = new TextBlock { Text = StatusText(), FontSize = 12, Foreground = Muted, TextAlignment = TextAlignment.Center, LineHeight = 21, Margin = new Thickness(0, 0, 0, 12) };
            page.Children.Add(homeStatus);
            UniformGridButtons(page);
            Border rule = new Border { Height = 1, Background = Brush("#E2E7DC"), Margin = new Thickness(0, 18, 0, 14) }; page.Children.Add(rule);
            StackPanel sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
            sizeRow.Children.Add(new TextBlock { Text = "小狗大小", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Width = 76 });
            Slider size = new Slider { Minimum = .7, Maximum = 1.6, Value = settings.Size, TickFrequency = .1, IsSnapToTickEnabled = true, Width = 230, VerticalAlignment = VerticalAlignment.Center };
            size.ValueChanged += delegate { if (home != null && home.IsLoaded) ChangeSize(size.Value); }; sizeRow.Children.Add(size); page.Children.Add(sizeRow);
            CheckBox roam = new CheckBox { Content = "让小白自由散步", IsChecked = settings.Roam, FontSize = 12, Margin = new Thickness(0, 14, 0, 0), Foreground = Ink };
            roam.Click += delegate { settings.Roam = roam.IsChecked == true; if (!settings.Roam && mode == "walk") SetState("idle", 0); settings.Save(); }; page.Children.Add(roam);
            page.Children.Add(new TextBlock { Text = "拖动小狗移动位置 · 右键打开菜单 · 滚轮调整大小\n关闭小窝后，小白依然会在桌面陪你。", FontSize = 10, Foreground = Muted, Margin = new Thickness(0, 15, 0, 0), LineHeight = 18 });
            home.Closed += delegate { home = null; homeStatus = null; if (!closing) nextAction = clock.Elapsed.TotalSeconds + 20; };
            home.Show(); home.Activate();
        }
        void UniformGridButtons(StackPanel page)
        {
            System.Windows.Controls.Primitives.UniformGrid row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            row.Children.Add(SmallButton("♥ 摸摸", "摸摸小白", delegate { Act("pet"); }));
            row.Children.Add(SmallButton("✦ 喂食", "一块小饼干", delegate { Act("feed"); }));
            row.Children.Add(SmallButton("● 玩球", "一起玩球", delegate { Act("play"); }));
            row.Children.Add(SmallButton("☾ 睡觉", "睡觉 / 叫醒", delegate { Act(mode == "sleep" ? "wake" : "sleep"); }));
            page.Children.Add(row);
        }

        void SelfTest()
        {
            string directory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa");
            Directory.CreateDirectory(directory);
            List<string> log = new List<string>();
            try
            {
                Rect area = new Rect(-1920, 0, 1920, 1080);
                Point p = ClampToWorkArea(400, -200, 320, 350, area);
                if (p != new Point(-320, 0)) throw new InvalidOperationException("Negative-monitor clamp failed.");
                p = ClampToWorkArea(-9999, 9999, 320, 350, new Rect(0, 0, 1920, 1040));
                if (p != new Point(0, 690)) throw new InvalidOperationException("Off-screen recovery failed.");
                log.Add("PASS: monitor bounds and off-screen recovery");
                if (model.TriangleCount < 10000 || model.TriangleCount > 200000) throw new InvalidOperationException("Unexpected mesh complexity.");
                log.Add("PASS: articulated model, " + model.TriangleCount + " triangles");
                HashSet<string> hashes = new HashSet<string>();
                foreach (string state in new[] { "idle", "pet", "feed", "play", "sleep", "walk" })
                {
                    mode = state;
                    model.Heading.Angle = state == "walk" ? -68 : 52;
                    for (int frame = 0; frame < 45; frame++)
                        model.Animate(1.1 + frame * .042, state, frame * .042);
                    stateStart = 0; UpdateEffects(44 * .042);
                    Say(state == "idle" ? "你好呀，我是小白。" : "小白 · " + state, 10);
                    design.UpdateLayout();
                    RenderTargetBitmap bitmap = new RenderTargetBitmap(640, 700, 192, 192, PixelFormats.Pbgra32);
                    bitmap.Render(design);
                    PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    string file = System.IO.Path.Combine(directory, state + ".png");
                    using (FileStream stream = File.Create(file)) encoder.Save(stream);
                    byte[] pixels = new byte[640 * 700 * 4]; bitmap.CopyPixels(pixels, 640 * 4, 0);
                    int opaque = 0, transparent = 0;
                    for (int i = 3; i < pixels.Length; i += 4) { if (pixels[i] == 0) transparent++; else opaque++; }
                    if (opaque < 10000 || transparent < 10000) throw new InvalidOperationException("Transparency/render failed: " + state);
                    using (var sha = System.Security.Cryptography.SHA256.Create()) hashes.Add(Convert.ToBase64String(sha.ComputeHash(pixels)));
                    log.Add("PASS: " + state + " renders with transparent background (" + opaque + " visible pixels)");
                }
                if (hashes.Count != 6) throw new InvalidOperationException("Action frames are not distinct.");
                log.Add("PASS: six visually distinct action frames");
                mode = "play"; stateStart = 0; bubble.Visibility = Visibility.Hidden; model.Heading.Angle = 52;
                for (int frame = 0; frame < 60; frame++) model.Animate(4 + frame * .042, "play", 0);
                for (int frame = 0; frame < 24; frame++)
                {
                    double actionTime = frame / 6.0;
                    for (int tick = 0; tick < 4; tick++) model.Animate(7 + actionTime + tick / 24.0, "play", actionTime + tick / 24.0);
                    UpdateEffects(actionTime + 3 / 24.0); design.UpdateLayout();
                    RenderTargetBitmap bitmap = new RenderTargetBitmap(640, 700, 192, 192, PixelFormats.Pbgra32); bitmap.Render(design);
                    PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (FileStream stream = File.Create(System.IO.Path.Combine(directory, "play-motion-" + frame.ToString("00") + ".png"))) encoder.Save(stream);
                }
                log.Add("PASS: 24 synchronized play frames using production ball and model motion");
                // Exercise the same transition paths used by toolbar and menu actions.
                foreach (string action in new[] { "pet", "feed", "play", "sleep", "wake", "walk" })
                {
                    Act(action);
                    string expected = action == "wake" ? "idle" : action;
                    if (mode != expected) throw new InvalidOperationException("Action failed: " + action);
                }
                if (affection != 1 || treats != 1) throw new InvalidOperationException("Interaction counters failed.");
                log.Add("PASS: action transitions, wake, interaction counters");
                File.WriteAllLines(System.IO.Path.Combine(directory, "results.txt"), log.ToArray());
            }
            catch (Exception e)
            {
                TestFailed = true; log.Add("FAIL: " + e); File.WriteAllLines(System.IO.Path.Combine(directory, "results.txt"), log.ToArray());
            }
            Close();
        }
    }
}
