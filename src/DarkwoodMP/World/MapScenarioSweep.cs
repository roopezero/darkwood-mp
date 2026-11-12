using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for leftover sync bugs (the order is tools/tests/map_scenario_sweep.py):
    /// - a story flag set while flag sharing is paused (inputs forbidden, as while the player is dying) reaches the
    ///   other game once it resumes (FlagsSync keeps what it last sent through such a pause);
    /// - an enemy's loop stopped by other means than playIdleLoop("") (destroySounds) stops in the client's copy too
    ///   (NetEnemies.LoopCheck);
    /// - an open door whose leaf our player walks into rests at the same angle in every game (DoorPush).
    /// </summary>
    public static class MapScenarioSweep
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            Add("sweep-flag-paused", FlagPaused, timeout: 30f);
            Add("sweep-loop-stop", LoopStop);
            Add("sweep-loop-is", LoopIs, timeout: 15f);
            Add("sweep-push-door", PushDoor, timeout: 30f);
            Add("sweep-door-is", DoorIs, timeout: 15f);
            Add("sweep-door-leaf", DoorLeafIs, timeout: 15f);
        }

        /// <summary>
        /// "sweep-flag-paused N": with inputs forbidden (which pauses flag sharing, as the death screen does), set the
        /// Nth story flag (by name, leaving out the player's own "player_" ones) the other way; then allow inputs
        /// again. The other game must get it.
        /// </summary>
        private static IEnumerator FlagPaused(string[] a, Result r)
        {
            var f = Singleton<Flags>.Instance;
            var dict = HarmonyLib.AccessTools.FieldRefAccess<Flags, System.Collections.Generic.Dictionary<string, Flags.Flag>>("flagsDict")(f);
            // One the world's database has that isn't set yet (a fresh save has only a few set), and isn't about the
            // player, a chapter or a death.
            string[] skip = { "player", "chapter", "died", "dead", "kill", "dream" };
            string name = f.database.worldFlags.Where(x => x != null && !string.IsNullOrEmpty(x.name) && !dict.ContainsKey(x.name)
                    && !skip.Any(k => x.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(x => x.name).OrderBy(x => x, System.StringComparer.Ordinal).Skip(a.Length > 0 ? int.Parse(a[0]) : 0).FirstOrDefault();
            if (name == null) { r.Fail($"no such flag ({dict.Count} set)"); yield break; }
            bool was = Core.forbidInputs;
            Core.forbidInputs = true;
            try
            {
                yield return new WaitForSecondsRealtime(1f);
                if (WorldShare.WorldReady) { r.Fail("flag sharing didn't pause (WorldReady with inputs forbidden)"); yield break; }
                f.setFlag(name, true);
                // Longer than FlagsSync.Interval: under the old code its baseline was taken without it here.
                yield return new WaitForSecondsRealtime(FlagsSync.Interval + 1.5f);
            }
            finally { Core.forbidInputs = was; }
            // WorldReady comes back 2 s after, then the next flush sends it.
            yield return new WaitForSecondsRealtime(2f + FlagsSync.Interval + 1.5f);
            r.Info = $"{name} set {(dict.TryGetValue(name, out var fl) && fl.isTrue)} while sharing was paused";
        }

        private static Character NearestHostEnemy()
        {
            var ne = Combat.NetEnemies.Instance;
            return Nearest(Object.FindObjectsOfType<Character>().Where(x => x.alive && Combat.NetEnemies.Eligible(x) && ne != null && ne.NetIdOf(x, out _)
                && StateDump.Flat(x.transform.position - Centre) < 2500f), P.transform.position);
        }

        private static string LoopOf(Character c)
        {
            var loop = c.sounds != null ? c.sounds.idleAudioObject : null;
            return loop != null && loop.IsPlaying() ? loop.audioID : "";
        }

        /// <summary>"sweep-loop-stop": the host's enemy nearest to us stops its loop by destroySounds (no playIdleLoop).</summary>
        private static IEnumerator LoopStop(string[] a, Result r)
        {
            var c = NearestHostEnemy();
            if (c == null || c.sounds == null) { r.Fail("no enemy with sounds here"); yield break; }
            // A loop playing first (a dog's idle one can be none): the first of its loops it has.
            var snd = c.sounds;
            string play = new[] { snd.idleLoop, snd.defensive, snd.escapingLoop, snd.sleeping }.FirstOrDefault(x => !string.IsNullOrEmpty(x));
            if (play == null) { r.Fail($"{c.name} has no loop"); yield break; }
            // It plays one as it comes at us (run 2: dog_aggressive_loop); its AI can replace one we start at once,
            // and can stop it again (Character.setBehaviour plays the next behaviour's loop, or none): wait until one
            // has played for a second without a break, so the clients have it, starting ours again when none plays.
            float until = Time.unscaledTime + 10f, next = 0f, since = -1f;
            string was = "", seen = "";
            while (Time.unscaledTime < until)
            {
                string now = LoopOf(c);
                if (now == "") { since = -1f; if (Time.unscaledTime >= next) { next = Time.unscaledTime + 2f; snd.playIdleLoop(play, forceReplace: true); } }
                else if (since < 0f || now != seen) since = Time.unscaledTime;
                else if (Time.unscaledTime - since >= 1f) { was = now; break; }
                seen = now;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (was == "") { r.Fail($"{c.name}'s loop {play} didn't start"); yield break; }
            snd.destroySounds();
            yield return new WaitForSecondsRealtime(1.5f);
            r.Info = $"{c.name} {ObjRef.Of(c.gameObject)}: loop '{was}' stopped, now '{LoopOf(c)}'";
        }

        /// <summary>"sweep-loop-is [AUDIO]": the nearest host's enemy (our copy of it on a client) plays this loop ("" none), within the timeout.</summary>
        private static IEnumerator LoopIs(string[] a, Result r)
        {
            string want = a.Length > 0 ? a[0] : "";
            float until = Time.unscaledTime + 10f;
            Character c = null;
            while (Time.unscaledTime < until)
            {
                c = NearestHostEnemy();
                if (c != null && LoopOf(c) == want) { r.Info = $"{c.name} {ObjRef.Of(c.gameObject)} loop '{want}'"; yield break; }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            r.Fail(c == null ? "no enemy of the host's here" : $"{c.name} plays '{LoopOf(c)}', not '{want}'");
        }

        private static Door DoorAtCentre() =>
            Nearest(Object.FindObjectsOfType<Door>().Where(d => !d.destroyed && !d.barricaded && d.body != null && !(d.GetComponent<Locked>() is Locked l && l.locked)
                && !(d.GetComponent<Padlock>() is Padlock pl && pl.locked) && ObjRef.Of(d.gameObject).Valid), Centre);

        /// <summary>
        /// "sweep-push-door [ANGLE]": open the door nearest the centre (as our player), wait for its leaf to swing
        /// open, stand behind its hinge (out of its swing) and turn the leaf by physics (its angular velocity, as
        /// walking into it does) until it's ANGLE degrees (default 40) from shut, and leave it at rest. DoorPush must
        /// send where it rests. Where a walk would leave the leaf depends on the path, so the turn is given here, and
        /// the push counts as our player's (DoorPush.TestTouch) whether or not the walk got within its touch distance.
        /// </summary>
        private static IEnumerator PushDoor(string[] a, Result r)
        {
            float target = a.Length > 0 ? float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture) : 40f;
            var d = DoorAtCentre();
            if (d == null) { r.Fail("no door"); yield break; }
            yield return Walk(d.transform.position);
            if (!d.opened) AsPlayer(() => d.open(P.transform.position, P.transform, 0f));
            float until = Time.unscaledTime + 4f;
            while (Time.unscaledTime < until && !DoorLeaf.Turned(d)) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            if (!d.opened || !DoorLeaf.Turned(d)) { r.Fail($"{d.name} didn't swing open (open={d.opened}, leaf at {DoorLeaf.Angle(d):0})"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            float opened = DoorLeaf.Angle(d);
            if (Mathf.Abs(opened - target) < 8f) { r.Fail($"{d.name} swung open to {opened:0}, too near {target:0} to tell a push"); yield break; }
            var rb = d.body.GetComponent<Rigidbody>();
            var hinge = d.body.GetComponent<HingeJoint>();
            if (rb == null || hinge == null) { r.Fail($"{d.name}'s leaf has no rigidbody or hinge"); yield break; }
            // Behind the hinge, away from where the leaf points: the leaf is fastened there and doesn't swing there
            // (run 4 of the old step: standing 58 from the leaf's side, the turn was stopped by our player).
            var u = d.body.position - hinge.connectedAnchor; u.y = 0f;
            if (u.sqrMagnitude < 1f) u = Vector3.right;
            yield return Walk(hinge.connectedAnchor - u.normalized * 60f, 40f);
            DoorPush.TestTouch = d;
            try
            {
                // Turn it about the hinge (world up) towards the target, as a walk into it would.
                float last = DoorLeaf.Angle(d), sign = 1f;
                until = Time.unscaledTime + 8f;
                while (d.opened && Time.unscaledTime < until)
                {
                    yield return new WaitForFixedUpdate();
                    if (rb == null || rb.isKinematic) break;
                    float now = DoorLeaf.Angle(d);
                    if (Mathf.Abs(now - target) < 1.5f) break;
                    if (Mathf.Abs(now - last) > 0.3f && (now > last) != (target > now)) sign = -sign; // going the wrong way
                    last = now;
                    rb.angularVelocity = new Vector3(0f, sign * Mathf.Clamp(Mathf.Abs(now - target) * 0.05f, 0.4f, 2f), 0f);
                }
                if (rb != null) { rb.angularVelocity = Vector3.zero; rb.velocity = Vector3.zero; }
                yield return new WaitForSecondsRealtime(2f); // DoorPush's rest time, and the send
            }
            finally { DoorPush.TestTouch = null; }
            r.Info = $"{d.name} {ObjRef.Of(d.gameObject)}: leaf {opened:0} -> {DoorLeaf.Angle(d):0} (signed {DoorLeaf.Signed(d):0}, wanted {target:0}), open={d.opened}, we are {StateDump.Flat(P.transform.position - hinge.connectedAnchor):0} from the hinge";
            if (!d.opened || Mathf.Abs(DoorLeaf.Angle(d) - target) > 4f) r.Fail($"{r.Info}: not turned to {target:0}");
        }

        /// <summary>"sweep-door-leaf ANGLE": the leaf of the door nearest the centre is ANGLE degrees (within 5) from shut here, within the timeout.</summary>
        private static IEnumerator DoorLeafIs(string[] a, Result r)
        {
            float want = a.Length > 0 ? float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture) : 40f;
            var d = DoorAtCentre();
            if (d == null) { r.Fail("no door"); yield break; }
            float until = Time.unscaledTime + 10f;
            while (Time.unscaledTime < until && !(d.opened && Mathf.Abs(DoorLeaf.Angle(d) - want) <= 5f)) yield return null;
            r.Info = $"{d.name} {ObjRef.Of(d.gameObject)} open={d.opened}, leaf at {DoorLeaf.Angle(d):0} (signed {DoorLeaf.Signed(d):0})";
            if (!d.opened || Mathf.Abs(DoorLeaf.Angle(d) - want) > 5f) r.Fail($"{r.Info}: not at {want:0}");
        }

        /// <summary>"sweep-door-is open|shut": the door nearest the centre is open (or shut) here, within the timeout.</summary>
        private static IEnumerator DoorIs(string[] a, Result r)
        {
            var d = DoorAtCentre();
            if (d == null) { r.Fail("no door"); yield break; }
            bool want = a.Length > 0 && a[0] == "open";
            float until = Time.unscaledTime + 10f;
            while (Time.unscaledTime < until && d.opened != want) yield return null;
            r.Info = $"{d.name} {ObjRef.Of(d.gameObject)} open={d.opened}, leaf at {DoorLeaf.Angle(d):0}";
            if (d.opened != want) r.Fail($"{r.Info}: not {(want ? "open" : "shut")}");
        }
    }
}
