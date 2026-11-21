using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The game's skill icons (tk2d sprites "skill_NAME" in the UISkill
    /// prefab's collection), drawn in IMGUI.
    /// </summary>
    internal static class SkillIcons
    {
        private static tk2dSpriteCollectionData _coll;
        private static bool _tried;
        private static readonly Dictionary<string, int> _ids =
            new Dictionary<string, int>();

        public static bool Draw(Rect r, string skill, Color tint)
        {
            if (!_tried)
            {
                _tried = true;
                try
                {
                    var go = Resources.Load("Prefabs/UI/UISkill") as GameObject;
                    tk2dSprite spr = null;
                    if (go != null)
                    {
                        spr = go.GetComponent<tk2dSprite>();
                    }

                    if (spr != null && spr.Collection != null)
                    {
                        _coll = spr.Collection.inst;
                    }
                    else
                    {
                        _coll = null;
                    }

                    if (_coll == null)
                    {
                        Plugin.Log.LogWarning(
                            "[skills] no skill icons (UISkill's sprites): "
                                + "names only");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[skills] no skill icons: {e.Message}");
                }
            }

            if (_coll == null)
            {
                return false;
            }

            if (!_ids.TryGetValue(skill, out int id))
            {
                id = _coll.GetSpriteIdByName("skill_" + skill, -1);
                _ids[skill] = id;
                if (id < 0)
                {
                    Plugin.Log.LogInfo($"[skills] no icon for {skill}");
                }
                else
                {
                    var d0 = _coll.spriteDefinitions[id];
                    string corners = string.Join(
                        " ",
                        Array.ConvertAll(
                            d0.uvs, u => $"({u.x:0.###},{u.y:0.###})"));
                    Plugin.Log.LogInfo(
                        $"[skills] icon for {skill}: flipped {d0.flipped}, uvs "
                            + $"{corners}");
                }
            }

            if (id < 0 || Event.current.type != EventType.Repaint)
            {
                return id >= 0;
            }

            var def = _coll.spriteDefinitions[id];
            Material mat;
            if (def.materialInst != null)
            {
                mat = def.materialInst;
            }
            else
            {
                mat = def.material;
            }

            Texture tex = null;
            if (mat != null)
            {
                tex = mat.mainTexture;
            }

            if (tex == null || def.uvs == null || def.uvs.Length < 4)
            {
                return false;
            }

            float u0 = float.MaxValue,
                v0 = float.MaxValue,
                u1 = float.MinValue,
                v1 = float.MinValue;
            foreach (var uv in def.uvs)
            {
                u0 = Mathf.Min(u0, uv.x);
                v0 = Mathf.Min(v0, uv.y);
                u1 = Mathf.Max(u1, uv.x);
                v1 = Mathf.Max(v1, uv.y);
            }

            var old = GUI.color;
            GUI.color = tint * old;
            if (def.flipped == tk2dSpriteDefinition.FlipMode.None)
            {
                GUI.DrawTextureWithTexCoords(
                    r, tex, new Rect(u0, v0, u1 - u0, v1 - v0), true);
            }
            else
            {
                // Packed turned a quarter: draw it turned back about the icon's
                // centre.
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(-90f, r.center);
                GUI.DrawTextureWithTexCoords(
                    r, tex, new Rect(u0, v0, u1 - u0, v1 - v0), true);
                GUI.matrix = m;
            }

            GUI.color = old;
            return true;
        }
    }
}
