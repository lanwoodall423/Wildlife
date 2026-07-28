using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Herds
{
    internal sealed class WildlifeBridgeHotCommand
    {
        public string name;
        public string description;
        public bool allowMutation;
        public readonly List<XElement> steps = new List<XElement>();
    }

    internal static class WildlifeBridgeHotReload
    {
        private const string ModuleFile = "Wildlife-Bridge-HotCommands.xml";
        private static Dictionary<string, WildlifeBridgeHotCommand> commands =
            new Dictionary<string, WildlifeBridgeHotCommand>(StringComparer.OrdinalIgnoreCase);
        private static int generation;
        private static DateTime loadedAtUtc;
        private static string loadedVersion = "none";
        private static string lastError = "";

        public static string ModulePath =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, ModuleFile);
        public static string ModuleFileName => ModuleFile;
        public static int Generation => generation;
        public static int CommandCount => commands.Count;
        public static string LastError => lastError;
        public static IEnumerable<string> CommandNames => commands.Keys.OrderBy(value => value);
        public static bool CanObserve(string name) =>
            commands.TryGetValue(name ?? "", out WildlifeBridgeHotCommand command) &&
            !command.allowMutation;

        public static void Initialize()
        {
            EnsureTemplate();
            Reload();
        }

        public static List<string> Reload()
        {
            try
            {
                XDocument document = XDocument.Load(ModulePath, LoadOptions.None);
                XElement root = document.Root;
                if (root == null || root.Name.LocalName != "WildlifeBridgeModules")
                    throw new InvalidDataException("Root element must be WildlifeBridgeModules.");
                Dictionary<string, WildlifeBridgeHotCommand> replacement =
                    new Dictionary<string, WildlifeBridgeHotCommand>(StringComparer.OrdinalIgnoreCase);
                foreach (XElement element in root.Elements("Command"))
                {
                    string name = ((string)element.Attribute("name") ?? "").Trim().ToUpperInvariant();
                    if (!ValidCommandName(name))
                        throw new InvalidDataException("Invalid hot command name: " + name);
                    WildlifeBridgeHotCommand command = new WildlifeBridgeHotCommand
                    {
                        name = name,
                        description = ((string)element.Attribute("description") ?? "").Trim(),
                        allowMutation = ParseBool((string)element.Attribute("allowMutation"))
                    };
                    foreach (XElement step in element.Elements())
                    {
                        string kind = step.Name.LocalName;
                        if (kind != "Text" && kind != "Builtin" && kind != "Query" &&
                            kind != "Invoke")
                            throw new InvalidDataException("Unsupported step " + kind +
                                " in " + name + ".");
                        if (kind == "Invoke" && !command.allowMutation)
                            throw new InvalidDataException(name +
                                " contains Invoke but allowMutation is not true.");
                        command.steps.Add(new XElement(step));
                    }
                    replacement.Add(name, command);
                }
                commands = replacement;
                loadedVersion = (string)root.Attribute("version") ?? "unspecified";
                loadedAtUtc = DateTime.UtcNow;
                generation++;
                lastError = "";
                return StatusLines("reloaded");
            }
            catch (Exception exception)
            {
                lastError = exception.GetBaseException().Message;
                return new List<string>
                {
                    "hotReload=failed",
                    "generation=" + generation,
                    "commandsRetained=" + commands.Count,
                    "error=" + Clean(lastError)
                };
            }
        }

        public static bool TryExecute(string name, string argument, Map map,
            Func<string, string, Map, List<string>> builtinExecutor, out List<string> lines)
        {
            lines = null;
            if (!commands.TryGetValue(name, out WildlifeBridgeHotCommand command)) return false;
            lines = new List<string>
            {
                "hotCommand=" + command.name,
                "generation=" + generation
            };
            for (int i = 0; i < command.steps.Count; i++)
            {
                XElement step = command.steps[i];
                string kind = step.Name.LocalName;
                if (kind == "Text")
                {
                    lines.Add(Expand((string)step.Attribute("value") ?? step.Value, argument));
                }
                else if (kind == "Builtin")
                {
                    string builtin = Expand((string)step.Attribute("name"), argument)
                        .ToUpperInvariant();
                    string stepArgument = Expand((string)step.Attribute("argument"), argument);
                    List<string> result = builtinExecutor?.Invoke(builtin, stepArgument, map);
                    if (result == null)
                        lines.Add("builtin=" + builtin + " unsupported");
                    else
                        lines.AddRange(result);
                }
                else if (kind == "Query")
                {
                    lines.AddRange(ExecuteQuery(step, argument, map));
                }
                else if (kind == "Invoke")
                {
                    lines.AddRange(ExecuteInvoke(step, argument, map));
                }
            }
            return true;
        }

        public static List<string> StatusLines(string state = "ready")
        {
            return new List<string>
            {
                "hotReload=" + state,
                "generation=" + generation,
                "version=" + loadedVersion,
                "commands=" + commands.Count,
                "loadedUtc=" + (loadedAtUtc == default(DateTime)
                    ? "never" : loadedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                "module=" + ModulePath.Replace('\\', '/'),
                "lastError=" + (lastError.NullOrEmpty() ? "none" : Clean(lastError))
            };
        }

        public static bool SelfTest(Map map)
        {
            try
            {
                XElement query = XElement.Parse(
                    "<Query source=\"animals\" where=\"RaceProps.Animal=true\" " +
                    "aggregate=\"count\" prefix=\"animals\" />");
                List<string> queryLines = ExecuteQuery(query, "", map);
                return queryLines.Count == 1 && queryLines[0].StartsWith("animals=") &&
                    Expand("$argument", "ok") == "ok" &&
                    Compare(3, ">", "2") && Compare(null, "=", "null") &&
                    ResolvePath(map, "mapPawns") != null &&
                    ResolveSource("component:Herds.HerdMapComponent/AllHerds", map) != null;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> ExecuteQuery(XElement step, string argument, Map map)
        {
            string sourceName = Expand((string)step.Attribute("source"), argument);
            List<object> source = ResolveSource(sourceName, map).ToList();
            string where = Expand((string)step.Attribute("where"), argument);
            if (!where.NullOrEmpty())
            {
                string[] filters = where.Split(new[] { "&&" },
                    StringSplitOptions.RemoveEmptyEntries);
                source = source.Where(item => filters.All(filter =>
                    Matches(item, filter.Trim()))).ToList();
            }

            string sortBy = (string)step.Attribute("sortBy");
            if (!sortBy.NullOrEmpty())
            {
                Func<object, string> key = item => FormatValue(ResolvePath(item, sortBy));
                source = ParseBool((string)step.Attribute("descending"))
                    ? source.OrderByDescending(key).ToList()
                    : source.OrderBy(key).ToList();
            }

            int limit = ParseInt((string)step.Attribute("limit"), 20, 1, 200);
            string prefix = Expand((string)step.Attribute("prefix"), argument);
            if (prefix.NullOrEmpty()) prefix = sourceName.NullOrEmpty() ? "query" : sourceName;
            string groupBy = (string)step.Attribute("groupBy");
            string aggregate = (string)step.Attribute("aggregate");
            if (!groupBy.NullOrEmpty())
            {
                return source.GroupBy(item => FormatValue(ResolvePath(item, groupBy)))
                    .OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
                    .Take(limit).Select(group => prefix + "=" + Clean(group.Key) +
                        " count:" + group.Count()).ToList();
            }
            if (!aggregate.NullOrEmpty())
                return new List<string> { prefix + "=" + Aggregate(source, aggregate) };

            string select = (string)step.Attribute("select");
            string[] paths = (select.NullOrEmpty() ? "GetType.Name" : select)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> result = new List<string>();
            for (int i = 0; i < Math.Min(limit, source.Count); i++)
            {
                object item = source[i];
                result.Add(prefix + "=" + string.Join(" ", paths.Select(path =>
                {
                    string trimmed = path.Trim();
                    return trimmed.Replace('.', '_') + ":" +
                        Clean(FormatValue(ResolvePath(item, trimmed)));
                })));
            }
            if (result.Count == 0) result.Add(prefix + "=none");
            return result;
        }

        private static IEnumerable<object> ResolveSource(string source, Map map)
        {
            string normalized = (source ?? "").Trim();
            string lowered = normalized.ToLowerInvariant();
            if (lowered.StartsWith("component:"))
            {
                string specification = normalized.Substring("component:".Length);
                int separator = specification.IndexOf('/');
                string typeName = separator >= 0 ? specification.Substring(0, separator) :
                    specification;
                string path = separator >= 0 ? specification.Substring(separator + 1) : "";
                Type type = AccessTools.TypeByName(typeName);
                object component = map?.components?.FirstOrDefault(value =>
                    type != null && type.IsInstanceOfType(value));
                return ExpandSource(path.NullOrEmpty() ? component :
                    ResolvePath(component, path));
            }
            string[] rooted = normalized.Split(new[] { ':' }, 2);
            if (rooted.Length == 2 && (rooted[0].Equals("map", StringComparison.OrdinalIgnoreCase) ||
                rooted[0].Equals("game", StringComparison.OrdinalIgnoreCase) ||
                rooted[0].Equals("world", StringComparison.OrdinalIgnoreCase) ||
                rooted[0].Equals("settings", StringComparison.OrdinalIgnoreCase)))
            {
                object root = rooted[0].Equals("map", StringComparison.OrdinalIgnoreCase) ? map :
                    rooted[0].Equals("game", StringComparison.OrdinalIgnoreCase) ? Current.Game :
                    rooted[0].Equals("world", StringComparison.OrdinalIgnoreCase) ? Find.World :
                    HerdsMod.Settings;
                return ExpandSource(ResolvePath(root, rooted[1]));
            }
            switch (lowered)
            {
                case "map": return map == null ? Enumerable.Empty<object>() : new object[] { map };
                case "game": return Current.Game == null ? Enumerable.Empty<object>() :
                    new object[] { Current.Game };
                case "world": return Find.World == null ? Enumerable.Empty<object>() :
                    new object[] { Find.World };
                case "settings": return HerdsMod.Settings == null ? Enumerable.Empty<object>() :
                    new object[] { HerdsMod.Settings };
                case "components": return map?.components?.Cast<object>() ??
                    Enumerable.Empty<object>();
                case "colonists": return map?.mapPawns?.FreeColonistsSpawned?.Cast<object>() ??
                    Enumerable.Empty<object>();
                case "pawns": return map?.mapPawns?.AllPawnsSpawned?.Cast<object>() ??
                    Enumerable.Empty<object>();
                case "animals": return map?.mapPawns?.AllPawnsSpawned?
                    .Where(pawn => pawn?.RaceProps?.Animal == true).Cast<object>() ??
                    Enumerable.Empty<object>();
                case "things": return map?.listerThings?.AllThings?.Cast<object>() ??
                    Enumerable.Empty<object>();
                case "buildings": return map?.listerBuildings?.allBuildingsColonist?
                    .Cast<object>() ?? Enumerable.Empty<object>();
                case "animaldefs": return DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(def => def.race?.Animal == true).Cast<object>();
                case "selected": return Find.Selector?.SelectedObjects?.Cast<object>() ??
                    Enumerable.Empty<object>();
                default: return Enumerable.Empty<object>();
            }
        }

        private static IEnumerable<object> ExpandSource(object value)
        {
            if (value == null) return Enumerable.Empty<object>();
            if (value is string) return new[] { value };
            if (value is IDictionary dictionary)
                return dictionary.Values.Cast<object>();
            if (value is IEnumerable enumerable)
                return enumerable.Cast<object>();
            return new[] { value };
        }

        private static bool Matches(object item, string filter)
        {
            string[] operators = { "!=", ">=", "<=", "~", ">", "<", "=" };
            for (int i = 0; i < operators.Length; i++)
            {
                int index = filter.IndexOf(operators[i], StringComparison.Ordinal);
                if (index <= 0) continue;
                string path = filter.Substring(0, index).Trim();
                string expected = filter.Substring(index + operators[i].Length).Trim();
                return Compare(ResolvePath(item, path), operators[i], expected);
            }
            return false;
        }

        private static bool Compare(object actual, string operation, string expected)
        {
            if (expected.Equals("null", StringComparison.OrdinalIgnoreCase))
                return operation == "!=" ? actual != null : actual == null;
            string actualText = FormatValue(actual);
            if (operation == "~")
                return actualText.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
            if (double.TryParse(actualText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double actualNumber) &&
                double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double expectedNumber))
            {
                switch (operation)
                {
                    case ">": return actualNumber > expectedNumber;
                    case "<": return actualNumber < expectedNumber;
                    case ">=": return actualNumber >= expectedNumber;
                    case "<=": return actualNumber <= expectedNumber;
                    case "!=": return Math.Abs(actualNumber - expectedNumber) > 0.000001;
                    default: return Math.Abs(actualNumber - expectedNumber) <= 0.000001;
                }
            }
            bool equal = actualText.Equals(expected, StringComparison.OrdinalIgnoreCase);
            return operation == "!=" ? !equal : equal;
        }

        private static string Aggregate(List<object> source, string specification)
        {
            string[] parts = specification.Split(new[] { ':' }, 2);
            string operation = parts[0].Trim().ToLowerInvariant();
            string path = parts.Length > 1 ? parts[1].Trim() : "";
            if (operation == "count") return source.Count.ToString(CultureInfo.InvariantCulture);
            List<double> values = source.Select(item => ResolvePath(item, path))
                .Select(value =>
                {
                    double.TryParse(FormatValue(value), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double parsed);
                    return parsed;
                }).ToList();
            if (values.Count == 0) return "0";
            switch (operation)
            {
                case "sum": return values.Sum().ToString("0.###", CultureInfo.InvariantCulture);
                case "avg": return values.Average().ToString("0.###", CultureInfo.InvariantCulture);
                case "min": return values.Min().ToString("0.###", CultureInfo.InvariantCulture);
                case "max": return values.Max().ToString("0.###", CultureInfo.InvariantCulture);
                case "distinct": return source.Select(item =>
                    FormatValue(ResolvePath(item, path))).Distinct().Count()
                    .ToString(CultureInfo.InvariantCulture);
                default: return "unsupported:" + operation;
            }
        }

        private static List<string> ExecuteInvoke(XElement step, string argument, Map map)
        {
            string targetSpec = Expand((string)step.Attribute("target"), argument);
            string methodName = Expand((string)step.Attribute("method"), argument);
            string rawArguments = Expand((string)step.Attribute("arguments"), argument);
            object target = null;
            Type targetType = null;
            if (targetSpec.Equals("map", StringComparison.OrdinalIgnoreCase))
            {
                target = map;
                targetType = map?.GetType();
            }
            else if (targetSpec.Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                target = Current.Game;
                targetType = target?.GetType();
            }
            else if (targetSpec.Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                target = HerdsMod.Settings;
                targetType = target?.GetType();
            }
            else if (targetSpec.StartsWith("component:", StringComparison.OrdinalIgnoreCase))
            {
                string typeName = targetSpec.Substring("component:".Length);
                targetType = AccessTools.TypeByName(typeName);
                target = map?.components?.FirstOrDefault(component =>
                    targetType != null && targetType.IsInstanceOfType(component));
            }
            else if (targetSpec.StartsWith("static:", StringComparison.OrdinalIgnoreCase))
            {
                targetType = AccessTools.TypeByName(targetSpec.Substring("static:".Length));
            }
            if (targetType == null) return new List<string> { "invoke=target_not_found" };

            string[] raw = rawArguments.NullOrEmpty() ? Array.Empty<string>() :
                rawArguments.Split(',');
            MethodInfo method = targetType.GetMethods(BindingFlags.Public |
                BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.Name == methodName &&
                    candidate.GetParameters().Length == raw.Length);
            if (method == null) return new List<string> { "invoke=method_not_found" };
            ParameterInfo[] parameters = method.GetParameters();
            object[] converted = new object[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                converted[i] = ConvertScalar(raw[i].Trim(), parameters[i].ParameterType);
            object value = method.Invoke(target, converted);
            if (value is IEnumerable enumerable && value is not string)
            {
                List<string> lines = new List<string>();
                foreach (object item in enumerable)
                {
                    lines.Add("invoke=" + Clean(FormatValue(item)));
                    if (lines.Count >= 100) break;
                }
                return lines.Count == 0 ? new List<string> { "invoke=ok" } : lines;
            }
            return new List<string> { "invoke=" + Clean(FormatValue(value ?? "ok")) };
        }

        private static object ResolvePath(object value, string path)
        {
            if (value == null || path.NullOrEmpty()) return value;
            string[] parts = path.Split('.');
            object current = value;
            for (int i = 0; i < parts.Length && current != null; i++)
            {
                string part = parts[i];
                if (part == "GetType")
                {
                    current = current.GetType();
                    continue;
                }
                Type type = current.GetType();
                PropertyInfo property = type.GetProperty(part, BindingFlags.Public |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    current = property.GetValue(property.GetMethod?.IsStatic == true ? null : current,
                        null);
                    continue;
                }
                FieldInfo field = type.GetField(part, BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.IgnoreCase);
                if (field == null) return null;
                current = field.GetValue(field.IsStatic ? null : current);
            }
            return current;
        }

        private static object ConvertScalar(string value, Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            if (target == typeof(string)) return value;
            if (target == typeof(bool)) return ParseBool(value);
            if (target.IsEnum) return Enum.Parse(target, value, true);
            if (target == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
            if (target == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
            if (typeof(Def).IsAssignableFrom(target))
            {
                MethodInfo getNamed = typeof(DefDatabase<>).MakeGenericType(target)
                    .GetMethod("GetNamed", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(string), typeof(bool) }, null);
                return getNamed?.Invoke(null, new object[] { value, false });
            }
            return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "null";
            if (value is bool boolean) return boolean ? "true" : "false";
            if (value is float single) return single.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is double number) return number.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is Def def) return def.defName;
            if (value is Thing thing) return thing.thingIDNumber + ":" + thing.LabelShortCap;
            if (value is Type type) return type.FullName;
            return value.ToString().Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string Expand(string value, string argument) =>
            (value ?? "").Replace("$argument", argument ?? "");

        private static bool ValidCommandName(string value) =>
            !value.NullOrEmpty() && value.Length <= 64 &&
            value.All(character => char.IsLetterOrDigit(character) || character == '_');

        private static bool ParseBool(string value) =>
            value != null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

        private static int ParseInt(string value, int fallback, int minimum, int maximum) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int parsed) ? Math.Max(minimum, Math.Min(maximum, parsed)) : fallback;

        private static string Clean(string value) =>
            (value ?? "none").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');

        private static void EnsureTemplate()
        {
            if (File.Exists(ModulePath)) return;
            XDocument template = new XDocument(
                new XElement("WildlifeBridgeModules",
                    new XAttribute("version", "1"),
                    new XElement("Command",
                        new XAttribute("name", "HOT_ECOSYSTEM_BRIEF"),
                        new XAttribute("description", "Compact live ecosystem composition."),
                        new XElement("Builtin", new XAttribute("name", "SNAPSHOT")),
                        new XElement("Query",
                            new XAttribute("source", "animals"),
                            new XAttribute("where", "Faction=null"),
                            new XAttribute("groupBy", "def.defName"),
                            new XAttribute("limit", "12"),
                            new XAttribute("prefix", "wildSpecies"))),
                    new XElement("Command",
                        new XAttribute("name", "HOT_PREDATOR_AUDIT"),
                        new XAttribute("description", "Live predator species and current jobs."),
                        new XElement("Query",
                            new XAttribute("source", "animals"),
                            new XAttribute("where", "RaceProps.predator=true"),
                            new XAttribute("select",
                                "thingIDNumber,def.defName,CurJobDef.defName,Position"),
                            new XAttribute("limit", "40"),
                            new XAttribute("prefix", "predator"))),
                    new XElement("Command",
                        new XAttribute("name", "HOT_COMPONENT_AUDIT"),
                        new XAttribute("description", "Loaded map component types."),
                        new XElement("Query",
                            new XAttribute("source", "components"),
                            new XAttribute("select", "GetType.FullName"),
                            new XAttribute("limit", "100"),
                            new XAttribute("prefix", "component")))));
            string temporary = ModulePath + ".tmp";
            template.Save(temporary);
            File.Move(temporary, ModulePath);
        }
    }
}
