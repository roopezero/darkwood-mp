using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Two things a player does that the game tells nearby enemies about, besides noise: a swing about to land
    /// (Player.notifyEnemiesAboutAttack: enemies within 150 that see it may dodge, Character.doIWantToDodge) and
    /// aiming a weapon (Player.aimScare, every second: Character.scareInArea makes enemies within 350 scared). Both run
    /// only in the acting player's game, and the enemies that matter are the host's, so a client sends each as a
    /// PlayerCue and the host runs the game's own call at that player's proxy. A client's frozen copies of the host's
    /// enemies neither dodge nor get scared.
    /// </summary>
    public static class PlayerCues
    {
        public const byte Dodge = 0, Scare = 1;
        private const int EnemyLayers = 2099200; // the game's layers in both calls
        private static readonly Collider[] _near = new Collider[128];

        public static void Send(byte kind)
        {
            var s = Net.Session.Instance;
            if (s == null || !s.IsClient || !NetEnemies.ClientMode) return;
            var w = s.Begin(Net.MsgType.PlayerCue);
            w.W.Write(kind);
            s.SendToHost(w, Net.Channel.Control, reliable: true);
        }

        /// <summary>Host: a client's player swung or aimed; the game's own reaction, at their proxy.</summary>
        public static void OnCue(ulong from, Net.PacketReader r)
        {
            var s = Net.Session.Instance;
            if (s == null || !s.IsHost) return;
            byte kind = r.R.ReadByte();
            if (!NetPlayerProxy.All.TryGetValue(from, out var proxy) || proxy == null || !proxy.alive) return;
            Vector3 pos = proxy.transform.position;
            int n = 0;
            if (kind == Dodge)
            {
                int hits = Physics.OverlapSphereNonAlloc(pos, 150f, _near, EnemyLayers);
                for (int i = 0; i < hits; i++)
                {
                    var c = _near[i] != null ? _near[i].GetComponent<Character>() : null;
                    if (c == null) continue;
                    c.doIWantToDodge(pos);
                    n++;
                }
            }
            else if (kind == Scare)
            {
                if (Threat.Debug) n = CountCharacters(pos, 350f);
                Character.scareInArea(pos, 350f);
            }
            else return;
            if (Threat.Debug)
                Plugin.Log.LogInfo($"[threat] {proxy.OwnerName} {(kind == Dodge ? "swings" : "aims")}: {n} characters " +
                                   (kind == Dodge ? "within 150 may dodge" : "within 350 told to be scared"));
        }

        private static int CountCharacters(Vector3 pos, float range)
        {
            int hits = Physics.OverlapSphereNonAlloc(pos, range, _near, EnemyLayers), n = 0;
            for (int i = 0; i < hits; i++) if (_near[i] != null && _near[i].GetComponent<Character>() != null) n++;
            return n;
        }
    }

    /// <summary>Client: its player's swing goes to the host too, for the host's enemies to dodge.</summary>
    [HarmonyPatch(typeof(Player), "notifyEnemiesAboutAttack")]
    static class AttackCuePatch
    {
        static void Postfix() => PlayerCues.Send(PlayerCues.Dodge);
    }

    /// <summary>Client: its player's aim goes to the host too. Only Player.aimScare calls scareInArea.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.scareInArea))]
    static class AimCuePatch
    {
        static void Prefix() => PlayerCues.Send(PlayerCues.Scare);
    }

    /// <summary>Client: frozen copies of the host's enemies don't dodge or get scared (the host's do, and are sent).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.doIWantToDodge))]
    static class FrozenNoDodgePatch
    {
        static bool Prefix(Character __instance) => !(NetEnemies.ClientMode && NetEnemies.Frozen.Contains(__instance));
    }

    [HarmonyPatch(typeof(Character), nameof(Character.getScared))]
    static class FrozenNotScaredPatch
    {
        static bool Prefix(Character __instance) => !(NetEnemies.ClientMode && NetEnemies.Frozen.Contains(__instance));
    }
}
