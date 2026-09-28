using System.Linq;
using NUnit.Framework;

/// <summary>
/// Phrase regression suite for the Tier 0 voice parser. Every misfire listed here was
/// real before the safety pass. Run from Window > General > Test Runner > EditMode.
/// </summary>
public class CommandParserTests
{
    private static ShipAction[] Confident(CommandParser.Result r)
    {
        return r.Matches.Where(m => m.Confidence >= CommandParser.AcceptHigh)
                        .Select(m => m.Action)
                        .OrderBy(a => a)
                        .ToArray();
    }

    private static CommandParser.Result AssertOrders(string speech, params ShipAction[] expected)
    {
        var r = CommandParser.Parse(speech);
        var got = Confident(r);
        CollectionAssert.AreEquivalent(expected, got,
            "\"" + speech + "\" parsed as [" + string.Join(", ", got) + "], normalised \"" + r.Normalized + "\"");
        return r;
    }

    private static void AssertNoOrder(string speech)
    {
        var r = CommandParser.Parse(speech);
        Assert.IsFalse(r.HasConfidentOrder,
            "\"" + speech + "\" must not move the ship, got [" + string.Join(", ", Confident(r)) + "]");
    }

    // ------------------------------------------------------------------
    // Standard orders keep working
    // ------------------------------------------------------------------
    [TestCase("Full ahead.", ShipAction.EngineFull)]
    [TestCase("All ahead full!", ShipAction.EngineFull)]
    [TestCase("Full!", ShipAction.EngineFull)]
    [TestCase("Half ahead", ShipAction.EngineHalf)]
    [TestCase("Slow ahead", ShipAction.EngineSlow)]
    [TestCase("Dead slow ahead", ShipAction.EngineDeadSlow)]
    [TestCase("Flank speed", ShipAction.EngineFlank)]
    [TestCase("Stop", ShipAction.EngineStop)]
    [TestCase("All stop.", ShipAction.EngineStop)]
    [TestCase("Stop engines", ShipAction.EngineStop)]
    [TestCase("Slow astern", ShipAction.EngineSlowAstern)]
    [TestCase("Astern", ShipAction.EngineSlowAstern)]
    [TestCase("Back up", ShipAction.EngineSlowAstern)]
    [TestCase("Full astern!", ShipAction.EngineFullAstern)]
    [TestCase("Faster", ShipAction.EngineIncrease)]
    [TestCase("Slower", ShipAction.EngineDecrease)]
    [TestCase("Decrease speed", ShipAction.EngineDecrease)]
    [TestCase("Port", ShipAction.TurnPort)]
    [TestCase("Turn to port", ShipAction.TurnPort)]
    [TestCase("Starboard", ShipAction.TurnStarboard)]
    [TestCase("Hard to port!", ShipAction.HardPort)]
    [TestCase("Hard a port", ShipAction.HardPort)]
    [TestCase("Hard over to port", ShipAction.HardPort)]
    [TestCase("Hard to starboard", ShipAction.HardStarboard)]
    [TestCase("Hard left!", ShipAction.HardPort)]
    [TestCase("Hard astern!", ShipAction.EngineFullAstern)]
    [TestCase("Midships", ShipAction.RudderMidships)]
    [TestCase("Steady as she goes", ShipAction.RudderMidships)]
    [TestCase("Rudder amidships", ShipAction.RudderMidships)]
    [TestCase("What do you see?", ShipAction.QueryHazard)]
    [TestCase("Status report", ShipAction.QueryStatus)]
    [TestCase("How fast are we going?", ShipAction.QuerySpeed)]
    [TestCase("Can you turn to port?", ShipAction.TurnPort)]
    [TestCase("Finn, full ahead", ShipAction.EngineFull)]
    public void StandardOrders(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    // ------------------------------------------------------------------
    // R-01: astern never runs ahead
    // ------------------------------------------------------------------
    [TestCase("Full speed astern", ShipAction.EngineFullAstern)]
    [TestCase("Emergency full astern!", ShipAction.EngineFullAstern)]
    [TestCase("Full stern", ShipAction.EngineFullAstern)]
    [TestCase("Full a stern", ShipAction.EngineFullAstern)]
    [TestCase("Half astern", ShipAction.EngineSlowAstern)]
    [TestCase("Half speed astern", ShipAction.EngineSlowAstern)]
    [TestCase("Dead slow astern", ShipAction.EngineSlowAstern)]
    [TestCase("Stop engines, full astern!", ShipAction.EngineFullAstern)]
    public void AsternNeverRunsAhead(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [Test]
    public void AheadAndAsternTogetherAsks()
    {
        var r = CommandParser.Parse("Full ahead then astern");
        Assert.IsTrue(r.DirectionConflict);
        Assert.IsFalse(r.HasConfidentOrder);
    }

    // ------------------------------------------------------------------
    // R-02: real words are never heard as different words
    // ------------------------------------------------------------------
    [TestCase("Fuel?", ShipAction.QueryFuel)]
    [TestCase("Check fuel", ShipAction.QueryFuel)]
    [TestCase("How's our fuel?", ShipAction.QueryFuel)]
    [TestCase("Fuel report", ShipAction.QueryFuel)]
    [TestCase("We have a reef", ShipAction.QueryHazard)]
    public void RealWordsKeepTheirMeaning(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [TestCase("How do you feel?")]
    [TestCase("Fall in line")]
    [TestCase("Step on it!")]
    [TestCase("Oh crap!")]
    [TestCase("Bring her about")]
    [TestCase("The tank is full")]
    [TestCase("Yes master")]
    [TestCase("Hold on tight.")]
    public void EverydayWordsDontFireOrders(string speech)
    {
        AssertNoOrder(speech);
    }

    // ------------------------------------------------------------------
    // R-03: questions never move the ship
    // ------------------------------------------------------------------
    [TestCase("Is there a reef ahead?", ShipAction.QueryHazard)]
    [TestCase("How far is the reef on our port side?", ShipAction.QueryHazard)]
    [TestCase("What's ahead on the right?", ShipAction.QueryHazard)]
    [TestCase("Harris, any reef ahead?", ShipAction.QueryHazard)]
    [TestCase("Is that part of the reef?", ShipAction.QueryHazard)]
    [TestCase("Where is the port?", ShipAction.QueryDestination)]
    [TestCase("Head to the port", ShipAction.QueryDestination)]
    [TestCase("Take us into port", ShipAction.QueryDestination)]
    [TestCase("Where's the dock?", ShipAction.QueryDestination)]
    public void QuestionsOnlyAnswer(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [Test]
    public void OrderAskedAsQuestionIsFlagged()
    {
        var r = AssertOrders("Should we go hard to port?");
        Assert.IsTrue(r.OrderInQuestion);
    }

    [Test]
    public void QuestionThenOrderRunsTheOrder()
    {
        AssertOrders("Anything ahead? Full astern!", ShipAction.QueryHazard, ShipAction.EngineFullAstern);
    }

    [Test]
    public void CommonWordsInALongerSentenceAreNotOrders()
    {
        AssertOrders("Right, full ahead", ShipAction.EngineFull);
        AssertNoOrder("That's right.");
        AssertNoOrder("Go on.");
        Assert.IsTrue(CommandParser.Parse("That's right.").WasFillerOnly);
    }

    // ------------------------------------------------------------------
    // R-04: corrections, negation, belay
    // ------------------------------------------------------------------
    [TestCase("No, not port, starboard!", ShipAction.TurnStarboard)]
    [TestCase("Left! No, right!", ShipAction.TurnStarboard)]
    [TestCase("Full ahead, I mean half ahead", ShipAction.EngineHalf)]
    [TestCase("No problem, full ahead", ShipAction.EngineFull)]
    [TestCase("Don't stop, full ahead", ShipAction.EngineFull)]
    [TestCase("Not so fast", ShipAction.EngineDecrease)]
    [TestCase("Stop turning", ShipAction.RudderMidships)]
    [TestCase("Belay that", ShipAction.Belay)]
    [TestCase("Cancel that order", ShipAction.Belay)]
    public void TheLastWordCounts(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [TestCase("Don't stop")]
    [TestCase("Don't go full ahead")]
    [TestCase("Hard to port, no")]
    [TestCase("Full ahead, belay that")]
    [TestCase("No.")]
    public void CancelledOrdersChangeNothing(string speech)
    {
        var r = CommandParser.Parse(speech);
        Assert.IsFalse(r.HasConfidentOrder, "\"" + speech + "\" got [" + string.Join(", ", Confident(r)) + "]");
        Assert.IsTrue(r.NoChange, "\"" + speech + "\" should report no change");
    }

    [TestCase("Starboard, no wait, port!", ShipAction.TurnPort)]
    [TestCase("Actually, slow ahead", ShipAction.EngineSlow)]
    [TestCase("As you were.", ShipAction.Belay)]
    [TestCase("Disregard.", ShipAction.Belay)]
    public void MoreCorrections(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [TestCase("Never mind.")]
    [TestCase("Don't turn yet.")]
    [TestCase("Not yet.")]
    public void NothingToDoMeansNoChange(string speech)
    {
        var r = CommandParser.Parse(speech);
        Assert.IsFalse(r.HasConfidentOrder, speech);
        Assert.IsTrue(r.NoChange, speech);
    }

    [Test]
    public void CorrectedTurnKeepsItsNumber()
    {
        var r = AssertOrders("Port 20, no, starboard 20", ShipAction.TurnStarboard);
        Assert.AreEqual(20f, r.Degrees);
    }

    [TestCase("Port and starboard")]
    [TestCase("Hard over!")]
    [TestCase("Turn around.")]
    public void MissingOrMixedSideAsks(string speech)
    {
        var r = CommandParser.Parse(speech);
        Assert.IsTrue(r.SideConflict, speech);
        Assert.IsFalse(r.HasConfidentOrder, speech);
    }

    // ------------------------------------------------------------------
    // Modifier words stay with the order they modify
    // ------------------------------------------------------------------
    [TestCase("A little more left.", ShipAction.TurnPort)]
    [TestCase("Go left.", ShipAction.TurnPort)]
    [TestCase("Less rudder.", ShipAction.RudderMidships)]
    [TestCase("Keep her straight.", ShipAction.RudderMidships)]
    [TestCase("How far to the dock?", ShipAction.QueryDestination)]
    [TestCase("Speed?", ShipAction.QuerySpeed)]
    [TestCase("Port side is clear?", ShipAction.QueryHazard)]
    public void ModifiersStayWithTheirOrder(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [Test]
    public void AcknowledgementsAndNames()
    {
        Assert.IsTrue(CommandParser.Parse("Roger that.").WasFillerOnly);
        Assert.IsTrue(CommandParser.Parse("Engine room?").WasNameOnly);
        AssertNoOrder("Steer 270.");
    }

    // ------------------------------------------------------------------
    // Compound orders
    // ------------------------------------------------------------------
    [Test]
    public void CompoundOrders()
    {
        AssertOrders("Full ahead and hard to port", ShipAction.EngineFull, ShipAction.HardPort);
        AssertOrders("Slow down, reef ahead", ShipAction.EngineDecrease, ShipAction.QueryHazard);
        AssertOrders("Full ahead, slow ahead", ShipAction.EngineSlow);
    }

    // ------------------------------------------------------------------
    // Turn sizes belong to the helm order
    // ------------------------------------------------------------------
    [TestCase("Port twenty", 20f)]
    [TestCase("Starboard one five", 15f)]
    [TestCase("Port twenty five", 25f)]
    [TestCase("Starboard 30", 30f)]
    [TestCase("Ten degrees to port", 10f)]
    public void TurnDegrees(string speech, float expected)
    {
        Assert.AreEqual(expected, CommandParser.Parse(speech).Degrees, speech);
    }

    [TestCase("Turn left, everyone hold on")]
    [TestCase("Channel two, turn port")]
    [TestCase("Port, the freight container is loose")]
    public void StrayNumbersAreNotTurns(string speech)
    {
        Assert.IsNull(CommandParser.Parse(speech).Degrees, speech);
    }

    [Test]
    public void ExtractDegreesUsesWholeWords()
    {
        Assert.IsNull(CommandParser.ExtractDegrees("turn left everyone"));
        Assert.AreEqual(20f, CommandParser.ExtractDegrees("turn port 20"));
        Assert.IsNull(CommandParser.ExtractDegrees("come to two seven zero"));
    }

    [Test]
    public void NameOnly()
    {
        Assert.IsTrue(CommandParser.Parse("Harris?").WasNameOnly);
    }

    // ------------------------------------------------------------------
    // Beginner English: plain words work, not only bridge vocabulary
    // ------------------------------------------------------------------
    [TestCase("Turn left.", ShipAction.TurnPort)]
    [TestCase("Turn the boat to the right.", ShipAction.TurnStarboard)]
    [TestCase("Ship go left.", ShipAction.TurnPort)]
    [TestCase("I want go right.", ShipAction.TurnStarboard)]
    [TestCase("Left more.", ShipAction.TurnPort)]
    [TestCase("Go straight.", ShipAction.RudderMidships)]
    [TestCase("Forward.", ShipAction.EngineSlow)]
    [TestCase("Slow.", ShipAction.EngineSlow)]
    [TestCase("Go fast.", ShipAction.EngineFull)]
    [TestCase("Go go go!", ShipAction.EngineFull)]
    [TestCase("Max speed.", ShipAction.EngineFlank)]
    [TestCase("More fast.", ShipAction.EngineIncrease)]
    [TestCase("Less fast.", ShipAction.EngineDecrease)]
    [TestCase("Stop the boat.", ShipAction.EngineStop)]
    [TestCase("Go backward.", ShipAction.EngineSlowAstern)]
    [TestCase("Backwards.", ShipAction.EngineSlowAstern)]
    [TestCase("Go back fast.", ShipAction.EngineFullAstern)]
    [TestCase("Any danger?", ShipAction.QueryHazard)]
    [TestCase("What is in front?", ShipAction.QueryHazard)]
    [TestCase("Where we go?", ShipAction.QueryDestination)]
    [TestCase("Where is the finish?", ShipAction.QueryDestination)]
    public void BeginnerEnglish(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [TestCase("U-turn.")]
    [TestCase("Turn back.")]
    public void TurnAroundAsksWhichSide(string speech)
    {
        var r = CommandParser.Parse(speech);
        Assert.IsTrue(r.SideConflict, speech);
    }

    // ------------------------------------------------------------------
    // R-05: chat replies may never claim an order was carried out
    // ------------------------------------------------------------------
    [TestCase("Full astern, aye. Over.", true)]
    [TestCase("Aye, hard to port! Over.", true)]
    [TestCase("Aye, I'd slow down if I were you, Captain. Over.", false)]
    [TestCase("Reef's close, Captain. Over.", false)]
    public void ChatCannotClaimOrders(string reply, bool claims)
    {
        Assert.AreEqual(claims, CrewPersona.ClaimsOrderCarriedOut(reply), reply);
    }
}
