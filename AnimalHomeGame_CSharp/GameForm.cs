using System;
using System.Drawing;
using System.Windows.Forms;

namespace AnimalHomeGame_CSharp;

public partial class GameForm : Form
{
    private UserProfile currentUser;
    private TuioHandler tuioHandler;
    private MainForm mainFormReference;

    public GameForm(UserProfile profile, MainForm mf = null)
    {
        this.currentUser = profile;
        this.mainFormReference = mf;
        InitializeComponent();
        SetupGUI();
        SetupTuio();
    }

    private void SetupTuio()
    {
        tuioHandler = new TuioHandler();
        tuioHandler.OnObjectAdded += HandleTuioAdded;
        tuioHandler.Start();
    }

    private void HandleTuioAdded(int symbolId, float normX, float normY)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        
        // Marker 6 = Open Animal Facts Menu
        if (symbolId == 6)
        {
            this.Invoke(new Action(() =>
            {
                AnimalFactsForm factsForm = new AnimalFactsForm();
                factsForm.ShowDialog(this);
            }));
        }
    }

    private void SetupGUI()
    {
        this.Text = "Animal Home Game - Main Menu";
        this.Size = new Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.WhiteSmoke;
        this.FormClosed += GameForm_FormClosed;

        Panel headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            BackColor = Color.LightSkyBlue
        };

        Label welcomeLabel = new Label
        {
            Text = $"Welcome back, {currentUser.PlayerName}!\nRole: {currentUser.Role}",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.DarkSlateBlue,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };

        headerPanel.Controls.Add(welcomeLabel);
        this.Controls.Add(headerPanel);

        Panel controlsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(50)
        };

        Button playButton = new Button
        {
            Text = "Play Game",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            BackColor = Color.LightGreen,
            Size = new Size(300, 60),
            Location = new Point(250, 50),
            Cursor = Cursors.Hand
        };
        playButton.Click += (s, e) => 
        {
            MainForm? mainForm = null;
            foreach (Form f in Application.OpenForms)
                if (f is MainForm mf) { mainForm = mf; break; }

            if (mainForm != null)
            {
                GamePlayForm gamePlay = new GamePlayForm(currentUser, mainForm);
                gamePlay.Show();
                this.Hide();
            }
        };

        Button factsButton = new Button
        {
            Text = "Animal Facts Menu",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            BackColor = Color.LightSkyBlue,
            Size = new Size(300, 50),
            Location = new Point(250, 130),
            Cursor = Cursors.Hand
        };
        factsButton.Click += (s, e) =>
        {
            AnimalFactsForm factsForm = new AnimalFactsForm();
            factsForm.ShowDialog(this);
        };

        Button logoutButton = new Button
        {
            Text = "Logout & Switch User",
            Font = new Font("Segoe UI", 14),
            BackColor = Color.LightPink,
            Size = new Size(300, 50),
            Location = new Point(250, currentUser.Role == "Admin" ? 430 : 210),
            Cursor = Cursors.Hand
        };
        logoutButton.Click += (s, e) => this.Close();

        controlsPanel.Controls.Add(playButton);
        controlsPanel.Controls.Add(factsButton);
        controlsPanel.Controls.Add(logoutButton);

        if (currentUser.Role == "Admin")
        {
            Button viewStatsButton = new Button
            {
                Text = "View Global Stats",
                Font = new Font("Segoe UI", 14),
                BackColor = Color.Wheat,
                Size = new Size(300, 50),
                Location = new Point(250, 210),
                Cursor = Cursors.Hand
            };
            viewStatsButton.Click += (s, e) => MessageBox.Show("Opening Stats...", "Admin Only");

            Button manageUsersButton = new Button
            {
                Text = "Manage Users",
                Font = new Font("Segoe UI", 14),
                BackColor = Color.LightYellow,
                Size = new Size(300, 50),
                Location = new Point(250, 280),
                Cursor = Cursors.Hand
            };
            manageUsersButton.Click += (s, e) => MessageBox.Show("Opening User Manager...", "Admin Only");

            Button settingsButton = new Button
            {
                Text = "Game Settings",
                Font = new Font("Segoe UI", 14),
                BackColor = Color.LightGray,
                Size = new Size(300, 50),
                Location = new Point(250, 350),
                Cursor = Cursors.Hand
            };
            settingsButton.Click += (s, e) => MessageBox.Show("Opening Settings...", "Admin Only");

            controlsPanel.Controls.Add(viewStatsButton);
            controlsPanel.Controls.Add(manageUsersButton);
            controlsPanel.Controls.Add(settingsButton);
        }

        this.Controls.Add(controlsPanel);
        controlsPanel.BringToFront();
    }

    private void GameForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        tuioHandler?.Stop();
        tuioHandler?.Dispose();

        if (mainFormReference != null)
        {
            mainFormReference.ResetScanner();
            mainFormReference.Show();
        }
        else
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form is MainForm mainForm)
                {
                    mainForm.ResetScanner();
                    mainForm.Show();
                    break;
                }
            }
        }
    }

    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        this.ClientSize = new System.Drawing.Size(284, 261);
        this.Name = "GameForm";
        this.ResumeLayout(false);
    }
}
