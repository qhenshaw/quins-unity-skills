using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MyGame.DebugTools
{
    /// <summary>
    /// Registry for the in-game debug menu. Features call <see cref="Register"/> (usually in OnEnable)
    /// to add a section of controls, and <see cref="Unregister"/> (in OnDisable) to remove it.
    ///
    /// Both calls are compiled out of release builds (they only exist in the Editor and Development
    /// builds), including the lambdas passed to them, so callers don't need #if blocks.
    /// </summary>
    public static class DebugMenu
    {
        private static readonly List<DebugSection> sections = new();

        /// <summary>Raised when sections are added or removed, so the menu can rebuild.</summary>
        public static event Action SectionsChanged;

        /// <summary>All registered sections, in registration order.</summary>
        public static IReadOnlyList<DebugSection> Sections => sections;

        /// <summary>
        /// Add a section of controls under <paramref name="title"/>, owned by <paramref name="owner"/>.
        /// Several owners can share a title; their controls are shown together.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Register(object owner, string title, Action<DebugSection> build)
        {
            if (owner == null || build == null) return;
            var section = new DebugSection(owner, title);
            build(section);
            sections.Add(section);
            SectionsChanged?.Invoke();
        }

        /// <summary>Remove every section registered by <paramref name="owner"/>.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Unregister(object owner)
        {
            if (sections.RemoveAll(s => s.Owner == owner) > 0)
            {
                SectionsChanged?.Invoke();
            }
        }

        // Clear stale sections when entering Play mode with domain reload disabled.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            sections.Clear();
            SectionsChanged = null;
        }
    }

    /// <summary>One feature's group of debug controls. Built through the methods below.</summary>
    public class DebugSection
    {
        internal DebugSection(object owner, string title)
        {
            Owner = owner;
            Title = title;
        }

        public object Owner { get; }
        public string Title { get; }
        public List<DebugControl> Controls { get; } = new();

        /// <summary>A button that runs <paramref name="onClick"/>.</summary>
        public DebugSection Button(string label, Action onClick)
        {
            Controls.Add(new DebugControl(DebugControlKind.Button, label) { OnClick = onClick });
            return this;
        }

        /// <summary>A checkbox bound to a getter and setter.</summary>
        public DebugSection Toggle(string label, Func<bool> get, Action<bool> set)
        {
            Controls.Add(new DebugControl(DebugControlKind.Toggle, label) { GetBool = get, SetBool = set });
            return this;
        }

        /// <summary>A slider bound to a getter and setter.</summary>
        public DebugSection Slider(string label, float min, float max, Func<float> get, Action<float> set)
        {
            Controls.Add(new DebugControl(DebugControlKind.Slider, label)
            {
                Min = min, Max = max, GetFloat = get, SetFloat = set
            });
            return this;
        }

        /// <summary>A read-only line of live state, refreshed a few times a second.</summary>
        public DebugSection Readout(string label, Func<string> get)
        {
            Controls.Add(new DebugControl(DebugControlKind.Readout, label) { GetText = get });
            return this;
        }
    }

    public enum DebugControlKind
    {
        Button,
        Toggle,
        Slider,
        Readout
    }

    /// <summary>Description of one control; the menu turns it into UI.</summary>
    public class DebugControl
    {
        internal DebugControl(DebugControlKind kind, string label)
        {
            Kind = kind;
            Label = label;
        }

        public DebugControlKind Kind { get; }
        public string Label { get; }

        public Action OnClick { get; internal set; }
        public Func<bool> GetBool { get; internal set; }
        public Action<bool> SetBool { get; internal set; }
        public float Min { get; internal set; }
        public float Max { get; internal set; }
        public Func<float> GetFloat { get; internal set; }
        public Action<float> SetFloat { get; internal set; }
        public Func<string> GetText { get; internal set; }
    }
}
