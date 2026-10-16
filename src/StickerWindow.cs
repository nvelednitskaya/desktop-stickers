using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Stickers
{
    public class StickerWindow : Window
    {
        public const double M = 16;               // transparent margin around the card for the shadow
        const double MinW = 190, MinH = 120;

        public readonly Note Note;
        readonly StickerApp app;

        // Brushes are recolored in place on a color change, so every element updates by itself.
        readonly SolidColorBrush bg = new SolidColorBrush(), ink = new SolidColorBrush(), inkSoft = new SolidColorBrush(),
            inkHint = new SolidColorBrush(), inkLine = new SolidColorBrush(), inkFaint = new SolidColorBrush(),
            inkBox = new SolidColorBrush(), edge = new SolidColorBrush();

        Grid root;
        Border card;
        Grid bar, body, textGrid;
        StackPanel tools;
        Button pinBtn, eyeBtn, addBtn, colorBtn, moreBtn, closeBtn;
        TextBox titleBox, textBox;
        TextBlock titleHint, textHint;
        Border titleLine, addRow;
        ScrollViewer scroll;
        StackPanel list;
        Thumb grip;
        Popup colorPopup, menuPopup;

        // Private-note blur: an item is revealed when the mouse rests on it or while it is being edited.
        static readonly BlurEffect Blur = CreateBlur();
        static BlurEffect CreateBlur()
        {
            var b = new BlurEffect { Radius = 9, KernelType = KernelType.Gaussian };
            b.Freeze();
            return b;
        }
        DispatcherTimer revealTimer;
        FrameworkElement revealHover, revealed;

        class Row
        {
            public NoteItem Item;
            public Border Root, Box;
            public Path Check;
            public TextBox Tb;
            public TextBlock Date;
            public Button Del;
        }
        readonly List<Row> rows = new List<Row>();

        IntPtr hwnd;
        double scale = 1;
        bool hover, dragging;
        Point dragCursor0, dragWin0, rsCursor0;
        Size rsSize0;

        public StickerWindow(StickerApp app, Note note)
        {
            this.app = app;
            Note = note;

            Title = L.T("Стикер", "Sticker");
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = note.Pinned;
            FontFamily = Theme.Font;
            FontSize = 14;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            Left = note.X - M;
            Top = note.Y - M;
            Width = note.W + 2 * M;
            Height = note.H + 2 * M;

            BuildChrome();
            ApplyColor();
            BuildBody();
            ApplyPin();
            ApplyPrivate();
            if (note.Collapsed) ApplyCollapsed();

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                var src = PresentationSource.FromVisual(this);
                if (src != null && src.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
            };
            Closing += (s, e) => { if (!app.Exiting && !forceClose) e.Cancel = true; };
            PreviewKeyDown += WindowKeys;
        }

        bool forceClose;
        public void ForceClose() { forceClose = true; Close(); }

        public FrameworkElement Visual { get { return root; } }

        public Rect CardRect
        {
            get { return new Rect(Left + M, Top + M, Math.Max(0, ActualWidth - 2 * M), Math.Max(0, ActualHeight - 2 * M)); }
        }

        // ================= chrome =================

        void BuildChrome()
        {
            root = new Grid();
            card = new Border
            {
                Margin = new Thickness(M),
                CornerRadius = new CornerRadius(14),
                Background = bg,
                BorderBrush = edge,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.2 }
            };
            root.Children.Add(card);
            Content = root;

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            card.Child = g;
            card.MouseEnter += (s, e) => SetHover(true);
            card.MouseLeave += (s, e) => SetHover(false);

            // --- top bar: drag to move, double-click to collapse ---
            bar = new Grid { Height = 34, Background = Brushes.Transparent, Margin = new Thickness(6, 4, 6, 0) };
            bar.MouseLeftButtonDown += BarDown;
            bar.MouseMove += BarMove;
            bar.MouseLeftButtonUp += BarUp;
            bar.LostMouseCapture += (s, e) => EndDrag();
            bar.MouseRightButtonUp += (s, e) => { ShowMenu(); e.Handled = true; };

            // Left side: state toggles, always visible while switched on.
            var status = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            pinBtn = Theme.MakeIconButton(Theme.IcPin, "", inkSoft);
            pinBtn.Click += (s, e) => TogglePin();
            eyeBtn = Theme.MakeIconButton(Theme.IcEye, "", inkSoft);
            eyeBtn.Click += (s, e) => TogglePrivate();
            status.Children.Add(pinBtn);
            status.Children.Add(eyeBtn);
            bar.Children.Add(status);

            tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0 };
            addBtn = Theme.MakeIconButton(Theme.IcAdd, L.T("Новый стикер рядом  (Ctrl+N)", "New sticker next to this one  (Ctrl+N)"), inkSoft);
            colorBtn = Theme.MakeIconButton(Theme.IcColor, L.T("Цвет", "Color"), inkSoft);
            moreBtn = Theme.MakeIconButton(Theme.IcMore, L.T("Ещё", "More"), inkSoft);
            closeBtn = Theme.MakeIconButton(Theme.IcClose, L.T("Закрыть (не удаляет) — вернуть можно из значка в трее  (Ctrl+W)", "Close (not deleted): reopen it from the tray icon  (Ctrl+W)"), inkSoft, 11);
            addBtn.Click += (s, e) => app.NewNote(this);
            colorBtn.Click += (s, e) => ShowColors();
            moreBtn.Click += (s, e) => ShowMenu();
            closeBtn.Click += (s, e) => app.CloseNote(this);
            foreach (var b in new[] { addBtn, colorBtn, moreBtn, closeBtn }) tools.Children.Add(b);
            bar.Children.Add(tools);
            g.Children.Add(bar);

            // --- title ---
            var titleStack = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
            var titleGrid = new Grid();
            titleHint = new TextBlock
            {
                Text = L.T("Название", "Title"), FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = inkHint,
                Margin = new Thickness(2, 0, 0, 0), IsHitTestVisible = false
            };
            titleBox = new TextBox
            {
                Style = Theme.BareText, FontSize = 17, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, MaxLength = 160, Text = Note.Title
            };
            Ink(titleBox);
            titleBox.TextChanged += (s, e) =>
            {
                Note.Title = titleBox.Text;
                titleHint.Visibility = titleBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                app.ScheduleSave();
            };
            titleBox.PreviewKeyDown += TitleKeys;
            titleHint.Visibility = Note.Title.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            titleGrid.Children.Add(titleHint);
            titleGrid.Children.Add(titleBox);
            titleStack.Children.Add(titleGrid);
            titleLine = new Border { Height = 1.5, Background = inkLine, Margin = new Thickness(0, 9, 0, 0) };
            titleStack.Children.Add(titleLine);
            Grid.SetRow(titleStack, 1);
            g.Children.Add(titleStack);

            // --- content ---
            body = new Grid { Margin = new Thickness(16, 2, 8, 14) };
            Grid.SetRow(body, 2);
            g.Children.Add(body);

            scroll = new ScrollViewer
            {
                Focusable = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
                Foreground = ink
            };
            // Inner TextBoxes swallow the mouse wheel, so the list is scrolled here.
            scroll.PreviewMouseWheel += (s, e) =>
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            };
            scroll.MouseLeftButtonDown += ListBlankClick;
            list = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            scroll.Content = list;
            body.Children.Add(scroll);

            addRow = new Border { Padding = new Thickness(0, 7, 0, 7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var addText = new TextBlock { Foreground = inkHint, FontSize = 13.5 };
            addText.Inlines.Add(new System.Windows.Documents.Run("+") { FontSize = 15 });
            addText.Inlines.Add(new System.Windows.Documents.Run(L.T("   добавить пункт", "   add item")));
            addRow.Child = addText;
            addRow.MouseEnter += (s, e) => addText.Foreground = inkSoft;
            addRow.MouseLeave += (s, e) => addText.Foreground = inkHint;
            addRow.MouseLeftButtonDown += (s, e) => { AddAtEnd(); e.Handled = true; };

            textGrid = new Grid { Margin = new Thickness(0, 6, 0, 0), Background = Brushes.Transparent };
            textHint = new TextBlock
            {
                Text = L.T("Пиши здесь…", "Write here…"), Foreground = inkHint, Margin = new Thickness(2, 0, 0, 0),
                IsHitTestVisible = false
            };
            textBox = new TextBox
            {
                Style = Theme.BareText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 8, 0)
            };
            Ink(textBox);
            textBox.TextChanged += (s, e) =>
            {
                Note.Text = textBox.Text;
                textHint.Visibility = textBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                app.ScheduleSave();
            };
            textBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None && LineOf(textBox) == 0)
                {
                    FocusBox(titleBox, titleBox.Text.Length);
                    e.Handled = true;
                }
            };
            WatchReveal(textGrid, textBox);
            textGrid.Children.Add(textHint);
            textGrid.Children.Add(textBox);
            body.Children.Add(textGrid);

            // --- resize grip ---
            grip = new Thumb
            {
                Style = Theme.Grip, Foreground = inkHint, Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3)
            };
            grip.DragStarted += (s, e) => { rsCursor0 = CursorDip(); rsSize0 = new Size(Note.W, Note.H); };
            grip.DragDelta += GripDelta;
            grip.DragCompleted += (s, e) => app.ScheduleSave();
            Grid.SetRowSpan(grip, 3);
            g.Children.Add(grip);
        }

        void Ink(TextBox tb)
        {
            tb.Foreground = ink;
            tb.CaretBrush = ink;
            tb.SelectionBrush = ink;
        }

        // ================= color =================

        public void ApplyColor()
        {
            Color c = Theme.Parse(Note.Color);
            Color k = Theme.Ink(c);
            bg.Color = c;
            ink.Color = k;
            inkSoft.Color = Theme.Alpha(k, 0xA6);
            inkHint.Color = Theme.Alpha(k, 0x6E);
            inkBox.Color = Theme.Alpha(k, 0xB8);
            inkLine.Color = Theme.Alpha(k, 0x40);
            inkFaint.Color = Theme.Alpha(k, 0x1F);
            edge.Color = Theme.IsDark(c) ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0);
        }

        void ShowColors()
        {
            colorPopup = new Popup
            {
                PlacementTarget = colorBtn,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = -84,
                VerticalOffset = -6,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };
            colorPopup.Child = Theme.BuildPalette(Note.Color, app.State.CustomColors,
                hex => { colorPopup.IsOpen = false; SetColor(hex); },
                () => { colorPopup.IsOpen = false; PickCustomColor(); });
            colorPopup.Closed += (s, e) => SetHover(card.IsMouseOver);
            colorPopup.IsOpen = true;
        }

        public void SetColor(string hex)
        {
            Note.Color = hex;
            ApplyColor();
            app.ScheduleSave();
        }

        void PickCustomColor()
        {
            var c = Theme.Parse(Note.Color);
            using (var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                dlg.CustomColors = app.State.CustomColors.Select(h =>
                {
                    var x = Theme.Parse(h);
                    return x.R | (x.G << 8) | (x.B << 16);
                }).ToArray();
                if (dlg.ShowDialog(new Win32Owner(hwnd)) != System.Windows.Forms.DialogResult.OK) return;
                string hex = Theme.Hex(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
                app.RememberColor(hex);
                SetColor(hex);
            }
        }

        // ================= content =================

        void BuildBody()
        {
            rows.Clear();
            list.Children.Clear();
            if (Note.IsList)
            {
                foreach (var item in Note.Items) AppendRow(item);
                list.Children.Add(addRow);
                scroll.Visibility = Visibility.Visible;
                textGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                textBox.Text = Note.Text;
                scroll.Visibility = Visibility.Collapsed;
                textGrid.Visibility = Visibility.Visible;
                UpdateBlur(textGrid, textBox);
            }
        }

        Row MakeRow(NoteItem item)
        {
            var r = new Row { Item = item };
            r.Root = new Border
            {
                BorderBrush = inkFaint,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 7, 0, 7),
                Background = Brushes.Transparent
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r.Root.Child = g;

            r.Check = new Path
            {
                Data = Geometry.Parse("M 2.6,6.4 L 5.1,8.8 L 9.6,3.7"),
                Stroke = bg, StrokeThickness = 1.9,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            };
            r.Box = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(4.5), BorderThickness = new Thickness(1.5),
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 1.5, 0, 0), Cursor = Cursors.Hand, Child = r.Check
            };
            r.Box.MouseLeftButtonDown += (s, e) => { SetDone(r, !r.Item.Done); e.Handled = true; };
            g.Children.Add(r.Box);

            r.Tb = new TextBox { Style = Theme.BareText, TextWrapping = TextWrapping.Wrap, Text = item.Text };
            Ink(r.Tb);
            r.Date = new TextBlock { FontSize = 11, Foreground = inkHint, Margin = new Thickness(2, 2, 0, 0) };
            var cell = new StackPanel();
            cell.Children.Add(r.Tb);
            cell.Children.Add(r.Date);
            Grid.SetColumn(cell, 1);
            r.Tb.TextChanged += (s, e) => { r.Item.Text = r.Tb.Text; app.ScheduleSave(); };
            r.Tb.PreviewKeyDown += (s, e) => RowKeys(r, e);
            r.Tb.GotKeyboardFocus += (s, e) => r.Root.BringIntoView();
            g.Children.Add(cell);

            r.Del = Theme.MakeIconButton(Theme.IcClose, L.T("Удалить пункт", "Delete item"), inkSoft, 9);
            r.Del.Width = r.Del.Height = 20;
            r.Del.VerticalAlignment = VerticalAlignment.Top;
            r.Del.Opacity = 0;
            r.Del.Click += (s, e) => { RemoveRow(r); app.ScheduleSave(); };
            Grid.SetColumn(r.Del, 2);
            g.Children.Add(r.Del);
            r.Root.MouseEnter += (s, e) => r.Del.Opacity = 1;
            r.Root.MouseLeave += (s, e) => r.Del.Opacity = 0;
            WatchReveal(r.Root, r.Tb);

            StyleRow(r);
            UpdateBlur(r.Root, r.Tb);
            return r;
        }

        // ================= private: blur =================

        void TogglePrivate()
        {
            Note.Private = !Note.Private;
            revealed = null;
            ApplyPrivate();
            app.ScheduleSave();
        }

        void ApplyPrivate()
        {
            eyeBtn.Content = Theme.Glyph(Note.Private ? Theme.IcHide : Theme.IcEye, 14);
            eyeBtn.ToolTip = Note.Private
                ? L.T("Содержимое скрыто — показать. Чтобы прочитать один пункт, задержи на нём мышку", "Content hidden: click to show. To read one item, rest the mouse on it")
                : L.T("Скрыть содержимое (размыть пункты)", "Hide content (blur the items)");
            eyeBtn.Foreground = Note.Private ? ink : inkSoft;
            eyeBtn.BeginAnimation(OpacityProperty, null);
            eyeBtn.Opacity = Note.Private || hover ? 1 : 0;
            foreach (var r in rows) UpdateBlur(r.Root, r.Tb);
            UpdateBlur(textGrid, textBox);
        }

        void UpdateBlur(FrameworkElement area, TextBox tb)
        {
            bool show = !Note.Private || tb.IsKeyboardFocusWithin || revealed == area;
            tb.Effect = show ? null : Blur;
        }

        // Reveal only after the mouse rests on an item, so text does not flash when the cursor just passes by.
        void WatchReveal(FrameworkElement area, TextBox tb)
        {
            area.MouseEnter += (s, e) =>
            {
                if (!Note.Private) return;
                revealHover = area;
                if (revealTimer == null)
                {
                    revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                    revealTimer.Tick += (s2, e2) =>
                    {
                        revealTimer.Stop();
                        if (revealHover == null || !revealHover.IsMouseOver) return;
                        revealed = revealHover;
                        ApplyPrivate();
                    };
                }
                revealTimer.Stop();
                revealTimer.Start();
            };
            area.MouseLeave += (s, e) =>
            {
                if (revealHover == area) revealHover = null;
                if (revealed == area) { revealed = null; UpdateBlur(area, tb); }
            };
            tb.GotKeyboardFocus += (s, e) => UpdateBlur(area, tb);
            tb.LostKeyboardFocus += (s, e) => UpdateBlur(area, tb);
        }

        void StyleRow(Row r)
        {
            bool d = r.Item.Done;
            r.Box.Background = d ? (Brush)ink : Brushes.Transparent;
            r.Box.BorderBrush = d ? ink : inkBox;
            r.Check.Visibility = d ? Visibility.Visible : Visibility.Hidden;
            r.Tb.TextDecorations = d ? TextDecorations.Strikethrough : null;
            r.Tb.Opacity = d ? 0.5 : 1;
            r.Date.Text = r.Item.DoneAt;
            r.Date.Visibility = d && !string.IsNullOrEmpty(r.Item.DoneAt) ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetDone(Row r, bool done)
        {
            r.Item.Done = done;
            r.Item.DoneAt = done ? DateTime.Now.ToString("dd.MM.yyyy") : null;
            StyleRow(r);
            app.ScheduleSave();
        }

        void AppendRow(NoteItem item)
        {
            var r = MakeRow(item);
            rows.Add(r);
            list.Children.Add(r.Root);
        }

        Row InsertRow(int index, NoteItem item)
        {
            var r = MakeRow(item);
            rows.Insert(index, r);
            Note.Items.Insert(index, item);
            list.Children.Insert(index, r.Root);
            return r;
        }

        void RemoveRow(Row r)
        {
            rows.Remove(r);
            Note.Items.Remove(r.Item);
            list.Children.Remove(r.Root);
        }

        void AddAtEnd()
        {
            if (Note.Collapsed) SetCollapsed(false);
            var last = rows.LastOrDefault();
            if (last != null && last.Tb.Text.Length == 0) { FocusBox(last.Tb, 0); return; }
            var r = InsertRow(rows.Count, new NoteItem());
            FocusBox(r.Tb, 0);
            app.ScheduleSave();
        }

        void ListBlankClick(object sender, MouseButtonEventArgs e)
        {
            // A click on a row outside its text puts the caret in that row; a click on empty space adds an item.
            for (var d = e.OriginalSource as DependencyObject; d != null && d != scroll; d = VisualTreeHelper.GetParent(d))
            {
                var r = rows.FirstOrDefault(x => x.Root == d);
                if (r != null) { FocusBox(r.Tb, r.Tb.Text.Length); e.Handled = true; return; }
            }
            AddAtEnd();
            e.Handled = true;
        }

        void ToggleMode()
        {
            if (Note.IsList)
            {
                Note.Text = string.Join("\n", Note.Items.Select(i => (i.Done ? "✓ " : "") + i.Text.Replace("\r\n", "\n")));
                Note.Mode = "text";
            }
            else
            {
                var dates = Note.Items.Where(i => i.Done && i.DoneAt != null)
                    .GroupBy(i => i.Text.Replace("\r\n", "\n")).ToDictionary(x => x.Key, x => x.First().DoneAt);
                Note.Items = Note.Text.Replace("\r\n", "\n").Split('\n')
                    .Where(l => l.Trim().Length > 0)
                    .Select(l => l.StartsWith("✓")
                        ? new NoteItem { Text = l.Substring(1).TrimStart(), Done = true }
                        : new NoteItem { Text = l })
                    .ToList();
                string at;
                foreach (var i in Note.Items) if (i.Done && dates.TryGetValue(i.Text, out at)) i.DoneAt = at;
                Note.Mode = "list";
            }
            BuildBody();
            app.ScheduleSave();
        }

        // ================= keyboard =================

        static int LineOf(TextBox tb)
        {
            return tb.GetLineIndexFromCharacterIndex(tb.CaretIndex);
        }

        void FocusBox(TextBox tb, int caret)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                tb.Focus();
                tb.CaretIndex = Math.Min(caret, tb.Text.Length);
            }));
        }

        public void FocusTitle()
        {
            Activate();
            FocusBox(titleBox, titleBox.Text.Length);
        }

        void TitleKeys(object sender, KeyEventArgs e)
        {
            bool enter = e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None;
            bool down = e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None
                        && LineOf(titleBox) >= titleBox.LineCount - 1;
            if (!enter && !down) return;
            e.Handled = true;
            if (Note.Collapsed) SetCollapsed(false);
            if (!Note.IsList) { FocusBox(textBox, 0); return; }
            if (rows.Count == 0) { AddAtEnd(); return; }
            FocusBox(rows[0].Tb, 0);
        }

        void RowKeys(Row r, KeyEventArgs e)
        {
            var tb = r.Tb;
            int i = rows.IndexOf(r);
            var mods = Keyboard.Modifiers;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Enter && mods == ModifierKeys.Control)
            {
                SetDone(r, !r.Item.Done);
                e.Handled = true;
            }
            else if (key == Key.Enter && mods == ModifierKeys.Shift)
            {
                tb.SelectedText = "\n";
                tb.CaretIndex = tb.SelectionStart + 1;
                tb.SelectionLength = 0;
                e.Handled = true;
            }
            else if (key == Key.Enter && mods == ModifierKeys.None)
            {
                if (tb.SelectionLength > 0) tb.SelectedText = "";
                int c = tb.CaretIndex;
                string tail = tb.Text.Substring(c);
                tb.Text = tb.Text.Substring(0, c);
                var nr = InsertRow(i + 1, new NoteItem { Text = tail });
                FocusBox(nr.Tb, 0);
                app.ScheduleSave();
                e.Handled = true;
            }
            else if (key == Key.Back && mods == ModifierKeys.None && tb.SelectionLength == 0 && tb.CaretIndex == 0)
            {
                if (i > 0)
                {
                    var prev = rows[i - 1];
                    int pos = prev.Tb.Text.Length;
                    prev.Tb.Text += tb.Text;
                    RemoveRow(r);
                    FocusBox(prev.Tb, pos);
                    app.ScheduleSave();
                    e.Handled = true;
                }
                else if (tb.Text.Length == 0)
                {
                    RemoveRow(r);
                    if (rows.Count > 0) FocusBox(rows[0].Tb, 0); else FocusBox(titleBox, titleBox.Text.Length);
                    app.ScheduleSave();
                    e.Handled = true;
                }
            }
            else if (key == Key.Delete && mods == ModifierKeys.None && tb.SelectionLength == 0
                     && tb.CaretIndex == tb.Text.Length && i < rows.Count - 1)
            {
                var next = rows[i + 1];
                int pos = tb.Text.Length;
                tb.Text += next.Tb.Text;
                RemoveRow(next);
                tb.CaretIndex = pos;
                app.ScheduleSave();
                e.Handled = true;
            }
            else if (key == Key.Up && mods == ModifierKeys.Alt) { MoveRow(r, -1); e.Handled = true; }
            else if (key == Key.Down && mods == ModifierKeys.Alt) { MoveRow(r, +1); e.Handled = true; }
            else if (key == Key.Up && mods == ModifierKeys.None && LineOf(tb) <= 0)
            {
                if (i > 0) FocusBox(rows[i - 1].Tb, rows[i - 1].Tb.Text.Length);
                else FocusBox(titleBox, titleBox.Text.Length);
                e.Handled = true;
            }
            else if (key == Key.Down && mods == ModifierKeys.None && i < rows.Count - 1
                     && (LineOf(tb) < 0 || LineOf(tb) >= tb.LineCount - 1))
            {
                FocusBox(rows[i + 1].Tb, rows[i + 1].Tb.Text.Length);
                e.Handled = true;
            }
        }

        void MoveRow(Row r, int dir)
        {
            int i = rows.IndexOf(r), j = i + dir;
            if (j < 0 || j >= rows.Count) return;
            int caret = r.Tb.CaretIndex;
            rows.RemoveAt(i); rows.Insert(j, r);
            Note.Items.RemoveAt(i); Note.Items.Insert(j, r.Item);
            list.Children.RemoveAt(i); list.Children.Insert(j, r.Root);
            FocusBox(r.Tb, caret);
            app.ScheduleSave();
        }

        void WindowKeys(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                app.NewNote(this);
                e.Handled = true;
            }
            else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
            {
                app.CloseNote(this);
                e.Handled = true;
            }
        }

        // ================= toolbar =================

        void SetHover(bool on)
        {
            bool popupOpen = (colorPopup != null && colorPopup.IsOpen) || (menuPopup != null && menuPopup.IsOpen);
            if (!on && (popupOpen || dragging)) return;
            if (hover == on) return;
            hover = on;
            var anim = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 120 : 220));
            tools.BeginAnimation(OpacityProperty, anim);
            grip.BeginAnimation(OpacityProperty, anim);
            if (!Note.Pinned) pinBtn.BeginAnimation(OpacityProperty, anim);
            if (!Note.Private) eyeBtn.BeginAnimation(OpacityProperty, anim);
        }

        public void ShowToolsNow()
        {
            hover = true;
            tools.Opacity = grip.Opacity = pinBtn.Opacity = eyeBtn.Opacity = 1;
        }

        void ShowMenu()
        {
            menuPopup = new Popup
            {
                PlacementTarget = moreBtn,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = -150,
                VerticalOffset = -6,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };
            Action<Action> run = a => { menuPopup.IsOpen = false; a(); };
            menuPopup.Child = Theme.BuildMenu(new[]
            {
                Note.IsList
                    ? Theme.Item(Theme.IcNote, L.T("Сделать обычной заметкой", "Make it a plain note"), () => run(ToggleMode))
                    : Theme.Item(Theme.IcChecklist, L.T("Сделать чеклистом", "Make it a checklist"), () => run(ToggleMode)),
                Theme.Item(Note.Private ? Theme.IcEye : Theme.IcHide,
                    Note.Private ? L.T("Показать содержимое", "Show content") : L.T("Скрыть содержимое (блюр)", "Hide content (blur)"), () => run(TogglePrivate)),
                Theme.Item(Note.Collapsed ? Theme.IcDown : Theme.IcUp,
                    Note.Collapsed ? L.T("Развернуть", "Expand") : L.T("Свернуть до названия", "Collapse to title"), () => run(() => SetCollapsed(!Note.Collapsed))),
                Theme.Item(Theme.IcClose, L.T("Закрыть  (Ctrl+W)", "Close  (Ctrl+W)"), () => run(() => app.CloseNote(this))),
                null,
                Theme.Item(Theme.IcDelete, L.T("Удалить в корзину", "Move to trash"), () => run(() => app.Delete(this)))
            });
            menuPopup.Closed += (s, e) => SetHover(card.IsMouseOver);
            menuPopup.IsOpen = true;
        }

        void TogglePin()
        {
            Note.Pinned = !Note.Pinned;
            Topmost = Note.Pinned;
            ApplyPin();
            app.ScheduleSave();
        }

        void ApplyPin()
        {
            pinBtn.Content = Theme.Glyph(Note.Pinned ? Theme.IcPinned : Theme.IcPin, 13);
            pinBtn.ToolTip = Note.Pinned ? L.T("Закреплён поверх всех окон — открепить", "Pinned above all windows: unpin") : L.T("Закрепить поверх всех окон", "Pin above all windows");
            pinBtn.Foreground = Note.Pinned ? ink : inkSoft;
            pinBtn.BeginAnimation(OpacityProperty, null);
            pinBtn.Opacity = Note.Pinned || hover ? 1 : 0;
        }

        void SetCollapsed(bool collapsed)
        {
            Note.Collapsed = collapsed;
            ApplyCollapsed();
            app.ScheduleSave();
        }

        void ApplyCollapsed()
        {
            bool c = Note.Collapsed;
            body.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
            grip.Visibility = body.Visibility;
            titleLine.Visibility = body.Visibility;
            titleBox.Margin = new Thickness(0, 0, 0, c ? 14 : 0);
            titleHint.Margin = new Thickness(2, 0, 0, c ? 14 : 0);
            if (c) SizeToContent = SizeToContent.Height;
            else
            {
                SizeToContent = SizeToContent.Manual;
                Height = Note.H + 2 * M;
            }
        }

        // ================= drag and resize =================

        Point CursorDip()
        {
            Native.POINT p;
            Native.GetCursorPos(out p);
            return new Point(p.X / scale, p.Y / scale);
        }

        IEnumerable<Rect> ScreenAreas()
        {
            return System.Windows.Forms.Screen.AllScreens.Select(s => new Rect(
                s.WorkingArea.X / scale, s.WorkingArea.Y / scale, s.WorkingArea.Width / scale, s.WorkingArea.Height / scale));
        }

        IEnumerable<Rect> Neighbours()
        {
            return app.Stickers.Where(w => w != this && w.IsVisible).Select(w => w.CardRect).ToList();
        }

        void BarDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { SetCollapsed(!Note.Collapsed); e.Handled = true; return; }
            dragging = true;
            dragCursor0 = CursorDip();
            dragWin0 = new Point(Left, Top);
            bar.CaptureMouse();
            e.Handled = true;
        }

        void BarMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point c = CursorDip();
            double x = dragWin0.X + c.X - dragCursor0.X, y = dragWin0.Y + c.Y - dragCursor0.Y;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                var me = new Rect(x + M, y + M, ActualWidth - 2 * M, ActualHeight - 2 * M);
                Vector v = Snap.Move(me, Neighbours(), ScreenAreas());
                x += v.X;
                y += v.Y;
            }
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(x * scale), (int)Math.Round(y * scale), 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Note.X = x + M;
            Note.Y = y + M;
        }

        void BarUp(object sender, MouseButtonEventArgs e)
        {
            if (dragging) bar.ReleaseMouseCapture();
        }

        void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            app.ScheduleSave();
            SetHover(card.IsMouseOver);
        }

        void GripDelta(object sender, DragDeltaEventArgs e)
        {
            Point c = CursorDip();
            double w = Math.Max(MinW, rsSize0.Width + c.X - rsCursor0.X);
            double h = Math.Max(MinH, rsSize0.Height + c.Y - rsCursor0.Y);
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                Size s = Snap.Resize(new Rect(Left + M, Top + M, w, h), Neighbours(), ScreenAreas());
                w = Math.Max(MinW, s.Width);
                h = Math.Max(MinH, s.Height);
            }
            Note.W = w;
            Note.H = h;
            Width = w + 2 * M;
            Height = h + 2 * M;
        }

        // ---- hooks for --selftest ----
        internal void TestKey(int row, int caret, Key key)
        {
            TextBox tb = row < 0 ? titleBox : rows[row].Tb;
            tb.CaretIndex = caret;
            tb.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(tb), 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            });
        }

        internal void TestToggleMode() { ToggleMode(); }

        internal void TestTogglePrivate() { TogglePrivate(); }

        internal void TestSetDone(int row, bool done) { SetDone(rows[row], done); }

        internal string TestDates()
        {
            return string.Join("|", rows.Select(r => r.Date.Visibility == Visibility.Visible ? r.Date.Text : "-"));
        }

        internal string TestBlurred()
        {
            return string.Join("", rows.Select(r => r.Tb.Effect != null ? "B" : "-"));
        }

        internal string TestDump()
        {
            return string.Join("|", Note.Items.Select(i => (i.Done ? "+" : "") + i.Text))
                + " ui=" + string.Join("|", rows.Select(r => r.Tb.Text));
        }

        public void BringToFront()
        {
            if (!IsVisible) Show();
            Topmost = true;
            Topmost = Note.Pinned;
        }
    }
}
