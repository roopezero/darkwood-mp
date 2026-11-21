using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Sync.Players;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Sends the local player's snapshot at a fixed rate and drives puppets for
    /// everyone else. Clients send to the host; the host applies and relays to
    /// the other clients.
    /// </summary>
    public sealed class PlayerSync
    {
        /// Eases our player out of the puppets it walks into.
        public readonly PlayerSeparation Separation = new PlayerSeparation();

        /// The puppets' markers on the game's world map.
        public readonly MapMarkers Markers;

        public readonly Dictionary<ulong, RemotePlayer> Remotes =
            new Dictionary<ulong, RemotePlayer>();
        /// <summary>Another player died (their name).</summary>
        public event System.Action<string> OnRemoteDied;

        private readonly Session _session;
        private readonly PlayerCapture _capture = new PlayerCapture();
        private readonly PlayerSnapshot _snap = new PlayerSnapshot();
        private readonly PlayerSnapshot _incoming = new PlayerSnapshot();
        private Seconds _nextSend;
        // Players who left, and when. Over Steam a PlayerState sent before
        // PlayerLeft can arrive after it (they go on different channels, which
        // Steam doesn't order), and would bring back a frozen puppet.
        private readonly LeftPlayers _left = new LeftPlayers();
        /// <summary>
        /// The tests' dummy (--dwmp-dummy): a copy of you left standing where
        /// you were, which the AI treats as another player. Never a real peer
        /// id.
        /// </summary>
        public const ulong DummyId = PuppetRules.DummyId;
        public bool HasDummy => Remotes.ContainsKey(DummyId);
        private Seconds _dummyFlashUntil;
        private float _dummyDamage;

        public void SpawnDummy(Vector3 offset)
        {
            var snap = new PlayerSnapshot();
            if (!_capture.Capture(snap))
            {
                return;
            }

            snap.Position += offset;
            RemoveRemote(DummyId);
            var d = GetOrCreate(DummyId, "Dummy");
            d.Push(snap);
            Plugin.Log.LogInfo($"Dummy left at {snap.Position}");
        }

        public void FlashDummy(float damage)
        {
            _dummyDamage += damage;
            _dummyFlashUntil = NameTagRules.FlashUntil(new Seconds(Time.time));
        }

        public PlayerSync(Session session)
        {
            _session = session;
            Markers = new MapMarkers(this);
            session.Register(MsgType.PlayerState, OnPlayerState);
            session.OnPeerDisconnected += id =>
            {
                RemoveRemote(id);
                _left.Left(id, Now);
            };

            session.OnPeerConnected += id => _left.Joined(id);
            session.OnSessionEnded += () =>
            {
                _left.Clear();
                foreach (var r in Remotes.Values)
                {
                    r.Destroy();
                }

                Remotes.Clear();
            };
        }

        public void Update()
        {
            Seconds now = Now;
            if (PlayerStateRules.SendDue(now, _nextSend))
            {
                _nextSend = new Seconds(now.Value + PlayerStateRules
                    .SendInterval(Plugin.SendRate.Value).Value);
                Send(reliable: false);
            }

            foreach (var r in Remotes.Values)
            {
                r.Tick();
            }

            Separation.Update(this, _session.LocalId);
            Markers.Tick();
        }

        /// <summary>
        /// Client: send where our player is now, reliably on Control, ahead of
        /// a request the host checks against our position (a trigger set off
        /// the moment we arrived somewhere). Control is ordered, so the host
        /// has handled this (through its MoveCheck, as every PlayerState)
        /// before the request. The State channel's updates are unreliable and
        /// can come later, so without it the host's stand-in could still be
        /// where we were before a teleport, and the request would be refused as
        /// too far away.
        /// </summary>
        public void FlushNow()
        {
            if (_session.IsClient)
            {
                Send(reliable: true);
            }
        }

        private void Send(bool reliable)
        {
            if (!_capture.Capture(_snap))
            {
                return;
            }

            _snap.Owner = _session.LocalId;
            // A joiner isn't shown until its character is placed
            // (World.Characters.Placing), off the others.
            if (!PlayerStateRules.MaySend(SendOf()))
            {
                return;
            }

            var w = _session.Begin(MsgType.PlayerState);
            _snap.Write(w);
            int ch;
            if (reliable)
            {
                ch = Channel.Control;
            }
            else
            {
                ch = Channel.State;
            }

            if (_session.IsHost)
            {
                _session.Broadcast(w, ch, reliable);
            }
            else
            {
                _session.SendToHost(w, ch, reliable);
            }
        }

        private void OnPlayerState(ulong from, PacketReader r)
        {
            _incoming.Read(r);
            StateArrival arrival = PlayerStateRules.Arrive(StateOf(from));
            if (arrival.Drop == StateDrop.LeftJustNow
                || arrival.Drop == StateDrop.NotFromHost)
            {
                return;
            }

            // Never trust the owner field from a client, nor a place it
            // couldn't have got to.
            _incoming.Owner = arrival.Owner;
            // A spectator (dead for good, World.Spectator) goes along with
            // whoever it watches and can't touch anything: it's not
            // checked, and starts afresh if it ever plays again.
            var coop = Coop.Current;
            if (coop != null && arrival.Move == StateMove.Forget)
            {
                coop.Moves.Forget(from);
            }
            else if (coop != null && arrival.Move == StateMove.Check)
            {
                _incoming.Position =
                    coop.Moves.Check(from, _incoming.Position);
            }

            if (arrival.Relays)
            {
                var w = _session.Begin(MsgType.PlayerState);
                _incoming.Write(w);
                _session.Broadcast(
                    w, Channel.State, reliable: false, except: from);
            }

            if (arrival.Drop == StateDrop.AboutUs)
            {
                return;
            }

            var remote =
                GetOrCreate(_incoming.Owner, _session.NameOf(_incoming.Owner));
            remote.Push(_incoming);
        }

        /// The PlayerState just read into _incoming, from a peer.
        private StateFacts StateOf(ulong from)
        {
            ulong key = PlayerStateRules.GuardKey(
                _session.IsHost, from, _incoming.Owner);
            return new StateFacts
            {
                Host = _session.IsHost,
                From = from,
                Said = _incoming.Owner,
                HostId = _session.HostId,
                LocalId = _session.LocalId,
                LeftJustNow = _left.JustNow(key, Now),
                Spectating = _incoming.Spectating,
            };
        }

        private SendFacts SendOf()
        {
            var characters = World.Characters.Instance;
            return new SendFacts
            {
                Active = _session.Active,
                Connected = _session.Connected,
                Placing = characters != null && characters.Placing,
            };
        }

        private TagFacts TagsOf()
        {
            return new TagFacts
            {
                MenuOver = World.UiState.MenuOver,
                MapOpen = MapMarkers.MapOpen,
                OverlaysHidden = World.UiState.WorldOverlaysHidden,
            };
        }

        private LabelFacts LabelOf(RemotePlayer r)
        {
            var last = r.Last;
            byte health = 0;
            if (last != null)
            {
                health = last.Health;
            }

            return new LabelFacts
            {
                Name = r.Name,
                Dummy = r.Owner == DummyId,
                HasLast = last != null,
                Alive = r.Alive,
                Health = health,
                Now = new Seconds(Time.time),
                FlashUntil = _dummyFlashUntil,
                Damage = _dummyDamage,
            };
        }

        private static Seconds Now => new Seconds(Time.unscaledTime);

        private RemotePlayer GetOrCreate(ulong id, string name)
        {
            if (!Remotes.TryGetValue(id, out var r))
            {
                r = new RemotePlayer(id, name);
                if (id != DummyId)
                {
                    r.OnDied += rp =>
                    {
                        System.Action<string> handler = OnRemoteDied;
                        if (handler != null)
                        {
                            handler(_session.NameOf(rp.Owner));
                        }
                    };
                }

                Remotes[id] = r;
            }

            return r;
        }

        private void RemoveRemote(ulong id)
        {
            if (Remotes.TryGetValue(id, out var r))
            {
                r.Destroy();
                Remotes.Remove(id);
            }
        }

        /// <summary>
        /// Draw name tags above puppets (IMGUI; avoids depending on a font
        /// asset).
        /// </summary>
        public void DrawNameTags()
        {
            // Not over the pause menu or the mod's windows; on the map, beside
            // the players' markers; not over a cutscene, a game menu or
            // anything else that isn't the world in play (UiState).
            TagLayer layer = NameTagRules.Where(TagsOf());
            if (layer == TagLayer.Map)
            {
                Markers.DrawNames();
                return;
            }

            if (layer == TagLayer.None)
            {
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            foreach (var r in Remotes.Values)
            {
                if (r.Root == null || !r.Root.activeSelf)
                {
                    continue;
                }

                var sp = cam.WorldToScreenPoint(r.Root.transform.position);
                if (!NameTagRules.Draws(true, sp.z))
                {
                    continue;
                }

                var rect =
                    new Rect(sp.x - 100, Screen.height - sp.y - 60, 200, 22);
                if (NameTagRules.RefreshesName(r.Owner))
                {
                    // names can arrive after the first snapshot
                    r.Name = _session.NameOf(r.Owner);
                }

                NameTag tag = NameTagRules.Label(LabelOf(r));
                if (tag.ResetsDamage)
                {
                    _dummyDamage = 0f;
                }

                GUI.Label(rect, tag.Text, Plugin.NameTagStyle);
            }
        }
    }
}
