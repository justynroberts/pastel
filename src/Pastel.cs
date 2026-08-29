// Pastel — a delightful clipboard manager for Windows (Pasta-style)
// Native WPF, compiles with the .NET Framework 4.8 in-box C# 5 compiler.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: System.Reflection.AssemblyTitle("Pastel")]
[assembly: System.Reflection.AssemblyProduct("Pastel")]
[assembly: System.Reflection.AssemblyDescription("A delightful clipboard manager for Windows")]
[assembly: System.Reflection.AssemblyCopyright("2026")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]

namespace Pastel
{
    // ------------------------------------------------------------------ native
    internal static class Native
    {
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // 2 = round
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;        // 3 = acrylic (transient window)

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        public const int WM_HOTKEY = 0x0312;
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
        public const uint KEYEVENTF_KEYUP = 0x2;
        public const byte VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_V = 0x56;
        public const int HOTKEY_ID = 0x5A31;
    }

    // ------------------------------------------------------------------ model
    public class ClipItem
    {
        public string Id;
        public string Type;        // text | link | color | code | image | files
        public string Text;        // content (or file list / image caption)
        public string Rtf;         // rich text (RTF) if the copy carried it
        public string Html;        // CF_HTML payload if the copy carried it
        public string ImageFile;   // png filename for image items
        public string Hash;        // dedup hash for images
        public string Source;      // friendly app name
        public long Ts;            // DateTime.UtcNow.Ticks
        public bool Pinned;
        public int Count;          // times copied / pasted
        public int W, H;           // image dimensions
        public int Ci;             // color index

        public DateTime Time { get { return new DateTime(Ts, DateTimeKind.Utc).ToLocalTime(); } }
    }

    // ------------------------------------------------------------------ store
    public class Store
    {
        public readonly string Dir;
        public readonly string ImgDir;
        private readonly string _file;
        public List<ClipItem> Items = new List<ClipItem>();
        public const int MaxItems = 500;

        public Store(string dir)
        {
            Dir = dir;
            ImgDir = Path.Combine(dir, "images");
            Directory.CreateDirectory(Dir);
            Directory.CreateDirectory(ImgDir);
            _file = Path.Combine(dir, "history.json");
            Load();
        }

        private static JavaScriptSerializer Json()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = 100000000;
            return s;
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_file)) return;
                var list = Json().Deserialize<List<ClipItem>>(File.ReadAllText(_file, Encoding.UTF8));
                if (list != null) Items = list.Where(i => i != null && !string.IsNullOrEmpty(i.Type)).ToList();
            }
            catch { Items = new List<ClipItem>(); }
        }

        public void Save()
        {
            try
            {
                Prune();
                File.WriteAllText(_file, Json().Serialize(Items), Encoding.UTF8);
            }
            catch { }
        }

        private void Prune()
        {
            if (Items.Count <= MaxItems) return;
            var keep = new List<ClipItem>();
            int budget = MaxItems;
            // pinned always survive
            foreach (var it in Items)
            {
                if (it.Pinned) { keep.Add(it); }
            }
            budget -= keep.Count;
            foreach (var it in Items)
            {
                if (it.Pinned) continue;
                if (budget <= 0)
                {
                    if (!string.IsNullOrEmpty(it.ImageFile))
                        try { File.Delete(Path.Combine(ImgDir, it.ImageFile)); } catch { }
                    continue;
                }
                keep.Add(it); budget--;
            }
            Items = keep.OrderByDescending(i => i.Ts).ToList();
        }
    }

    // ------------------------------------------------------------------ theme
    internal static class Theme
    {
        public static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        public static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush(C(hex)); b.Freeze(); return b;
        }
        // single restrained accent — a muted indigo used only for selection,
        // the active filter, and small highlights
        public const string AccentHex = "#8A8AF2";
        public static readonly Brush AccentBrush = B(AccentHex);
        public static readonly Brush TextPrimary = B("#F2F2F7");
        public static readonly Brush TextSecondary = B("#A5A4B4");
        public static readonly Brush TextDim = B("#77768A");
        public static readonly Brush CardBrush = B("#A32B2A34");

        public static readonly FontFamily UiFont =
            new FontFamily("Inter, Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily MonoFont =
            new FontFamily("Cascadia Code, Cascadia Mono, Consolas");
    }

    // ------------------------------------------------------------------ main window
    public class MainWindow : Window
    {
        private readonly Store _store;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _tickTimer;
        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _lastForeground = IntPtr.Zero;
        private DateTime _ignoreClipUntil = DateTime.MinValue;
        private bool _paused;
        private int _colorCounter;
        private string _filter = "all";
        private int _selected = -1;
        private List<ClipItem> _visible = new List<ClipItem>();
        private readonly List<Border> _cards = new List<Border>();
        private readonly List<KeyValuePair<TextBlock, ClipItem>> _timeTexts = new List<KeyValuePair<TextBlock, ClipItem>>();
        private readonly Dictionary<string, Border> _pills = new Dictionary<string, Border>();
        private readonly Dictionary<string, TextBlock> _pillLabels = new Dictionary<string, TextBlock>();

        private TextBox _search;
        private TextBlock _searchHint;
        private WrapPanel _wrap;
        private ScrollViewer _scroll;
        private Grid _emptyState;
        private TextBlock _countText;
        private Border _root;
        private TranslateTransform _rootSlide;
        private Popup _menu;
        private System.Windows.Forms.NotifyIcon _tray;
        public bool KeepVisible;   // test hook: disables hide-on-focus-loss

        public MainWindow(Store store, bool demo)
        {
            _store = store;
            if (demo) SeedDemo();
            _colorCounter = _store.Items.Count;

            Title = "Pastel";
            FontFamily = Theme.UiFont;
            Width = 970; Height = 620;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            BuildUi();
            SetupTray();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _saveTimer.Tick += delegate { _saveTimer.Stop(); _store.Save(); };
            _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _tickTimer.Tick += delegate { if (IsVisible) UpdateTimeTexts(); };
            _tickTimer.Start();

            Deactivated += delegate { if (IsVisible && !KeepVisible) HideWindow(false); };
            PreviewKeyDown += OnKey;

            // create the HWND now (window stays hidden) so we can listen to the clipboard
            var helper = new WindowInteropHelper(this);
            _hwnd = helper.EnsureHandle();
            var src = HwndSource.FromHwnd(_hwnd);
            src.AddHook(WndProc);
            ApplyBackdrop();
            Native.AddClipboardFormatListener(_hwnd);
            if (!Native.RegisterHotKey(_hwnd, Native.HOTKEY_ID, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_V))
                Native.RegisterHotKey(_hwnd, Native.HOTKEY_ID, Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT, Native.VK_V);
        }

        // -------------------------------------------------------------- backdrop
        private void ApplyBackdrop()
        {
            bool ok = false;
            try
            {
                int dark = 1;
                Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
                int corner = 2; // DWMWCP_ROUND
                Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
                int backdrop = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
                ok = Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4) == 0;
            }
            catch { }
            if (!ok)
            {
                // Windows 10: no system backdrop — fall back to an opaque surface
                var bg = new LinearGradientBrush();
                bg.StartPoint = new Point(0, 0); bg.EndPoint = new Point(1, 1);
                bg.GradientStops.Add(new GradientStop(Theme.C("#26242F"), 0));
                bg.GradientStops.Add(new GradientStop(Theme.C("#1B1A23"), 1));
                bg.Freeze();
                _root.Background = bg;
            }
        }

        // -------------------------------------------------------------- UI build
        private static TextBlock Glyph(string g, double size, Brush b)
        {
            return new TextBlock
            {
                Text = g,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = size,
                Foreground = b,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void BuildUi()
        {
            var shadowHost = new Grid();

            // translucent surface — the DWM acrylic backdrop shows through;
            // ApplyBackdrop() swaps in an opaque background on Windows 10
            _root = new Border
            {
                Background = Theme.B("#B31C1B24"),
                RenderTransform = (_rootSlide = new TranslateTransform())
            };

            var dock = new DockPanel { LastChildFill = true };
            _root.Child = dock;
            shadowHost.Children.Add(_root);
            Content = shadowHost;

            // ---- header
            var header = new Grid { Margin = new Thickness(22, 18, 22, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            var logo = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(10),
                Background = Theme.B("#33FFFFFF"), VerticalAlignment = VerticalAlignment.Center
            };
            var logoGlyph = Glyph("", 17, Brushes.White);
            logoGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            logo.Child = logoGlyph;
            brand.Children.Add(logo);
            var word = new TextBlock
            {
                Text = "Pastel",
                FontSize = 21, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.TextPrimary
            };
            brand.Children.Add(word);
            header.Children.Add(brand);

            // ---- filter pills
            var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pills, 2);
            header.Children.Add(pills);
            AddPill(pills, "all", "", "All");
            AddPill(pills, "pinned", "", "Pinned");
            AddPill(pills, "text", "", "Text");
            AddPill(pills, "link", "", "Links");
            AddPill(pills, "image", "", "Images");

            // ---- search
            var searchOuter = new Border
            {
                Margin = new Thickness(22, 16, 22, 4),
                CornerRadius = new CornerRadius(12),
                Background = Theme.B("#14FFFFFF"),
                BorderBrush = Theme.B("#22FFFFFF"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 0, 14, 0),
                Height = 44
            };
            DockPanel.SetDock(searchOuter, Dock.Top);
            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var sIcon = Glyph("", 15, Theme.TextDim);
            sIcon.Margin = new Thickness(0, 0, 10, 0);
            searchGrid.Children.Add(sIcon);
            var searchHost = new Grid();
            Grid.SetColumn(searchHost, 1);
            _search = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Theme.TextPrimary,
                CaretBrush = Theme.AccentBrush,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center
            };
            _search.TextChanged += delegate
            {
                _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                _selected = 0;
                RefreshList();
            };
            _searchHint = new TextBlock
            {
                Text = "Search your clipboard…",
                Foreground = Theme.TextDim,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            searchHost.Children.Add(_search);
            searchHost.Children.Add(_searchHint);
            searchGrid.Children.Add(searchHost);
            searchOuter.Child = searchGrid;
            dock.Children.Add(searchOuter);

            // ---- footer
            var footer = new Grid { Margin = new Thickness(22, 8, 22, 14) };
            DockPanel.SetDock(footer, Dock.Bottom);
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hints = new TextBlock
            {
                Text = "↑↓←→ navigate    Enter paste    Ctrl+1–9 quick paste    Ctrl+P pin    Del delete    Esc hide",
                Foreground = Theme.TextDim,
                FontSize = 11.5
            };
            footer.Children.Add(hints);
            _countText = new TextBlock { Foreground = Theme.TextDim, FontSize = 11.5 };
            Grid.SetColumn(_countText, 1);
            footer.Children.Add(_countText);
            dock.Children.Add(footer);

            var sep = new Border
            {
                Height = 1, Background = Theme.B("#1AFFFFFF"),
                Margin = new Thickness(22, 0, 22, 0)
            };
            DockPanel.SetDock(sep, Dock.Bottom);
            dock.Children.Add(sep);

            // ---- cards
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(14, 8, 14, 8),
                Focusable = false,
                PanningMode = PanningMode.VerticalOnly
            };
            var host = new Grid();
            _wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            host.Children.Add(_wrap);
            _emptyState = BuildEmptyState();
            host.Children.Add(_emptyState);
            _scroll.Content = host;
            dock.Children.Add(_scroll);

            // context menu popup
            _menu = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = false,
                Placement = PlacementMode.MousePoint,
                PopupAnimation = PopupAnimation.Fade
            };
        }

        private Grid BuildEmptyState()
        {
            var g = new Grid { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 60) };
            var circle = new Border
            {
                Width = 84, Height = 84, CornerRadius = new CornerRadius(18),
                Background = Theme.B("#12FFFFFF"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var gl = Glyph("", 34, Theme.B("#338A8AF2"));
            gl.HorizontalAlignment = HorizontalAlignment.Center;
            circle.Child = gl;
            sp.Children.Add(circle);
            sp.Children.Add(new TextBlock
            {
                Text = "Nothing here yet",
                Foreground = Theme.TextPrimary,
                FontSize = 18, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 6)
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Copy some text, a link or an image and it will appear here.",
                Foreground = Theme.TextSecondary,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            g.Children.Add(sp);
            return g;
        }

        private void AddPill(Panel parent, string key, string icon, string label)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(13, 7, 13, 7),
                Margin = new Thickness(6, 0, 0, 0),
                Background = Theme.B("#12FFFFFF"),
                Cursor = Cursors.Hand
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Glyph(icon, 12, Theme.TextSecondary);
            ic.Margin = new Thickness(0, 0, 7, 0);
            var tb = new TextBlock
            {
                Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                Foreground = Theme.TextSecondary, VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(ic); sp.Children.Add(tb);
            b.Child = sp;
            b.MouseLeftButtonUp += delegate { _filter = key; _selected = 0; UpdatePills(); RefreshList(); };
            parent.Children.Add(b);
            _pills[key] = b;
            _pillLabels[key] = tb;
        }

        private void UpdatePills()
        {
            foreach (var kv in _pills)
            {
                bool on = kv.Key == _filter;
                kv.Value.Background = on ? Theme.B("#26FFFFFF") : Theme.B("#12FFFFFF");
                var sp = (StackPanel)kv.Value.Child;
                ((TextBlock)sp.Children[0]).Foreground = on ? Brushes.White : Theme.TextSecondary;
                ((TextBlock)sp.Children[1]).Foreground = on ? Brushes.White : Theme.TextSecondary;
            }
        }

        // -------------------------------------------------------------- cards
        private void RefreshList()
        {
            _wrap.Children.Clear();
            _cards.Clear();
            _timeTexts.Clear();

            var q = _search.Text.Trim();
            IEnumerable<ClipItem> src = _store.Items;
            if (_filter == "pinned") src = src.Where(i => i.Pinned);
            else if (_filter == "text") src = src.Where(i => i.Type == "text" || i.Type == "code" || i.Type == "color");
            else if (_filter == "link") src = src.Where(i => i.Type == "link");
            else if (_filter == "image") src = src.Where(i => i.Type == "image");
            if (q.Length > 0)
                src = src.Where(i =>
                    (i.Text != null && i.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (i.Source != null && i.Source.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));

            _visible = src.Take(120).ToList();

            for (int i = 0; i < _visible.Count; i++)
                _wrap.Children.Add(BuildCard(_visible[i], i));

            _emptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int total = _store.Items.Count;
            _countText.Text = string.Format("{0} of {1} clips", _visible.Count, total);

            if (_selected >= _visible.Count) _selected = _visible.Count - 1;
            if (_selected < 0 && _visible.Count > 0) _selected = 0;
            ApplySelection(false);
            UpdatePills();
        }

        private Border BuildCard(ClipItem item, int idx)
        {
            var card = new Border
            {
                Width = 220, Height = 172,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8),
                Background = Theme.CardBrush,
                BorderBrush = Theme.B("#1EFFFFFF"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = idx,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var scale = new ScaleTransform(1, 1);
            card.RenderTransform = scale;

            var outer = new Grid();
            card.Child = outer;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.Children.Add(grid);

            // content
            UIElement content = BuildCardContent(item);
            var contentHost = new Border { Padding = new Thickness(13, 12, 13, 4), ClipToBounds = true };
            contentHost.Child = content;
            Grid.SetRow(contentHost, 0);
            grid.Children.Add(contentHost);

            // footer
            var foot = new Grid { Margin = new Thickness(13, 0, 10, 9) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(foot, 1);
            grid.Children.Add(foot);

            var meta = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var tIcon = Glyph(TypeGlyph(item.Type), 11, Theme.TextDim);
            tIcon.Margin = new Thickness(0, 0, 6, 0);
            meta.Children.Add(tIcon);
            var metaText = new TextBlock
            {
                FontSize = 10.5,
                Foreground = Theme.TextDim,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 120
            };
            metaText.Text = MetaLine(item);
            meta.Children.Add(metaText);
            _timeTexts.Add(new KeyValuePair<TextBlock, ClipItem>(metaText, item));
            foot.Children.Add(meta);

            // hover actions
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Opacity = item.Pinned ? 1 : 0 };
            Grid.SetColumn(actions, 1);
            var pinBtn = MakeIconButton(item.Pinned ? "" : "",
                item.Pinned ? Theme.AccentBrush : Theme.TextSecondary);
            pinBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                TogglePin(item);
            };
            actions.Children.Add(pinBtn);
            var delBtn = MakeIconButton("", Theme.TextSecondary);
            delBtn.Margin = new Thickness(4, 0, 0, 0);
            delBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                DeleteItem(item);
            };
            actions.Children.Add(delBtn);
            foot.Children.Add(actions);

            // quick-paste badge
            if (idx < 9)
            {
                var badge = new Border
                {
                    Background = Theme.B("#33000000"),
                    BorderBrush = Theme.B("#22FFFFFF"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(6, 1, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 11, 9, 0)
                };
                badge.Child = new TextBlock
                {
                    Text = (idx + 1).ToString(),
                    FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = Theme.TextSecondary
                };
                outer.Children.Add(badge);
            }

            // pinned indicator ribbon
            if (item.Pinned)
            {
                var pinMark = Glyph("", 11, Theme.AccentBrush);
                pinMark.HorizontalAlignment = HorizontalAlignment.Left;
                pinMark.VerticalAlignment = VerticalAlignment.Top;
                pinMark.Margin = new Thickness(12, 12, 0, 0);
                outer.Children.Add(pinMark);
            }

            // interactions
            card.MouseEnter += delegate
            {
                _selected = (int)card.Tag;
                ApplySelection(false);
                Animate(scale, 1.03, 110);
                if (!item.Pinned) actions.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
            };
            card.MouseLeave += delegate
            {
                Animate(scale, 1.0, 110);
                if (!item.Pinned) actions.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
            };
            card.MouseLeftButtonUp += delegate { PasteItem(item, false); };
            card.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ShowMenu(item);
            };

            _cards.Add(card);
            return card;
        }

        private static void Animate(ScaleTransform t, double to, int ms)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() };
            t.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            t.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private UIElement BuildCardContent(ClipItem item)
        {
            if (item.Type == "image" && !string.IsNullOrEmpty(item.ImageFile))
            {
                var path = Path.Combine(_store.ImgDir, item.ImageFile);
                var imgBorder = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 2, 0, 2) };
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(path);
                    bi.DecodePixelWidth = 400;
                    bi.EndInit();
                    bi.Freeze();
                    imgBorder.Background = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                }
                catch { imgBorder.Background = Theme.B("#22FFFFFF"); }
                return imgBorder;
            }

            if (item.Type == "color")
            {
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var swatch = new Border
                {
                    Width = 54, Height = 54, CornerRadius = new CornerRadius(12),
                    BorderBrush = Theme.B("#44FFFFFF"), BorderThickness = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                try { swatch.Background = Theme.B(NormalizeHex(item.Text)); } catch { swatch.Background = Brushes.Gray; }
                sp.Children.Add(swatch);
                sp.Children.Add(new TextBlock
                {
                    Text = item.Text.Trim(),
                    FontFamily = Theme.MonoFont,
                    FontSize = 14, FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.TextPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                });
                return sp;
            }

            if (item.Type == "link")
            {
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                string domain = item.Text;
                try { domain = new Uri(item.Text.Trim()).Host; } catch { }
                sp.Children.Add(new TextBlock
                {
                    Text = domain,
                    FontSize = 14.5, FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.TextPrimary,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 4, 0, 4)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = item.Text.Trim(),
                    FontSize = 11.5,
                    Foreground = Theme.TextSecondary,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 68
                });
                return sp;
            }

            if (item.Type == "files")
            {
                var lines = (item.Text ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                sp.Children.Add(new TextBlock
                {
                    Text = lines.Length == 1 ? "1 file" : lines.Length + " files",
                    FontSize = 14, FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.TextPrimary,
                    Margin = new Thickness(0, 4, 0, 5)
                });
                foreach (var l in lines.Take(4))
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = Path.GetFileName(l.Trim()),
                        FontSize = 11.5,
                        Foreground = Theme.TextSecondary,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
                return sp;
            }

            // text / code
            var tb = new TextBlock
            {
                Text = Preview(item.Text),
                TextWrapping = TextWrapping.Wrap,
                FontSize = item.Type == "code" ? 11.5 : 12.5,
                Foreground = Theme.TextPrimary,
                LineHeight = item.Type == "code" ? 16 : 18,
                VerticalAlignment = VerticalAlignment.Top
            };
            if (item.Type == "code") tb.FontFamily = Theme.MonoFont;
            return tb;
        }

        private Border MakeIconButton(string glyph, Brush color)
        {
            var b = new Border
            {
                Width = 27, Height = 27,
                CornerRadius = new CornerRadius(8),
                Background = Theme.B("#2E000000"),
                Cursor = Cursors.Hand
            };
            var g = Glyph(glyph, 11.5, color);
            g.HorizontalAlignment = HorizontalAlignment.Center;
            b.Child = g;
            b.MouseEnter += delegate { b.Background = Theme.B("#55000000"); };
            b.MouseLeave += delegate { b.Background = Theme.B("#2E000000"); };
            return b;
        }

        private static string TypeGlyph(string type)
        {
            switch (type)
            {
                case "link": return "";
                case "image": return "";
                case "files": return "";
                case "code": return "";
                case "color": return "";
                default: return "";
            }
        }

        private string MetaLine(ClipItem item)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(item.Source)) parts.Add(item.Source);
            parts.Add(TimeAgo(item.Time));
            if (item.Count > 1) parts.Add("×" + item.Count);
            if (!string.IsNullOrEmpty(item.Rtf) || !string.IsNullOrEmpty(item.Html)) parts.Add("rich");
            return string.Join("  ·  ", parts);
        }

        private void UpdateTimeTexts()
        {
            foreach (var kv in _timeTexts)
                kv.Key.Text = MetaLine(kv.Value);
        }

        private static string TimeAgo(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 50) return "just now";
            if (d.TotalMinutes < 60) return string.Format("{0}m ago", (int)d.TotalMinutes);
            if (d.TotalHours < 24 && t.Date == DateTime.Today) return string.Format("{0}h ago", (int)d.TotalHours);
            if (t.Date == DateTime.Today.AddDays(-1)) return "yesterday";
            return t.ToString("MMM d", CultureInfo.InvariantCulture);
        }

        private static string Preview(string s)
        {
            if (s == null) return "";
            s = s.Trim().Replace("\r", "");
            if (s.Length > 380) s = s.Substring(0, 380) + "…";
            return s;
        }

        private static string NormalizeHex(string s)
        {
            s = s.Trim();
            if (s.Length == 4) // #abc -> #aabbcc
                return "#" + string.Concat(s.Substring(1).Select(c => new string(c, 2)));
            return s;
        }

        // -------------------------------------------------------------- selection
        private void ApplySelection(bool scrollTo)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (i == _selected)
                {
                    card.BorderBrush = Theme.AccentBrush;
                    card.BorderThickness = new Thickness(2);
                    card.Effect = new DropShadowEffect
                    {
                        BlurRadius = 14, ShadowDepth = 0, Opacity = 0.30,
                        Color = Theme.C(Theme.AccentHex)
                    };
                    if (scrollTo) card.BringIntoView();
                }
                else
                {
                    card.BorderBrush = Theme.B("#1EFFFFFF");
                    card.BorderThickness = new Thickness(1);
                    card.Effect = null;
                }
            }
        }

        private int Columns()
        {
            double w = _wrap.ActualWidth;
            if (w < 10) w = Width - 76;
            int c = (int)(w / 236);
            return Math.Max(1, c);
        }

        // -------------------------------------------------------------- key handling
        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { HideWindow(true); e.Handled = true; return; }

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (ctrl && e.Key >= Key.D1 && e.Key <= Key.D9)
            {
                int n = e.Key - Key.D1;
                if (n < _visible.Count) PasteItem(_visible[n], false);
                e.Handled = true; return;
            }
            if (ctrl && e.Key == Key.P)
            {
                if (_selected >= 0 && _selected < _visible.Count) TogglePin(_visible[_selected]);
                e.Handled = true; return;
            }
            if (e.Key == Key.Enter)
            {
                if (_selected >= 0 && _selected < _visible.Count)
                    PasteItem(_visible[_selected], (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                e.Handled = true; return;
            }
            if (e.Key == Key.Delete)
            {
                bool searchEditing = _search.IsKeyboardFocused && _search.Text.Length > 0;
                if (!searchEditing && _selected >= 0 && _selected < _visible.Count)
                {
                    DeleteItem(_visible[_selected]);
                    e.Handled = true;
                }
                return;
            }

            int cols = Columns();
            int next = _selected;
            if (e.Key == Key.Down) next = _selected + cols;
            else if (e.Key == Key.Up) next = _selected - cols;
            else if (e.Key == Key.Right && (!_search.IsKeyboardFocused || _search.Text.Length == 0)) next = _selected + 1;
            else if (e.Key == Key.Left && (!_search.IsKeyboardFocused || _search.Text.Length == 0)) next = _selected - 1;
            else return;

            if (_visible.Count == 0) return;
            if (next < 0) next = 0;
            if (next > _visible.Count - 1) next = _visible.Count - 1;
            _selected = next;
            ApplySelection(true);
            e.Handled = true;
        }

        // -------------------------------------------------------------- actions
        private void TogglePin(ClipItem item)
        {
            item.Pinned = !item.Pinned;
            SaveSoon();
            RefreshList();
        }

        private void DeleteItem(ClipItem item)
        {
            _store.Items.Remove(item);
            if (!string.IsNullOrEmpty(item.ImageFile))
                try { File.Delete(Path.Combine(_store.ImgDir, item.ImageFile)); } catch { }
            SaveSoon();
            RefreshList();
        }

        private void SaveSoon()
        {
            _saveTimer.Stop(); _saveTimer.Start();
        }

        private void ShowMenu(ClipItem item)
        {
            var panel = new StackPanel();
            var host = new Border
            {
                Background = Theme.B("#2E2D3A"),
                BorderBrush = Theme.B("#30FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(5),
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.5 },
                Margin = new Thickness(0, 0, 12, 12)
            };
            host.Child = panel;

            Action<string, string, Action> add = delegate(string glyph, string label, Action action)
            {
                var row = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(10, 7, 22, 7),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = Glyph(glyph, 12, Theme.TextSecondary);
                ic.Margin = new Thickness(0, 0, 10, 0);
                sp.Children.Add(ic);
                sp.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 12.5, Foreground = Theme.TextPrimary,
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Child = sp;
                row.MouseEnter += delegate { row.Background = Theme.B("#18FFFFFF"); };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
                row.MouseLeftButtonUp += delegate { _menu.IsOpen = false; action(); };
                panel.Children.Add(row);
            };

            add("", "Paste", delegate { PasteItem(item, false); });
            if (item.Type != "image" && item.Type != "files")
                add("", "Paste as plain text", delegate { PasteItem(item, true); });
            add("", "Copy only", delegate { SetClipboard(item, false); HideWindow(true); });
            add(item.Pinned ? "" : "", item.Pinned ? "Unpin" : "Pin", delegate { TogglePin(item); });
            add("", "Delete", delegate { DeleteItem(item); });

            _menu.Child = host;
            _menu.IsOpen = true;
        }

        // -------------------------------------------------------------- clipboard capture
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE)
            {
                OnClipboardChanged();
                handled = true;
            }
            else if (msg == Native.WM_HOTKEY && wParam.ToInt32() == Native.HOTKEY_ID)
            {
                if (IsVisible) HideWindow(true); else ShowWindow();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnClipboardChanged()
        {
            if (_paused) return;
            if (DateTime.UtcNow < _ignoreClipUntil) return;

            // small delay lets the source app finish writing all formats
            Dispatcher.BeginInvoke(new Action(delegate { CaptureClipboard(); }),
                DispatcherPriority.Background);
        }

        private void CaptureClipboard()
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        var img = Clipboard.GetImage();
                        if (img != null) AddImage(img);
                        return;
                    }
                    if (Clipboard.ContainsFileDropList())
                    {
                        var files = Clipboard.GetFileDropList();
                        var paths = files.Cast<string>().Where(p => !string.IsNullOrEmpty(p)).ToList();
                        if (paths.Count > 0) AddText(string.Join("\n", paths), "files", null, null);
                        return;
                    }
                    if (Clipboard.ContainsText())
                    {
                        var text = Clipboard.GetText();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            string rtf = null, html = null;
                            try { rtf = Clipboard.GetData(DataFormats.Rtf) as string; } catch { }
                            try { html = Clipboard.GetData(DataFormats.Html) as string; } catch { }
                            if (rtf != null && rtf.Length > 150000) rtf = null;
                            if (html != null && html.Length > 150000) html = null;
                            AddText(text, null, rtf, html);
                        }
                        return;
                    }
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(60);
                }
            }
        }

        private static readonly Regex LinkRx = new Regex(@"^https?://\S+$", RegexOptions.Compiled);
        private static readonly Regex ColorRx = new Regex(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.Compiled);

        private void AddText(string text, string forcedType, string rtf, string html)
        {
            if (text.Length > 200000) text = text.Substring(0, 200000);
            string type = forcedType;
            if (type == null)
            {
                var t = text.Trim();
                if (ColorRx.IsMatch(t)) type = "color";
                else if (LinkRx.IsMatch(t) && !t.Contains("\n")) type = "link";
                else if (LooksLikeCode(text)) type = "code";
                else type = "text";
            }

            var existing = _store.Items.FirstOrDefault(i => i.Type != "image" && i.Text == text);
            if (existing != null)
            {
                existing.Count++;
                existing.Ts = DateTime.UtcNow.Ticks;
                existing.Source = ForegroundApp() ?? existing.Source;
                if (rtf != null) existing.Rtf = rtf;
                if (html != null) existing.Html = html;
                _store.Items.Remove(existing);
                _store.Items.Insert(0, existing);
            }
            else
            {
                _store.Items.Insert(0, new ClipItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = type,
                    Text = text,
                    Rtf = rtf,
                    Html = html,
                    Source = ForegroundApp(),
                    Ts = DateTime.UtcNow.Ticks,
                    Count = 1,
                    Ci = (_colorCounter++) % 6
                });
            }
            SaveSoon();
            if (IsVisible) RefreshList();
        }

        private void AddImage(BitmapSource img)
        {
            byte[] png;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                png = ms.ToArray();
            }
            string hash;
            using (var sha = SHA1.Create())
                hash = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "");

            var existing = _store.Items.FirstOrDefault(i => i.Type == "image" && i.Hash == hash);
            if (existing != null)
            {
                existing.Count++;
                existing.Ts = DateTime.UtcNow.Ticks;
                _store.Items.Remove(existing);
                _store.Items.Insert(0, existing);
            }
            else
            {
                var name = Guid.NewGuid().ToString("N") + ".png";
                File.WriteAllBytes(Path.Combine(_store.ImgDir, name), png);
                _store.Items.Insert(0, new ClipItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = "image",
                    Text = string.Format("Image {0}×{1}", img.PixelWidth, img.PixelHeight),
                    ImageFile = name,
                    Hash = hash,
                    Source = ForegroundApp(),
                    Ts = DateTime.UtcNow.Ticks,
                    Count = 1,
                    W = img.PixelWidth, H = img.PixelHeight,
                    Ci = (_colorCounter++) % 6
                });
            }
            SaveSoon();
            if (IsVisible) RefreshList();
        }

        private static bool LooksLikeCode(string s)
        {
            if (!s.Contains("\n")) return false;
            int hits = 0;
            if (s.Contains("{") && s.Contains("}")) hits++;
            if (s.Contains(";")) hits++;
            if (s.Contains("=>") || s.Contains("->")) hits++;
            if (Regex.IsMatch(s, @"^\s*(def |class |function |import |from |var |let |const |public |private |#include|using |if \(|for \()", RegexOptions.Multiline)) hits += 2;
            if (s.Contains("</") || s.Contains("/>")) hits++;
            return hits >= 2;
        }

        private static readonly Dictionary<string, string> FriendlyApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome", "Chrome" }, { "msedge", "Edge" }, { "firefox", "Firefox" },
            { "Code", "VS Code" }, { "devenv", "Visual Studio" }, { "explorer", "Explorer" },
            { "WINWORD", "Word" }, { "EXCEL", "Excel" }, { "POWERPNT", "PowerPoint" },
            { "OUTLOOK", "Outlook" }, { "olk", "Outlook" }, { "ms-teams", "Teams" },
            { "slack", "Slack" }, { "notepad", "Notepad" },
            { "powershell", "PowerShell" }, { "pwsh", "PowerShell" },
            { "WindowsTerminal", "Terminal" }, { "cmd", "Cmd" },
            { "Discord", "Discord" }, { "Spotify", "Spotify" }, { "Figma", "Figma" },
        };

        private string ForegroundApp()
        {
            try
            {
                var h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero || h == _hwnd) return null;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return null;
                var name = Process.GetProcessById((int)pid).ProcessName;
                string friendly;
                if (FriendlyApps.TryGetValue(name, out friendly)) return friendly;
                if (name.Length > 1) return char.ToUpper(name[0]) + name.Substring(1);
                return name;
            }
            catch { return null; }
        }

        // -------------------------------------------------------------- paste
        private void SetClipboard(ClipItem item, bool plain)
        {
            _ignoreClipUntil = DateTime.UtcNow.AddSeconds(2);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (item.Type == "image" && !string.IsNullOrEmpty(item.ImageFile))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.UriSource = new Uri(Path.Combine(_store.ImgDir, item.ImageFile));
                        bi.EndInit();
                        bi.Freeze();
                        Clipboard.SetImage(bi);
                    }
                    else if (item.Type == "files")
                    {
                        var sc = new StringCollection();
                        foreach (var l in (item.Text ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            if (File.Exists(l.Trim()) || Directory.Exists(l.Trim())) sc.Add(l.Trim());
                        if (sc.Count > 0) Clipboard.SetFileDropList(sc);
                        else Clipboard.SetText(item.Text ?? "");
                    }
                    else
                    {
                        var data = new DataObject();
                        data.SetData(DataFormats.UnicodeText, item.Text ?? "");
                        if (!plain && !string.IsNullOrEmpty(item.Rtf))
                            data.SetData(DataFormats.Rtf, item.Rtf);
                        if (!plain && !string.IsNullOrEmpty(item.Html))
                            data.SetData(DataFormats.Html, item.Html);
                        Clipboard.SetDataObject(data, true);
                    }
                    return;
                }
                catch (Exception) { Thread.Sleep(70); }
            }
        }

        private void PasteItem(ClipItem item, bool plain)
        {
            item.Count++;
            item.Ts = DateTime.UtcNow.Ticks;
            _store.Items.Remove(item);
            _store.Items.Insert(0, item);
            SaveSoon();

            SetClipboard(item, plain);
            var target = _lastForeground;
            HideWindow(false);

            ThreadPool.QueueUserWorkItem(delegate
            {
                if (target != IntPtr.Zero) Native.SetForegroundWindow(target);
                Thread.Sleep(90);
                // wait for user's physical modifiers to come up
                for (int i = 0; i < 30; i++)
                {
                    bool down = (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0
                             || (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0
                             || (Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0;
                    if (!down) break;
                    Thread.Sleep(30);
                }
                Native.keybd_event(Native.VK_CONTROL, 0, 0, UIntPtr.Zero);
                Native.keybd_event(Native.VK_V, 0, 0, UIntPtr.Zero);
                Native.keybd_event(Native.VK_V, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Native.keybd_event(Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            });
        }

        // -------------------------------------------------------------- show/hide
        public void ShowWindow()
        {
            _lastForeground = Native.GetForegroundWindow();
            PositionOnCursorScreen();
            _search.Text = "";
            _filter = "all";
            _selected = 0;
            RefreshList();
            UpdateTimeTexts();

            _root.Opacity = 0;
            _rootSlide.Y = 22;
            Show();
            Activate();
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            _rootSlide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Dispatcher.BeginInvoke(new Action(delegate { _search.Focus(); }), DispatcherPriority.Input);
        }

        public void HideWindow(bool animate)
        {
            if (_menu != null) _menu.IsOpen = false;
            if (!animate) { Hide(); return; }
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
            a.Completed += delegate { Hide(); _root.BeginAnimation(OpacityProperty, null); };
            _root.BeginAnimation(OpacityProperty, a);
        }

        private void PositionOnCursorScreen()
        {
            try
            {
                Native.POINT p;
                Native.GetCursorPos(out p);
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
                var wa = screen.WorkingArea;

                var src = HwndSource.FromHwnd(_hwnd);
                double scale = 1.0;
                if (src != null && src.CompositionTarget != null)
                    scale = src.CompositionTarget.TransformFromDevice.M11;

                Left = (wa.Left + (wa.Width - Width / scale) / 2.0) * scale;
                Top = (wa.Top + (wa.Height - Height / scale) / 2.0) * scale;
            }
            catch
            {
                Left = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
                Top = (SystemParameters.WorkArea.Height - Height) / 2 + SystemParameters.WorkArea.Top;
            }
        }

        // -------------------------------------------------------------- tray
        private void SetupTray()
        {
            _tray = new System.Windows.Forms.NotifyIcon();
            try
            {
                _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch { _tray.Icon = System.Drawing.SystemIcons.Application; }
            _tray.Text = "Pastel — Ctrl+Alt+V";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(ShowWindow)); };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = menu.Items.Add("Open Pastel\tCtrl+Alt+V");
            open.Click += delegate { Dispatcher.Invoke(new Action(ShowWindow)); };
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var pause = new System.Windows.Forms.ToolStripMenuItem("Pause capture") { CheckOnClick = true };
            pause.CheckedChanged += delegate { _paused = pause.Checked; };
            menu.Items.Add(pause);

            var autostart = new System.Windows.Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
            autostart.Checked = IsAutoStart();
            autostart.CheckedChanged += delegate { SetAutoStart(autostart.Checked); };
            menu.Items.Add(autostart);

            var clear = menu.Items.Add("Clear history (keeps pinned)");
            clear.Click += delegate
            {
                Dispatcher.Invoke(new Action(delegate
                {
                    if (MessageBox.Show("Remove all unpinned clips?", "Pastel",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    foreach (var it in _store.Items.Where(i => !i.Pinned && !string.IsNullOrEmpty(i.ImageFile)))
                        try { File.Delete(Path.Combine(_store.ImgDir, it.ImageFile)); } catch { }
                    _store.Items = _store.Items.Where(i => i.Pinned).ToList();
                    _store.Save();
                    if (IsVisible) RefreshList();
                }));
            };
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var quit = menu.Items.Add("Quit Pastel");
            quit.Click += delegate
            {
                Dispatcher.Invoke(new Action(delegate
                {
                    _store.Save();
                    _tray.Visible = false;
                    _tray.Dispose();
                    Native.UnregisterHotKey(_hwnd, Native.HOTKEY_ID);
                    Native.RemoveClipboardFormatListener(_hwnd);
                    Application.Current.Shutdown();
                }));
            };
            _tray.ContextMenuStrip = menu;

            // first-run balloon
            var marker = Path.Combine(_store.Dir, ".welcomed");
            if (!File.Exists(marker))
            {
                try { File.WriteAllText(marker, "1"); } catch { }
                _tray.BalloonTipTitle = "Pastel is running";
                _tray.BalloonTipText = "Press Ctrl+Alt+V to open your clipboard history.";
                _tray.ShowBalloonTip(4000);
            }
        }

        private static bool IsAutoStart()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return k != null && k.GetValue("Pastel") != null;
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (k == null) throw new InvalidOperationException("Windows startup settings are unavailable.");
                    if (on) k.SetValue("Pastel", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --startup");
                    else k.DeleteValue("Pastel", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Pastel couldn't update your Windows startup setting.\n\n" + ex.Message,
                    "Pastel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // -------------------------------------------------------------- demo
        private void SeedDemo()
        {
            if (_store.Items.Count > 0) return;
            int n = 0;
            Action<string, string, string, int> add = delegate(string type, string text, string source, int minsAgo)
            {
                _store.Items.Add(new ClipItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = type, Text = text, Source = source,
                    Ts = DateTime.UtcNow.AddMinutes(-minsAgo).Ticks,
                    Count = 1, Ci = (n++) % 6
                });
            };
            add("text", "The details are not the details. They make the design. — Charles Eames", "Chrome", 2);
            add("link", "https://github.com/anthropics/claude-code", "Edge", 6);
            add("color", "#F5576C", "Figma", 11);
            add("code", "def fib(n):\n    a, b = 0, 1\n    for _ in range(n):\n        a, b = b, a + b\n    return a", "VS Code", 18);
            add("text", "Meeting moved to 3:30pm — bring the Q3 numbers and the launch checklist.", "Slack", 25);
            add("link", "https://news.ycombinator.com/item?id=39917234", "Firefox", 41);
            add("code", "SELECT id, email FROM users\nWHERE created_at > NOW() - INTERVAL '7 days'\nORDER BY created_at DESC;", "Terminal", 55);
            add("color", "#4FACFE", "Figma", 70);
            add("text", "justynroberts@gmail.com", "Outlook", 90);
            add("text", "Pastel — every copy, one keystroke away. Press Ctrl+Alt+V anywhere.", "Notepad", 130);
            add("files", "C:\\Users\\justy\\Documents\\Q3-report.xlsx\nC:\\Users\\justy\\Documents\\launch-deck.pptx", "Explorer", 160);
            add("text", "★ Pinned notes stay forever — pin anything you paste often.", "Notepad", 300);
            _store.Items[_store.Items.Count - 1].Pinned = true;
            _store.Items = _store.Items.OrderByDescending(i => i.Ts).ToList();
        }
    }

    // -------------------------------------------------------------- splash
    public sealed class SplashWindow : Window
    {
        public SplashWindow()
        {
            Width = 520; Height = 310;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;

            var shell = new Border
            {
                CornerRadius = new CornerRadius(24),
                Background = Theme.B("#F21B1B20"),
                BorderBrush = Theme.B("#35FFFFFF"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(42),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 34, ShadowDepth = 10, Opacity = .4, Color = Colors.Black
                }
            };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("Pastel.Logo.png"))
                {
                    if (stream != null)
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        stack.Children.Add(new System.Windows.Controls.Image
                        {
                            Source = bitmap,
                            Width = 104,
                            Height = 104,
                            Stretch = Stretch.Uniform,
                            Margin = new Thickness(0, 0, 0, 12)
                        });
                    }
                }
            }
            catch { }
            stack.Children.Add(new TextBlock
            {
                Text = "Pastel",
                FontFamily = Theme.UiFont,
                FontSize = 44,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = "the missing Windows clipboard manager",
                FontFamily = Theme.UiFont,
                FontSize = 16,
                Foreground = Theme.B("#B8FFFFFF"),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            shell.Child = stack;
            Content = shell;
            Opacity = 0;
        }

        public void PlayAndClose()
        {
            Show();
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
            BeginAnimation(Window.OpacityProperty, fadeIn);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1150) };
            timer.Tick += delegate
            {
                timer.Stop();
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
                fadeOut.Completed += delegate { Close(); };
                BeginAnimation(Window.OpacityProperty, fadeOut);
            };
            timer.Start();
        }
    }

    // ------------------------------------------------------------------ entry
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            string dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pastel");
            bool show = false, demo = false, keepVisible = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--show") show = true;
                else if (args[i] == "--demo") demo = true;
                else if (args[i] == "--keepvisible") keepVisible = true;
                else if (args[i] == "--startup") { /* explicit quiet sign-in launch */ }
                else if (args[i] == "--datadir" && i + 1 < args.Length) dataDir = args[++i];
            }

            bool created;
            var mutex = new Mutex(true, "PastelClipboardManager", out created);
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "PastelShowSignal");
            if (!created)
            {
                // already running: ask the live instance to show itself
                showSignal.Set();
                return;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var store = new Store(dataDir);
            var win = new MainWindow(store, demo) { KeepVisible = keepVisible };

            // A quick brand moment on app/sign-in launch. Hotkey reveals never recreate it.
            if (!demo && !keepVisible)
            {
                var splash = new SplashWindow();
                splash.PlayAndClose();
            }

            var waiter = new Thread(delegate()
            {
                while (true)
                {
                    showSignal.WaitOne();
                    win.Dispatcher.BeginInvoke(new Action(win.ShowWindow));
                }
            }) { IsBackground = true };
            waiter.Start();

            if (show) win.Dispatcher.BeginInvoke(new Action(win.ShowWindow), DispatcherPriority.Loaded);

            app.Run();
            GC.KeepAlive(mutex);
        }
    }
}
