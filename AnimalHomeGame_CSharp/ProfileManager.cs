using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AnimalHomeGame_CSharp;

public static class ProfileManager
{
    private const string FilePath = "users.json";

    public static List<UserProfile> LoadProfiles()
    {
        if (!File.Exists(FilePath))
        {
            return new List<UserProfile>();
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<UserProfile>>(json) ?? new List<UserProfile>();
        }
        catch
        {
            return new List<UserProfile>();
        }
    }

    public static void SaveProfiles(List<UserProfile> profiles)
    {
        string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
