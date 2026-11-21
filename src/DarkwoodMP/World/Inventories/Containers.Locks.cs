using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// Containers: one player at a time has a container open (ContainerLock,
    /// ContainerBusy).
    /// </summary>
    internal sealed partial class Containers
    {
        // ---- one player at a time -------------------------------------------

        /// <summary>
        /// Host: who has which container open ("kind:id" of its ObjRef).
        /// </summary>
        private readonly ContainerLocks _locks = new ContainerLocks();
        public int LockCount => _locks.Count;
        private static string LockKey(ObjRef r) => r.ToString();

        private void Opened(Inventory inv)
        {
            var r = ObjRef.Of(inv.gameObject);
            if (!r.Valid)
            {
                return;
            }

            if (_session.IsClient)
            {
                SendLock(r, open: true);
                return;
            }

            // Host: our own player opening it.
            if (!_locks.Open(LockKey(r), _session.LocalId, out var holder))
            {
                Busy(inv, _session.NameOf(holder));
            }
        }

        private void Closed(Inventory inv)
        {
            if (!_refs.TryGetValue(inv, out var r) || !r.Valid)
            {
                return;
            }

            if (_session.IsClient)
            {
                SendLock(r, open: false);
            }
            else
            {
                _locks.Close(LockKey(r), _session.LocalId);
            }
        }

        /// <summary>
        /// Host: whether someone other than this player has the container open.
        /// </summary>
        public bool LockedByOther(ObjRef r, ulong who, out string holderName)
        {
            holderName = null;
            if (!_locks.HeldByOther(LockKey(r), who, out var holder))
            {
                return false;
            }

            holderName = _session.NameOf(holder);
            return true;
        }

        private void SendLock(ObjRef r, bool open)
        {
            var w = _session.Begin(MsgType.ContainerLock);
            ContainerLockMessage.Write(w, ObjRef.NameOf(r), open);
            _session.SendToHost(w, Channel.Control, reliable: true);
        }

        private void OnLock(ulong from, PacketReader r)
        {
            if (!_session.IsHost)
            {
                return;
            }

            var obj = ObjRef.RefOf(ContainerLockMessage.Read(r, out bool open));
            string key = LockKey(obj);
            if (!open)
            {
                _locks.Close(key, from);
                return;
            }

            if (!_locks.Open(key, from, out var holder))
            {
                var w = _session.Begin(MsgType.ContainerBusy);
                ContainerBusyMessage.Write(
                    w, ObjRef.NameOf(obj), _session.NameOf(holder));
                _session.SendTo(from, w, Channel.Control, reliable: true);
            }
        }

        private void OnBusy(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }

            var obj = ObjRef.RefOf(
                ContainerBusyMessage.Read(r, out string who));
            var inv = ObjRef.Resolve<Inventory>(obj);
            if (inv != null)
            {
                Busy(inv, who);
            }
        }

        private void Busy(Inventory inv, string who)
        {
            _watched.Remove(inv); // nothing of ours to send from it
            if (inv.open)
            {
                inv.hide();
            }

            Player instance = Player.Instance;
            if (instance != null)
            {
                instance.displayMessage($"{who} is using this");
            }

            Plugin.Log.LogInfo($"{inv.name} is in use by {who}");
        }
    }
}
