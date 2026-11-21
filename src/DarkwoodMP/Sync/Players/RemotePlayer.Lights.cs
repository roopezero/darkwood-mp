using UnityEngine;

namespace DarkwoodMP.Sync.Players
{
    /// RemotePlayer's lights: what it holds that lights it, copied from the
    /// player's own.
    public sealed partial class RemotePlayer
    {
        // ---- what lights them -----------------------------------------------
        // A torch or lamp in hand (its light and flames), or the flashlight's
        // beam.

        private string _heldShown = "";
        private Transform _heldLight, _heldFlames;
        private GameObject _beam;

        /// <summary>
        /// For the tests: the puppet's flashlight beam (null if it never had
        /// one).
        /// </summary>
        internal GameObject Beam => _beam;
        /// <summary>
        /// For the tests: the puppet's lantern light (null when there's none).
        /// </summary>
        internal Light2D LanternLight => _lantern;
        /// <summary>
        /// For the tests: the light of the torch or lamp in the puppet's hand
        /// (null when there's none).
        /// </summary>
        internal Light2D HeldLightShown
        {
            get
            {
                if (_heldLight != null)
                {
                    return _heldLight.GetComponent<Light2D>();
                }

                return null;
            }
        }

        private bool _flamesLogged;

        private void ApplyLights()
        {
            if (Root == null)
            {
                return;
            }

            PuppetFacts puppet = Facts;
            string want = PuppetRules.HeldLight(puppet);
            if (PuppetRules.RedoesHeld(want, _heldShown, _heldLight != null))
            {
                RemoveHeld();
                _heldShown = want;
                if (want != "")
                {
                    try
                    {
                        ItemsDatabase instance =
                            Singleton<ItemsDatabase>.Instance;
                        InvItem item = null;
                        if (instance != null)
                        {
                            item = instance.getItem(want, instantiate: false);
                        }

                        if (item != null && item.lightEmitter != null)
                        {
                            // As the game does for its own player
                            // (Player.onDoneSwitchingItem), on the puppet.
                            _heldLight = Core.AddPrefab(
                                item.lightEmitter, Vector3.zero,
                                Quaternion.Euler(90f, 0f, 0f), Root).transform;
                            if (item._particleEmitter != null)
                            {
                                _heldFlames = Core.AddPrefab(
                                    item._particleEmitter, Vector3.zero,
                                    Quaternion.Euler(90f, 0f, 0f),
                                    Root).transform;
                            }

                            NotSolid(_heldLight);
                            NotSolid(_heldFlames);
                        }
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log.LogWarning(
                            $"Couldn't show {Name}'s {want}: {e.Message}");
                    }
                }
            }

            // The flames go where their game put them: on the torch in hand,
            // which moves with each animation frame and facing
            // (Player.ProcessAnims). The light itself stays at the player's
            // centre, as the game leaves it.
            if (_heldFlames != null)
            {
                _heldFlames.localPosition = _last.HeldFlames;
                if (!_flamesLogged && _last.HeldFlames != Vector3.zero)
                {
                    _flamesLogged = true;
                    Plugin.Log.LogInfo(
                        $"[fx] {Name}'s {_heldShown}: flames on the puppet at "
                            + $"{_heldFlames.localPosition} from its centre "
                            + "(on the item in hand)");
                }
            }

            bool beam = PuppetRules.Beam(puppet);
            if (beam && _beam == null)
            {
                Transform own = null;
                if (Player.Instance != null)
                {
                    own = Player.Instance.transform.Find("Flashlight");
                }

                if (own != null)
                {
                    _beam =
                        CopyLight(own.gameObject, Root.transform, "Flashlight");
                    NotSolid(_beam.transform);
                }
            }

            if (_beam != null && _beam.activeSelf != beam)
            {
                _beam.SetActive(beam);
            }

            // The copy keeps the radius and colour our own beam had when it was
            // copied (the prefab's, if we never used one); their game sets them
            // each frame from where their mouse is (Player.processFlashlight).
            Light2D beamLight = null;
            if (beam && _beam != null)
            {
                beamLight = _beam.GetComponent<Light2D>();
            }

            if (beamLight != null && PuppetRules.SetsBeam(puppet))
            {
                if (beamLight.LightRadius != _last.BeamRadius)
                {
                    beamLight.LightRadius = _last.BeamRadius;
                }

                var c = new Color(
                    _last.BeamBright, _last.BeamBright, _last.BeamBright, 0f);
                if (beamLight.LightColor != c)
                {
                    beamLight.LightColor = c;
                }
            }

            ApplyLantern();
        }

        // A lantern worn on the hotbar: in their game it widens Player.lightDot
        // and makes it a logic light (InvItemClass.checkForActiveSwitches).
        // Here a copy of our own player's light dot on the puppet does the
        // same: it's drawn, and it lights the path nodes around them
        // (Controller.logicLights), so the host's AI and Core.isInLight see the
        // joiner's proxy, which stands where the puppet is, as lit.
        private Light2D _lantern;

        /// <summary>
        /// The radius of the lantern light shown on the puppet (0: none).
        /// </summary>
        public float LanternShown
        {
            get
            {
                if (_lantern != null)
                {
                    return _lantern.LightRadius;
                }

                return 0f;
            }
        }

        private void ApplyLantern()
        {
            float want = PuppetRules.Lantern(Facts);
            if (PuppetRules.RemovesLantern(want))
            {
                RemoveLantern();
                return;
            }

            var ctl = Singleton<Controller>.Instance;
            if (_lantern == null)
            {
                Light2D own = null;
                if (Player.Instance != null)
                {
                    own = Player.Instance.lightDot;
                }

                if (own == null || ctl == null)
                {
                    return;
                }

                var go = CopyLight(
                    own.gameObject, Root.transform, "PuppetLanternDot");
                go.SetActive(true);
                NotSolid(go.transform);
                _lantern = go.GetComponent<Light2D>();
                if (_lantern == null)
                {
                    Object.Destroy(go);
                    return;
                }

                Plugin.Log.LogInfo(
                    $"[fx] {Name}'s lantern: a light of radius {want:0} on "
                        + "their puppet");
            }

            if (_lantern.LightRadius != want)
            {
                _lantern.LightRadius = want;
            }

            _lantern.lightsPlayer = true;
            _lantern.updateGraph = true;
            if (ctl != null && !ctl.logicLights.Contains(_lantern))
            {
                ctl.logicLights.Add(_lantern);
            }
        }

        private void RemoveLantern()
        {
            if (_lantern == null)
            {
                return;
            }

            Unlight(_lantern);
            Object.Destroy(_lantern.gameObject);
            _lantern = null;
        }

        private void RemoveHeld()
        {
            if (_heldLight != null)
            {
                var light = _heldLight.GetComponent<Light2D>();
                if (light != null)
                {
                    Unlight(light);
                }

                Object.Destroy(_heldLight.gameObject);
            }

            if (_heldFlames != null)
            {
                Object.Destroy(_heldFlames.gameObject);
            }

            _heldLight = _heldFlames = null;
            _heldShown = "";
        }

        /// <summary>
        /// A copy of one of our own player's lights (its flashlight beam, its
        /// light dot) for the puppet, with a mesh of its own. Light2D keeps the
        /// mesh it draws in a serialized field (_mesh) and its MeshFilter holds
        /// it too, so Object.Instantiate gave the copy our light's mesh: both
        /// lights then drew their shape, walls' shadows and all, into the one
        /// mesh, and our player's light showed the shape worked out where the
        /// puppet stands (a joiner beside the host's lantern saw the host's
        /// shadows around their own player, and the puppet's lantern showed our
        /// small dot). Destroying the copy also destroyed our light's mesh
        /// (Light2D.OnDestroy). So the copy is made under a switched-off holder
        /// (no Awake or OnEnable yet), its mesh references are cleared, and
        /// only then is it put on the puppet, where it makes its own mesh when
        /// it first draws (Light2D.CreateMeshObject).
        /// </summary>
        internal static GameObject CopyLight(
            GameObject own, Transform parent, string name)
        {
            var holder = new GameObject("DarkwoodMP_LightCopy");
            holder.SetActive(false);
            var go = Object.Instantiate(own, holder.transform, false);
            go.name = name;
            foreach (var l in go.GetComponentsInChildren<Light2D>(true))
            {
                l._mesh = null;
            }

            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
            {
                f.sharedMesh = null;
            }

            go.transform.SetParent(parent, false);
            Object.Destroy(holder);
            return go;
        }

        /// <summary>
        /// Take a puppet's light off the lighting graph and out of the game's
        /// list of lights.
        /// </summary>
        private static void Unlight(Light2D light)
        {
            light.unlightGraphNodes();
            var ctl = Singleton<Controller>.Instance;
            if (ctl != null)
            {
                while (ctl.logicLights.Remove(light))
                {
                }
            }
        }

        /// <summary>
        /// A puppet is only seen: a sprite whose collection has collider data,
        /// or a copied light, may bring a solid collider, and a puppet moved to
        /// where its player is would shove the furniture and dropped items it
        /// walks through in this game only. Their own game decides where those
        /// end up (DragSync), so it's turned off.
        /// </summary>
        private static void NotSolid(Transform t)
        {
            if (t == null)
            {
                return;
            }

            foreach (var c in t.GetComponentsInChildren<Collider>(true))
            {
                if (c != null && !c.isTrigger)
                {
                    c.enabled = false;
                }
            }
        }
    }
}
