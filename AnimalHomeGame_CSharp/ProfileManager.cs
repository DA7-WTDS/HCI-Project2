using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimalHomeGame_CSharp;

/// <summary>
/// Reads children and teachers from the JSON databases written by database.py.
/// C# is read-only here — Python owns all writes.
/// </summary>
public static class ProfileManager
{
    // database.py writes these files next to ai_vision.py / database.py
    private static readonly string BaseDir =
        Path.Combine(AppContext.BaseDirectory, "..");   // one level up from bin/

    private static string ChildrenPath =>
        Path.Combine(AppContext.BaseDirectory, "children_db.json");

    private static string TeachersPath =>
        Path.Combine(AppContext.BaseDirectory, "teachers_db.json");

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // ── lookup by username ────────────────────────────────────────────────

    public static UserProfile? FindChild(string username)
        => LoadDict(ChildrenPath).TryGetValue(username, out var p) ? p : null;

    public static UserProfile? FindTeacher(string username)
        => LoadDict(TeachersPath).TryGetValue(username, out var p) ? p : null;

    public static UserProfile? Find(string username, string role) =>
        role == "teacher" ? FindTeacher(username) : FindChild(username);

    // ── list helpers (for future dashboards) ─────────────────────────────

    public static List<UserProfile> GetAllChildren()
    {
        var list = new List<UserProfile>(LoadDict(ChildrenPath).Values);
        list.Sort((a, b) => string.Compare(a.PlayerName, b.PlayerName,
                                           StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static List<UserProfile> GetAllTeachers()
    {
        var list = new List<UserProfile>(LoadDict(TeachersPath).Values);
        list.Sort((a, b) => string.Compare(a.PlayerName, b.PlayerName,
                                           StringComparison.OrdinalIgnoreCase));
        return list;
    }

    // ── internal loader ───────────────────────────────────────────────────

    private static Dictionary<string, UserProfile> LoadDict(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            string json = File.ReadAllText(path);
            // database.py stores {"username": { "id":..., "username":..., ... }, ...}
            var raw = JsonSerializer.Deserialize<Dictionary<string, RawDbRecord>>(json, _opts)
                      ?? new();

            var result = new Dictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, r) in raw)
            {
                result[key] = new UserProfile
                {
                    Id           = r.Id          ?? key,
                    PlayerName   = r.Username    ?? key,
                    Role         = r.Role        ?? "child",
                    Age          = r.Age,
                    HighScore    = r.HighScore,
                    GamesPlayed  = r.GamesPlayed,
                    LastEmotion  = r.LastEmotion ?? "neutral",
                    LastPlayed   = r.LastPlayed,
                    RegisteredAt = r.RegisteredAt,
                };
            }
            return result;
        }
        catch
        {
            return new();
        }
    }

    // ── raw DTO that mirrors database.py JSON schema ──────────────────────

    private class RawDbRecord
    {
        [JsonPropertyName("id")]            public string?  Id           { get; set; }
        [JsonPropertyName("username")]      public string?  Username     { get; set; }
        [JsonPropertyName("role")]          public string?  Role         { get; set; }
        [JsonPropertyName("age")]           public int?     Age          { get; set; }
        [JsonPropertyName("high_score")]    public double?  HighScore    { get; set; }
        [JsonPropertyName("games_played")]  public int      GamesPlayed  { get; set; }
        [JsonPropertyName("last_emotion")]  public string?  LastEmotion  { get; set; }
        [JsonPropertyName("last_played")]   public string?  LastPlayed   { get; set; }
        [JsonPropertyName("registered_at")] public string?  RegisteredAt { get; set; }
    }

    // ── admin write operations ────────────────────────────────────────────
    // C# writes directly to the same JSON format database.py uses.

    public static bool DeleteUser(string username, string role)
    {
        string path = role == "teacher" ? TeachersPath : ChildrenPath;
        if (!File.Exists(path)) return false;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.Nodes.JsonObject>>(
                          File.ReadAllText(path), _opts) ?? new();
            if (!raw.Remove(username)) return false;
            File.WriteAllText(path,
                JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch { return false; }
    }

    public static bool UpdateAge(string username, string role, int age)
    {
        string path = role == "teacher" ? TeachersPath : ChildrenPath;
        if (!File.Exists(path)) return false;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.Nodes.JsonObject>>(
                          File.ReadAllText(path), _opts) ?? new();
            if (!raw.TryGetValue(username, out var obj)) return false;
            obj["age"] = age;
            File.WriteAllText(path,
                JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch { return false; }
    }
}
