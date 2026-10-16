using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Stickers
{
    public class NoteItem
    {
        public string Text { get; set; }
        public bool Done { get; set; }
        public string DoneAt { get; set; }   // completion date "dd.MM.yyyy", checklist items only
        public NoteItem() { Text = ""; }
    }

    public class Note
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Color { get; set; }
        // Position and size of the card itself (without the transparent shadow margin), in DIPs.
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public bool Pinned { get; set; }
        public bool Collapsed { get; set; }
        public bool Hidden { get; set; }   // closed but not deleted: reopened from the tray menu
        public bool Private { get; set; }  // content is blurred
        public string Mode { get; set; }   // "list" | "text"
        public string Text { get; set; }
        public List<NoteItem> Items { get; set; }
        public string DeletedAt { get; set; }

        public Note()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = "";
            Color = Theme.Defaults[0];
            W = 270;
            H = 250;
            Mode = "list";
            Text = "";
            Items = new List<NoteItem>();
        }

        public bool IsList { get { return Mode != "text"; } }

        public bool IsEmpty()
        {
            if (!string.IsNullOrWhiteSpace(Title)) return false;
            if (IsList) return Items.All(i => string.IsNullOrWhiteSpace(i.Text));
            return string.IsNullOrWhiteSpace(Text);
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Title == null) Title = "";
            if (string.IsNullOrEmpty(Color)) Color = Theme.Defaults[0];
            if (Mode != "text") Mode = "list";
            if (Text == null) Text = "";
            if (Items == null) Items = new List<NoteItem>();
            Items.RemoveAll(i => i == null);
            foreach (var i in Items) if (i.Text == null) i.Text = "";
            if (W < 150 || double.IsNaN(W)) W = 270;
            if (H < 100 || double.IsNaN(H)) H = 250;
            if (double.IsNaN(X)) X = 100;
            if (double.IsNaN(Y)) Y = 100;
        }
    }

    public class AppState
    {
        public int Version { get; set; }
        public bool FirstRunDone { get; set; }
        public bool Autostart { get; set; }
        public List<string> CustomColors { get; set; }
        public List<Note> Notes { get; set; }
        public List<Note> Trash { get; set; }

        public AppState()
        {
            Version = 1;
            CustomColors = new List<string>();
            Notes = new List<Note>();
            Trash = new List<Note>();
        }

        public void Normalize()
        {
            if (CustomColors == null) CustomColors = new List<string>();
            if (Notes == null) Notes = new List<Note>();
            if (Trash == null) Trash = new List<Note>();
            Notes.RemoveAll(n => n == null);
            Trash.RemoveAll(n => n == null);
            foreach (var n in Notes) n.Normalize();
            foreach (var n in Trash) n.Normalize();
        }
    }

    public static class Store
    {
        public static string Dir;
        static string FilePath { get { return Path.Combine(Dir, "notes.json"); } }
        static string BackupPath { get { return Path.Combine(Dir, "notes.backup.json"); } }

        static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public static AppState Load()
        {
            foreach (var path in new[] { FilePath, BackupPath })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var state = Serializer().Deserialize<AppState>(File.ReadAllText(path, Encoding.UTF8));
                    if (state == null) continue;
                    state.Normalize();
                    return state;
                }
                catch (Exception ex)
                {
                    Log("Could not read " + path + ": " + ex.Message);
                }
            }
            return new AppState();
        }

        public static void Save(AppState state)
        {
            Directory.CreateDirectory(Dir);
            string json = Serializer().Serialize(state);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, BackupPath, true);
            else File.Move(tmp, FilePath);
        }

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
