using DarkwoodMP.World.Inventories;
using DarkwoodMP.World.Rolls;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.World.Scripted;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The world test's steps with things in the world (WorldTest).
    /// </summary>
    public static partial class WorldTest
    {
        /// <summary>
        /// Dragging, a thrown molotov, a burning door, another player's
        /// look, objects named by place and late save ids.
        /// </summary>
        private static IEnumerator ThingsStep(Player player, Vector3 at)
        {
            // Dragging something: the player takes hold of it
            // (itemBeingDragged, as Item.startDragging sets) and it moves.
            var drag = Nearest(
                Object.FindObjectsOfType<Item>().Where(
                    i => i.draggable && !i.isLight && !i.hasInventory
                        && i.GetComponent<Rigidbody>() != null),
                at);
            if (drag != null)
            {
                var start = drag.transform.position;
                // First as if a client had taken hold of it already (the host
                // agreed to its DragClaim): our player's drag is let go of at
                // once, and the game's own Item.startDragging doesn't start
                // one.
                DragSync ds = null;
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    ds = coop.Drags;
                }

                if (ds != null)
                {
                    int cancelled = ds.Cancelled;
                    ds.TestOwner(drag.gameObject, 999);
                    int blocked = ds.Blocked;
                    drag.startDragging();
                    bool stopped = ds.Blocked > blocked
                        && !drag.beingDragged
                        && player.itemBeingDragged != drag;
                    // as a script takes hold of it
                    player.itemBeingDragged = drag;
                    yield return null;
                    yield return null;
                    bool letGo = player.itemBeingDragged != drag
                        && ds.Cancelled > cancelled;
                    string text = "started";
                    if (stopped)
                    {
                        text = "not started";
                    }

                    string ourDrag = "kept";
                    if (letGo)
                    {
                        ourDrag = "let go";
                    }

                    string dragVerdict = "FAIL";
                    if (stopped && letGo)
                    {
                        dragVerdict = "PASS";
                    }

                    Plugin.Log.LogInfo(
                        $"[world test] {drag.name} dragged by another player: "
                            + $"the game's drag {text}, ours {ourDrag}: "
                            + $"{dragVerdict}");
                    if (player.itemBeingDragged == drag)
                    {
                        player.itemBeingDragged = null;
                    }

                    ds.TestOwner(drag.gameObject, 0);
                    yield return new WaitForSeconds(0.5f);
                    start = drag.transform.position;
                }

                Plugin.Log.LogInfo(
                    $"[world test] dragging {drag.name} from {start}");
                player.itemBeingDragged = drag;
                for (float t = 0f; t < 2f; t += Time.deltaTime)
                {
                    drag.transform.position =
                        start + new Vector3(30f * t, 0f, 0f);
                    yield return null;
                }

                player.itemBeingDragged = null;
                Plugin.Log.LogInfo(
                    $"[world test] let go of {drag.name} at "
                        + $"{drag.transform.position}");
                yield return new WaitForSeconds(2.5f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] nothing draggable found");
            }

            // A molotov thrown a little way off: it flies, lands and burns, in
            // every game.
            var db = Singleton<ItemsDatabase>.Instance;
            string molotov = null;
            if (db != null)
            {
                molotov = db.itemsDict.Keys.FirstOrDefault(
                    k => k.ToLowerInvariant().Contains("molotov"));
            }

            InvItem mi = null;
            if (molotov != null)
            {
                mi = db.getItem(molotov, instantiate: false);
            }

            if (mi != null && mi.item != null)
            {
                var from = player.transform.position;
                var go = Core.AddPrefab(
                    mi.item, from, Quaternion.Euler(90f, 0f, 0f), null);
                var ti = go.GetComponent<ThrownItem>();
                var rb = go.GetComponent<Rigidbody>();
                if (ti != null && rb != null)
                {
                    var dir = new Vector3(0f, 0f, -1f);
                    ti.landTarget = from + dir * 300f;
                    rb.drag = 2f;
                    rb.velocity = dir * 300f * 2.5f;
                    ti.thrown = true;
                    ti.objectThatSpawnedMe = player.transform;
                    ti.setFallSpeed(300f);
                    var r = go.GetComponent<Renderer>();
                    if (r != null)
                    {
                        r.enabled = true;
                    }

                    Plugin.Log.LogInfo(
                        $"[world test] throwing {molotov} south");
                    CoopSession coop = Coop.Current;
                    if (coop != null)
                    {
                        coop.Throws.Thrown(molotov, go, from);
                    }

                    yield return new WaitForSeconds(5f);
                }
                else
                {
                    Plugin.Log.LogInfo(
                        $"[world test] {molotov} isn't a thrown item");
                }
            }
            else
            {
                Plugin.Log.LogInfo(
                    "[world test] no molotov in the item database");
            }

            // A door on fire: the host's world burns it, the others are told.
            var burnDoor = Nearest(
                Object.FindObjectsOfType<Door>().Where(
                    d => !d.destroyed && d.GetComponent<Burn>() == null),
                at);
            if (burnDoor != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] setting {burnDoor.name} on fire (health "
                        + $"{burnDoor.health})");
                burnDoor.gameObject.AddComponent<Burn>().burnTime = 4f;
                yield return new WaitForSeconds(6f);
                Plugin.Log.LogInfo(
                    $"[world test] {burnDoor.name} after burning: health "
                        + $"{burnDoor.health}");
            }

            // Another player's look: sprites from an animation library this
            // game hasn't loaded (a weapon or torch our player never held) are
            // still found, by loading that library.
            var loaded = new HashSet<string>(
                Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>().Select(
                    l => l.name));
            foreach (var libName in new[]
            {
                "PlayerTorchAnims",
                "PlayerFlashlightAnims",
                "PlayerPitchforkAnims",
                "PlayerShotgunAnims"
            })
            {
                if (loaded.Contains(libName))
                {
                    Plugin.Log.LogInfo(
                        $"[world test] look: {libName} already loaded here");
                    continue;
                }

                // A sprite from a collection not loaded here, from a player
                // whose body uses libName: that loads it.
                Plugin.Instance.Host.Sprites.Get(
                    Net.StableHash.Of("not loaded here"), libName);
                var lib =
                    Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>()
                        .FirstOrDefault(l => l.name == libName);
                List<string> colls = null;
                if (lib != null)
                {
                    tk2dSpriteAnimationClip[] clips = lib.clips;
                    if (clips != null)
                    {
                        colls = clips.SelectMany(c =>
                        {
                            tk2dSpriteAnimationFrame[] frames = c.frames;
                            if (frames != null)
                            {
                                return frames;
                            }

                            return new tk2dSpriteAnimationFrame[0];
                        }).Select(f =>
                        {
                            tk2dSpriteCollectionData collection =
                                f.spriteCollection;
                            if (collection != null)
                            {
                                return collection.spriteCollectionName;
                            }

                            return null;
                        }).Where(n => n != null).Distinct().ToList();
                    }
                }

                if (colls == null)
                {
                    colls = new List<string>();
                }

                int inLib = colls.Count(
                    n => Plugin.Instance.Host.Sprites.Get(
                        Net.StableHash.Of(n)) != null);
                string libState = "FAIL: not loaded";
                if (lib != null)
                {
                    libState = "loaded";
                }

                Plugin.Log.LogInfo(
                    $"[world test] look: {libName} {libState}; {inLib} of "
                        + $"{colls.Count} of its sprite collections found");
            }

            // Objects named by where they stand (no save id) are still found
            // when they lie a little off that spot, as after physics settled
            // differently in another game: nudge some, look each up by its old
            // name.
            var draggable = Object.FindObjectsOfType<Item>()
                .Where(i => i.draggable).ToList();
            int bySave = draggable.Count(
                i => ObjRef.Of(i.gameObject).K == ObjRef.Kind.Saved);
            int byPlace = draggable.Count(
                i => ObjRef.Of(i.gameObject).K == ObjRef.Kind.Placed);
            string examples = string.Join(
                ", ", draggable.Take(6).Select(i => i.name));
            Plugin.Log.LogInfo(
                $"[world test] draggable things: {draggable.Count}, named by "
                    + "save id "
                    + $"{bySave}, by place {byPlace}; e.g. {examples}");
            // Save ids this game gave out after the joiner's snapshot aren't
            // the joiner's: never named by save id, and a Saved ref to one
            // names nothing here.
            var saveables = Object.FindObjectsOfType<SaveableObject>();
            var late = saveables.Where(o => o.uniqueId
                    >= SharedIds.SendLimit).ToList();
            int lateSaved =
                late.Count(o => ObjRef.Of(o.gameObject).K == ObjRef.Kind.Saved);
            int sharedSaved = saveables.Count(
                o => o.uniqueId > 0 && o.uniqueId < SharedIds.SendLimit
                    && ObjRef.Of(o.gameObject).K == ObjRef.Kind.Saved);
            SaveableObject lateHit = null;
            if (late.Count > 0)
            {
                lateHit = ObjRef.Resolve<SaveableObject>(new ObjRef
                {
                    K = ObjRef.Kind.Saved,
                    Id = late[0].uniqueId
                });
            }

            bool lateOk = lateSaved == 0
                && (late.Count == 0
                    || late[0].uniqueId < SharedIds.ResolveLimit
                    || lateHit == null);
            int sendLimit = SharedIds.SendLimit;
            int resolveLimit = SharedIds.ResolveLimit;
            string verdict = "FAIL";
            if (lateOk)
            {
                verdict = "ok";
            }

            Plugin.Log.LogInfo(
                $"[world test] save ids: shared below {sendLimit} (resolved "
                    + $"below {resolveLimit}); {sharedSaved} named by save id, "
                    + $"{late.Count} given out later, {lateSaved} of those "
                    + $"named by save id: {verdict}");
            var placed = Object.FindObjectsOfType<Item>()
                .Where(i => ObjRef.Of(i.gameObject).K == ObjRef.Kind.Placed)
                .Take(20).ToList();
            int found = 0;
            foreach (var pi in placed)
            {
                var name = ObjRef.Of(pi.gameObject);
                var was = pi.transform.position;
                pi.transform.position = was + new Vector3(12f, 0f, -9f);
                if (ObjRef.Resolve<Item>(name) == pi)
                {
                    found++;
                }

                pi.transform.position = was;
            }

            Plugin.Log.LogInfo(
                $"[world test] placed objects nudged 15 units: {found} of "
                    + $"{placed.Count} found by their old names");
        }

        /// <summary>The game's own stack size of an item type.</summary>
        private static int GameSize(string type)
        {
            return Plugin.Instance.Host.Stacks.OriginalOf(type);
        }
    }
}
