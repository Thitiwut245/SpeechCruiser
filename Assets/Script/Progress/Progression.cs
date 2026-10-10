using System.Collections.Generic;
using UnityEngine;

/// <summary>The five things a ship can be upgraded in.</summary>
public enum Upgrade { Engine, Rudder, Lookout, Hull, Crew }

/// <summary>What the Captain practises on the radio and at sea, counted across voyages.</summary>
public enum Skill
{
    EngineOrders,   // engine orders in standard phrases
    HelmOrders,     // helm orders in standard phrases
    HelmDegrees,    // ... with degrees ("port twenty")
    Questions,      // reports asked for ("any danger?", "where is the dock?")
    CrewTalk,       // conversations with the crew (small talk, word questions)
    CleanVoyages,   // voyages delivered with no hits
    ClearVoyages,   // voyages where the crew understood 80% of calls or more (5 calls at least)
    Emergencies,    // story emergency calls answered in time
}

/// <summary>
/// Upgrades are earned, not just bought. Each level of an upgrade needs a radio certificate,
/// won by using the right English at sea, and then credits to fit it. This is the learning
/// built into the reward (intrinsic integration, Digital Game-Based Learning): the engine gets
/// faster because the Captain learned the engine orders.
///   Competence (Self-Determination Theory): the progress shown is the player's own skill.
///   Autonomy: five upgrade paths; the player chooses which skill to work on.
///   Goal gradient and endowed progress: bars toward the next certificate, with the tutorial's
///     orders already counted.
///   Spaced practice: each skill counts only a few times a voyage, so a certificate takes
///     several voyages of use, not one voyage of repeating a phrase.
/// Certificates also give the Captain a rank (Cadet to Master), which the crew congratulate.
/// Saved in PlayerPrefs; a new career clears it. Pure rules apart from the saving, so tested.
/// </summary>
public static class Progression
{
    public struct Need
    {
        public Skill Skill;
        public int Target;
        public Need(Skill skill, int target) { Skill = skill; Target = target; }
    }

    public sealed class Certificate
    {
        public string Id, Title, Effect;
        public Upgrade For;
        public int Level;      // the upgrade level it opens (2 or 3)
        public int Credits;    // the fitting cost once earned
        public Need[] Needs;
    }

    /// <summary>The most each skill counts in one voyage (spaced practice).</summary>
    public static int PerVoyage(Skill s)
    {
        switch (s)
        {
            case Skill.EngineOrders: case Skill.HelmOrders: return 4;
            case Skill.HelmDegrees: return 2;
            case Skill.Questions: case Skill.CrewTalk: return 3;
            case Skill.Emergencies: return 3;
            default: return 1; // a voyage is clean or clear once
        }
    }

    public static readonly Certificate[] Catalog =
    {
        new Certificate { Id = "engine2", Title = "Engine Orders I", For = Upgrade.Engine, Level = 2, Credits = 400, Effect = "20% faster",
            Needs = new[] { new Need(Skill.EngineOrders, 8) } },
        new Certificate { Id = "engine3", Title = "Engine Orders II", For = Upgrade.Engine, Level = 3, Credits = 900, Effect = "40% faster",
            Needs = new[] { new Need(Skill.EngineOrders, 20), new Need(Skill.ClearVoyages, 2) } },
        new Certificate { Id = "rudder2", Title = "Helm Orders I", For = Upgrade.Rudder, Level = 2, Credits = 400, Effect = "15% sharper turns",
            Needs = new[] { new Need(Skill.HelmOrders, 8) } },
        new Certificate { Id = "rudder3", Title = "Helm Orders II", For = Upgrade.Rudder, Level = 3, Credits = 900, Effect = "30% sharper turns",
            Needs = new[] { new Need(Skill.HelmOrders, 20), new Need(Skill.HelmDegrees, 4) } },
        new Certificate { Id = "lookout2", Title = "Lookout Reports I", For = Upgrade.Lookout, Level = 2, Credits = 350, Effect = "radar sees 25% further",
            Needs = new[] { new Need(Skill.Questions, 6) } },
        new Certificate { Id = "lookout3", Title = "Lookout Reports II", For = Upgrade.Lookout, Level = 3, Credits = 800, Effect = "radar sees 50% further",
            Needs = new[] { new Need(Skill.Questions, 15), new Need(Skill.ClearVoyages, 1) } },
        new Certificate { Id = "hull2", Title = "Safe Passage I", For = Upgrade.Hull, Level = 2, Credits = 500, Effect = "takes one more hit",
            Needs = new[] { new Need(Skill.CleanVoyages, 2) } },
        new Certificate { Id = "hull3", Title = "Safe Passage II", For = Upgrade.Hull, Level = 3, Credits = 1100, Effect = "takes two more hits",
            Needs = new[] { new Need(Skill.CleanVoyages, 4), new Need(Skill.Emergencies, 2) } },
        new Certificate { Id = "crew2", Title = "Crew Trust I", For = Upgrade.Crew, Level = 2, Credits = 300, Effect = "\"Did you mean...?\" when they miss an order",
            Needs = new[] { new Need(Skill.CrewTalk, 6) } },
        new Certificate { Id = "crew3", Title = "Crew Trust II", For = Upgrade.Crew, Level = 3, Credits = 700, Effect = "earlier warnings, more time in emergencies",
            Needs = new[] { new Need(Skill.CrewTalk, 15), new Need(Skill.ClearVoyages, 2) } },
    };

    public static readonly string[] Ranks = { "Cadet", "Third Mate", "Second Mate", "Chief Mate", "Master" };
    private static readonly int[] RankAt = { 0, 2, 4, 6, 9 };

    /// <summary>The rank for <paramref name="certificates"/> earned.</summary>
    public static string RankFor(int certificates)
    {
        int r = 0;
        for (int i = 0; i < RankAt.Length; i++) if (certificates >= RankAt[i]) r = i;
        return Ranks[r];
    }

    /// <summary>Certificates still needed for the next rank (0 at Master).</summary>
    public static int ToNextRank(int certificates)
    {
        foreach (int at in RankAt) if (at > certificates) return at - certificates;
        return 0;
    }

    public static string SkillText(Skill s)
    {
        switch (s)
        {
            case Skill.EngineOrders: return "Standard engine orders";
            case Skill.HelmOrders: return "Standard helm orders";
            case Skill.HelmDegrees: return "Helm orders with degrees";
            case Skill.Questions: return "Reports asked for";
            case Skill.CrewTalk: return "Talks with the crew";
            case Skill.CleanVoyages: return "Voyages with no hits";
            case Skill.ClearVoyages: return "Voyages the crew understood";
            default: return "Emergency calls in time";
        }
    }

    public static string UpgradeName(Upgrade u)
    {
        switch (u)
        {
            case Upgrade.Engine: return "ENGINE";
            case Upgrade.Rudder: return "RUDDER";
            case Upgrade.Lookout: return "LOOKOUT GEAR";
            case Upgrade.Hull: return "HULL PLATING";
            default: return "CREW EXPERIENCE";
        }
    }

    public static Certificate For(Upgrade u, int level)
    {
        foreach (var c in Catalog) if (c.For == u && c.Level == level) return c;
        return null;
    }

    // ------------------------------------------------------------------ the book (saved)

    /// <summary>Where the counts and certificates are kept; swappable so the tests don't touch the player's save.</summary>
    public interface IStore
    {
        int Get(string key);
        void Set(string key, int value);
        void Save();
    }

    private sealed class Prefs : IStore
    {
        public int Get(string key) => PlayerPrefs.GetInt(key, 0);
        public void Set(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    public static IStore Store = new Prefs();
    private const string Prefix = "progress.";

    public static int Count(Skill s) => Store.Get(Prefix + "skill." + s);
    public static bool Earned(Certificate c) => c != null && Store.Get(Prefix + "cert." + c.Id) == 1;
    public static int EarnedCount { get { int n = 0; foreach (var c in Catalog) if (Earned(c)) n++; return n; } }
    public static string Rank => RankFor(EarnedCount);

    /// <summary>How far toward <paramref name="c"/>, 0..1 (the average over its needs).</summary>
    public static float Fraction(Certificate c)
    {
        if (Earned(c)) return 1f;
        float sum = 0f;
        foreach (var n in c.Needs) sum += Mathf.Clamp01(Count(n.Skill) / (float)n.Target);
        return sum / c.Needs.Length;
    }

    /// <summary>Met every need (it is awarded at the end of the next voyage, or now by <see cref="Award"/>).</summary>
    public static bool Ready(Certificate c)
    {
        foreach (var n in c.Needs) if (Count(n.Skill) < n.Target) return false;
        return true;
    }

    /// <summary>Marks every met certificate earned; returns the new ones.</summary>
    public static List<Certificate> Award()
    {
        var fresh = new List<Certificate>();
        foreach (var c in Catalog)
        {
            // Mk III's certificate comes after Mk II's.
            var before = c.Level > 2 ? For(c.For, c.Level - 1) : null;
            if (Earned(c) || !Ready(c) || (before != null && !Earned(before))) continue;
            Store.Set(Prefix + "cert." + c.Id, 1);
            fresh.Add(c);
        }
        return fresh;
    }

    public struct Voyage
    {
        public int EngineOrders, HelmOrders, HelmDegrees, Questions, CrewTalk, Emergencies;
        public bool Clean, Clear;
    }

    public struct Outcome
    {
        public List<Certificate> Earned;
        public string PromotedTo; // null if the rank didn't change
    }

    /// <summary>A voyage ended: its practice is counted (up to each skill's cap), certificates and rank follow.</summary>
    public static Outcome EndVoyage(Voyage v)
    {
        int before = EarnedCount;
        Add(Skill.EngineOrders, v.EngineOrders);
        Add(Skill.HelmOrders, v.HelmOrders);
        Add(Skill.HelmDegrees, v.HelmDegrees);
        Add(Skill.Questions, v.Questions);
        Add(Skill.CrewTalk, v.CrewTalk);
        Add(Skill.Emergencies, v.Emergencies);
        Add(Skill.CleanVoyages, v.Clean ? 1 : 0);
        Add(Skill.ClearVoyages, v.Clear ? 1 : 0);
        var fresh = Award();
        Store.Save();
        string rankBefore = RankFor(before), rankNow = RankFor(EarnedCount);
        return new Outcome { Earned = fresh, PromotedTo = rankNow != rankBefore ? rankNow : null };
    }

    private static void Add(Skill s, int amount)
    {
        if (amount <= 0) return;
        Store.Set(Prefix + "skill." + s, Count(s) + Mathf.Min(amount, PerVoyage(s)));
    }

    /// <summary>
    /// Once per career: the tutorial's drill counts toward the first engine and helm
    /// certificates, and upgrades bought before certificates existed keep theirs.
    /// </summary>
    public static void Begin(int engineLevel, int rudderLevel, bool tutorialDone)
    {
        if (tutorialDone && Store.Get(Prefix + "headstart") == 0)
        {
            Store.Set(Prefix + "headstart", 1);
            Store.Set(Prefix + "skill." + Skill.EngineOrders, Count(Skill.EngineOrders) + 2);
            Store.Set(Prefix + "skill." + Skill.HelmOrders, Count(Skill.HelmOrders) + 2);
        }
        for (int l = 2; l <= engineLevel; l++) Store.Set(Prefix + "cert." + For(Upgrade.Engine, l).Id, 1);
        for (int l = 2; l <= rudderLevel; l++) Store.Set(Prefix + "cert." + For(Upgrade.Rudder, l).Id, 1);
        Store.Save();
    }

    /// <summary>A new career: no counts, no certificates.</summary>
    public static void Reset()
    {
        foreach (Skill s in System.Enum.GetValues(typeof(Skill))) Store.Set(Prefix + "skill." + s, 0);
        foreach (var c in Catalog) Store.Set(Prefix + "cert." + c.Id, 0);
        Store.Set(Prefix + "headstart", 0);
        Store.Save();
    }

    /// <summary>The certificate closest to done that isn't earned yet (for "next goal"), or null.</summary>
    public static Certificate Closest()
    {
        Certificate best = null;
        float bestF = -1f;
        foreach (var c in Catalog)
        {
            if (Earned(c)) continue;
            var before = c.Level > 2 ? For(c.For, c.Level - 1) : null;
            if (before != null && !Earned(before)) continue;
            float f = Fraction(c);
            if (f > bestF) { bestF = f; best = c; }
        }
        return best;
    }
}
