using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The story screen on the title menu, on the title's paper (navy ink on sand). Tabs pick a
/// chapter (locked until reached). The chapter is a chart in ink: its title and what it
/// teaches, a portrait, and the voyage route through a marker per mission (solid where sailed,
/// dashed ahead; done, next or locked). Clicking a marker shows that mission below: its goal
/// in a line, the weather and danger, the new words to learn, and SET SAIL. Only what helps
/// the player choose and speak: no pay or client tags. Built in code inside the title
/// screen's container, styled by Resources/Story/StoryUI.uss.
/// </summary>
public class StoryMenuController
{
    private static readonly CharacterId[] ChapterFaces = { CharacterId.Ada, CharacterId.Finn, CharacterId.Harris };
    private static readonly Vector2[] ThreeStops = { new Vector2(0.17f, 0.76f), new Vector2(0.5f, 0.6f), new Vector2(0.83f, 0.76f) };
    private static readonly Vector2[] FourStops = { new Vector2(0.13f, 0.76f), new Vector2(0.38f, 0.6f), new Vector2(0.62f, 0.74f), new Vector2(0.87f, 0.6f) };

    /// <summary>Where a chapter's missions sit on its chart: three or four ports.</summary>
    private static Vector2[] StopsFor(StoryChapter chapter) => chapter.Missions.Length > 3 ? FourStops : ThreeStops;

    private readonly VisualElement _panel;
    private readonly VisualElement _tabs;
    private readonly VisualElement _chart;
    private readonly VisualElement _detail;
    private readonly Label _progress;
    private readonly Action _onBack;
    private int _chapter;
    private StoryMission _selected;

    public bool IsOpen => _panel.style.display == DisplayStyle.Flex;

    public StoryMenuController(VisualElement root, Action onBack)
    {
        _onBack = onBack;
        var sheet = Resources.Load<StyleSheet>("Story/StoryUI");
        if (sheet != null) root.styleSheets.Add(sheet);

        _panel = new VisualElement { name = "StoryPanel" };
        _panel.AddToClassList("story-panel");
        _panel.style.display = DisplayStyle.None;
        var container = root.Q<VisualElement>("MainMenuContainer") ?? root;
        container.Add(_panel);

        var title = new Label(StoryCatalog.StoryTitle);
        title.AddToClassList("story-title");
        _tabs = new VisualElement();
        _tabs.AddToClassList("story-tabs");
        _chart = new VisualElement();
        _chart.AddToClassList("chart-card");
        _detail = new VisualElement();
        _detail.AddToClassList("mission-detail");

        var footer = new VisualElement();
        footer.AddToClassList("story-footer");
        _progress = new Label();
        _progress.AddToClassList("story-progress");
        var back = new Button(() => _onBack?.Invoke()) { text = "Back" };
        back.AddToClassList("story-back");
        footer.Add(_progress);
        footer.Add(back);

        _panel.Add(title);
        _panel.Add(_tabs);
        _panel.Add(_chart);
        _panel.Add(_detail);
        _panel.Add(footer);
    }

    /// <summary>Opens on the chapter being played (or the one with the next mission), that mission selected.</summary>
    public void Show()
    {
        var focus = StoryProgress.ActiveMission ?? StoryProgress.NextToPlay;
        // Back from a finished mission: point at the next one.
        if (StoryProgress.ActiveMission != null && StoryProgress.IsDone(StoryProgress.ActiveMission))
            focus = StoryProgress.NextToPlay ?? StoryProgress.ActiveMission;
        _chapter = focus != null ? focus.Chapter : StoryCatalog.Chapters.Length;
        _selected = focus;
        Render();
        _panel.style.display = DisplayStyle.Flex;
    }

    public void Hide()
    {
        _panel.style.display = DisplayStyle.None;
    }

    private StoryChapter Shown => StoryCatalog.Chapters[Mathf.Clamp(_chapter - 1, 0, StoryCatalog.Chapters.Length - 1)];

    private void Render()
    {
        var shown = Shown;
        if (_selected == null || _selected.Chapter != shown.Number) _selected = DefaultIn(shown);
        RenderTabs();
        RenderChart(shown);
        RenderDetail(_selected);

        int count = StoryCatalog.Missions.Count;
        int finished = StoryProgress.DoneCount;
        _progress.text = finished >= count ? "Story complete! All " + count + " missions done." : finished + " of " + count + " missions done";
    }

    /// <summary>The chapter's next mission to play, else its first.</summary>
    private static StoryMission DefaultIn(StoryChapter chapter)
    {
        foreach (var m in chapter.Missions) if (!StoryProgress.IsDone(m) && StoryProgress.IsUnlocked(m)) return m;
        return chapter.Missions[0];
    }

    private void RenderTabs()
    {
        _tabs.Clear();
        foreach (var chapter in StoryCatalog.Chapters)
        {
            var c = chapter;
            bool open = StoryProgress.IsUnlocked(c);
            var tab = new Button(() => { _chapter = c.Number; _selected = null; Render(); });
            tab.AddToClassList("story-tab");
            tab.EnableInClassList("selected", c.Number == _chapter);
            tab.SetEnabled(open);
            var number = new Label("CHAPTER " + c.Number);
            number.AddToClassList("story-tab__number");
            var name = new Label(open ? c.Title : "Locked");
            name.AddToClassList("story-tab__name");
            tab.Add(number);
            tab.Add(name);
            _tabs.Add(tab);
        }
    }

    private void RenderChart(StoryChapter chapter)
    {
        _chart.Clear();

        var route = new ChartRoute(chapter.Number * 97 + 11);
        route.AddToClassList("chart-fill");
        int sailed = 0;
        foreach (var m in chapter.Missions) if (StoryProgress.IsDone(m)) sailed++;
        var stops = StopsFor(chapter);
        route.SetRoute(stops, sailed);
        _chart.Add(route);

        var rose = new CompassRose(74f, UiTheme.Ink, UiTheme.InkSoft, UiTheme.Rust);
        rose.AddToClassList("chart-rose");
        _chart.Add(rose);

        var text = new VisualElement();
        text.AddToClassList("chart-text");
        var name = new Label("Chapter " + chapter.Number + ": " + chapter.Title);
        name.AddToClassList("chart-title");
        var focus = new Label("You will learn: " + chapter.Focus);
        focus.AddToClassList("chart-focus");
        text.Add(name);
        text.Add(focus);
        _chart.Add(text);

        var portrait = new VisualElement();
        portrait.AddToClassList("chart-portrait");
        var face = PortraitStudio.Get(ChapterFaces[(chapter.Number - 1) % ChapterFaces.Length]) as RenderTexture;
        if (face != null) portrait.style.backgroundImage = Background.FromRenderTexture(face);
        _chart.Add(portrait);

        var next = StoryProgress.NextToPlay;
        for (int i = 0; i < chapter.Missions.Length && i < stops.Length; i++)
        {
            var m = chapter.Missions[i];
            bool open = StoryProgress.IsUnlocked(m), done = StoryProgress.IsDone(m);
            var marker = new Button(() => { _selected = m; Render(); }) { text = m.Id };
            marker.AddToClassList("chart-marker");
            marker.EnableInClassList("done", done);
            marker.EnableInClassList("next", m == next);
            marker.EnableInClassList("locked", !open);
            marker.EnableInClassList("selected", m == _selected);
            marker.style.left = Length.Percent(stops[i].x * 100f);
            marker.style.top = Length.Percent(stops[i].y * 100f);
            _chart.Add(marker);

            var caption = new Label(open ? m.Title : "?");
            caption.AddToClassList("chart-marker-caption");
            caption.style.left = Length.Percent(stops[i].x * 100f);
            caption.style.top = Length.Percent(stops[i].y * 100f);
            _chart.Add(caption);
        }
    }

    private void RenderDetail(StoryMission m)
    {
        _detail.Clear();
        if (m == null) return;
        bool open = StoryProgress.IsUnlocked(m), done = StoryProgress.IsDone(m);

        var head = new VisualElement();
        head.AddToClassList("mission-head");
        var number = new Label(m.Id);
        number.AddToClassList("mission-number");
        var text = new VisualElement();
        text.AddToClassList("mission-text");
        var title = new Label(m.Title);
        title.AddToClassList("mission-title");
        var summary = new Label(open ? m.Summary : "Finish mission " + StoryCatalog.Missions[Mathf.Max(0, StoryCatalog.IndexOf(m) - 1)].Id + " first.");
        summary.AddToClassList("mission-summary");
        text.Add(title);
        text.Add(summary);
        head.Add(number);
        head.Add(text);
        _detail.Add(head);

        var tags = new VisualElement();
        tags.AddToClassList("mission-tags");
        if (m.Goal == MissionGoal.Rescue) Tag(tags, "RESCUE", "goal");
        if (m.Weather == Weather.Storm) Tag(tags, "STORM", "weather");
        else if (m.Weather == Weather.NightFog) Tag(tags, "NIGHT FOG", "weather");
        else if (m.Weather == Weather.Fog) Tag(tags, "FOG", "weather");
        if (m.Job.Mines) Tag(tags, "MINES", "danger");
        if (done) Tag(tags, "DONE", "done");
        if (tags.childCount > 0) _detail.Add(tags);

        if (open && m.Phrases.Length > 0)
        {
            var words = new VisualElement();
            words.AddToClassList("mission-words");
            var caption = new Label("NEW WORDS");
            caption.AddToClassList("mission-words__caption");
            words.Add(caption);
            foreach (var phrase in m.Phrases)
            {
                var word = new Label(phrase);
                word.AddToClassList("mission-word");
                words.Add(word);
            }
            _detail.Add(words);
        }

        var sail = new Button(() => Sail(m)) { text = !open ? "LOCKED" : done ? "SAIL AGAIN" : "SET SAIL" };
        sail.AddToClassList("mission-sail");
        sail.SetEnabled(open);
        _detail.Add(sail);
    }

    private static void Tag(VisualElement parent, string text, string kind)
    {
        var tag = new Label(text);
        tag.AddToClassList("mission-tag");
        tag.AddToClassList("mission-tag--" + kind);
        parent.Add(tag);
    }

    private static void Sail(StoryMission mission)
    {
        if (!StoryProgress.IsUnlocked(mission) || SceneLoader.Instance == null || SceneLoader.Instance.IsLoading) return;
        StoryProgress.Begin(mission);
        SceneLoader.Instance.Load(SceneId.Game);
    }
}
