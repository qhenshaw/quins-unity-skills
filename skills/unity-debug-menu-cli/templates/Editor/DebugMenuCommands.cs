using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;

namespace MyGame.DebugTools.Editor
{
    /// <summary>
    /// Drives the in-game debug menu from the CLI: list every section's controls with their current values, and
    /// press a button, or set a toggle or slider, by section and label. E.g.
    /// `unity command debugmenu do --section Enemies --label "Freeze AI" --value true`.
    ///
    /// Sections exist only while their owners are enabled, so both commands are for Play mode. New controls need
    /// no code here: anything registered with <see cref="DebugMenu"/> is listed and can be driven.
    /// </summary>
    public static class DebugMenuCommands
    {
        [CliCommand("debugmenu", "list", "Every debug menu section with its controls and their current values.")]
        public static string List([CliArg("section", "Only this section (optional)")] string section = "")
        {
            var sections = DebugMenu.Sections
                .Where(s => string.IsNullOrEmpty(section) || s.Title == section)
                .GroupBy(s => s.Title)
                .Select(g => "{" + Field("section", Quote(g.Key)) + "," +
                             Field("controls", Array(g.SelectMany(s => s.Controls).Select(Describe))) + "}");
            return Array(sections);
        }

        [CliCommand("debugmenu", "do", "Press a debug button, or set a toggle (true/false) or slider (number), by section and label. Returns the section's values afterwards.")]
        public static string Do(
            [CliArg("section", "Section title, e.g. Enemies")] string section,
            [CliArg("label", "Control label, e.g. \"Freeze AI\"")] string label,
            [CliArg("value", "For toggles: true/false. For sliders: a number.")] string value = "")
        {
            var control = DebugMenu.Sections.Where(s => s.Title == section).SelectMany(s => s.Controls)
                .FirstOrDefault(c => c.Label == label);
            if (control == null) return Error($"no control '{label}' in section '{section}' (is the game in Play mode?)");

            if (control.OnClick != null)
            {
                control.OnClick();
            }
            else if (control.SetBool != null)
            {
                switch ((value ?? "").Trim().ToLowerInvariant())
                {
                    case "true": case "1": case "on": control.SetBool(true); break;
                    case "false": case "0": case "off": control.SetBool(false); break;
                    default: return Error($"'{label}' is a toggle: pass --value true or false");
                }
            }
            else if (control.SetFloat != null)
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                    return Error($"'{label}' is a slider: pass --value <number>");
                control.SetFloat(number);
            }
            else
            {
                return Error($"'{label}' is a readout and can't be set");
            }

            return List(section);
        }

        private static string Describe(DebugControl c)
        {
            var fields = new List<string> { Field("label", Quote(c.Label)), Field("kind", Quote(c.Kind.ToString())) };
            if (c.GetText != null) fields.Add(Field("value", Quote(c.GetText())));
            else if (c.GetBool != null) fields.Add(Field("value", c.GetBool() ? "true" : "false"));
            else if (c.GetFloat != null) fields.Add(Field("value", Number(c.GetFloat())));
            return "{" + string.Join(",", fields) + "}";
        }

        // A few lines of JSON writing, so the commands need no JSON package.

        private static string Error(string message) => "{" + Field("error", Quote(message)) + "}";

        private static string Field(string key, string json) => $"{Quote(key)}:{json}";

        private static string Array(IEnumerable<string> items) => "[" + string.Join(",", items) + "]";

        private static string Number(float f) =>
            float.IsFinite(f) ? System.Math.Round(f, 3).ToString(CultureInfo.InvariantCulture) : "null";

        private static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
