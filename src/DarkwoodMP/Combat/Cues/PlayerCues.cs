using DarkwoodMP.Combat.Cues;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Two things a player does that the game tells nearby enemies about,
    /// besides noise: a swing about to land (Player.notifyEnemiesAboutAttack:
    /// enemies within 150 that see it may dodge, Character.doIWantToDodge) and
    /// aiming a weapon (Player.aimScare, every second: Character.scareInArea
    /// makes enemies within 350 scared). Both run only in the acting player's
    /// game, and the enemies that matter are the host's, so a client sends each
    /// as a PlayerCue and the host runs the game's own call at that player's
    /// proxy. A client's frozen copies of the host's enemies neither dodge nor
    /// get scared. One per session (CoopSession.Cues).
    /// </summary>
    internal sealed class PlayerCues
    {
        private readonly CoopSession _coop;
        private readonly Collider[] _near = new Collider[CueRules.MaxNear];

        public PlayerCues(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(Net.MsgType.PlayerCue, OnCue);
        }

        /// <summary>
        /// Client: our player swung or aimed (CueRules.Dodge or Scare).
        /// </summary>
        public void Send(byte kind)
        {
            var s = _coop.Net;
            if (!CueRules.Sends(true, s.IsClient, NetEnemies.ClientMode))
            {
                return;
            }

            var w = s.Begin(Net.MsgType.PlayerCue);
            PlayerCue.Write(w, kind);
            s.SendToHost(w, Net.Channel.Control, reliable: true);
        }

        /// <summary>
        /// Host: a client's player swung or aimed; the game's own reaction, at
        /// their proxy.
        /// </summary>
        private void OnCue(ulong from, Net.PacketReader r)
        {
            byte kind = PlayerCue.Read(r);
            NetPlayerProxy.All.TryGetValue(from, out var proxy);
            var cue = FactsOf(_coop.Net, kind, proxy);
            CueReaction reaction = CueRules.Reaction(cue);
            if (reaction == CueReaction.None)
            {
                return;
            }

            Vector3 pos = proxy.transform.position;
            int n = 0;
            if (reaction == CueReaction.Dodge)
            {
                int hits = Physics.OverlapSphereNonAlloc(
                    pos, CueRules.DodgeRadius, _near, CueRules.EnemyLayers);
                for (int i = 0; i < hits; i++)
                {
                    Character c = null;
                    if (_near[i] != null)
                    {
                        c = _near[i].GetComponent<Character>();
                    }

                    if (c == null)
                    {
                        continue;
                    }

                    c.doIWantToDodge(pos);
                    n++;
                }
            }
            else
            {
                if (Plugin.Instance.Host.Threats.Debug)
                {
                    n = CountCharacters(pos, CueRules.ScareRadius);
                }

                Character.scareInArea(pos, CueRules.ScareRadius);
            }

            if (Plugin.Instance.Host.Threats.Debug)
            {
                string text = "aims";
                if (reaction == CueReaction.Dodge)
                {
                    text = "swings";
                }

                string effect = "within 350 told to be scared";
                if (reaction == CueReaction.Dodge)
                {
                    effect = "within 150 may dodge";
                }

                Plugin.Log.LogInfo(
                    $"[threat] {proxy.OwnerName} {text}: {n} characters " +
                                   effect);
            }
        }

        /// <summary>
        /// Client: whether a character dodges or gets scared (not a frozen
        /// copy of the host's enemy).
        /// </summary>
        public static bool Reacts(Character c)
        {
            return CueRules.Reacts(
                NetEnemies.ClientMode,
                Coop.Current != null
                    && Coop.Current.Enemies.Frozen.Contains(c));
        }

        private static CueFacts FactsOf(
            Net.Session s, byte kind, NetPlayerProxy proxy)
        {
            bool alive = false;
            if (proxy != null)
            {
                alive = proxy.alive;
            }

            return new CueFacts
            {
                Hosting = s.IsHost,
                Kind = kind,
                StandInAlive = alive,
            };
        }

        private int CountCharacters(Vector3 pos, float range)
        {
            int hits = Physics.OverlapSphereNonAlloc(
                pos, range, _near, CueRules.EnemyLayers),
                n = 0;
            for (int i = 0; i < hits; i++)
            {
                if (_near[i] != null
                    && _near[i].GetComponent<Character>() != null)
                {
                    n++;
                }
            }

            return n;
        }
    }
}
