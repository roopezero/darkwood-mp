namespace DarkwoodMP.Codemod.DiffTest;

/// The code every differential case is compiled with: a trace that records
/// each operand in evaluation order, and a fake Unity object whose == treats a
/// destroyed object as null.
static class DiffFixture
{
    public const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;

        namespace DiffFixture
        {
            public sealed class UnityObject
            {
                public bool Dead;
                public int Count = 3;
                public UnityObject Next;

                public int Hit()
                {
                    Trace.Log("hit", Count);
                    return Count;
                }

                public static bool operator ==(UnityObject a, UnityObject b)
                {
                    return Same(a, b);
                }

                public static bool operator !=(UnityObject a, UnityObject b)
                {
                    return !Same(a, b);
                }

                public static implicit operator bool(UnityObject a)
                {
                    return !Same(a, null);
                }

                static bool Same(UnityObject a, UnityObject b)
                {
                    bool noA = ReferenceEquals(a, null) || a.Dead;
                    bool noB = ReferenceEquals(b, null) || b.Dead;
                    if (noA || noB)
                    {
                        return noA && noB;
                    }
                    return ReferenceEquals(a, b);
                }

                public override bool Equals(object other)
                {
                    return Same(this, other as UnityObject);
                }

                public override int GetHashCode()
                {
                    return 0;
                }
            }

            /// Marks a Unity object parameter that the code passes on without
            /// checking it, as b in "a ?? b". The unity-null rule mustn't treat
            /// it as null, so the harness expects the original's behaviour for
            /// it when it is destroyed, instead of the behaviour for null.
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class PassesThroughAttribute : Attribute
            {
            }

            public static class Trace
            {
                public static List<string> Entries = new List<string>();

                /// The type of the first value since Reset that Describe
                /// couldn't show, or null. The harness stops on it, because
                /// a difference in such a value would go unseen.
                public static string Undescribable;

                public static void Reset()
                {
                    Entries = new List<string>();
                    Undescribable = null;
                }

                public static T Log<T>(string name, T value)
                {
                    Entries.Add(name + "=" + Describe(value));
                    return value;
                }

                public static string Describe(object value)
                {
                    if (ReferenceEquals(value, null))
                    {
                        return "null";
                    }
                    if (value is UnityObject unity)
                    {
                        if (unity.Dead)
                        {
                            return "destroyed";
                        }
                        return "alive";
                    }
                    if (value is bool flag)
                    {
                        if (flag)
                        {
                            return "true";
                        }
                        return "false";
                    }
                    if (value is string text)
                    {
                        return "\"" + text + "\"";
                    }
                    // Numbers and enums.
                    if (value is IFormattable number)
                    {
                        return value.GetType().Name + ":"
                            + number.ToString(null, CultureInfo.InvariantCulture);
                    }
                    if (value is char letter)
                    {
                        return "Char:" + letter;
                    }
                    // A collection lists its elements. A lazy sequence isn't
                    // run here: that would add effects to the Log call.
                    if (value is System.Collections.ICollection items)
                    {
                        var parts = new List<string>();
                        foreach (var item in items)
                        {
                            parts.Add(Describe(item));
                        }
                        return value.GetType().Name
                            + "{" + string.Join(", ", parts) + "}";
                    }
                    if (value is System.Runtime.CompilerServices.ITuple tuple)
                    {
                        var parts = new List<string>();
                        for (int i = 0; i < tuple.Length; i++)
                        {
                            parts.Add(Describe(tuple[i]));
                        }
                        return value.GetType().Name
                            + "(" + string.Join(", ", parts) + ")";
                    }
                    if (SaysWhatItIs(value.GetType()))
                    {
                        return value.GetType().Name + ":" + value.ToString();
                    }
                    if (Undescribable == null)
                    {
                        Undescribable = value.GetType().FullName;
                    }
                    return "undescribable " + value.GetType().Name;
                }

                /// A type with a ToString of its own, apart from a task's:
                /// a pending ValueTask prints as an empty string. A delegate,
                /// a lazy sequence, a Task and a type that keeps object's
                /// ToString print only their type's name.
                static bool SaysWhatItIs(Type type)
                {
                    if (type.Namespace == "System.Threading.Tasks")
                    {
                        return false;
                    }
                    var method = type.GetMethod("ToString", Type.EmptyTypes);
                    return method.DeclaringType != typeof(object)
                        && method.DeclaringType != typeof(ValueType);
                }
            }
        }
        """;

    /// A case's method inside the class the harness runs.
    public static string Wrap(string method)
    {
        var indented = string.Join(
            "\n",
            method.TrimEnd().Split('\n').Select(line => "        " + line));
        return "using System;\n"
            + "using System.Collections.Generic;\n"
            + "using static DiffFixture.Trace;\n\n"
            + "namespace DiffFixture\n{\n"
            + "    public static class Case\n    {\n"
            + indented + "\n    }\n}\n";
    }
}
