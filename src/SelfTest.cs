using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Stickers
{
    // Self-test of the checklist logic and persistence: Stickers.exe --selftest --data <folder>
    // Test strings are Cyrillic on purpose: they check the Unicode round-trip.
    static class SelfTest
    {
        public static void Run(StickerApp app)
        {
            var log = new List<string>();
            int failed = 0;
            var note = new Note { Title = "Тест", X = -30000, Y = -30000 };
            note.Items.Add(new NoteItem { Text = "abc" });
            note.Items.Add(new NoteItem { Text = "def" });
            app.State.Notes.Add(note);
            var w = app.OpenForTest(note);

            var steps = new List<Tuple<string, Action, string>>
            {
                Step("Enter splits an item", () => w.TestKey(0, 1, Key.Enter), "a|bc|def"),
                Step("Backspace at the start merges with the previous item", () => w.TestKey(1, 0, Key.Back), "abc|def"),
                Step("Delete at the end merges with the next item", () => w.TestKey(0, 3, Key.Delete), "abcdef"),
                Step("Enter at the end adds an empty item", () => w.TestKey(0, 6, Key.Enter), "abcdef|"),
                Step("Backspace in an empty item removes it", () => w.TestKey(1, 0, Key.Back), "abcdef"),
                Step("Enter in the title creates an item", () => { note.Items.Clear(); w.TestToggleMode(); w.TestToggleMode(); w.TestKey(-1, 0, Key.Enter); }, ""),
                Step("Checklist -> text -> checklist", () =>
                {
                    note.Items.Clear();
                    note.Items.Add(new NoteItem { Text = "раз", Done = true });
                    note.Items.Add(new NoteItem { Text = "два" });
                    w.TestToggleMode();
                    w.TestToggleMode();
                }, "+раз|два"),
                Step("Checking an item sets the date", () => w.TestSetDone(1, true), "+раз|+два"),
                Step("The date survives text -> checklist", () => { w.TestToggleMode(); w.TestToggleMode(); }, "+раз|+два"),
                Step("Unchecking clears the date", () => w.TestSetDone(1, false), "+раз|два"),
                Step("Blur turns on", () => w.TestTogglePrivate(), "+раз|два"),
            };

            int idx = 0;
            Action next = null;
            next = () =>
            {
                if (idx < steps.Count)
                {
                    var s = steps[idx++];
                    s.Item2();
                    app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        string dump = w.TestDump();
                        string got = dump.Substring(0, dump.IndexOf(" ui="));
                        bool ok = got == s.Item3 && dump.EndsWith(" ui=" + string.Join("|", note.Items.Select(i => i.Text)));
                        if (!ok) failed++;
                        log.Add((ok ? "OK   " : "FAIL ") + s.Item1 + "  →  " + dump + "  dates: " + w.TestDates());
                        next();
                    }));
                    return;
                }

                bool blurred = w.TestBlurred() == "BB";
                if (!blurred) failed++;
                log.Add((blurred ? "OK   " : "FAIL ") + "Both items blurred: " + w.TestBlurred());
                w.TestTogglePrivate();
                bool clear = w.TestBlurred() == "--";
                if (!clear) failed++;
                log.Add((clear ? "OK   " : "FAIL ") + "Blur turns off: " + w.TestBlurred());
                note.Private = true;

                // Close without deleting: the note stays in the data with Hidden = true.
                app.CloseNote(w);
                bool closed = app.State.Notes.Contains(note) && note.Hidden && !app.Stickers.Contains(w);
                if (!closed) failed++;
                log.Add((closed ? "OK   " : "FAIL ") + "Close without deleting");

                // Save and load back (Cyrillic text, flags, sizes).
                note.Pinned = true;
                note.Color = "#F0954F";
                Store.Save(app.State);
                var back = Store.Load().Notes.FirstOrDefault(n => n.Id == note.Id);
                bool saved = back != null && back.Title == "Тест" && back.Pinned && back.Color == "#F0954F"
                             && back.Hidden && back.Private
                             && back.Items.Count == 2 && back.Items[0].Done && back.Items[0].Text == "раз";
                if (!saved) failed++;
                log.Add((saved ? "OK   " : "FAIL ") + "Save/load round-trip");

                Store.Log("SELFTEST " + (failed == 0 ? "PASSED" : failed + " FAILED") + Environment.NewLine + string.Join(Environment.NewLine, log));
                app.Exiting = true;
                app.Shutdown();
            };
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, next);
        }

        static Tuple<string, Action, string> Step(string name, Action a, string expected)
        {
            return Tuple.Create(name, a, expected);
        }
    }
}
