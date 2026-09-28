using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// The only place that changes scenes. Fades to black, loads the target scene async
/// (Single mode), then fades back in. Lives on the persistent Services prefab.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class SceneLoader : Singleton<SceneLoader>
{
    [SerializeField] private float FadeDuration = 0.35f;

    [Tooltip("Draw order of the fade overlay relative to other UIDocuments on the same PanelSettings.")]
    [SerializeField] private float SortingOrder = 1000f;

    private UIDocument _document;
    private VisualElement _overlay;

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
        _overlay.style.backgroundColor = Color.black;
        _overlay.style.opacity = 0f;
        _document.rootVisualElement.Add(_overlay);
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

        yield return Fade(0f, 1f);

        AsyncOperation op = SceneManager.LoadSceneAsync(SceneName(id), LoadSceneMode.Single);
        while (!op.isDone) yield return null;

        OnSceneLoaded?.Invoke(id);

        yield return Fade(1f, 0f);

        _overlay.pickingMode = PickingMode.Ignore;
        IsLoading = false;
    }

    // Unscaled time: menus and tutorials may have timeScale at 0.
    private IEnumerator Fade(float from, float to)
    {
        for (float t = 0f; t < FadeDuration; t += Time.unscaledDeltaTime)
        {
            _overlay.style.opacity = Mathf.Lerp(from, to, t / FadeDuration);
            yield return null;
        }
        _overlay.style.opacity = to;
    }
}
