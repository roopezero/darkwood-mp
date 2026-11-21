using System;
using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Checks of enemy copies, and of enemies near a joiner.</summary>
    public static partial class MapScenarioFixChecks
    {
        // ---- 43-45: a copy's health bar, looks and pre-death stage ----------

        // host: the dog
        private static Character _fx;
        // host: its health before the client's hit
        private static float _fxHealth;
        // client: the copy fx-hit hit
        private static uint _fxId;

        /// <summary>The enemy with this net id in this game, or null.</summary>
        internal static Character ById(uint id)
        {
            var ne = NetEnemies.Instance;
            if (ne == null)
            {
                return null;
            }

            return Object.FindObjectsOfType<Character>().FirstOrDefault(
                c => c != null && ne.NetIdOf(c, out uint i) && i == id);
        }

        /// <summary>
        /// "fx-spawn [predeath]" (host): a Dog at the "fx" spot (with a
        /// pre-death stage of 40 health).
        /// </summary>
        private static IEnumerator FxSpawn(string[] a, Result r)
        {
            var c = SpawnAt(Spot("fx"));
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }

            _fx = c;
            yield return new WaitForSeconds(1.5f);
            if (a.Contains("predeath"))
            {
                c.hasPreDeath = true;
                c.preDeathMaxHealth = 40f;
            }

            _fxHealth = c.health;
            r.Info =
                $"{c.name} #{IdOf(c)} at {Fmt(c.transform.position)}, health "
                    + $"{c.health}/{c.maxHealth}, preDeath {c.hasPreDeath}";
        }

        /// <summary>"fx-hit DAMAGE" (client): hit the host's dog once. Its
        /// health bar shows here, then follows the host's health (44); its
        /// shadow lies as the game places it (45).</summary>
        private static IEnumerator FxHit(string[] a, Result r)
        {
            float dmg = 20f;
            if (a.Length > 0)
            {
                dmg = F(a[0]);
            }

            Character c = null;
            float until = Time.realtimeSinceStartup + 6f;
            while (c == null && Time.realtimeSinceStartup < until)
            {
                c = Shared(
                    Spot("fx"), x => x.alive && x.name.StartsWith("Dog"));
                if (c == null)
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }

            if (c == null)
            {
                r.Fail("no copy of the host's dog here");
                yield break;
            }

            _fxId = IdOf(c);
            yield return Walk(c.transform.position, 90f);
            float before = c.health;
            AsPlayer(
                () => c.getHit(
                    dmg, P.transform, false, true, true, true, false, false,
                    false));
            var bar = Singleton<UI>.Instance.enemyHealthBar;
            bool shownAtOnce = bar.currentObj == c.gameObject;
            yield return new WaitForSeconds(1.5f); // the host's next states
            float frac = -1f;
            if (c.maxHealth > 0f)
            {
                frac = c.health / c.maxHealth;
            }

            r.Info =
                FormattableString.Invariant(
                    $"{c.name} #{IdOf(c)}: health {before:0} -> {c.health:0} ")
                + FormattableString.Invariant($"of {c.maxHealth:0}; ")
                + FormattableString.Invariant(
                    $"bar on it {bar.currentObj == c.gameObject} ")
                + FormattableString.Invariant(
                    $"(at once {shownAtOnce}), bar {BarFraction(bar):0.00}");
            if (!shownAtOnce)
            {
                r.Fail($"our hit didn't show its health bar: {r.Info}");
            }
            else if (!(c.health < before - 0.5f))
            {
                r.Fail($"the host's health for it didn't come: {r.Info}");
            }
            else if (bar.currentObj == c.gameObject
                && Mathf.Abs(BarFraction(bar) - frac) > 0.03f)
            {
                r.Fail(
                    "the bar wasn't refreshed with the host's health: "
                        + $"{r.Info}");
            }

            // 45: its shadow, as Character.Update places it.
            if (c.shadow != null && r.Failure == null)
            {
                var sh = c.shadow;
                Vector3 rot = sh.eulerAngles;
                float dy = sh.position.y - c.transform.position.y;
                var lift =
                    new Vector3(c.shadowOffset.x, 0f, c.shadowOffset.z);
                float off = StateDump.Flat(
                    sh.position - c.transform.position - lift);
                r.Info +=
                    FormattableString.Invariant(
                        $"; shadow rot {rot.x:0},{rot.y:0}")
                    + FormattableString.Invariant(
                        $" (rotateShadow {c.rotateShadow}),")
                    + FormattableString.Invariant($" dy {dy:0}, off {off:0}");
                if (!c.rotateShadow
                    && (Mathf.Abs(Mathf.DeltaAngle(sh.eulerAngles.x, 90f))
                            > 1f
                        || Mathf.Abs(Mathf.DeltaAngle(sh.eulerAngles.y, 0f))
                            > 1f))
                {
                    r.Fail($"its shadow is turned with the body: {r.Info}");
                }
                else if (StateDump.Flat(
                    sh.position - c.transform.position - new Vector3(
                        c.shadowOffset.x, 0f, c.shadowOffset.z)) > 20f)
                {
                    r.Fail($"its shadow isn't under it: {r.Info}");
                }
            }

            yield return Walk(Centre, 60f);
        }

        /// <summary>
        /// "fx-hurt" (host): the client's hit reached the dog, and the host's
        /// bar doesn't show it (not ours, not selected).
        /// </summary>
        private static IEnumerator FxHurt(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.3f);
            if (_fx == null)
            {
                r.Fail("no dog (fx-spawn first)");
                yield break;
            }

            var bar = Singleton<UI>.Instance.enemyHealthBar;
            r.Info =
                $"{_fx.name}: health {_fxHealth:0} -> {_fx.health:0}, our bar "
                    + $"on it {bar.currentObj == _fx.gameObject}";
            if (!(_fx.health < _fxHealth))
            {
                r.Fail($"the client's hit did nothing here: {r.Info}");
            }
            else if (bar.currentObj == _fx.gameObject
                && P.selectedObject != _fx.transform)
            {
                r.Fail($"the client's hit showed our health bar: {r.Info}");
            }
        }

        /// <summary>
        /// "fx-predeath" (host): our player brings the dog down to its
        /// pre-death stage (43): not alive, still in play.
        /// </summary>
        private static IEnumerator FxPreDeath(string[] a, Result r)
        {
            if (_fx == null)
            {
                r.Fail("no dog");
                yield break;
            }

            var c = _fx;
            AsPlayer(
                () => c.getHit(
                    c.health + 5f, P.transform, false, true, false, true, false,
                    false, false));
            yield return new WaitForSeconds(1f);
            r.Info =
                $"{c.name}: alive {c.alive}, health {c.health:0}, in play "
                    + $"{NetEnemies.InPlay(c)}";
            if (c.alive)
            {
                r.Fail($"it's still alive: {r.Info}");
            }
            else if (!NetEnemies.InPlay(c))
            {
                r.Fail($"it didn't go into its pre-death stage: {r.Info}");
            }
        }

        /// <summary>"fx-crawling" (client): our copy of the crawling dog is
        /// still in the fight (not a corpse), and our finishing blow is taken
        /// by the host.</summary>
        private static IEnumerator FxCrawling(string[] a, Result r)
        {
            int mark = MapScenarioIssues.LogMark();
            yield return new WaitForSeconds(1f);
            var c = ById(_fxId);
            if (c == null)
            {
                r.Fail($"no copy of the dog #{_fxId} here (fx-hit first)");
                yield break;
            }

            uint id = IdOf(c);
            bool settled = MapScenarioIssues.LogLines().Skip(mark)
                .Any(l => l.Contains($"#{id} died"));
            string crawl =
                $"{c.name} #{id}: alive {c.alive}, health {c.health:0} of "
                    + $"{c.maxHealth:0}, settled {settled}";
            if (!c.alive || settled)
            {
                r.Fail(
                    $"it's a corpse here while it crawls on the host: {crawl}");
                yield break;
            }

            int hits = 0;
            while (c != null && c.alive && hits < 12)
            {
                yield return Walk(c.transform.position, 90f);
                hits++;
                AsPlayer(
                    () => c.getHit(
                        25f, P.transform, false, true, true, true, false, false,
                        false));
                yield return new WaitForSeconds(0.6f);
            }

            r.Info =
                $"{crawl}; then {hits} hit(s): alive {(c != null && c.alive)}";
            if (c != null && c.alive)
            {
                r.Fail($"our hits didn't finish it: {r.Info}");
            }

            yield return Walk(Centre, 60f);
        }

        /// <summary>"fx-dead" (host): the dog is really dead now.</summary>
        private static IEnumerator FxDead(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            if (_fx == null)
            {
                r.Fail("no dog");
                yield break;
            }

            r.Info =
                $"{_fx.name}: alive {_fx.alive}, health {_fx.health:0}, in "
                    + $"play {NetEnemies.InPlay(_fx)}";
            if (NetEnemies.InPlay(_fx))
            {
                r.Fail($"it's still in play: {r.Info}");
            }
        }

        /// <summary>
        /// "fx-corpse" (client): our copy settled into a corpse, its shadow
        /// faded and its particles stopped (45).
        /// </summary>
        private static IEnumerator FxCorpse(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 8f;
            Character c = null;
            while (Time.realtimeSinceStartup < until)
            {
                c = ById(_fxId);
                if (c != null && !c.alive && c.GetComponent<Item>() != null)
                {
                    break;
                }

                yield return new WaitForSeconds(0.5f);
            }

            if (c == null || c.alive)
            {
                r.Fail($"the dog #{_fxId} isn't dead here");
                yield break;
            }

            // the shadow's fade takes a second
            yield return new WaitForSeconds(1.5f);
            tk2dBaseSprite sp = null;
            if (c.shadow != null)
            {
                sp = c.shadow.GetComponent<tk2dBaseSprite>();
            }

            int emitting = c.GetComponentsInChildren<ParticleSystem>().Count(
                ps => ps != null && ps.isEmitting);
            string name = c.name;
            uint id = IdOf(c);
            bool searchable = c.GetComponent<Item>() != null;
            float alpha = -1f;
            if (sp != null)
            {
                alpha = sp.color.a;
            }

            r.Info =
                FormattableString.Invariant(
                    $"{name} #{id}: searchable {searchable}, shadow alpha ")
                + FormattableString.Invariant(
                    $"{alpha:0.00}, emitting particles {emitting}");
            if (c.GetComponent<Item>() == null)
            {
                r.Fail($"its corpse isn't searchable: {r.Info}");
            }
            else if (sp != null && sp.color.a > 0.1f)
            {
                r.Fail($"its shadow didn't fade: {r.Info}");
            }
            else if (emitting > 0)
            {
                r.Fail($"its particles still play: {r.Info}");
            }
        }

        // ---- 50: near a joiner ----------------------------------------------

        /// <summary>"near-joiner" (host, the client standing at "client"): a
        /// dog 1200 from the client and 2600 from us is near a player
        /// (Character.inSightOrCloseToPlayer, NearJoinerPatch); one 1700 from
        /// the client and 2200 from us isn't.</summary>
        private static IEnumerator NearJoiner(string[] a, Result r)
        {
            var other = Other();
            if (other == null)
            {
                r.Fail("no proxy for the client");
                yield break;
            }

            var near = SpawnAt(Spot("near"));
            var far = SpawnAt(Spot("far"));
            if (near == null || far == null)
            {
                r.Fail("couldn't spawn the dogs");
                yield break;
            }

            yield return new WaitForSeconds(1f);
            float dn = Core.trueDistance(
                other.position, near.transform.position),
                df = Core.trueDistance(other.position, far.transform.position);
            float hn = Core.trueDistance(
                P.transform.position, near.transform.position),
                hf = Core.trueDistance(
                    P.transform.position, far.transform.position);
            bool nearRes = near.inSightOrCloseToPlayer(),
                farRes = far.inSightOrCloseToPlayer();
            r.Info =
                $"near dog: {dn:0} from the client, {hn:0} from us: {nearRes}; "
                    + $"far dog: {df:0} / {hf:0}: {farRes}";
            Object.Destroy(near.gameObject);
            Object.Destroy(far.gameObject);
            if (dn > 1450f || hn < 1600f)
            {
                r.Fail($"the client isn't where the step needs it: {r.Info}");
            }
            else if (!nearRes)
            {
                r.Fail($"a dog by the client isn't near a player: {r.Info}");
            }
            else if (farRes)
            {
                r.Fail($"a dog far from both is near a player: {r.Info}");
            }
        }
    }
}
