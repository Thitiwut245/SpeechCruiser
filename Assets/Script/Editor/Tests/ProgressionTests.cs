using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Earned upgrades: practice counts up to a cap each voyage (spaced practice), certificates are
/// awarded in order, ranks follow, and the tutorial's head start is given once. Runs on an
/// in-memory store, never the player's save.
/// </summary>
public class ProgressionTests
{
    private sealed class Memory : Progression.IStore
    {
        public readonly Dictionary<string, int> Values = new Dictionary<string, int>();
        public int Get(string key) => Values.TryGetValue(key, out int v) ? v : 0;
        public void Set(string key, int value) => Values[key] = value;
        public void Save() { }
    }

    private Progression.IStore _real;

    [SetUp] public void UseMemory() { _real = Progression.Store; Progression.Store = new Memory(); }
    [TearDown] public void PutBack() { Progression.Store = _real; }

    private static Progression.Certificate Cert(string id)
    {
        foreach (var c in Progression.Catalog) if (c.Id == id) return c;
        return null;
    }

    [Test]
    public void OneVoyageCountsOnlyUpToTheCap()
    {
        Progression.EndVoyage(new Progression.Voyage { EngineOrders = 30 });
        Assert.AreEqual(Progression.PerVoyage(Skill.EngineOrders), Progression.Count(Skill.EngineOrders));
        Assert.IsFalse(Progression.Earned(Cert("engine2")), "one voyage of repeating isn't enough");
    }

    [Test]
    public void ACertificateTakesSeveralVoyagesAndOpensItsUpgrade()
    {
        Progression.EndVoyage(new Progression.Voyage { EngineOrders = 5 });
        var outcome = Progression.EndVoyage(new Progression.Voyage { EngineOrders = 5 });
        CollectionAssert.Contains(outcome.Earned, Cert("engine2"));
        Assert.IsTrue(Progression.Earned(Cert("engine2")));
        Assert.AreEqual(1f, Progression.Fraction(Cert("engine2")));
    }

    [Test]
    public void MkThreeComesAfterMkTwo()
    {
        // Enough clean voyages for both hull certificates' first need, but Mk III also needs emergencies.
        for (int i = 0; i < 4; i++) Progression.EndVoyage(new Progression.Voyage { Clean = true });
        Assert.IsTrue(Progression.Earned(Cert("hull2")));
        Assert.IsFalse(Progression.Earned(Cert("hull3")));
        var outcome = Progression.EndVoyage(new Progression.Voyage { Emergencies = 2 });
        CollectionAssert.Contains(outcome.Earned, Cert("hull3"));
    }

    [Test]
    public void RanksRiseWithCertificates()
    {
        Assert.AreEqual("Cadet", Progression.RankFor(0));
        Assert.AreEqual("Third Mate", Progression.RankFor(2));
        Assert.AreEqual("Second Mate", Progression.RankFor(5));
        Assert.AreEqual("Master", Progression.RankFor(Progression.Catalog.Length));
        Assert.AreEqual(2, Progression.ToNextRank(0));
        Assert.AreEqual(0, Progression.ToNextRank(Progression.Catalog.Length));
    }

    [Test]
    public void APromotionIsReported()
    {
        Progression.EndVoyage(new Progression.Voyage { EngineOrders = 4, HelmOrders = 4 });
        var outcome = Progression.EndVoyage(new Progression.Voyage { EngineOrders = 4, HelmOrders = 4 });
        Assert.AreEqual(2, outcome.Earned.Count);
        Assert.AreEqual("Third Mate", outcome.PromotedTo);
    }

    [Test]
    public void TheTutorialHeadStartIsGivenOnceAndOldUpgradesKeepTheirCertificates()
    {
        Progression.Begin(2, 1, true);
        Progression.Begin(2, 1, true);
        Assert.AreEqual(2, Progression.Count(Skill.EngineOrders));
        Assert.IsTrue(Progression.Earned(Cert("engine2")), "an engine bought before certificates keeps it");
        Assert.IsFalse(Progression.Earned(Cert("rudder2")));
    }

    [Test]
    public void ANewCareerStartsTheRecordAgain()
    {
        Progression.EndVoyage(new Progression.Voyage { EngineOrders = 4, Clean = true });
        Progression.Reset();
        Assert.AreEqual(0, Progression.Count(Skill.EngineOrders));
        Assert.AreEqual(0, Progression.EarnedCount);
    }

    [Test]
    public void EveryUpgradeHasTwoCertificatesWithReachableNeeds()
    {
        foreach (Upgrade u in System.Enum.GetValues(typeof(Upgrade)))
            for (int level = 2; level <= GameSession.MaxUpgradeLevel; level++)
            {
                var c = Progression.For(u, level);
                Assert.IsNotNull(c, u + " Mk " + level);
                Assert.Greater(c.Credits, 0);
                foreach (var n in c.Needs) Assert.Greater(n.Target, 0);
            }
    }

    [TestCase("did you mean", null)]
    public void DidYouMeanNeedsACloseOrder(string speech, string expected)
    {
        Assert.AreEqual(expected, CommandRouter.DidYouMean(CommandParser.Parse(speech)));
    }
}
