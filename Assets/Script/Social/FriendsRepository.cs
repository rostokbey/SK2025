using System.IO;
using UnityEngine;

public static class FriendsRepository
{
    static string FilePath =>
        Path.Combine(Application.persistentDataPath, "friends_" + SocialIdentity.ProfileId + ".json");

    public static FriendsTable Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new FriendsTable();
            var json = File.ReadAllText(FilePath);
            return JsonUtility.FromJson<FriendsTable>(json) ?? new FriendsTable();
        }
        catch { return new FriendsTable(); }
    }

    public static void Save(FriendsTable t)
    {
        try
        {
            var json = JsonUtility.ToJson(t, true);
            File.WriteAllText(FilePath, json);
        }
        catch { /* ignore */ }
    }
}
