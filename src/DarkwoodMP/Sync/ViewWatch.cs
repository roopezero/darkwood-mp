using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// What hides our player's whole view. Light2D.LateUpdate, each time a light has moved, casts a ray straight down
    /// through it (from 100 above, 200 long, on layers Default, Static and FOVBlocker: the constant 16809985) and, if
    /// that ray hits anything, trigger colliders included (Physics.queriesHitTriggers is on), switches the light's
    /// renderer off until it moves somewhere the ray hits nothing. For the player's view light (Player.FOVLight) that
    /// is a completely black screen, which comes and goes as the player steps over the edge of whatever is there.
    /// The game relies on nothing on those layers lying where a player can stand; something that does (only in one
    /// game) blacks out that player's screen there. This names it: every 0.25 s, in a co-op session, the same ray at
    /// our player, logged once per set of hits (again after 30 s).
    /// </summary>
    public static class ViewWatch
    {
        /// <summary>The layers of Light2D.LateUpdate's ray (Default 0, Static 15, FOVBlocker 24).</summary>
        public const int HideMask = 16809985;

        private static readonly HarmonyLib.AccessTools.FieldRef<Player, Light2D> FovLight = HarmonyLib.AccessTools.FieldRefAccess<Player, Light2D>("FOVLight");

        /// <summary>Where our player's view light is (the ray starts above it), or null.</summary>
        public static Vector3? LightPos()
        {
            var p = Player.Instance;
            Light2D l = null;
            if (p != null)
            {
                l = FovLight(p);
            }
            if (l != null)
            {
                return l.transform.position;
            }
            return (Vector3?)null;
        }

        private static float _next, _loggedAt;
        private static string _logged = "";

        /// <summary>What the game's ray at this position hits: each collider's path, layer, type, trigger and size.</summary>
        public static List<string> Hiders(Vector3 pos)
        {
            var list = new List<string>();
            var hits = Physics.RaycastAll(pos + new Vector3(0f, 100f, 0f), Vector3.down, 200f, HideMask);
            foreach (var h in hits.OrderBy(x => x.distance))
            {
                var c = h.collider;
                if (c == null)
                {
                    continue;
                }
                var b = c.bounds;
                var path = Path(c.transform);
                var layer = c.gameObject.layer;
                var kind = c.GetType().Name;
                string trigger = "";
                if (c.isTrigger)
                {
                    trigger = ", trigger";
                }
                list.Add($"{path} [layer {layer}, {kind}{trigger}, " +
                         $"{b.size.x:0}x{b.size.z:0} at ({b.center.x:0}, {b.center.z:0})]");
            }
            return list;
        }

        /// <summary>The game's own test: does the ray at this position hit anything (our view would be off here)?</summary>
        public static bool Hidden(Vector3 pos) => Physics.Raycast(pos + new Vector3(0f, 100f, 0f), Vector3.down, 200f, HideMask);

        public static string Path(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent)
            {
                p = x.name + "/" + p;
            }
            return p;
        }

        public static void LateUpdate(bool inSession)
        {
            if (!inSession || Time.unscaledTime < _next)
            {
                return;
            }
            _next = Time.unscaledTime + 0.25f;
            var p = Player.Instance;
            if (p == null || !p.gameObject.activeInHierarchy || Core.mainMenu)
            {
                return;
            }
            var light = FovLight(p);
            if (light == null)
            {
                return;
            }
            var pos = light.transform.position;
            if (!Hidden(pos))
            {
                _logged = "";
                return;
            }
            string set = string.Join("; ", Hiders(pos));
            if (set == _logged && Time.unscaledTime - _loggedAt < 30f)
            {
                return;
            }
            _logged = set;
            _loggedAt = Time.unscaledTime;
            var r = light.GetComponent<Renderer>();
            Plugin.Log.LogInfo($"[view] our player's view is switched off at ({pos.x:0}, {pos.z:0}) (a black screen): " +
                               $"the game's ray down through the player's light hits {set}; light drawn {(r != null && r.enabled)}");
        }
    }
}
