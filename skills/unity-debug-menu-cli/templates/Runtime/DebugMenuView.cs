using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MyGame.DebugTools
{
    /// <summary>
    /// Shows the debug menu: one foldout per section registered with <see cref="DebugMenu"/>.
    /// Put it on a GameObject with a UIDocument (source asset DebugMenu.uxml) in any scene that should have the
    /// menu. Removes itself in release builds.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DebugMenuView : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional: an action from the project's input actions asset that opens and closes " +
                                 "the menu. Leave empty to use the built-in action below.")]
        private InputActionReference toggleActionReference;

        [SerializeField, Tooltip("Used when no reference is set. With no bindings it defaults to backquote on " +
                                 "keyboard and Select + Start on gamepad.")]
        private InputAction toggleAction = new InputAction("ToggleDebugMenu", InputActionType.Button);

        [SerializeField, Tooltip("Start with the menu open.")]
        private bool startOpen;

        [SerializeField, Min(0.05f), Tooltip("Seconds between refreshes of readouts and bound values.")]
        private float refreshInterval = 0.25f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private UIDocument document;
        private VisualElement panel;
        private ScrollView sectionList;
        private InputAction boundAction;
        private readonly List<System.Action> refreshers = new();
        private float nextRefresh;
        private bool rebuildPending = true;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            var root = document.rootVisualElement;
            panel = root.Q<VisualElement>("debug-panel");
            sectionList = root.Q<ScrollView>("debug-sections");

            DebugMenu.SectionsChanged += OnSectionsChanged;

            boundAction = ResolveToggleAction();
            boundAction.performed += OnToggle;
            boundAction.Enable();

            SetOpen(startOpen);
        }

        private void OnDisable()
        {
            DebugMenu.SectionsChanged -= OnSectionsChanged;
            if (boundAction == null) return;

            boundAction.performed -= OnToggle;
            // An action from the project's asset is left enabled: other code may rely on it.
            if (boundAction == toggleAction) boundAction.Disable();
            boundAction = null;
        }

        private InputAction ResolveToggleAction()
        {
            if (toggleActionReference != null && toggleActionReference.action != null)
            {
                return toggleActionReference.action;
            }

            toggleAction ??= new InputAction("ToggleDebugMenu", InputActionType.Button);
            if (toggleAction.bindings.Count == 0)
            {
                toggleAction.AddBinding("<Keyboard>/backquote");
                toggleAction.AddCompositeBinding("OneModifier")
                    .With("Modifier", "<Gamepad>/select")
                    .With("Binding", "<Gamepad>/start");
            }

            return toggleAction;
        }

        private void Update()
        {
            if (!IsOpen) return;

            if (rebuildPending) Rebuild();

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + refreshInterval;
                foreach (var refresh in refreshers) refresh();
            }
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (panel == null) return;

            panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                Rebuild();
                // Focus the first control so a gamepad can navigate straight away.
                sectionList?.Q<Foldout>()?.Focus();
            }
        }

        private void OnToggle(InputAction.CallbackContext context) => SetOpen(!IsOpen);

        private void OnSectionsChanged() => rebuildPending = true;

        private void Rebuild()
        {
            rebuildPending = false;
            if (sectionList == null) return;

            sectionList.Clear();
            refreshers.Clear();

            // Group sections that share a title into one foldout.
            var foldouts = new Dictionary<string, Foldout>();
            foreach (var section in DebugMenu.Sections)
            {
                if (!foldouts.TryGetValue(section.Title, out var foldout))
                {
                    foldout = new Foldout { text = section.Title, value = false };
                    foldout.AddToClassList("debug-section");
                    foldouts.Add(section.Title, foldout);
                    sectionList.Add(foldout);
                }

                foreach (var control in section.Controls)
                {
                    foldout.Add(CreateControl(control));
                }
            }

            if (DebugMenu.Sections.Count == 0)
            {
                sectionList.Add(new Label("No debug controls registered in this scene."));
            }
        }

        private VisualElement CreateControl(DebugControl control)
        {
            switch (control.Kind)
            {
                case DebugControlKind.Button:
                {
                    var button = new Button(() => control.OnClick?.Invoke()) { text = control.Label };
                    button.AddToClassList("debug-button");
                    return button;
                }
                case DebugControlKind.Toggle:
                {
                    var toggle = new Toggle(control.Label);
                    toggle.SetValueWithoutNotify(control.GetBool?.Invoke() ?? false);
                    toggle.RegisterValueChangedCallback(e => control.SetBool?.Invoke(e.newValue));
                    refreshers.Add(() => toggle.SetValueWithoutNotify(control.GetBool?.Invoke() ?? false));
                    return toggle;
                }
                case DebugControlKind.Slider:
                {
                    var slider = new Slider(control.Label, control.Min, control.Max) { showInputField = true };
                    slider.SetValueWithoutNotify(control.GetFloat?.Invoke() ?? control.Min);
                    slider.RegisterValueChangedCallback(e => control.SetFloat?.Invoke(e.newValue));
                    refreshers.Add(() =>
                    {
                        // Don't fight the user while they're dragging or typing.
                        if (slider.focusController?.focusedElement is VisualElement focused && slider.Contains(focused)) return;
                        slider.SetValueWithoutNotify(control.GetFloat?.Invoke() ?? control.Min);
                    });
                    return slider;
                }
                default:
                {
                    var row = new Label();
                    row.AddToClassList("debug-readout");
                    void Refresh() => row.text = $"{control.Label}: {control.GetText?.Invoke()}";
                    Refresh();
                    refreshers.Add(Refresh);
                    return row;
                }
            }
        }
#else
        private void Awake()
        {
            // The debug menu doesn't exist in release builds.
            Destroy(gameObject);
        }
#endif
    }
}
