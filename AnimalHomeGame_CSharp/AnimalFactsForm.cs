using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AnimalHomeGame_CSharp;

public class AnimalFactsForm : Form
{
    private Label factLabel;
    private ListBox animalListBox;
    private TuioHandler tuioHandler;
    private Point? tuioCursorPosition = null;

    private readonly Dictionary<string, string> animalFacts = new()
    {
        { "Bird", "Birds have hollow bones which help them fly." },
        { "Dog", "A dog's sense of smell is 10,000 to 100,000 times more sensitive than humans." },
        { "Fish", "Some fish can swim backwards, but most only swim forward." },
        { "Horse", "Horses can sleep both lying down and standing up." },
        { "Cat", "Cats spend 70% of their lives sleeping." },
        { "Lion", "A lion's roar can be heard up to 5 miles away." },
        { "Elephant", "Elephants are the only mammals that can't jump." }
    };

    public AnimalFactsForm()
    {
        InitializeComponent();
        SetupGUI();
        SetupTuio();
    }

    private void SetupTuio()
    {
        tuioHandler = new TuioHandler();
        tuioHandler.OnObjectAdded += HandleTuioAdded;
        tuioHandler.OnObjectUpdated += HandleTuioUpdated;
        tuioHandler.OnObjectRemoved += HandleTuioRemoved;
        tuioHandler.Start();
    }

    private void HandleTuioUpdated(int symbolId, float normX, float normY)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        this.Invoke(new Action(() =>
        {
            if (symbolId == 7) // HOVER Marker
            {
                UpdateCursor(normX, normY);
            }
        }));
    }

    private void HandleTuioAdded(int symbolId, float normX, float normY)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        this.Invoke(new Action(() =>
        {
            if (symbolId == 7) // HOVER Marker
            {
                UpdateCursor(normX, normY);
            }
            else if (symbolId == 8) // CLICK Marker
            {
                TriggerClick();
            }
        }));
    }

    private void HandleTuioRemoved(int symbolId, float normX, float normY)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        if (symbolId == 7)
        {
            this.Invoke(new Action(() =>
            {
                tuioCursorPosition = null;
                this.Invalidate();
            }));
        }
    }

    private void UpdateCursor(float normX, float normY)
    {
        int pixelX = (int)(normX * this.ClientSize.Width);
        int pixelY = (int)(normY * this.ClientSize.Height);
        tuioCursorPosition = new Point(pixelX, pixelY);

        Point clientPos = animalListBox.PointToClient(this.PointToScreen(tuioCursorPosition.Value));
        int hoverIndex = animalListBox.IndexFromPoint(clientPos);
        
        // If the cursor is actually inside the listbox bounds
        if (clientPos.X >= 0 && clientPos.X <= animalListBox.Width &&
            clientPos.Y >= 0 && clientPos.Y <= animalListBox.Height)
        {
            if (hoverIndex >= 0 && hoverIndex < animalListBox.Items.Count)
            {
                animalListBox.SelectedIndex = hoverIndex;
            }
        }

        this.Invalidate();
    }

    private void TriggerClick()
    {
        if (animalListBox.SelectedItem is string selectedAnimal)
        {
            if (animalFacts.TryGetValue(selectedAnimal, out string fact))
            {
                factLabel.Text = $"Did you know?\n\n{fact}";
            }
        }
    }

    private void SetupGUI()
    {
        this.Text = "Fun Animal Facts!";
        this.Size = new Size(600, 400);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.WhiteSmoke;
        this.DoubleBuffered = true;
        this.Paint += AnimalFactsForm_Paint;
        this.FormClosed += AnimalFactsForm_FormClosed;

        Label titleLabel = new Label
        {
            Text = "Marker 7: Move Cursor | Marker 8: Show Fact",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.DarkSlateBlue,
            Dock = DockStyle.Top,
            Height = 50,
            TextAlign = ContentAlignment.MiddleCenter
        };

        animalListBox = new ListBox
        {
            Font = new Font("Segoe UI", 14),
            Dock = DockStyle.Left,
            Width = 200,
            BackColor = Color.LightYellow,
            Cursor = Cursors.Hand,
            IntegralHeight = false
        };

        foreach (var animal in animalFacts.Keys)
        {
            animalListBox.Items.Add(animal);
        }

        // Allow normal mouse clicks to work as well!
        animalListBox.MouseClick += (s, e) => TriggerClick();

        factLabel = new Label
        {
            Text = "Hover over an animal with Marker 7\nthen put down Marker 8 to click!",
            Font = new Font("Segoe UI", 14, FontStyle.Italic),
            ForeColor = Color.DarkGreen,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(20)
        };

        this.Controls.Add(factLabel);
        this.Controls.Add(animalListBox);
        this.Controls.Add(titleLabel);
    }

    private void AnimalFactsForm_Paint(object sender, PaintEventArgs e)
    {
        if (tuioCursorPosition.HasValue)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int radius = 15;
            using (SolidBrush brush = new SolidBrush(Color.Red))
            {
                e.Graphics.FillEllipse(brush, 
                    tuioCursorPosition.Value.X - radius, 
                    tuioCursorPosition.Value.Y - radius, 
                    radius * 2, radius * 2);
            }
        }
    }

    private void AnimalFactsForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        tuioHandler?.Stop();
        tuioHandler?.Dispose();
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        this.ClientSize = new Size(600, 400);
        this.Name = "AnimalFactsForm";
        this.ResumeLayout(false);
    }
}
