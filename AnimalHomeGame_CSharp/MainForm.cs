using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using System.Windows.Forms;

namespace AnimalHomeGame_CSharp;

public partial class MainForm : Form
{
    private Label statusLabel;
    private Label instructionsLabel;
    private DeviceWatcher deviceWatcher;
    private bool isAuthenticated = false;

    public MainForm()
    {
        InitializeComponent();
        SetupGUI();
        SetupBluetoothWatcher();
    }

    private void SetupGUI()
    {
        this.Text = "Animal Home Game - Authentication";
        this.Size = new Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.WhiteSmoke;

        statusLabel = new Label 
        { 
            Text = "Scanning for your Bluetooth device...", 
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
            Text = "How it works:\n\n1. Make sure your phone or device's Bluetooth is turned ON and is 'Discoverable'.\n2. Keep your device nearby.\n3. We will automatically detect you!\n\nIf you are new, we will instantly create your profile.\nIf you are returning, you will be logged right in.",
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

    private void SetupBluetoothWatcher()
    {
        string[] requestedProperties = { "System.ItemNameDisplay", "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };
        
        deviceWatcher = DeviceInformation.CreateWatcher(
            "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")",
            requestedProperties,
            DeviceInformationKind.AssociationEndpoint);

        deviceWatcher.Added += DeviceWatcher_Added;
        deviceWatcher.Start();
    }

    private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        if (isAuthenticated) return;
        if (string.IsNullOrWhiteSpace(deviceInfo.Name)) return;

        lock (this)
        {
            if (isAuthenticated) return;
            isAuthenticated = true;
            
            if (deviceWatcher.Status == DeviceWatcherStatus.Started)
            {
                deviceWatcher.Stop();
            }
        }

        if (this.InvokeRequired)
        {
            this.Invoke(new Action(() => AuthenticateDevice(deviceInfo)));
        }
        else
        {
            AuthenticateDevice(deviceInfo);
        }
    }

    private void AuthenticateDevice(DeviceInformation deviceInfo)
    {
        List<UserProfile> profiles = ProfileManager.LoadProfiles();
        
        UserProfile? existingProfile = profiles.FirstOrDefault(p => p.BluetoothDeviceId == deviceInfo.Id);

        UserProfile activeProfile = null;

        if (existingProfile != null)
        {
            activeProfile = existingProfile;
            statusLabel.Text = $"Welcome back, {existingProfile.PlayerName}!";
            instructionsLabel.Text = $"Automatic Sign-In Successful.\nYour Role: {existingProfile.Role}\n\nGetting everything ready for you...";
            statusLabel.ForeColor = Color.Green;
            instructionsLabel.ForeColor = Color.Black;
        }
        else
        {
            string newRole = profiles.Count == 0 ? "Admin" : "User";
            
            UserProfile newProfile = new UserProfile 
            {
                PlayerName = deviceInfo.Name,
                BluetoothDeviceId = deviceInfo.Id,
                Role = newRole
            };
            
            profiles.Add(newProfile);
            ProfileManager.SaveProfiles(profiles);

            activeProfile = newProfile;
            statusLabel.Text = $"Account Created for {newProfile.PlayerName}!";
            instructionsLabel.Text = $"Automatic Sign-Up Successful.\nYour Role: {newProfile.Role}\n\nGetting everything ready for you...";
            statusLabel.ForeColor = Color.Blue;
            instructionsLabel.ForeColor = Color.Black;
        }
        
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 3000 };
        timer.Tick += (s, args) => 
        {
            timer.Stop();
            
            GameForm gameMenuForm = new GameForm(activeProfile, this);
            gameMenuForm.Show();
            this.Hide();
        };
        timer.Start();
    }

    public void ResetScanner()
    {
        isAuthenticated = false;
        
        statusLabel.Text = "Scanning for your Bluetooth device...";
        statusLabel.ForeColor = Color.DimGray;
        instructionsLabel.Text = "How it works:\n\n1. Make sure your phone or device's Bluetooth is turned ON and is 'Discoverable'.\n2. Keep your device nearby.\n3. We will automatically detect you!\n\nIf you are new, we will instantly create your profile.\nIf you are returning, you will be logged right in.";
        instructionsLabel.ForeColor = Color.DarkSlateGray;

        if (deviceWatcher != null && deviceWatcher.Status != DeviceWatcherStatus.Started)
        {
            deviceWatcher.Start();
        }
    }
}
