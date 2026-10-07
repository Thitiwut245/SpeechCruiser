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
        Assert.AreEqual("turn left", hints[0]);
        CollectionAssert.Contains(hints, "slow down");
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
        Assert.AreEqual("stop", PhraseHints.For(s)[0]);
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
        Assert.AreEqual("please turn right", hints[0]);
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

    [Test]
    public void EverySuggestedOrderIsUnderstood()
    {
        foreach (var phrase in new[] { "turn left", "turn right", "hard left", "hard right", "slow down", "slow ahead", "half ahead", "full ahead", "stop", "please turn left", "please slow down" })
            Assert.IsTrue(CommandParser.Parse(phrase).HasConfidentCommand, phrase);
    }
}
