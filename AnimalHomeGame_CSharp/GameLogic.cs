using System;
using System.Drawing;
using System.Windows.Forms;

namespace AnimalHomeGame_CSharp;

public class GameItem
{
    public string Name { get; set; } = "";
    
    public int TuioId { get; set; } = -1;
    
    public PictureBox Picture { get; set; } = null!;
    public Point OriginalLocation { get; set; }
    
    public string TargetHomeName { get; set; } = "";
    
    public bool IsMatched { get; set; } = false;
}

public static class GameLogic
{
    public static bool ValidateTuioId(GameItem animal, int symbolId)
    {
        return animal.TuioId == symbolId;
    }

    /// <summary>
    /// Returns true when the animal is close enough to the correct home.
    /// Two checks are used — whichever passes first:
    ///   1. Pixel intersection (works perfectly for mouse drag).
    ///   2. Centre-to-centre distance ≤ 160 px (forgiving for TUIO placement).
    /// </summary>
    public static bool CheckDropMatch(GameItem animal, GameItem home)
    {
        if (animal.TargetHomeName != home.Name)
            return false;

        // ── Check 1: pixel overlap (mouse drag) ──────────────────────────────
        if (animal.Picture.Bounds.IntersectsWith(home.Picture.Bounds))
            return true;

        // ── Check 2: centre distance (TUIO — generous 160 px tolerance) ──────
        Point ac = new Point(
            animal.Picture.Left + animal.Picture.Width  / 2,
            animal.Picture.Top  + animal.Picture.Height / 2);
        Point hc = new Point(
            home.Picture.Left + home.Picture.Width  / 2,
            home.Picture.Top  + home.Picture.Height / 2);

        double dist = Math.Sqrt(
            Math.Pow(ac.X - hc.X, 2) +
            Math.Pow(ac.Y - hc.Y, 2));

        return dist <= 160;
    }
}
