using System;
using System.Collections.Generic;
using System.IO;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Progress
{
    // The host's side of the shared progression: the record beside the save,
    // the shared numbers sent to everyone, and its checks of the clients.
    internal sealed partial class Progression
    {
        private static string SavedPath()
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null || Core.currentProfile == null)
            {
                return null;
            }

            return Path.Combine(
                Path.Combine(
                    sm.baseSaveDirectory, "prof" + Core.currentProfile.id),
                "darkwoodmp_progress.json");
        }

        /// <summary>
        /// Host: the shared progression of this world, loaded or started (when
        /// someone is playing with us); null: none.
        /// </summary>
        private Saved HostSaved(bool start, bool alone = false)
        {
            var s = S;
            var p = Player.Instance;
            bool solo = Solo;
            string path = SavedPath();
            var record = new RecordFacts
            {
                Solo = solo,
                IsHost = s != null && s.IsHost,
                InWorld = WorldShare.InWorld,
                Loading = WorldShare.Loading,
                HasPlayer = p != null,
                ClientMode = NetEnemies.ClientMode,
                HasPath = path != null,
            };

            if (!RecordRules.Keeps(record))
            {
                return null;
            }

            if (path != _savedPath)
            {
                _saved = null;
                _savedPath = path;
                try
                {
                    if (File.Exists(path))
                    {
                        _saved = JsonConvert.DeserializeObject<Saved>(
                            File.ReadAllText(path));
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[progress] couldn't read {path}: {e.Message}");
                }

                if (_saved != null)
                {
                    if (_saved.Resets == null)
                    {
                        _saved.Resets = new Dictionary<string, int>();
                    }

                    SharedExp = _saved.Exp;
                    SharedLevel = _saved.Level;
                    Plugin.Log.LogInfo(
                        $"[progress] this world's shared essence {SharedExp}, "
                            + $"level {SharedLevel}");
                }
            }

            int peers = 0;
            if (s != null)
            {
                peers = s.Peers.Count;
            }

            var starting = new RecordFacts
            {
                Solo = solo,
                HasRecord = _saved != null,
                Start = start,
                Alone = alone,
                Peers = peers,
            };

            if (RecordRules.Starts(starting))
            {
                _saved =
                    new Saved { Exp = p.experience, Level = p.currentLevel };
                SharedExp = _saved.Exp;
                SharedLevel = _saved.Level;
                Plugin.Log.LogInfo(
                    "[progress] shared progression starts from yours: essence "
                        + $"{SharedExp}, level {SharedLevel}");
            }

            return _saved;
        }

        private void SendState(ulong to)
        {
            var s = S;
            var w = s.Begin(MsgType.ProgressState);
            Progress.ProgressState.Write(
                w,
                new ProgressNumbers { Exp = SharedExp, Level = SharedLevel });
            if (to == 0)
            {
                s.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                s.SendTo(to, w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Host: someone's character was just sent; send them the shared
        /// progression after it.
        /// </summary>
        public void HostSendTo(ulong peer)
        {
            if (HostSaved(start: true) != null)
            {
                SendState(peer);
            }
        }

        /// <summary>
        /// Host: take in our own player's essence and level, tell everyone when
        /// they changed, and show ours.
        /// </summary>
        private void HostSync()
        {
            var saved = HostSaved(start: true);
            var p = Player.Instance;
            if (saved == null || p == null)
            {
                return;
            }

            var shared =
                new ProgressNumbers { Exp = SharedExp, Level = SharedLevel };
            var merged = ProgressRules.Merge(
                shared,
                new ProgressNumbers
                {
                    Exp = p.experience,
                    Level = p.currentLevel,
                });

            if (merged != shared)
            {
                SharedExp = saved.Exp = merged.Exp;
                SharedLevel = saved.Level = merged.Level;
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[progress] shared essence {SharedExp}, level "
                            + $"{SharedLevel}");
                }

                if (!Solo)
                {
                    SendState(0);
                }
            }

            Apply(p);
        }

        /// <summary>
        /// Host: the game saved; write the shared progression beside it.
        /// </summary>
        public void OnHostSaved()
        {
            var write = new WriteFacts
            {
                Snapshotting = WorldShare.Snapshotting,
                ClientMode = NetEnemies.ClientMode,
                HasRecord = _saved != null,
                HasPath = _savedPath != null,
                PathCurrent = _savedPath == SavedPath(),
            };

            if (!RecordRules.Writes(write))
            {
                return;
            }

            try
            {
                File.WriteAllText(
                    _savedPath, JsonConvert.SerializeObject(_saved));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[progress] couldn't write {_savedPath}: {e.Message}");
            }
        }

        private void OnEssence(ulong from, PacketReader r)
        {
            int amount = Progress.EssenceAdd.Read(r);
            var p = Player.Instance;
            if (!Handles(p))
            {
                return;
            }

            var verdict = ProgressRules.MayAddEssence(FactsOf(p), amount);
            if (verdict.Refused)
            {
                SendState(from);
                Coop.Current.Authority.Refuse(verdict);
                return;
            }

            SharedExp += amount;
            _saved.Exp = SharedExp;
            Plugin.Log.LogInfo(
                $"[progress] {S.NameOf(from)} cooked {amount} essence: shared "
                    + $"{SharedExp}");
            SendState(0);
            Apply(p);
        }

        private void OnLevelTaken(ulong from, PacketReader r)
        {
            int level = Progress.LevelTaken.Read(r);
            var p = Player.Instance;
            if (!Handles(p))
            {
                return;
            }

            var facts = FactsOf(p);
            int allowed = ProgressRules.Allowed(facts);
            if (level > allowed)
            {
                Plugin.Log.LogInfo(
                    $"[progress] {S.NameOf(from)} says level {level}; the "
                        + $"shared essence allows {allowed}");
            }

            level = ProgressRules.AcceptedLevel(facts, level);
            if (!ProgressRules.Raises(facts, level))
            {
                return;
            }

            SharedLevel = _saved.Level = level;
            Plugin.Log.LogInfo(
                $"[progress] {S.NameOf(from)} took level {level}: everyone can "
                    + "take it at a stove");
            SendState(0);
        }

        /// <summary>
        /// Host: whether it takes in a client's EssenceAdd or LevelTaken.
        /// The record (which can start here) is read only as the host.
        /// </summary>
        private bool Handles(Player p)
        {
            bool host = S.IsHost;
            bool record = host && HostSaved(start: true) != null;
            return ProgressRules.Handles(host, record, p != null);
        }

        private static int Today
        {
            get
            {
                if (Singleton<Controller>.Instance != null)
                {
                    return Singleton<Controller>.Instance.day;
                }

                return 0;
            }
        }

        /// <summary>
        /// Whether this player (0: ours) is in their morning: after dawn, not
        /// yet out of their hideout (Controller.isAfterNight).
        /// </summary>
        internal static bool InMorning(ulong peer)
        {
            if (peer == 0)
            {
                return Mornings.AfterNight.Local;
            }

            var sync = Plugin.Instance.Host.Players;
            return sync != null && sync.Remotes.TryGetValue(peer, out var rp)
                && rp.Last != null && rp.Last.AfterNight;
        }

        /// <summary>
        /// Host: whether this player (key hash, or "host"; peer 0: ours) may
        /// reset now; if so, it counts as today's.
        /// </summary>
        private Verdict TakeReset(string who, ulong peer)
        {
            var morning = SkillResetRules.Morning(InMorning(peer));
            if (morning.Refused)
            {
                return morning;
            }

            var saved =
                HostSaved(start: true, alone: SkillResetRules.Alone(who));
            int day = 0;
            bool hasDay =
                saved != null && saved.Resets.TryGetValue(who, out day);
            var reset = new ResetFacts
            {
                HasRecord = saved != null,
                HasDay = hasDay,
                Day = day,
                Today = Today,
            };

            var verdict = SkillResetRules.Today(reset);
            if (verdict.Refused)
            {
                return verdict;
            }

            saved.Resets[who] = Today;
            return verdict;
        }

        private void OnSkillReset(ulong from, PacketReader r)
        {
            bool host = S.IsHost;
            string key = null;
            bool hasKey = host && S.Keys.TryGetValue(from, out key);
            if (!SkillResetRules.Handles(host, hasKey))
            {
                return;
            }

            var verdict = TakeReset(SkillResetRules.Who(key), from);
            if (verdict.Refused)
            {
                Coop.Current.Authority.Refuse(verdict);
                return;
            }

            Plugin.Log.LogInfo(
                $"[progress] {S.NameOf(from)} reset their skills (day "
                    + $"{Today})");
        }
    }
}
