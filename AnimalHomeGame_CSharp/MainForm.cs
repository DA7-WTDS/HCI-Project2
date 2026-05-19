using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AnimalHomeGame_CSharp;

public partial class MainForm : Form
{
    private Label statusLabel;
    private Label instructionsLabel;
    private bool isAuthenticated = false;
    private UdpClient? udpClient;
    private bool isListening = false;

    public MainForm()
    {
        InitializeComponent();
        SetupGUI();
        SetupFaceRecognitionListener();
    }

    private void SetupGUI()
    {
        this.Text = "Animal Home Game - Face Login";
        this.Size = new Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.WhiteSmoke;
        this.FormClosed += MainForm_FormClosed;

        statusLabel = new Label 
        { 
            Text = "Waiting for Face Recognition...", 
            Font = new Font("Segoe UI", 20, FontStyle.Bold), 
            ForeColor = Color.DimGray, 
            AutoSize = false, 
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 150,
            Padding = new Padding(0, 50, 0, 0)
        };

        instructionsLabel = new Label
        {
            Text = "How it works:\n\n1. Ensure the Python AI Vision script is running.\n2. Look at the camera.\n3. We will automatically detect your face and log you in!\n\nIf you are new, a profile will be created for you automatically.",
            Font = new Font("Segoe UI", 12, FontStyle.Regular),
            ForeColor = Color.DarkSlateGray,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Padding = new Padding(20)
        };
        
        this.Controls.Add(instructionsLabel);
        this.Controls.Add(statusLabel);
    }

    private void SetupFaceRecognitionListener()
    {
        try
        {
            udpClient = new UdpClient(5008);
            isListening = true;
            Task.Run(() => ListenForFaces());
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not start face listener: " + ex.Message);
        }
    }

    private void ListenForFaces()
    {
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 5008);
        while (isListening)
        {
            try
            {
                byte[] bytes = udpClient!.Receive(ref endPoint);
                string message = Encoding.UTF8.GetString(bytes);
                if (message.StartsWith("LOGIN:"))
                {
                    string username = message.Substring(6);
                    SafeInvoke(() => ProcessLogin(username));
                }
            }
            catch
            {
                break;
            }
        }
    }

    private void SafeInvoke(Action action)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        if (this.InvokeRequired)
            this.Invoke(action);
        else
            action();
    }

    private void ProcessLogin(string message)
    {
        if (isAuthenticated) return;
        isAuthenticated = true;

        // Message format: "username:role"  (role added by ai_vision.py)
        // Fall back to "child" if role is missing (backward compat)
        string[] parts   = message.Split(':', 2);
        string username  = parts[0].Trim();
        string role      = parts.Length > 1 ? parts[1].Trim().ToLower() : "child";

        // Build the in-memory profile (Python owns persistence)
        UserProfile activeProfile = ProfileManager.Find(username, role) ?? new UserProfile
        {
            Id         = username,
            PlayerName = username,
            Role       = role,
        };

        if (role == "teacher")
        {
            statusLabel.Text      = $"Welcome, {username}! (Teacher)";
            instructionsLabel.Text = "Redirecting to Teacher Dashboard...";
            statusLabel.ForeColor  = Color.DarkViolet;
        }
        else
        {
            statusLabel.Text      = $"Welcome, {username}!";
            instructionsLabel.Text = "Getting everything ready for you...";
            statusLabel.ForeColor  = Color.Green;
        }
        instructionsLabel.ForeColor = Color.Black;

        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2000 };
        timer.Tick += (s, args) =>
        {
            timer.Stop();
            if (activeProfile.Role == "teacher")
            {
                var dash = new AdminDashboardForm(activeProfile, this);
                dash.Show();
            }
            else
            {
                var gamePlay = new GamePlayForm(activeProfile, this);
                gamePlay.Show();
            }
            this.Hide();
        };
        timer.Start();
    }

    public void ResetScanner()
    {
        isAuthenticated = false;
        statusLabel.Text = "Waiting for Face Recognition...";
        statusLabel.ForeColor = Color.DimGray;
        instructionsLabel.Text = "How it works:\n\n1. Ensure the Python AI Vision script is running.\n2. Look at the camera.\n3. We will automatically detect your face and log you in!\n\nIf you are new, a profile will be created for you automatically.";
        instructionsLabel.ForeColor = Color.DarkSlateGray;
    }

    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        isListening = false;
        udpClient?.Close();
    }
}
