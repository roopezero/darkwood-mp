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
        private Seconds _nextWorldLabel;
        private Seconds _nextPresence;
        private string _lastPresence;
        private bool _biomeKeysLogged;

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
            var now = new Seconds(Time.unscaledTime);
            if (PresenceRules.LabelDue(_host.Tcp.IsHost, now, _nextWorldLabel))
            {
                _nextWorldLabel = PresenceRules.NextLabel(now);
                _host.Tcp.WorldLabel = WorldLabel();
            }

            var timer = new PresenceTimerFacts
            {
                SteamUp = _host.Steam != null,
                Logged = Logged,
                Now = now,
                Next = _nextPresence,
            };

            if (PresenceRules.PresenceDue(timer))
            {
                _nextPresence = PresenceRules.NextPresence(now);
                UpdatePresence();
            }
        }

        /// <summary>
        /// The server browser's World column for this host: the day and
        /// chapter its world is on.
        /// </summary>
        private static string WorldLabel()
        {
            try
            {
                if (!WorldShare.InWorld)
                {
                    return PresenceRules.WorldLabel(false, 0, 0);
                }

                var (day, chapter) = DayAndChapter();
                return PresenceRules.WorldLabel(true, day, chapter);
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
                text = PresenceRules.Text(FactsOf(_host.Session));
            }
            catch (Exception e)
            {
                text = PresenceRules.Fallback(PluginInfo.Version);
                Plugin.Log.LogWarning($"Steam presence: {e.Message}");
            }

            text = PresenceRules.Cut(text);
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

        // Non-inlined, like SteamStartup's CreateSteamBackend: names
        // SteamBackend, so it runs only once Steam is there.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SetSteamPresence(string text, bool log)
        {
            if (!(_host.Steam is SteamBackend sb))
            {
                return;
            }

            int players = 0;
            if (_host.Session.Active)
            {
                players = _host.Session.PlayerCount;
            }

            sb.SetPresence(
                text,
                PresenceRules.SteamPlayers(_host.Session.Active, players));
            if (log)
            {
                Plugin.Log.LogInfo(
                    $"[presence] {text} (Steam has: {sb.PresenceReadBack()})");
            }
        }

        /// <summary>
        /// The game and the session as the presence tells of them. The world
        /// is read only while one is loaded, and the session only while it
        /// runs.
        /// </summary>
        private PresenceFacts FactsOf(Session session)
        {
            if (!WorldShare.InWorld)
            {
                return new PresenceFacts { Version = PluginInfo.Version };
            }

            var (day, chapter) = DayAndChapter();
            string biome = BiomeName();
            if (!session.Active)
            {
                return new PresenceFacts
                {
                    Version = PluginInfo.Version,
                    InWorld = true,
                    Day = day,
                    Chapter = chapter,
                    Biome = biome,
                };
            }

            session.Names.TryGetValue(session.HostId, out string host);
            return new PresenceFacts
            {
                Version = PluginInfo.Version,
                InWorld = true,
                Day = day,
                Chapter = chapter,
                Biome = biome,
                InSession = true,
                IsHost = session.IsHost,
                Players = session.PlayerCount,
                MaxPlayers = session.Backend.MaxPlayers,
                HostName = host,
            };
        }

        /// <summary>
        /// Where our player is, for the biome's name.
        /// </summary>
        private static WhereFacts FactsOf(Player player)
        {
            if (player == null || player.whereAmI == null)
            {
                return new WhereFacts();
            }

            bool outside = Singleton<OutsideLocations>.Instance != null
                && Singleton<OutsideLocations>.Instance
                    .playerInOutsideLocation;
            var biome = player.whereAmI.currentInfoBiome;
            if (biome == null)
            {
                return new WhereFacts { HasPlayer = true, Outside = outside };
            }

            return new WhereFacts
            {
                HasPlayer = true,
                Outside = outside,
                HasBiome = true,
                Kind = (BiomeKind)(int)biome.type,
            };
        }

        /// <summary>
        /// The name of the part of the map our player is in, as the game's
        /// map screen names it (its "UI" sheet), or an English name if the
        /// sheet can't be read. Empty inside a separate location (a cave, an
        /// interior), where the biome doesn't apply.
        /// </summary>
        private string BiomeName()
        {
            var where = FactsOf(Player.Instance);
            if (!PresenceRules.InBiome(where))
            {
                return "";
            }

            string sheet = "";
            try
            {
                LogBiomeKeys();
                sheet = Language.Get(PresenceRules.BiomeKey(where.Kind), "UI");
            }
            catch
            {
            }

            return PresenceRules.BiomeName(where.Kind, sheet);
        }

        /// <summary>
        /// Once: the biome names the game's "UI" sheet has.
        /// </summary>
        private void LogBiomeKeys()
        {
            if (_biomeKeysLogged)
            {
                return;
            }

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
    }
}
