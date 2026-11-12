using System;
using System.IO;
using System.Security.Cryptography;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// No accounts, as in goasis: the mod makes a random player key on first start and gives it to every host
    /// it joins, which knows the player by it. Kept in BepInEx/config/DarkwoodMP.player.key — keep it secret,
    /// and copy it to another computer to be the same player there.
    /// </summary>
    public static class PlayerKey
    {
        public static string Load(string configDir)
        {
            string path = Path.Combine(configDir, "DarkwoodMP.player.key");
            try
            {
                if (File.Exists(path))
                {
                    string k = File.ReadAllText(path).Trim();
                    if (k.Length >= 32) return k;
                }
                var bytes = new byte[32];
                // Qualified: the game has a RandomNumberGenerator of its own in the global namespace.
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                string key = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                File.WriteAllText(path, key);
                Plugin.Log.LogInfo($"Made a new player key in {path}");
                return key;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't read or write the player key ({e.Message}); using a temporary one");
                return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            }
        }
    }
}
