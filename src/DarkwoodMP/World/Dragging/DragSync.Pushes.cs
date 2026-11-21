using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Dragging;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// DragSync's pushed things: loose objects watched until they're at
    /// rest, and where each is sent or put back.
    internal sealed partial class DragSync
    {
        /// <summary>
        /// Every loose object this game knows of is checked against where it's
        /// known to be in every game. One that's away from there by more than a
        /// little, once it's at rest again, is settled: the host sends where it
        /// is (whatever moved it, even in a moment between two checks); a
        /// client sends it only if its own player was at it, and otherwise puts
        /// its copy back, for the host's word on it (which comes as an ObjMove
        /// if it moved there too). So the host's resting position is the last
        /// word on everything.
        /// </summary>
        private void WatchPushes(Player player)
        {
            if (player == null)
            {
                return;
            }

            var me = player.transform.position;
            if (!_looseFound)
            {
                _looseFound = true;
                // The host notes the switched-off ones too (where the save put
                // them): a part of the world switched on later around a
                // client's player settles there (a chair, a table shifting a
                // few units), and that is then sent to everyone, so a client
                // whose copy is still switched off has them settled too. Noted
                // only once seen, they would have settled already and
                // nothing would be sent, leaving that client's copies a few
                // units from everyone else's.
                foreach (var b in Object.FindObjectsOfType<Rigidbody>(
                    _session.IsHost))
                {
                    Note(b, null);
                }
            }

            if (OurDragRules.Due(Now, new Seconds(_nextLook)))
            {
                _nextLook = Time.unscaledTime + PushRules.LookEvery;
                Look(me);
                if (_session.IsHost)
                {
                    foreach (var proxy in Combat.NetPlayerProxy.All.Values)
                    {
                        if (proxy != null && proxy.alive && !proxy.Away)
                        {
                            Look(proxy.transform.position);
                        }
                    }
                }
            }

            // A client away (spectating) has no player of its own to push
            // anything: it takes the host's word on all. Our pushes count
            // wherever the host runs our area too (a separate location it
            // placed, the shared dream): only in an area that's ours alone does
            // what our player knocks over stay ours.
            bool hostRunsHere = Places.Whereabouts.HostRunsHere();
            List<Rigidbody> gone = null;
            foreach (var kv in _loose)
            {
                var p = kv.Value;
                var step = PushRules.Watch(FactsOf(p, me, hostRunsHere));
                var outcome = step.Outcome;
                if (outcome == PushOutcome.Drop)
                {
                    if (gone == null)
                    {
                        gone = new List<Rigidbody>();
                    }

                    gone.Add(kv.Key);
                    continue;
                }

                if (outcome == PushOutcome.Rebase)
                {
                    // where the drag or the other game puts it, which is sent
                    // or heard as that
                    Rebase(p);
                    continue;
                }

                if (outcome == PushOutcome.Still)
                {
                    p.Since = -1f;
                    p.Touched = false;
                    continue;
                }

                var t = p.Item.transform;
                float moved = (t.position - p.Pos).magnitude;
                if (outcome == PushOutcome.Slide
                    || outcome == PushOutcome.Wait)
                {
                    p.Since = step.Since.Value;
                    p.Touched = step.Touched;
                }

                if (outcome == PushOutcome.Slide)
                {
                    // Still sliding: where it is now goes out too (unreliably,
                    // as a drag's moves), so the other games show it sliding
                    // there instead of jumping to where it comes to rest. Only
                    // what this game has the word on: the host's, or a client's
                    // own player's push.
                    Send(p.Item, p.Ref, last: false);
                    continue;
                }

                if (outcome == PushOutcome.Wait)
                {
                    continue;
                }

                if (outcome == PushOutcome.PutBack)
                {
                    // Not our player's doing (settling, a nudge, our copy of an
                    // explosion): the host decides where it is, so our copy
                    // goes back to where it was last known to be.
                    p.Touched = false;
                    t.SetPositionAndRotation(p.Pos, p.Rot);
                    p.Body.position = p.Pos;
                    p.Body.rotation = p.Rot;
                    p.Body.velocity = Vector3.zero;
                    p.Body.angularVelocity = Vector3.zero;
                    p.Body.Sleep();
                    p.Since = -1f;
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] {p.Item.name} ({p.Ref}) moved "
                                + $"{moved:0.0} units by itself: put back");
                    }

                    continue;
                }

                Send(p.Item, p.Ref, last: true); // which notes it there
                PushesSent++;
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] pushed {p.Item.name} ({p.Ref}) {moved:0.0} "
                            + "units: sent where it is");
                }
            }

            if (gone != null)
            {
                foreach (var b in gone)
                {
                    _loose.Remove(b);
                }
            }
        }

        /// <summary>
        /// Our player hit this thing (Item.getHit). A shot pushes it from as
        /// far as the gun reaches, where the player isn't within
        /// PushRules.Touch of it, and a client would put it back as not its
        /// player's doing (and the host's copy, which gets the hit but not its
        /// push, doesn't move). So for a few seconds a move of it counts as our
        /// player's push.
        /// </summary>
        public void Hit(Item item)
        {
            Rigidbody b = null;
            if (item != null)
            {
                b = item.GetComponent<Rigidbody>();
            }

            if (b == null)
            {
                return;
            }

            if (!_loose.ContainsKey(b))
            {
                Note(b, null);
            }

            if (_loose.TryGetValue(b, out var p))
            {
                p.HitAt = Time.unscaledTime;
            }
        }

        /// <summary>
        /// Host: notes every loose object under root at once (a separate
        /// location it starts running for a joiner). Objects are otherwise
        /// noted only within PushRules.WatchRadius of a player, and one an
        /// enemy pushes far from everyone (a dog chasing about a location
        /// while its joiner is elsewhere in it) would move unseen and stay
        /// where the host's enemy left it in the host's game only.
        /// </summary>
        public void NoteAll(GameObject root)
        {
            if (root == null || !_session.IsHost)
            {
                return;
            }

            foreach (var b in root.GetComponentsInChildren<Rigidbody>(true))
            {
                Note(b, null);
            }
        }

        /// <summary>
        /// Host: an object the world made has just been given an id (HostIds)
        /// and announced where it stands now. It is noted there at once, so if
        /// it settles or is pushed later (a location loaded for a joiner, whose
        /// furniture only settles once it's switched on), the place it comes to
        /// rest is sent as a drag's end, like any push.
        /// </summary>
        public void Watch(GameObject go)
        {
            if (go == null || !_session.IsHost)
            {
                return;
            }

            Note(go.GetComponent<Rigidbody>(), null);
        }

        /// <summary>
        /// Client: our copy of an object the host gave an id to has just been
        /// tied to it. A loose one goes to where the host has it (the host
        /// decides where things are): each game's physics settles a fresh copy
        /// on its own, so the two can come to rest a few units apart, and
        /// nothing else would move ours. Left alone while it's being dragged,
        /// held still for another game's move, or kinematic (carried, fixed in
        /// place). True if it was moved.
        /// </summary>
        public bool PutAtHost(GameObject go, Vector3 pos)
        {
            if (go == null || !_session.IsClient)
            {
                return false;
            }

            var b = go.GetComponent<Rigidbody>();
            var item = LooseItem(b);
            var t = go.transform;
            float d = (t.position - pos).magnitude;
            var place = new PlaceFacts
            {
                Client = true,
                Loose = item != null,
            };

            if (item != null)
            {
                place = place with
                {
                    Kinematic = b.isKinematic,
                    Dragged = item == _dragging,
                    Settling = item == _settling,
                    Shown = _moving.ContainsKey(go),
                    Distance = d,
                };
            }

            var r = ObjRef.Of(go);
            place = place with { Other = DragRules.OtherOwner(FactsOf(r)) };
            if (!PushRules.PutsAtHost(place))
            {
                return false;
            }

            t.position = pos;
            b.position = pos;
            b.velocity = Vector3.zero;
            b.angularVelocity = Vector3.zero;
            b.Sleep(); // where the host has it: not to drift off by itself here
            Rebase(go);
            Recell(go);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    System.FormattableString.Invariant(
                            $"[world] {go.name} ({r}) ")
                        + System.FormattableString.Invariant(
                            $"put where the host has it, {d:0.0} units ")
                        + "from where it settled here");
            }

            return true;
        }

        /// <summary>
        /// Notes the loose objects around center not noted yet (spawned, or
        /// come into the world since).
        /// </summary>
        private void Look(Vector3 center)
        {
            int n = Physics.OverlapSphereNonAlloc(
                center, PushRules.WatchRadius, _near, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var b = _near[i].attachedRigidbody;
                if (b != null && !_loose.ContainsKey(b))
                {
                    Note(b, _near[i]);
                }
            }
        }

        /// <summary>
        /// An Item with a rigidbody that can move, not a character, door or
        /// window.
        /// </summary>
        private static Item LooseItem(Rigidbody b)
        {
            if (!PushRules.IsLoose(FactsOf(b)))
            {
                return null;
            }

            return b.GetComponent<Item>();
        }

        /// <summary>
        /// What the push rules know of a body, to tell a loose thing: the
        /// only code that reads it for them. Read only as far as needed: a
        /// body that's frozen or no Item isn't loose whatever else it is.
        /// </summary>
        private static LooseFacts FactsOf(Rigidbody b)
        {
            if (b == null)
            {
                return default;
            }

            var facts = new LooseFacts
            {
                Body = true,
                Frozen = b.constraints == RigidbodyConstraints.FreezeAll,
            };

            if (facts.Frozen)
            {
                return facts;
            }

            facts = facts with { Item = b.GetComponent<Item>() != null };
            if (!facts.Item)
            {
                return facts;
            }

            return facts with
            {
                Character = b.GetComponent<CharBase>() != null,
                InDoor = b.GetComponentInParent<Door>() != null,
                InWindow = b.GetComponentInParent<Window>() != null,
            };
        }

        /// <summary>
        /// A loose object (an Item with a rigidbody that can move), noted where
        /// it is now.
        /// </summary>
        private void Note(Rigidbody b, Collider col)
        {
            if (b == null || _loose.ContainsKey(b))
            {
                return;
            }

            var item = LooseItem(b);
            if (item == null)
            {
                return;
            }

            var r = ObjRef.Pin(b.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var p = new Loose { Item = item, Body = b, Ref = r };
            if (col != null)
            {
                p.Col = col;
            }
            else
            {
                p.Col = b.GetComponentInChildren<Collider>();
            }

            Rebase(p);
            _loose[b] = p;
        }

        /// <summary>
        /// For the tests' log: whether this body is watched for pushes here,
        /// and where it's known to be.
        /// </summary>
        internal string WatchInfo(Rigidbody b)
        {
            if (b == null)
            {
                return "no body";
            }

            if (!_loose.TryGetValue(b, out var p))
            {
                return "not watched";
            }

            string text = "";
            if (p.Since >= 0f)
            {
                text = ", away";
            }

            string touched = "";
            if (p.Touched)
            {
                touched = ", touched";
            }

            return System.FormattableString.Invariant(
                    $"watched, known at {p.Pos.x:0.0} {p.Pos.z:0.0}")
                + System.FormattableString.Invariant($"{text}{touched}");
        }

        /// <summary>
        /// What the push rules know of one watched thing, with our player at
        /// me: the only code that reads a watched thing for them.
        /// </summary>
        private PushFacts FactsOf(Loose p, Vector3 me, bool hostRunsHere)
        {
            if (p.Body == null || p.Item == null)
            {
                return new PushFacts { Gone = true };
            }

            var t = p.Item.transform;
            var facts = new PushFacts
            {
                Kinematic = p.Body.isKinematic,
                Dragged = p.Item == _dragging,
                Settling = p.Item == _settling,
                Shown = _moving.ContainsKey(p.Body.gameObject),
                Moved = (t.position - p.Pos).magnitude,
                Turned = Quaternion.Angle(t.rotation, p.Rot),
                Strayed = p.Since >= 0f,
                Since = new Seconds(p.Since),
                Touched = p.Touched,
                Host = _session.IsHost,
                HostRunsHere = hostRunsHere,
                Hit = p.HitAt >= 0f,
                HitAt = new Seconds(p.HitAt),
                AtRest = AtRest(p.Body),
                Now = Now,
            };

            if (p.Col != null && !_session.IsHost)
            {
                // only a client's own player's touch counts
                facts = facts with
                {
                    Collider = true,
                    Reach = p.Col.bounds.SqrDistance(me),
                };
            }

            return facts;
        }

        private static void Rebase(Loose p)
        {
            var t = p.Item.transform;
            p.Pos = t.position;
            p.Rot = t.rotation;
            p.Since = -1f;
            p.Touched = false;
        }

        /// <summary>
        /// This object is where every game is told (or told by another) it is.
        /// </summary>
        private void Rebase(GameObject go)
        {
            Rigidbody b = null;
            if (go != null)
            {
                b = go.GetComponent<Rigidbody>();
            }

            if (b != null && _loose.TryGetValue(b, out var p) && p.Item != null)
            {
                Rebase(p);
            }
        }
    }
}
