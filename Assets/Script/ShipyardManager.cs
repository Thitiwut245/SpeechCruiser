using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

/// <summary>
/// The shipyard phase of the Game scene. The store is one page on paper (navy ink on a sand
/// gradient from the left edge, projector-safe): the heading and credits, the jobs (or the
/// story mission with its phrases to practise out loud), a link to the upgrades, and SET SAIL.
/// Kept small on purpose, so the time goes on sailing and speaking: the hull is repaired free
/// between voyages, and jobs say only what makes them different. The harbour is built and the
/// ship moored as soon as the scene loads, so the player sees their ship on the right.
/// SET SAIL is a lever: pressing it throws the lever, hides the store, and the cargo loading
/// becomes the mission intro (Space skips it).
/// Attach to the GameObject with the Shipyard UIDocument.
/// </summary>
public class ShipyardManager : MonoBehaviour
{
    [Tooltip("The UIDocument containing your Shipyard.uxml layout")]
    [SerializeField] private UIDocument _document;

    [Tooltip("The Transform where the ship should respawn when starting a mission")]
    [SerializeField] private Transform StartPort;

    [Header("Cargo Loading")]
    [Tooltip("Assign multiple container prefab variants here to spawn them randomly")]
    [SerializeField] private List<GameObject> ContainerPrefabs = new List<GameObject>();
    [SerializeField] private Vector3 CargoStartOffset = new Vector3(0, 1.5f, 0f);
    [SerializeField] private Vector3 ContainerSpacing = new Vector3(2.5f, 2.5f, 6.0f);
    [SerializeField] private int Columns = 3;
    [SerializeField] private int Rows = 4;

    [Header("Camera")]
    [Tooltip("Where the ship sits on screen in the shipyard (0 = centre, 0.5 = right edge), clear of the store panel.")]
    [SerializeField] private float ShipyardScreenX = 0.16f;

    [Header("Cargo Loss")]
    [Tooltip("Seconds a container knocked overboard stays visible before it is removed.")]
    [SerializeField] private float OverboardSeconds = 3f;

    private VisualElement _storePanel;
    private Label _statusText;
    private Label _toastText;
    private Label _creditsText;
    private Button _jobEasyBtn;
    private Button _jobNormalBtn;
    private Button _jobHardBtn;
    private UpgradesPage _upgrades;
    private Button _startMissionBtn;
    private Label _loadingCaption;

    // The page shows the jobs (or the story mission), or, one link away, the upgrades.
    private VisualElement _jobsPage;
    private VisualElement _upgradesPage;
    private Button _upgradesLink;
    private SailLever _lever;
    private VisualElement _storeScrim;
    private const float LeverSeconds = 0.35f;

    private JobSpec _selectedJob = JobSpec.Easy;
    private bool _loading;
    private static ShipyardManager _instance;

    /// <summary>The containers aboard (the destination's crane unloads them); null outside the Game scene.</summary>
    public static List<GameObject> Cargo => _instance != null ? _instance._spawnedContainers : null;

    /// <summary>Cargo is being loaded after SET SAIL: the radio check is over, the voyage is starting.</summary>
    public static bool IsLoadingCargo => _instance != null && _instance._loading;
    private bool _skipLoading;
    private bool _toldReady;
    private bool _restarting;
    private float _toastUntil;
    private CinemachinePositionComposer _composer;
    private CinemachineCamera _vcam;
    private RadioMic _mic;

    private readonly List<GameObject> _spawnedContainers = new List<GameObject>();

    // Story mode: the mission replaces the job list, and its briefing plays over the harbour.
    private StoryMission _story;
    private bool _briefing;
    // Phrase practice: each new phrase lights up once it's said in a radio check.
    private readonly List<Label> _phraseChips = new List<Label>();
    private readonly HashSet<string> _practised = new HashSet<string>();
    private Label _practiceHint;

    private void Start()
    {
        if (_document == null) _document = GetComponent<UIDocument>();
        if (_document == null) return;

        var root = _document.rootVisualElement;
        UiTheme.Apply(root, overSea: true); // the report and the loading caption, over the sea
        // The store stands on a sand gradient from the left edge (no board), in navy ink.
        _storeScrim = UiTheme.SideScrim(1400f, 0.96f, UiTheme.Sand);
        root.Insert(0, _storeScrim);

        _storePanel = root.Q<VisualElement>("store-panel");
        UiTheme.Paper(_storePanel);
        _statusText = root.Q<Label>("status-text");
        _toastText = root.Q<Label>("toast-text");
        _creditsText = root.Q<Label>("credits-text");
        _jobEasyBtn = root.Q<Button>("job-btn-easy");
        _jobNormalBtn = root.Q<Button>("job-btn-normal");
        _jobHardBtn = root.Q<Button>("job-btn-hard");
        _startMissionBtn = root.Q<Button>("start-mission-btn");
        _loadingCaption = root.Q<Label>("loading-caption");
        _jobsPage = root.Q<VisualElement>("page-jobs");
        _upgradesPage = root.Q<VisualElement>("page-upgrades");
        _upgrades = new UpgradesPage(_upgradesPage, OnFitted);
        _upgradesLink = root.Q<Button>("upgrades-link");
        _upgradesLink.clicked += () => SceneLoader.Page(() => ShowUpgrades(!_upgradesPage.ClassListContains("shown")));
        // While something can be fitted, the link beats gently so it is noticed.
        _upgradesLink.schedule.Execute(() =>
            _upgradesLink.EnableInClassList("beat", _upgradesLink.ClassListContains("ready") && !_upgradesLink.ClassListContains("beat"))).Every(650);
        var slot = root.Q<VisualElement>("lever-slot");
        if (slot != null)
        {
            _lever = new SailLever();
            slot.Add(_lever);
        }

        _jobEasyBtn.clicked += () => SelectJob(JobSpec.Easy);
        _jobNormalBtn.clicked += () => SelectJob(JobSpec.Normal);
        _jobHardBtn.clicked += () => SelectJob(JobSpec.Hard);
        _startMissionBtn.clicked += OnStartMission;

        _vcam = FindAnyObjectByType<CinemachineCamera>();
        _composer = FindAnyObjectByType<CinemachinePositionComposer>();

        if (GameSession.Instance != null) GameSession.Instance.OnProgressUpdated += RefreshUI;
        _instance = this;
        _story = StoryProgress.ActiveMission;
        if (_story != null) SetUpStory(root);
        _mic = FindAnyObjectByType<RadioMic>();
        if (_mic != null) _mic.OnRadioStatus += HandleRadioStatus;
        ShipHull.OnHullHit += DropCargo;

        SelectJob(GameSession.Instance != null ? GameSession.Instance.CurrentJob : JobSpec.Easy); // remembers the last job
        PrepareHarbour();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            HandleStateChanged(GameManager.Instance.CurrentState);
        }

        RefreshUI();

        // A voyage left unfinished last time (the game was closed at sea): say what it cost, once.
        string last = GameSession.Instance != null ? GameSession.Instance.LastMissionSummary : "";
        if (!string.IsNullOrEmpty(last) && (last.StartsWith("VOYAGE ABANDONED") || last.StartsWith("TUTORIAL VOYAGE LEFT")))
            ShowToast(last.Substring(0, 1) + last.Substring(1).ToLowerInvariant(), 8f);

        // A restart (pause menu, report, off-course prompt) sails again at once; from a checkpoint
        // the ship is then put back there. Any other visit starts the mission from scratch.
        var restart = VoyageRestart.Take();
        if (restart == VoyageRestart.Mode.None) Checkpoints.Clear();

        // The briefing plays the first time; a retry goes straight to the ship ("Replay briefing" brings it back).
        if (StoryProgress.TakeBriefing(_story) && restart == VoyageRestart.Mode.None) PlayBriefing();
        if (restart != VoyageRestart.Mode.None) SailAgain(restart == VoyageRestart.Mode.FromCheckpoint);
    }

    /// <summary>A restart: Set Sail straight away, the crane hurrying, and back to the checkpoint if asked.</summary>
    private void SailAgain(bool fromCheckpoint)
    {
        if (fromCheckpoint && StoryDirector.Instance != null)
        {
            var checkpoints = StoryDirector.Instance.GetComponent<Checkpoints>();
            if (checkpoints != null) checkpoints.RestoreOnSailing();
        }
        _restarting = true; // a quick retry: no SET SAIL band over the checkpoint's message
        OnStartMission();
        _skipLoading = true;
    }

    /// <summary>Takes <paramref name="count"/> containers off the deck without a splash (a checkpoint had fewer aboard).</summary>
    public static void ShedCargo(int count)
    {
        if (_instance == null || count <= 0 || GameSession.Instance == null) return;
        int lost = GameSession.Instance.LoseCargo(count);
        for (int i = 0; i < lost && _instance._spawnedContainers.Count > 0; i++)
        {
            var box = _instance._spawnedContainers[_instance._spawnedContainers.Count - 1];
            _instance._spawnedContainers.RemoveAt(_instance._spawnedContainers.Count - 1);
            if (box != null) Destroy(box);
        }
    }

    // ------------------------------------------------------------ story mode

    /// <summary>Swaps the job list for the mission card and starts the mission's director.</summary>
    private void SetUpStory(VisualElement root)
    {
        var sheet = Resources.Load<StyleSheet>("Story/StoryUI");
        if (sheet != null) root.styleSheets.Add(sheet);
        StoryDirector.Install(_story);

        var list = _jobEasyBtn.parent;
        int at = list.IndexOf(_jobEasyBtn);
        foreach (var label in root.Query<Label>(className: "section-label").ToList())
        {
            if (label.text != "CHOOSE A JOB") continue;
            label.text = StoryCatalog.Chapters[_story.Chapter - 1].Title.ToUpperInvariant() + "  ·  " + _story.Label.ToUpperInvariant();
        }
        _jobEasyBtn.style.display = DisplayStyle.None;
        _jobNormalBtn.style.display = DisplayStyle.None;
        _jobHardBtn.style.display = DisplayStyle.None;

        var card = new VisualElement();
        card.AddToClassList("story-brief");
        var title = new Label(_story.Title);
        title.AddToClassList("story-brief-title");
        var text = new Label(_story.Summary);
        text.AddToClassList("story-brief-text");
        var words = new VisualElement();
        words.AddToClassList("story-chips");
        foreach (var phrase in _story.Phrases)
        {
            var chip = new Label("\"" + phrase + "\"");
            chip.AddToClassList("story-chip");
            words.Add(chip);
            _phraseChips.Add(chip);
        }
        _practiceHint = new Label();
        _practiceHint.AddToClassList("story-brief-text");
        RefreshPractice();
        var replay = new Button(PlayBriefing) { text = "REPLAY BRIEFING" };
        replay.AddToClassList("story-brief-replay");
        card.Add(title);
        card.Add(text);
        if (_story.Phrases.Length > 0)
        {
            card.Add(words);
            card.Add(_practiceHint);
        }
        card.Add(replay);
        list.Insert(at, card);
    }

    private string TalkHint()
    {
        bool toggle = SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk;
        return (toggle ? "Press " : "Hold ") + (_mic != null ? _mic.OrderKeyName : "T");
    }

    private void RefreshPractice()
    {
        if (_practiceHint == null) return;
        for (int i = 0; i < _phraseChips.Count; i++)
            _phraseChips[i].EnableInClassList("done", _practised.Contains(_story.Phrases[i]));
        _practiceHint.text = _practised.Count >= _story.Phrases.Length
            ? "All practised. Ready to set sail!"
            : TalkHint() + " and say each phrase out loud.";
    }

    /// <summary>A radio check in the shipyard during a story mission: was it one of the new phrases?</summary>
    private bool PracticePhrase(string heard)
    {
        bool polite = false;
        foreach (var phrase in _story.Phrases)
        {
            var verdict = PhraseCheck.Check(phrase, heard);
            if (verdict == PhraseCheck.Verdict.NeedsPlease) polite = true;
            if (verdict != PhraseCheck.Verdict.Pass) continue;
            bool fresh = _practised.Add(phrase);
            RefreshPractice();
            ShowToast("Good! \"" + phrase + "\"" + (fresh && _practised.Count >= _story.Phrases.Length ? " All done!" : ""), 4f);
            return true;
        }
        ShowToast(polite ? "Nearly! Say it politely: add \"please\"." : "I heard \"" + heard + "\". Try one of the new phrases.", 5f);
        return true;
    }

    private void PlayBriefing()
    {
        if (_story == null || _loading || DialoguePlayer.IsPlaying) return;
        _briefing = true;
        if (GameManager.Instance != null) HandleStateChanged(GameManager.Instance.CurrentState);
        string title = "CHAPTER " + _story.Chapter + "  ·  " + _story.Label.ToUpperInvariant() + "  ·  " + _story.Title.ToUpperInvariant();
        var lines = new List<DialogueLine>(_story.Intro);
        if (_story.Phrases.Length > 0 && _practised.Count < _story.Phrases.Length)
            lines.Add(new DialogueLine(CharacterId.Harris, "Before we sail, practise the new words. " + TalkHint() + " and say them on the radio."));
        DialoguePlayer.Play(lines.ToArray(), title, () =>
        {
            _briefing = false;
            if (this != null && GameManager.Instance != null) HandleStateChanged(GameManager.Instance.CurrentState);
        });
    }

    private void OnDestroy()
    {
        // GameSession outlives this scene, so this unsubscribe is what prevents a leak.
        if (GameSession.Instance != null) GameSession.Instance.OnProgressUpdated -= RefreshUI;
        if (_mic != null) _mic.OnRadioStatus -= HandleRadioStatus; // RadioMic outlives this scene
        if (_instance == this) _instance = null;
        ShipHull.OnHullHit -= DropCargo;
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (_toastText != null && _toastText.text.Length > 0 && Time.unscaledTime > _toastUntil) _toastText.text = "";

        var kb = Keyboard.current;
        if (kb == null) return;
        if (_loading && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
        {
            _skipLoading = true;
            if (Quay.Home != null && Quay.Home.Crane != null) Quay.Home.Crane.Hurry = true;
        }
    }

    /// <summary>Builds the route for the selected job and moors the ship at the dock, camera snapped to it.</summary>
    private void PrepareHarbour()
    {
        if (GameSession.Instance != null && _story == null) GameSession.Instance.SetJob(_selectedJob);
        if (LevelGenerator.Instance != null) LevelGenerator.Instance.GenerateLevel();
        PlaceShipAtStart();
        if (_vcam != null) _vcam.PreviousStateIsValid = false; // snap, don't slide
    }

    private void SetShipyardFraming(bool shipyard)
    {
        if (_composer == null) return;
        var composition = _composer.Composition;
        composition.ScreenPosition = new Vector2(shipyard ? ShipyardScreenX : 0f, 0f);
        _composer.Composition = composition;
    }

    /// <summary>Swaps the page between the jobs (or the story mission) and the upgrades.</summary>
    private void ShowUpgrades(bool show)
    {
        _jobsPage.EnableInClassList("shown", !show);
        _upgradesPage.EnableInClassList("shown", show);
        RefreshUpgradeLink();
    }

    /// <summary>The link names how many upgrades are ready to fit, in rust, so they aren't missed.</summary>
    private void RefreshUpgradeLink()
    {
        if (_upgradesLink == null) return;
        bool showing = _upgradesPage.ClassListContains("shown");
        int ready = GameSession.Instance != null ? GameSession.Instance.ReadyUpgrades().Count : 0;
        _upgradesLink.text = showing ? (_story != null ? "BACK TO THE MISSION" : "BACK TO THE JOBS")
                           : ready > 0 ? "UPGRADE SHIP  ·  " + ready + " READY" : "UPGRADE SHIP";
        _upgradesLink.EnableInClassList("ready", ready > 0 && !showing);
        if (ready == 0 || showing) _upgradesLink.RemoveFromClassList("beat");
    }

    /// <summary>Back in port with an upgrade ready to fit: say so once, by name.</summary>
    private void TellReadyUpgrade()
    {
        if (_toldReady || GameSession.Instance == null) return;
        var ready = GameSession.Instance.ReadyUpgrades();
        if (ready.Count == 0) return;
        _toldReady = true;
        var u = ready[0];
        string what = Progression.UpgradeName(u) + " Mk " + UpgradesPage.Roman(GameSession.Instance.Level(u) + 1);
        ShowToast("New upgrade ready to fit: " + what + (ready.Count > 1 ? " and " + (ready.Count - 1) + " more" : "") + ". Open UPGRADE SHIP below.", 8f);
    }

    /// <summary>An upgrade fitted: the ship shows it off and the page says what changed.</summary>
    private void OnFitted(Upgrade u)
    {
        var session = GameSession.Instance;
        if (session == null) return;
        int level = session.Level(u);
        string name = Progression.UpgradeName(u) + "  ·  MK " + UpgradesPage.Roman(level);
        ShowToast(Progression.UpgradeName(u) + " fitted: Mk " + UpgradesPage.Roman(level));
        var nav = ShipNavigator.Instance;
        Transform ship = nav != null && nav.Movement != null ? nav.Movement.Visual : null;
        UpgradeFanfare.Play(ship, _document.rootVisualElement, name, Progression.For(u, level).Effect);
        RefreshUpgradeLink();
    }

    /// <summary>The yard mends the hull between voyages, free: one less thing to manage before sailing.</summary>
    private void RepairForFree()
    {
        var hull = Hull();
        if (hull != null && hull.CurrentHealth < hull.MaxHealthAmount) hull.RepairShip();
    }

    private void SelectJob(JobSpec job)
    {
        if (GameSession.Instance != null && !GameSession.Instance.IsUnlocked(job)) job = JobSpec.Easy;
        _selectedJob = job;

        _jobEasyBtn.EnableInClassList("active-job", job.Name == JobSpec.Easy.Name);
        _jobNormalBtn.EnableInClassList("active-job", job.Name == JobSpec.Normal.Name);
        _jobHardBtn.EnableInClassList("active-job", job.Name == JobSpec.Hard.Name);

        RefreshStartButton();
    }

    private void HandleStateChanged(GameState state)
    {
        var root = _document != null ? _document.rootVisualElement : null;
        if (root == null) return;

        // Hidden while sailing; the results panel owns the screen after a voyage.
        root.style.display = state == GameState.Playing ? DisplayStyle.None : DisplayStyle.Flex;
        bool store = state == GameState.Shipyard && !_loading && !_briefing;
        _storePanel.style.display = store ? DisplayStyle.Flex : DisplayStyle.None;
        if (_storeScrim != null) _storeScrim.style.display = store ? DisplayStyle.Flex : DisplayStyle.None;
        if (state != GameState.Shipyard) _loadingCaption.style.display = DisplayStyle.None;
        SetShipyardFraming(store);

        if (state == GameState.Shipyard)
        {
            if (_lever != null && !_loading) _lever.ResetBack();
            if (!_loading) RepairForFree();
            if (store) TellReadyUpgrade();
            _statusText.text = _story != null ? "Chapter " + _story.Chapter + ": " + StoryCatalog.Chapters[_story.Chapter - 1].Title
                                              : "Shipyard";
            RefreshUI();
        }
    }

    // The key runs a radio check in the shipyard: say how it went in the toast.
    private void HandleRadioStatus(RadioStatus status, string text)
    {
        if (_storePanel == null || _storePanel.resolvedStyle.display != DisplayStyle.Flex) return;
        // "Message cut" arrives the same frame the cut message starts decoding: keep it up.
        if (status == RadioStatus.Decoding && _lastRadioStatus == RadioStatus.Cut) { _lastRadioStatus = status; return; }
        _lastRadioStatus = status;
        if (status == RadioStatus.Tested && _story != null && _story.Phrases.Length > 0 && PracticePhrase(text)) return;
        string line = RadioCheckText(status, text, _mic);
        if (line != null) ShowToast(line, RadioLineSeconds(status));
    }

    /// <summary>
    /// What the title screen and the shipyard show during a radio check; null for statuses
    /// that don't need words. Shared so both screens say the same thing.
    /// </summary>
    private RadioStatus _lastRadioStatus = RadioStatus.Heard;

    /// <summary>How long a radio-check line stays up: listening lines until the next status replaces them.</summary>
    public static float RadioLineSeconds(RadioStatus status) =>
        status == RadioStatus.Recording || status == RadioStatus.Decoding ? 30f : status == RadioStatus.Tested ? 6f : 4f;

    public static string RadioCheckText(RadioStatus status, string text, RadioMic mic)
    {
        bool toggle = SettingsService.Instance != null && SettingsService.Instance.ToggleToTalk;
        string key = mic != null ? mic.OrderKeyName : "T";
        switch (status)
        {
            case RadioStatus.Recording:
                return "Radio check: listening..." + (toggle ? " Press " + key + " again to finish." : " Let go of " + key + " to finish.");
            case RadioStatus.Decoding:
                return "Radio check: listening back...";
            case RadioStatus.Tested:
                return RadioMic.TestHeardPrefix + "\"" + text + "\". Orders work at sea.";
            case RadioStatus.Heard:
            case RadioStatus.Cancelled:
                return null;
            default:
                return string.IsNullOrEmpty(text) ? null : text;
        }
    }

    private ShipHull Hull()
    {
        var nav = ShipNavigator.Instance;
        return nav != null ? nav.GetComponentInChildren<ShipHull>() : null;
    }

    private void RefreshUI()
    {
        var session = GameSession.Instance;
        if (session == null || _creditsText == null) return;

        _creditsText.text = "$" + session.Credits;

        SetJobCard(_jobEasyBtn, "route-easy", JobSpec.Easy, "Rocks only");
        SetJobCard(_jobNormalBtn, "route-normal", JobSpec.Normal, "Rocks and mines");
        SetJobCard(_jobHardBtn, "route-hard", JobSpec.Hard, "A long, crowded route");

        _upgrades.Refresh();
        RefreshUpgradeLink();

        RefreshStartButton();
    }

    /// <summary>Pay at the current tier, what makes the job different, and whether it is open yet.</summary>
    private void SetJobCard(Button card, string descLabel, JobSpec job, string blurb)
    {
        var label = _document.rootVisualElement.Q<Label>(descLabel);
        var session = GameSession.Instance;
        if (label == null || session == null) return;

        var pay = card.Q<Label>(className: "card-pay");
        if (pay != null) pay.text = "$" + session.RewardFor(job);

        bool open = session.IsUnlocked(job);
        card.SetEnabled(open);
        int more = job.UnlockDeliveries - session.MissionsCompleted;
        label.text = open ? blurb : "Opens after " + more + (more == 1 ? " more delivery" : " more deliveries");
    }

    private void RefreshStartButton()
    {
        if (_startMissionBtn != null) _startMissionBtn.SetEnabled(!_loading);
    }

    private void ShowToast(string message, float seconds = 2.5f)
    {
        _toastText.text = message;
        _toastUntil = Time.unscaledTime + seconds;
    }

    private void OnStartMission()
    {
        if (_loading) return;

        RepairForFree(); // in case a hit landed while moored
        if (_mic != null) _mic.EndRadioCheck();
        _loading = true;
        _skipLoading = false;
        _startMissionBtn.SetEnabled(false);
        StartCoroutine(SetSailAfterLever());
    }

    /// <summary>The lever is thrown and seen to land; then the store gives way to the cargo loading.</summary>
    private IEnumerator SetSailAfterLever()
    {
        if (_lever != null)
        {
            _lever.Throw();
            yield return new WaitForSecondsRealtime(LeverSeconds);
        }
        if (!_restarting) ScreenBanner.SetSail();

        // The route is rebuilt for the chosen job and current credits; the dock sits at the
        // origin every time, so the moored ship doesn't move.
        PrepareHarbour();

        _storePanel.style.display = DisplayStyle.None;
        if (_storeScrim != null) _storeScrim.style.display = DisplayStyle.None;
        SetShipyardFraming(false);
        StartCoroutine(LoadContainersRoutine(_story != null ? _story.Job.Containers : _selectedJob.Containers));
    }

    private IEnumerator LoadContainersRoutine(int count)
    {
        var nav = ShipNavigator.Instance;
        if (nav == null) yield break;

        Transform shipTransform = nav.transform;

        ClearContainers();
        _loadingCaption.style.display = DisplayStyle.Flex;

        // Calculate dynamic spacing from the first available prefab's BoxCollider
        Vector3 dynamicSpacing = ContainerSpacing;
        if (ContainerPrefabs != null && ContainerPrefabs.Count > 0)
        {
            foreach (var prefab in ContainerPrefabs)
            {
                if (prefab != null)
                {
                    BoxCollider col = prefab.GetComponentInChildren<BoxCollider>();
                    if (col != null)
                    {
                        Vector3 scale = prefab.transform.localScale;
                        // Add a tiny margin to prevent Z-fighting or clipping
                        dynamicSpacing = new Vector3(col.size.x * scale.x, col.size.y * scale.y, col.size.z * scale.z) + new Vector3(0.05f, 0.05f, 0.05f);
                    }
                    break;
                }
            }
        }

        // Each container's place on deck, in loading order (bottom layer first).
        var slots = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int containersPerLayer = Columns * Rows;
            int layer = i / containersPerLayer;
            int remainder = i % containersPerLayer;
            int zIndex = remainder / Columns; // how deep (rows)
            int xIndex = remainder % Columns; // left to right (cols)
            float xOffset = (xIndex - (Columns - 1) / 2.0f) * dynamicSpacing.x;
            float zOffset = (zIndex - (Rows - 1) / 2.0f) * dynamicSpacing.z;
            slots[i] = CargoStartOffset + new Vector3(xOffset, layer * dynamicSpacing.y, -zOffset);
        }

        // On the banking model so it rolls with the deck; no colliders, or they would join the
        // ship's physics and make it hit things with its cargo.
        Transform deck = nav.Movement != null ? nav.Movement.Visual : shipTransform;
        QuayCrane crane = Quay.Home != null ? Quay.Home.Crane : null;

        // The cargo waits stacked on the quay; the crane lifts it aboard, top of the stack first.
        var boxes = new GameObject[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 waiting = crane != null ? crane.StackSpot(Quay.Home.Berth, i) : shipTransform.position + shipTransform.right * 12f + Vector3.up * (2f + i * 1.3f);
            boxes[i] = SpawnContainer(waiting, shipTransform.rotation);
            foreach (var col in boxes[i].GetComponentsInChildren<Collider>()) col.enabled = false;
            _spawnedContainers.Add(boxes[i]);
        }
        // Quick enough that a full ship is loaded in about 15 seconds.
        float pace = Mathf.Clamp(15f / (count * 2.6f), 0.35f, 1f);

        for (int k = 0; k < count; k++)
        {
            _loadingCaption.text = $"LOADING CARGO {k + 1} / {count}  ·  SPACE TO SKIP";
            Transform box = boxes[count - 1 - k].transform;
            Vector3 local = slots[k];
            if (crane != null)
            {
                crane.Hurry = _skipLoading;
                yield return crane.Move(box, () => deck.TransformPoint(local), pace);
            }
            box.SetParent(deck, true);
            box.localPosition = local;
            box.localRotation = Quaternion.identity;
        }
        if (crane != null) crane.Hurry = false;

        _loadingCaption.text = "ALL ABOARD";
        if (!_skipLoading) yield return new WaitForSeconds(0.5f);
        _loadingCaption.style.display = DisplayStyle.None;

        _loading = false;
        GameSession.Instance.LoadCargo(_spawnedContainers.Count);
        GameManager.Instance.SetState(GameState.Playing);
    }

    private GameObject SpawnContainer(Vector3 position, Quaternion rotation)
    {
        GameObject prefab = ContainerPrefabs != null && ContainerPrefabs.Count > 0
            ? ContainerPrefabs[Random.Range(0, ContainerPrefabs.Count)]
            : null;
        if (prefab != null) return Instantiate(prefab, position, rotation);

        // Placeholder when no prefab is assigned.
        var container = GameObject.CreatePrimitive(PrimitiveType.Cube);
        container.transform.SetPositionAndRotation(position, rotation);
        container.transform.localScale = new Vector3(2f, 2.5f, 4.5f);
        var r = container.GetComponent<Renderer>();
        if (r != null) r.material.color = new Color(Random.value, Random.value, Random.value);
        return container;
    }

    /// <summary>A hit shakes containers off the deck: they fall into the sea and are not paid for.</summary>
    private void DropCargo(int containers)
    {
        if (GameSession.Instance == null) return;

        int lost = GameSession.Instance.LoseCargo(containers);
        for (int i = 0; i < lost && _spawnedContainers.Count > 0; i++)
        {
            GameObject box = _spawnedContainers[_spawnedContainers.Count - 1];
            _spawnedContainers.RemoveAt(_spawnedContainers.Count - 1);
            if (box == null) continue;

            box.transform.SetParent(null, true);
            foreach (var col in box.GetComponentsInChildren<Collider>()) col.enabled = false;
            var body = box.GetComponent<Rigidbody>();
            if (body == null) body = box.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.AddForce((Vector3.up * 4f + Random.insideUnitSphere * 3f), ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
            Destroy(box, OverboardSeconds);
        }
        // ShipHull's damage report says how many went over.
    }

    private void ClearContainers()
    {
        foreach (var c in _spawnedContainers)
        {
            if (c != null) Destroy(c);
        }
        _spawnedContainers.Clear();
    }

    private void PlaceShipAtStart()
    {
        var nav = ShipNavigator.Instance;
        if (nav == null) return;

        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        if (LevelGenerator.Instance != null && LevelGenerator.Instance.IsLevelReady)
        {
            position = LevelGenerator.Instance.StartPosition;
            rotation = LevelGenerator.Instance.StartRotation;
        }
        else if (StartPort != null)
        {
            position = StartPort.position;
            rotation = StartPort.rotation;
        }

        nav.transform.SetPositionAndRotation(position, rotation);
        nav.transform.eulerAngles = new Vector3(0f, nav.transform.eulerAngles.y, 0f);
    }
}
