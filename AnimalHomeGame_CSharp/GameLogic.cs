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

    public static bool CheckDropMatch(GameItem animal, GameItem home)
    {
        if (animal.TargetHomeName != home.Name)
            return false;

        return animal.Picture.Bounds.IntersectsWith(home.Picture.Bounds);
    }
}
