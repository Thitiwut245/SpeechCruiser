using NUnit.Framework;

/// <summary>Story emergency calls: which orders save the ship, the order shown, the time given, and the counting.</summary>
public class EmergencyCallTests
{
    [TestCase(ShipAction.HardStarboard, true, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.TurnStarboard, true, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.HardPort, true, EmergencyCall.Response.WrongWay)]
    [TestCase(ShipAction.HardPort, false, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.TurnStarboard, false, EmergencyCall.Response.WrongWay)]
    [TestCase(ShipAction.EngineFullAstern, true, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.EngineStop, false, EmergencyCall.Response.Saves)]
    [TestCase(ShipAction.EngineFlank, true, EmergencyCall.Response.WrongWay)]
    [TestCase(ShipAction.QueryHazard, true, EmergencyCall.Response.Other)]
    [TestCase(ShipAction.RudderMidships, true, EmergencyCall.Response.Other)]
    public void TheRightMeaningSavesTheShip(ShipAction action, bool avoidRight, EmergencyCall.Response expected)
    {
        Assert.AreEqual(expected, EmergencyCall.Judge(action, avoidRight));
    }

    [Test]
    public void TheCallIsTheStandardHardOverOrder()
    {
        Assert.AreEqual("hard a starboard", EmergencyCall.CallFor(true));
        Assert.AreEqual("hard a port", EmergencyCall.CallFor(false));
        foreach (var call in new[] { EmergencyCall.CallFor(true), EmergencyCall.CallFor(false) })
        {
            var parsed = CommandParser.Parse(call);
            Assert.IsTrue(parsed.HasConfidentOrder, call);
            Assert.AreEqual(EmergencyCall.Response.Saves, EmergencyCall.Judge(parsed.Matches[0].Action, call.EndsWith("starboard")), call);
            Assert.IsTrue(StandardPhrases.IsStandard(parsed.Matches[0].Action, parsed.Matches[0].Phrase), call);
        }
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
