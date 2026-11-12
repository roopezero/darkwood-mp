using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DarkwoodMP.Combat;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for fixes that had no test in game (docs/INTERACTIONS.md gaps 24, 28, 40,
    /// 43-45, 50, 51, 58). The order they run in is tools/tests/map_scenario_fixchecks.py. Each does a thing in
    /// one game and checks it in that game; a step in the other game checks what reached it.
    ///
    /// The enemies here are spawned by the host at fixed offsets from the centre, and each game finds them again
    /// as the shared enemy (a NetEnemies id) nearest to that spot.
    /// </summary>
    public static class MapScenarioFixChecks
    {
        private static readonly Dictionary<string, Vector3> Spots = new Dictionary<string, Vector3>
        {
            ["fx"] = new Vector3(-600f, 0f, 1100f),       // the dog for the health bar, the looks and the pre-death stage
            ["near"] = new Vector3(2600f, 0f, 0f),        // 2600 from the host, 1200 from the client at "client"
            ["far"] = new Vector3(1400f, 0f, -1700f),     // 2200 from the host, 1700 from the client at "client"
            ["client"] = new Vector3(1400f, 0f, 0f),
            ["spirit"] = new Vector3(0f, 0f, -600f),
            ["drop"] = new Vector3(400f, 0f, 700f),       // clear of the stations, for a dropped match or flare
        };
        private static Vector3 Spot(string which) => Spots.TryGetValue(which, out var off) ? Centre + off : Centre;

        public static void Register()
        {
            // Walking somewhere by name.
            MapScenario.Add("fx-goto", (a, r) => GoTo(a, r), timeout: 30f);
            // 43-45: enemy copies (health bar, looks, a pre-death stage).
            MapScenario.Add("fx-spawn", FxSpawn);
            MapScenario.Add("fx-hit", FxHit, timeout: 25f);
            MapScenario.Add("fx-hurt", FxHurt);
            MapScenario.Add("fx-predeath", FxPreDeath);
            MapScenario.Add("fx-crawling", FxCrawling, timeout: 30f);
            MapScenario.Add("fx-dead", FxDead);
            MapScenario.Add("fx-corpse", FxCorpse, timeout: 20f);
            // 28: an InteractiveItem's switch.
            MapScenario.Add("ii-setup", IiSetup);
            MapScenario.Add("ii-switch", IiSwitch, timeout: 25f);
            MapScenario.Add("ii-check", IiCheck);
            // 40: a held throwable dropped.
            MapScenario.Add("drop-held", DropHeld, timeout: 30f);
            MapScenario.Add("expect-dropped", ExpectDropped);
            // 50: enemies near a joiner count as near a player.
            MapScenario.Add("near-joiner", NearJoiner, timeout: 20f);
            // 51: the forest spirit and the inside of its target.
            MapScenario.Add("fx-inside", FxInside, timeout: 30f);
            MapScenario.Add("spirit", Spirit, timeout: 25f);
            // 58: slow motion in co-op.
            MapScenario.Add("slowmo", Slowmo, timeout: 20f);
            // 24: the night trader killed.
            MapScenario.Add("kill-trader", KillTrader, timeout: 40f);
            MapScenario.Add("trader-dream", TraderDream, timeout: 20f);

            // How the dead look (45): a corpse's shadow faded and its particles stopped, in every game.
            StateDump.AddSection("enemylooks", ctx =>
            {
                var ne = NetEnemies.Instance;
                foreach (var c in StateDump.All<Character>())
                {
                    if (!NetEnemies.Eligible(c) || !ctx.Near(c.transform.position)) continue;
                    if (ne == null || !ne.NetIdOf(c, out _)) continue;
                    if (NetEnemies.InPlay(c) && !c.gameObject.activeInHierarchy && !NetEnemies.ClientMode) continue; // culled on the host: not shared (see "enemies")
                    var e = ctx.Add(c.gameObject);
                    e["inPlay"] = NetEnemies.InPlay(c); // a copy is alive while the host's is in play
                    if (NetEnemies.InPlay(c))
                        e["health"] = c.maxHealth > 0f ? Mathf.RoundToInt(Mathf.Clamp01(c.health / c.maxHealth) * 100f) : -1;
                    else
                    {
                        var sp = c.shadow != null ? c.shadow.GetComponent<tk2dBaseSprite>() : null;
                        e["shadow"] = sp == null ? "none" : sp.color.a < 0.1f ? "faded" : "shown";
                        e["particles"] = c.GetComponentsInChildren<ParticleSystem>().Count(ps => ps != null && ps.isEmitting);
                    }
                }
            });
        }

        // ---- helpers ------------------------------------------------------------------------------------------------

        private static Player P => Player.Instance;
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static IEnumerator GoTo(string[] a, Result r)
        {
            var to = Spot(a.Length > 0 ? a[0] : "");
            yield return Walk(to, 40f);
            r.Info = $"at {Fmt(P.transform.position)}";
        }

        /// <summary>The nearest spot to `at` with nothing solid on it.</summary>
        private static Vector3 FreeSpot(Vector3 at)
        {
            for (int ring = 0; ring < 12; ring++)
                for (int i = 0; i < Math.Max(1, ring * 6); i++)
                {
                    float an = i * Mathf.PI * 2f / Math.Max(1, ring * 6);
                    var p = at + new Vector3(Mathf.Cos(an), 0f, Mathf.Sin(an)) * ring * 50f;
                    if (!Physics.OverlapSphere(p, 60f, ~0, QueryTriggerInteraction.Ignore).Any(c => c.attachedRigidbody == null || !c.attachedRigidbody.CompareTag("Player")))
                        return p;
                }
            return at;
        }

        /// <summary>The shared enemy (one with a net id) nearest to a spot.</summary>
        private static Character Shared(Vector3 at, Func<Character, bool> ok = null, float within = 900f)
        {
            var ne = NetEnemies.Instance;
            if (ne == null) return null;
            return Object.FindObjectsOfType<Character>().Where(c => c != null && NetEnemies.Eligible(c) && ne.NetIdOf(c, out _) && (ok == null || ok(c))
                && StateDump.Flat(c.transform.position - at) < within).OrderBy(c => StateDump.Flat(c.transform.position - at)).FirstOrDefault();
        }

        private static uint IdOf(Character c) { uint id = 0; NetEnemies.Instance?.NetIdOf(c, out id); return id; }

        private static Character SpawnAt(Vector3 at, string type = "Dog")
        {
            var spot = FreeSpot(at);
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(P.gameObject, spot, 1f, type, nocturnal: false, attackPlayer: false);
            if (c != null) c.transform.position = spot;
            return c;
        }

        /// <summary>Where the other player is in this game: the host's proxy for them, or their puppet.</summary>
        private static Transform Other()
        {
            foreach (var p in NetPlayerProxy.All.Values) if (p != null) return p.transform;
            var ps = Sync.PlayerSync.Instance;
            if (ps != null)
                foreach (var rp in ps.Remotes.Values)
                    if (rp.Owner != Sync.PlayerSync.DummyId && rp.Root != null) return rp.Root.transform;
            return null;
        }

        private static float BarFraction(EnemyHealthBar bar) => bar != null && bar.full != null ? bar.full.clipTopRight.x : -1f;

        // ---- 43-45: a copy's health bar, looks and pre-death stage ---------------------------------------------------

        private static Character _fx;      // host: the dog
        private static float _fxHealth;    // host: its health before the client's hit
        private static uint _fxId;         // client: the copy fx-hit hit

        private static Character ById(uint id)
        {
            var ne = NetEnemies.Instance;
            return ne == null ? null : Object.FindObjectsOfType<Character>().FirstOrDefault(c => c != null && ne.NetIdOf(c, out uint i) && i == id);
        }

        /// <summary>"fx-spawn [predeath]" (host): a Dog at the "fx" spot (with a pre-death stage of 40 health).</summary>
        private static IEnumerator FxSpawn(string[] a, Result r)
        {
            var c = SpawnAt(Spot("fx"));
            if (c == null) { r.Fail("couldn't spawn a dog"); yield break; }
            _fx = c;
            yield return new WaitForSeconds(1.5f);
            if (a.Contains("predeath")) { c.hasPreDeath = true; c.preDeathMaxHealth = 40f; }
            _fxHealth = c.health;
            r.Info = $"{c.name} #{IdOf(c)} at {Fmt(c.transform.position)}, health {c.health}/{c.maxHealth}, preDeath {c.hasPreDeath}";
        }

        /// <summary>"fx-hit DAMAGE" (client): hit the host's dog once. Its health bar shows here, then follows the host's
        /// health (44); its shadow lies as the game places it (45).</summary>
        private static IEnumerator FxHit(string[] a, Result r)
        {
            float dmg = a.Length > 0 ? F(a[0]) : 20f;
            Character c = null;
            float until = Time.realtimeSinceStartup + 6f;
            while (c == null && Time.realtimeSinceStartup < until) { c = Shared(Spot("fx"), x => x.alive && x.name.StartsWith("Dog")); if (c == null) yield return new WaitForSeconds(0.3f); }
            if (c == null) { r.Fail("no copy of the host's dog here"); yield break; }
            _fxId = IdOf(c);
            yield return Walk(c.transform.position, 90f);
            float before = c.health;
            AsPlayer(() => c.getHit(dmg, P.transform, false, true, true, true, false, false, false));
            var bar = Singleton<UI>.Instance.enemyHealthBar;
            bool shownAtOnce = bar.currentObj == c.gameObject;
            yield return new WaitForSeconds(1.5f); // the host's next states
            float frac = c.maxHealth > 0f ? c.health / c.maxHealth : -1f;
            r.Info = FormattableString.Invariant($"{c.name} #{IdOf(c)}: health {before:0} -> {c.health:0} of {c.maxHealth:0}; bar on it {bar.currentObj == c.gameObject} (at once {shownAtOnce}), bar {BarFraction(bar):0.00}");
            if (!shownAtOnce) r.Fail($"our hit didn't show its health bar: {r.Info}");
            else if (!(c.health < before - 0.5f)) r.Fail($"the host's health for it didn't come: {r.Info}");
            else if (bar.currentObj == c.gameObject && Mathf.Abs(BarFraction(bar) - frac) > 0.03f) r.Fail($"the bar wasn't refreshed with the host's health: {r.Info}");
            // 45: its shadow, as Character.Update places it.
            if (c.shadow != null && r.Failure == null)
            {
                var sh = c.shadow;
                r.Info += FormattableString.Invariant($"; shadow rot {sh.eulerAngles.x:0},{sh.eulerAngles.y:0} (rotateShadow {c.rotateShadow}), dy {sh.position.y - c.transform.position.y:0}, off {StateDump.Flat(sh.position - c.transform.position - new Vector3(c.shadowOffset.x, 0f, c.shadowOffset.z)):0}");
                if (!c.rotateShadow && (Mathf.Abs(Mathf.DeltaAngle(sh.eulerAngles.x, 90f)) > 1f || Mathf.Abs(Mathf.DeltaAngle(sh.eulerAngles.y, 0f)) > 1f))
                    r.Fail($"its shadow is turned with the body: {r.Info}");
                else if (StateDump.Flat(sh.position - c.transform.position - new Vector3(c.shadowOffset.x, 0f, c.shadowOffset.z)) > 20f)
                    r.Fail($"its shadow isn't under it: {r.Info}");
            }
            yield return Walk(Centre, 60f);
        }

        /// <summary>"fx-hurt" (host): the client's hit reached the dog, and the host's bar doesn't show it (not ours, not selected).</summary>
        private static IEnumerator FxHurt(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.3f);
            if (_fx == null) { r.Fail("no dog (fx-spawn first)"); yield break; }
            var bar = Singleton<UI>.Instance.enemyHealthBar;
            r.Info = $"{_fx.name}: health {_fxHealth:0} -> {_fx.health:0}, our bar on it {bar.currentObj == _fx.gameObject}";
            if (!(_fx.health < _fxHealth)) r.Fail($"the client's hit did nothing here: {r.Info}");
            else if (bar.currentObj == _fx.gameObject && P.selectedObject != _fx.transform) r.Fail($"the client's hit showed our health bar: {r.Info}");
        }

        /// <summary>"fx-predeath" (host): our player brings the dog down to its pre-death stage (43): not alive, still in play.</summary>
        private static IEnumerator FxPreDeath(string[] a, Result r)
        {
            if (_fx == null) { r.Fail("no dog"); yield break; }
            var c = _fx;
            AsPlayer(() => c.getHit(c.health + 5f, P.transform, false, true, false, true, false, false, false));
            yield return new WaitForSeconds(1f);
            r.Info = $"{c.name}: alive {c.alive}, health {c.health:0}, in play {NetEnemies.InPlay(c)}";
            if (c.alive) r.Fail($"it's still alive: {r.Info}");
            else if (!NetEnemies.InPlay(c)) r.Fail($"it didn't go into its pre-death stage: {r.Info}");
        }

        /// <summary>"fx-crawling" (client): our copy of the crawling dog is still in the fight (not a corpse), and our
        /// finishing blow is taken by the host.</summary>
        private static IEnumerator FxCrawling(string[] a, Result r)
        {
            int mark = MapScenarioIssues.LogMark();
            yield return new WaitForSeconds(1f);
            var c = ById(_fxId);
            if (c == null) { r.Fail($"no copy of the dog #{_fxId} here (fx-hit first)"); yield break; }
            uint id = IdOf(c);
            bool settled = MapScenarioIssues.LogLines().Skip(mark).Any(l => l.Contains($"#{id} died"));
            string crawl = $"{c.name} #{id}: alive {c.alive}, health {c.health:0} of {c.maxHealth:0}, settled {settled}";
            if (!c.alive || settled) { r.Fail($"it's a corpse here while it crawls on the host: {crawl}"); yield break; }
            int hits = 0;
            while (c != null && c.alive && hits < 12)
            {
                yield return Walk(c.transform.position, 90f);
                hits++;
                AsPlayer(() => c.getHit(25f, P.transform, false, true, true, true, false, false, false));
                yield return new WaitForSeconds(0.6f);
            }
            r.Info = $"{crawl}; then {hits} hit(s): alive {(c != null && c.alive)}";
            if (c != null && c.alive) r.Fail($"our hits didn't finish it: {r.Info}");
            yield return Walk(Centre, 60f);
        }

        /// <summary>"fx-dead" (host): the dog is really dead now.</summary>
        private static IEnumerator FxDead(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            if (_fx == null) { r.Fail("no dog"); yield break; }
            r.Info = $"{_fx.name}: alive {_fx.alive}, health {_fx.health:0}, in play {NetEnemies.InPlay(_fx)}";
            if (NetEnemies.InPlay(_fx)) r.Fail($"it's still in play: {r.Info}");
        }

        /// <summary>"fx-corpse" (client): our copy settled into a corpse, its shadow faded and its particles stopped (45).</summary>
        private static IEnumerator FxCorpse(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 8f;
            Character c = null;
            while (Time.realtimeSinceStartup < until)
            {
                c = ById(_fxId);
                if (c != null && !c.alive && c.GetComponent<Item>() != null) break;
                yield return new WaitForSeconds(0.5f);
            }
            if (c == null || c.alive) { r.Fail($"the dog #{_fxId} isn't dead here"); yield break; }
            yield return new WaitForSeconds(1.5f); // the shadow's fade takes a second
            var sp = c.shadow != null ? c.shadow.GetComponent<tk2dBaseSprite>() : null;
            int emitting = c.GetComponentsInChildren<ParticleSystem>().Count(ps => ps != null && ps.isEmitting);
            r.Info = FormattableString.Invariant($"{c.name} #{IdOf(c)}: searchable {c.GetComponent<Item>() != null}, shadow alpha {(sp != null ? sp.color.a : -1f):0.00}, emitting particles {emitting}");
            if (c.GetComponent<Item>() == null) r.Fail($"its corpse isn't searchable: {r.Info}");
            else if (sp != null && sp.color.a > 0.1f) r.Fail($"its shadow didn't fade: {r.Info}");
            else if (emitting > 0) r.Fail($"its particles still play: {r.Info}");
        }

        // ---- 28: an InteractiveItem's switch ------------------------------------------------------------------------

        private static InteractiveItem _ii;
        private static global::EventTrigger _onT, _offT;

        private static readonly string[] SwitchHosts = { "candles_01", "Radio", "Stove" };

        /// <summary>"ii-setup" (both games): make the station's candles (a saved object with nothing else to do) a switch (an InteractiveItem with an on and an
        /// off trigger, whose firing we can see). The test world has none of the game's own.</summary>
        private static IEnumerator IiSetup(string[] a, Result r)
        {
            SaveableObject host = null;
            foreach (var n in SwitchHosts)
            {
                host = Nearest(Object.FindObjectsOfType<SaveableObject>().Where(i => i.gameObject.name.Replace("(Clone)", "").Trim() == n && ObjRef.Of(i.gameObject).Valid
                    && StateDump.Flat(i.transform.position - Centre) < 1500f), Centre);
                if (host != null) break;
            }
            if (host == null) { r.Fail($"none of {string.Join(", ", SwitchHosts)} here"); yield break; }
            var tex = Resources.LoadAll<Texture2D>("InteractiveItems/Textures");
            if (tex == null || tex.Length == 0) { r.Fail("no InteractiveItems textures"); yield break; }
            var ii = host.GetComponent<InteractiveItem>() ?? host.gameObject.AddComponent<InteractiveItem>();
            ii.textureOn = tex[0].name;
            ii.textureOff = tex[tex.Length > 1 ? 1 : 0].name;
            ii.switchSound = "";
            ii.doesNotPauseGame = true;
            ii.isOn = false;
            ii.onTrigger = MakeTrigger(host.transform, "mapOnTrigger", out _onT);
            ii.offTrigger = MakeTrigger(host.transform, "mapOffTrigger", out _offT);
            _ii = ii;
            yield return null;
            r.Info = $"{host.name} {ObjRef.Of(host.gameObject)} at {Fmt(host.transform.position)} (textures {ii.textureOn}/{ii.textureOff})";
        }

        private static EventTriggers MakeTrigger(Transform parent, string name, out global::EventTrigger t)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ets = go.AddComponent<EventTriggers>();
            t = new global::EventTrigger { type = global::EventTrigger.Type.area, multipleFire = true };
            ets.eventTriggers.Add(t);
            return ets;
        }

        private static void HideSwitchWindow()
        {
            var w = Singleton<UI>.Instance.transform.Find("InteractiveItem");
            if (w != null) w.gameObject.SetActive(false);
        }

        /// <summary>"ii-switch on|off": our player switches it (InteractiveItem.switchMe from its window); its trigger fires here.</summary>
        private static IEnumerator IiSwitch(string[] a, Result r)
        {
            if (_ii == null) { r.Fail("no switch (ii-setup first)"); yield break; }
            bool on = a.Length == 0 || a[0] == "on";
            yield return Walk(_ii.transform.position);
            if (_ii.isOn == on) { r.Fail($"it's already {(on ? "on" : "off")} here"); yield break; }
            _onT.fired = false; _offT.fired = false;
            AsPlayer(() => _ii.switchMe());
            HideSwitchWindow();
            yield return new WaitForSeconds(1f);
            var mine = on ? _onT : _offT;
            r.Info = $"{_ii.name}: on {_ii.isOn}, its {(on ? "on" : "off")} trigger fired {mine.fired}";
            if (_ii.isOn != on) r.Fail($"it didn't switch (refused?): {r.Info}");
            else if (!mine.fired) r.Fail($"its trigger didn't fire here: {r.Info}");
            yield return Walk(Centre, 60f);
        }

        /// <summary>"ii-check on|off [--fired|--not-fired]": the other player's switch reached us; whether its trigger fired here.</summary>
        private static IEnumerator IiCheck(string[] a, Result r)
        {
            if (_ii == null) { r.Fail("no switch (ii-setup first)"); yield break; }
            bool on = a.Length == 0 || a[0] == "on";
            float until = Time.realtimeSinceStartup + 4f;
            while (_ii.isOn != on && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.2f);
            yield return new WaitForSeconds(0.5f);
            var t = on ? _onT : _offT;
            r.Info = $"{_ii.name}: on {_ii.isOn}, its {(on ? "on" : "off")} trigger fired here {t.fired}";
            if (_ii.isOn != on) r.Fail($"the other's switch didn't reach us: {r.Info}");
            else if (a.Contains("--fired") && !t.fired) r.Fail($"its events didn't run here: {r.Info}");
            else if (a.Contains("--not-fired") && t.fired) r.Fail($"its events ran here too: {r.Info}");
            _onT.fired = false; _offT.fired = false;
        }

        // ---- 40: a held throwable dropped -------------------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Player, GameObject> HeldItem = AccessTools.FieldRefAccess<Player, GameObject>("heldItem");
        private static readonly System.Reflection.MethodInfo DropItemM = AccessTools.Method(typeof(Player), "dropItem", new[] { typeof(bool) });

        /// <summary>"drop-held TYPE": take TYPE out, hold it as aiming does (Player's heldItem), and drop it as the game
        /// does when the aim is let go of (Player.dropItem).</summary>
        private static IEnumerator DropHeld(string[] a, Result r)
        {
            var p = P;
            string type = a.Length > 0 ? a[0] : "flare";
            yield return Walk(FreeSpot(Spot("drop")), 40f);
            int free = p.Hotbar.slots.FindIndex(s => InvItemClass.isNull(s.invItem));
            if (free < 0) free = p.Hotbar.slots.Count - 1;
            var slot = p.Hotbar.slots[free];
            slot.inventory = p.Hotbar;
            if (!InvItemClass.isNull(slot.invItem)) slot.removeItem();
            slot.createItem(type, 1);
            foreach (var s in p.Hotbar.slots) s.deselect();
            slot.select(noiseless: true);
            float until = Time.realtimeSinceStartup + 5f;
            while ((InvItemClass.isNull(p.currentItem) || p.currentItem.type != type || p.switchingItem) && Time.realtimeSinceStartup < until) yield return null;
            if (InvItemClass.isNull(p.currentItem) || p.currentItem.type != type) { r.Fail($"couldn't take the {type} out"); yield break; }
            if (!p.currentItem.baseClass.isThrowable) { r.Fail($"a {type} isn't throwable"); yield break; }
            // As Player's aim does for a throwable.
            var held = Core.AddPrefab(p.currentItem.baseClass.item, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), p.gameObject);
            if (held == null) { r.Fail($"couldn't make a held {type}"); yield break; }
            var ren = held.GetComponent<Renderer>(); if (ren != null) ren.enabled = false;
            var hc = held.GetComponent<Collider>(); var pc = p.GetComponent<Collider>();
            if (hc != null && pc != null) Physics.IgnoreCollision(hc, pc);
            HeldItem(p) = held;
            yield return new WaitForSeconds(0.3f);
            AsPlayer(() => DropItemM.Invoke(p, new object[] { true }));
            var ti = held != null ? held.GetComponent<ThrownItem>() : null;
            r.Info = $"{type} ({held?.name}) at {(held != null ? Fmt(held.transform.position) : "-")}: held {HeldItem(p) != null}, on the ground {(ti != null && ti.onGround)}";
            if (HeldItem(p) != null) r.Fail($"still held: {r.Info}");
            yield return new WaitForSeconds(1.5f);
            yield return Walk(Centre, 60f);
        }

        /// <summary>"expect-dropped TYPE": the other player's dropped TYPE lies here by them, dropped (ThrownItem.dropMe).</summary>
        private static IEnumerator ExpectDropped(string[] a, Result r)
        {
            string type = a.Length > 0 ? a[0] : "flare";
            var it = Singleton<ItemsDatabase>.Instance.getItem(type, instantiate: false);
            if (it == null || it.item == null) { r.Fail($"no {type} in the database"); yield break; }
            string prefab = it.item.name;
            GameObject found = null;
            float until = Time.realtimeSinceStartup + 4f;
            while (found == null && Time.realtimeSinceStartup < until)
            {
                found = ThrownHits.Copies.Where(g => g != null && g.name.StartsWith(prefab, StringComparison.Ordinal)
                    && StateDump.Flat(g.transform.position - Spot("drop")) < 500f).FirstOrDefault();
                if (found == null) yield return new WaitForSeconds(0.3f);
            }
            if (found == null) { r.Fail($"no copy of a dropped {type} ({prefab}) by the drop spot here"); yield break; }
            var ti = found.GetComponent<ThrownItem>();
            var rb = found.GetComponent<Rigidbody>();
            r.Info = $"{found.name} at {Fmt(found.transform.position)}: on the ground {(ti != null && ti.onGround)}, speed {(rb != null ? rb.velocity.magnitude : 0f):0}";
            if (ti == null || !ti.onGround) r.Fail($"it wasn't dropped (ThrownItem.dropMe) here: {r.Info}");
        }

        // ---- 50: near a joiner -------------------------------------------------------------------------------------

        /// <summary>"near-joiner" (host, the client standing at "client"): a dog 1200 from the client and 2600 from us
        /// is near a player (Character.inSightOrCloseToPlayer, NearJoinerPatch); one 1700 from the client and 2200
        /// from us isn't.</summary>
        private static IEnumerator NearJoiner(string[] a, Result r)
        {
            var other = Other();
            if (other == null) { r.Fail("no proxy for the client"); yield break; }
            var near = SpawnAt(Spot("near"));
            var far = SpawnAt(Spot("far"));
            if (near == null || far == null) { r.Fail("couldn't spawn the dogs"); yield break; }
            yield return new WaitForSeconds(1f);
            float dn = Core.trueDistance(other.position, near.transform.position), df = Core.trueDistance(other.position, far.transform.position);
            float hn = Core.trueDistance(P.transform.position, near.transform.position), hf = Core.trueDistance(P.transform.position, far.transform.position);
            bool nearRes = near.inSightOrCloseToPlayer(), farRes = far.inSightOrCloseToPlayer();
            r.Info = $"near dog: {dn:0} from the client, {hn:0} from us: {nearRes}; far dog: {df:0} / {hf:0}: {farRes}";
            Object.Destroy(near.gameObject);
            Object.Destroy(far.gameObject);
            if (dn > 1450f || hn < 1600f) r.Fail($"the client isn't where the step needs it: {r.Info}");
            else if (!nearRes) r.Fail($"a dog by the client isn't near a player: {r.Info}");
            else if (farRes) r.Fail($"a dog far from both is near a player: {r.Info}");
        }

        // ---- 51: the forest spirit ------------------------------------------------------------------------------------

        /// <summary>"fx-inside": walk onto the inside ground (Ground.isInside) nearest the centre: the hideout.</summary>
        private static IEnumerator FxInside(string[] a, Result r)
        {
            var spot = Inside();
            if (!spot.HasValue) { r.Fail("no inside ground near the centre"); yield break; }
            yield return Walk(spot.Value, 20f);
            yield return new WaitForSeconds(1f);
            r.Info = $"at {Fmt(P.transform.position)}: inside {P.isInside} (ground there {JoinerAi.InsideAt(P.transform.position)})";
            if (!JoinerAi.InsideAt(P.transform.position)) r.Fail($"not on inside ground: {r.Info}");
        }

        private static Vector3? Inside()
        {
            Vector3? best = null; float bd = float.MaxValue;
            foreach (var g in Object.FindObjectsOfType<Ground>())
            {
                if (g == null || !g.isInside) continue;
                var col = g.GetComponent<Collider>();
                if (col == null) continue;
                var c = col.bounds.center; c.y = P.transform.position.y;
                float d = StateDump.Flat(c - Centre);
                if (d < bd && JoinerAi.InsideAt(c)) { bd = d; best = c; }
            }
            return best;
        }

        private static readonly System.Reflection.MethodInfo CheckStuff = AccessTools.Method(typeof(Character), "checkStuff");

        /// <summary>"spirit leaves|stays" (host): a dog made a forest spirit after the client's proxy runs its check
        /// (Character.checkStuff): it leaves when the client is inside (whatever the host is), stays when they aren't.</summary>
        private static IEnumerator Spirit(string[] a, Result r)
        {
            bool leaves = a.Length == 0 || a[0] == "leaves";
            NetPlayerProxy proxy = NetPlayerProxy.All.Values.FirstOrDefault(x => x != null);
            if (proxy == null) { r.Fail("no proxy"); yield break; }
            var c = SpawnAt(Spot("spirit"));
            if (c == null) { r.Fail("couldn't spawn a dog"); yield break; }
            yield return new WaitForSeconds(1.5f);
            bool clientInside = JoinerAi.InsideAt(proxy.transform.position);
            string state = $"client inside {clientInside}, we inside {P.isInside}";
            c.forestSpirit = true;
            c.target = proxy.transform;
            CheckStuff.Invoke(c, null);
            if (c != null) c.forestSpirit = false; // its own checks from now on are a dog's
            yield return new WaitForSeconds(4f); // removeMe destroys a spirit 3 s later
            bool gone = c == null;
            r.Info = $"{state}: the spirit {(gone ? "left" : "stayed")}";
            if (!gone) Object.Destroy(c.gameObject);
            // leaves: the client inside, we outside (the old check, on our player, would keep it); stays: the other way round.
            if (clientInside != leaves || P.isInside == leaves) r.Fail($"the players aren't where the step needs them: {r.Info}");
            else if (gone != leaves) r.Fail(r.Info);
        }

        // ---- 58: slow motion -------------------------------------------------------------------------------------------

        /// <summary>"slowmo": in co-op, the skill wheel's slow motion (Core.setTimeScale 0.2), a scripted event's
        /// (Controller.alterTimeScale) and the wheel itself leave the time scale at 1.</summary>
        private static IEnumerator Slowmo(string[] a, Result r)
        {
            var parts = new List<string>();
            float min = 1f;
            Core.setTimeScale(0.2f);
            yield return null;
            min = Mathf.Min(min, Time.timeScale);
            parts.Add(FormattableString.Invariant($"setTimeScale(0.2): {Time.timeScale:0.00}"));
            var ctl = Singleton<Controller>.Instance;
            ctl.StartCoroutine(ctl.alterTimeScale(2f, 0.2f));
            float until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until) { min = Mathf.Min(min, Time.timeScale); yield return null; }
            parts.Add(FormattableString.Invariant($"alterTimeScale: lowest {min:0.00}"));
            var wheel = Singleton<UI>.Instance.activeSkillsMenu;
            // The wheel opens only with a skill to pick: lend it one of the game's while it's open.
            PlayerSkill lent = null;
            if (P.skills != null && P.skills.manuallyActivatedSkills.Count == 0)
            {
                lent = P.skills.availableSkills.Concat(P.skills.skills).Concat(P.skills.progressionSkills).FirstOrDefault(sk => sk != null);
                if (lent != null) P.skills.manuallyActivatedSkills.Add(lent);
            }
            if (wheel != null && P.skills != null && P.skills.manuallyActivatedSkills.Count > 0)
            {
                wheel.show();
                until = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < until) { min = Mathf.Min(min, Time.timeScale); yield return null; }
                parts.Add(FormattableString.Invariant($"the wheel open {wheel.opened}: {Time.timeScale:0.00}"));
                if (wheel.opened) wheel.hide();
                if (lent != null) P.skills.manuallyActivatedSkills.Remove(lent);
            }
            else parts.Add($"no skills to lend ({P.skills?.availableSkills.Count} available, {P.skills?.skills.Count} known): the wheel wasn't opened");
            if (Time.timeScale != 1f && Time.timeScale > 0f) Core.setTimeScale(1f);
            r.Info = string.Join("; ", parts);
            if (min < 0.99f) r.Fail($"time slowed in co-op: {r.Info}");
        }

        // ---- 24: the night trader killed ------------------------------------------------------------------------------

        /// <summary>"kill-trader" (client): our player kills the night trader; our own game plays the killer's dream.</summary>
        private static IEnumerator KillTrader(string[] a, Result r)
        {
            var t = Nearest(Object.FindObjectsOfType<Character>().Where(c => c.isNightTrader && c.alive), Centre);
            if (t == null) { r.Fail("no living night trader here"); yield break; }
            string what = $"{t.name} {ObjRef.Of(t.gameObject)}";
            int hits = 0;
            while (t != null && t.alive && hits < 30)
            {
                yield return Walk(t.transform.position, 90f);
                hits++;
                AsPlayer(() => t.getHit(80f, P.transform, false, true, true, true, false, false, false));
                yield return new WaitForSeconds(0.4f);
            }
            yield return new WaitForSeconds(1f);
            var dreams = Singleton<Dreams>.Instance;
            bool dream = Core.EnteringDream || (dreams != null && dreams.currentTransition != null);
            r.Info = $"{what}: alive {(t != null && t.alive)} after {hits} hits; our dream {dream}";
            if (t != null && t.alive) r.Fail($"still alive: {r.Info}");
            else if (!dream) r.Fail($"the killer's own dream didn't play: {r.Info}");
        }

        /// <summary>"trader-dream" (host): the night trader died here too, and our player didn't get the killer's dream.</summary>
        private static IEnumerator TraderDream(string[] a, Result r)
        {
            yield return new WaitForSeconds(2f);
            var alive = Object.FindObjectsOfType<Character>().Where(c => c.isNightTrader && c.alive && StateDump.Flat(c.transform.position - Centre) < 2000f).ToList();
            var dreams = Singleton<Dreams>.Instance;
            bool dream = Core.EnteringDream || (dreams != null && dreams.currentTransition != null) || P.endingSleep;
            bool logged = MapScenarioIssues.LogLines().Any(l => l.Contains("the night trader was killed in another game"));
            r.Info = $"living night traders here {alive.Count}; our dream {dream}; skipped it {logged}";
            if (alive.Count > 0) r.Fail($"the night trader is alive here: {r.Info}");
            else if (dream) r.Fail($"we got the killer's dream: {r.Info}");
            else if (!logged) r.Fail($"the replayed death didn't go by NightTraderDreamPatch: {r.Info}");
        }
    }
}
