using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The story's dialogue scenes: a box at the bottom of the screen with the speaker's 3D
/// portrait, name and line. Each line is spoken in the character's own voice (face to face,
/// no radio) while the mouth moves; the text types out and stays until the player moves on.
/// Enter, Space or a click: finish the line, then the next one. Skip ends the scene.
/// Created on first use, outlives scene loads, and draws above the game UI but under the
/// pause menu and the scene fade.
/// </summary>
public class DialoguePlayer : MonoBehaviour
{
    private const float CharsPerSecond = 48f;
    private const float InputGrace = 0.35f;
    private const float MouthFlap = 0.11f;
    private const int SortingOrder = 50;

    private static DialoguePlayer _instance;

    private VisualElement _overlay;
    private VisualElement _box;
    private VisualElement _portrait;
    private Label _title;
    private Label _name;
    private Label _role;
    private Label _text;
    private Label _hint;

    private DialogueLine[] _lines;
    private Action _onDone;
    private int _index;
    private float _lineStart;
    private string _fullText = "";
    private bool _typing;
    private bool _voiced;
    private float _inputFrom;
    private float _nextFlap;
    private bool _mouthOpen;
    private CharacterId _speaker;

    /// <summary>A dialogue scene is on screen.</summary>
    public static bool IsPlaying => _instance != null && _instance._lines != null;

    /// <summary>Plays the lines, then calls onDone (also when skipped). An empty list calls it at once.</summary>
    public static void Play(DialogueLine[] lines, string title, Action onDone)
    {
        if (lines == null || lines.Length == 0)
        {
            onDone?.Invoke();
            return;
        }
        var player = Ensure();
        if (player == null)
        {
            onDone?.Invoke();
            return;
        }
        player.Begin(lines, title, onDone);
    }

    /// <summary>Ends the scene now without calling onDone (the scene is being left).</summary>
    public static void Stop()
    {
        if (_instance == null) return;
        _instance._onDone = null;
        _instance.Finish();
    }

    private static DialoguePlayer Ensure()
    {
        if (_instance != null) return _instance;

        var host = SceneLoader.Instance != null ? SceneLoader.Instance.GetComponent<UIDocument>() : null;
        if (host == null || host.panelSettings == null)
        {
            Debug.LogWarning("[DIALOGUE]: no panel settings to draw on; dialogue skipped.");
            return null;
        }

        var go = new GameObject("StoryDialogue");
        go.SetActive(false);
        DontDestroyOnLoad(go);
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = host.panelSettings;
        doc.sortingOrder = SortingOrder;
        _instance = go.AddComponent<DialoguePlayer>();
        go.SetActive(true);
        return _instance;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    // A scene change (pause menu > Main Menu) ends any scene still on screen.
    private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Stop();

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        var root = GetComponent<UIDocument>().rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        var sheet = Resources.Load<StyleSheet>("Story/StoryUI");
        if (sheet != null) root.styleSheets.Add(sheet);

        _overlay = new VisualElement();
        _overlay.AddToClassList("dlg-overlay");
        _overlay.style.display = DisplayStyle.None;
        _overlay.RegisterCallback<ClickEvent>(e => { if (e.target == _overlay || IsInBox(e.target as VisualElement)) Advance(); });
        root.Add(_overlay);

        var top = new VisualElement();
        top.AddToClassList("dlg-top");
        _title = new Label();
        _title.AddToClassList("dlg-title");
        var skip = new Button(() => { if (Time.unscaledTime >= _inputFrom) Finish(); }) { text = "SKIP  >>" };
        skip.AddToClassList("dlg-skip");
        top.Add(_title);
        top.Add(skip);
        _overlay.Add(top);

        _box = new VisualElement();
        _box.AddToClassList("dlg-box");
        _portrait = new VisualElement();
        _portrait.AddToClassList("dlg-portrait");
        var body = new VisualElement();
        body.AddToClassList("dlg-body");
        _name = new Label();
        _name.AddToClassList("dlg-name");
        _role = new Label();
        _role.AddToClassList("dlg-role");
        _text = new Label();
        _text.AddToClassList("dlg-text");
        _hint = new Label();
        _hint.AddToClassList("dlg-hint");
        body.Add(_name);
        body.Add(_role);
        body.Add(_text);
        body.Add(_hint);
        _box.Add(_portrait);
        _box.Add(body);
        _overlay.Add(_box);
    }

    private bool IsInBox(VisualElement e)
    {
        for (; e != null; e = e.parent) if (e == _box) return true;
        return false;
    }

    private void Begin(DialogueLine[] lines, string title, Action onDone)
    {
        _lines = lines;
        _onDone = onDone;
        _index = -1;
        _title.text = title ?? "";
        _title.style.display = string.IsNullOrEmpty(title) ? DisplayStyle.None : DisplayStyle.Flex;
        _overlay.style.display = DisplayStyle.Flex;
        _overlay.pickingMode = PickingMode.Position; // nothing behind takes clicks meanwhile
        _inputFrom = Time.unscaledTime + InputGrace;
        Next();
    }

    private void Next()
    {
        _index++;
        if (_index >= _lines.Length)
        {
            Finish();
            return;
        }

        var line = _lines[_index];
        var who = StoryCast.Get(line.Speaker);
        _speaker = line.Speaker;
        _name.text = who.Name;
        _name.style.color = who.Accent;
        _role.text = who.Role;
        _box.style.borderTopColor = _box.style.borderBottomColor = _box.style.borderLeftColor = _box.style.borderRightColor = who.Accent;
        _portrait.style.borderTopColor = _portrait.style.borderBottomColor = _portrait.style.borderLeftColor = _portrait.style.borderRightColor = who.Accent;
        SetMouth(false);

        _fullText = line.Text;
        _text.text = "";
        _typing = true;
        _lineStart = Time.unscaledTime;
        _hint.text = (_index + 1) + " / " + _lines.Length;

        var audio = AudioService.Instance;
        _voiced = audio != null && audio.SpeakFace(who.SpeechVoice, CrewSpeech.Speakable(line.Text));
    }

    private void Advance()
    {
        if (_lines == null || Time.unscaledTime < _inputFrom) return;
        _inputFrom = Time.unscaledTime + 0.12f; // one press, one step
        if (_typing)
        {
            _typing = false;
            _text.text = _fullText;
            UpdateHint();
            return;
        }
        Next();
    }

    private void Finish()
    {
        if (_lines == null) return;
        _lines = null;
        if (AudioService.Instance != null) AudioService.Instance.StopFace();
        _overlay.style.display = DisplayStyle.None;
        _overlay.pickingMode = PickingMode.Ignore;
        var done = _onDone;
        _onDone = null;
        done?.Invoke();
    }

    private void UpdateHint()
    {
        bool last = _index >= _lines.Length - 1;
        _hint.text = (_index + 1) + " / " + _lines.Length + "      ENTER  " + (last ? "done" : "next");
    }

    private void SetMouth(bool open)
    {
        _mouthOpen = open;
        var tex = PortraitStudio.Get(_speaker, open);
        _portrait.style.backgroundImage = tex != null ? Background.FromRenderTexture(tex as RenderTexture) : new StyleBackground(StyleKeyword.None);
    }

    private void Update()
    {
        if (_lines == null) return;

        // The pause menu (Esc) sits on top: wait for it.
        var pause = FindAnyObjectByType<PauseMenuController>();
        bool paused = pause != null && pause.IsOpen;

        if (_typing)
        {
            int shown = Mathf.Min(_fullText.Length, Mathf.FloorToInt((Time.unscaledTime - _lineStart) * CharsPerSecond));
            _text.text = _fullText.Substring(0, shown);
            if (shown >= _fullText.Length)
            {
                _typing = false;
                UpdateHint();
            }
        }

        // The mouth moves while the voice plays (or, with no voice, while the words type out).
        var audio = AudioService.Instance;
        bool talking = _voiced ? audio != null && audio.FaceVoicePlaying : _typing;
        if (talking && Time.unscaledTime >= _nextFlap)
        {
            _nextFlap = Time.unscaledTime + MouthFlap * UnityEngine.Random.Range(0.7f, 1.4f);
            SetMouth(!_mouthOpen);
        }
        else if (!talking && _mouthOpen)
        {
            SetMouth(false);
        }

        if (paused || (SceneLoader.Instance != null && SceneLoader.Instance.IsLoading)) return;
        var kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            Advance();
    }
}
