using System;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.KeptCharacters
{
    /// <summary>
    /// Client: our own character, asked for, placed and sent back.
    /// </summary>
    internal sealed partial class Characters
    {
        private bool _asked, _have;
        private float _nextSave, _readyAt = -1f;

        /// <summary>
        /// Client: in the host's world, but our character isn't placed yet. Our
        /// player still stands where the host's save had the host's player,
        /// which is often where the host is now, so PlayerSync doesn't send
        /// where we are until then (while the world loads, and at most
        /// PlaceWait seconds after): the others first see us where we're put.
        /// </summary>
        public bool Placing => OurCharacterRules.Placing(OurFactsNow());

        private OurFacts OurFactsNow() => new OurFacts
        {
            Client = NetEnemies.ClientMode,
            InWorld = WorldShare.InWorld,
            Have = _have,
            ReadyAt = new Seconds(_readyAt),
            Now = new Seconds(Time.unscaledTime),
            QuitSent = _quitSent,
            Prologue = Prologue.Active,
        };

        /// <summary>
        /// Client: our character in the host's world has come (CharacterLoad).
        /// </summary>
        public bool HasCharacter => _have;

        /// <summary>
        /// The loaded world (scene handle) we asked for our character in.
        /// Joining again (Rejoin: a new chapter, the host's world reloaded, the
        /// panel's reload) is welcomed while the old world is still loaded, and
        /// the host's world is loaded only after that, in the same session. The
        /// request used to go from the old world in that last frame (the game's
        /// returnToMainMenu clears forbidInputs, and its world is there until
        /// the next frame); the answer was lost with that world and
        /// nothing asked again, so the player kept the host's character from
        /// the host's save: the host's place, hotbar and bags. Now each loaded
        /// world asks for itself.
        /// </summary>
        private int _askedScene;

        private static int ActiveScene =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

        public void Update()
        {
            if (!NetEnemies.ClientMode || !WorldShare.InWorld)
            {
                _readyAt = -1f;
                return;
            }

            if (_asked && ActiveScene != _askedScene)
            {
                // Another world loaded in this session: what we had or asked
                // for was the last one's.
                _asked = false;
                _have = false;
                _readyAt = -1f;
            }

            if (!WorldShare.WorldReady)
            {
                return;
            }

            if (_readyAt < 0f)
            {
                // the wait starts once the world has loaded
                _readyAt = Time.unscaledTime;
            }

            if (!_asked)
            {
                if (Core.mainMenu)
                {
                    // the menu over a world: we may be leaving it
                    // (returnToMainMenu sets it at once)
                    return;
                }

                _asked = true;
                _askedScene = ActiveScene;
                _session.SendToHost(
                    _session.Begin(MsgType.CharacterRequest), Channel.Control,
                    reliable: true);
                return;
            }

            if (_have && Time.unscaledTime >= _nextSave)
            {
                SaveNow();
            }
        }

        /// <summary>
        /// Direct's BeforeLeave: our character goes to the host before we go.
        /// </summary>
        public static void SaveBeforeLeaving()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Characters.SaveNow();
            }
        }

        /// <summary>
        /// Client: send our character to the host now (also as we leave, and as
        /// the game quits).
        /// </summary>
        public void SaveNow()
        {
            // the prologue's character isn't kept: the host keeps ours from
            // its end on
            if (!OurCharacterRules.Sends(OurFactsNow()))
            {
                return;
            }

            _nextSave = Time.unscaledTime
                + OurCharacterRules.SaveInterval.Value;
            var p = Player.Instance;
            try
            {
                var w = _session.Begin(MsgType.CharacterSave);
                CharacterSave.Write(
                    w,
                    new CharacterSaveBody
                    {
                        Player = JsonConvert.SerializeObject(
                            new Player.SaveState(p)),
                        Inventory = JsonConvert.SerializeObject(
                            new Inventory.SaveState(p.Inventory)),
                        Hotbar = JsonConvert.SerializeObject(
                            new Inventory.SaveState(p.Hotbar)),
                        // the chapter of the world we're in (the host's,
                        // until it moves on)
                        Chapter = Chapters.HostChapter,
                    });

                _session.SendToHost(w, Channel.Control, reliable: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't send your character: {e.Message}");
            }
        }

        // Set once the game quits: sent then, while the world is still whole.
        // Leaving at OnDestroy sends nothing more, as the player may already be
        // torn down by then.
        private bool _quitSent;

        /// <summary>
        /// Client, OnApplicationQuit: send our character now, and only now.
        /// </summary>
        public void SaveForQuit()
        {
            SaveNow();
            _quitSent = true;
        }

        private void OnLoad(ulong from, PacketReader r)
        {
            var step = OurCharacterRules.Take(
                new LoadFacts
                {
                    FromHost = from == _session.HostId,
                    Client = NetEnemies.ClientMode,
                    Asked = _asked,
                    AskedScene = _askedScene,
                    Scene = ActiveScene,
                    InWorld = WorldShare.InWorld,
                });

            if (step == LoadStep.Ignore)
            {
                return;
            }

            if (step == LoadStep.AskAgain)
            {
                Plugin.Log.LogInfo(
                    "Our character came for a world we've left: asking again "
                        + "in this one");
                _asked = false;
                return;
            }

            var body = CharacterLoad.Read(r);
            var kind = body.Kind;
            string ps = body.Player,
                inv = body.Inventory,
                hot = body.Hotbar;
            Vector3 spawn = new Vector3(body.At.X, body.At.Y, body.At.Z);
            var p = Player.Instance;
            if (p == null)
            {
                return;
            }

            if (kind == CharacterKind.Dead)
            {
                // Our character died for good here: as the game doesn't bring
                // it back, we watch the others again.
                _have = true;
                _nextSave = Time.unscaledTime
                + OurCharacterRules.SaveInterval.Value;
                Plugin.Log.LogInfo(
                    "Your character in this world died for good: you watch the "
                        + "others");
                Spectator.BeginDead();
                return;
            }

            try
            {
                if (OurCharacterRules.Applies(kind))
                {
                    ApplyRecord(p, ps, inv, hot);
                }

                var facts = FactsOf(kind, spawn, p);
                // Our own kept place, but not on this world's map (a character
                // kept with the wrong chapter, or one that was away in a
                // separate location): beside the host, as from the last
                // chapter.
                if (OurCharacterRules.OffMap(facts))
                {
                    Plugin.Log.LogInfo(
                        $"[spawn] your kept place {p.transform.position} isn't "
                            + "on this map: your character is back, beside the "
                            + "host");
                }

                // In the prologue, a new character is what the stage made it
                // (its own items): only put beside the host. A copy of the
                // host's is kept, beside them. Our own stays
                // where it was saved, not on the host (or at 0,0 when no
                // position was given).
                var stand = OurCharacterRules.Stand(facts);
                if (stand == StandStep.AtSpawn)
                {
                    p.transform.position = spawn;
                }
                else if (stand == StandStep.Fresh)
                {
                    Fresh(p, spawn);
                }

                Vector3? host;
                if (OurCharacterRules.IsZero(facts.Spawn))
                {
                    host = (Vector3?)null;
                }
                else
                {
                    host = spawn;
                }

                PlaceFree(p, host);
                p.refreshRecipes();
                Reequip(p);
                Progression.AfterCharacter(p);
                Plugin.Log.LogInfo(LoadedText(OurCharacterRules.Note(facts)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't load your character: {e}");
            }

            _have = true;
            _nextSave = Time.unscaledTime
                + OurCharacterRules.SaveInterval.Value;
        }

        private static StandFacts FactsOf(
            CharacterKind kind, Vector3 spawn, Player p)
        {
            return new StandFacts
            {
                Kind = kind,
                Prologue = Prologue.Active,
                Spawn = new Vec3(spawn.x, spawn.y, spawn.z),
                OnMap = OnMap(p.transform.position),
            };
        }

        private static string LoadedText(LoadNote note)
        {
            switch (note)
            {
                case LoadNote.Prologue:
                    return "A new character for you in the host's prologue";
                case LoadNote.Own:
                    return "Your character in this world is back";
                case LoadNote.LastChapter:
                    return "Your character from the last chapter is back, "
                        + "beside the host";
                case LoadNote.New:
                    return "A new character for you in this world";
                default:
                    return "You start as a copy of the host's character";
            }
        }
    }
}
