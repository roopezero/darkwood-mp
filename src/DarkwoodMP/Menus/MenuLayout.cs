using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// The main menu and the pause menu laid out as Half-Life's and
    /// Counter-Strike 1.6's: the logo in the top left, the entries in a column
    /// in the lower left (MenuList, from the same left edge) and the versions
    /// in the bottom right corner. The game places the logo and the versions
    /// with PositionMe (from the screen's middle and an offset); this moves
    /// them after it, in screen pixels through the UI camera, so it fits any
    /// screen shape.
    /// </summary>
    public sealed class MenuLayout
    {
        /// The game placed one of the menu's things (PositionMe): the menu is
        /// laid out again at once, so it never shows in the game's place.
        internal void Placed(PositionMe placed)
        {
            var mm = Singleton<MainMenu>.Instance;
            if (mm == null || mm.Menu0 == null
                || !placed.transform.IsChildOf(mm.Menu0.transform))
            {
                return;
            }

            Apply(mm);
        }

        // the logo's left edge (and MenuList's), as a share of the screen's
        // width the logo's word's top below the screen's top, as a share of its
        // height the versions' distance from the right and bottom edges, as a
        // share of the height
        internal const float Left = 0.065f;
        private const float LogoTop = 0.12f;
        private const float Corner = 0.016f;

        private bool _busy;

        public void Apply(MainMenu mm)
        {
            if (_busy || mm == null || mm.Menu0 == null
                || !mm.Menu0.activeInHierarchy)
            {
                return;
            }

            Camera cam = null;
            if (Core.CamUI != null)
            {
                cam = Core.CamUI.GetComponent<Camera>();
            }

            if (cam == null)
            {
                return;
            }

            _busy = true;
            try
            {
                Lay(mm, cam);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't lay out the menu: {e.Message}");
            }
            finally
            {
                _busy = false;
            }
        }

        private void Lay(MainMenu mm, Camera cam)
        {
            float W = Screen.width, H = Screen.height;
            float left = W * Left;
            // The menu's vignette (Background/Overlay) darkens the screen's
            // edges and is drawn over the logo, which the game keeps in the
            // middle. In the corner it'd be dimmed, so it's brought to the
            // version line's depth, in front of it (the UI camera is
            // orthographic and looks along -Y, so depth doesn't move it on the
            // screen).
            float? front;
            if (mm.CurrentVersion != null && cam.orthographic)
            {
                front = mm.CurrentVersion.transform.position.y;
            }
            else
            {
                front = (float?)null;
            }

            // The logo: top left, its left edge on the column's.
            var logo = mm.Menu0.transform.Find("Darkwood");
            Renderer logoR = null;
            if (logo != null)
            {
                logoR = logo.GetComponent<Renderer>();
            }

            if (logoR != null)
            {
                var rect = ScreenRect(cam, logoR.bounds);
                // The picture has a wide clear margin round the word (its
                // glow): the word starts 23% in from its left and 35% down from
                // its top (measured: 810 x 336 pixels, the word 447 x 105, at
                // 1080 lines).
                float wordLeft = rect.xMin + rect.width * 0.232f,
                    wordTop = rect.yMax - rect.height * 0.348f;
                MoveBy(cam, logo, left - wordLeft, (H - H * LogoTop) - wordTop);
                ToFront(logo, front);
            }

            // The versions: right-aligned in the bottom right corner, the
            // player ID line above them.
            float margin = H * Corner, bottom = margin;
            Transform idLine = mm.Menu0.transform.Find("playerID");
            tk2dTextMesh idText = null;
            if (idLine != null)
            {
                idText = idLine.GetComponent<tk2dTextMesh>();
            }

            foreach (var t in new[] { mm.CurrentVersion, idText })
            {
                if (t == null || !t.gameObject.activeInHierarchy
                    || string.IsNullOrEmpty(t.text))
                {
                    continue;
                }

                if (t.anchor != TextAnchor.LowerRight)
                {
                    t.anchor = TextAnchor.LowerRight;
                    t.Commit();
                }

                var r = t.GetComponent<Renderer>();
                if (r == null)
                {
                    continue;
                }

                var rect = ScreenRect(cam, r.bounds);
                if (rect.height < 1f)
                {
                    continue;
                }

                MoveBy(
                    cam, t.transform, (W - margin) - rect.xMax,
                    bottom - rect.yMin);
                bottom += rect.height + margin * 0.5f;
            }
        }

        private void ToFront(Transform t, float? y)
        {
            if (y == null || Mathf.Abs(t.position.y - y.Value) < 0.01f)
            {
                return;
            }

            var p = t.position;
            t.position = new Vector3(p.x, y.Value, p.z);
        }

        /// <summary>
        /// Move a thing by this many screen pixels (whole pixels, so the bitmap
        /// font stays sharp).
        /// </summary>
        private void MoveBy(Camera cam, Transform t, float dx, float dy)
        {
            if (Mathf.Abs(dx) < 0.5f && Mathf.Abs(dy) < 0.5f)
            {
                return;
            }

            var at = cam.WorldToScreenPoint(t.position);
            t.position = cam.ScreenToWorldPoint(
                new Vector3(
                    at.x + Mathf.Round(dx), at.y + Mathf.Round(dy), at.z));
        }

        /// <summary>
        /// A renderer's bounds as a rectangle in screen pixels (from the bottom
        /// left).
        /// </summary>
        private Rect ScreenRect(Camera cam, Bounds b)
        {
            Vector3 a = cam.WorldToScreenPoint(b.min),
                c = cam.WorldToScreenPoint(b.max);
            return Rect.MinMaxRect(
                Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x),
                Mathf.Max(a.y, c.y));
        }
    }
}
