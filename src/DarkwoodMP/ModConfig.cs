using BepInEx.Configuration;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// The mod's settings (BepInEx/config/com.darkwoodmp.coop.cfg).
    /// </summary>
    public sealed class ModConfig
    {
        public ConfigEntry<int> SendRate;
        public ConfigEntry<bool> LogPlayerSprites;
        public ConfigEntry<bool> SkipIntro;
        public ConfigEntry<string> PlayerName;
        public ConfigEntry<int> DirectPort;
        public ConfigEntry<bool> OpenPort;
        public ConfigEntry<string> LastAddress;
        public ConfigEntry<string> Password;
        public ConfigEntry<string> ServerName;
        public ConfigEntry<int> MaxPlayers;
        public ConfigEntry<bool> LiveWorld;
        public ConfigEntry<bool> SnapshotOffThread;
        public ConfigEntry<string> NewCharacters;
        public ConfigEntry<KeyCode> SkillsKey;
        public ConfigEntry<string> HostWith;
        public ConfigEntry<int> StackSize;
        public ConfigEntry<bool> SoftPlayerCollision;

        /// <summary>
        /// The settings in BepInEx/config, with their defaults and the
        /// words the config file shows for each.
        /// </summary>
        public ModConfig(ConfigFile config)
        {
            PlayerName = config.Bind(
                "General", "PlayerName", "",
                "Name shown to other players. Empty = Steam name, or your "
                    + "OS user name.");
            SendRate = config.Bind(
                "Network", "SendRate", 20,
                "Player state updates per second.");
            ServerName = config.Bind(
                "Host", "ServerName", "Darkwood",
                "Your server's name in other players' server browsers.");
            DirectPort = config.Bind(
                "Host", "Port", TcpBackend.DefaultPort,
                "Port to host on: TCP for the game, UDP for the server "
                    + "browser.");
            OpenPort = config.Bind(
                "Host", "OpenPort", true,
                "When hosting with Direct, ask your router to forward the "
                    + "port (UPnP or NAT-PMP), and close it again when you "
                    + "stop. Off: forward it by hand.");
            Password = config.Bind(
                "Host", "Password", "",
                "Password players need to join your server (empty for "
                    + "none).");
            MaxPlayers = config.Bind(
                "Host", "MaxPlayers", Protocol.MaxPlayers,
                "Most players at once, you included.");
            LiveWorld = config.Bind(
                "Host", "LiveWorld", true,
                "Players who join get the world as it is now (a snapshot "
                    + "saved for them, your own save untouched). Off: as "
                    + "you last saved it.");
            SnapshotOffThread = config.Bind(
                "Host", "SnapshotOffThread", true,
                "Write the snapshot for a joiner on a worker thread, so "
                    + "your game stops only while the world is read. Off: "
                    + "the game saves it its own way, all at once.");
            NewCharacters = config.Bind(
                "Host", "NewCharacters", "fresh",
                new ConfigDescription(
                    "Someone joining your world for the first time starts "
                        + "with: fresh (no items, full health, beside you) "
                        + "or copy (a copy of your character).",
                    new AcceptableValueList<string>("fresh", "copy")));
            HostWith = config.Bind(
                "Host", "HostWith", "steam",
                new ConfigDescription(
                    "How the Start Game window hosts: steam (friends join "
                        + "by invite) or direct (IP and port). Without "
                        + "Steam (GOG) it's always direct.",
                    new AcceptableValueList<string>("steam", "direct")));
            StackSize = config.Bind(
                "Host", "StackSize", 50,
                new ConfigDescription(
                    "How many of an item one inventory slot holds, for "
                        + "every item that stacks (players in your game "
                        + "use yours). Items that already stack higher "
                        + "keep theirs. 0: the game's own sizes.",
                    new AcceptableValueRange<int>(0, 999)));
            LastAddress = config.Bind(
                "Network", "LastAddress", "",
                "Last server joined directly.");
            SkipIntro = config.Bind(
                "General", "SkipIntro", true,
                "Start without the studio logo (about 6 seconds sooner at "
                    + "the main menu).");
            SkillsKey = config.Bind(
                "General", "SkillsKey", KeyCode.K,
                "Key that opens and closes the Skills panel (spend skill "
                    + "points).");
            SoftPlayerCollision = config.Bind(
                "General", "SoftPlayerCollision", true,
                "Players standing in each other are eased apart, gently "
                    + "(no hard bump). Off: players pass through each "
                    + "other.");
            LogPlayerSprites = config.Bind(
                "Debug", "LogPlayerSprites", false,
                "Log every sprite in the player hierarchy once (which are "
                    + "synced/skipped).");
        }
    }
}
