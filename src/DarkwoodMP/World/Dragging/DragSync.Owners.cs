using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Dragging;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// DragSync's owner table: who drags what, as the host decides, and
    /// the claims and DragOwner messages that keep it.
    internal sealed partial class DragSync
    {
        // ---- who drags what -------------------------------------------------

        /// The game's own words for a thing out of reach.
        private static string TooFarText =>
            Language.Get("Playermsg_tooFar", "UI");

        /// <summary>
        /// The name of someone else who is dragging this object now (as the
        /// host decided), or null.
        /// </summary>
        public string OtherOwner(ObjRef r)
        {
            ulong other = DragRules.OtherOwner(FactsOf(r));
            if (other == 0)
            {
                return null;
            }

            return _session.NameOf(other);
        }

        /// <summary>"NAME is moving it", the words of Refusal.OtherMoving.
        /// </summary>
        internal static string MovingText(string who)
        {
            return Refusals.Text(Verdict.Refuse(Refusal.OtherMoving, who));
        }

        private void SendOwner(ObjRef r, ulong owner, bool letGo, ulong to = 0)
        {
            var w = _session.Begin(MsgType.DragOwner);
            var word = new OwnerWord
            {
                Name = ObjRef.NameOf(r),
                Owner = owner,
                LetGo = letGo,
            };

            DragOwner.Write(w, word);
            if (to != 0)
            {
                _session.SendTo(to, w, Channel.Control, reliable: true);
            }
            else
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Host: where this object is here, as a drag's end, to one player (0:
        /// everyone), for their copy to go there.
        /// </summary>
        private void SendHere(ObjRef r, ulong to)
        {
            var item = ObjRef.Resolve<Item>(r);
            if (item == null)
            {
                return;
            }

            var w = Message(
                r, item.transform.position, item.transform.rotation, true);
            if (to != 0)
            {
                _session.SendTo(to, w, Channel.Control, reliable: true);
            }
            else
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Host, a DragClaim inside a Request: the player took hold of this
        /// object. Theirs unless someone else drags it.
        /// </summary>
        private void OnClaim(ulong from, PacketReader r)
        {
            var authority = Coop.Current.Authority;
            var obj = ObjRef.RefOf(DragClaim.Read(r));
            var outcome = DragRules.MayClaim(FactsOf(obj, from));
            if (outcome.Answer == ClaimAnswer.None)
            {
                return;
            }

            if (outcome.Answer == ClaimAnswer.Refused)
            {
                if (outcome.PutBack)
                {
                    // before the Verdict: where their copy goes as they let go
                    SendHere(obj, from);
                }

                authority.Refuse(outcome.Verdict);
                return;
            }

            var item = ObjRef.Resolve<Item>(obj);
            string key = obj.ToString();
            _owners[key] =
                new Owner { Id = from, Heard = Time.unscaledTime, Ref = obj };
            SendOwner(obj, from, letGo: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[dragging] {_session.NameOf(from)} drags {item.name} "
                        + $"({obj})");
            }
        }

        /// <summary>
        /// What the host knows of a claim of obj by from: the only code that
        /// reads the thing and the owner table for DragRules.MayClaim. The
        /// thing is looked up only on the host, in a request's handler.
        /// </summary>
        private ClaimFacts FactsOf(ObjRef obj, ulong from)
        {
            var authority = Coop.Current.Authority;
            var claim = new ClaimFacts
            {
                Host = _session.IsHost,
                Handling = authority.Handling,
                From = from,
                Now = Now,
            };

            if (!claim.Host || !claim.Handling)
            {
                return claim;
            }

            var item = ObjRef.Resolve<Item>(obj);
            if (item == null)
            {
                return claim;
            }

            claim = claim with
            {
                Found = true,
                Reach = authority.ReachOf(
                    item.transform.position, WorldActions.Reach, item.name),
            };

            if (_owners.TryGetValue(obj.ToString(), out var o))
            {
                claim = claim with
                {
                    Owned = true,
                    Owner = o.Id,
                    OwnerName = _session.NameOf(o.Id),
                    Heard = new Seconds(o.Heard),
                };
            }

            return claim;
        }

        /// <summary>
        /// Host: whether a player's ObjMove is taken. While someone drags the
        /// thing, only their moves are (anyone else is sent where it is, at
        /// most twice a second); with nobody dragging it, a move that isn't a
        /// drag's end (a drag not agreed to) is dropped, and an end (a push
        /// come to rest) is taken as before. The mover must be within reach
        /// of the thing and of where it went: if not, their copy is put back
        /// where the thing is here, and an owner who isn't loses the drag,
        /// and lets go.
        /// </summary>
        private bool TakeMove(ulong from, in Moved move)
        {
            var obj = ObjRef.RefOf(move.Name);
            string key = obj.ToString();
            var item = ObjRef.Resolve<Item>(obj);
            string ck = key + "/" + from;
            var take = DragRules.TakeMove(FactsOf(item, from, move));
            if (take.FreeStale)
            {
                Free(key, _owners[key], "stopped sending");
            }

            if (take.Correct)
            {
                _corrected[ck] = Time.unscaledTime;
                SendHere(obj, from);
                Plugin.Log.LogInfo(
                    $"[dragging] {_session.NameOf(from)} moved "
                        + $"{obj}, which {_session.NameOf(_owners[key].Id)} "
                        + "drags: not taken, sent where it is");
            }

            if (take.Dropped && WorldActions.Debug)
            {
                // A push still sliding (nobody drags it) is passed on to be
                // shown only if the pusher is at it. Where it comes to rest
                // is checked and taken as its last move.
                Plugin.Log.LogInfo(
                    $"[dragging] {_session.NameOf(from)} moved {obj} from "
                        + "too far without a claim: dropped");
            }

            if (take.PutBack)
            {
                // a player drags only what they're at, to near where they
                // are: their copy goes back where the thing is here
                Plugin.Log.LogInfo(
                    $"[authority] {_session.NameOf(from)} dragged {item.name} "
                        + "from too far: put back");
                _session.SendTo(
                    from,
                    Message(
                        obj, item.transform.position, item.transform.rotation,
                        true),
                    Channel.Control, reliable: true);
            }

            if (take.Disown)
            {
                _owners.Remove(key);
                SendOwner(obj, 0, letGo: true);
            }

            if (take.Refresh)
            {
                _owners[key].Heard = Time.unscaledTime;
            }

            if (take.EndDrag)
            {
                _owners.Remove(key);
                SendOwner(obj, 0, letGo: false);
            }

            return take.Taken;
        }

        /// <summary>
        /// What the host knows of from's move of item: the only code that
        /// reads the thing, the owner table and the corrections for
        /// DragRules.TakeMove.
        /// </summary>
        private DragMoveFacts FactsOf(Item item, ulong from, in Moved told)
        {
            string key = ObjRef.RefOf(told.Name).ToString();
            var move = new DragMoveFacts
            {
                From = from,
                Last = told.Last,
                Now = Now,
            };

            if (_owners.TryGetValue(key, out var o))
            {
                move = move with
                {
                    Owned = true,
                    Owner = o.Id,
                    Heard = new Seconds(o.Heard),
                };
            }

            if (_corrected.TryGetValue(key + "/" + from, out float at))
            {
                move = move with
                {
                    Corrected = true,
                    CorrectedAt = new Seconds(at),
                };
            }

            if (item == null)
            {
                return move;
            }

            var authority = Coop.Current.Authority;
            return move with
            {
                Found = true,
                NearThing = authority.Near(
                    from, item.transform.position, WorldActions.Reach),
                NearPlace = authority.Near(
                    from, VectorOf(told.Pos), WorldActions.Reach),
            };
        }

        /// <summary>
        /// Host: a drag whose owner has gone quiet or left is nobody's:
        /// everyone is sent where the thing is here.
        /// </summary>
        private void Free(string key, Owner o, string why)
        {
            _owners.Remove(key);
            SendHere(o.Ref, 0);
            SendOwner(o.Ref, 0, letGo: true);
            Plugin.Log.LogInfo(
                $"[dragging] {_session.NameOf(o.Id)}'s drag of {o.Ref} freed: "
                    + $"{why}");
        }

        private void FreeStale()
        {
            if (DragRules.ForgetsCorrections(_owners.Count))
            {
                if (_corrected.Count > 0)
                {
                    _corrected.Clear();
                }

                return;
            }

            List<string> stale = null;
            foreach (var kv in _owners)
            {
                if (DragRules.Sweep(FactsOf(kv.Value)) != OwnerSweep.Keep)
                {
                    if (stale == null)
                    {
                        stale = new List<string>();
                    }

                    stale.Add(kv.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (var k in stale)
            {
                var o = _owners[k];
                if (DragRules.Sweep(FactsOf(o)) == OwnerSweep.Free)
                {
                    Free(k, o, "stopped sending");
                }
                else
                {
                    _owners.Remove(k);
                }
            }
        }

        private void FreeOwnedBy(ulong who)
        {
            List<string> theirs = null;
            foreach (var kv in _owners)
            {
                if (DragRules.FreesOnLeave(FactsOf(kv.Value), who))
                {
                    if (theirs == null)
                    {
                        theirs = new List<string>();
                    }

                    theirs.Add(kv.Key);
                }
            }

            if (theirs != null)
            {
                foreach (var k in theirs)
                {
                    Free(k, _owners[k], "they left");
                }
            }
        }

        /// <summary>
        /// Client: who drags this object now, as the host decided; let go of it
        /// if the host says so.
        /// </summary>
        private void OnDragOwner(ulong from, PacketReader r)
        {
            var word = DragOwner.Read(r);
            var obj = ObjRef.RefOf(word.Name);
            ulong owner = word.Owner;
            string key = obj.ToString();
            var heard = new NewsFacts
            {
                Client = _session.IsClient,
                FromHost = from == _session.HostId,
                InWorld = WorldShare.InWorld,
                Owner = owner,
                LetGo = word.LetGo,
                Ours = _dragging != null && _dragRef.ToString() == key,
                Local = _session.LocalId,
            };

            var news = OurDragRules.News(heard);
            if (news.Entry == OwnerEntry.Forget)
            {
                _owners.Remove(key);
            }
            else if (news.Entry == OwnerEntry.Remember)
            {
                _owners[key] = new Owner
                {
                    Id = owner,
                    Heard = Time.unscaledTime,
                    Ref = obj
                };
            }

            if (news.Drag == ClaimReply.Claimed)
            {
                _claimed = true;
            }
            else if (news.Drag == ClaimReply.Moving)
            {
                CancelLocal(_dragging, MovingText(_session.NameOf(owner)));
            }
            else if (news.Drag == ClaimReply.TooFar)
            {
                CancelLocal(_dragging, TooFarText);
            }
        }

        /// <summary>
        /// For the tests (WorldTest): another player (owner; 0: nobody) drags
        /// this object, as if the host had agreed.
        /// </summary>
        internal void TestOwner(GameObject go, ulong owner)
        {
            var r = ObjRef.Pin(go);
            if (owner == 0)
            {
                _owners.Remove(r.ToString());
            }
            else
            {
                _owners[r.ToString()] = new Owner
                {
                    Id = owner,
                    Heard = Time.unscaledTime,
                    Ref = r
                };
            }
        }
    }
}
