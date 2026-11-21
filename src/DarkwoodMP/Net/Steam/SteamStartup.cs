using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Finds the game's Steam once it's up and makes Steam's backend, and
    /// works out the player's name (the setting, Steam's or the OS user's).
    /// Process state, on ModHost: it runs from the main menu on.
    /// </summary>
    internal sealed class SteamStartup
    {
        private readonly ModHost _host;
        // SteamManager.s_instance and its m_bInitialized
        private FieldInfo _steamInstance, _steamInitialized;
        private bool _steamUnavailable;
        private float _nextSteamCheck;

        public SteamStartup(ModHost host)
        {
            _host = host;
        }

        /// <summary>
        /// Steam isn't there at all (GOG), as opposed to not up yet.
        /// </summary>
        public bool Missing => _steamUnavailable;

        /// <summary>
        /// Each frame: look for Steam every second until it's up or missing.
        /// </summary>
        public void Update()
        {
            if (_host.Steam == null && !_steamUnavailable
                && Time.unscaledTime >= _nextSteamCheck)
            {
                _nextSteamCheck = Time.unscaledTime + 1f;
                if (SteamReady())
                {
                    TryCreateSteamBackend();
                }
            }
        }

        /// <summary>
        /// Checks the game's own SteamManager via reflection (it may be missing
        /// or never init on GOG). Only one the game made is looked at:
        /// SteamManager.Initialized makes a SteamManager when there's none yet,
        /// and its Awake quits the game when steam_api can't be loaded (as on a
        /// GOG build that never makes one itself).
        /// </summary>
        private bool SteamReady()
        {
            try
            {
                if (_steamInstance == null)
                {
                    var t = AccessTools.TypeByName("SteamManager");
                    if (t != null)
                    {
                        _steamInstance = t.GetField(
                            "s_instance",
                            BindingFlags.NonPublic | BindingFlags.Public
                                | BindingFlags.Static);
                    }
                    else
                    {
                        _steamInstance = null;
                    }

                    if (t != null)
                    {
                        _steamInitialized = t.GetField(
                            "m_bInitialized",
                            BindingFlags.NonPublic | BindingFlags.Public
                                | BindingFlags.Instance);
                    }
                    else
                    {
                        _steamInitialized = null;
                    }

                    if (_steamInstance == null || _steamInitialized == null)
                    {
                        _steamUnavailable = true;
                        Plugin.Log.LogInfo(
                            "No SteamManager in this build: direct IP only");
                        return false;
                    }
                }

                var manager =
                    _steamInstance.GetValue(null) as UnityEngine.Object;
                return manager != null
                    && (bool)_steamInitialized.GetValue(manager);
            }
            catch
            {
                return false; // Steam not running (GOG)
            }
        }

        private void TryCreateSteamBackend()
        {
            try
            {
                CreateSteamBackend();
            }
            catch (Exception e)
            {
                _steamUnavailable = true;
                Plugin.Log.LogWarning(
                    "Steam networking unavailable, direct IP only: "
                        + $"{e.Message}");
            }
        }

        // Separate, non-inlined method: if Steamworks.NET can't load, only this
        // call fails.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CreateSteamBackend()
        {
            _host.AddSteam(new SteamBackend());
            UpdateLocalName();
            Plugin.Log.LogInfo(
                "Steam ready: Steam lobbies and invites enabled");
        }

        /// <summary>
        /// Your name (General.PlayerName; empty: the Steam or OS user name),
        /// changed in Options.
        /// </summary>
        public void SetPlayerName(string name)
        {
            string stored = name;
            if (stored == null)
            {
                stored = "";
            }

            Plugin.Cfg.PlayerName.Value = stored;
            UpdateLocalName();
        }

        public void UpdateLocalName()
        {
            string name = Plugin.Cfg.PlayerName.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                IGuiSection guiSection = _host.Steam as IGuiSection;
                if (guiSection != null)
                {
                    name = guiSection.SuggestedPlayerName;
                }
                else
                {
                    name = null;
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Environment.UserName;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                _host.Session.LocalName = "Player";
            }
            else
            {
                _host.Session.LocalName = name.Trim();
            }
        }
    }
}
