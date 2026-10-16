using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Stickers
{
    public class StickerApp : Application
    {
        const string ShowEventName = "Local\\Stickers.Desktop.Show";
        const string ExitEventName = "Local\\Stickers.Desktop.Exit";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public AppState State;
        public readonly List<StickerWindow> Stickers = new List<StickerWindow>();
        public bool Exiting;
        internal StickerWindow OpenForTest(Note n) { return Open(n, false); }

        string snapshotPath, langOverride;
        bool customData, selfTest;
        Forms.NotifyIcon tray;
        Forms.ToolStripMenuItem hiddenMenu, trashMenu, autostartItem;
        DispatcherTimer saveTimer;
        EventWaitHandle showEvent, exitEvent;
        int cascade;

        [STAThread]
        public static void Main(string[] args)
        {
            string data = null, snapshot = null, lang = null;
            bool exit = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--data" && i + 1 < args.Length) data = args[++i];
                else if (args[i] == "--snapshot" && i + 1 < args.Length) snapshot = args[++i];
                else if (args[i] == "--exit") exit = true;
                else if (args[i] == "--selftest") snapshot = "";
                else if (args[i] == "--lang" && i + 1 < args.Length) lang = args[++i];
            }
            // Data lives in a "data" folder next to the exe, not in AppData: when the app is started by a
            // packaged (MSIX) app, Windows redirects AppData and registry writes into that package's private copy.
            Store.Dir = data ?? Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "data");

            Mutex mutex = null;
            if (snapshot == null)
            {
                bool created;
                mutex = new Mutex(true, "Local\\Stickers.Desktop.Single", out created);
                if (!created || exit)
                {
                    try { EventWaitHandle.OpenExisting(exit ? ExitEventName : ShowEventName).Set(); } catch { }
                    return;
                }
            }

            Forms.Application.EnableVisualStyles();
            var app = new StickerApp
            {
                snapshotPath = snapshot == "" ? null : snapshot,
                selfTest = snapshot == "",
                customData = data != null,
                langOverride = lang
            };
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.Run();
            GC.KeepAlive(mutex);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, ex) => { Store.Log("Error: " + ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Store.Log("Fatal error: " + ex.ExceptionObject);

            Directory.CreateDirectory(Store.Dir);
            Theme.LoadFonts(Store.Dir);
            Theme.InstallStyles(this);
            State = Store.Load();
            L.Init(langOverride ?? State.Language);

            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            saveTimer.Tick += (s, ev) => SaveNow();

            if (snapshotPath != null) { RunSnapshot(); return; }
            if (selfTest) { SelfTest.Run(this); return; }

            if (!State.FirstRunDone)
            {
                State.FirstRunDone = true;
                State.Autostart = !customData;
                if (State.Notes.Count == 0) State.Notes.Add(WelcomeNote());
            }
            if (!customData) ApplyAutostart();

            foreach (var n in State.Notes.Where(x => !x.Hidden)) { KeepOnScreen(n); Open(n, false); }
            SetupTray();

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            ThreadPool.RegisterWaitForSingleObject(showEvent,
                (st, t) => Dispatcher.BeginInvoke(new Action(BringAll)), null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(exitEvent,
                (st, t) => Dispatcher.BeginInvoke(new Action(ExitApp)), null, -1, true);

            SessionEnding += (s, ev) => SaveNow();
            SaveNow();
        }

        // ================= stickers =================

        StickerWindow Open(Note n, bool activate)
        {
            var w = new StickerWindow(this, n);
            Stickers.Add(w);
            w.Show();
            if (activate) w.FocusTitle();
            return w;
        }

        public void NewNote(StickerWindow near)
        {
            var n = new Note();
            Rect wa = SystemParameters.WorkArea;
            if (near != null)
            {
                Rect r = near.CardRect;
                n.Color = Theme.NextDefault(near.Note.Color);
                n.W = near.Note.W;
                n.H = near.Note.H;
                if (r.Right + Snap.Gap + n.W <= wa.Right) { n.X = r.Right + Snap.Gap; n.Y = r.Top; }
                else if (r.Bottom + Snap.Gap + n.H <= wa.Bottom) { n.X = r.Left; n.Y = r.Bottom + Snap.Gap; }
                else { n.X = r.Left - Snap.Gap - n.W; n.Y = r.Top; }
            }
            else
            {
                n.Color = Theme.Defaults[State.Notes.Count % Theme.Defaults.Length];
                double off = (cascade++ % 6) * 28;
                n.X = wa.Left + (wa.Width - n.W) / 2 + off;
                n.Y = wa.Top + (wa.Height - n.H) / 2 + off - 60;
            }
            KeepOnScreen(n);
            State.Notes.Add(n);
            Open(n, true);
            ScheduleSave();
        }

        public void Delete(StickerWindow w)
        {
            var n = w.Note;
            State.Notes.Remove(n);
            Stickers.Remove(w);
            w.ForceClose();
            if (!n.IsEmpty())
            {
                n.DeletedAt = DateTime.Now.ToString("dd.MM HH:mm");
                State.Trash.Insert(0, n);
                if (State.Trash.Count > 30) State.Trash.RemoveRange(30, State.Trash.Count - 30);
            }
            ScheduleSave();
        }

        // Close without deleting: the note stays in the data and can be reopened from the tray menu.
        public void CloseNote(StickerWindow w)
        {
            if (w.Note.IsEmpty()) { Delete(w); return; }
            w.Note.Hidden = true;
            Stickers.Remove(w);
            w.ForceClose();
            ScheduleSave();
        }

        void Reopen(Note n)
        {
            n.Hidden = false;
            KeepOnScreen(n);
            Open(n, true);
            ScheduleSave();
        }

        void Restore(Note n)
        {
            State.Trash.Remove(n);
            n.DeletedAt = null;
            n.Hidden = false;
            KeepOnScreen(n);
            State.Notes.Add(n);
            Open(n, true);
            ScheduleSave();
        }

        public void RememberColor(string hex)
        {
            State.CustomColors.RemoveAll(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));
            State.CustomColors.Insert(0, hex);
            if (State.CustomColors.Count > 10) State.CustomColors.RemoveRange(10, State.CustomColors.Count - 10);
            ScheduleSave();
        }

        void BringAll()
        {
            if (Stickers.Count == 0) { NewNote(null); return; }
            foreach (var w in Stickers) w.BringToFront();
        }

        static void KeepOnScreen(Note n)
        {
            double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
            double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
            // At least the drag bar at the top must stay on screen.
            if (n.X + n.W < l + 60 || n.X > r - 60 || n.Y < t || n.Y > b - 50)
            {
                Rect wa = SystemParameters.WorkArea;
                n.X = wa.Left + (wa.Width - n.W) / 2;
                n.Y = wa.Top + (wa.Height - n.H) / 2;
            }
        }

        // ================= templates =================

        static Note WelcomeNote()
        {
            Rect wa = SystemParameters.WorkArea;
            var n = new Note
            {
                Title = L.T("Привет! Это стикер", "Hi! This is a sticker"),
                Color = "#EBE645",
                W = 320,
                H = 400,
                Items = new List<NoteItem>
                {
                    new NoteItem { Text = L.T("Тяни за верхнюю полоску — стикеры прилипают друг к другу", "Drag the top bar: stickers snap to each other") },
                    new NoteItem { Text = L.T("Enter — новый пункт, Ctrl+Enter — отметить", "Enter adds an item, Ctrl+Enter checks it") },
                    new NoteItem { Text = L.T("Наведи мышку — сверху появятся кнопки", "Hover to see the buttons at the top") },
                    new NoteItem { Text = L.T("Булавка — стикер поверх всех окон", "The pin keeps a sticker above all windows") },
                    new NoteItem { Text = L.T("Двойной клик по полоске — свернуть", "Double-click the top bar to collapse") },
                    new NoteItem { Text = L.T("Матрица Эйзенхауэра — в меню значка в трее", "Eisenhower matrix: in the tray icon menu") }
                }
            };
            n.X = wa.Right - n.W - 40;
            n.Y = wa.Top + 40;
            return n;
        }

        void CreateGrid(int cols, int rows, string[] titles, string[] colors, double maxW, double maxH, string[] textModes)
        {
            Rect wa = SystemParameters.WorkArea;
            double g = Snap.Gap;
            double w = Math.Min(maxW, (wa.Width - 80 - (cols - 1) * g) / cols);
            double h = Math.Min(maxH, (wa.Height - 40 - (rows - 1) * g) / rows);
            double x0 = wa.Left + (wa.Width - (cols * w + (cols - 1) * g)) / 2;
            double y0 = wa.Top + (wa.Height - (rows * h + (rows - 1) * g)) / 2;
            StickerWindow first = null;
            for (int i = 0; i < titles.Length; i++)
            {
                var n = new Note
                {
                    Title = titles[i],
                    Color = colors[i % colors.Length],
                    W = w,
                    H = h,
                    X = x0 + (i % cols) * (w + g),
                    Y = y0 + (i / cols) * (h + g),
                    Mode = textModes != null && textModes.Contains(titles[i]) ? "text" : "list"
                };
                State.Notes.Add(n);
                var win = Open(n, false);
                if (first == null) first = win;
            }
            if (first != null) first.Activate();
            ScheduleSave();
        }

        void CreateMatrix()
        {
            CreateGrid(2, 2,
                new[] { L.T("Срочно и важно", "Urgent & important"), L.T("Важно, не срочно", "Important, not urgent"), L.T("Срочно, не важно", "Urgent, not important"), L.T("Не срочно и не важно", "Not urgent, not important") },
                new[] { "#F0954F", "#BCD979", "#EBE645", "#C3DFE0" },
                320, 270, null);
        }

        void CreateBrainDump()
        {
            CreateGrid(3, 4,
                new[]
                {
                    L.T("Позвонить", "To call"), L.T("Написать письмо", "To email"), L.T("Написать в мессенджер", "To text"),
                    L.T("Сделать сегодня", "To do today"), L.T("Встречи", "Appointments"), L.T("Напомнить / проверить", "To follow up"),
                    L.T("Сделать скоро", "To do soon"), L.T("Запланировать", "To schedule"), L.T("Изучить", "To research"),
                    L.T("Победы дня", "Today's wins"), L.T("Купить", "To buy"), L.T("Мысли", "Thoughts")
                },
                new[]
                {
                    "#EBD5F2", "#DCDAF6", "#D6E2F6",
                    "#F8DEC4", "#E7E8C2", "#D3ECEB",
                    "#F8DEC4", "#EFE3C6", "#CFECDF",
                    "#F6CABF", "#F6D5CC", "#F3C9A6"
                },
                250, 215, new[] { L.T("Мысли", "Thoughts") });
        }

        // ================= tray =================

        void SetupTray()
        {
            var menu = new Forms.ContextMenuStrip { Font = new System.Drawing.Font("Segoe UI", 9.5f), ShowImageMargin = false };
            menu.ShowCheckMargin = true;
            menu.Items.Add(L.T("Новый стикер", "New sticker"), null, (s, e) => NewNote(null));
            menu.Items.Add(L.T("Матрица Эйзенхауэра (4 стикера)", "Eisenhower matrix (4 stickers)"), null, (s, e) => CreateMatrix());
            menu.Items.Add(L.T("Brain dump (12 стикеров)", "Brain dump (12 stickers)"), null, (s, e) => CreateBrainDump());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(L.T("Показать все стикеры", "Show all stickers"), null, (s, e) => BringAll());
            hiddenMenu = new Forms.ToolStripMenuItem(L.T("Закрытые стикеры", "Closed stickers"));
            menu.Items.Add(hiddenMenu);
            trashMenu = new Forms.ToolStripMenuItem(L.T("Корзина", "Trash"));
            menu.Items.Add(trashMenu);
            menu.Items.Add(new Forms.ToolStripSeparator());
            autostartItem = new Forms.ToolStripMenuItem(L.T("Запускать вместе с Windows", "Start with Windows"));
            autostartItem.Click += (s, e) =>
            {
                State.Autostart = !State.Autostart;
                ApplyAutostart();
                ScheduleSave();
            };
            menu.Items.Add(autostartItem);
            menu.Items.Add(L.T("Открыть папку с данными", "Open data folder"), null, (s, e) => Process.Start("explorer.exe", "\"" + Store.Dir + "\""));
            var langMenu = new Forms.ToolStripMenuItem("Язык / Language");
            langMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(L.T("Как в Windows", "Same as Windows"), null,
                (s, e) => SetLanguage(null)) { Checked = State.Language == null });
            langMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Русский", null,
                (s, e) => SetLanguage("ru")) { Checked = State.Language == "ru" });
            langMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("English", null,
                (s, e) => SetLanguage("en")) { Checked = State.Language == "en" });
            menu.Items.Add(langMenu);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(L.T("Выход", "Exit"), null, (s, e) => ExitApp());
            menu.Opening += (s, e) => RefreshMenu();

            tray = new Forms.NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = L.T("Стикеры — клик: показать все", "Stickers: click to show all"),
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) BringAll(); };
        }

        // Switching the language rebuilds the sticker windows and the tray menu; the notes themselves do not change.
        void SetLanguage(string lang)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                State.Language = lang;
                L.Init(lang);
                ScheduleSave();
                foreach (var w in Stickers.ToList()) w.ForceClose();
                Stickers.Clear();
                foreach (var n in State.Notes.Where(x => !x.Hidden)) Open(n, false);
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                SetupTray();
            }));
        }

        void RefreshMenu()
        {
            autostartItem.Checked = State.Autostart;

            var hidden = State.Notes.Where(n => n.Hidden).ToList();
            hiddenMenu.Text = hidden.Count == 0 ? L.T("Закрытые стикеры", "Closed stickers") : L.T("Закрытые стикеры (", "Closed stickers (") + hidden.Count + ")";
            hiddenMenu.DropDownItems.Clear();
            if (hidden.Count == 0)
                hiddenMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(L.T("Нет закрытых", "No closed stickers")) { Enabled = false });
            foreach (var n in hidden)
            {
                var note = n;
                hiddenMenu.DropDownItems.Add(L.T("Открыть: ", "Open: ") + MenuTitle(n), null, (s, e) => Reopen(note));
            }
            if (hidden.Count > 1)
            {
                hiddenMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
                hiddenMenu.DropDownItems.Add(L.T("Открыть все", "Open all"), null, (s, e) => { foreach (var n in hidden) Reopen(n); });
            }

            trashMenu.DropDownItems.Clear();
            if (State.Trash.Count == 0)
            {
                trashMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(L.T("Пусто", "Empty")) { Enabled = false });
                return;
            }
            foreach (var n in State.Trash.ToList())
            {
                var note = n;
                trashMenu.DropDownItems.Add(L.T("Вернуть: ", "Restore: ") + MenuTitle(n) + "   · " + n.DeletedAt, null, (s, e) => Restore(note));
            }
            trashMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            trashMenu.DropDownItems.Add(L.T("Очистить корзину", "Empty trash"), null, (s, e) => { State.Trash.Clear(); ScheduleSave(); });
        }

        // For private notes the menu shows only the title, never the item text.
        static string MenuTitle(Note n)
        {
            string title = !string.IsNullOrWhiteSpace(n.Title) ? n.Title : n.Private ? L.T("личное, без названия", "personal, untitled") : FirstLine(n);
            title = title.Trim();
            return title.Length > 40 ? title.Substring(0, 40) + "…" : title;
        }

        static string FirstLine(Note n)
        {
            string s = n.IsList ? n.Items.Select(i => i.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) : n.Text;
            s = (s ?? L.T("без названия", "untitled")).Trim();
            int nl = s.IndexOf('\n');
            return nl > 0 ? s.Substring(0, nl).Trim() : s;
        }

        static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Stickers.icon.ico"))
                    return new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
            }
            catch { return System.Drawing.SystemIcons.Application; }
        }

        void ApplyAutostart()
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            bool on = State.Autostart;
            // Older versions used HKCU\...\Run; remove it so the app is not started twice.
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null) k.DeleteValue("Stickers", false);
            }
            catch (Exception ex) { Store.Log("Autostart (registry): " + ex.Message); }

            // Autostart is a Task Scheduler "at log on" task: it lives in the real system
            // (not in a virtualized registry copy) and starts the app just like a double-click.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { SyncLogonTask(on, exe); }
                catch (Exception ex) { Store.Log("Autostart (Task Scheduler): " + ex.Message); }
            });
        }

        const string TaskName = "Stickers";

        static void SyncLogonTask(bool on, string exe)
        {
            if (!on)
            {
                RunSchtasks("/delete /tn \"" + TaskName + "\" /f");
                return;
            }
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                "  <RegistrationInfo><Description>Desktop Stickers: start at Windows sign-in</Description></RegistrationInfo>\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId></LogonTrigger></Triggers>\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId>" +
                "<LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exe) + "</Command></Exec></Actions>\n" +
                "</Task>\n";
            string file = Path.Combine(Path.GetTempPath(), "stickers-task.xml");
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            try
            {
                int code = RunSchtasks("/create /tn \"" + TaskName + "\" /xml \"" + file + "\" /f");
                if (code != 0) Store.Log("Autostart (Task Scheduler): schtasks exit code " + code);
            }
            finally { File.Delete(file); }
        }

        static int RunSchtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode;
            }
        }

        // ================= saving and exit =================

        public void ScheduleSave()
        {
            if (saveTimer == null) return;
            saveTimer.Stop();
            saveTimer.Start();
        }

        void SaveNow()
        {
            saveTimer.Stop();
            if (snapshotPath != null || selfTest) return;
            try { Store.Save(State); }
            catch (Exception ex) { Store.Log("Could not save: " + ex.Message); }
        }

        void ExitApp()
        {
            if (Exiting) return;
            Exiting = true;
            SaveNow();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            foreach (var w in Stickers.ToList()) w.Close();
            Shutdown();
        }

        // ================= layout snapshot (--snapshot) =================

        void RunSnapshot()
        {
            var notes = new List<Note>();
            Func<string, string, double, double, double, double, Note> mk = (title, color, x, y, w, h) =>
            {
                var n = new Note { Title = title, Color = color, X = x, Y = y, W = w, H = h };
                notes.Add(n);
                return n;
            };
            double g = Snap.Gap;
            var a = mk(L.T("Срочно и важно", "Urgent & important"), "#F0954F", 0, 0, 300, 250);
            a.Items.AddRange(new[] {
                new NoteItem { Text = L.T("Отправить договор партнёрам", "Send the contract to partners") },
                new NoteItem { Text = L.T("Созвон с командой в 15:00", "Team call at 3 pm"), Done = true, DoneAt = "13.10.2026" },
                new NoteItem { Text = L.T("Счёт за хостинг — оплатить до пятницы, иначе отключат", "Pay the hosting bill by Friday or the site goes down") } });
            a.Pinned = true;
            var b = mk(L.T("Важно, не срочно", "Important, not urgent"), "#BCD979", 300 + g, 0, 300, 250);
            b.Items.AddRange(new[] { new NoteItem { Text = L.T("План на квартал", "Quarterly plan") }, new NoteItem { Text = L.T("Спорт 3 раза в неделю", "Workout 3 times a week") } });
            var c = mk(L.T("Срочно, не важно", "Urgent, not important"), "#EBE645", 0, 250 + g, 300, 250);
            c.Items.Add(new NoteItem { Text = L.T("Ответить в чатах", "Reply in the chats") });
            mk(L.T("Не срочно и не важно", "Not urgent, not important"), "#C3DFE0", 300 + g, 250 + g, 300, 250);
            var t = mk(L.T("Мысли", "Thoughts"), "#F6CABF", 640, 0, 260, 200);
            t.Mode = "text";
            t.Text = L.T("Сделать лендинг светлее.\nСпросить про бюджет на ноябрь.", "Make the landing page lighter.\nAsk about the November budget.");
            var d = mk("", "#3A3835", 640, 200 + g, 260, 180);
            d.Items.Add(new NoteItem { Text = L.T("Тёмный стикер", "Dark sticker") });
            d.Items.Add(new NoteItem { Text = L.T("с отметкой", "with a checkmark"), Done = true, DoneAt = "14.10.2026" });
            var col = mk(L.T("Свёрнутый стикер", "Collapsed sticker"), "#DCDAF6", 640, 390 + 2 * g, 260, 200);
            col.Collapsed = true;
            var priv = mk(L.T("Личное", "Personal"), "#C3DFE0", 930, 330, 260, 220);
            priv.Private = true;
            priv.Items.Add(new NoteItem { Text = L.T("Записаться к врачу на четверг", "Book a doctor for Thursday") });
            priv.Items.Add(new NoteItem { Text = L.T("Подарок маме", "Gift for mom"), Done = true });

            const double off = -30000;
            var wins = notes.Select(n =>
            {
                n.X += off; n.Y += off;
                var w = new StickerWindow(this, n);
                w.Show();
                return w;
            }).ToList();
            wins[1].ShowToolsNow();

            var palette = new System.Windows.Controls.Grid();
            palette.Children.Add(Theme.BuildPalette("#BCD979", new[] { "#7FB3D5", "#C39BD3" }, h => { }, () => { }));
            var menuPanel = new System.Windows.Controls.Grid();
            menuPanel.Children.Add(Theme.BuildMenu(new[]
            {
                Theme.Item(Theme.IcNote, L.T("Сделать обычной заметкой", "Make it a plain note"), () => { }),
                Theme.Item(Theme.IcHide, L.T("Скрыть содержимое (блюр)", "Hide content (blur)"), () => { }),
                Theme.Item(Theme.IcUp, L.T("Свернуть до названия", "Collapse to title"), () => { }),
                Theme.Item(Theme.IcClose, L.T("Закрыть  (Ctrl+W)", "Close  (Ctrl+W)"), () => { }),
                null,
                Theme.Item(Theme.IcDelete, L.T("Удалить в корзину", "Move to trash"), () => { })
            }));

            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                palette.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                palette.Arrange(new Rect(palette.DesiredSize));
                palette.UpdateLayout();
                menuPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                menuPanel.Arrange(new Rect(menuPanel.DesiredSize));
                menuPanel.UpdateLayout();

                const double pad = 30, dpi = 192, k = dpi / 96;
                double W = 930 + 280 + 2 * pad, H = 800 + 2 * pad;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x6C, 0x7A, 0x86), Color.FromRgb(0x3B, 0x45, 0x4F), 60),
                        null, new Rect(0, 0, W, H));
                    foreach (var w in wins)
                    {
                        var el = w.Visual;
                        var rtb = new RenderTargetBitmap((int)(el.ActualWidth * k), (int)(el.ActualHeight * k), dpi, dpi, PixelFormats.Pbgra32);
                        rtb.Render(el);
                        dc.DrawImage(rtb, new Rect(w.Note.X - off - StickerWindow.M + pad, w.Note.Y - off - StickerWindow.M + pad,
                            el.ActualWidth, el.ActualHeight));
                    }
                    var prtb = new RenderTargetBitmap((int)(palette.ActualWidth * k), (int)(palette.ActualHeight * k), dpi, dpi, PixelFormats.Pbgra32);
                    prtb.Render(palette);
                    dc.DrawImage(prtb, new Rect(930 + pad - 14, pad - 12, palette.ActualWidth, palette.ActualHeight));
                    var mrtb = new RenderTargetBitmap((int)(menuPanel.ActualWidth * k), (int)(menuPanel.ActualHeight * k), dpi, dpi, PixelFormats.Pbgra32);
                    mrtb.Render(menuPanel);
                    dc.DrawImage(mrtb, new Rect(930 + pad - 14, 330 + 220 + pad - 4, menuPanel.ActualWidth, menuPanel.ActualHeight));
                }
                var all = new RenderTargetBitmap((int)(W * k), (int)(H * k), dpi, dpi, PixelFormats.Pbgra32);
                all.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(all));
                using (var f = File.Create(snapshotPath)) enc.Save(f);

                Store.Log("snapshot font=" + Theme.Font.Source + " faces=" +
                    string.Join(",", Theme.Font.GetTypefaces().Select(tf => tf.Weight.ToString())));
                Exiting = true;
                foreach (var w in wins) w.Close();
                Shutdown();
            }));
        }
    }
}
