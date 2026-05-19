using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AnimalHomeGame_CSharp;

public partial class AdminDashboardForm : Form
{
    private readonly UserProfile _teacher;
    private readonly MainForm    _mainForm;

    // cached full list — sidebar/search filter this
    private List<UserProfile> _allUsers  = new();
    private List<UserProfile> _displayed = new();

    // live references used by multiple methods
    private DataGridView _grid      = null!;
    private TextBox      _searchBox = null!;
    private ComboBox     _roleFilter = null!;
    private const string SearchPlaceholder = "Search by username...";

    public AdminDashboardForm(UserProfile teacher, MainForm mainForm)
    {
        _teacher  = teacher;
        _mainForm = mainForm;
        InitializeComponent();
        SetupGUI();
        LoadData("all");
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    private void LoadData(string filter)
    {
        _allUsers = filter switch
        {
            "student" => ProfileManager.GetAllChildren().Cast<UserProfile>().ToList(),
            "teacher" => ProfileManager.GetAllTeachers().Cast<UserProfile>().ToList(),
            _         => ProfileManager.GetAllChildren()
                             .Concat(ProfileManager.GetAllTeachers())
                             .OrderBy(u => u.PlayerName, StringComparer.OrdinalIgnoreCase)
                             .ToList(),
        };
        ApplySearch();
    }

    private void ApplySearch()
    {
        string query = _searchBox.ForeColor == Color.Gray
            ? "" : _searchBox.Text.Trim().ToLower();
        string roleQ = _roleFilter.SelectedIndex switch
        {
            1 => "child",
            2 => "teacher",
            _ => ""
        };

        _displayed = _allUsers
            .Where(u =>
                (string.IsNullOrEmpty(query) || u.PlayerName.ToLower().Contains(query)) &&
                (string.IsNullOrEmpty(roleQ) || u.Role.Equals(roleQ, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        PopulateGrid();
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var u in _displayed)
        {
            string score = u.HighScore.HasValue ? $"{u.HighScore:F1}s" : "—";
            string played = u.LastPlayed ?? "—";
            string reg    = u.RegisteredAt ?? "—";
            _grid.Rows.Add(u.PlayerName, u.Role, u.Age?.ToString() ?? "—",
                           u.GamesPlayed, score, played, reg);
        }
    }

    private UserProfile? SelectedUser()
    {
        if (_grid.CurrentRow == null) return null;
        int idx = _grid.CurrentRow.Index;
        return idx >= 0 && idx < _displayed.Count ? _displayed[idx] : null;
    }

    // ── GUI build ─────────────────────────────────────────────────────────────

    private void SetupGUI()
    {
        this.Text            = "Admin Dashboard — Animal Home Game";
        this.Size            = new Size(1100, 700);
        this.StartPosition   = FormStartPosition.CenterScreen;
        this.BackColor       = Color.WhiteSmoke;
        this.MinimumSize     = new Size(900, 600);
        this.FormClosed     += (s, e) => { _mainForm.ResetScanner(); _mainForm.Show(); };

        // ── Header ────────────────────────────────────────────────────────────
        Panel headerPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 80,
            BackColor = Color.LightSkyBlue
        };

        Label titleLabel = new Label
        {
            Text      = "Admin Dashboard",
            Font      = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.DarkSlateBlue,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location  = new Point(20, 0),
            Size      = new Size(400, 80)
        };

        Label subtitleLabel = new Label
        {
            Text      = $"Logged in as: {_teacher.PlayerName}   •   Manage students and teachers",
            Font      = new Font("Segoe UI", 10),
            ForeColor = Color.SlateGray,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleRight,
            Anchor    = AnchorStyles.Top | AnchorStyles.Right,
            Location  = new Point(500, 0),
            Size      = new Size(570, 80)
        };

        Button logoutButton = new Button
        {
            Text      = "Logout",
            Font      = new Font("Segoe UI", 11),
            BackColor = Color.LightPink,
            Size      = new Size(100, 40),
            Anchor    = AnchorStyles.Top | AnchorStyles.Right,
            Location  = new Point(975, 20),
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        logoutButton.FlatAppearance.BorderSize = 0;
        logoutButton.Click += (s, e) => this.Close();

        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(subtitleLabel);
        headerPanel.Controls.Add(logoutButton);
        this.Controls.Add(headerPanel);

        // ── Sidebar ───────────────────────────────────────────────────────────
        Panel sidebarPanel = new Panel
        {
            Dock      = DockStyle.Left,
            Width     = 200,
            BackColor = Color.DarkSlateBlue,
            Padding   = new Padding(10)
        };

        var menuItems  = new[] { "All Users",  "Students",       "Teachers"  };
        var menuColors = new[] { Color.SteelBlue, Color.MediumSeaGreen, Color.Goldenrod };
        var menuKeys   = new[] { "all",         "student",        "teacher"  };

        for (int i = 0; i < menuItems.Length; i++)
        {
            string key = menuKeys[i];
            Button menuBtn = new Button
            {
                Text      = menuItems[i],
                Font      = new Font("Segoe UI", 12),
                BackColor = menuColors[i],
                ForeColor = Color.White,
                Size      = new Size(175, 45),
                Location  = new Point(12, 20 + i * 60),
                Cursor    = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(10, 0, 0, 0)
            };
            menuBtn.FlatAppearance.BorderSize = 0;
            menuBtn.Click += (s, e) =>
            {
                _roleFilter.SelectedIndex = 0;
                LoadData(key);
            };
            sidebarPanel.Controls.Add(menuBtn);
        }

        // Stats panel at bottom of sidebar
        Label statsLabel = new Label
        {
            Name      = "lblStats",
            Text      = "Loading…",
            Font      = new Font("Segoe UI", 9),
            ForeColor = Color.LightGray,
            AutoSize  = false,
            Dock      = DockStyle.Bottom,
            Height    = 80,
            TextAlign = ContentAlignment.TopLeft,
            Padding   = new Padding(12, 8, 0, 0)
        };
        sidebarPanel.Controls.Add(statsLabel);
        this.Controls.Add(sidebarPanel);

        // ── Main Content ──────────────────────────────────────────────────────
        Panel contentPanel = new Panel
        {
            Dock      = DockStyle.Fill,
            Padding   = new Padding(20),
            BackColor = Color.WhiteSmoke
        };

        // ── Search bar ────────────────────────────────────────────────────────
        Panel searchPanel = new Panel
        {
            Height    = 50,
            Dock      = DockStyle.Top,
            BackColor = Color.WhiteSmoke
        };

        _searchBox = new TextBox
        {
            Font        = new Font("Segoe UI", 11),
            Size        = new Size(280, 35),
            Location    = new Point(0, 8),
            BorderStyle = BorderStyle.FixedSingle,
            ForeColor   = Color.Gray,
            Text        = SearchPlaceholder
        };
        _searchBox.Enter += (s, e) =>
        {
            if (_searchBox.Text == SearchPlaceholder)
            { _searchBox.Text = ""; _searchBox.ForeColor = Color.Black; }
        };
        _searchBox.Leave += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_searchBox.Text))
            { _searchBox.Text = SearchPlaceholder; _searchBox.ForeColor = Color.Gray; }
        };
        _searchBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Return) ApplySearch(); };

        _roleFilter = new ComboBox
        {
            Font          = new Font("Segoe UI", 11),
            Size          = new Size(140, 35),
            Location      = new Point(295, 8),
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle     = FlatStyle.Flat
        };
        _roleFilter.Items.AddRange(new string[] { "All Roles", "Student", "Teacher" });
        _roleFilter.SelectedIndex = 0;

        Button searchButton = new Button
        {
            Text      = "Search",
            Font      = new Font("Segoe UI", 11),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            Size      = new Size(90, 35),
            Location  = new Point(445, 8),
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        searchButton.FlatAppearance.BorderSize = 0;
        searchButton.Click += (s, e) => ApplySearch();

        Button resetButton = new Button
        {
            Text      = "Reset",
            Font      = new Font("Segoe UI", 11),
            BackColor = Color.LightGray,
            Size      = new Size(80, 35),
            Location  = new Point(545, 8),
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        resetButton.FlatAppearance.BorderSize = 0;
        resetButton.Click += (s, e) =>
        {
            _searchBox.Text      = SearchPlaceholder;
            _searchBox.ForeColor = Color.Gray;
            _roleFilter.SelectedIndex = 0;
            LoadData("all");
        };

        searchPanel.Controls.Add(_searchBox);
        searchPanel.Controls.Add(_roleFilter);
        searchPanel.Controls.Add(searchButton);
        searchPanel.Controls.Add(resetButton);

        // ── Data grid ─────────────────────────────────────────────────────────
        _grid = new DataGridView
        {
            Dock                  = DockStyle.Fill,
            BackgroundColor       = Color.White,
            BorderStyle           = BorderStyle.None,
            RowHeadersVisible     = false,
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            ReadOnly              = true,
            SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            Font                  = new Font("Segoe UI", 10),
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.Fill,
            GridColor             = Color.LightGray,
            CellBorderStyle       = DataGridViewCellBorderStyle.SingleHorizontal,
            MultiSelect           = false,
        };

        _grid.ColumnHeadersDefaultCellStyle.BackColor  = Color.DarkSlateBlue;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor  = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Font       = new Font("Segoe UI", 11, FontStyle.Bold);
        _grid.ColumnHeadersHeight                      = 40;
        _grid.EnableHeadersVisualStyles                = false;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.AliceBlue;
        _grid.DefaultCellStyle.SelectionBackColor      = Color.LightSkyBlue;
        _grid.DefaultCellStyle.SelectionForeColor      = Color.DarkSlateBlue;
        _grid.RowTemplate.Height                       = 35;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colUsername",    HeaderText = "Username",       FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRole",        HeaderText = "Role",           FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colAge",         HeaderText = "Age",            FillWeight = 8  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colGamesPlayed", HeaderText = "Games Played",   FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colHighScore",   HeaderText = "High Score (s)", FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colLastPlayed",  HeaderText = "Last Played",    FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRegistered",  HeaderText = "Registered At",  FillWeight = 20 });

        // ── Bottom Action Bar ─────────────────────────────────────────────────
        Panel actionPanel = new Panel
        {
            Height    = 60,
            Dock      = DockStyle.Bottom,
            BackColor = Color.WhiteSmoke,
            Padding   = new Padding(0, 10, 0, 0)
        };

        Button selectButton = new Button
        {
            Text      = "Select",
            Font      = new Font("Segoe UI", 12, FontStyle.Bold),
            BackColor = Color.MediumSeaGreen,
            ForeColor = Color.White,
            Size      = new Size(120, 40),
            Location  = new Point(0, 10),
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        selectButton.FlatAppearance.BorderSize = 0;
        selectButton.Click += (s, e) =>
        {
            var u = SelectedUser();
            if (u == null) { MessageBox.Show("Select a row first.", "Select"); return; }
            MessageBox.Show(
                $"Username:    {u.PlayerName}\n" +
                $"Role:        {u.Role}\n" +
                $"Games Played:{u.GamesPlayed}\n" +
                $"High Score:  {(u.HighScore.HasValue ? $"{u.HighScore:F1}s" : "—")}\n" +
                $"Registered:  {u.RegisteredAt ?? "—"}",
                $"Profile — {u.PlayerName}",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        Button detailsButton = new Button
        {
            Text      = "View Details",
            Font      = new Font("Segoe UI", 12),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            Size      = new Size(130, 40),
            Location  = new Point(130, 10),
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        detailsButton.FlatAppearance.BorderSize = 0;
        detailsButton.Click += (s, e) =>
        {
            var u = SelectedUser();
            if (u == null) { MessageBox.Show("Select a row first.", "Details"); return; }
            MessageBox.Show(
                $"ID:          {u.Id}\n" +
                $"Username:    {u.PlayerName}\n" +
                $"Role:        {u.Role}\n" +
                $"Age:         {u.Age?.ToString() ?? "—"}\n" +
                $"Games Played:{u.GamesPlayed}\n" +
                $"High Score:  {(u.HighScore.HasValue ? $"{u.HighScore:F1}s" : "—")}\n" +
                $"Last Emotion:{u.LastEmotion}\n" +
                $"Last Played: {u.LastPlayed ?? "—"}\n" +
                $"Registered:  {u.RegisteredAt ?? "—"}",
                $"Details — {u.PlayerName}",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        Button editButton = new Button
        {
            Text      = "Edit Age",
            Font      = new Font("Segoe UI", 12),
            BackColor = Color.Goldenrod,
            ForeColor = Color.White,
            Size      = new Size(100, 40),
            Location  = new Point(270, 10),
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        editButton.FlatAppearance.BorderSize = 0;
        editButton.Click += (s, e) =>
        {
            var u = SelectedUser();
            if (u == null) { MessageBox.Show("Select a row first.", "Edit"); return; }

            string? input = ShowPrompt($"Enter new age for {u.PlayerName}:", "Edit Age",
                                       u.Age?.ToString() ?? "");
            if (string.IsNullOrWhiteSpace(input)) return;
            if (!int.TryParse(input.Trim(), out int age) || age < 1 || age > 120)
            { MessageBox.Show("Please enter a valid age (1–120).", "Invalid"); return; }

            if (ProfileManager.UpdateAge(u.PlayerName, u.Role, age))
            {
                LoadData("all");
                MessageBox.Show($"Age updated to {age} for {u.PlayerName}.", "Saved");
            }
            else MessageBox.Show("Could not update the record.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        Button deleteButton = new Button
        {
            Text      = "Delete",
            Font      = new Font("Segoe UI", 12),
            BackColor = Color.IndianRed,
            ForeColor = Color.White,
            Size      = new Size(100, 40),
            Location  = new Point(380, 10),
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        deleteButton.FlatAppearance.BorderSize = 0;
        deleteButton.Click += (s, e) =>
        {
            var u = SelectedUser();
            if (u == null) { MessageBox.Show("Select a row first.", "Delete"); return; }
            if (u.PlayerName == _teacher.PlayerName)
            { MessageBox.Show("You cannot delete your own account.", "Not Allowed", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var confirm = MessageBox.Show(
                $"Delete '{u.PlayerName}' ({u.Role})? This cannot be undone.",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            if (ProfileManager.DeleteUser(u.PlayerName, u.Role))
            {
                LoadData("all");
                MessageBox.Show($"'{u.PlayerName}' deleted.", "Deleted");
            }
            else MessageBox.Show("Could not delete the record.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        Button refreshButton = new Button
        {
            Text      = "↻ Refresh",
            Font      = new Font("Segoe UI", 12),
            BackColor = Color.SlateGray,
            ForeColor = Color.White,
            Size      = new Size(110, 40),
            Location  = new Point(490, 10),
            Cursor    = Cursors.Hand,
            FlatStyle = FlatStyle.Flat
        };
        refreshButton.FlatAppearance.BorderSize = 0;
        refreshButton.Click += (s, e) => LoadData("all");

        actionPanel.Controls.Add(selectButton);
        actionPanel.Controls.Add(detailsButton);
        actionPanel.Controls.Add(editButton);
        actionPanel.Controls.Add(deleteButton);
        actionPanel.Controls.Add(refreshButton);

        contentPanel.Controls.Add(_grid);
        contentPanel.Controls.Add(actionPanel);
        contentPanel.Controls.Add(searchPanel);

        this.Controls.Add(contentPanel);
        contentPanel.BringToFront();

        // Update sidebar stats label after controls are all set up
        this.Shown += (s, e) => UpdateStats();
    }

    private void UpdateStats()
    {
        int children = ProfileManager.GetAllChildren().Count;
        int teachers = ProfileManager.GetAllTeachers().Count;
        if (this.Controls.Find("lblStats", true).FirstOrDefault() is System.Windows.Forms.Label lbl)
            lbl.Text = $"Children: {children}\nTeachers: {teachers}\nTotal:    {children + teachers}";
    }

    // ── Simple input prompt (replaces VB InputBox) ───────────────────────

    private static string? ShowPrompt(string prompt, string title, string defaultValue = "")
    {
        using var dlg = new Form
        {
            Text            = title,
            Size            = new Size(360, 150),
            StartPosition   = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox     = false,
            MaximizeBox     = false
        };
        var lbl = new System.Windows.Forms.Label
            { Text = prompt, Left = 12, Top = 12, Width = 320, AutoSize = false, Height = 22 };
        var tb  = new TextBox
            { Left = 12, Top = 38, Width = 320, Text = defaultValue };
        var ok  = new Button
            { Text = "OK",     Left = 155, Width = 80, Top = 72, DialogResult = DialogResult.OK };
        var cancel = new Button
            { Text = "Cancel", Left = 248, Width = 80, Top = 72, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        return dlg.ShowDialog() == DialogResult.OK ? tb.Text : null;
    }

    // ── Designer boilerplate ──────────────────────────────────────────────────

    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        this.ClientSize = new System.Drawing.Size(1100, 700);
        this.Name = "AdminDashboardForm";
        this.ResumeLayout(false);
    }
}
