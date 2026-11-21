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
        private System.Collections.IEnumerator SoloTest(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            System.Collections.IEnumerator Shot(string name) =>
                Screenshot(
                    System.IO.Path.Combine(dir, name + ".png"), before: 1.5f,
                    log: $"[solo] shot {name}");
            while (!WorldShare.WorldReady)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(6f);
            Log.LogInfo($"[solo] session active {_host.Session.Active}");
            yield return Shot("solo-world");
            Core.showHidePauseMenu();
            yield return Shot("solo-pause");
            var cam = Singleton<CamMain>.Instance;
            Log.LogInfo(
                "[solo] pause menu: world camera on "
                    + $"{cam != null && cam.gameObject.activeSelf}, time scale "
                    + $"{Time.timeScale}, paused {Core.Paused}");
            Core.showHidePauseMenu();
            yield return new WaitForSecondsRealtime(1f);

            // The game's keys: which actions K and N are on (the Skills panel's
            // key must be free).
            ControlsMenu controls = null;
            if (Singleton<MainMenu>.Instance != null)
            {
                controls = Singleton<MainMenu>.Instance.ControlsMenu;
            }

            if (controls != null)
            {
                var keys = new List<string>();
                foreach (var a in ("Aim|Attack|Context Menu|Cycle|Dodge|"
                        + "Effects Cycle|Far Look|Hotbar1|Hotbar2|Hotbar3|"
                        + "Hotbar4|Hotbar5|Hotbar6|Hotbar7|Hotbar8|Hotbar9|"
                        + "Inventory|Journal|Jump|Map|Move Horizontal|"
                        + "Move Vertical|Player Menu|Primary Action|Reload|Run|"
                        + "Secondary Attack|Skill Menu|Start|CycleLight|"
                        + "ToggleLightFlash|Menu").Split('|'))
                {
                    try
                    {
                        keys.Add($"{a}={controls.getButtonName(a)}");
                    }
                    catch (Exception e)
                    {
                        keys.Add($"{a}=? {e.GetType().Name}");
                    }
                }

                Log.LogInfo(
                    $"[solo] the game's keys: {string.Join(", ", keys)}");
            }

            Log.LogInfo($"[solo] skill tiers: {SkillTree.Describe()}");
            var p = Player.Instance;
            var ctl = Singleton<Controller>.Instance;
            var dreams = Singleton<Dreams>.Instance;
            bool dreamsWere = dreams.disabled;
            // the levels given here mustn't start the level dreams
            dreams.disabled = true;

            // The Skills panel: two levels with their points, the panel opened
            // with its key, a point spent.
            _host.Progress.TestGiveLevels(2, chooseSkills: false);
            Log.LogInfo(
                $"[solo] given two levels: {_host.Progress.TestState()}");
            SkillsPanel.TestPressKey();
            yield return new WaitForSecondsRealtime(2f);
            Log.LogInfo(
                $"[solo] skills panel open {SkillsPanel.Open}, backpack open "
                    + $"{p.Inventory.open}");
            yield return Shot("solo-skills");
            var choice = SkillTree.AnyChoice(p);
            int pointsBefore = SkillTree.Points(p);
            if (choice == null)
            {
                Log.LogInfo("[solo] FAIL no skill to learn");
            }
            else
            {
                SkillsPanel.TestPick(choice[0], choice[1]);
                yield return Shot("solo-skills-picked");
                string why = SkillsPanel.TestLearn(choice[0], choice[1]);
                string text = "";
                if (choice[1] != null)
                {
                    text = " with " + choice[1];
                }

                string outcome;
                if (why == null)
                {
                    outcome = "ok";
                }
                else
                {
                    outcome = "FAIL " + why;
                }

                Log.LogInfo(
                    $"[solo] learned {choice[0]}{text}: {outcome}, points "
                        + $"{pointsBefore} -> {SkillTree.Points(p)}");
                yield return Shot("solo-skills-learned");
                // Against the rules: the same skill again.
                string again = SkillsPanel.TestLearn(choice[0], choice[1]);
                string repeat = "FAIL learned";
                if (again != null)
                {
                    repeat = "refused (" + again + ")";
                }

                Log.LogInfo($"[solo] the same skill again: {repeat}");

                // The mouse (the panel's test mouse) on the learned icon's
                // bottom right pixel.
                if (SkillsPanel.TestRects(
                    choice[0], out var icon, out var cell))
                {
                    SkillsPanel.TestMouse =
                        new Vector2(icon.xMax - 1f, icon.yMax - 1f);
                    yield return Shot("solo-skills-hover-edge");
                    string testHover = SkillsPanel.TestHover;
                    if (testHover == null)
                    {
                        testHover = "nothing";
                    }

                    Log.LogInfo(
                        $"[solo] test mouse at {choice[0]}'s icon edge: over "
                            + $"{testHover}; icon {icon}, cell {cell}");
                    SkillsPanel.TestMouse = null;
                }

                // Hovering every skill in turn mustn't change the window, its
                // scrollbar or the grid's place.
                yield return SkillsPanel.TestHoverSweep(null);
                // The real mouse: tools outside the game (xdotool) move it and
                // click on the line below.
                var next = SkillTree.AnyChoice(p);
                if (next != null
                    && SkillsPanel.TestRects(next[0], out var nIcon, out _))
                {
                    int eaten = SkillsPanel.TestEaten;
                    Log.LogInfo(
                        $"[solo] real-mouse {Mathf.RoundToInt(nIcon.xMin + 1)} "
                            + $"{Mathf.RoundToInt(nIcon.yMin + 1)} click (top "
                            + $"left pixel of {next[0]})");
                    yield return new WaitForSecondsRealtime(6f);
                    string hovered = SkillsPanel.TestHover;
                    if (hovered == null)
                    {
                        hovered = "nothing";
                    }

                    string testChosen = SkillsPanel.TestChosen;
                    if (testChosen == null)
                    {
                        testChosen = "nothing";
                    }

                    Log.LogInfo(
                        $"[solo] real mouse: over {hovered}, chosen "
                            + $"{testChosen}, the game's mouse reads eaten "
                            + $"{SkillsPanel.TestEaten - eaten}");
                    yield return Shot("solo-skills-real-mouse");
                }

                // Several points spent one after another with the real mouse: a
                // skill, its negative one if needed, Learn.
                if (RealMouse.Available)
                {
                    _host.Progress.TestGiveLevels(1, chooseSkills: false);
                    yield return new WaitForSecondsRealtime(1f);
                    int had = SkillTree.Points(p);
                    List<string> clickFails = null;
                    yield return SkillsPanel.TestClickAll(f => clickFails = f);
                    yield return Shot("solo-skills-clicked");
                    string result;
                    if (clickFails.Count == 0 && had >= 2)
                    {
                        result = "PASS all learned";
                    }
                    else
                    {
                        result = "FAIL " + string.Join("; ", clickFails);
                    }

                    Log.LogInfo(
                        $"[solo] real clicks on {had} points: {result}: "
                            + $"{_host.Progress.TestState()}");
                }
                else
                {
                    Log.LogInfo(
                        "[solo] no ydotool: the real clicks were not tried");
                }
            }

            SkillsPanel.TestPressKey();
            yield return new WaitForSecondsRealtime(1.5f);
            Log.LogInfo(
                $"[solo] skills panel open {SkillsPanel.Open}, backpack open "
                    + $"{p.Inventory.open}");

            // The stove: a reset outside the morning is refused; in the morning
            // it's allowed, once a day.
            Singleton<UI>.Instance.LevelingMenu.show();
            float until = Time.realtimeSinceStartup + 20f;
            while (!_host.Progress.TestCanReset
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            bool morningWas = ctl.isAfterNight;
            ctl.isAfterNight = false;
            Log.LogInfo(
                $"[solo] stove menu open {_host.Progress.TestCanReset}; "
                    + $"before: {_host.Progress.TestState()}");
            yield return Shot("solo-stove");
            _host.Progress.TestReset();
            yield return new WaitForSecondsRealtime(1f);
            Log.LogInfo(
                "[solo] after a reset outside the morning: "
                    + $"{_host.Progress.TestState()}");
            // as after dawn in the hideout (Controller.startAfterNight),
            // without the trader
            ctl.isAfterNight = true;
            yield return Shot("solo-stove-morning");
            _host.Progress.TestReset();
            yield return new WaitForSecondsRealtime(1f);
            Log.LogInfo(
                "[solo] after a reset in the morning: "
                    + $"{_host.Progress.TestState()}");
            yield return Shot("solo-stove-reset");
            _host.Progress.TestReset();
            yield return new WaitForSecondsRealtime(0.5f);
            Log.LogInfo(
                "[solo] after a second reset the same day: "
                    + $"{_host.Progress.TestState()}");
            ctl.isAfterNight = morningWas;

            // A full bar at the stove gives the level and its point, without
            // the game's skill choice.
            int levelBefore = p.currentLevel;
            p.experience = Mathf.Max(
                p.experience,
                p.levelRequirements[
                    Mathf.Clamp(
                        p.currentLevel, 0, p.levelRequirements.Count - 1)]);
            _host.Progress.Cooked(1);
            Singleton<UI>.Instance.LevelingMenu.updateLiquid();
            until = Time.realtimeSinceStartup + 10f;
            while (p.currentLevel == levelBefore
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(1f);
            Log.LogInfo(
                $"[solo] after a full bar at the stove: level {levelBefore} -> "
                    + $"{p.currentLevel}, the game's skill choice open "
                    + $"{Singleton<SkillsMenu>.Instance.opened}: "
                    + $"{_host.Progress.TestState()}");
            yield return Shot("solo-stove-level");
            Singleton<UI>.Instance.LevelingMenu.hide();
            yield return new WaitForSecondsRealtime(2f);
            SkillsPanel.TestPressKey();
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("solo-skills-after");
            SkillsPanel.TestPressKey();
            dreams.disabled = dreamsWere;
            Log.LogInfo("[solo] done");
        }

        private System.Collections.IEnumerator DumpPrefabs()
        {
            while (!WorldShare.WorldReady)
            {
                yield return null;
            }

            var sm = Singleton<SaveManager>.Instance;
            foreach (var kv in sm.prefabDictionary)
            {
                Log.LogInfo($"[prefab] {kv.Key} = {kv.Value}");
            }

            Log.LogInfo($"[prefab] {sm.prefabDictionary.Count} prefabs");
            var idb = Singleton<ItemsDatabase>.Instance;
            if (idb != null)
            {
                foreach (var kv in idb.itemsDict)
                {
                    Log.LogInfo($"[item] {kv.Key} = {kv.Value}");
                }
            }
        }

        private System.Collections.IEnumerator DumpUi()
        {
            while (!WorldShare.MainMenuReady())
            {
                yield return null;
            }

            yield return new WaitForSeconds(3f);
            var menu = Singleton<MainMenu>.Instance;
            if (menu != null)
            {
                DumpTree(menu.transform, 0);
            }

            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
            {
                Log.LogInfo($"[ui] font {f.name} dynamic={f.dynamic}");
            }

            var ui = Singleton<UI>.Instance;
            if (ui != null)
            {
                foreach (Transform c in ui.transform)
                {
                    Log.LogInfo(
                        $"[ui] UI child {c.name} "
                            + $"active={c.gameObject.activeSelf} "
                            + $"pos={c.localPosition}");
                }
            }

            try
            {
                foreach (var kv in global::Language.GetAllKeysForSheet("UI"))
                {
                    Log.LogInfo($"[ui] UI key \"{kv.Key}\" = \"{kv.Value}\"");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"[ui] no UI sheet: {e.Message}");
            }
        }

        private static void DumpTree(Transform t, int depth)
        {
            if (depth > 6)
            {
                return;
            }

            var comps = string.Join(",",
                Array.ConvertAll(t.GetComponents<Component>(), c =>
            {
                if (c == null)
                {
                    return "?";
                }

                return c.GetType().Name;
            }));

            string extra = "";
            var tm = t.GetComponent<tk2dTextMesh>();
            if (tm != null)
            {
                string text = tm.text;
                tk2dFontData font = tm.font;
                string fontName = null;
                if (font != null)
                {
                    fontName = font.name;
                }

                extra += $" text=\"{text}\" font={fontName}";
            }

            var sp = t.GetComponent<tk2dBaseSprite>();
            if (sp != null)
            {
                tk2dSpriteDefinition currentSprite = sp.CurrentSprite;
                string name = null;
                if (currentSprite != null)
                {
                    name = currentSprite.name;
                }

                tk2dSpriteCollectionData collection = sp.Collection;
                string spriteCollectionName = null;
                if (collection != null)
                {
                    spriteCollectionName = collection.spriteCollectionName;
                }

                extra +=
                    $" sprite={name} coll={spriteCollectionName} "
                        + $"color={sp.color}";
            }

            var mb = t.GetComponent<Button>();
            if (mb != null)
            {
                extra +=
                    $" button function={mb.function} localized={mb.localized}";
            }

            Log.LogInfo(
                $"[ui] {new string(' ', depth * 2)}{t.name} "
                    + $"active={t.gameObject.activeSelf} "
                    + $"layer={t.gameObject.layer} pos={t.localPosition} "
                    + $"scale={t.localScale} [{comps}]{extra}");
            foreach (Transform c in t)
            {
                DumpTree(c, depth + 1);
            }
        }
    }
}
