using System;
using System.Collections;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>The saw's steps (ui-saw-*).</summary>
    public static partial class MapScenarioUi
    {
        // ---- the saw --------------------------------------------------------

        private static Saw _saw;

        /// <summary>
        /// "ui-saw-open NAME": open the saw's window as the player does
        /// (Item.openInventory) and leave it open.
        /// </summary>
        private static IEnumerator SawOpen(string[] a, Result r)
        {
            var saw = MapScenarioWorld.Find<Saw>(a[0]);
            if (saw == null)
            {
                r.Fail($"no saw {a[0]}");
                yield break;
            }

            var item = saw.GetComponent<Item>();
            var inv = saw.GetComponent<Inventory>();
            if (item == null || inv == null)
            {
                r.Fail($"{saw.name} has no Item or Inventory");
                yield break;
            }

            yield return Walk(saw.transform.position);
            AsPlayer(() => item.openInventory());
            yield return new WaitForSeconds(1.5f);
            if (!inv.open)
            {
                r.Fail($"{saw.name}'s window didn't open");
                yield break;
            }

            _saw = saw;
            r.Info =
                $"{saw.name} {MapScenarioWorld.Ref(saw)} fuel {saw.fuel:0.0}, "
                    + $"label '{Label(inv)}'";
        }

        private static string Label(Inventory inv)
        {
            if (inv.thisLabel != null && inv.thisLabel.rightText != null)
            {
                return inv.thisLabel.rightText.text;
            }

            return null;
        }

        /// <summary>"ui-saw-check [MINFUEL]": with no input from us, the open
        /// saw's "Fuel: N" label shows its fuel (within 4 s), which is at least
        /// MINFUEL, and its Convert button is enabled exactly when it can cut a
        /// log.</summary>
        private static IEnumerator SawCheck(string[] a, Result r)
        {
            var saw = _saw;
            Inventory inv = null;
            if (saw != null)
            {
                inv = saw.GetComponent<Inventory>();
            }

            if (inv == null || !inv.open)
            {
                r.Fail("the saw's window isn't open here");
                yield break;
            }

            float min = 0f;
            if (a.Length > 0)
            {
                min = F(a[0]);
            }

            string Want() => Language.Get("Fuel", "UI") + ": " + saw.fuel;
            float until = Time.realtimeSinceStartup + 4f;
            while ((saw.fuel < min || Label(inv) != Want())
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            global::Button btn = null;
            if (saw.convertFuelBtn != null)
            {
                btn = saw.convertFuelBtn.GetComponent<global::Button>();
            }

            bool enabled = btn != null && btn.background != null
                && btn.background.activeSelf;
            bool can = saw.canConvert();
            r.Info =
                $"fuel {saw.fuel:0.0}, label '{Label(inv)}', convert "
                    + $"enabled={enabled} can={can}";
            if (saw.fuel < min)
            {
                r.Fail($"{r.Info}: the fuel didn't arrive (wanted {min:0})");
            }
            else if (Label(inv) != Want())
            {
                r.Fail($"{r.Info}: the label is stale (should be '{Want()}')");
            }
            else if (btn != null && enabled != can)
            {
                r.Fail($"{r.Info}: the Convert button is drawn stale");
            }
        }

        private static IEnumerator SawClose(string[] a, Result r)
        {
            try
            {
                P.closeInventory();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[map] ui-saw-close: {e.Message}");
            }

            yield return new WaitForSeconds(0.8f);
            Inventory inv = null;
            if (_saw != null)
            {
                inv = _saw.GetComponent<Inventory>();
            }

            if (inv != null && inv.open)
            {
                r.Fail("still open");
            }

            r.Info = "closed";
        }
    }
}
