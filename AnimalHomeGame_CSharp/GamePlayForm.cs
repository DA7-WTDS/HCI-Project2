using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Net;

namespace AnimalHomeGame_CSharp;

public class GamePlayForm : Form
{
    private readonly UserProfile currentUser;
    private readonly MainForm parentScanner;
    private TuioHandler tuioHandler;

    // ── Emotion / Gaze / Lighting listener ────────────────────────────────
    private UdpClient? udpClient;
    private bool isListeningEmotions = false;
    private Label emotionLabel = null!;
    private Label gazeLabel = null!;
    private Label lightingLabel = null!;
    private string currentGaze = "Center";
    private string currentLighting = "bright";
    private bool isNightMode = false;

    // ── Gaze highlight ────────────────────────────────────────────────────
    private bool gazeHighlightActive = false;   // are highlights currently on?
    private static readonly Color GazeHighlightColor = Color.Yellow;

    // ── Gaze heatmap ─────────────────────────────────────────────────────
    // Downscaled 4x relative to the 1024×768 client area → 256×192 cells
    private const int HeatW = 256;
    private const int HeatH = 192;
    private readonly float[,] gazeHeat = new float[HeatW, HeatH];
    private float gazeHeatMax = 1f;  // running max for normalisation

    // ── Hand Menu listener ────────────────────────────────────────────────
    private UdpClient? handMenuUdpClient;
    private bool isListeningHandMenu = false;

    // ── Hand Menu visual overlay (single double-buffered pie panel) ───────
    private Panel handMenuOverlay = null!;
    private string currentHoveredOption = "";

    // ── YOLO listener (NEW) ───────────────────────────────────────────────
    private UdpClient? yoloUdpClient;
    private bool isListeningYolo = false;

    // ── Input-source tracking per animal (NEW) ────────────────────────────
    // Stores the last source that grabbed each animal: "TUIO", "YOLO", or "Mouse"
    private readonly Dictionary<int, string> animalInputSource = new();
    // One small badge label per animal, keyed by tuioId
    private readonly Dictionary<int, Label> inputSourceBadge = new();

    private const int LOGOUT_MARKER_ID = 5;

    private readonly Dictionary<int, GameItem> animalById = new();
    private readonly List<GameItem> homes = new();
    private readonly Dictionary<int, GameItem> grabbedAnimals = new();
    private GameItem? mouseDragItem = null;
    private Point mouseOffset;

    private Label feedbackLabel = null!;
    private Label debugLabel = null!;

    private static readonly (string name, int tuioId, string image, string home)[] AnimalDefs =
    {
        ("Bird",  0, "bird.jpeg",  "Nest"),
        ("Dog",   1, "dog.jpeg",   "Doghouse"),
        ("Fish",  2, "fish.jpeg",  "Water"),
        ("Cow",  3, "cow.jpeg",  "COW"),
    };

    private static readonly (string name, string image)[] HomeDefs =
    {
        ("Nest",     "nest.jpeg"),
        ("Doghouse", "doghouse.jpeg"),
        ("Water",    "water.jpeg"),
        ("Farm",     "farm.jpeg"),
    };

    public GamePlayForm(UserProfile profile, MainForm mainForm)
    {
        currentUser = profile;
        parentScanner = mainForm;
        tuioHandler = new TuioHandler();
        InitializeComponent();
        SetupGUI();
        SetupTuio();
        SetupEmotionListener();
        SetupYoloListener();
        SetupHandMenuListener();
    }

    private void SetupGUI()
    {
        this.Text = "Animal Home Game - Playing";
        this.Size = new Size(1024, 768);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormClosed += GamePlayForm_FormClosed;
        this.DoubleBuffered = true;

        string bgPath = GetAssetPath("background.jpeg");
        if (File.Exists(bgPath))
        {
            this.BackgroundImage = Image.FromFile(bgPath);
            this.BackgroundImageLayout = ImageLayout.Stretch;
        }
        else
        {
            this.BackColor = Color.DarkGreen;
        }

        Button backButton = new Button
        {
            Text = "← Back",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            BackColor = Color.FromArgb(180, 80, 80),
            ForeColor = Color.White,
            Size = new Size(110, 40),
            Location = new Point(10, 10),
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        backButton.FlatAppearance.BorderSize = 0;
        backButton.Click += (s, e) => this.Close();
        this.Controls.Add(backButton);

        feedbackLabel = new Label
        {
            Text = "Place the correct TUIO marker on each animal to unlock it!",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(160, 20, 20, 20),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(700, 42),
            Location = new Point((this.ClientSize.Width - 700) / 2, 10),
        };
        this.Controls.Add(feedbackLabel);

        debugLabel = new Label
        {
            Text = "TUIO: waiting...",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.LightGray,
            BackColor = Color.FromArgb(130, 0, 0, 0),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Size = new Size(300, 22),
            Location = new Point(10, this.ClientSize.Height - 30),
        };
        this.Controls.Add(debugLabel);

        emotionLabel = new Label
        {
            Text = "Emotion: None",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.Yellow,
            BackColor = Color.FromArgb(130, 0, 0, 0),
            AutoSize = true,
            Location = new Point(this.ClientSize.Width - 250, 10),
        };
        this.Controls.Add(emotionLabel);

        gazeLabel = new Label
        {
            Text = "Gaze: Center",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.Cyan,
            BackColor = Color.FromArgb(130, 0, 0, 0),
            AutoSize = true,
            Location = new Point(this.ClientSize.Width - 250, 38),
        };
        this.Controls.Add(gazeLabel);

        lightingLabel = new Label
        {
            Text = "Lighting: bright",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.LightSalmon,
            BackColor = Color.FromArgb(130, 0, 0, 0),
            AutoSize = true,
            Location = new Point(this.ClientSize.Width - 250, 62),
        };
        this.Controls.Add(lightingLabel);

        int count    = AnimalDefs.Length;
        int itemHeight = 100;
        int itemWidth  = 110;
        int startY   = 100;
        int spacingY = 130;
        int leftX    = 40;
        int rightX   = 860;

        for (int i = 0; i < count; i++)
        {
            var (name, tuioId, imageFile, targetHome) = AnimalDefs[i];
            int y = startY + i * spacingY;

            Label animalLabel = new Label
            {
                Text = $"{name}  [Marker #{tuioId}]",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(120, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(itemWidth, 20),
                Location = new Point(leftX, y - 22)
            };
            this.Controls.Add(animalLabel);

            PictureBox animalPic = new PictureBox
            {
                Size = new Size(itemWidth, itemHeight),
                Location = new Point(leftX, y),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand,
                Tag = name
            };

            string path = GetAssetPath(imageFile);
            if (File.Exists(path))
                animalPic.Image = Image.FromFile(path);
            else
                animalPic.BackColor = Color.LightGray;

            animalPic.MouseDown += AnimalPic_MouseDown;
            animalPic.MouseMove += AnimalPic_MouseMove;
            animalPic.MouseUp   += AnimalPic_MouseUp;

            GameItem animalItem = new GameItem
            {
                Name = name,
                TuioId = tuioId,
                Picture = animalPic,
                OriginalLocation = new Point(leftX, y),
                TargetHomeName = targetHome,
                IsMatched = false
            };

            animalById[tuioId] = animalItem;
            this.Controls.Add(animalPic);
            animalPic.BringToFront();
            animalLabel.BringToFront();

            // ── NEW: input-source badge ───────────────────────────────────
            // Small pill rendered just below each animal picture.
            // Hidden until an input source grabs the animal.
            Label badge = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(180, 40, 40, 40),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(itemWidth, 16),
                Location = new Point(leftX, y + itemHeight + 2),
                Visible = false
            };
            this.Controls.Add(badge);
            badge.BringToFront();
            inputSourceBadge[tuioId] = badge;
            // ─────────────────────────────────────────────────────────────
        }

        for (int i = 0; i < HomeDefs.Length; i++)
        {
            var (homeName, imageFile) = HomeDefs[i];
            int y = startY + i * spacingY;

            Label homeLabel = new Label
            {
                Text = homeName,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(120, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(itemWidth, 20),
                Location = new Point(rightX, y - 22)
            };
            this.Controls.Add(homeLabel);

            PictureBox homePic = new PictureBox
            {
                Size = new Size(itemWidth, itemHeight),
                Location = new Point(rightX, y),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BorderStyle = BorderStyle.Fixed3D,
                Tag = homeName
            };

            string path = GetAssetPath(imageFile);
            if (File.Exists(path))
                homePic.Image = Image.FromFile(path);
            else
                homePic.BackColor = Color.LightGray;

            GameItem homeItem = new GameItem
            {
                Name = homeName,
                Picture = homePic,
                OriginalLocation = new Point(rightX, y)
            };

            homes.Add(homeItem);
            this.Controls.Add(homePic);
            homeLabel.BringToFront();
        }

        backButton.BringToFront();
        feedbackLabel.BringToFront();
        debugLabel.BringToFront();
        emotionLabel.BringToFront();
        gazeLabel.BringToFront();
        lightingLabel.BringToFront();

        BuildHandMenuOverlay();
    }

    private void BuildHandMenuOverlay()
    {
        // Single double-buffered panel — all pie drawing in one Paint pass.
        // No child controls = no cascading repaints = no stuttering.
        handMenuOverlay = new DoubleBufferedPanel
        {
            Size      = this.ClientSize,
            Location  = Point.Empty,
            BackColor = Color.FromArgb(200, 8, 8, 24),
            Visible   = false,
        };
        handMenuOverlay.Paint += DrawPieMenu;
        this.Controls.Add(handMenuOverlay);
        handMenuOverlay.BringToFront();
    }

    // ── Pie menu draw ─────────────────────────────────────────────────────
    private static readonly (string Key, string Icon, string Label,
        Color Base, float StartAngle)[] PieSlices =
    {
        // Three 120° slices. GDI+ angles: 0=right, clockwise.
        // startAngle=210 puts Hint at the top (centre angle=270°).
        ("Hint",    "💡", "Hint",    Color.FromArgb(255, 210, 170,  20), 210f),
        ("Logout",  "🚪", "Logout",  Color.FromArgb(255,  60, 110, 240), 330f),
        ("Restart", "🔄", "Restart", Color.FromArgb(255, 230,  90,  20),  90f),
    };

    private void DrawPieMenu(object? sender, PaintEventArgs e)
    {
        var g  = e.Graphics;
        g.SmoothingMode      = SmoothingMode.AntiAlias;
        g.TextRenderingHint  = TextRenderingHint.AntiAliasGridFit;

        int cx = handMenuOverlay.Width  / 2;
        int cy = handMenuOverlay.Height / 2;
        int r  = 190;
        var pieRect = new RectangleF(cx - r, cy - r, r * 2, r * 2);

        // ── Draw pie slices ───────────────────────────────────────────────
        foreach (var (key, icon, label, baseColor, startAngle) in PieSlices)
        {
            bool hov = key == currentHoveredOption;
            Color fill = hov
                ? Color.FromArgb(255,
                    Math.Min(255, baseColor.R + 50),
                    Math.Min(255, baseColor.G + 50),
                    Math.Min(255, baseColor.B + 50))
                : Color.FromArgb(190, baseColor.R, baseColor.G, baseColor.B);

            using (var brush = new SolidBrush(fill))
                g.FillPie(brush, pieRect.X, pieRect.Y, pieRect.Width, pieRect.Height,
                          startAngle, 120f);

            using (var pen = new Pen(hov ? Color.Gold : Color.FromArgb(180, 255, 255, 255),
                                     hov ? 4f : 1.5f))
                g.DrawPie(pen, pieRect.X, pieRect.Y, pieRect.Width, pieRect.Height,
                          startAngle, 120f);

            // ── Text label in the middle of each slice ────────────────────
            double midRad = (startAngle + 60.0) * Math.PI / 180.0;
            float  tr     = r * 0.62f;
            float  tx     = cx + tr * (float)Math.Cos(midRad);
            float  ty     = cy + tr * (float)Math.Sin(midRad);

            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            // Icon
            using (var f = new Font("Segoe UI Emoji", 22))
            using (var b = new SolidBrush(Color.White))
                g.DrawString(icon, f, b,
                    new RectangleF(tx - 44, ty - 44, 88, 48), sf);

            // Label
            Color labelCol = hov ? Color.Gold : Color.White;
            using (var f = new Font("Segoe UI", 11, FontStyle.Bold))
            using (var b = new SolidBrush(labelCol))
                g.DrawString(label, f, b,
                    new RectangleF(tx - 50, ty + 8, 100, 28), sf);
        }

        // ── Centre circle ─────────────────────────────────────────────────
        int cr = 44;
        using (var b = new SolidBrush(Color.FromArgb(230, 20, 20, 40)))
            g.FillEllipse(b, cx - cr, cy - cr, cr * 2, cr * 2);
        using (var pen = new Pen(Color.FromArgb(160, 255, 255, 255), 2))
            g.DrawEllipse(pen, cx - cr, cy - cr, cr * 2, cr * 2);
        using (var f  = new Font("Segoe UI Emoji", 22))
        using (var b  = new SolidBrush(Color.White))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center,
                                           LineAlignment = StringAlignment.Center })
            g.DrawString("✋", f, b,
                new RectangleF(cx - cr, cy - cr, cr * 2, cr * 2), sf);

        // ── Title ─────────────────────────────────────────────────────────
        using (var f  = new Font("Segoe UI", 18, FontStyle.Bold))
        using (var b  = new SolidBrush(Color.White))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center })
            g.DrawString("Hand Menu", f, b, new PointF(cx, cy - r - 46), sf);

        using (var f  = new Font("Segoe UI", 10))
        using (var b  = new SolidBrush(Color.FromArgb(200, 200, 200, 220)))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center })
            g.DrawString("Make a FIST to select  ·  Lower hand to cancel",
                f, b, new PointF(cx, cy - r - 22), sf);
    }

    // ── Input-source badge helpers (NEW) ──────────────────────────────────
    private void SetInputSourceBadge(int tuioId, string source)
    {
        if (!inputSourceBadge.TryGetValue(tuioId, out Label? badge)) return;

        animalInputSource[tuioId] = source;

        Color bg = source switch
        {
            "TUIO"  => Color.FromArgb(200, 30,  100, 200),  // blue
            "YOLO"  => Color.FromArgb(200, 30,  160,  60),  // green
            "Mouse" => Color.FromArgb(200, 160,  80,  20),  // orange
            _       => Color.FromArgb(180,  40,  40,  40)
        };

        badge.Text    = $"via {source}";
        badge.BackColor = bg;
        badge.Visible = true;
    }

    private void ClearInputSourceBadge(int tuioId)
    {
        if (!inputSourceBadge.TryGetValue(tuioId, out Label? badge)) return;
        animalInputSource.Remove(tuioId);
        badge.Text    = "";
        badge.Visible = false;
    }
    // ─────────────────────────────────────────────────────────────────────

    private void SetupTuio()
    {
        tuioHandler.OnObjectAdded   += HandleTuioAdded;
        tuioHandler.OnObjectUpdated += HandleTuioUpdated;
        tuioHandler.OnObjectRemoved += HandleTuioRemoved;
        tuioHandler.Start();

    }

    private void SetupEmotionListener()
    {
        try
        {
            udpClient = new UdpClient(5005);
            isListeningEmotions = true;
            Task.Run(() => ListenForEmotions());
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not start emotion listener: " + ex.Message);
        }
    }

    private void ListenForEmotions()
    {
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 5005);
        while (isListeningEmotions)
        {
            try
            {
                byte[] bytes = udpClient!.Receive(ref endPoint);
                string message = Encoding.UTF8.GetString(bytes);
                SafeInvoke(() => HandleVisionData(message));
            }
            catch
            {
                break;
            }
        }
    }

    private string lastHintEmotion = "";
    private DateTime lastHintTime = DateTime.MinValue;

    private void HandleVisionData(string message)
    {
        string emotion = "none";
        string prevGaze = currentGaze;
        float gazeX = 0.5f, gazeY = 0.5f;
        bool hasGazeXY = false;

        // Parse structured payload:
        // EMOTION:happy|GAZE:Left|GAZE_X:0.71|GAZE_Y:0.48|LIGHTING:bright
        foreach (var part in message.Split('|'))
        {
            var kv = part.Split(':', 2);
            if (kv.Length != 2) continue;
            switch (kv[0].ToUpper())
            {
                case "EMOTION":  emotion = kv[1].ToLower(); break;
                case "GAZE":     currentGaze = kv[1]; break;
                case "LIGHTING": currentLighting = kv[1]; break;
                case "GAZE_X":
                    if (float.TryParse(kv[1], System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float gx))
                    { gazeX = gx; hasGazeXY = true; }
                    break;
                case "GAZE_Y":
                    if (float.TryParse(kv[1], System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float gy))
                    { gazeY = gy; }
                    break;
            }
        }

        // Accumulate gaze into heatmap buffer
        if (hasGazeXY)
            AccumulateGaze(gazeX, gazeY);

        // Update UI labels
        emotionLabel.Text  = $"Emotion: {emotion}";
        gazeLabel.Text     = $"Gaze: {currentGaze}";
        lightingLabel.Text = $"Lighting: {currentLighting}";

        // ── Gaze-based animal highlight ───────────────────────────────────
        ApplyGazeHighlight();

        // Context Awareness (Lighting) ─────────────────────────────────
        if (currentLighting == "dark" && !isNightMode)
        {
            isNightMode = true;
            ApplyNightMode(true);
        }
        else if (currentLighting == "bright" && isNightMode)
        {
            isNightMode = false;
            ApplyNightMode(false);
        }

        // Emotion-based hints (unchanged logic)
        if ((emotion == "sad" || emotion == "angry") && (DateTime.Now - lastHintTime).TotalSeconds > 5)
        {
            ShowFeedback($"Hey! Don't be {emotion}! Here's a hint: Check the animals' environments!", Color.Orange);
            lastHintEmotion = emotion;
            lastHintTime = DateTime.Now;
        }
        else if (emotion == "happy" && (DateTime.Now - lastHintTime).TotalSeconds > 5)
        {
            ShowFeedback($"Glad to see you smiling! Keep up the good work!", Color.HotPink);
            lastHintTime = DateTime.Now;
            lastHintEmotion = "happy";
        }
    }

    // ── Gaze heatmap accumulation ──────────────────────────────────────────
    /// <summary>
    /// Adds a Gaussian "splash" centred on the normalised gaze position.
    /// gazeX: 0=right edge, 1=left edge (iris convention) → flipped to screen.
    /// gazeY: 0=top, 1=bottom (nose-tip Y).
    /// </summary>
    private void AccumulateGaze(float gazeX, float gazeY)
    {
        // Iris ratio is mirrored: high ratio = user looking left = screen left.
        // Flip X so heatmap X maps naturally to screen X.
        float sx = 1f - gazeX;   // screen-normalised X
        float sy = gazeY;        // screen-normalised Y

        // Centre in the heatmap grid
        int cx = (int)(sx * (HeatW - 1));
        int cy = (int)(sy * (HeatH - 1));

        // Gaussian radius in heatmap cells (~10% of width)
        const float sigma = HeatW * 0.08f;
        int radius = (int)(sigma * 3);
        int x0 = Math.Max(0, cx - radius);
        int x1 = Math.Min(HeatW - 1, cx + radius);
        int y0 = Math.Max(0, cy - radius);
        int y1 = Math.Min(HeatH - 1, cy + radius);

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                float dx = x - cx, dy = y - cy;
                float v = (float)Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma));
                gazeHeat[x, y] += v;
                if (gazeHeat[x, y] > gazeHeatMax)
                    gazeHeatMax = gazeHeat[x, y];
            }
        }
    }

    /// <summary>
    /// Renders the accumulated heatmap onto a screenshot of the game form
    /// and saves the result as a PNG to Desktop\GazeHeatmaps.
    /// </summary>
    private void SaveGazeHeatmap()
    {
        try
        {
            // ─ 1. Screenshot of the game form ──────────────────────────────
            int fw = this.ClientSize.Width;
            int fh = this.ClientSize.Height;
            using var screenshot = new Bitmap(fw, fh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            this.DrawToBitmap(screenshot, new Rectangle(0, 0, fw, fh));

            // ─ 2. Render heatmap cells to full-res overlay ───────────────────
            using var heatLayer = new Bitmap(fw, fh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(heatLayer))
            {
                g.Clear(Color.Transparent);
                float cellW = (float)fw / HeatW;
                float cellH = (float)fh / HeatH;

                for (int x = 0; x < HeatW; x++)
                {
                    for (int y = 0; y < HeatH; y++)
                    {
                        float norm = gazeHeat[x, y] / gazeHeatMax;   // 0–1
                        if (norm < 0.01f) continue;

                        Color col = HeatColor(norm);
                        // Alpha proportional to intensity, capped at 200
                        int alpha = (int)(norm * 200);
                        using var brush = new SolidBrush(Color.FromArgb(alpha, col));
                        g.FillRectangle(brush,
                            x * cellW, y * cellH,
                            cellW + 1, cellH + 1);
                    }
                }
            }

            // ─ 3. Composite screenshot + heat layer ────────────────────────
            using var composite = new Bitmap(fw, fh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(composite))
            {
                g.DrawImage(screenshot, 0, 0);
                g.DrawImage(heatLayer,  0, 0);

                // ─ Legend bar (bottom strip) ──────────────────────────────
                int legW = 200, legH = 18, legX = fw - legW - 10, legY = fh - 28;
                for (int i = 0; i < legW; i++)
                {
                    float t = (float)i / legW;
                    using var b = new SolidBrush(HeatColor(t));
                    g.FillRectangle(b, legX + i, legY, 1, legH);
                }
                using var legPen = new Pen(Color.White, 1);
                g.DrawRectangle(legPen, legX, legY, legW, legH);
                using var legFont = new Font("Segoe UI", 8);
                using var legBrush = new SolidBrush(Color.White);
                g.DrawString("Low", legFont, legBrush, legX - 28, legY);
                g.DrawString("High", legFont, legBrush, legX + legW + 3, legY);
                g.DrawString("Gaze Heatmap", legFont, legBrush, legX + legW / 2 - 35, legY - 14);

                // ─ Timestamp + username watermark ──────────────────────────
                string stamp = $"{currentUser.PlayerName}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                using var stFont = new Font("Segoe UI", 9, FontStyle.Bold);
                using var stBrush = new SolidBrush(Color.FromArgb(220, Color.White));
                g.DrawString(stamp, stFont, stBrush, 8, fh - 20);
            }

            // ─ 4. Save PNG ───────────────────────────────────────────────
            string desktop  = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string saveDir  = Path.Combine(desktop, "GazeHeatmaps");
            Directory.CreateDirectory(saveDir);

            string safeUser = string.Concat(currentUser.PlayerName.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"gaze_{safeUser}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(saveDir, fileName);

            composite.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            ShowFeedback($"📅 Heatmap saved → GazeHeatmaps\\{fileName}", Color.DeepSkyBlue);
        }
        catch (Exception ex)
        {
            ShowFeedback($"Heatmap save failed: {ex.Message}", Color.OrangeRed);
        }
    }

    /// <summary>
    /// Maps a normalised intensity (0–1) to a blue→cyan→green→yellow→red gradient.
    /// </summary>
    private static Color HeatColor(float t)
    {
        // 4 stops: 0=blue, 0.33=cyan, 0.66=yellow, 1=red
        t = Math.Max(0f, Math.Min(1f, t));
        int r, gv, b;
        if (t < 0.25f)
        {
            float s = t / 0.25f;
            r = 0; gv = (int)(s * 255); b = 255;
        }
        else if (t < 0.5f)
        {
            float s = (t - 0.25f) / 0.25f;
            r = 0; gv = 255; b = (int)((1 - s) * 255);
        }
        else if (t < 0.75f)
        {
            float s = (t - 0.5f) / 0.25f;
            r = (int)(s * 255); gv = 255; b = 0;
        }
        else
        {
            float s = (t - 0.75f) / 0.25f;
            r = 255; gv = (int)((1 - s) * 255); b = 0;
        }
        return Color.FromArgb(r, gv, b);
    }

    // ── Gaze highlight helpers ────────────────────────────────────────────
    private void ApplyGazeHighlight()
    {
        bool lookingLeft = currentGaze == "Left";

        if (lookingLeft && !gazeHighlightActive)
        {
            gazeHighlightActive = true;
            HighlightAllAnimals();
            ShowFeedback("👀 Looking at the animals!", Color.Yellow);
        }
        else if (!lookingLeft && gazeHighlightActive)
        {
            gazeHighlightActive = false;
            ClearAllGazeHighlights();
        }
    }

    private void HighlightAllAnimals()
    {
        foreach (var animal in animalById.Values)
        {
            if (animal.IsMatched) continue;   // already home — leave it
            animal.Picture.Paint += GazeHighlight_Paint;
            animal.Picture.Invalidate();
        }
    }

    private void ClearAllGazeHighlights()
    {
        foreach (var animal in animalById.Values)
        {
            animal.Picture.Paint -= GazeHighlight_Paint;
            animal.Picture.Invalidate();
        }
    }

    private void GazeHighlight_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not PictureBox pic) return;
        using var pen = new Pen(GazeHighlightColor, 5);
        e.Graphics.DrawRectangle(pen, 2, 2, pic.Width - 5, pic.Height - 5);
    }
    // ─────────────────────────────────────────────────────────────────────

    private void ApplyNightMode(bool night)
    {
        // Swap background
        string bgName = night ? "background_night.jpeg" : "background.jpeg";
        string bgPath = GetAssetPath(bgName);
        if (File.Exists(bgPath))
        {
            this.BackgroundImage?.Dispose();
            this.BackgroundImage = Image.FromFile(bgPath);
        }
        else
        {
            this.BackgroundImage = null;
            this.BackColor = night ? Color.MidnightBlue : Color.DarkGreen;
        }

        // Swap animal images to night/day variants
        foreach (var animal in animalById.Values)
        {
            string suffix   = night ? "_night.jpeg" : ".jpeg";
            string baseName = animal.Name.ToLower();
            string imgPath  = GetAssetPath(baseName + suffix);

            if (File.Exists(imgPath))
            {
                animal.Picture.Image?.Dispose();
                animal.Picture.Image = Image.FromFile(imgPath);
            }
        }

        if (night)
            ShowFeedback("It's getting dark... Help the animals to their beds!", Color.DarkSlateBlue);
        else
            ShowFeedback("The sun is up! Time to play.", Color.Orange);
    }

    // ── YOLO listener (NEW) ───────────────────────────────────────────────
    private void SetupYoloListener()
    {
        try
        {
            yoloUdpClient = new UdpClient(5006);
            isListeningYolo = true;
            Task.Run(() => ListenForYolo());
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not start YOLO listener: " + ex.Message);
        }
    }

    private void ListenForYolo()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 5006);
        while (isListeningYolo)
        {
            try
            {
                byte[] bytes = yoloUdpClient!.Receive(ref ep);
                string json  = Encoding.UTF8.GetString(bytes);
                YoloMessage? msg = JsonSerializer.Deserialize<YoloMessage>(json);
                if (msg == null) continue;

                SafeInvoke(() =>
                {
                    debugLabel.Text = $"YOLO: {msg.Event} id={msg.Id} x={msg.X:F2} y={msg.Y:F2}";
                    switch (msg.Event)
                    {
                        case "added":   HandleYoloAdded(msg.Id, msg.X, msg.Y);   break;
                        case "update":  HandleYoloUpdated(msg.Id, msg.X, msg.Y); break;
                        case "removed": HandleYoloRemoved(msg.Id, msg.X, msg.Y); break;
                    }
                });
            }
            catch { break; }
        }
    }

    private void HandleYoloAdded(int animalId, float normX, float normY)
    {
        if (!animalById.TryGetValue(animalId, out GameItem? animal)) return;
        if (animal.IsMatched) return;
        // Don't hijack if TUIO already has this animal
        if (grabbedAnimals.ContainsKey(animalId) &&
            animalInputSource.TryGetValue(animalId, out string? src) && src == "TUIO") return;

        grabbedAnimals[animalId] = animal;
        animal.Picture.BorderStyle = BorderStyle.Fixed3D;
        MoveAnimalToMarker(animal, 1f - normX, normY);
        SetInputSourceBadge(animalId, "YOLO");
        ShowFeedback($"YOLO detected {animal.Name}! Move it to its home!", Color.LimeGreen);
    }

    private void HandleYoloUpdated(int animalId, float normX, float normY)
    {
        if (!grabbedAnimals.TryGetValue(animalId, out GameItem? animal)) return;
        if (animalInputSource.TryGetValue(animalId, out string? src) && src != "YOLO") return;
        MoveAnimalToMarker(animal, 1f - normX, normY);
    }

    private void HandleYoloRemoved(int animalId, float normX, float normY)
    {
        if (!grabbedAnimals.TryGetValue(animalId, out GameItem? animal)) return;
        if (animalInputSource.TryGetValue(animalId, out string? src) && src != "YOLO") return;
        grabbedAnimals.Remove(animalId);
        ClearInputSourceBadge(animalId);
        TrySnapOrReturn(animal);
    }
    // ─────────────────────────────────────────────────────────────────────

    // ── Hand Menu listener (NEW) ──────────────────────────────────────────
    private void SetupHandMenuListener()
    {
        try
        {
            handMenuUdpClient = new UdpClient(5007);
            isListeningHandMenu = true;
            Task.Run(() => ListenForHandMenu());
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not start Hand Menu listener: " + ex.Message);
        }
    }

    private void ListenForHandMenu()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 5007);
        while (isListeningHandMenu)
        {
            try
            {
                byte[] bytes = handMenuUdpClient!.Receive(ref ep);
                string command = Encoding.UTF8.GetString(bytes);

                SafeInvoke(() =>
                {
                    if (command.StartsWith("SELECT:"))
                    {
                        string action = command.Substring(7);
                        HideHandMenuOverlay();
                        HandleHandMenuAction(action);
                    }
                    else if (command.StartsWith("HOVER:"))
                    {
                        string action = command.Substring(6);
                        UpdateHandMenuHover(action);
                    }
                    else if (command.StartsWith("OPEN_MENU:"))
                    {
                        string initial = command.Substring(10);
                        ShowHandMenuOverlay(initial);
                    }
                    else if (command == "CANCEL")
                    {
                        HideHandMenuOverlay();
                    }
                });
            }
            catch { break; }
        }
    }

    private void HandleHandMenuAction(string action)
    {
        if (action == "Hint")
        {
            ShowFeedback("Hint: Check the environments to find where each animal belongs!", Color.Gold);
        }
        else if (action == "Restart")
        {
            foreach (var animal in animalById.Values)
            {
                animal.IsMatched = false;
                animal.Picture.BorderStyle = BorderStyle.FixedSingle;
                ReturnToOrigin(animal);
            }
            grabbedAnimals.Clear();
            animalInputSource.Clear();
            foreach (var badge in inputSourceBadge.Values) { badge.Visible = false; badge.Text = ""; }
            ShowFeedback("Game Restarted via Hand Menu!", Color.Orange);
        }
        else if (action == "Logout")
        {
            ShowFeedback("Logging out via Hand Menu...", Color.DodgerBlue);
            var logoutTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            logoutTimer.Tick += (s, e) => { logoutTimer.Stop(); this.Close(); };
            logoutTimer.Start();
        }
    }

    // ── Hand Menu overlay helpers ─────────────────────────────────────────
    private void ShowHandMenuOverlay(string initialOption)
    {
        currentHoveredOption = initialOption;
        RefreshMenuCards();
        handMenuOverlay.Visible = true;
        handMenuOverlay.BringToFront();
        ShowFeedback("✋ Hand Menu open — make a FIST to select", Color.Cyan);
    }

    private void HideHandMenuOverlay()
    {
        handMenuOverlay.Visible  = false;
        currentHoveredOption     = "";
    }

    private void UpdateHandMenuHover(string option)
    {
        currentHoveredOption = option;
        RefreshMenuCards();
        ShowFeedback($"👆 {option} — make a fist to confirm", Color.Gold);
    }

    private void RefreshMenuCards()
    {
        // Single panel = single repaint — no cascading child-control redraws
        handMenuOverlay.Invalidate();
    }
    // ─────────────────────────────────────────────────────────────────────

    private void HandleTuioAdded(int symbolId, float normX, float normY)
    {
        SafeInvoke(() =>
        {
            debugLabel.Text = $"TUIO: Added ID={symbolId}  x={normX:F2} y={normY:F2}";

            if (symbolId == LOGOUT_MARKER_ID)
            {
                ShowFeedback("👋 Logging out...", Color.DodgerBlue);
                var logoutTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                logoutTimer.Tick += (s, e) => { logoutTimer.Stop(); this.Close(); };
                logoutTimer.Start();
                return;
            }

            if (symbolId == 6)
            {
                AnimalFactsForm factsForm = new AnimalFactsForm();
                factsForm.ShowDialog(this);
                return;
            }

            if (!animalById.TryGetValue(symbolId, out GameItem? animal))
            {
                ShowFeedback($"❌ Marker #{symbolId} is not assigned to any animal!", Color.Red);
                return;
            }

            if (animal.IsMatched)
            {
                ShowFeedback($"{animal.Name} is already home — no need to move it!", Color.Gold);
                return;
            }

            if (grabbedAnimals.ContainsKey(symbolId) && animalInputSource.TryGetValue(symbolId, out string? existingSrc) && existingSrc == "YOLO")
            {
                grabbedAnimals.Remove(symbolId);
                ClearInputSourceBadge(symbolId);
            }

            grabbedAnimals[symbolId] = animal;
            animal.Picture.BorderStyle = BorderStyle.Fixed3D;
            MoveAnimalToMarker(animal, normX, normY);
            SetInputSourceBadge(symbolId, "TUIO"); // NEW
            ShowFeedback($"✅ Marker #{symbolId} grabbed {animal.Name}. Move it to its home!", Color.DarkGreen);
        });
    }

    private void HandleTuioUpdated(int symbolId, float normX, float normY)
    {
        SafeInvoke(() =>
        {
            debugLabel.Text = $"TUIO: Move ID={symbolId}  x={normX:F2} y={normY:F2}";

            if (!grabbedAnimals.ContainsKey(symbolId))
            {
                // Missed the add event — grab it now
                HandleTuioAdded(symbolId, normX, normY);
                return;
            }

            if (!grabbedAnimals.TryGetValue(symbolId, out GameItem? animal)) return;
            MoveAnimalToMarker(animal, normX, normY);
        });
    }

    private void HandleTuioRemoved(int symbolId, float normX, float normY)
    {
        SafeInvoke(() =>
        {
            debugLabel.Text = $"TUIO: Removed ID={symbolId}";
            if (!grabbedAnimals.TryGetValue(symbolId, out GameItem? animal)) return;
            grabbedAnimals.Remove(symbolId);
            ClearInputSourceBadge(symbolId); // NEW
            TrySnapOrReturn(animal);
        });
    }

    private void MoveAnimalToMarker(GameItem animal, float normX, float normY)
    {
        Point screenPt = NormToScreen(normX, normY);
        animal.Picture.Location = new Point(
            screenPt.X - animal.Picture.Width  / 2,
            screenPt.Y - animal.Picture.Height / 2);
        animal.Picture.BringToFront();
    }

    private void SafeInvoke(Action action)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        if (this.InvokeRequired)
            this.Invoke(action);
        else
            action();
    }

    private void AnimalPic_MouseDown(object? sender, MouseEventArgs e)
    {
        if (sender is not PictureBox pic) return;
        GameItem? animal = FindAnimalByPic(pic);
        if (animal == null || animal.IsMatched) return;

        mouseDragItem = animal;
        mouseOffset = new Point(e.X, e.Y);
        animal.Picture.BringToFront();
        SetInputSourceBadge(animal.TuioId, "Mouse"); // NEW
        ShowFeedback($"[Mouse] Dragging {animal.Name}…", Color.White);
    }

    private void AnimalPic_MouseMove(object? sender, MouseEventArgs e)
    {
        if (mouseDragItem == null || e.Button != MouseButtons.Left) return;
        var newLoc = mouseDragItem.Picture.Location;
        newLoc.Offset(e.X - mouseOffset.X, e.Y - mouseOffset.Y);
        mouseDragItem.Picture.Location = newLoc;
    }

    private void AnimalPic_MouseUp(object? sender, MouseEventArgs e)
    {
        if (mouseDragItem == null) return;
        var animal = mouseDragItem;
        mouseDragItem = null;
        ClearInputSourceBadge(animal.TuioId); // NEW
        TrySnapOrReturn(animal);
    }

    private void TrySnapOrReturn(GameItem animal)
    {
        animal.Picture.BorderStyle = BorderStyle.FixedSingle;
        foreach (var home in homes)
        {
            if (GameLogic.CheckDropMatch(animal, home))
            {
                animal.Picture.Location = new Point(
                    home.Picture.Left + (home.Picture.Width  - animal.Picture.Width)  / 2,
                    home.Picture.Top  + (home.Picture.Height - animal.Picture.Height) / 2);
                animal.IsMatched = true;
                ClearInputSourceBadge(animal.TuioId); // NEW — hide badge when matched
                ShowFeedback($"🎉 {animal.Name} is home!", Color.Gold);
                CheckWinCondition();
                return;
            }
        }

        ReturnToOrigin(animal);
        ShowFeedback($"❌ Wrong home! {animal.Name} returned to start.", Color.OrangeRed);
    }

    private void ReturnToOrigin(GameItem animal)
    {
        animal.Picture.Location = animal.OriginalLocation;
    }

    private void CheckWinCondition()
    {
        bool allMatched = true;
        foreach (var animal in animalById.Values)
            if (!animal.IsMatched) { allMatched = false; break; }

        if (allMatched)
        {
            feedbackLabel.Text = "🏆 All animals are home! You win!";
            feedbackLabel.BackColor = Color.FromArgb(200, 20, 120, 20);

            // Signal Python to save the gaze heatmap it has been building live
            try
            {
                string winMsg = $"WIN:{currentUser.PlayerName}";
                using var winSock = new System.Net.Sockets.UdpClient();
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(winMsg);
                winSock.Send(bytes, bytes.Length, "127.0.0.1", 5009);
                ShowFeedback("📊 Heatmap saving… check Desktop\\GazeHeatmaps!", Color.DeepSkyBlue);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not send WIN signal: " + ex.Message);
            }

            MessageBox.Show("🎉 Congratulations! All animals found their homes!", "You Win!",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ShowFeedback(string message, Color color)
    {
        if (feedbackLabel.InvokeRequired)
        {
            feedbackLabel.Invoke(new Action(() => ShowFeedback(message, color)));
            return;
        }
        feedbackLabel.Text = message;
        feedbackLabel.BackColor = Color.FromArgb(190, color.R / 3, color.G / 3, color.B / 3);
    }

    private Point NormToScreen(float nx, float ny)
    {
        return new Point((int)(nx * this.ClientSize.Width), (int)(ny * this.ClientSize.Height));
    }

    private GameItem? FindAnimalByPic(PictureBox pic)
    {
        foreach (var a in animalById.Values)
            if (a.Picture == pic) return a;
        return null;
    }

    private static string GetAssetPath(string filename)
    {
        string path = Path.Combine(Application.StartupPath, "Assets", filename);
        if (File.Exists(path)) return path;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Assets", filename);
    }

    private void GamePlayForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        isListeningEmotions = false;
        udpClient?.Close();

        isListeningYolo = false;   // NEW
        yoloUdpClient?.Close();    // NEW

        isListeningHandMenu = false;
        handMenuUdpClient?.Close();

        tuioHandler.Stop();
        tuioHandler.Dispose();

        if (parentScanner != null && !parentScanner.IsDisposed)
        {
            parentScanner.ResetScanner();
            parentScanner.Show();
        }
    }

    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            tuioHandler?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        this.ClientSize = new Size(1024, 768);
        this.Name = "GamePlayForm";
        this.ResumeLayout(false);
    }

    // ── YOLO message DTO (NEW) ────────────────────────────────────────────
    private record YoloMessage(
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("id")]    int    Id,
        [property: JsonPropertyName("x")]     float  X,
        [property: JsonPropertyName("y")]     float  Y
    );
}

// ── Double-buffered panel (eliminates pie menu flicker) ───────────────────────
internal sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        this.SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint  |
            ControlStyles.UserPaint,
            true);
        this.UpdateStyles();
    }
}
