using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TUIO;

namespace AnimalHomeGame_CSharp
{
    public class TuioDemo : Form, TuioListener
    {
        private enum GameState
        {
            WaitingForLogin,
            Playing
        }

        private GameState currentState = GameState.WaitingForLogin;
        private TuioClient client;
        private Client socketClient;

        // --- NEW MENU STATE VARIABLES ---
        private bool isMenuOpen = false;
        private string hoveredSlice = "";
        private string hintedHome = ""; // Tracks which home should glow

        private Dictionary<long, TuioObject> objectList = new Dictionary<long, TuioObject>(128);
        private Dictionary<long, TuioCursor> cursorList = new Dictionary<long, TuioCursor>(128);
        private Dictionary<long, TuioBlob> blobList = new Dictionary<long, TuioBlob>(128);

        public static int width, height;
        private int window_width = 1024;
        private int window_height = 768;
        private int window_left = 0;
        private int window_top = 0;
        private int screen_width = Screen.PrimaryScreen.Bounds.Width;
        private int screen_height = Screen.PrimaryScreen.Bounds.Height;
        private bool fullscreen = false;
        private bool verbose = false;

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

        private Dictionary<int, PointF> animalPositions = new Dictionary<int, PointF>();
        private Dictionary<int, float> animalAngles = new Dictionary<int, float>();
        private Dictionary<int, PointF> animalOrigins = new Dictionary<int, PointF>();
        private Dictionary<int, bool> animalMatched = new Dictionary<int, bool>();
        private Dictionary<long, int> sessionToAnimal = new Dictionary<long, int>();
        private Dictionary<string, RectangleF> homeRects = new Dictionary<string, RectangleF>();

        private const int ITEM_W = 110;
        private const int ITEM_H = 100;

        private string feedbackText = "Place a TUIO marker on an animal and move it to its home!";
        private Color feedbackColor = Color.FromArgb(160, 20, 20, 40);

        private Dictionary<string, Image> images = new Dictionary<string, Image>();
        private Image backgroundImage;

        private readonly Font titleFont = new Font("Segoe UI", 13f, FontStyle.Bold);
        private readonly Font labelFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font smallFont = new Font("Segoe UI", 9f);
        private readonly SolidBrush white = new SolidBrush(Color.White);
        private readonly SolidBrush darkBg = new SolidBrush(Color.FromArgb(15, 20, 50));
        private readonly SolidBrush cursorBrush = new SolidBrush(Color.FromArgb(160, 80, 200));
        private readonly Pen trailPen = new Pen(Color.FromArgb(100, 160, 255), 2);

        public TuioDemo(int port)
        {
            this.Text = "Animal Home Game — TuioDemo";
            this.ClientSize = new Size(window_width, window_height);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(15, 20, 50);

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

            client = new TuioClient(port);
            client.addTuioListener(this);
            client.connect();

            Task.Run(() => stream());
        }

        public void stream()
        {
            socketClient = new Client();
            System.Threading.Thread.Sleep(1000);

            if (socketClient.connectToSocket("localhost", 5000))
            {
                while (true)
                {
                    string msg = socketClient.receiveMessage();

                    if (msg == "q" || msg == null)
                    {
                        if (socketClient.stream != null) socketClient.stream.Close();
                        if (socketClient.client != null) socketClient.client.Close();
                        Console.WriteLine("Connection Terminated!");

                        this.Invoke(new Action(() => {
                            currentState = GameState.WaitingForLogin;
                            isMenuOpen = false;
                            Invalidate();
                        }));
                        break;
                    }
                    else if (!string.IsNullOrEmpty(msg))
                    {
                        this.Invoke(new Action(() => {
                            // Split by newline in case Python sends multiple messages at the exact same millisecond
                            string[] commands = msg.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                            foreach (string cmd in commands)
                            {
                                ProcessSocketCommand(cmd);
                            }
                            Invalidate();
                        }));
                    }
                }
            }
        }

        // --- NEW: Parses the logic from Python ---
        private void ProcessSocketCommand(string cmd)
        {
            if (cmd.StartsWith("LOGIN:"))
            {
                string username = cmd.Substring(6);
                currentState = GameState.Playing;
                SetFeedback("👋 Welcome, " + username + "! Raise an open hand to view the menu.", Color.DeepSkyBlue);
            }
            else if (cmd.StartsWith("OPEN_MENU:"))
            {
                isMenuOpen = true;
                hoveredSlice = cmd.Substring(10);
            }
            else if (cmd.StartsWith("HOVER:"))
            {
                hoveredSlice = cmd.Substring(6);
            }
            else if (cmd == "CANCEL")
            {
                isMenuOpen = false;
            }
            else if (cmd.StartsWith("SELECT:"))
            {
                string selection = cmd.Substring(7);
                isMenuOpen = false;

                if (selection == "Restart") ResetGame();
                else if (selection == "Logout") PerformLogout();
                else if (selection == "Hint") ShowHint();
            }
        }

        // --- NEW: Action Methods ---
        private void ResetGame()
        {
            foreach (var def in AnimalDefs)
            {
                animalPositions[def.tuioId] = animalOrigins[def.tuioId];
                animalAngles[def.tuioId] = 0f;
                animalMatched[def.tuioId] = false;
            }
            sessionToAnimal.Clear();
            SetFeedback("🔄 Game Restarted! Match the animals to their homes.", Color.DodgerBlue);
        }

        private void PerformLogout()
        {
            currentState = GameState.WaitingForLogin;
            ResetGame();
            if (socketClient != null)
            {
                socketClient.sendMessage("LOGOUT\n");
            }
        }

        private async void ShowHint()
        {
            string target = "";
            foreach (var def in AnimalDefs)
            {
                if (!animalMatched.ContainsKey(def.tuioId) || !animalMatched[def.tuioId])
                {
                    target = def.home;
                    break;
                }
            }

            if (target != "")
            {
                hintedHome = target;
                SetFeedback($"💡 Hint: Look for the {target}!", Color.Goldenrod);
                Invalidate();

                // Keep the glow on for 4 seconds
                await Task.Delay(4000);

                if (hintedHome == target)
                {
                    hintedHome = "";
                    Invalidate();
                }
            }
            else
            {
                SetFeedback("You've already matched them all!", Color.LimeGreen);
            }
        }

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

                bool matched = animalMatched.ContainsKey(tuioId) && animalMatched[tuioId];
                if (!matched)
                {
                    animalPositions[tuioId] = origin;
                    animalAngles[tuioId] = 0f;
                }

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
                        catch { }
                        break;
                    }
                }
            }

            images.TryGetValue("background.jpeg", out backgroundImage);
        }

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
            else if (e.KeyData == Keys.L)
            {
                if (currentState == GameState.Playing) PerformLogout();
            }
        }

        private void Form_Closing(object sender, CancelEventArgs e)
        {
            client.removeTuioListener(this);
            client.disconnect();
            System.Environment.Exit(0);
        }

        public void addTuioObject(TuioObject o)
        {
            lock (objectList) objectList.Add(o.SessionID, o);

            if (currentState != GameState.Playing || isMenuOpen) return;

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
            if (currentState != GameState.Playing || isMenuOpen) return;

            if (sessionToAnimal.TryGetValue(o.SessionID, out int tuioId))
            {
                float sx = o.getX() * width - ITEM_W / 2f;
                float sy = o.getY() * height - ITEM_H / 2f;
                animalPositions[tuioId] = new PointF(sx, sy);
                animalAngles[tuioId] = (float)(o.Angle * 180.0 / Math.PI);
            }
        }

        public void removeTuioObject(TuioObject o)
        {
            lock (objectList) objectList.Remove(o.SessionID);

            if (currentState != GameState.Playing || isMenuOpen) return;

            if (sessionToAnimal.TryGetValue(o.SessionID, out int tuioId))
            {
                sessionToAnimal.Remove(o.SessionID);
                TrySnapOrReturn(tuioId);
            }
        }

        public void addTuioCursor(TuioCursor c)
        {
            lock (cursorList) cursorList.Add(c.SessionID, c);
        }

        public void updateTuioCursor(TuioCursor c)
        {
        }

        public void removeTuioCursor(TuioCursor c)
        {
            lock (cursorList) cursorList.Remove(c.SessionID);
        }

        public void addTuioBlob(TuioBlob b)
        {
            lock (blobList) blobList.Add(b.SessionID, b);
        }

        public void updateTuioBlob(TuioBlob b)
        {
        }

        public void removeTuioBlob(TuioBlob b)
        {
            lock (blobList) blobList.Remove(b.SessionID);
        }

        public void refresh(TuioTime frameTime)
        {
            Invalidate();
        }

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
                float snapX = homeRect.X + (homeRect.Width - ITEM_W) / 2f;
                float snapY = homeRect.Y + (homeRect.Height - ITEM_H) / 2f;
                animalPositions[tuioId] = new PointF(snapX, snapY);
                animalAngles[tuioId] = 0f;
                animalMatched[tuioId] = true;

                // Clear the hint if they successfully matched it!
                if (hintedHome == targetHome) hintedHome = "";

                SetFeedback("🎉 " + animalName + " is home!", Color.Gold);
                CheckWinCondition();
            }
            else
            {
                animalPositions[tuioId] = animalOrigins[tuioId];
                animalAngles[tuioId] = 0f;
                SetFeedback("❌ Wrong home! " + animalName + " returned to start.", Color.OrangeRed);
            }
        }

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

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (showMenu)
            {
                // Dummy values to display the menu for testing
                menuRenderer.Draw(g, width, height,
                    currentSelection: 0,
                    rotationAngle: 45f,
                    navPresent: true,
                    selectPresent: true,
                    menuNavPos: new PointF(width / 2f - 60, height / 2f + 50),
                    menuSelectPos: new PointF(width / 2f + 80, height / 2f + 20),
                    confirmProximity: 0.75f,
                    MENU_NAV_ID: 99,
                    MENU_SELECT_ID: 100);
            }
            else if (currentState == GameState.WaitingForLogin)
            {
                DrawLandingPage(g);
            }
            else if (currentState == GameState.Playing)
            {
                if (backgroundImage != null)
                    g.DrawImage(backgroundImage, 0, 0, width, height);
                else
                    g.FillRectangle(darkBg, 0, 0, width, height);

                using (SolidBrush overlay = new SolidBrush(Color.FromArgb(110, 10, 15, 40)))
                    g.FillRectangle(overlay, 0, 0, width, height);

                DrawFeedbackBar(g);
                DrawColumnHeaders(g);
                DrawHomes(g);
                DrawAnimals(g);
                DrawCursorTrails(g);
                DrawStatusBar(g);

                // --- NEW: Draw the Menu ON TOP of everything if it's open ---
                if (isMenuOpen)
                {
                    DrawPieMenu(g);
                }
            }
        }

        private void DrawLandingPage(Graphics g)
        {
            g.FillRectangle(darkBg, 0, 0, width, height);

            StringFormat centre = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using (Font hugeFont = new Font("Segoe UI", 32f, FontStyle.Bold))
            {
                g.DrawString("ANIMAL HOME GAME", hugeFont, white, new RectangleF(0, height / 2 - 150, width, 60), centre);
            }

            using (Font statusFont = new Font("Segoe UI", 16f, FontStyle.Bold))
            {
                g.DrawString("Awaiting Bluetooth Sign-In...", statusFont, Brushes.DeepSkyBlue, new RectangleF(0, height / 2 - 50, width, 40), centre);
            }

            using (Font instrFont = new Font("Segoe UI", 12f))
            {
                int yOffset = height / 2 + 20;
                g.DrawString("1. Step closer to the table to log in automatically.", instrFont, Brushes.LightGray, new RectangleF(0, yOffset, width, 30), centre);
                g.DrawString("2. Match the animals to their correct homes.", instrFont, Brushes.LightGray, new RectangleF(0, yOffset + 35, width, 30), centre);
                g.DrawString("3. Raise an OPEN HAND to view the pause menu.", instrFont, Brushes.Gold, new RectangleF(0, yOffset + 70, width, 30), centre);
            }

            DrawCursorTrails(g);
            DrawStatusBar(g);
        }

        // --- NEW: Render the Hand Tracking Menu ---
        private void DrawPieMenu(Graphics g)
        {
            // Dim the background while the menu is open
            using (SolidBrush overlay = new SolidBrush(Color.FromArgb(200, 10, 10, 15)))
                g.FillRectangle(overlay, 0, 0, width, height);

            int cx = width / 2;
            int cy = height / 2;
            int baseRadius = 200;

            // Draw the three slices. (Start Angle, Sweep Angle)
            // C# angles start at 0 (Right) and go clockwise.
            // Top Slice: Starts at 210, sweeps 120 (goes to 330)
            DrawSlice(g, "Hint", "💡 Get Hint", 210, 120, cx, cy, baseRadius, Color.FromArgb(220, 180, 40));
            // Right Slice: Starts at 330, sweeps 120 (goes to 90)
            DrawSlice(g, "Logout", "🚪 Log Out", 330, 120, cx, cy, baseRadius, Color.FromArgb(200, 50, 60));
            // Left Slice: Starts at 90, sweeps 120 (goes to 210)
            DrawSlice(g, "Restart", "🔄 Restart", 90, 120, cx, cy, baseRadius, Color.FromArgb(50, 120, 200));

            // Draw the donut hole in the center
            int holeRadius = 65;
            using (SolidBrush holeBrush = new SolidBrush(Color.FromArgb(20, 25, 45)))
                g.FillEllipse(holeBrush, cx - holeRadius, cy - holeRadius, holeRadius * 2, holeRadius * 2);

            // Draw instruction text in the middle
            StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (Font f = new Font("Segoe UI", 10f, FontStyle.Bold))
                g.DrawString("MAKE FIST\nTO SELECT", f, Brushes.LightGray, cx, cy, format);
        }

        private void DrawSlice(Graphics g, string sliceId, string label, float startAngle, float sweepAngle, int cx, int cy, int baseRadius, Color baseColor)
        {
            bool isHovered = (hoveredSlice == sliceId);

            // Pop out the slice if hovered
            int r = isHovered ? baseRadius + 30 : baseRadius;
            Color c = isHovered ? ControlPaint.Light(baseColor, 0.3f) : baseColor;

            Rectangle rect = new Rectangle(cx - r, cy - r, r * 2, r * 2);

            using (SolidBrush brush = new SolidBrush(c))
                g.FillPie(brush, rect, startAngle, sweepAngle);

            using (Pen borderPen = new Pen(Color.FromArgb(15, 20, 50), 5))
                g.DrawPie(borderPen, rect, startAngle, sweepAngle);

            // Calculate exact center of the slice to draw the text
            double rad = (startAngle + sweepAngle / 2) * Math.PI / 180.0;
            float textX = cx + (float)(Math.Cos(rad) * (r * 0.65));
            float textY = cy + (float)(Math.Sin(rad) * (r * 0.65));

            StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (Font f = new Font("Segoe UI", 14f, FontStyle.Bold))
                g.DrawString(label, f, Brushes.White, textX, textY, format);
        }

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

                if (images.TryGetValue(def.image, out Image img))
                    g.DrawImage(img, rect);
                else
                {
                    using (SolidBrush fb = new SolidBrush(Color.DimGray))
                        g.FillRectangle(fb, rect);
                }

                // --- NEW: Give the target home a massive golden glow if HINT is active ---
                if (hintedHome == def.name)
                {
                    using (Pen glow = new Pen(Color.Gold, 6))
                        g.DrawRectangle(glow, rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6);
                }
                else
                {
                    using (Pen border = new Pen(Color.FromArgb(200, 255, 220, 80), 2))
                        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                }

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
                float angle = animalAngles.ContainsKey(def.tuioId) ? animalAngles[def.tuioId] : 0f;

                GraphicsState state = g.Save();

                float cx = rect.X + rect.Width / 2f;
                float cy = rect.Y + rect.Height / 2f;
                g.TranslateTransform(cx, cy);
                g.RotateTransform(angle);
                g.TranslateTransform(-cx, -cy);

                if (images.TryGetValue(def.image, out Image img))
                    g.DrawImage(img, rect);
                else
                {
                    using (SolidBrush fb = new SolidBrush(Color.LightGray))
                        g.FillRectangle(fb, rect);
                }

                Color borderColor = matched ? Color.Gold
                                  : grabbed ? Color.Cyan
                                  : Color.White;
                float borderWidth = matched || grabbed ? 3f : 1f;
                using (Pen border = new Pen(borderColor, borderWidth))
                    g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);

                if (grabbed)
                {
                    using (Pen glow = new Pen(Color.FromArgb(60, 0, 220, 255), 8))
                        g.DrawRectangle(glow, rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8);
                }

                g.Restore(state);

                RectangleF labelRect = new RectangleF(rect.X, rect.Y - 22, rect.Width, 20);
                using (SolidBrush lbg = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                    g.FillRectangle(lbg, labelRect);
                g.DrawString(def.name + "  [#" + def.tuioId + "]", smallFont, white, labelRect, centre);
            }
        }

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
                + $"   |   F1 = Full   V = Verbose   L = Log Out   Esc = Exit";

            g.DrawString(stats, smallFont, white, new PointF(10, height - 20));
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new Size(window_width, window_height);
            this.Name = "TuioDemo";
            this.ResumeLayout(false);
        }

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

    class Client
    {
        public NetworkStream stream;
        public TcpClient client;

        public bool connectToSocket(string host, int portNumber)
        {
            try
            {
                client = new TcpClient(host, portNumber);
                stream = client.GetStream();
                Console.WriteLine("Connection made! with " + host);
                return true;
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Console.WriteLine("Connection Failed: " + e.Message);
                return false;
            }
        }

        public string receiveMessage()
        {
            try
            {
                byte[] receiveBuffer = new byte[1024];
                int bytesReceived = stream.Read(receiveBuffer, 0, 1024);
                if (bytesReceived > 0)
                {
                    string data = Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived);
                    return data;
                }
                else if (bytesReceived == 0)
                {
                    // 0 bytes means the Python server closed the connection.
                    return "q"; 
                }
            }
            catch (System.Exception e)
            {
                Console.WriteLine("Receive error: " + e.Message);
                return "q"; // Also quit on error so it doesn't loop infinitely
            }

            return null;
        }

        public void sendMessage(string message)
        {
            try
            {
                if (stream != null && client.Connected)
                {
                    byte[] sendBuffer = Encoding.UTF8.GetBytes(message);
                    stream.Write(sendBuffer, 0, sendBuffer.Length);
                }
            }
            catch (System.Exception e)
            {
                Console.WriteLine("Send error: " + e.Message);
            }
        }
    }
}