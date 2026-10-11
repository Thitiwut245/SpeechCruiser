using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The shipyard's upgrade page, on paper: the Captain's rank at the top, then a card for each
/// of the five upgrades with its level, what the next level does, and the radio certificate it
/// needs, with a bar for each part of it (how many standard engine orders so far, of how many).
/// FIT pays for a level whose certificate is earned. The bars are the goal in view: the next
/// upgrade is something to work toward on the radio, not just to buy.
/// </summary>
public class UpgradesPage
{
    private static readonly Color Ink = new Color(0.086f, 0.141f, 0.251f);
    private static readonly Color InkDim = new Color(0.243f, 0.29f, 0.392f);
    private static readonly Color Rust = new Color(0.627f, 0.196f, 0.094f);
    private static readonly Color Teal = new Color(0.047f, 0.424f, 0.384f);

    private readonly VisualElement _page;
    private readonly System.Action<string> _toast;
    private ScrollView _scroll;

    public UpgradesPage(VisualElement page, System.Action<string> toast)
    {
        _page = page;
        _toast = toast;
        // The five cards are taller than the panel: the page may shrink, and the cards scroll.
        if (_page != null) _page.AddToClassList("scrolls");
    }

    public void Refresh()
    {
        var session = GameSession.Instance;
        if (_page == null || session == null) return;
        Vector2 keep = _scroll != null ? _scroll.scrollOffset : Vector2.zero;
        _page.Clear();

        int earned = Progression.EarnedCount;
        var rank = new Label("RANK  ·  " + Progression.Rank.ToUpperInvariant());
        rank.AddToClassList("section-label");
        _page.Add(rank);
        int more = Progression.ToNextRank(earned);
        var note = new Label(earned + " of " + Progression.Catalog.Length + " radio certificates" +
                             (more > 0 ? "  ·  " + more + " more to " + Progression.RankFor(earned + more) : "  ·  the highest rank"));
        note.AddToClassList("card-desc");
        note.style.marginBottom = 6f;
        _page.Add(note);

        _scroll = new ScrollView(ScrollViewMode.Vertical);
        _scroll.AddToClassList("upgrade-scroll");
        _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
        _scroll.mouseWheelScrollSize = 80f;
        foreach (Upgrade u in System.Enum.GetValues(typeof(Upgrade))) _scroll.Add(Card(session, u));
        _page.Add(_scroll);
        // Back where it was (after fitting an upgrade the page is rebuilt).
        _scroll.schedule.Execute(() => _scroll.scrollOffset = keep);
    }

    private VisualElement Card(GameSession session, Upgrade u)
    {
        int level = session.Level(u);
        var card = new VisualElement();
        card.AddToClassList("card");
        card.AddToClassList("upgrade-card");
        card.style.paddingTop = 6f; card.style.paddingBottom = 6f;

        var main = new VisualElement();
        main.AddToClassList("card-main");
        var title = new Label(Progression.UpgradeName(u) + "  ·  MK " + Roman(level));
        title.AddToClassList("card-title");
        title.style.fontSize = 21f;
        main.Add(title);

        var button = new Button();
        button.AddToClassList("buy-btn");
        if (level >= GameSession.MaxUpgradeLevel)
        {
            main.Add(Line("Fully fitted.", InkDim));
            button.text = "MAX";
            button.SetEnabled(false);
        }
        else
        {
            var cert = Progression.For(u, level + 1);
            bool has = Progression.Earned(cert);
            main.Add(Line("Mk " + Roman(level + 1) + ": " + cert.Effect, InkDim));
            if (has) main.Add(Line("Certificate earned: " + cert.Title, Teal));
            else
            {
                main.Add(Line("Needs the certificate " + cert.Title + ":", Ink));
                foreach (var need in cert.Needs) main.Add(Bar(need));
            }
            string why = session.WhyNot(u);
            button.text = why == null ? "FIT  $" + cert.Credits : why == "credits" ? "$" + cert.Credits : "LOCKED";
            button.SetEnabled(why == null);
            button.clicked += () =>
            {
                if (!session.TryBuy(u)) return;
                _toast?.Invoke(Progression.UpgradeName(u) + " fitted: Mk " + Roman(session.Level(u)));
                Refresh();
            };
        }
        card.Add(main);
        card.Add(button);
        return card;
    }

    private static Label Line(string text, Color colour)
    {
        var l = new Label(text);
        l.AddToClassList("card-desc");
        l.style.fontSize = 16f;
        l.style.color = colour;
        l.style.marginTop = 1f;
        return l;
    }

    /// <summary>One part of a certificate: what to do, how many so far, and a bar.</summary>
    private static VisualElement Bar(Progression.Need need)
    {
        int have = Mathf.Min(Progression.Count(need.Skill), need.Target);
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 2f;
        var text = Line(Progression.SkillText(need.Skill) + "  " + have + " / " + need.Target, have >= need.Target ? Teal : Ink);
        text.style.width = 330f;
        var track = new VisualElement();
        track.style.flexGrow = 1f;
        track.style.height = 5f;
        track.style.marginLeft = 8f;
        track.style.marginRight = 14f;
        track.style.backgroundColor = new Color(Ink.r, Ink.g, Ink.b, 0.15f);
        var fill = new VisualElement();
        fill.style.height = 5f;
        fill.style.width = Length.Percent(100f * have / need.Target);
        fill.style.backgroundColor = have >= need.Target ? Teal : Rust;
        track.Add(fill);
        row.Add(text);
        row.Add(track);
        return row;
    }

    private static string Roman(int n) => n == 1 ? "I" : n == 2 ? "II" : "III";
}
