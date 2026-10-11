using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Typed orders, for players who can't or don't want to speak (a shared room, a speech
/// difficulty, a mic Whisper handles badly). Enter opens a box, Enter sends, Esc cancels,
/// Up recalls the last order. The text goes through RadioMic.SubmitTyped, so it is routed,
/// read back and counted by the tutorial exactly like a spoken order. Time slows while typing.
/// Lives next to ShipHUD and draws into its document.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TypedOrders : MonoBehaviour
{
    public static bool IsTyping { get; private set; }
    private static int _closedFrame = -10;

    /// <summary>
    /// True while the box is open, on the frame it closes (so the Enter or Esc that closed it
    /// doesn't also open the pause menu or key the radio), while Settings is waiting for a
    /// new key, and while a Settings text box (the participant code) has the keyboard. Every
    /// game hotkey checks this.
    /// </summary>
    public static bool BlocksHotkeys => IsTyping || Time.frameCount - _closedFrame <= 1 ||
                                        SettingsPanelController.IsRebinding || SettingsPanelController.RebindEndedThisFrame ||
                                        SettingsPanelController.IsEditingText;

    private VisualElement _box;
    private TextField _field;
    private RadioMic _mic;
    private string _lastOrder = "";

    // The Enter that opened the box reaches the text field too, a moment later, as a key-down.
    // Taken as "send", it would send nothing and close the box in the same frame, so the box
    // never seemed to open. Enter only sends once the opening press has been released.
    private bool _openingEnterHeld;

    private void Start()
    {
        _mic = FindAnyObjectByType<RadioMic>();
        Build(GetComponent<UIDocument>().rootVisualElement);
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleStateChanged;
        if (IsTyping) Close();
    }

    private void Build(VisualElement root)
    {
        _box = new VisualElement();
        _box.style.position = Position.Absolute;
        _box.style.top = 84f;
        _box.style.left = Length.Percent(50f);
        _box.style.translate = new Translate(Length.Percent(-50f), 0f);
        _box.style.width = 720f;
        _box.style.paddingTop = 12f; _box.style.paddingBottom = 12f;
        _box.style.paddingLeft = 16f; _box.style.paddingRight = 16f;
        _box.style.backgroundColor = new Color(0.03f, 0.07f, 0.11f, 1f);
        var accent = new Color(1f, 0.78f, 0.27f);
        _box.style.borderTopWidth = 2f; _box.style.borderBottomWidth = 2f; _box.style.borderLeftWidth = 2f; _box.style.borderRightWidth = 2f;
        _box.style.borderTopColor = accent; _box.style.borderBottomColor = accent; _box.style.borderLeftColor = accent; _box.style.borderRightColor = accent;
        _box.style.borderTopLeftRadius = 10f; _box.style.borderTopRightRadius = 10f;
        _box.style.borderBottomLeftRadius = 10f; _box.style.borderBottomRightRadius = 10f;
        _box.style.display = DisplayStyle.None;

        var title = new Label("TYPE AN ORDER   ·   Enter to send   ·   Esc to cancel   ·   ↑ last order");
        title.style.fontSize = 16f;
        title.style.color = accent;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 6f;
        _box.Add(title);

        _field = new TextField();
        _field.maxLength = 120;
        _field.style.fontSize = 24f;
        _field.style.marginLeft = 0f; _field.style.marginRight = 0f;
        _field.RegisterCallback<KeyDownEvent>(HandleKey, TrickleDown.TrickleDown);
        // Clicking anywhere else closes the box, so it can't sit open with time slowed and
        // every hotkey blocked.
        _field.RegisterCallback<FocusOutEvent>(evt =>
        {
            var next = evt.relatedTarget as VisualElement;
            if (IsTyping && (next == null || !_box.Contains(next))) Close("focus moved away");
        });
        _box.Add(_field);

        var example = new Label("e.g.  slow ahead  ·  turn left  ·  any danger?  ·  Finn, what do you see?");
        example.style.fontSize = 15f;
        example.style.color = new Color(0.77f, 0.82f, 0.87f);
        example.style.marginTop = 6f;
        _box.Add(example);

        root.Add(_box);

        // The text inside the field: dark box, light text, whatever the theme says.
        _field.schedule.Execute(() =>
        {
            var input = _field.Q(className: "unity-text-field__input");
            if (input == null) return;
            input.style.backgroundColor = new Color(0.10f, 0.16f, 0.22f, 1f);
            input.style.color = new Color(0.95f, 0.97f, 0.98f);
            input.style.paddingTop = 6f; input.style.paddingBottom = 6f;
        });
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (!IsTyping)
        {
            if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && CanOpen()) Open();
            return;
        }

        if (_openingEnterHeld && !kb.enterKey.isPressed && !kb.numpadEnterKey.isPressed) _openingEnterHeld = false;
        if (kb.escapeKey.wasPressedThisFrame) Close("Esc");
    }

    private bool CanOpen()
    {
        if (Time.frameCount - _closedFrame <= 1) return false; // the Enter that just sent
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing) return false;
        if (TimeController.Instance != null && TimeController.Instance.IsPaused) return false;
        if (_mic != null && _mic.IsRecording) return false;
        return true;
    }

    public void Open()
    {
        IsTyping = true;
        _openingEnterHeld = true;
        Debug.Log("[TYPED]: order box opened.");
        _box.style.display = DisplayStyle.Flex;
        _field.SetValueWithoutNotify("");
        _field.schedule.Execute(() => _field.Focus());
        if (TimeController.Instance != null) TimeController.Instance.SetTypingActive(true);
    }

    public void Close() => Close("closed");

    private void Close(string why)
    {
        if (IsTyping) Debug.Log("[TYPED]: order box closed (" + why + ").");
        IsTyping = false;
        _closedFrame = Time.frameCount;
        if (_box != null) _box.style.display = DisplayStyle.None;
        _field?.Blur();
        if (TimeController.Instance != null) TimeController.Instance.SetTypingActive(false);
    }

    /// <summary>Sends the text as an order (also used by tests).</summary>
    public void Submit(string text)
    {
        Close(string.IsNullOrWhiteSpace(text) ? "sent empty" : "sent");
        if (string.IsNullOrWhiteSpace(text)) return;
        _lastOrder = text.Trim();

        // Typed with the Thai layout still on: read the keys as English and say so.
        string english = KeyboardLayouts.FromThaiKedmanee(_lastOrder);
        if (english != null)
        {
            if (ShipHUD.Instance != null) ShipHUD.Instance.ShowRadioNote("Keyboard is in Thai. Read as: \"" + english + "\"");
            _lastOrder = english;
        }
        if (_mic != null) _mic.SubmitTyped(_lastOrder);
    }

    private void HandleKey(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            evt.StopPropagation();
            if (_openingEnterHeld) return; // the press that opened the box, not a send
            Submit(_field.value);
        }
        else if (evt.keyCode == KeyCode.UpArrow && _lastOrder.Length > 0)
        {
            evt.StopPropagation();
            _field.value = _lastOrder;
            _field.SelectAll();
        }
        else if (evt.keyCode == KeyCode.Escape)
        {
            evt.StopPropagation();
            Close("Esc");
        }
    }

    private void HandleStateChanged(GameState state)
    {
        if (state != GameState.Playing && IsTyping) Close("voyage ended");
    }
}
