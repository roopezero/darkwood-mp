using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A "journal updated" popup the game has no text for isn't shown
    /// (JournalPopups). Also in a game played alone.
    /// </summary>
    [HarmonyPatch(
        typeof(Journal), nameof(Journal.showJournalInfoPopup),
        new[] { typeof(string), typeof(string) })]
    static class JournalShowJournalInfoPopup
    {
        static bool Prefix(string _type, string _desc)
        {
            return JournalPopups.HasText(_type, _desc);
        }
    }

    /// <summary>
    /// A scripted event's reputation step runs in every game, so its change
    /// isn't sent (FlagsSync.ReputationBefore, StepRan). Patches the body of
    /// the GameEvent.fire coroutine.
    /// </summary>
    [HarmonyPatch]
    static class GameEventFireReputation
    {
        private static System.Reflection.FieldInfo _this;

        static System.Reflection.MethodBase TargetMethod()
        {
            var mn = AccessTools.EnumeratorMoveNext(
                AccessTools.Method(typeof(GameEvent), nameof(GameEvent.fire)));
            if (mn != null)
            {
                _this = AccessTools.Field(mn.DeclaringType, "<>4__this");
            }

            return mn;
        }

        static void Prefix(object __instance, out int __state)
        {
            __state = int.MinValue;
            CoopSession coop = Coop.Current;
            if (_this == null || coop == null)
            {
                return;
            }

            var ev = _this.GetValue(__instance) as GameEvent;
            __state = coop.Story.ReputationBefore(ev);
        }

        static void Postfix(object __instance, int __state)
        {
            CoopSession coop = Coop.Current;
            if (__state == int.MinValue || coop == null)
            {
                return;
            }

            coop.Story.StepRan((GameEvent)_this.GetValue(__instance), __state);
        }
    }
}
