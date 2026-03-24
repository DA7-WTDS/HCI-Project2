using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AnimalHomeGame_CSharp
{
    public class MenuRenderer
    {
        private readonly Font titleFont = new Font("Segoe UI", 16f, FontStyle.Bold);
        private readonly Font menuFont = new Font("Segoe UI", 18f, FontStyle.Bold);
        private readonly Font menuSmall = new Font("Segoe UI", 10f);
        private readonly Font smallFont = new Font("Segoe UI", 9f);

        private static readonly (string Label, string Sub, Color Color)[] Options =
        {
            ("PLAY",  "Match animals\nto their homes", Color.FromArgb(34, 180, 100)),
            ("QUIZ",  "Test your\nanimal knowledge",   Color.FromArgb(60, 130, 220)),
            ("LEARN", "Discover\nanimal habitats",     Color.FromArgb(180, 90, 200)),
        };

        private const float SWEEP = 120f;

        public void Draw(
            Graphics g,
            int width, int height,
            int currentSelection,
            float rotationAngle,
            bool navPresent,
            bool selectPresent,
            PointF menuNavPos,
            PointF menuSelectPos,
            float confirmProximity,
            int MENU_NAV_ID,
            int MENU_SELECT_ID)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // Background
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(18, 22, 55)))
                g.FillRectangle(bg, 0, 0, width, height);

            // Title
            string title = "Animal Home Game";
            using (SolidBrush tb = new SolidBrush(Color.White))
                g.DrawString(title, titleFont, tb, width / 2f - 80, 24f);

            int cx = width / 2;
            int cy = height / 2 + 10;

            int R = Math.Min(width, height) / 3;
            int ri = R / 2;

            Rectangle outerRect = new Rectangle(cx - R, cy - R, R * 2, R * 2);
            Rectangle innerRect = new Rectangle(cx - ri, cy - ri, ri * 2, ri * 2);

            StringFormat center = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            float anglePer = 360f / Options.Length;

            float normAngle = (rotationAngle % 360 + 360) % 360;

            // 🔥 correct selection from angle
            int autoSelection = (int)((normAngle + anglePer / 2f) / anglePer) % Options.Length;

            for (int i = 0; i < Options.Length; i++)
            {
                var opt = Options[i];
                bool sel = (i == autoSelection);

                float start = -90f + (i * anglePer) + rotationAngle;

                Color fill = sel
                    ? Color.FromArgb(200, opt.Color)
                    : Color.FromArgb(70, opt.Color);

                using (SolidBrush sb = new SolidBrush(fill))
                    g.FillPie(sb, outerRect, start, anglePer);

                using (Pen p = new Pen(opt.Color, sel ? 3f : 1f))
                    g.DrawPie(p, outerRect, start, anglePer);

                float mid = start + anglePer / 2f;
                float rad = mid * (float)Math.PI / 180f;

                float lx = cx + (R * 0.65f) * (float)Math.Cos(rad);
                float ly = cy + (R * 0.65f) * (float)Math.Sin(rad);

                using (SolidBrush t = new SolidBrush(Color.White))
                    g.DrawString(opt.Label, menuFont, t, lx - 30, ly - 10);
            }

            // center hole
            using (SolidBrush hole = new SolidBrush(Color.FromArgb(18, 22, 55)))
                g.FillEllipse(hole, innerRect);

            using (StringFormat sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            using (SolidBrush c = new SolidBrush(Color.White))
                g.DrawString(Options[autoSelection].Label, menuFont, c,
                    new RectangleF(cx - ri, cy - ri, ri * 2, ri * 2), sf);

            // confirm arc
            if (selectPresent && confirmProximity > 0f)
            {
                using (Pen arc = new Pen(Color.Gold, 4))
                    g.DrawArc(arc, outerRect, -90, 360f * confirmProximity);
            }

            DrawDot(g, menuNavPos, Color.Lime, $"NAV #{MENU_NAV_ID}");
            DrawDot(g, menuSelectPos, Color.DeepSkyBlue, $"SELECT #{MENU_SELECT_ID}");
        }

        private void DrawDot(Graphics g, PointF p, Color c, string label)
        {
            int r = 14;

            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, c)))
                g.FillEllipse(b, p.X - r, p.Y - r, r * 2, r * 2);

            using (Pen pen = new Pen(c, 2))
                g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2);

            using (SolidBrush t = new SolidBrush(Color.White))
                g.DrawString(label, smallFont, t, p.X + r + 4, p.Y - 8);
        }
    }
}
