using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Net.Presence
{
    /// <summary>
    /// What our friends and the server browser are told about our game: the
    /// Steam rich presence and a host's World column. Process state, on
    /// ModHost: the main menu has a presence too.
    /// </summary>
    internal sealed class PresenceReport
    {
        private readonly ModHost _host;

        public PresenceReport(ModHost host)
        {
            _host = host;
        }

        /// <summary>
        /// --dwmp-test-presence: log the presence text every 5 s even without
        /// Steam, to check it.
        /// </summary>
        public bool Logged;

        /// <summary>
        /// Each frame: a host's World column every 2 s, the presence every
        /// 5 s.
        /// </summary>
        public void Update()
        {
            if (_host.Tcp.IsHost && Time.unscaledTime >= _nextWorldLabel)
            {
                _nextWorldLabel = Time.unscaledTime + 2f;
                _host.Tcp.WorldLabel = WorldLabel();
            }

            if ((_host.Steam != null || Logged)
                && Time.unscaledTime >= _nextPresence)
            {
                _nextPresence = Time.unscaledTime + 5f;
                UpdatePresence();
            }
        }

        private float _nextWorldLabel;
        private float _nextPresence;
        private string _lastPresence;

        /// <summary>
        /// The server browser's World column for this host: the day and chapter
        /// its world is on.
        /// </summary>
        private static string WorldLabel()
        {
            try
            {
                if (!WorldShare.InWorld)
                {
                    return "";
                }

                var (day, chapter) = DayAndChapter();
                if (chapter > 0)
                {
                    return $"Day {day}, chapter {chapter}";
                }

                return $"Day {day}";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// The world's day, and its chapter (0 when there's no world
        /// generator).
        /// </summary>
        private static (int day, int chapter) DayAndChapter()
        {
            int day = Singleton<Controller>.Instance.day;
            int chapter = 0;
            if (Singleton<WorldGenerator>.Instance != null)
            {
                chapter = Singleton<WorldGenerator>.Instance.chapterID;
            }

            return (day, chapter);
        }

        /// <summary>
        /// Our Steam rich presence: what friends see in "View game info" (see
        /// SteamBackend.SetPresence).
        /// </summary>
        private void UpdatePresence()
        {
            string text;
            try
            {
                text = PresenceText();
            }
            catch (Exception e)
            {
                text = $"DarkwoodMP {PluginInfo.Version}";
                Plugin.Log.LogWarning($"Steam presence: {e.Message}");
            }

            if (text.Length > 256)
            {
                text = text.Substring(0, 256); // Steam's limit for a value
            }

            bool changed = text != _lastPresence;
            _lastPresence = text;
            if (_host.Steam == null)
            {
                if (changed)
                {
                    Plugin.Log.LogInfo(
                        $"[presence] {text} (no Steam: not set)");
                }

                return;
            }

            try
            {
                SetSteamPresence(text, changed);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't set the Steam presence: {e.Message}");
            }
        }

        // Non-inlined, like CreateSteamBackend: names SteamBackend, so it runs
        // only once Steam is there.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetSteamPresence(string text, bool log)
        {
            if (!(_host.Steam is SteamBackend sb))
            {
                return;
            }

            int players = 1;
            if (_host.Session.Active)
            {
                players = _host.Session.PlayerCount;
            }

            sb.SetPresence(text, players);
            if (log)
            {
                Plugin.Log.LogInfo(
                    $"[presence] {text} (Steam has: {sb.PresenceReadBack()})");
            }
        }

        /// <summary>
        /// "DarkwoodMP 0.22.0 · Chapter 2, day 5 · Silent Forest · Hosting, 3/5
        /// players", or "... · In the main menu".
        /// </summary>
        private string PresenceText()
        {
            var parts = new List<string> { $"DarkwoodMP {PluginInfo.Version}" };
            if (!WorldShare.InWorld)
            {
                parts.Add("In the main menu");
                return string.Join(" · ", parts);
            }

            var (day, chapter) = DayAndChapter();
            string item;
            if (chapter > 0)
            {
                item = $"Chapter {Mathf.Clamp(chapter, 1, 10)}, day {day}";
            }
            else
            {
                item = $"Day {day}";
            }

            parts.Add(item);
            string biome = BiomeName();
            if (!string.IsNullOrEmpty(biome))
            {
                parts.Add(biome);
            }

            if (!_host.Session.Active)
            {
                parts.Add("Playing alone");
            }
            else
            {
                string count =
                    $"{_host.Session.PlayerCount}/"
                        + $"{_host.Session.Backend.MaxPlayers} players";
                if (_host.Session.IsHost)
                {
                    parts.Add($"Hosting, {count}");
                }
                else if (_host.Session.Names.TryGetValue(
                    _host.Session.HostId, out var host)
                    && !string.IsNullOrEmpty(host))
                {
                    parts.Add($"In {host}'s game, {count}");
                }
                else
                {
                    parts.Add($"In co-op, {count}");
                }
            }

            return string.Join(" · ", parts);
        }

        private bool _biomeKeysLogged;

        /// <summary>
        /// The name of the part of the map our player is in, as the game's map
        /// screen names it (its "UI" sheet, key "biome_" + the biome type), or
        /// an English name if the sheet can't be read. Empty inside a separate
        /// location (a cave, an interior), where the biome doesn't apply.
        /// </summary>
        private string BiomeName()
        {
            if (Player.Instance == null || Player.Instance.whereAmI == null)
            {
                return "";
            }

            if (Singleton<OutsideLocations>.Instance != null
                && Singleton<OutsideLocations>.Instance.playerInOutsideLocation)
            {
                return "";
            }

            var b = Player.Instance.whereAmI.currentInfoBiome;
            if (b == null || b.type == Biome.Type.empty)
            {
                return "";
            }

            // The game's sheet has biome_forest, _forest_mutated, _meadow and
            // _swamp; dense forest is forest on its map.
            Biome.Type biomeType;
            if (b.type == Biome.Type.forest_dense)
            {
                biomeType = Biome.Type.forest;
            }
            else
            {
                biomeType = b.type;
            }

            string key = "biome_" + biomeType;
            string name = "";
            try
            {
                if (!_biomeKeysLogged)
                {
                    _biomeKeysLogged = true;
                    var keys = new List<string>();
                    foreach (var k in Language.GetAllKeysForSheet("UI").Keys)
                    {
                        if (k.StartsWith("biome"))
                        {
                            keys.Add(k);
                        }
                    }

                    Plugin.Log.LogInfo(
                        "[presence] the game's biome names: "
                            + $"{string.Join(", ", keys)}");
                }

                name = Language.Get(key, "UI");
            }
            catch
            {
            }

            if (!string.IsNullOrEmpty(name) && name != key)
            {
                return name;
            }

            switch (b.type)
            {
                case Biome.Type.meadow:
                    return "Dry Meadow";
                case Biome.Type.forest:
                case Biome.Type.forest_dense:
                    return "Silent Forest";
                case Biome.Type.swamp:
                    return "Swamp";
                default:
                    return "";
            }
        }
    }
}
