/*
    TuioDemo.cs — Self-contained Animal Home Game using the original TUIO C# demo architecture.

    Architecture mirrors the original TUIO C# Demo by Martin Kaltenbrunner (GPL v2+):
      • TuioClient + TuioListener implemented directly on the Form class
      • objectList / cursorList / blobList dictionaries track all live TUIO events
      • OnPaintBackground draws everything (no PictureBox controls)

    Game UI matches the Animal Home Game design:
      • Animals on the left  — controlled by TUIO markers
      • Homes   on the right — animals must be dragged onto their matching home
      • Images loaded from the Assets folder (or a fallback colour if missing)

    Controls:
      F1  — toggle fullscreen
      V   — toggle verbose console output
      Esc — exit
*/

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using TUIO;

namespace AnimalHomeGame_CSharp
{
    public class TuioDemo : Form, TuioListener
    {
        // ── TUIO state (identical to original demo) ──────────────────────
        private TuioClient client;
        private Dictionary<long, TuioObject> objectList = new Dictionary<long, TuioObject>(128);
        private Dictionary<long, TuioCursor> cursorList = new Dictionary<long, TuioCursor>(128);
        private Dictionary<long, TuioBlob> blobList = new Dictionary<long, TuioBlob>(128);

        // ── Window geometry (same fields as original demo) ───────────────
        public static int width, height;
        private int window_width = 1024;
        private int window_height = 768;
        private int window_left = 0;
        private int window_top = 0;
        private int screen_width = Screen.PrimaryScreen.Bounds.Width;
        private int screen_height = Screen.PrimaryScreen.Bounds.Height;
        private bool fullscreen = false;
        private bool verbose = false;

        // ── Animal and home definitions ──────────────────────────────────
        // Each animal has a name, the TUIO marker ID that controls it,
        // its image file, and the name of the home it belongs to.
        private static readonly (string name, int tuioId, string image, string home)[] AnimalDefs =
        {
            ("Bird", 0, "bird.jpeg",  "Nest"),
            ("Dog",  1, "dog.jpeg",   "Doghouse"),
            ("Fish", 2, "fish.jpeg",  "Water"),
            ("Cow",  3, "farm.jpeg",  "Farm"),
        };

        private static readonly (string name, string image)[] HomeDefs =
        {
            ("Nest",     "nest.jpeg"),
            ("Doghouse", "doghouse.jpeg"),
            ("Water",    "water.jpeg"),
            ("Farm",     "farm.jpeg"),
        };

        // ── Game state ───────────────────────────────────────────────────
        // Current position of each animal on screen (keyed by tuioId)
        private Dictionary<int, PointF> animalPositions = new Dictionary<int, PointF>();
        // Starting position each animal returns to when dropped incorrectly
        private Dictionary<int, PointF> animalOrigins = new Dictionary<int, PointF>();
        // True once an animal has been successfully snapped to its home
        private Dictionary<int, bool> animalMatched = new Dictionary<int, bool>();
        // Maps a live TUIO session ID → the tuioId of the animal it controls
        private Dictionary<long, int> sessionToAnimal = new Dictionary<long, int>();
        // Bounding rectangle of each home zone (for overlap detection on drop)
        private Dictionary<string, RectangleF> homeRects = new Dictionary<string, RectangleF>();

        private const int ITEM_W = 110;
        private const int ITEM_H = 100;

        // ── Feedback bar ─────────────────────────────────────────────────
        private string feedbackText = "Place a TUIO marker on an animal and move it to its home!";
        private Color feedbackColor = Color.FromArgb(160, 20, 20, 40);

        // ── Images ───────────────────────────────────────────────────────
        private Dictionary<string, Image> images = new Dictionary<string, Image>();
        private Image backgroundImage;

        // ── Brushes / fonts (Animal Home Game colour palette) ────────────
        private readonly Font titleFont = new Font("Segoe UI", 13f, FontStyle.Bold);
        private readonly Font labelFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font smallFont = new Font("Segoe UI", 9f);
        private readonly SolidBrush white = new SolidBrush(Color.White);
        private readonly SolidBrush darkBg = new SolidBrush(Color.FromArgb(15, 20, 50));
        private readonly SolidBrush cursorBrush = new SolidBrush(Color.FromArgb(160, 80, 200));
        private readonly Pen trailPen = new Pen(Color.FromArgb(100, 160, 255), 2);

        // ─────────────────────────────────────────────────────────────────
        public TuioDemo(int port)
        {
            this.Text = "Animal Home Game — TuioDemo";
            this.ClientSize = new Size(window_width, window_height);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(15, 20, 50);

            // Enable double-buffered flicker-free painting (same as original demo)
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.DoubleBuffer, true);

            width = window_width;
            height = window_height;

            this.Closing += new CancelEventHandler(Form_Closing);
            this.KeyDown += new KeyEventHandler(Form_KeyDown);
            this.Resize += (s, e) =>
            {
                width = this.ClientSize.Width;
                height = this.ClientSize.Height;
                RecalcLayout();
            };

            LoadImages();
            RecalcLayout();

            // Start TUIO (original demo pattern)
            client = new TuioClient(port);
            client.addTuioListener(this);
            client.connect();
        }

        // ── Layout ───────────────────────────────────────────────────────
        // Calculates where each animal and home sits on screen.
        // Called once at startup and again whenever the window is resized.
        private void RecalcLayout()
        {
            int startY = 100;
            int spacingY = 130;
            int leftX = 40;
            int rightX = width - 40 - ITEM_W;

            for (int i = 0; i < AnimalDefs.Length; i++)
            {
                int tuioId = AnimalDefs[i].tuioId;
                PointF origin = new PointF(leftX, startY + i * spacingY);

                animalOrigins[tuioId] = origin;

                // Only reset position if the animal hasn't been matched yet
                bool matched = animalMatched.ContainsKey(tuioId) && animalMatched[tuioId];
                if (!matched)
                    animalPositions[tuioId] = origin;

                if (!animalMatched.ContainsKey(tuioId))
                    animalMatched[tuioId] = false;
            }

            homeRects.Clear();
            for (int i = 0; i < HomeDefs.Length; i++)
            {
                string name = HomeDefs[i].name;
                float x = rightX;
                float y = startY + i * spacingY;
                homeRects[name] = new RectangleF(x, y, ITEM_W, ITEM_H);
            }
        }

        // ── Image loading ─────────────────────────────────────────────────
        // Looks in the Assets folder next to the executable first,
        // then falls back to the project source tree (useful in Visual Studio).
        private void LoadImages()
        {
            string[] searchRoots =
            {
                Path.Combine(Application.StartupPath, "Assets"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets"),
            };

            var fileNames = new List<string>();
            foreach (var a in AnimalDefs)
                if (!fileNames.Contains(a.image)) fileNames.Add(a.image);
            foreach (var h in HomeDefs)
                if (!fileNames.Contains(h.image)) fileNames.Add(h.image);
            fileNames.Add("background.jpeg");

            foreach (string fname in fileNames)
            {
                foreach (string root in searchRoots)
                {
                    string full = Path.Combine(root, fname);
                    if (File.Exists(full))
                    {
                        try { images[fname] = Image.FromFile(full); }
                        catch { /* skip unreadable files */ }
                        break;
                    }
                }
            }

            images.TryGetValue("background.jpeg", out backgroundImage);
        }

        // ── Keyboard handler (identical behaviour to original demo) ───────
        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.F1)
            {
                if (!fullscreen)
                {
                    width = screen_width;
                    height = screen_height;
                    window_left = this.Left;
                    window_top = this.Top;
                    this.FormBorderStyle = FormBorderStyle.None;
                    this.Left = 0; this.Top = 0;
                    this.Width = screen_width; this.Height = screen_height;
                    fullscreen = true;
                }
                else
                {
                    width = window_width;
                    height = window_height;
                    this.FormBorderStyle = FormBorderStyle.Sizable;
                    this.Left = window_left; this.Top = window_top;
                    this.Width = window_width; this.Height = window_height;
                    fullscreen = false;
                }
                RecalcLayout();
            }
            else if (e.KeyData == Keys.Escape) this.Close();
            else if (e.KeyData == Keys.V) verbose = !verbose;
        }

        // ── Form close handler (identical to original demo) ───────────────
        private void Form_Closing(object sender, CancelEventArgs e)
        {
            client.removeTuioListener(this);
            client.disconnect();
            System.Environment.Exit(0);
        }

        // ═════════════════════════════════════════════════════════════════
        //   TuioListener — original demo methods + game logic layered on top
        // ═════════════════════════════════════════════════════════════════

        public void addTuioObject(TuioObject o)
        {
            lock (objectList) objectList.Add(o.SessionID, o);

            if (verbose)
                Console.WriteLine("add obj " + o.SymbolID + " (" + o.SessionID + ") "
                    + o.X + " " + o.Y + " " + o.Angle);

            // Game: if this marker ID matches an animal, start tracking it
            foreach (var def in AnimalDefs)
            {
                if (def.tuioId == o.getSymbolID() && !animalMatched[def.tuioId])
                {
                    sessionToAnimal[o.SessionID] = def.tuioId;
                    SetFeedback("✅ Marker #" + def.tuioId + " grabbed " + def.name + ". Move it to its home!", Color.DarkGreen);
                    break;
                }
            }
        }

        public void updateTuioObject(TuioObject o)
        {
            if (verbose)
                Console.WriteLine("set obj " + o.SymbolID + " " + o.SessionID + " "
                    + o.X + " " + o.Y + " " + o.Angle
                    + " " + o.MotionSpeed + " " + o.RotationSpeed
                    + " " + o.MotionAccel + " " + o.RotationAccel);

            // Game: move the animal to follow the marker position
            if (sessionToAnimal.TryGetValue(o.SessionID, out int tuioId))
            {
                float sx = o.getX() * width - ITEM_W / 2f;
                float sy = o.getY() * height - ITEM_H / 2f;
                animalPositions[tuioId] = new PointF(sx, sy);
            }
        }

        public void removeTuioObject(TuioObject o)
        {
            lock (objectList) objectList.Remove(o.SessionID);

            if (verbose)
                Console.WriteLine("del obj " + o.SymbolID + " (" + o.SessionID + ")");

            // Game: marker was lifted — try to snap to home or return to origin
            if (sessionToAnimal.TryGetValue(o.SessionID, out int tuioId))
            {
                sessionToAnimal.Remove(o.SessionID);
                TrySnapOrReturn(tuioId);
            }
        }

        public void addTuioCursor(TuioCursor c)
        {
            lock (cursorList) cursorList.Add(c.SessionID, c);
            if (verbose) Console.WriteLine("add cur " + c.CursorID + " (" + c.SessionID + ") " + c.X + " " + c.Y);
        }

        public void updateTuioCursor(TuioCursor c)
        {
            if (verbose) Console.WriteLine("set cur " + c.CursorID + " (" + c.SessionID + ") " + c.X + " " + c.Y
                + " " + c.MotionSpeed + " " + c.MotionAccel);
        }

        public void removeTuioCursor(TuioCursor c)
        {
            lock (cursorList) cursorList.Remove(c.SessionID);
            if (verbose) Console.WriteLine("del cur " + c.CursorID + " (" + c.SessionID + ")");
        }

        public void addTuioBlob(TuioBlob b)
        {
            lock (blobList) blobList.Add(b.SessionID, b);
            if (verbose) Console.WriteLine("add blb " + b.BlobID + " (" + b.SessionID + ") "
                + b.X + " " + b.Y + " " + b.Angle + " " + b.Width + " " + b.Height + " " + b.Area);
        }

        public void updateTuioBlob(TuioBlob b)
        {
            if (verbose) Console.WriteLine("set blb " + b.BlobID + " (" + b.SessionID + ") "
                + b.X + " " + b.Y + " " + b.Angle + " " + b.Width + " " + b.Height + " " + b.Area
                + " " + b.MotionSpeed + " " + b.RotationSpeed + " " + b.MotionAccel + " " + b.RotationAccel);
        }

        public void removeTuioBlob(TuioBlob b)
        {
            lock (blobList) blobList.Remove(b.SessionID);
            if (verbose) Console.WriteLine("del blb " + b.BlobID + " (" + b.SessionID + ")");
        }

        // refresh() triggers a repaint — same as original demo
        public void refresh(TuioTime frameTime)
        {
            Invalidate();
        }

        // ═════════════════════════════════════════════════════════════════
        //   Game Logic
        // ═════════════════════════════════════════════════════════════════

        // When a marker is lifted, check if the animal overlaps its correct home.
        // If yes, snap it into place. If no, send it back to its starting spot.
        private void TrySnapOrReturn(int tuioId)
        {
            string animalName = "";
            string targetHome = "";
            foreach (var def in AnimalDefs)
            {
                if (def.tuioId == tuioId) { animalName = def.name; targetHome = def.home; break; }
            }

            RectangleF animalRect = new RectangleF(animalPositions[tuioId], new SizeF(ITEM_W, ITEM_H));

            if (homeRects.TryGetValue(targetHome, out RectangleF homeRect)
                && animalRect.IntersectsWith(homeRect))
            {
                // Centre the animal inside the home
                float snapX = homeRect.X + (homeRect.Width - ITEM_W) / 2f;
                float snapY = homeRect.Y + (homeRect.Height - ITEM_H) / 2f;
                animalPositions[tuioId] = new PointF(snapX, snapY);
                animalMatched[tuioId] = true;
                SetFeedback("🎉 " + animalName + " is home!", Color.Gold);
                CheckWinCondition();
            }
            else
            {
                animalPositions[tuioId] = animalOrigins[tuioId];
                SetFeedback("❌ Wrong home! " + animalName + " returned to start.", Color.OrangeRed);
            }
        }

        // If every animal is matched, the player wins.
        private void CheckWinCondition()
        {
            foreach (bool matched in animalMatched.Values)
                if (!matched) return;

            SetFeedback("🏆 All animals are home! You win!", Color.LimeGreen);
        }

        private void SetFeedback(string text, Color baseColor)
        {
            feedbackText = text;
            feedbackColor = Color.FromArgb(190, baseColor.R / 3, baseColor.G / 3, baseColor.B / 3);
        }

        // ═════════════════════════════════════════════════════════════════
        //   OnPaintBackground — all drawing lives here (original demo approach)
        // ═════════════════════════════════════════════════════════════════
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Background
            if (backgroundImage != null)
                g.DrawImage(backgroundImage, 0, 0, width, height);
            else
                g.FillRectangle(darkBg, 0, 0, width, height);

            // Dark overlay so text and game items are always readable
            using (SolidBrush overlay = new SolidBrush(Color.FromArgb(110, 10, 15, 40)))
                g.FillRectangle(overlay, 0, 0, width, height);

            // 2. Feedback / HUD bar at the top
            DrawFeedbackBar(g);

            // 3. Column headers
            DrawColumnHeaders(g);

            // 4. Home zones (right column) — drawn first so animals appear on top
            DrawHomes(g);

            // 5. Animals (left column, at their current tracked positions)
            DrawAnimals(g);

            // 6. Cursor trails (original demo logic)
            DrawCursorTrails(g);

            // 7. Status bar at the bottom (live TUIO counters)
            DrawStatusBar(g);
        }

        // ── Individual drawing helpers ────────────────────────────────────

        private void DrawFeedbackBar(Graphics g)
        {
            int barW = 720, barH = 42;
            int barX = (width - barW) / 2;
            int barY = 8;

            using (SolidBrush bg = new SolidBrush(feedbackColor))
                g.FillRectangle(bg, barX, barY, barW, barH);

            using (Pen border = new Pen(Color.FromArgb(80, 255, 255, 255), 1))
                g.DrawRectangle(border, barX, barY, barW, barH);

            StringFormat centre = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(feedbackText, titleFont, white, new RectangleF(barX, barY, barW, barH), centre);
        }

        private void DrawColumnHeaders(Graphics g)
        {
            int leftX = 40;
            int rightX = width - 40 - ITEM_W;
            StringFormat centre = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using (SolidBrush hdrBg = new SolidBrush(Color.FromArgb(140, 20, 20, 60)))
            {
                g.FillRectangle(hdrBg, leftX - 5, 60, ITEM_W + 10, 28);
                g.FillRectangle(hdrBg, rightX - 5, 60, ITEM_W + 10, 28);
            }

            g.DrawString("ANIMALS", labelFont, white,
                new RectangleF(leftX - 5, 60, ITEM_W + 10, 28), centre);
            g.DrawString("HOMES", labelFont, white,
                new RectangleF(rightX - 5, 60, ITEM_W + 10, 28), centre);
        }

        private void DrawHomes(Graphics g)
        {
            StringFormat centre = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            foreach (var def in HomeDefs)
            {
                if (!homeRects.TryGetValue(def.name, out RectangleF rect)) continue;

                // Image or fallback colour
                if (images.TryGetValue(def.image, out Image img))
                    g.DrawImage(img, rect);
                else
                {
                    using (SolidBrush fb = new SolidBrush(Color.DimGray))
                        g.FillRectangle(fb, rect);
                }

                // Gold border (Fixed3D styling equivalent)
                using (Pen border = new Pen(Color.FromArgb(200, 255, 220, 80), 2))
                    g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);

                // Name label above the home image
                RectangleF labelRect = new RectangleF(rect.X, rect.Y - 22, rect.Width, 20);
                using (SolidBrush lbg = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                    g.FillRectangle(lbg, labelRect);
                g.DrawString(def.name, smallFont, white, labelRect, centre);
            }
        }

        private void DrawAnimals(Graphics g)
        {
            StringFormat centre = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            foreach (var def in AnimalDefs)
            {
                if (!animalPositions.TryGetValue(def.tuioId, out PointF pos)) continue;
                bool matched = animalMatched.ContainsKey(def.tuioId) && animalMatched[def.tuioId];
                bool grabbed = sessionToAnimal.ContainsValue(def.tuioId);

                RectangleF rect = new RectangleF(pos.X, pos.Y, ITEM_W, ITEM_H);

                // Image or fallback colour
                if (images.TryGetValue(def.image, out Image img))
                    g.DrawImage(img, rect);
                else
                {
                    using (SolidBrush fb = new SolidBrush(Color.LightGray))
                        g.FillRectangle(fb, rect);
                }

                // Border: gold = matched, cyan = being grabbed, white = idle
                Color borderColor = matched ? Color.Gold
                                  : grabbed ? Color.Cyan
                                  : Color.White;
                float borderWidth = matched || grabbed ? 3f : 1f;
                using (Pen border = new Pen(borderColor, borderWidth))
                    g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);

                // Glow behind the grabbed animal so it visually "pops"
                if (grabbed)
                {
                    using (Pen glow = new Pen(Color.FromArgb(60, 0, 220, 255), 8))
                        g.DrawRectangle(glow, rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8);
                }

                // Name + marker ID label above the animal image
                RectangleF labelRect = new RectangleF(rect.X, rect.Y - 22, rect.Width, 20);
                using (SolidBrush lbg = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                    g.FillRectangle(lbg, labelRect);
                g.DrawString(def.name + "  [#" + def.tuioId + "]", smallFont, white, labelRect, centre);
            }
        }

        // Draws cursor motion trails exactly as the original demo does
        private void DrawCursorTrails(Graphics g)
        {
            if (cursorList.Count == 0) return;

            lock (cursorList)
            {
                foreach (TuioCursor tcur in cursorList.Values)
                {
                    List<TuioPoint> path = tcur.Path;
                    TuioPoint current = path[0];

                    for (int i = 0; i < path.Count; i++)
                    {
                        TuioPoint next = path[i];
                        g.DrawLine(trailPen,
                            current.getScreenX(width), current.getScreenY(height),
                            next.getScreenX(width), next.getScreenY(height));
                        current = next;
                    }

                    int r = height / 100;
                    g.FillEllipse(cursorBrush,
                        current.getScreenX(width) - r,
                        current.getScreenY(height) - r,
                        r * 2, r * 2);

                    g.DrawString(tcur.CursorID.ToString(), labelFont, white,
                        new PointF(tcur.getScreenX(width) - 10, tcur.getScreenY(height) - 10));
                }
            }
        }

        private void DrawStatusBar(Graphics g)
        {
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(170, 10, 10, 30)))
                g.FillRectangle(bg, 0, height - 28, width, 28);

            int matched = 0;
            foreach (bool v in animalMatched.Values) if (v) matched++;

            string stats =
                $"Objects: {objectList.Count}   Cursors: {cursorList.Count}   Blobs: {blobList.Count}"
                + $"   |   Matched: {matched}/{AnimalDefs.Length}"
                + $"   |   Verbose: {(verbose ? "ON" : "OFF")}"
                + $"   |   F1 = Fullscreen   V = Verbose   Esc = Exit";

            g.DrawString(stats, smallFont, white, new PointF(10, height - 20));
        }

        // ── Required by Windows Forms designer ───────────────────────────
        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new Size(window_width, window_height);
            this.Name = "TuioDemo";
            this.ResumeLayout(false);
        }

        // ── Entry point (same argument handling as original demo) ─────────
        public static void Main(string[] argv)
        {
            int port;
            switch (argv.Length)
            {
                case 1:
                    port = int.Parse(argv[0], null);
                    if (port == 0) goto default;
                    break;
                case 0:
                    port = 3333;
                    break;
                default:
                    Console.WriteLine("usage: TuioDemo [port]");
                    System.Environment.Exit(0);
                    return;
            }

            Application.Run(new TuioDemo(port));
        }
    }
}
