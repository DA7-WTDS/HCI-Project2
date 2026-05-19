using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AnimalHomeGame_CSharp;

/// <summary>
/// Shown to teachers after login. Teacher dashboard will be built here.
/// </summary>
public class TeacherDashboardForm : Form
{
    private readonly UserProfile _teacher;
    private readonly MainForm    _mainForm;

    public TeacherDashboardForm(UserProfile teacher, MainForm mainForm)
    {
        _teacher  = teacher;
        _mainForm = mainForm;
        InitUI();
    }

    private void InitUI()
    {
        this.Text            = "Teacher Dashboard — Animal Home Game";
        this.Size            = new Size(900, 620);
        this.StartPosition   = FormStartPosition.CenterScreen;
        this.BackColor       = Color.FromArgb(18, 22, 38);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox     = false;
        this.FormClosed     += (s, e) => { _mainForm.ResetScanner(); _mainForm.Show(); };

        // ── Header ──────────────────────────────────────────────────────────
        Panel header = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 110,
            BackColor = Color.Transparent,
        };
        header.Paint += (s, e) =>
        {
            using var brush = new LinearGradientBrush(
                header.ClientRectangle,
                Color.FromArgb(60, 80, 200),
                Color.FromArgb(30, 40, 120),
                LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, header.ClientRectangle);
        };

        Label title = new Label
        {
            Text      = $"👩‍🏫  Welcome, {_teacher.PlayerName}!",
            Font      = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock      = DockStyle.Fill,
            Padding   = new Padding(30, 0, 0, 0),
        };
        header.Controls.Add(title);
        this.Controls.Add(header);

        // ── Body ────────────────────────────────────────────────────────────
        Panel body = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding   = new Padding(40),
        };

        Label coming = new Label
        {
            Text      = "📊  Teacher Dashboard\n\nThis screen will show:\n\n" +
                        "  •  All registered children and their scores\n" +
                        "  •  High-score leaderboard\n" +
                        "  •  Individual session history\n" +
                        "  •  Emotion trends per child\n" +
                        "  •  Manage / delete student profiles\n\n" +
                        "Coming soon — check back after the next sprint! 🚀",
            Font      = new Font("Segoe UI", 13),
            ForeColor = Color.FromArgb(200, 220, 255),
            AutoSize  = false,
            TextAlign = ContentAlignment.TopLeft,
            Dock      = DockStyle.Fill,
        };
        body.Controls.Add(coming);
        this.Controls.Add(body);

        // ── Footer / Logout ──────────────────────────────────────────────────
        Panel footer = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 60,
            BackColor = Color.FromArgb(25, 30, 55),
        };

        Button logout = new Button
        {
            Text      = "🚪  Logout",
            Font      = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(180, 40, 40),
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(140, 36),
            Location  = new Point(footer.Width - 160, 12),
            Anchor    = AnchorStyles.Right | AnchorStyles.Top,
            Cursor    = Cursors.Hand,
        };
        logout.FlatAppearance.BorderSize = 0;
        logout.Click += (s, e) => this.Close();
        footer.Controls.Add(logout);
        this.Controls.Add(footer);
    }
}
