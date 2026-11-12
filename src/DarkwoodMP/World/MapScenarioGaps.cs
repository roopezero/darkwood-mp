using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for gaps in docs/INTERACTIONS.md that no other test reached: the dream clock
    /// (11), journal items handed over (61), the morning in the hideout (62, 63, 38), food rotting in an open
    /// container (64), vines (65), shadow-covered items (66) and a death bag's map marker (68). The order they run in
    /// is tools/tests/map_scenario_gaps.py.
    ///
    /// The things these steps add are put at fixed offsets from the centre (<see cref="Spots"/>), away from the
    /// stations, and found again in both games as the nearest of their kind to that spot. Waits are in real time:
    /// a dialogue may slow or stop the game's time.
    /// </summary>
    public static class MapScenarioGaps
    {
        private static List<NPC> _awayTraders = new List<NPC>();

        private static readonly Dictionary<string, Vector3> Spots = new Dictionary<string, Vector3>
        {
            ["rot"] = new Vector3(-700f, 0f, 700f),
            ["shadow"] = new Vector3(700f, 0f, -700f),
            ["vine"] = new Vector3(-700f, 0f, -700f),
            ["transport"] = new Vector3(600f, 0f, 600f),
        };
        private static Vector3 Spot(string which) => Centre + Spots[which];

        public static void Register()
        {
            MapScenario.Add("gaps-setup", Setup);
            // 11 dreams
            MapScenario.Add("dream", Dream, timeout: 30f);
            MapScenario.Add("clock-note", (a, r) => { _noted = Singleton<Controller>.Instance.CurrentTime; r.Info = $"{_noted}"; return Nothing(); });
            MapScenario.Add("clock-check", ClockCheck, timeout: 30f);
            // 61 journal
            MapScenario.Add("journal-pick", JournalPick);
            MapScenario.Add("journal-add", JournalAdd);
            MapScenario.Add("journal-remove", JournalRemove);
            MapScenario.Add("journal-has", JournalHas, timeout: 20f);
            // 62, 63, 38 the morning
            MapScenario.Add("an-start", AnStart, timeout: 40f);
            // "trader-away" (both games): the test area's night trader goes. In the game he is only ever in the
            // hideout in the morning; two copies of one trader at once (the station's and the morning's) get
            // their stock by name (TraderSync), on a different copy in each game.
            // He is switched off, not destroyed (TraderSync only finds active traders), and "trader-back" switches him on
            // again after the morning steps, for the night trader's kill (map_scenario_fixchecks.py) at the end.
            MapScenario.Add("trader-away", (a, r) =>
            {
                _awayTraders = StateDump.All<NPC>().Where(n => n.trader && n.name == "nightTrader" && n.gameObject.activeSelf && StateDump.Flat(n.transform.position - Centre) < 2000f).ToList();
                foreach (var n in _awayTraders) n.gameObject.SetActive(false);
                r.Info = $"{_awayTraders.Count} switched off";
                return Nothing();
            });
            MapScenario.Add("trader-back", (a, r) =>
            {
                int n = 0;
                foreach (var t in _awayTraders) if (t != null) { t.gameObject.SetActive(true); n++; }
                _awayTraders.Clear();
                r.Info = $"{n} back";
                return Nothing();
            });
            MapScenario.Add("an-end", AnEnd, timeout: 20f);
            MapScenario.Add("an-check", AnCheck, timeout: 20f);
            MapScenario.Add("an-clock", AnClock, timeout: 40f);
            MapScenario.Add("talk", Talk, timeout: 30f);
            MapScenario.Add("talk-end", TalkEnd, timeout: 20f);
            MapScenario.Add("trade-open", TradeOpen, timeout: 20f);
            MapScenario.Add("talk-free", TalkFree, timeout: 30f);
            // 64 rot
            MapScenario.Add("rot-open", RotOpen, timeout: 60f);
            MapScenario.Add("rot-host", RotHost);
            // 65 vines
            MapScenario.Add("vine-make", VineMake);
            MapScenario.Add("vine-cut", VineCut, timeout: 20f);
            MapScenario.Add("vine-check", VineCheck, timeout: 20f);
            // 66 shadows
            MapScenario.Add("shadow-ready", ShadowReady);
            MapScenario.Add("shadow-hit", ShadowHit, timeout: 20f);
            MapScenario.Add("shadow-check", ShadowCheck, timeout: 30f);
            // 68 death bag marker
            MapScenario.Add("bag-drop", BagDrop, timeout: 20f);
            MapScenario.Add("bag-check", BagCheck, timeout: 20f);
            MapScenario.Add("bag-take", BagTake, timeout: 30f);

            StateDump.AddSection("vines", ctx =>
            {
                foreach (var cp in StateDump.All<ChainParent>())
                {
                    if (!ctx.Near(cp.transform.position)) continue;
                    var e = ctx.Add(cp.gameObject);
                    e["attached"] = cp.attached; e["health"] = StateDump.R(cp.health, 1f);
                }
            });
            StateDump.AddSection("shadows", ctx =>
            {
                var crate = ShadowCrate();
                if (crate != null && ctx.Near(crate.transform.position))
                {
                    var e = ctx.Add(crate.gameObject);
                    e["shadow"] = crate.GetComponent<ShadowArmor>() != null; e["health"] = crate.health; e["destroyed"] = crate.destroyed;
                }
                foreach (var sa in StateDump.All<ShadowArmor>())
                {
                    if (sa.GetComponent<Item>() == null || sa.GetComponent<Character>() != null || !ctx.Near(sa.transform.position)) continue;
                    if (crate != null && sa.gameObject == crate.gameObject) continue;
                    ctx.Add(sa.gameObject)["shadow"] = true;
                }
            });
            StateDump.AddSection("deathbags", ctx =>
            {
                foreach (var d in StateDump.All<DeathDrop>())
                {
                    if (!ctx.Near(d.transform.position)) continue;
                    var e = ctx.Add(d.gameObject);
                    e["marker"] = d.additionalMapMarker != null ? (object)StateDump.Pos(d.additionalMapMarker.transform.position) : "none";
                }
            });
            StateDump.AddSection("afternight", ctx =>
            {
                var c = Singleton<Controller>.Instance;
                if (c != null) ctx.Set("morning", new Dictionary<string, object> { ["afterNight"] = c.isAfterNight });
            });
        }

        private static IEnumerator Nothing() { yield break; }
        private static IEnumerator Real(float s) { yield return new WaitForSecondsRealtime(s); }
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");
        private static List<string> LogSince(int mark) => MapScenarioIssues.LogLines().Skip(Math.Max(0, mark)).ToList();

        private static T NearestAll<T>(Vector3 to, Func<T, bool> ok) where T : Component =>
            StateDump.All<T>().Where(ok).OrderBy(t => StateDump.Flat(t.transform.position - to)).FirstOrDefault();

        private static GameObject Place(string prefab, Vector3 at, bool save = true)
        {
            var go = Core.AddPrefab(prefab, at, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true);
            if (go != null && save) Core.addToSaveable(go, isDynamic: true, assignID: true);
            return go;
        }

        // ---- setup (host, before the join) ---------------------------------------------------------------------

        private const string RotPrefab = "Furnitures/Cottage/Wardrobe_low_small_01";
        private static readonly string[] ShadowPrefabs = { "Locations/med_hideout_05/LootContainer_MetalCrate_med_hideout_05_level", "Loot_containers/_Locations/LootContainer_WoodenCrate_1A" };

        private static Inventory RotWardrobe() => NearestAll<Inventory>(Spot("rot"), i => i.invType == Inventory.InvType.itemInv && i.name.StartsWith("Wardrobe_low_small_01", StringComparison.Ordinal) && StateDump.Flat(i.transform.position - Spot("rot")) < 200f);
        private static Item ShadowCrate() => NearestAll<Item>(Spot("shadow"), i => StateDump.Flat(i.transform.position - Spot("shadow")) < 120f && i.GetComponent<Inventory>() != null);

        /// <summary>The first item type (by name) that rots in a container: one with a rotten form whose durability
        /// drains. One that isn't an experience item (mushrooms) if there is one: those drain only while the game's
        /// clock runs, which on a joiner it never does (the clock is the host's).</summary>
        private static string RotFood(out string all)
        {
            var db = Singleton<ItemsDatabase>.Instance;
            var found = new List<(string type, bool exp)>();
            foreach (var t in db.itemsDict.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                InvItem it;
                try { it = db.getItem(t, instantiate: false); } catch { continue; }
                if (it != null && it.rottenItem != null && it.durabilityDrain > 0f && it.maxDurability > 0f) found.Add((t, it.isExpItem));
            }
            all = string.Join(",", found.Select(f => f.type + (f.exp ? "(exp)" : "")));
            return found.Where(f => !f.exp).Select(f => f.type).FirstOrDefault() ?? found.Select(f => f.type).FirstOrDefault();
        }

        /// <summary>"gaps-setup" (host, before the join): a wardrobe with rotting food in it; a crate with a shadow
        /// on it (one that dies from a few hits, and not from light).</summary>
        private static IEnumerator Setup(string[] a, Result r)
        {
            var made = new List<string>();
            var w = Place(RotPrefab, Spot("rot"));
            if (w == null) { r.Fail($"couldn't place {RotPrefab}"); yield break; }
            GameObject crate = null; string used = null;
            foreach (var pf in ShadowPrefabs)
            {
                if (Resources.Load("Prefabs/" + pf) == null) continue;
                crate = Place(pf, Spot("shadow"));
                if (crate != null) { used = pf; break; }
            }
            if (crate == null) { r.Fail("couldn't place a crate for the shadow"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);

            var inv = w.GetComponent<Inventory>() ?? w.GetComponentInChildren<Inventory>();
            string food = RotFood(out string foods);
            // Not a failure here (that would stop the whole run): rot-open fails later instead.
            if (inv == null || food == null) made.Add($"no inventory in the wardrobe ({inv != null}) or no rotting food (of {Singleton<ItemsDatabase>.Instance.itemsDict.Count} items: {foods})");
            else
            {
                if (inv.slots.Count == 0) inv.initSlots();
                foreach (var s in inv.slots) if (!InvItemClass.isNull(s.invItem)) s.removeItem();
                var max = Singleton<ItemsDatabase>.Instance.getItem(food, instantiate: false).maxDurability;
                inv.slots[0].createItem(food, 1); // durability 1: whole (the game multiplies it by the maximum)
                made.Add($"wardrobe {ObjRef.Of(inv.gameObject)} at {Fmt(w.transform.position)} with {food} ({max:0.##}; rotting: {foods})");
            }

            var item = crate.GetComponent<Item>();
            var sa = crate.GetComponent<ShadowArmor>() ?? crate.AddComponent<ShadowArmor>();
            sa.maxHealth = 30f; sa.health = 30f; sa.normalDamageMod = 1f; sa.lightDamage = 0f;
            AccessTools.FieldRefAccess<ShadowArmor, float>("destHealth")(sa) = 30f;
            if (item != null) item.shadowArmor = sa;
            made.Add($"shadow crate {used} {ObjRef.Of(crate)} at {Fmt(crate.transform.position)} (item {item != null})");
            r.Info = string.Join("; ", made);
        }

        // ---- 11 dreams ----------------------------------------------------------------------------------------

        private static int _noted;

        /// <summary>"dream start|end": as if our player went into a dream (Dreams.startDreaming: the world's time kept in
        /// timeCopy, the clock showing the dream's, 5 hours on) or woke (endDreaming: the clock back to timeCopy). A
        /// joiner uses the dream-switching flag, which TimeSync treats the same, so its game doesn't also wake its own
        /// enemies near it for a dream that isn't there.</summary>
        private static IEnumerator Dream(string[] a, Result r)
        {
            var d = Singleton<Dreams>.Instance;
            var c = Singleton<Controller>.Instance;
            bool client = Net.Session.Instance != null && Net.Session.Instance.IsClient;
            if (d == null || c == null) { r.Fail("no Dreams or Controller"); yield break; }
            if (a.Length > 0 && a[0] == "start")
            {
                d.timeCopy = c.CurrentTime;
                if (client) d.switchingDream = true; else d.dreaming = true;
                c.CurrentTime = (c.CurrentTime + 300) % 1440;
                _dreamTime = c.CurrentTime;
                r.Info = $"world time {d.timeCopy:0} kept, dream at {c.CurrentTime}";
            }
            else
            {
                c.CurrentTime = (int)d.timeCopy;
                d.dreaming = false; d.switchingDream = false;
                c.refreshTime(afterGameLoad: true);
                r.Info = $"woke at {c.CurrentTime}";
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        private static int _dreamTime;

        /// <summary>"clock-check noted|dream [SECS]": after SECS (3) real seconds, our clock is still the minute
        /// noted with clock-note (within 3: the host's world clock stood still while it dreamed), or still our dream's.</summary>
        private static IEnumerator ClockCheck(string[] a, Result r)
        {
            var c = Singleton<Controller>.Instance;
            float secs = a.Length > 1 ? float.Parse(a[1], CultureInfo.InvariantCulture) : 3f;
            yield return new WaitForSecondsRealtime(secs);
            int want = a.Length > 0 && a[0] == "dream" ? _dreamTime : _noted;
            int off = Math.Abs(((c.CurrentTime - want) % 1440 + 1440 + 720) % 1440 - 720);
            r.Info = $"clock {c.CurrentTime} (wanted {want}, {off} off)";
            if (off > 3) r.Fail(r.Info);
        }

        // ---- 61 journal ---------------------------------------------------------------------------------------

        private static string _jKind, _jType;

        /// <summary>"journal-pick key|item": the first key (or quest item) by name that our journal hasn't got. Run in
        /// both games, which have the same journal, so they pick the same.</summary>
        private static IEnumerator JournalPick(string[] a, Result r)
        {
            var j = Singleton<UI>.Instance.journal;
            var db = Singleton<JournalDatabase>.Instance;
            _jKind = a.Length > 0 ? a[0] : "key";
            var keys = _jKind == "key" ? db.keysDict.Keys : db.itemsDict.Keys;
            _jType = keys?.Where(k => !string.IsNullOrEmpty(k) && (_jKind == "key" ? !j.keysDict.ContainsKey(k) : !j.itemsDict.ContainsKey(k)))
                .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
            if (_jType == null) r.Fail($"no {_jKind} our journal hasn't got (of {keys.Count} in the database)");
            r.Info = $"{_jKind} {_jType}";
            yield break;
        }

        /// <summary>"journal-add": our player picks up the picked key or item (as KeyReference/QuestItemReference.pickup).</summary>
        private static IEnumerator JournalAdd(string[] a, Result r)
        {
            if (_jType == null) { r.Fail("journal-pick found nothing"); yield break; }
            var j = Singleton<UI>.Instance.journal;
            if (_jKind == "key") j.keysDict[_jType] = new Journal.Key { type = _jType };
            else j.itemsDict[_jType] = new Journal.Item { type = _jType };
            r.Info = $"{_jKind} {_jType} added";
            yield break;
        }

        /// <summary>"journal-remove": handed over in a dialogue (DialogueWindow's removeItem outcome takes it out of the journal).</summary>
        private static IEnumerator JournalRemove(string[] a, Result r)
        {
            if (_jType == null) { r.Fail("journal-pick found nothing"); yield break; }
            var j = Singleton<UI>.Instance.journal;
            bool had = _jKind == "key" ? j.keysDict.Remove(_jType) : j.itemsDict.Remove(_jType);
            if (!had) r.Fail($"our journal hadn't got {_jType}");
            r.Info = $"{_jKind} {_jType} removed";
            yield break;
        }

        /// <summary>"journal-has yes|no": whether our journal has the picked one (waiting up to 8 s for it to arrive).</summary>
        private static IEnumerator JournalHas(string[] a, Result r)
        {
            if (_jType == null) { r.Fail("journal-pick found nothing"); yield break; }
            var j = Singleton<UI>.Instance.journal;
            bool want = a.Length == 0 || a[0] == "yes";
            bool Has() => _jKind == "key" ? j.keysDict.ContainsKey(_jType) : j.itemsDict.ContainsKey(_jType);
            float until = Time.realtimeSinceStartup + 8f;
            while (Has() != want && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.5f);
            r.Info = $"{_jKind} {_jType}: {(Has() ? "here" : "not here")}";
            if (Has() != want) r.Fail(r.Info + $", wanted {(want ? "here" : "gone")}");
            // Stay a moment, so that a game that still had it doesn't send it back in its next snapshot.
            yield return new WaitForSecondsRealtime(FlagsSync.Interval + 0.5f);
            if (Has() != want) r.Fail($"{_jType} came {(want ? "off" : "back")} after {FlagsSync.Interval + 0.5f:0.#} s");
        }

        // ---- 62, 63, 38 the morning ---------------------------------------------------------------------------

        internal static Location Hideout(Player p)
        {
            p.whereAmI.checkWhereAmI();
            var here = p.whereAmI.bigLocation;
            if (here != null && here.playerBase) return here;
            var wg = Singleton<WorldGenerator>.Instance;
            return wg?.locations.Where(l => l != null && l.playerBase && !l.isSubLocation)
                .OrderBy(l => StateDump.Flat(l.transform.position - p.transform.position)).FirstOrDefault();
        }

        private static NPC MorningNpc(Location l)
        {
            if (l == null) return null;
            var go = l.trader != null ? l.trader : l.wolf;
            return go != null ? go.GetComponent<NPC>() : null;
        }

        /// <summary>"an-start [noreward]": our player in their hideout at dawn: the game's morning (Controller.startAfterNight,
        /// which dawn calls in each game): the trader or the wolf comes, the clock is frozen until we walk out.</summary>
        private static IEnumerator AnStart(string[] a, Result r)
        {
            var p = Player.Instance;
            var c = Singleton<Controller>.Instance;
            var l = Hideout(p);
            if (l == null) { r.Fail("no hideout"); yield break; }
            if (Location.getAtPos(p.transform.position) != l)
            {
                var to = l.traderPosition != null ? l.traderPosition.transform.position : l.transform.position;
                yield return Walk(to, 150f);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (Location.getAtPos(p.transform.position) != l) { r.Fail($"couldn't get into {l.name} (at {Fmt(p.transform.position)})"); yield break; }
            bool reward = !a.Contains("noreward");
            c.startAfterNight(reward);
            yield return new WaitForSecondsRealtime(1f);
            var npc = MorningNpc(l);
            r.Info = $"{l.name}: afterNight {c.isAfterNight}, frozen {p.effects.hasEffectType(CharacterEffectType.timeFreeze)}, " +
                     $"{(npc != null ? npc.name + " " + ObjRef.Of(npc.gameObject) : "no trader or wolf")}";
            if (!c.isAfterNight) r.Fail(r.Info);
        }

        /// <summary>"an-end": our player's morning ends, as walking out of the hideout does (Location.OnTriggerExit -> endAfterNight).</summary>
        private static IEnumerator AnEnd(string[] a, Result r)
        {
            var c = Singleton<Controller>.Instance;
            if (!c.isAfterNight) { r.Fail("not in a morning"); yield break; }
            AsPlayer(() => c.endAfterNight());
            yield return new WaitForSecondsRealtime(0.5f);
            var hl = Hideout(Player.Instance);
            r.Info = $"afterNight {c.isAfterNight}, trader {(hl != null && hl.trader != null ? "still here" : "gone")}";
            if (c.isAfterNight) r.Fail(r.Info);
        }

        /// <summary>"an-check on|off": whether our game is in its morning (waiting up to 6 s); off: the trader has gone too.</summary>
        private static IEnumerator AnCheck(string[] a, Result r)
        {
            var c = Singleton<Controller>.Instance;
            bool want = a.Length == 0 || a[0] == "on";
            float until = Time.realtimeSinceStartup + 6f;
            while (c.isAfterNight != want && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForSecondsRealtime(0.5f);
            var l = Hideout(Player.Instance);
            var npc = l != null && l.trader != null ? l.trader.GetComponent<NPC>() : null; // the wolf stays (each game's own)
            bool frozen = Player.Instance.effects.hasEffectType(CharacterEffectType.timeFreeze);
            r.Info = $"afterNight {c.isAfterNight}, frozen {frozen}, trader {(npc != null ? npc.name : "gone")}{(l != null && l.wolf != null ? ", the wolf here" : "")}";
            if (c.isAfterNight != want || (!want && (npc != null || frozen))) r.Fail(r.Info);
        }

        /// <summary>"an-clock running|stopped" (host): whether the world clock goes on during our morning (waiting up
        /// to 15 s for it, after the joiners' grace), checked over the next few game minutes.</summary>
        private static IEnumerator AnClock(string[] a, Result r)
        {
            var c = Singleton<Controller>.Instance;
            bool want = a.Length > 0 && a[0] == "running";
            if (!c.isAfterNight) { r.Fail("not in a morning"); yield break; }
            float until = Time.realtimeSinceStartup + 15f;
            while (c.DoUpdateTime != want && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.25f);
            int t0 = c.CurrentTime;
            float wait = Mathf.Max(3f, c.timeChangeInterval * 3f);
            yield return new WaitForSecondsRealtime(wait);
            int moved = ((c.CurrentTime - t0) % 1440 + 1440) % 1440;
            r.Info = $"DoUpdateTime {c.DoUpdateTime}, clock {t0} -> {c.CurrentTime} in {wait:0.#} s";
            if (c.DoUpdateTime != want || (want ? moved == 0 : moved != 0)) r.Fail(r.Info + $", wanted {(want ? "running" : "stopped")}");
        }

        private static NPC _talkingTo;

        /// <summary>"talk [--refused]": our player talks to the morning's trader (or wolf) in our hideout (NPC.talkTo).
        /// With --refused, someone else is talking to them: the host says so and our dialogue closes again.</summary>
        private static IEnumerator Talk(string[] a, Result r)
        {
            var p = Player.Instance;
            var npc = MorningNpc(Hideout(p));
            if (npc == null) { r.Fail("no trader or wolf in our hideout"); yield break; }
            if (!npc.wantsToTalk || npc.characterDialogue == null) { r.Fail($"{npc.name} doesn't want to talk (wantsToTalk {npc.wantsToTalk})"); yield break; }
            bool expectRefused = a.Contains("--refused");
            yield return Walk(npc.transform.position, 150f);
            int mark = MapScenarioIssues.LogMark();
            var win = Singleton<UI>.Instance.dialogueWindow;
            AsPlayer(() => npc.talkTo());
            bool host = Net.Session.Instance == null || Net.Session.Instance.IsHost;
            if (expectRefused)
            {
                float until = Time.realtimeSinceStartup + 12f;
                while (Time.realtimeSinceStartup < until && (win.npc == npc || !LogSince(mark).Any(l => !l.Contains("[map]") && l.Contains("is talking to them")))) yield return new WaitForSecondsRealtime(0.25f);
                bool told = LogSince(mark).Any(l => !l.Contains("[map]") && l.Contains("is talking to them"));
                r.Info = $"{npc.name} {ObjRef.Of(npc.gameObject)}: told {told}, dialogue {(win.npc == npc ? "still open" : "closed")}";
                if (!told || win.npc == npc) r.Fail(r.Info);
                if (win.npc == npc) yield return Close(win, npc);
                yield break;
            }
            yield return new WaitForSecondsRealtime(host ? 1.5f : 2f);
            var refused = LogSince(mark).Where(l => !l.Contains("[map]")).Where(l => l.Contains("is talking to them") || l.Contains("just talked to them") || (l.Contains("refused") && l.Contains("talk"))).ToList();
            r.Info = $"{npc.name} {ObjRef.Of(npc.gameObject)}: dialogue {(win.npc == npc ? "open" : "not open")}";
            if (refused.Count > 0) r.Fail($"refused: {refused[0].Trim()}");
            else if (win.npc != npc) r.Fail(r.Info);
            _talkingTo = npc;
        }

        internal static IEnumerator Close(DialogueWindow win, NPC npc)
        {
            for (int i = 0; i < 40 && win.npc == npc && npc != null; i++)
            {
                if (!win.tweening && Player.Instance.inDialogue)
                {
                    // Not from the main menu, which shows the goodbye line instead (see TalkLock.CloseWhenReady). This
                    // runs nested in the step, where an exception would end it without a reply: catch it.
                    win.currentMenu = DialogueWindow.CurrentMenu.gossip;
                    try { win.close(); } catch (Exception e) { Plugin.Log.LogWarning($"[map] closing the dialogue: {e.Message}"); }
                }
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }

        /// <summary>"trade-open": in our dialogue (after "talk"), open the trade window (DialogueWindow.openTrade).</summary>
        private static IEnumerator TradeOpen(string[] a, Result r)
        {
            var win = Singleton<UI>.Instance.dialogueWindow;
            var p = Player.Instance;
            if (win.npc == null || !win.npc.trader) { r.Fail($"not talking to a trader ({(win.npc != null ? win.npc.name : "no one")})"); yield break; }
            float until = Time.realtimeSinceStartup + 8f;
            while ((win.tweening || !p.inDialogue) && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.25f);
            try { win.openTrade(); } catch (Exception e) { r.Fail($"openTrade: {e.Message}"); yield break; }
            yield return new WaitForSecondsRealtime(1f);
            r.Info = $"{win.npc?.name}: inShop {p.inShop}";
            if (!p.inShop) r.Fail(r.Info);
        }

        /// <summary>"talk-free": our dialogue (and trade) closed by itself, and our player has the controls back (issue
        /// #15: the host walked out of the hideout while we traded, and the trader left our game with us stuck in it).</summary>
        private static IEnumerator TalkFree(string[] a, Result r)
        {
            var win = Singleton<UI>.Instance.dialogueWindow;
            var p = Player.Instance;
            bool Free() => !p.inDialogue && !p.inShop && !p.performingAction && win.npc == null && !win.gameObject.activeSelf;
            float until = Time.realtimeSinceStartup + 15f;
            while (!Free() && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForSecondsRealtime(1.5f); // the fade back and Core.forbidInputsFor
            r.Info = $"inDialogue {p.inDialogue}, inShop {p.inShop}, performingAction {p.performingAction}, window " +
                     $"{(win.gameObject.activeSelf ? "open" : "closed")}, timeScale {Time.timeScale:0.##}, afterNight {Singleton<Controller>.Instance.isAfterNight}";
            if (!Free() || Time.timeScale <= 0f) r.Fail(r.Info);
            _talkingTo = null;
        }

        /// <summary>"talk-end": our dialogue closes.</summary>
        private static IEnumerator TalkEnd(string[] a, Result r)
        {
            var win = Singleton<UI>.Instance.dialogueWindow;
            var npc = _talkingTo ?? win.npc;
            if (npc == null) { r.Fail("not talking to anyone"); yield break; }
            yield return Close(win, npc);
            _talkingTo = null;
            r.Info = $"dialogue {(win.npc == null ? "closed" : "still open")}";
            if (win.npc != null) r.Fail(r.Info);
        }

        // ---- 64 rotting food in an open container ------------------------------------------------------------

        /// <summary>"rot-open" (client): open the setup's wardrobe, let its food rot by the game's own refresh
        /// (Inventory.refreshItems) while it's open, then take the food. Neither the rot nor the take may be sent as
        /// a change the host refuses.</summary>
        private static IEnumerator RotOpen(string[] a, Result r)
        {
            var p = Player.Instance;
            var inv = RotWardrobe();
            if (inv == null) { r.Fail("no wardrobe at the rot spot"); yield break; }
            yield return Walk(inv.transform.position);
            p.openedItemInventory = inv; // as if its window were open
            yield return new WaitForSecondsRealtime(SlotShare.AskTimeout + 0.5f);
            var slot = inv.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem) && s.invItem.baseClass != null && s.invItem.baseClass.rottenItem != null);
            if (slot == null) { r.Fail($"no rotting food in {inv.name} {ObjRef.Of(inv.gameObject)}"); p.openedItemInventory = null; yield break; }
            var it = slot.invItem;
            string food = it.type; // (the slot's item is cleared when it moves)
            float max = Mathf.Max(1f, it.baseClass.maxDurability), d0 = it.durability;
            if (!it.activated) it.switchActive(true); // a seen perishable in a container rots (checkForActiveSwitches)
            int mark = MapScenarioIssues.LogMark();
            int calls = 0;
            for (int frame = 0; frame < 30 && !InvItemClass.isNull(slot.invItem) && d0 - slot.invItem.durability < 0.02f * max; frame++)
            {
                // An experience item drains only while the clock runs (refreshItems), which a joiner's never does by
                // itself: let it run for these refreshes, as the host's does (TimeSync turns it off again).
                var ctl = Singleton<Controller>.Instance;
                bool was = ctl.DoUpdateTime;
                if (it.baseClass.isExpItem) ctl.DoUpdateTime = true;
                try { for (int i = 0; i < 1000 && slot.invItem == it && d0 - it.durability < 0.02f * max; i++) { inv.refreshItems(); calls++; } }
                finally { ctl.DoUpdateTime = was; }
                yield return null;
            }
            float d1 = InvItemClass.isNull(slot.invItem) ? -1f : slot.invItem.durability;
            yield return new WaitForSecondsRealtime(1.5f); // a few of Containers' looks at the open slots
            var sent = LogSince(mark).Where(l => l.Contains($"slot(s) of {inv.name}") && l.Contains("sent") && !l.Contains("[map]")).ToList();
            var refused = LogSince(mark).Where(l => l.Contains("refused") && !l.Contains("[map]")).ToList();
            r.Info = $"{it.type} durability {d0:0.###} -> {d1:0.###} of {max:0.##} in {calls} refreshes";
            if (d1 < 0f || d1 >= d0) { r.Fail($"it didn't rot: {r.Info}"); p.openedItemInventory = null; yield break; }
            if (sent.Count > 0) r.Fail($"the rot was sent as our change: {sent[0].Trim()}; {r.Info}");
            if (refused.Count > 0) r.Fail($"refused: {refused[0].Trim()}; {r.Info}");
            int carried0 = p.Inventory.slots.Concat(p.Hotbar.slots).Count(x => x != null && !InvItemClass.isNull(x.invItem) && x.invItem.type == food);
            mark = MapScenarioIssues.LogMark();
            AsPlayer(() => slot.transferItemAllToPlayer());
            yield return new WaitForSecondsRealtime(2f);
            p.openedItemInventory = null;
            yield return new WaitForSecondsRealtime(0.5f);
            refused = LogSince(mark).Where(l => l.Contains("refused") && !l.Contains("[map]")).ToList();
            int carried1 = p.Inventory.slots.Concat(p.Hotbar.slots).Count(x => x != null && !InvItemClass.isNull(x.invItem) && x.invItem.type == food);
            r.Info += $"; carried {carried0} -> {carried1}";
            if (refused.Count > 0) r.Fail($"the take was refused: {refused[0].Trim()}; {r.Info}");
            else if (carried1 <= carried0 || !InvItemClass.isNull(slot.invItem)) r.Fail($"the food didn't come into our bags: {r.Info}");
        }

        /// <summary>"rot-host" (host): the client's take came through (no food left in the wardrobe) and we refused nothing of it.</summary>
        private static IEnumerator RotHost(string[] a, Result r)
        {
            var inv = RotWardrobe();
            if (inv == null) { r.Fail("no wardrobe at the rot spot"); yield break; }
            var left = inv.slots.Where(s => !InvItemClass.isNull(s.invItem)).Select(s => $"{s.invItem.type}x{s.invItem.amount}").ToList();
            var refused = MapScenarioIssues.LogLines().Where(l => l.Contains($"of {inv.name}: refused")).ToList();
            r.Info = $"{inv.name} {ObjRef.Of(inv.gameObject)}: [{string.Join(",", left)}]";
            if (refused.Count > 0) r.Fail($"refused: {refused[0].Trim()}");
            else if (left.Count > 0) r.Fail($"the food is still here: {r.Info}");
        }

        // ---- 65 vines -----------------------------------------------------------------------------------------

        private static ChainParent Vine() => NearestAll<ChainParent>(Spot("vine"), cp => StateDump.Flat(cp.transform.position - Spot("vine")) < 300f);

        /// <summary>Where the game keeps its vines (Items/vine is only a segment): the vine attack's pieces, which a
        /// VineSpawner or a vine trap (Trigger.prefabToSpawn) makes.</summary>
        private static readonly string[] VinePrefabs =
        {
            "Traps/vineAttack_vines", "Traps/vineAttack_100", "Traps/vineAttack_140", "Traps/vineAttack_200",
            "Traps/vineAttack_400", "Traps/vineAttack_background", "Items/vine",
        };

        /// <summary>The first prefab with a ChainParent: one of VinePrefabs, or what one of them spawns.</summary>
        private static GameObject VinePrefab(out string from)
        {
            bool Has(Object o) => o is GameObject g && g.GetComponentInChildren<ChainParent>(true) != null;
            foreach (var path in VinePrefabs)
            {
                var pf = Resources.Load("Prefabs/" + path) as GameObject;
                if (pf == null) continue;
                if (Has(pf)) { from = path; return pf; }
                foreach (var t in pf.GetComponentsInChildren<Trigger>(true))
                    foreach (var o in new[] { t.prefabToSpawn, t.nonPooledPrefabToSpawn })
                        if (Has(o)) { from = $"{path} (its trap's {o.name})"; return (GameObject)o; }
                foreach (var vs in pf.GetComponentsInChildren<VineSpawner>(true))
                    foreach (var o in vs.vines)
                        if (Has(o)) { from = $"{path} (its spawner's {o.name})"; return o; }
            }
            from = null;
            return null;
        }

        /// <summary>"vine-make" (both games): a vine at the vine spot, as a VineSpawner makes one in each game after the
        /// join (not shared, found by where it stands). It isn't fastened to anything, so it's taken as attached and
        /// staying so (no detach timer).</summary>
        private static IEnumerator VineMake(string[] a, Result r)
        {
            var pf = VinePrefab(out string from);
            if (pf == null) { r.Fail($"no prefab with a ChainParent among {string.Join(", ", VinePrefabs)}"); yield break; }
            var go = Object.Instantiate(pf, Spot("vine"), Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer != null ? Core.ItemContainer.transform : null);
            go.name = pf.name;
            var cp = go.GetComponentInChildren<ChainParent>(true);
            yield return new WaitForSecondsRealtime(0.5f); // init runs a frame later
            cp.timeToDetach = 0f;
            cp.attached = true;
            if (cp.health <= 0f) cp.health = cp.maxHealth;
            r.Info = $"{from}: {ObjRef.Of(cp.gameObject)} at {Fmt(cp.transform.position)}: health {cp.health:0} of {cp.maxHealth:0}, segment {(cp.lastChainSegment != null ? cp.lastChainSegment.name : "none")}";
            if (cp.maxHealth <= 0f) r.Fail(r.Info + ": no health to cut");
        }

        /// <summary>"vine-cut half|all": our player hits the vine (MeleeSensor -> ChainParent.getHit) for half its
        /// maximum health, or for what's left.</summary>
        private static IEnumerator VineCut(string[] a, Result r)
        {
            var p = Player.Instance;
            var cp = Vine();
            if (cp == null || !cp.attached) { r.Fail($"no attached vine ({cp != null})"); yield break; }
            yield return Walk(cp.transform.position, 150f);
            float dmg = a.Length > 0 && a[0] == "all" ? cp.health : Mathf.Floor(cp.maxHealth / 2f);
            var seg = cp.lastChainSegment != null ? cp.lastChainSegment.GetComponent<Vine>() : null;
            if (seg == null) seg = cp.GetComponentInChildren<Vine>();
            if (seg == null) { r.Fail("the vine has no segment to hit"); yield break; }
            int mark = MapScenarioIssues.LogMark();
            AsPlayer(() => cp.getHit(dmg, p.transform, seg));
            yield return new WaitForSecondsRealtime(1.5f);
            var refused = LogSince(mark).Where(l => l.Contains("refused") && !l.Contains("[map]")).ToList();
            r.Info = $"hit {dmg:0}: health {cp.health:0}, attached {cp.attached}";
            if (refused.Count > 0) r.Fail($"refused: {refused[0].Trim()}");
        }

        /// <summary>"vine-check attached|cut [HEALTH|half]": the vine here (waiting up to 5 s).</summary>
        private static IEnumerator VineCheck(string[] a, Result r)
        {
            bool attached = a.Length == 0 || a[0] == "attached";
            var cp = Vine();
            if (cp == null) { r.Fail("no vine"); yield break; }
            // "half": what one vine-cut half left
            float health = a.Length < 2 ? -1f : a[1] == "half" ? cp.maxHealth - Mathf.Floor(cp.maxHealth / 2f) : float.Parse(a[1], CultureInfo.InvariantCulture);
            bool Ok() => cp.attached == attached && (health < 0f || Mathf.Abs(cp.health - health) < 0.5f);
            float until = Time.realtimeSinceStartup + 5f;
            while (!Ok() && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.25f);
            r.Info = $"{ObjRef.Of(cp.gameObject)}: attached {cp.attached}, health {cp.health:0} of {cp.maxHealth:0}";
            if (!Ok()) r.Fail(r.Info);
        }

        // ---- 66 shadow-covered items --------------------------------------------------------------------------

        /// <summary>"shadow-ready" (both games): the crate's Item points at its shadow. The game finds it in Item.Awake,
        /// before a save's ShadowArmor is added to a prefab that has none, which ours (made for the test) hasn't.</summary>
        private static IEnumerator ShadowReady(string[] a, Result r)
        {
            var crate = ShadowCrate();
            if (crate == null) { r.Fail("no crate at the shadow spot"); yield break; }
            var sa = crate.GetComponent<ShadowArmor>();
            if (sa == null) { r.Fail($"{crate.name} {ObjRef.Of(crate.gameObject)} has no shadow"); yield break; }
            crate.shadowArmor = sa;
            r.Info = $"{crate.name} {ObjRef.Of(crate.gameObject)}: shadow {BreakSync.ShadowHealth(crate):0.#} of {sa.maxHealth:0}, item health {crate.health}";
        }

        /// <summary>"shadow-hit DAMAGE": our player hits the crate (Item.getHit), which only the shadow takes.</summary>
        private static IEnumerator ShadowHit(string[] a, Result r)
        {
            var p = Player.Instance;
            var crate = ShadowCrate();
            if (crate == null || crate.shadowArmor == null) { r.Fail("no shadow-covered crate"); yield break; }
            int dmg = a.Length > 0 ? int.Parse(a[0]) : 10;
            yield return Walk(crate.transform.position, 150f);
            int hp = crate.health;
            int mark = MapScenarioIssues.LogMark();
            AsPlayer(() => crate.getHit(dmg, p.transform));
            yield return new WaitForSecondsRealtime(1.5f);
            var refused = LogSince(mark).Where(l => l.Contains("refused") && !l.Contains("[map]")).ToList();
            r.Info = $"shadow {BreakSync.ShadowHealth(crate):0.#}, item health {hp} -> {crate.health}";
            if (refused.Count > 0) r.Fail($"refused: {refused[0].Trim()}");
            else if (crate.health != hp) r.Fail($"the item took the hit, not the shadow: {r.Info}");
        }

        /// <summary>"shadow-check alive MAXHEALTH|dead": the crate's shadow here (waiting up to 15 s): alive and going down
        /// to at most MAXHEALTH, or gone; the crate itself unhurt.</summary>
        private static IEnumerator ShadowCheck(string[] a, Result r)
        {
            var crate = ShadowCrate();
            if (crate == null) { r.Fail("no crate at the shadow spot"); yield break; }
            bool alive = a.Length == 0 || a[0] == "alive";
            float at = a.Length > 1 ? float.Parse(a[1], CultureInfo.InvariantCulture) : 1e9f;
            bool Ok()
            {
                var sa = crate.GetComponent<ShadowArmor>();
                return alive ? sa != null && BreakSync.ShadowHealth(crate) <= at + 0.01f : sa == null;
            }
            float until = Time.realtimeSinceStartup + 15f;
            while (!Ok() && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.25f);
            var s = crate.GetComponent<ShadowArmor>();
            r.Info = $"shadow {(s != null ? $"alive, going to {BreakSync.ShadowHealth(crate):0.#}" : "gone")}, item health {crate.health}, destroyed {crate.destroyed}";
            if (!Ok() || crate.destroyed) r.Fail(r.Info);
        }

        // ---- 68 a death bag's map marker ----------------------------------------------------------------------

        private static GameObject _marker;

        /// <summary>"bag-drop": our player leaves a death bag as if they died in a separate location (Player.dropBody
        /// with OutsideLocations.playerInOutsideLocation and the thing that took them there): the game puts the bag
        /// and a map marker at that thing.</summary>
        private static IEnumerator BagDrop(string[] a, Result r)
        {
            var p = Player.Instance;
            var bags = p.Inventory.slots.Where(s => InvItemClass.isNull(s.invItem)).Take(4).ToList();
            string[] kit = { "bandages", "wire", "rope", "nail" };
            for (int i = 0; i < bags.Count; i++) { bags[i].inventory = p.Inventory; bags[i].createItem(kit[i], 1); }
            var o = Singleton<OutsideLocations>.Instance;
            var transport = new GameObject("dwmp_test_transport");
            transport.transform.position = Spot("transport");
            bool wasIn = o.playerInOutsideLocation; var wasObj = o.objectThatTransportedMe;
            o.playerInOutsideLocation = true; o.objectThatTransportedMe = transport;
            try { p.dropBody(p.transform.position + new Vector3(80f, 0f, 0f)); }
            finally { o.playerInOutsideLocation = wasIn; o.objectThatTransportedMe = wasObj; }
            yield return new WaitForSecondsRealtime(3f); // a bag is sent once it has come to rest
            var bag = NearestAll<DeathDrop>(p.transform.position, d => d.GetComponent<Inventory>() != null);
            _marker = bag != null ? bag.additionalMapMarker : null;
            r.Info = bag == null ? "no bag" : $"bag {ObjRef.Of(bag.gameObject)} at {Fmt(bag.transform.position)}, marker {(_marker != null ? Fmt(_marker.transform.position) : "none")}";
            if (bag == null || _marker == null) r.Fail(r.Info);
            Object.Destroy(transport);
        }

        /// <summary>"bag-check here|gone": the death bag here, with its marker at the transport spot (waiting up to
        /// 8 s), or the bag and its marker gone.</summary>
        private static IEnumerator BagCheck(string[] a, Result r)
        {
            bool here = a.Length == 0 || a[0] == "here";
            DeathDrop Bag() => NearestAll<DeathDrop>(Spot("transport"), d => d.GetComponent<Inventory>() != null && StateDump.Flat(d.transform.position - Centre) < 2500f);
            bool Ok()
            {
                var b = Bag();
                if (!here) return b == null && _marker == null;
                return b != null && b.additionalMapMarker != null && StateDump.Flat(b.additionalMapMarker.transform.position - Spot("transport")) < 5f;
            }
            float until = Time.realtimeSinceStartup + 8f;
            while (!Ok() && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.25f);
            var bag = Bag();
            if (here && bag != null) _marker = bag.additionalMapMarker;
            r.Info = bag == null ? $"no bag, marker {(_marker != null ? "still here" : "gone")}"
                : $"bag {ObjRef.Of(bag.gameObject)} at {Fmt(bag.transform.position)}, marker {(bag.additionalMapMarker != null ? Fmt(bag.additionalMapMarker.transform.position) : "none")}";
            if (!Ok()) r.Fail(r.Info);
        }

        /// <summary>"bag-take": our player empties the death bag (it goes when emptied, and its marker with it).</summary>
        private static IEnumerator BagTake(string[] a, Result r)
        {
            var p = Player.Instance;
            var bag = NearestAll<DeathDrop>(Spot("transport"), d => d.GetComponent<Inventory>() != null && StateDump.Flat(d.transform.position - Centre) < 2500f);
            if (bag == null) { r.Fail("no bag"); yield break; }
            var inv = bag.GetComponent<Inventory>();
            yield return Walk(bag.transform.position);
            _marker = bag.additionalMapMarker;
            int mark = MapScenarioIssues.LogMark();
            if (inv != null)
            {
                p.openedItemInventory = inv;
                yield return new WaitForSecondsRealtime(0.6f);
                AsPlayer(() => { foreach (var s in inv.slots.ToList()) if (!InvItemClass.isNull(s.invItem)) s.transferItemAllToPlayer(); });
                yield return new WaitForSecondsRealtime(0.6f);
                try { if (inv != null) AsPlayer(() => inv.hide()); } catch (Exception e) { Plugin.Log.LogWarning($"[map] bag-take: hide: {e.Message}"); }
                p.openedItemInventory = null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            if (bag != null) AsPlayer(() => bag.GetComponent<Item>()?.activate()); // a bag without items: its experience taken
            yield return new WaitForSecondsRealtime(1.5f);
            var refused = LogSince(mark).Where(l => l.Contains("refused") && !l.Contains("[map]")).ToList();
            r.Info = $"bag {(bag != null ? "still here" : "gone")}, marker {(_marker != null ? "still here" : "gone")}";
            if (refused.Count > 0) r.Fail($"refused: {refused[0].Trim()}");
            else if (bag != null || _marker != null) r.Fail(r.Info);
        }
    }
}
