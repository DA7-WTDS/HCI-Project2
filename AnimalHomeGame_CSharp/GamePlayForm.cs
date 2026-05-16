using System;
using System.Collections.Generic;
using System.Drawing;
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

    // ── Emotion listener (unchanged) ──────────────────────────────────────
    private UdpClient? udpClient;
    private bool isListeningEmotions = false;
    private Label emotionLabel = null!;

    // ── Hand Menu listener (NEW) ──────────────────────────────────────────
    private UdpClient? handMenuUdpClient;
    private bool isListeningHandMenu = false;

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
        ("Farm",  3, "farm.jpeg",  "Farm"),
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
        SetupYoloListener(); // NEW
        SetupHandMenuListener(); // NEW Hand Menu
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
                string emotion = Encoding.UTF8.GetString(bytes).ToLower();
                SafeInvoke(() => HandleEmotionReceived(emotion));
            }
            catch
            {
                break;
            }
        }
    }

    private string lastHintEmotion = "";
    private DateTime lastHintTime = DateTime.MinValue;

    private void HandleEmotionReceived(string emotion)
    {
        emotionLabel.Text = $"Emotion: {emotion}";

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
                        HandleHandMenuAction(action);
                    }
                    else if (command.StartsWith("HOVER:"))
                    {
                        string action = command.Substring(6);
                        ShowFeedback($"Hand Menu: Hovering over {action}...", Color.LightBlue);
                    }
                    else if (command.StartsWith("OPEN_MENU:"))
                    {
                        ShowFeedback("Hand Menu Opened! Make a fist to select.", Color.Cyan);
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
