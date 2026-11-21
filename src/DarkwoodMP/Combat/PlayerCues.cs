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
    /// get scared.
    /// </summary>
    public static class PlayerCues
    {
        public const byte Dodge = CueRules.Dodge, Scare = CueRules.Scare;
        private static readonly Collider[] _near =
            new Collider[CueRules.MaxNear];

        public static void Send(byte kind)
        {
            var s = Net.Session.Instance;
            bool client = false;
            if (s != null)
            {
                client = s.IsClient;
            }

            if (!CueRules.Sends(s != null, client, NetEnemies.ClientMode))
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
        public static void OnCue(ulong from, Net.PacketReader r)
        {
            byte kind = PlayerCue.Read(r);
            NetPlayerProxy.All.TryGetValue(from, out var proxy);
            var cue = FactsOf(Net.Session.Instance, kind, proxy);
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
                if (Threat.Debug)
                {
                    n = CountCharacters(pos, CueRules.ScareRadius);
                }

                Character.scareInArea(pos, CueRules.ScareRadius);
            }

            if (Threat.Debug)
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
                NetEnemies.ClientMode, NetEnemies.Frozen.Contains(c));
        }

        private static CueFacts FactsOf(
            Net.Session s, byte kind, NetPlayerProxy proxy)
        {
            bool alive = false;
            if (proxy != null)
            {
                alive = proxy.alive;
            }

            bool hosting = false;
            if (s != null)
            {
                hosting = s.IsHost;
            }

            return new CueFacts
            {
                Hosting = hosting,
                Kind = kind,
                StandInAlive = alive,
            };
        }

        private static int CountCharacters(Vector3 pos, float range)
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

    /// <summary>
    /// Client: its player's swing goes to the host too, for the host's enemies
    /// to dodge.
    /// </summary>
    [HarmonyPatch(typeof(Player), "notifyEnemiesAboutAttack")]
    static class AttackCuePatch
    {
        static void Postfix() => PlayerCues.Send(PlayerCues.Dodge);
    }

    /// <summary>
    /// Client: its player's aim goes to the host too. Only Player.aimScare
    /// calls scareInArea.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.scareInArea))]
    static class AimCuePatch
    {
        static void Prefix() => PlayerCues.Send(PlayerCues.Scare);
    }

    /// <summary>
    /// Client: frozen copies of the host's enemies don't dodge or get scared
    /// (the host's do, and are sent).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.doIWantToDodge))]
    static class FrozenNoDodgePatch
    {
        static bool Prefix(Character __instance) =>
            PlayerCues.Reacts(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.getScared))]
    static class FrozenNotScaredPatch
    {
        static bool Prefix(Character __instance) =>
            PlayerCues.Reacts(__instance);
    }
}
