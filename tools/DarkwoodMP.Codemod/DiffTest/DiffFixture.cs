namespace DarkwoodMP.Codemod.DiffTest;

/// The code every differential case is compiled with: a trace that records
/// each operand in evaluation order, and a fake Unity object whose == treats a
/// destroyed object as null. For the rule unity-null the harness defines
/// UNITY_NULL (UnitySymbol): the fake then derives from a stub
/// UnityEngine.Object that declares those operators, as Unity's does, so the
/// rule sees a Unity object and its "x != null" calls that ==. There it also
/// has a method that takes an argument and changes the object, a bool
/// property that logs its read and a delegate property.
static class DiffFixture
{
    public const string UnitySymbol = "UNITY_NULL";

    public const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;
        using System.Reflection;

        #if UNITY_NULL
        namespace UnityEngine
        {
            public class Object
            {
                public bool Dead;

                public static bool operator ==(Object a, Object b)
                {
                    return Same(a, b);
                }

                public static bool operator !=(Object a, Object b)
                {
                    return !Same(a, b);
                }

                public static implicit operator bool(Object a)
                {
                    return !Same(a, null);
                }

                static bool Same(Object a, Object b)
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
                    return Same(this, other as Object);
                }

                public override int GetHashCode()
                {
                    return 0;
                }
            }
        }
        #endif

        namespace DiffFixture
        {
        #if UNITY_NULL
            public sealed class UnityObject : UnityEngine.Object
            {
                public int Count = 3;
                public UnityObject Next;

                public int Hit()
                {
                    Trace.Log("hit", Count);
                    return Count;
                }

                public void Take(int k)
                {
                    Trace.Log("take", k);
                    Count += k;
                }

                public bool Big
                {
                    get
                    {
                        Trace.Log("big", Count);
                        return Count > 15;
                    }
                }

                public Action<int> Heard { get; set; }
            }
        #else
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
        #endif

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
                    return Describe(value, new List<object>());
                }

                /// How many objects, structs and collections deep Describe
                /// follows what they hold.
                const int Deepest = 6;

                /// Path holds what Describe is inside of, so that a cycle
                /// ends and the limit holds.
                static string Describe(object value, List<object> path)
                {
                    if (ReferenceEquals(value, null))
                    {
                        return "null";
                    }
                    if (value is UnityObject unity)
                    {
                        // Only the word: under unity-null the harness reads a
                        // destroyed object as null by it.
                        if (unity.Dead)
                        {
                            return "destroyed";
                        }
                        var fields = new List<KeyValuePair<string, object>>
                        {
                            Part("Count", unity.Count),
                            Part("Next", unity.Next),
                        };
                        return "alive" + Inside(value, path, fields);
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
                        var parts = new List<KeyValuePair<string, object>>();
                        foreach (var item in items)
                        {
                            parts.Add(Part(null, item));
                        }
                        return value.GetType().Name + Inside(value, path, parts);
                    }
                    if (value is System.Runtime.CompilerServices.ITuple tuple)
                    {
                        var parts = new List<KeyValuePair<string, object>>();
                        for (int i = 0; i < tuple.Length; i++)
                        {
                            parts.Add(Part(null, tuple[i]));
                        }
                        return value.GetType().Name + Inside(value, path, parts);
                    }
                    if (WrittenInTheCase(value.GetType()))
                    {
                        return value.GetType().Name
                            + Inside(value, path, FieldsOf(value));
                    }
                    if (SaysWhatItIs(value.GetType()))
                    {
                        return value.GetType().Name + ":" + value.ToString();
                    }
                    return Refuse(value.GetType().FullName);
                }

                static KeyValuePair<string, object> Part(string name, object value)
                {
                    return new KeyValuePair<string, object>(name, value);
                }

                /// "{name=value, ...}", or "{value, ...}" for parts without a
                /// name.
                static string Inside(
                    object owner,
                    List<object> path,
                    List<KeyValuePair<string, object>> parts)
                {
                    foreach (var outer in path)
                    {
                        if (ReferenceEquals(outer, owner))
                        {
                            return "{cycle}";
                        }
                    }
                    if (path.Count == Deepest)
                    {
                        return Refuse(
                            "value nested more than " + Deepest + " levels deep");
                    }
                    path.Add(owner);
                    var texts = new List<string>();
                    foreach (var part in parts)
                    {
                        var text = Describe(part.Value, path);
                        if (part.Key != null)
                        {
                            text = part.Key + "=" + text;
                        }
                        texts.Add(text);
                    }
                    path.RemoveAt(path.Count - 1);
                    return "{" + string.Join(", ", texts) + "}";
                }

                /// A struct or class written in the case, not generated, and
                /// so is every type it derives from apart from object. That
                /// leaves out delegates and enums, whose base types aren't.
                static bool WrittenInTheCase(Type type)
                {
                    if (type.Assembly != typeof(Trace).Assembly
                        || type.Name.StartsWith("<"))
                    {
                        return false;
                    }
                    var t = type.BaseType;
                    while (t != typeof(object) && t != typeof(ValueType))
                    {
                        if (t.Assembly != typeof(Trace).Assembly)
                        {
                            return false;
                        }
                        t = t.BaseType;
                    }
                    return true;
                }

                /// Every instance field, public or not, the base type's first
                /// and each type's in the order they are declared. An
                /// auto-property's goes by the property's name.
                static List<KeyValuePair<string, object>> FieldsOf(object value)
                {
                    var types = new List<Type>();
                    var t = value.GetType();
                    while (t != typeof(object) && t != typeof(ValueType))
                    {
                        types.Insert(0, t);
                        t = t.BaseType;
                    }
                    var flags = BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    var fields = new List<KeyValuePair<string, object>>();
                    foreach (var type in types)
                    {
                        var declared = new List<FieldInfo>(type.GetFields(flags));
                        declared.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                        foreach (var field in declared)
                        {
                            var name = field.Name;
                            if (name.StartsWith("<") && name.EndsWith(">k__BackingField"))
                            {
                                name = name.Substring(1, name.IndexOf('>') - 1);
                            }
                            fields.Add(Part(name, field.GetValue(value)));
                        }
                    }
                    return fields;
                }

                static string Refuse(string what)
                {
                    if (Undescribable == null)
                    {
                        Undescribable = what;
                    }
                    return "undescribable " + what;
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
