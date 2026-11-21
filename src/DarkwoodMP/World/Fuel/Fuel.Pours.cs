using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Fuel
{
    /// <summary>
    /// The pourer's side of Fuel: what the local player poured or cut, the
    /// Request that asks the host to add it, and the fuel given back to the
    /// can when the host takes less.
    /// </summary>
    internal sealed partial class Fuel
    {
        /// <summary>
        /// What the local player did to one tank since the last send: fuel
        /// added (or a saw's cut), and the can it came from.
        /// </summary>
        private sealed class Pour
        {
            public float Amount, Drained;
            public InvItemClass Can;
        }

        /// <summary>
        /// The local player changed this generator's or saw's fuel by delta (a
        /// pour, or a log cut). Each pour drains the can in the player's hand
        /// by one (Player.waitToSpillLiquid); a refusal gives that back.
        /// </summary>
        internal void Poured(Component c, float delta)
        {
            var guards = Coop.Guards;
            if (!PourRules.Records(guards.Sharing, guards.Replaying, delta))
            {
                return;
            }

            var key = (c, PourRules.IsCut(delta));
            if (!_pending.TryGetValue(key, out var p))
            {
                _pending[key] = p = new Pour();
            }

            p.Amount += delta;
            InvItemClass can = null;
            if (Player.Instance != null)
            {
                can = Player.Instance.currentItem;
            }

            bool holds = !InvItemClass.isNull(can);
            string type = null;
            if (holds)
            {
                type = can.type;
            }

            if (PourRules.FromCan(delta, holds, type))
            {
                p.Can = can;
                p.Drained += 1f;
            }
        }

        /// <summary>
        /// What a gasoline can holds: the most one pour can add.
        /// </summary>
        private static float CanHolds()
        {
            float most = 0f;
            try
            {
                ItemsDatabase instance = Singleton<ItemsDatabase>.Instance;
                InvItem it = null;
                if (instance != null)
                {
                    it = instance.getItem("gasoline", instantiate: false);
                }

                if (it != null)
                {
                    most = it.maxDurability;
                }
            }
            catch (System.Exception)
            {
                most = 0f;
            }

            return FuelRules.CanHolds(most);
        }

        /// <summary>
        /// Client: ask the host to add what our player poured (or used, cutting
        /// a log).
        /// </summary>
        private void Request(Component c, Pour p)
        {
            var r = ObjRef.Of(c.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var w = Net.Begin(MsgType.Fuel);
            FuelMessage.Write(w, new TankFuel
            {
                Tank = ObjRef.NameOf(r),
                Kind = TankOf(c),
                Value = p.Amount,
            });

            string name = c.name;
            Coop.Current.Authority.Request(
                w, $"fuel {p.Amount:+0.0;-0.0} for {name}",
                (ok, reason, result) =>
            {
                // Refused: all of it back to the can. Allowed, but only part of
                // it fitted: the rest back.
                float back = PourRules.Back(new RefundFacts
                {
                    Allowed = ok,
                    Amount = p.Amount,
                    Drained = p.Drained,
                    Result = result,
                });

                if (PourRules.Refunds(back))
                {
                    Refund(p.Can, back, name);
                }
            }, inventory: false);

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] asked to add fuel {p.Amount:0.0} to {name} "
                        + $"({r})");
            }
        }

        /// <summary>
        /// Put fuel back in the can it came from (or a can, if that one was
        /// used up by the pour).
        /// </summary>
        private static void Refund(InvItemClass can, float amount, string to)
        {
            var player = Player.Instance;
            try
            {
                int left = 0;
                if (can != null)
                {
                    left = can.amount;
                }

                var kind = PourRules.RefundOf(
                    player != null, can != null, left);
                if (kind == RefundKind.Nothing)
                {
                    if (player != null)
                    {
                        Plugin.Log.LogInfo(
                            $"[authority] {amount:0.0} fuel for {to} wasn't "
                                + "from a can: nothing to give back");
                    }

                    return;
                }

                if (kind == RefundKind.NewCan)
                {
                    // InvItemClass.drainDurability removed the emptied can:
                    // give a can back instead.
                    player.Inventory.addItemTypeToPlayer(
                        "gasoline", 1, dropIfNoRoom: true);
                    Plugin.Log.LogInfo(
                        "[authority] gave back a gasoline can for the fuel "
                            + $"{to} didn't take");
                    return;
                }

                float max = float.MaxValue;
                if (can.baseClass != null)
                {
                    max = can.baseClass.maxDurability;
                }

                can.durability = PourRules.Refilled(
                    can.durability, amount, max);
                can.refresh();
                Plugin.Log.LogInfo(
                    $"[authority] gave back {amount:0.0} fuel to the gasoline "
                        + $"can ({to} didn't take it)");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[authority] couldn't give the fuel back: {e.Message}");
            }
        }
    }
}
