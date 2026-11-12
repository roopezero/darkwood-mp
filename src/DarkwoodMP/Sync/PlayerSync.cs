using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// Sends the local player's snapshot at a fixed rate and drives puppets for everyone else.
    /// Clients send to the host; the host applies and relays to the other clients.
    /// </summary>
    public sealed class PlayerSync
    {
        public static PlayerSync Instance { get; private set; }

        public readonly Dictionary<ulong, RemotePlayer> Remotes = new Dictionary<ulong, RemotePlayer>();
        /// <summary>Another player died (their name).</summary>
        public event System.Action<string> OnRemoteDied;

        private readonly Session _session;
        private readonly PlayerCapture _capture = new PlayerCapture();
        private readonly PlayerSnapshot _snap = new PlayerSnapshot();
        private readonly PlayerSnapshot _incoming = new PlayerSnapshot();
        private float _nextSend;
        /// <summary>
        /// The tests' dummy (--dwmp-dummy): a copy of you left standing where you were, which the AI treats as
        /// another player. Never a real peer id.
        /// </summary>
        public const ulong DummyId = ulong.MaxValue - 1;
        public bool HasDummy => Remotes.ContainsKey(DummyId);
        private float _dummyFlashUntil;
        private float _dummyDamage;

        public void SpawnDummy(Vector3 offset)
        {
            var snap = new PlayerSnapshot();
            if (!_capture.Capture(snap)) return;
            snap.Position += offset;
            RemoveRemote(DummyId);
            var d = GetOrCreate(DummyId, "Dummy");
            d.Push(snap);
            Plugin.Log.LogInfo($"Dummy left at {snap.Position}");
        }

        public void FlashDummy(float damage)
        {
            _dummyDamage += damage;
            _dummyFlashUntil = Time.time + 1.5f;
        }

        public PlayerSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.PlayerState, OnPlayerState);
            _moves = new MoveCheck(session);
            session.OnPeerDisconnected += RemoveRemote;
            session.OnSessionEnded += () =>
            {
                foreach (var r in Remotes.Values) r.Destroy();
                Remotes.Clear();
            };
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextSend)
            {
                _nextSend = now + 1f / Mathf.Max(1, Plugin.SendRate.Value);
                if (_capture.Capture(_snap))
                {
                    _snap.Owner = _session.LocalId;
                    // A joiner isn't shown until its character is placed (World.Characters.Placing), off the others.
                    if (_session.Active && _session.Connected && !(World.Characters.Instance?.Placing ?? false))
                    {
                        var w = _session.Begin(MsgType.PlayerState);
                        _snap.Write(w);
                        if (_session.IsHost) _session.Broadcast(w, Channel.State, reliable: false);
                        else _session.SendToHost(w, Channel.State, reliable: false);
                    }
                }
            }

            foreach (var r in Remotes.Values) r.Tick();
            MapMarkers.Tick();
        }

        private readonly MoveCheck _moves;

        private void OnPlayerState(ulong from, PacketReader r)
        {
            _incoming.Read(r);
            if (_session.IsHost)
            {
                // Never trust the owner field from a client, nor a place it couldn't have got to.
                _incoming.Owner = from;
                // A spectator (dead for good, World.Spectator) goes along with whoever it watches and can't touch
                // anything: it's not checked, and starts afresh if it ever plays again.
                if (_incoming.Spectating) _moves.Forget(from);
                else _incoming.Position = _moves.Check(from, _incoming.Position);
                var w = _session.Begin(MsgType.PlayerState);
                _incoming.Write(w);
                _session.Broadcast(w, Channel.State, reliable: false, except: from);
            }
            else if (from != _session.HostId) return;

            if (_incoming.Owner == _session.LocalId) return;
            var remote = GetOrCreate(_incoming.Owner, _session.NameOf(_incoming.Owner));
            remote.Push(_incoming);
        }

        private RemotePlayer GetOrCreate(ulong id, string name)
        {
            if (!Remotes.TryGetValue(id, out var r))
            {
                r = new RemotePlayer(id, name);
                if (id != DummyId) r.OnDied += rp => OnRemoteDied?.Invoke(_session.NameOf(rp.Owner));
                Remotes[id] = r;
            }
            return r;
        }

        private void RemoveRemote(ulong id)
        {
            if (Remotes.TryGetValue(id, out var r)) { r.Destroy(); Remotes.Remove(id); }
        }

        /// <summary>Draw name tags above puppets (IMGUI; avoids depending on a font asset).</summary>
        public void DrawNameTags()
        {
            if (MapMarkers.MapOpen) { MapMarkers.DrawNames(); return; }
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var r in Remotes.Values)
            {
                if (r.Root == null || !r.Root.activeSelf) continue;
                var sp = cam.WorldToScreenPoint(r.Root.transform.position);
                if (sp.z < 0) continue;
                var rect = new Rect(sp.x - 100, Screen.height - sp.y - 60, 200, 22);
                if (r.Owner != DummyId) r.Name = _session.NameOf(r.Owner); // names can arrive after the first snapshot
                string label = r.Name;
                var last = r.Last;
                if (last != null && r.Alive) label += $"  {last.Health}%";
                if (r.Owner == DummyId && Time.time < _dummyFlashUntil) label += $"  -{_dummyDamage:0}";
                else if (r.Owner == DummyId) _dummyDamage = 0f;
                GUI.Label(rect, label, Plugin.NameTagStyle);
            }
        }
    }
}
