using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Stickers
{
    public static class Theme
    {
        // Main (default) palette plus brain-dump style pastels.
        public static readonly string[] Defaults = { "#C3DFE0", "#BCD979", "#EBE645", "#F0954F" };
        public static readonly string[] Pastels =
        {
            "#EBD5F2", "#DCDAF6", "#D6E2F6", "#D3ECEB", "#CFECDF",
            "#E7E8C2", "#F8DEC4", "#F6CABF", "#F4F1EA", "#3A3835"
        };

        public static FontFamily Font = new FontFamily("Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public const string IcAdd = "", IcClose = "", IcPin = "", IcPinned = "",
            IcColor = "", IcDelete = "", IcDown = "", IcUp = "",
            IcChecklist = "", IcNote = "", IcEye = "", IcHide = "", IcMore = "";

        // ---------- font ----------

        public static void LoadFonts(string dataDir)
        {
            try
            {
                if (Fonts.SystemFontFamilies.Any(f => f.Source == "TT Fors"))
                {
                    Font = new FontFamily("TT Fors");
                    return;
                }
                string dir = System.IO.Path.Combine(dataDir, "fonts");
                Directory.CreateDirectory(dir);
                var asm = Assembly.GetExecutingAssembly();
                foreach (string name in asm.GetManifestResourceNames().Where(n => n.StartsWith("Stickers.Fonts.")))
                {
                    string target = System.IO.Path.Combine(dir, name.Substring("Stickers.Fonts.".Length));
                    using (var s = asm.GetManifestResourceStream(name))
                    {
                        if (File.Exists(target) && new FileInfo(target).Length == s.Length) continue;
                        using (var f = File.Create(target)) s.CopyTo(f);
                    }
                }
                Font = new FontFamily(new Uri(dir.TrimEnd('\\') + "\\"), "./#Onest, Segoe UI");
            }
            catch (Exception ex)
            {
                Store.Log("Font failed to load: " + ex.Message);
            }
        }

        // ---------- colors ----------

        public static Color Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return (Color)ColorConverter.ConvertFromString(Defaults[0]); }
        }

        public static string Hex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        static double Lin(byte v)
        {
            double c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public static bool IsDark(Color c)
        {
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B) < 0.3;
        }

        public static Color Ink(Color bg)
        {
            return IsDark(bg) ? Color.FromRgb(0xF6, 0xF4, 0xEE) : Color.FromRgb(0x1E, 0x1D, 0x1B);
        }

        public static Color Alpha(Color c, byte a)
        {
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        public static string NextDefault(string current)
        {
            int i = Array.FindIndex(Defaults, d => string.Equals(d, current, StringComparison.OrdinalIgnoreCase));
            return Defaults[(i + 1) % Defaults.Length];
        }

        // ---------- styles ----------

        const string StylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='5'/>
    <Setter Property='MinWidth' Value='5'/>
    <Setter Property='Opacity' Value='0.3'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border CornerRadius='2.5' Background='{TemplateBinding Foreground}'/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Bare' TargetType='TextBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='SelectionOpacity' Value='0.22'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <ScrollViewer x:Name='PART_ContentHost' Focusable='False' Background='{TemplateBinding Background}'
                        HorizontalScrollBarVisibility='Disabled'
                        VerticalScrollBarVisibility='{TemplateBinding VerticalScrollBarVisibility}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='IconButton' TargetType='Button'>
    <Setter Property='Width' Value='26'/>
    <Setter Property='Height' Value='26'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid Background='Transparent'>
            <Border x:Name='hover' CornerRadius='7' Background='{TemplateBinding Foreground}' Opacity='0'/>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='hover' Property='Opacity' Value='0.1'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='hover' Property='Opacity' Value='0.18'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Grip' TargetType='Thumb'>
    <Setter Property='Width' Value='18'/>
    <Setter Property='Height' Value='18'/>
    <Setter Property='Cursor' Value='SizeNWSE'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Grid Background='Transparent'>
            <Path Data='M 15,7 L 7,15 M 15,11 L 11,15' Stroke='{TemplateBinding Foreground}'
                  StrokeThickness='1.4' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        public static Style BareText, IconButton, Grip;

        public static void InstallStyles(Application app)
        {
            var rd = (ResourceDictionary)XamlReader.Parse(StylesXaml);
            app.Resources.MergedDictionaries.Add(rd);
            BareText = (Style)rd["Bare"];
            IconButton = (Style)rd["IconButton"];
            Grip = (Style)rd["Grip"];

            var tip = new Style(typeof(ToolTip));
            tip.Setters.Add(new Setter(Control.FontFamilyProperty, Font));
            tip.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            app.Resources[typeof(ToolTip)] = tip;
        }

        public static TextBlock Glyph(string glyph, double size)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = Icons,
                FontSize = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Button MakeIconButton(string glyph, string tip, Brush fg, double glyphSize = 13)
        {
            var b = new Button { Style = IconButton, Foreground = fg, Content = Glyph(glyph, glyphSize), ToolTip = tip };
            ToolTipService.SetInitialShowDelay(b, 500);
            return b;
        }

        // ---------- palette popup ----------

        public static FrameworkElement BuildPalette(string current, IList<string> custom,
            Action<string> pick, Action pickCustom)
        {
            var ink = new SolidColorBrush(Color.FromRgb(0x1E, 0x1D, 0x1B));
            var muted = new SolidColorBrush(Color.FromArgb(0x88, 0x1E, 0x1D, 0x1B));
            var stack = new StackPanel { Width = 5 * 32 };

            Func<string, TextBlock> caption = text => new TextBlock
            {
                Text = text, FontFamily = Font, FontSize = 11.5, Foreground = muted, Margin = new Thickness(3, 0, 0, 4)
            };

            Func<IEnumerable<string>, WrapPanel> row = colors =>
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                foreach (string hex in colors) wrap.Children.Add(Swatch(hex, string.Equals(hex, current, StringComparison.OrdinalIgnoreCase), pick));
                return wrap;
            };

            stack.Children.Add(caption(L.T("Основные", "Main")));
            stack.Children.Add(row(Defaults));
            stack.Children.Add(caption(L.T("Пастель", "Pastel")));
            stack.Children.Add(row(Pastels));
            if (custom != null && custom.Count > 0)
            {
                stack.Children.Add(caption(L.T("Мои цвета", "My colors")));
                stack.Children.Add(row(custom));
            }

            var more = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = L.T("+  Свой цвет…", "+  Custom color…"), FontFamily = Font, FontSize = 13, Foreground = ink }
            };
            more.MouseEnter += (s, e) => more.Background = new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0));
            more.MouseLeave += (s, e) => more.Background = Brushes.Transparent;
            more.MouseLeftButtonUp += (s, e) => pickCustom();
            stack.Children.Add(more);

            return new Border
            {
                Margin = new Thickness(12),
                Padding = new Thickness(10, 10, 10, 6),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xFC, 0xF9)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.22 },
                Child = stack
            };
        }

        // ---------- "⋯" menu (same look as the palette) ----------

        public class MenuItemSpec
        {
            public string Glyph, Label;
            public Action Click;
        }

        public static MenuItemSpec Item(string glyph, string label, Action click)
        {
            return new MenuItemSpec { Glyph = glyph, Label = label, Click = click };
        }

        // A null entry is a separator.
        public static FrameworkElement BuildMenu(IEnumerable<MenuItemSpec> items)
        {
            var ink = new SolidColorBrush(Color.FromRgb(0x1E, 0x1D, 0x1B));
            var hoverBg = new SolidColorBrush(Color.FromArgb(0x12, 0, 0, 0));
            var stack = new StackPanel { MinWidth = 190 };
            foreach (var it in items)
            {
                if (it == null)
                {
                    stack.Children.Add(new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4), Background = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)) });
                    continue;
                }
                var spec = it;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = Glyph(spec.Glyph, 13);
                icon.Width = 18;
                icon.Foreground = ink;
                icon.Opacity = 0.75;
                row.Children.Add(icon);
                row.Children.Add(new TextBlock
                {
                    Text = spec.Label, FontFamily = Font, FontSize = 13.5, Foreground = ink,
                    Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
                });
                var b = new Border
                {
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 7, 14, 7),
                    Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row
                };
                b.MouseEnter += (s, e) => b.Background = hoverBg;
                b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
                b.MouseLeftButtonUp += (s, e) => spec.Click();
                stack.Children.Add(b);
            }
            return new Border
            {
                Margin = new Thickness(12),
                Padding = new Thickness(5),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xFC, 0xF9)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.22 },
                Child = stack
            };
        }

        static FrameworkElement Swatch(string hex, bool selected, Action<string> pick)
        {
            var g = new Grid { Width = 32, Height = 32, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = hex };
            var ring = new Ellipse
            {
                Width = 30, Height = 30, StrokeThickness = 2,
                Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x1D, 0x1B)),
                Opacity = selected ? 1 : 0
            };
            var dot = new Ellipse
            {
                Width = 23, Height = 23,
                Fill = new SolidColorBrush(Parse(hex)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)), StrokeThickness = 1
            };
            g.Children.Add(ring);
            g.Children.Add(dot);
            g.MouseEnter += (s, e) => { if (!selected) ring.Opacity = 0.3; };
            g.MouseLeave += (s, e) => { if (!selected) ring.Opacity = 0; };
            g.MouseLeftButtonUp += (s, e) => pick(hex);
            return g;
        }
    }

    // ---------- snapping to neighbours and screen edges ----------

    public static class Snap
    {
        public const double Gap = 10;       // gap between stickers
        const double Threshold = 14;        // snapping distance
        const double Near = 60;             // how close a neighbour must be on the other axis

        static void Try(ref double best, double delta)
        {
            if (Math.Abs(delta) < Math.Abs(best)) best = delta;
        }

        static bool Overlaps(double a1, double a2, double b1, double b2)
        {
            return a1 < b2 + Near && a2 > b1 - Near;
        }

        public static Vector Move(Rect r, IEnumerable<Rect> others, IEnumerable<Rect> areas)
        {
            double dx = double.MaxValue, dy = double.MaxValue;
            foreach (var o in others)
            {
                if (Overlaps(r.Top, r.Bottom, o.Top, o.Bottom))
                {
                    Try(ref dx, o.Right + Gap - r.Left);
                    Try(ref dx, o.Left - Gap - r.Right);
                    Try(ref dx, o.Left - r.Left);
                    Try(ref dx, o.Right - r.Right);
                }
                if (Overlaps(r.Left, r.Right, o.Left, o.Right))
                {
                    Try(ref dy, o.Bottom + Gap - r.Top);
                    Try(ref dy, o.Top - Gap - r.Bottom);
                    Try(ref dy, o.Top - r.Top);
                    Try(ref dy, o.Bottom - r.Bottom);
                }
            }
            foreach (var a in areas)
            {
                Try(ref dx, a.Left + Gap - r.Left);
                Try(ref dx, a.Right - Gap - r.Right);
                Try(ref dy, a.Top + Gap - r.Top);
                Try(ref dy, a.Bottom - Gap - r.Bottom);
            }
            return new Vector(Math.Abs(dx) <= Threshold ? dx : 0, Math.Abs(dy) <= Threshold ? dy : 0);
        }

        // Resizing only moves the right and bottom edges.
        public static Size Resize(Rect r, IEnumerable<Rect> others, IEnumerable<Rect> areas)
        {
            double dw = double.MaxValue, dh = double.MaxValue;
            foreach (var o in others)
            {
                if (Overlaps(r.Top, r.Bottom, o.Top, o.Bottom))
                {
                    Try(ref dw, o.Left - Gap - r.Right);
                    Try(ref dw, o.Right - r.Right);
                }
                if (Overlaps(r.Left, r.Right, o.Left, o.Right))
                {
                    Try(ref dh, o.Top - Gap - r.Bottom);
                    Try(ref dh, o.Bottom - r.Bottom);
                }
            }
            foreach (var a in areas)
            {
                Try(ref dw, a.Right - Gap - r.Right);
                Try(ref dh, a.Bottom - Gap - r.Bottom);
            }
            return new Size(r.Width + (Math.Abs(dw) <= Threshold ? dw : 0), r.Height + (Math.Abs(dh) <= Threshold ? dh : 0));
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    }

    class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; private set; }
        public Win32Owner(IntPtr h) { Handle = h; }
    }
}
