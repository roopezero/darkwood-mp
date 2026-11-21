using DarkwoodMP.Menus;
using DarkwoodMP.World.Inventories;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.Sync.Players;
using DarkwoodMP.World;
using HarmonyLib;
using Chapters = DarkwoodMP.World.Story.Chapters;
using UnityEngine;

namespace DarkwoodMP
{
    public sealed partial class Plugin
    {
        private System.Collections.IEnumerator DummyWhenInWorld()
        {
            while (!WorldShare.InWorld)
            {
                yield return null;
            }

            yield return new WaitForSeconds(5f);
            _host.Players.SpawnDummy(new Vector3(120f, 0f, 0f));
            Log.LogInfo("--dwmp-dummy: dummy left 120 units east of you");
            if (_spawnType == null)
            {
                yield break;
            }

            yield return new WaitForSeconds(2f);
            // "--dwmp-spawn TYPE": an enemy (a CharacterType name) between you
            // and the dummy, after it.
            NetPlayerProxy.All.TryGetValue(PlayerSync.DummyId, out var dummy);
            // With an offset, spawnCharacterAround puts it at offset *
            // distance: here, north of the point between you two.
            Vector3 mid;
            if (dummy != null)
            {
                mid = (dummy.transform.position
                        + Player.Instance.transform.position) / 2f;
            }
            else
            {
                mid = Player.Instance.transform.position;
            }

            Vector3 spot = mid + new Vector3(0f, 0f, 250f);
            var c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                Player.Instance.gameObject, spot, 1f, _spawnType,
                nocturnal: false, attackPlayer: false);
            string data;
            if (c != null)
            {
                data = $"--dwmp-spawn: {c.name} at {c.transform.position}";
            }
            else
            {
                data = $"--dwmp-spawn: couldn't spawn {_spawnType}";
            }

            Log.LogInfo(data);
            if (c == null || !Threat.Debug)
            {
                yield break;
            }

            yield return new WaitForSeconds(3f);
            Log.LogInfo($"[threat test] {c.name}.attackPlayer()");
            c.attackPlayer();
            // Threat test: after a while, you "hit" it, which should earn you
            // its attention.
            yield return new WaitForSeconds(15f);
            if (c != null && c.alive && Player.Instance != null)
            {
                Log.LogInfo(
                    $"[threat test] the host player hits {c.name} for 30");
                c.getHit(
                    30f, Player.Instance.transform, canCutInHalf: false,
                    byPlayer: true, canInterrupt: true);
            }
        }

        // The cast to SteamBackend is in the coroutine's MoveNext, which is
        // only compiled when this test runs, with Steam there.
        private System.Collections.IEnumerator SteamSelfTest()
        {
            float until = Time.realtimeSinceStartup + 60f;
            while (_host.Steam == null)
            {
                if (Time.realtimeSinceStartup > until || _steamUnavailable)
                {
                    Log.LogWarning("--dwmp-steam-test: no Steam");
                    yield break;
                }

                yield return null;
            }

            var steam = (SteamBackend)_host.Steam;
            steam.Host();
            until = Time.realtimeSinceStartup + 20f;
            while (!steam.InSession && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Log.LogInfo(
                $"--dwmp-steam-test: {steam.Status}; host={steam.IsHost}; in "
                    + $"session={_host.Session.Active} as "
                    + $"#{_host.Session.LocalId}; "
                    + $"members: {steam.MemberNames()}");
            yield return new WaitForSeconds(5f);
            steam.Leave();
            Log.LogInfo($"--dwmp-steam-test: left; {steam.Status}");
        }

        private System.Collections.IEnumerator ReloadAfterJoin(float seconds)
        {
            while (_host.Session.Peers.Count == 0)
            {
                yield return null;
            }

            yield return new WaitForSeconds(seconds);
            Log.LogInfo("--dwmp-test-reload: loading the world again");
            Singleton<UI>.Instance.quickLoadGame();
        }

        private System.Collections.IEnumerator ChapterTriggers()
        {
            while (!WorldShare.WorldReady)
            {
                yield return null;
            }

            int n = 0;
            foreach (var et in Resources.FindObjectsOfTypeAll<EventTriggers>())
            {
                if (et == null || !et.gameObject.scene.IsValid())
                {
                    continue;
                }

                for (int i = 0; i < et.eventTriggers.Count; i++)
                {
                    var t = et.eventTriggers[i];
                    GameEvents ge = null;
                    if (t != null)
                    {
                        ge = t.gameEvents;
                    }

                    if (ge == null
                        || !ge.events.Exists(
                            e => e != null
                                && e.type
                                    == GameEvent.Type.transportPlayerToObject
                                && e.activeModifier))
                    {
                        continue;
                    }

                    var r = World.ObjRef.Of(et.gameObject);
                    var at = et.transform.position;
                    var step = ge.events.Find(
                        e => e != null
                            && e.type
                                == GameEvent.Type.transportPlayerToObject
                            && e.activeModifier);
                    Log.LogInfo(
                        $"[chapter test] trigger {(int)r.K} {r.Id} {i} at "
                            + $"{at.x:0} {at.z:0} ({et.name}, {t.type}, to "
                            + $"chapter {step.intValue}, fired {t.fired})");
                    n++;
                }
            }

            foreach (var ge in Resources.FindObjectsOfTypeAll<GameEvents>())
            {
                if (ge != null && ge.gameObject.scene.IsValid()
                    && ge.events.Exists(
                        e => e != null
                            && e.type
                                == GameEvent.Type.transportPlayerToObject
                            && e.activeModifier))
                {
                    Log.LogInfo(
                        $"[chapter test] events {ge.name} at "
                            + $"{ge.transform.position.x:0} "
                            + $"{ge.transform.position.z:0}");
                }
            }

            // This world's own chapter event is in a separate location, loaded
            // only when someone goes there; put a stand-in event with the same
            // step beside the host, for a scripted player to ask for.
            var go = new GameObject("dwmp_test_chapter");
            go.transform.position =
                Player.Instance.transform.position + new Vector3(300f, 0f, 0f);
            var test = go.AddComponent<GameEvents>();
            int next = Chapters.HostChapter + 1;
            test.events.Add(new GameEvent
            {
                type = GameEvent.Type.transportPlayerToObject,
                activeModifier = true,
                intValue = next,
                eventAmount = 1
            });

            World.ObjRef.RegisterSpawned(990001, go);
            var tr = World.ObjRef.Of(go);
            Log.LogInfo(
                $"[chapter test] events {(int)tr.K} {tr.Id} 0 at "
                    + $"{go.transform.position.x:0} "
                    + $"{go.transform.position.z:0} to chapter {next}");
            Log.LogInfo(
                $"[chapter test] {n} chapter triggers; this is chapter "
                    + $"{Chapters.HostChapter}");
        }

        private System.Collections.IEnumerator EndAfterJoin(float seconds)
        {
            while (_host.Session.Peers.Count == 0 || !WorldShare.WorldReady)
            {
                yield return null;
            }

            yield return new WaitForSeconds(seconds);
            Log.LogInfo("--dwmp-test-end: starting the epilogue");
            // The step the game's last event runs (GameEvent startDream into
            // the epilogue).
            var step = new GameEvent
            {
                type = GameEvent.Type.startDream,
                Value = Chapters.EpilogueDream,
                eventAmount = 1
            };

            Singleton<Controller>.Instance.StartCoroutine(
                step.fire(Player.Instance.gameObject));
        }

        private System.Collections.IEnumerator MenuAfterJoin(float seconds)
        {
            while (_host.Session.Peers.Count == 0)
            {
                yield return null;
            }

            yield return new WaitForSeconds(seconds);
            Log.LogInfo("--dwmp-test-menu: going back to the main menu");
            Core.returnToMainMenu();
        }

        private System.Collections.IEnumerator DieAfterJoin()
        {
            while (_host.Session.Peers.Count == 0)
            {
                yield return null;
            }

            yield return new WaitForSeconds(6f);
            Log.LogInfo(
                "--dwmp-test-death: dying with "
                    + $"{Player.Instance.Inventory.getAllItems().Count} items");
            Player.Instance.die();
            yield return new WaitForSeconds(15f);
            Log.LogInfo(
                $"--dwmp-test-death: alive again: {Player.Instance.alive}, at "
                    + $"{Player.Instance.transform.position}");
        }

        private System.Collections.IEnumerator FxAfterJoin()
        {
            while (_host.Session.Peers.Count == 0)
            {
                yield return null;
            }

            yield return new WaitForSeconds(4f);
            var p = Player.Instance;
            Log.LogInfo($"--dwmp-test-fx: hit for 1 (health {p.health:0})");
            p.getHit(1f);
            yield return new WaitForSeconds(1f);
            // A Burn alone shows flames and plays the fire; only the burn
            // effect (CharacterEffect) does damage.
            if (p.GetComponent<Burn>() == null)
            {
                p.gameObject.AddComponent<Burn>().burnTime = 3f;
            }

            yield return new WaitForSeconds(5f);
            Log.LogInfo(
                $"--dwmp-test-fx: done (health {p.health:0}, burning "
                    + $"{p.GetComponent<Burn>() != null})");
        }

        private System.Collections.IEnumerator SaveAfterJoin(float seconds)
        {
            while (_host.Session.Peers.Count == 0)
            {
                yield return null;
            }

            yield return new WaitForSeconds(seconds);
            Log.LogInfo("--dwmp-test-save: saving the world");
            Singleton<SaveManager>.Instance.Save(doJson: true, force: true);
        }

        private System.Collections.IEnumerator ClientMenuRejoin(string address)
        {
            while (!(_host.Session.IsClient && WorldShare.WorldReady
                    && Coop.Current != null
                    && Coop.Current.Characters.HasCharacter))
            {
                yield return null;
            }

            yield return new WaitForSeconds(3f);
            Log.LogInfo(
                "[menu test] back to the main menu, as the pause menu's Back "
                    + "to main menu does");
            if (Core.mainMenu)
            {
                Singleton<MainMenu>.Instance.close();
            }

            Core.returnToMainMenu();
            yield return null;
            while (!WorldShare.MainMenuReady())
            {
                yield return null;
            }

            if (_host.Session.Active)
            {
                Log.LogInfo(
                    "[menu test] FAIL: at the main menu, still in the host's "
                        + "game");
            }
            else
            {
                Log.LogInfo(
                    "[menu test] PASS: at the main menu, out of the host's "
                        + "game");
            }

            yield return new WaitForSeconds(1f);
            Log.LogInfo(
                $"[menu test] joining {address} again from the Multiplayer "
                    + "window");
            _direct.JoinForTest(address);
            string data;
            if (_host.Tcp.InSession)
            {
                data = "[menu test] PASS: joining again";
            }
            else
            {
                data = $"[menu test] FAIL: not joining ({_host.Tcp.Status})";
            }

            Log.LogInfo(data);
        }

        private System.Collections.IEnumerator JoinWhenReady(string address)
        {
            while (!WorldShare.MainMenuReady())
            {
                yield return null;
            }

            Log.LogInfo($"--dwmp-join: joining {address}");
            _host.Tcp.Join(address, Cfg.Password.Value);
        }
    }
}
