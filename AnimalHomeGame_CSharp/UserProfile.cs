namespace AnimalHomeGame_CSharp;

public class UserProfile
{
    public string  Id           { get; set; } = string.Empty;
    public string  PlayerName   { get; set; } = string.Empty;  // "username" in DB
    public string  Role         { get; set; } = "child";       // "child" | "teacher"
    public int?    Age          { get; set; }
    public double? HighScore    { get; set; }   // best time in seconds (lower = better)
    public int     GamesPlayed  { get; set; }
    public string  LastEmotion  { get; set; } = "neutral";
    public string? LastPlayed   { get; set; }
    public string? RegisteredAt { get; set; }
}
