using System;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// StartGameWindow's options: the new game's and hosting's, and the
    /// controls they are drawn with.
    public sealed partial class StartGameWindow
    {
        private void DrawNewGame(Flow f, bool on)
        {
            // For a save: what it was started with, greyed out.
            Slot chosen = Chosen;
            GameProfile p = null;
            if (chosen != null)
            {
                p = chosen.Profile;
            }

            GameProfile.Difficulty diff;
            if (on || p == null)
            {
                diff = _difficulty;
            }
            else
            {
                diff = p.difficulty;
            }

            bool skip;
            if (on || p == null)
            {
                skip = _skipPrologue;
            }
            else
            {
                skip = p.skippedPrologue;
            }

            bool backer;
            if (on || p == null)
            {
                backer = _backer;
            }
            else
            {
                backer = p.backerOnlyContent;
            }

            Fade(on, () =>
            {
                var c = f.Row(Ui.RowH, 90, 0, 0, 0);
                Ui.Label(c[0], "Difficulty");
                foreach (var (i, d) in new[]
                {
                    (1, GameProfile.Difficulty.normal),
                    (2, GameProfile.Difficulty.hard),
                    (3, GameProfile.Difficulty.nightmare)
                })
                {
                    if (Choice(c[i], Difficulty(d), diff == d, on))
                    {
                        _difficulty = d;
                    }
                }

                string about =
                    "Dying costs you some of what you carried, and the game "
                        + "goes on.";
                if (diff == GameProfile.Difficulty.hard)
                {
                    about = "A few lives; when they're gone, so is the save.";
                }
                else if (diff == GameProfile.Difficulty.nightmare)
                {
                    about = "One life: dying deletes the save.";
                }

                Ui.Text(f, about, MenuSkin.Dim);
                var k = f.Row(Ui.RowH, 0, 0);
                bool ns = Check(k[0], skip, "Skip the prologue", on);
                bool nb = Check(k[1], backer, "Backer content", on);
                if (on)
                {
                    _skipPrologue = ns;
                    _backer = nb;
                }
            });
        }

        private void DrawHost(Flow f, bool on)
        {
            var plugin = Plugin.Instance;
            var cfg = Plugin.Cfg;
            bool steamUp = plugin.SteamAvailable;
            bool steam =
                HostingRules.HostsWithSteam(steamUp, cfg.HostWith.Value);
            Fade(on, () =>
            {
                var c = f.Row(Ui.RowH, 90, 0, 0);
                Ui.Label(c[0], "Connection");
                if (Choice(c[1], "Steam", steam, on && steamUp))
                {
                    cfg.HostWith.Value = "steam";
                }

                if (Choice(c[2], "Direct", !steam, on))
                {
                    cfg.HostWith.Value = "direct";
                }

                if (!steamUp)
                {
                    string text = "Waiting for Steam...";
                    if (plugin.SteamMissing)
                    {
                        text =
                            "Steam isn't running (the GOG version, say): "
                                + "Direct only.";
                    }

                    Ui.Text(f, text, MenuSkin.Dim);
                }

                var n = f.Row(Ui.RowH, 90, 0);
                Ui.Label(n[0], "Your name");
                string name = Ui.Field(n[1], plugin.Host.Session.LocalName, 32);
                string local = plugin.Host.Session.LocalName;
                if (NameRules.AppliesTypedName(on, name, local))
                {
                    plugin.SetPlayerName(name);
                }

                Fade(on && !steam, () =>
                {
                    cfg.ServerName.Value =
                        Ui.Field(f, "Server name", cfg.ServerName.Value, 48);
                    var pm = f.Row(Ui.RowH, 90, 70, 0, 90, 24, 30, 24);
                    Ui.Label(pm[0], "Port");
                    string port =
                        Ui.Field(pm[1], cfg.DirectPort.Value.ToString(), 5);
                    cfg.DirectPort.Value =
                        HostingRules.TypedPort(port, cfg.DirectPort.Value);

                    Ui.Label(
                        pm[3], "Max players", null, TextAnchor.MiddleRight);
                    int most = cfg.MaxPlayers.Value;
                    bool fewer = HostingRules.MayRemovePlayer(most);
                    if (Ui.Button(pm[4], "-", on && !steam && fewer))
                    {
                        cfg.MaxPlayers.Value--;
                    }

                    Ui.Label(
                        pm[5], cfg.MaxPlayers.Value.ToString(), MenuSkin.Bright,
                        TextAnchor.MiddleCenter);
                    bool more = HostingRules.MayAddPlayer(cfg.MaxPlayers.Value);
                    if (Ui.Button(pm[6], "+", on && !steam && more))
                    {
                        cfg.MaxPlayers.Value++;
                    }

                    cfg.Password.Value = Ui.Field(
                        f, "Password", cfg.Password.Value, 32, password: true);
                    cfg.OpenPort.Value = Check(
                        f.Next(Ui.RowH), cfg.OpenPort.Value,
                        "Open the port on my router (UPnP)", on && !steam);
                });

                var ch = f.Row(Ui.RowH, 90, 0, 0);
                Ui.Label(ch[0], "New players");
                bool copy = cfg.NewCharacters.Value == "copy";
                if (Choice(ch[1], "Start fresh", !copy, on))
                {
                    cfg.NewCharacters.Value = "fresh";
                }

                if (Choice(ch[2], "Copy of you", copy, on))
                {
                    cfg.NewCharacters.Value = "copy";
                }

                string hint;
                if (steam)
                {
                    hint =
                        "Friends join by your Steam invite, or Join Game in "
                            + "the Steam friends list. Hosting starts once "
                            + "your world has loaded.";
                }
                else
                {
                    hint = DirectPanel.PortHint(
                        cfg.DirectPort.Value,
                        cfg.OpenPort.Value)
                        + " Hosting starts once your world has loaded.";
                }

                Ui.Text(f, hint, MenuSkin.Dim);
            });
        }

        /// <summary>
        /// Draws a part greyed out and taking no input when
        /// <paramref name="on"/> is false.
        /// </summary>
        private static void Fade(bool on, Action draw)
        {
            bool enabled = GUI.enabled;
            var color = GUI.color;
            if (!on)
            {
                GUI.enabled = false;
                GUI.color =
                    new Color(color.r, color.g, color.b, color.a * 0.45f);
            }

            try
            {
                draw();
            }
            finally
            {
                GUI.enabled = enabled;
                GUI.color = color;
            }
        }

        /// <summary>
        /// One of a row of choices: the chosen one pressed in.
        /// </summary>
        private static bool Choice(
            Rect r, string text, bool chosen, bool enabled)
        {
            if (!chosen)
            {
                return Ui.Button(r, text, enabled && GUI.enabled);
            }

            GUI.Box(r, GUIContent.none, MenuSkin.Pressed);
            PixelFont.Draw(
                new Rect(r.x + S, r.y + S, r.width, r.height), text,
                MenuSkin.Bright, TextAnchor.MiddleCenter);
            return false;
        }

        private static bool Check(Rect r, bool on, string text, bool enabled) =>
            Ui.Check(r, on, text, enabled && GUI.enabled);
    }
}
