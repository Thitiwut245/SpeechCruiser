using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// The only place that changes scenes, and the page transition everywhere: a band of navy ink
/// with a rust edge sweeps across from the left, the change happens under it, and it sweeps off
/// to the right, with a whoosh. Scenes load async (Single mode) while the ink covers the screen.
/// Menus call <see cref="Page"/> to change page the same way. Lives on the persistent Services prefab.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class SceneLoader : Singleton<SceneLoader>
{
    [Tooltip("Seconds for the ink to cover the screen; it takes a little longer to clear.")]
    [SerializeField] private float FadeDuration = 0.3f;

    [Tooltip("Draw order of the fade overlay relative to other UIDocuments on the same PanelSettings.")]
    [SerializeField] private float SortingOrder = 1000f;

    private UIDocument _document;
    private VisualElement _overlay;   // swallows clicks during a transition
    private VisualElement _ink;       // the sweeping band
    private bool _wiping;

    private static readonly Color InkColour = new Color(0.055f, 0.09f, 0.165f);

    public bool IsLoading { get; private set; }

    public event Action<SceneId> OnSceneLoaded;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;

        _document = GetComponent<UIDocument>();
        _document.sortingOrder = SortingOrder;
    }

    private void OnEnable()
    {
        _overlay = new VisualElement { name = "SceneFadeOverlay", pickingMode = PickingMode.Ignore };
        _overlay.style.position = Position.Absolute;
        _overlay.style.left = 0;
        _overlay.style.right = 0;
        _overlay.style.top = 0;
        _overlay.style.bottom = 0;
        _overlay.style.overflow = Overflow.Hidden;
        _document.rootVisualElement.Add(_overlay);

        // Wider and taller than the screen and tipped a little, so its edges cross on a slant.
        _ink = new VisualElement { name = "InkWipe", pickingMode = PickingMode.Ignore };
        _ink.style.position = Position.Absolute;
        _ink.style.top = Length.Percent(-30f);
        _ink.style.bottom = Length.Percent(-30f);
        _ink.style.left = 0f;
        _ink.style.width = Length.Percent(170f);
        _ink.style.backgroundColor = InkColour;
        _ink.style.rotate = new Rotate(new Angle(8f, AngleUnit.Degree));
        _ink.style.display = DisplayStyle.None;
        foreach (bool leading in new[] { true, false })
        {
            var edge = new VisualElement { pickingMode = PickingMode.Ignore };
            edge.style.position = Position.Absolute;
            edge.style.top = 0f; edge.style.bottom = 0f;
            edge.style.width = leading ? 14f : 6f;
            if (leading) edge.style.right = 0f; else edge.style.left = 0f;
            edge.style.backgroundColor = UiTheme.Rust;
            _ink.Add(edge);
        }
        _overlay.Add(_ink);
    }

    /// <summary>
    /// Changes page under the ink wipe: <paramref name="swap"/> runs while the screen is covered.
    /// Runs it at once if a transition is already under way (or there is no loader).
    /// </summary>
    public static void Page(Action swap)
    {
        var loader = Instance;
        if (loader == null || loader.IsLoading || loader._wiping || !loader.isActiveAndEnabled) { swap?.Invoke(); return; }
        loader.StartCoroutine(loader.PageRoutine(swap));
    }

    private IEnumerator PageRoutine(Action swap)
    {
        _wiping = true;
        _overlay.pickingMode = PickingMode.Position;
        yield return Cover();
        swap?.Invoke();
        yield return null; // one frame for the new page to lay out under the ink
        yield return Reveal();
        _overlay.pickingMode = PickingMode.Ignore;
        _wiping = false;
    }

    public static string SceneName(SceneId id)
    {
        switch (id)
        {
            case SceneId.Menu: return "Menu";
            case SceneId.Game: return "Game";
            default: throw new ArgumentOutOfRangeException(nameof(id), id, null);
        }
    }

    public static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void Load(SceneId id)
    {
        if (IsLoading)
        {
            Debug.LogWarning($"[SCENE]: already loading, ignored request for {id}.");
            return;
        }
        StartCoroutine(LoadRoutine(id));
    }

    private IEnumerator LoadRoutine(SceneId id)
    {
        IsLoading = true;
        RadioEpoch.Advance(); // anything the radio is still working on belongs to the old scene
        _overlay.pickingMode = PickingMode.Position; // swallow clicks during the transition

        while (_wiping) yield return null;
        yield return Cover();

        AsyncOperation op = SceneManager.LoadSceneAsync(SceneName(id), LoadSceneMode.Single);
        while (!op.isDone) yield return null;

        OnSceneLoaded?.Invoke(id);

        yield return Reveal();

        _overlay.pickingMode = PickingMode.Ignore;
        IsLoading = false;
    }

    // Positions of the band's left edge, in screen widths: off to the left, covering, off to the right.
    private const float Before = -1.85f, Covering = -0.35f, After = 1.2f;

    private IEnumerator Cover()
    {
        UiSounds.Play(UiSounds.Whoosh, 0.55f);
        _ink.style.display = DisplayStyle.Flex;
        yield return Sweep(Before, Covering, FadeDuration, easeIn: true);
    }

    private IEnumerator Reveal()
    {
        yield return Sweep(Covering, After, FadeDuration * 1.2f, easeIn: false);
        _ink.style.display = DisplayStyle.None;
    }

    // Unscaled time: menus and tutorials may have timeScale at 0.
    private IEnumerator Sweep(float from, float to, float seconds, bool easeIn)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = t / seconds;
            k = easeIn ? k * k : 1f - (1f - k) * (1f - k);
            Place(Mathf.Lerp(from, to, k));
            yield return null;
        }
        Place(to);
    }

    private void Place(float screens)
    {
        float width = _document.rootVisualElement.resolvedStyle.width;
        if (float.IsNaN(width) || width <= 0f) width = Screen.width;
        _ink.style.translate = new Translate(screens * width, 0f);
    }
}
