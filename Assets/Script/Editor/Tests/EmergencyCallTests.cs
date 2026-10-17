using NUnit.Framework;

/// <summary>Story emergency calls: the way out, which orders save the ship, the orders called, the time given, and the counting.</summary>
public class EmergencyCallTests
{
    [TestCase(ShipAction.HardStarboard, EmergencyCall.Dodge.Starboard, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.TurnStarboard, EmergencyCall.Dodge.Starboard, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.HardPort, EmergencyCall.Dodge.Starboard, EmergencyCall.Response.WrongWay)]
    [TestCase(ShipAction.HardPort, EmergencyCall.Dodge.Port, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.TurnStarboard, EmergencyCall.Dodge.Port, EmergencyCall.Response.WrongWay)]
    [TestCase(ShipAction.EngineFullAstern, EmergencyCall.Dodge.Starboard, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.EngineStop, EmergencyCall.Dodge.Port, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.EngineFlank, EmergencyCall.Dodge.Starboard, EmergencyCall.Response.WrongWay)]
    [TestCase(ShipAction.EngineFull, EmergencyCall.Dodge.Stop, EmergencyCall.Response.WrongWay)]
    [TestCase(ShipAction.HardPort, EmergencyCall.Dodge.Stop, EmergencyCall.Response.Saves)]      // dead ahead: either side clears it
    [TestCase(ShipAction.TurnStarboard, EmergencyCall.Dodge.Stop, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.QueryHazard, EmergencyCall.Dodge.Starboard, EmergencyCall.Response.Other)]
    [TestCase(ShipAction.RudderMidships, EmergencyCall.Dodge.Port, EmergencyCall.Response.Other)]
    public void TheRightMeaningSavesTheShip(ShipAction action, EmergencyCall.Dodge dodge, EmergencyCall.Response expected)
    {
        Assert.AreEqual(expected, EmergencyCall.Judge(action, dodge));
    }

    [TestCase(-40f, 30f, "Reef", EmergencyCall.Dodge.Starboard)]   // danger to port: turn away to starboard
    [TestCase(35f, -30f, "Mine", EmergencyCall.Dodge.Port)]        // danger to starboard: port, whatever the channel
    [TestCase(3f, 25f, "Reef", EmergencyCall.Dodge.Starboard)]     // dead ahead: toward the channel
    [TestCase(-5f, -25f, "Reef", EmergencyCall.Dodge.Port)]
    [TestCase(2f, 25f, "Mine", EmergencyCall.Dodge.Stop)]          // a mine dead ahead: stop short
    public void TheWayOutFollowsTheDangerAndTheChannel(float threat, float channel, string kind, EmergencyCall.Dodge expected)
    {
        Assert.AreEqual(expected, EmergencyCall.Choose(threat, channel, kind));
    }

    [Test]
    public void EveryCallIsAStandardOrderThatSavesTheShip()
    {
        foreach (EmergencyCall.Dodge dodge in System.Enum.GetValues(typeof(EmergencyCall.Dodge)))
            foreach (var call in EmergencyCall.PhrasesFor(dodge))
            {
                var parsed = CommandParser.Parse(call);
                Assert.IsTrue(parsed.HasConfidentOrder, call);
                Assert.AreEqual(EmergencyCall.Response.Saves, EmergencyCall.Judge(parsed.Matches[0].Action, dodge), call);
                Assert.IsTrue(StandardPhrases.IsStandard(parsed.Matches[0].Action, parsed.Matches[0].Phrase), call);
            }
    }

    [Test]
    public void TheCallVariesButIsHardOverInAHurry()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        for (double roll = 0; roll < 1; roll += 0.1) seen.Add(EmergencyCall.CallFor(EmergencyCall.Dodge.Starboard, roll, false));
        Assert.Greater(seen.Count, 1);
        for (double roll = 0; roll < 1; roll += 0.1)
        {
            Assert.AreEqual("hard a starboard", EmergencyCall.CallFor(EmergencyCall.Dodge.Starboard, roll, true));
            Assert.AreEqual("hard a port", EmergencyCall.CallFor(EmergencyCall.Dodge.Port, roll, true));
        }
    }

    [TestCase(0f, "dead ahead")]
    [TestCase(-20f, "fine on the port bow")]
    [TestCase(45f, "on the starboard bow")]
    [TestCase(-90f, "on the port beam")]
    public void TheLookoutSaysWhereItIs(float bearing, string expected)
    {
        Assert.AreEqual(expected, EmergencyCall.Where(bearing));
    }

    [Test]
    public void TheFirstEmergencyGivesMoreTime()
    {
        Assert.Greater(EmergencyCall.WindowFor(0), EmergencyCall.WindowFor(1));
        Assert.AreEqual(EmergencyCall.WindowFor(1), EmergencyCall.WindowFor(2));
    }

    [Test]
    public void TheReportCountsEmergencies()
    {
        LanguageLog.Begin();
        LanguageLog.Emergency(true);
        LanguageLog.Emergency(false);
        var s = LanguageLog.Read();
        Assert.AreEqual(2, s.Emergencies);
        Assert.AreEqual(1, s.EmergenciesSaved);
        LanguageLog.Begin();
        Assert.AreEqual(0, LanguageLog.Read().Emergencies);
    }

    [Test]
    public void OrdersAreAnnouncedWithTheirStandardness()
    {
        ShipAction got = ShipAction.None;
        bool? standard = null;
        System.Action<ShipAction, bool> h = (a, s) => { got = a; standard = s; };
        LanguageLog.OnOrder += h;
        try
        {
            LanguageLog.Begin();
            LanguageLog.Order(false, "hard a starboard", 1f, ShipAction.HardStarboard);
        }
        finally { LanguageLog.OnOrder -= h; }
        Assert.AreEqual(ShipAction.HardStarboard, got);
        Assert.AreEqual(false, standard);
    }
}
