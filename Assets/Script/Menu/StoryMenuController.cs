using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The story screen on the title menu: a tab per chapter (locked until reached), the chapter's
/// card with a portrait, and its missions. Done missions say so, the next one is outlined, and
/// later ones are locked. Clicking an open mission sails it. Built in code inside the title
/// screen's container, styled by Resources/Story/StoryUI.uss.
/// </summary>
public class StoryMenuController
{
    private static readonly CharacterId[] ChapterFaces = { CharacterId.Ada, CharacterId.Finn, CharacterId.Harris };

    private readonly VisualElement _panel;
    private readonly VisualElement _tabs;
    private readonly VisualElement _chapterCard;
    private readonly VisualElement _missions;
    private readonly Label _progress;
    private readonly Action _onBack;
    private int _chapter;

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

        var eyebrow = new Label("STORY");
        eyebrow.AddToClassList("story-eyebrow");
        var title = new Label(StoryCatalog.StoryTitle);
        title.AddToClassList("story-title");
        _tabs = new VisualElement();
        _tabs.AddToClassList("story-tabs");
        _chapterCard = new VisualElement();
        _chapterCard.AddToClassList("story-chapter");
        _missions = new VisualElement();

        var footer = new VisualElement();
        footer.AddToClassList("story-footer");
        _progress = new Label();
        _progress.AddToClassList("story-progress");
        var back = new Button(() => _onBack?.Invoke()) { text = "Back" };
        back.AddToClassList("menu-button");
        back.AddToClassList("story-back");
        footer.Add(_progress);
        footer.Add(back);

        _panel.Add(eyebrow);
        _panel.Add(title);
        _panel.Add(_tabs);
        _panel.Add(_chapterCard);
        _panel.Add(_missions);
        _panel.Add(footer);
    }

    /// <summary>Opens on the chapter being played (or the one with the next mission).</summary>
    public void Show()
    {
        var focus = StoryProgress.ActiveMission ?? StoryProgress.NextToPlay;
        _chapter = focus != null ? focus.Chapter : StoryCatalog.Chapters.Length;
        Render();
        _panel.style.display = DisplayStyle.Flex;
    }

    public void Hide()
    {
        _panel.style.display = DisplayStyle.None;
    }

    private void Render()
    {
        var chapters = StoryCatalog.Chapters;
        var next = StoryProgress.NextToPlay;

        _tabs.Clear();
        foreach (var chapter in chapters)
        {
            var c = chapter;
            bool open = StoryProgress.IsUnlocked(c);
            var tab = new Button(() => { _chapter = c.Number; Render(); })
            {
                text = "Chapter " + c.Number + "\n" + (open ? c.Title : "Locked"),
            };
            tab.AddToClassList("story-tab");
            tab.EnableInClassList("selected", c.Number == _chapter);
            tab.SetEnabled(open);
            _tabs.Add(tab);
        }

        var shown = chapters[Mathf.Clamp(_chapter - 1, 0, chapters.Length - 1)];
        _chapterCard.Clear();
        var portrait = new VisualElement();
        portrait.AddToClassList("story-chapter-portrait");
        var face = PortraitStudio.Get(ChapterFaces[(shown.Number - 1) % ChapterFaces.Length]) as RenderTexture;
        if (face != null) portrait.style.backgroundImage = Background.FromRenderTexture(face);
        var text = new VisualElement();
        text.AddToClassList("story-chapter-text");
        var name = new Label("Chapter " + shown.Number + ": " + shown.Title);
        name.AddToClassList("story-chapter-name");
        var blurb = new Label(shown.Blurb);
        blurb.AddToClassList("story-chapter-blurb");
        var focusLine = new Label("You will learn: " + shown.Focus);
        focusLine.AddToClassList("story-chapter-focus");
        text.Add(name);
        text.Add(blurb);
        text.Add(focusLine);
        _chapterCard.Add(portrait);
        _chapterCard.Add(text);

        _missions.Clear();
        foreach (var mission in shown.Missions)
        {
            var m = mission;
            bool open = StoryProgress.IsUnlocked(m);
            bool done = StoryProgress.IsDone(m);
            var row = new Button(() => Sail(m));
            row.AddToClassList("story-mission");
            row.EnableInClassList("next", m == next);
            row.SetEnabled(open);

            var number = new Label(m.Id);
            number.AddToClassList("story-mission-number");
            var main = new VisualElement();
            main.AddToClassList("story-mission-main");
            var title = new Label(m.Title);
            title.AddToClassList("story-mission-title");
            var summary = new Label(open ? m.Summary : "Finish mission " + StoryCatalog.Missions[Mathf.Max(0, StoryCatalog.IndexOf(m) - 1)].Id + " first.");
            summary.AddToClassList("story-mission-summary");
            main.Add(title);
            main.Add(summary);

            var tag = new Label(done ? "DONE" : !open ? "LOCKED" : m == next ? "NEXT" : m.Goal == MissionGoal.Rescue ? "RESCUE" : "DELIVERY");
            tag.AddToClassList("story-mission-tag");
            tag.EnableInClassList("done", done);
            tag.EnableInClassList("new", !done && m == next);

            row.Add(number);
            row.Add(main);
            row.Add(tag);
            _missions.Add(row);
        }

        int count = StoryCatalog.Missions.Count;
        int finished = StoryProgress.DoneCount;
        _progress.text = finished >= count ? "Story complete! All " + count + " missions done." : finished + " of " + count + " missions done";
    }

    private static void Sail(StoryMission mission)
    {
        if (!StoryProgress.IsUnlocked(mission) || SceneLoader.Instance == null || SceneLoader.Instance.IsLoading) return;
        StoryProgress.Begin(mission);
        SceneLoader.Instance.Load(SceneId.Game);
    }
}
