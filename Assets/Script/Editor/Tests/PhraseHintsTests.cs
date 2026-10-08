using NUnit.Framework;

public class PhraseHintsTests
{
    private static PhraseHints.Situation Cruising() => new PhraseHints.Situation { Gear = 3, SpeedKn = 5, AvoidSide = "right" };

    [Test]
    public void AHazardSuggestsTurningAwayAndSlowing()
    {
        var s = Cruising();
        s.Threat = true;
        s.AvoidSide = "left";
        var hints = PhraseHints.For(s);
        Assert.AreEqual("port twenty", hints[0]);
        CollectionAssert.Contains(hints, "reduce speed");
    }

    [Test]
    public void NearTheBerthSuggestsRequestingDocking()
    {
        var s = Cruising();
        s.DockClose = true;
        s.DockReady = true;
        s.SpeedKn = 2;
        Assert.AreEqual("request docking", PhraseHints.For(s)[0]);

        s.SpeedKn = 7; // too fast for the pilot: slow down first
        var hints = PhraseHints.For(s);
        Assert.AreEqual("reduce speed", hints[0]);
        CollectionAssert.Contains(hints, "request docking");
    }

    [Test]
    public void AlongsideSuggestsMakingFast()
    {
        var s = Cruising();
        s.Gear = 0;
        s.SpeedKn = 0;
        s.Alongside = true;
        Assert.AreEqual("make fast", PhraseHints.For(s)[0]);
    }

    [Test]
    public void AStoppedShipSuggestsGettingGoing()
    {
        var s = Cruising();
        s.Gear = 0;
        s.SpeedKn = 0;
        CollectionAssert.Contains(PhraseHints.For(s), "slow ahead");
    }

    [Test]
    public void ARescueSuggestsStopping()
    {
        var s = Cruising();
        s.RescueNear = true;
        s.Threat = true; // the rescue matters more than a rock further off
        Assert.AreEqual("stop engine", PhraseHints.For(s)[0]);
    }

    [Test]
    public void FogSuggestsAskingTheLookout()
    {
        var s = Cruising();
        s.Fog = true;
        CollectionAssert.Contains(PhraseHints.For(s), "what can you see?");
    }

    [Test]
    public void ChapterThreeOrdersArePolite()
    {
        var s = Cruising();
        s.Threat = true;
        s.Polite = true;
        var hints = PhraseHints.For(s);
        Assert.AreEqual("please starboard twenty", hints[0]);
        // Questions stay as they are.
        s.Threat = false;
        s.Fog = true;
        CollectionAssert.Contains(PhraseHints.For(s), "what can you see?");
    }

    [Test]
    public void NeverMoreThanThreeAndNoRepeats()
    {
        var s = Cruising();
        s.Threat = true;
        s.Fog = true;
        s.DockClose = true;
        var hints = PhraseHints.For(s);
        Assert.LessOrEqual(hints.Count, PhraseHints.Max);
        CollectionAssert.AllItemsAreUnique(hints);
        Assert.GreaterOrEqual(PhraseHints.For(Cruising()).Count, 2);
    }

    private static readonly string[] SuggestedOrders =
    {
        "port twenty", "starboard twenty", "hard a port", "hard a starboard", "reduce speed", "slow ahead",
        "half ahead", "full ahead", "stop engine", "request docking", "make fast",
    };

    [Test]
    public void EverySuggestedOrderIsUnderstood()
    {
        foreach (var phrase in SuggestedOrders)
        {
            Assert.IsTrue(CommandParser.Parse(phrase).HasConfidentCommand, phrase);
            Assert.IsTrue(CommandParser.Parse("please " + phrase).HasConfidentCommand, "please " + phrase);
        }
    }

    /// <summary>A hint must never be something the crew would then correct.</summary>
    [Test]
    public void EverySuggestedOrderIsStandardRadioEnglish()
    {
        foreach (var phrase in SuggestedOrders)
        {
            var parsed = CommandParser.Parse(phrase);
            foreach (var m in parsed.Matches)
                Assert.IsTrue(StandardPhrases.IsStandard(m.Action, m.Phrase), phrase + " -> " + m.Action + " via \"" + m.Phrase + "\"");
        }
    }
}
