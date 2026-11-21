using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Owed
{
    /// <summary>
    /// Who is still owed their own part of a once-only trigger that fired for
    /// someone else (ScriptedEvents.LaterStep: a trip through a hole or down a
    /// ladder, its messages, an ordinary item), kept so that it outlasts a
    /// join, a world reload and a restart of the host's game. It used to be
    /// filled only as each EventFired arrived, so a player who joined after
    /// someone else used a hole (or anyone, after the world was loaded again)
    /// found it fired and was stuck on the wrong side.
    ///
    /// - A joiner is owed every once-only trigger with such steps that is fired
    ///   in the world it loaded, except those it had its own part of in this
    ///   world (it set them off, or got its part since). It asks the host (kind
    ///   5) when its player sets one off.
    /// - The host remembers, by player key, which triggers each joiner had its
    ///   part of, and refuses a second one. It's kept in memory across world
    ///   reloads (as the joiners' characters are, so it matches where they
    ///   stand) and written beside the save when the host's game saves:
    ///   prof&lt;id&gt;/darkwoodmp_later.json.
    /// - The host's own player is owed only the triggers a joiner set off first
    ///   (the ones fired before the mod kept this, it went through itself).
    ///   That list goes back to the saved one when the host's world is loaded
    ///   again, as the host's player goes back to where the save has it.
    ///
    /// A trigger is named by its object's save id (or scene, name and place)
    /// and its index in the object's triggers.
    /// </summary>
    internal sealed class OwedParts
    {
        private readonly CoopSession _coop;

        public OwedParts(CoopSession coop)
        {
            _coop = coop;
        }

        private Session S => _coop.Net;

        /// The host's record, kept as long as the game runs.
        private static OwedRecordFile Record => Plugin.Instance.Host.Owed;

        // ---- naming a trigger -----------------------------------------------

        /// <summary>
        /// A name for this trigger that's the same in every game and after the
        /// world is loaded again.
        /// </summary>
        internal static string IdOf(global::EventTrigger t)
        {
            EventTriggers et = null;
            if (t != null)
            {
                et = t.thisEventTriggers;
            }

            if (et == null)
            {
                return null;
            }

            return OwedRules.IdOf(FactsOf(et, t));
        }

        private static TriggerNameFacts FactsOf(
            EventTriggers et, global::EventTrigger t)
        {
            var save = et.GetComponent<SaveableObject>();
            int saveId = 0;
            if (save != null)
            {
                saveId = save.uniqueId;
            }

            var p = et.transform.position;
            return new TriggerNameFacts
            {
                Saved = save != null,
                SaveId = saveId,
                Index = et.eventTriggers.IndexOf(t),
                Scene = et.gameObject.scene.name,
                Name = et.name,
                X = p.x,
                Z = p.z,
            };
        }

        // ---- a joiner's game ------------------------------------------------

        /// <summary>
        /// Joiner: triggers whose own part our player had in the world loaded
        /// now.
        /// </summary>
        private readonly HashSet<global::EventTrigger> _oursHad =
            new HashSet<global::EventTrigger>();
        private int _oursScene;

        private HashSet<global::EventTrigger> Ours()
        {
            int scene =
                UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().handle;
            if (scene != _oursScene)
            {
                _oursHad.Clear();
                _oursScene = scene;
            }

            return _oursHad;
        }

        private string KeyOf(ulong peer)
        {
            string key = null;
            if (S != null)
            {
                S.Keys.TryGetValue(peer, out key);
            }

            if (OwedRules.UsesKey(key))
            {
                return CharacterRecords.KeyHash(key);
            }

            return OwedRules.Unkeyed(peer);
        }

        // ---- what ScriptedEvents asks ---------------------------------------

        /// <summary>
        /// Whether our player is owed its own part of this fired once-only
        /// trigger (see the class comment).
        /// </summary>
        internal bool Owed(global::EventTrigger t)
        {
            return OwedRules.Owed(FactsOf(t));
        }

        private OwedFacts FactsOf(global::EventTrigger t)
        {
            var s = S;
            bool active = s != null && s.Active;
            if (t == null || !active)
            {
                return new OwedFacts { Trigger = t != null, Active = active };
            }

            if (s.IsHost)
            {
                return new OwedFacts
                {
                    Trigger = true,
                    Active = true,
                    Host = true,
                    HostOwed = Record.Data().IsHostOwed(IdOf(t)),
                };
            }

            EventTriggers et = t.thisEventTriggers;
            bool alone = et != null && WorldActions.OursAlone(et.gameObject);
            bool had = false;
            if (et != null && !alone)
            {
                had = Ours().Contains(t);
            }

            return new OwedFacts
            {
                Trigger = true,
                Active = true,
                HasObject = et != null,
                OursAlone = alone,
                HadHere = had,
            };
        }

        /// <summary>
        /// Host: a joiner set off this once-only trigger first; our player is
        /// owed its own part of it.
        /// </summary>
        internal bool HostOwe(global::EventTrigger t)
        {
            var s = S;
            string id = null;
            if (s != null && s.IsHost)
            {
                id = IdOf(t);
            }

            return id != null && Record.Data().HostOwe(id);
        }

        /// <summary>
        /// Our player had (or is having now) its own part of this trigger: it
        /// isn't owed any more.
        /// </summary>
        internal bool Had(global::EventTrigger t)
        {
            var s = S;
            if (t == null || s == null || !s.Active)
            {
                return false;
            }

            if (s.IsHost)
            {
                return Record.Data().HostHad(IdOf(t));
            }

            return Ours().Add(t);
        }

        /// <summary>
        /// Host: whether this joiner had its own part of this trigger (set it
        /// off, or got it as a later player).
        /// </summary>
        internal bool Given(ulong peer, global::EventTrigger t)
        {
            string id = IdOf(t);
            return id != null && Record.Data().Given(KeyOf(peer), id);
        }

        /// <summary>
        /// Host: this joiner has its own part of this trigger now.
        /// </summary>
        internal void Give(ulong peer, global::EventTrigger t)
        {
            string id = IdOf(t);
            if (id == null)
            {
                return;
            }

            Record.Data().Give(KeyOf(peer), id);
        }
    }
}
