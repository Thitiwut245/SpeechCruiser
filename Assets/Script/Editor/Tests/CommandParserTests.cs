using System.Collections.Generic;
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
    [TestCase("Request docking", ShipAction.RequestDocking)]
    [TestCase("Requesting docking, please", ShipAction.RequestDocking)]
    [TestCase("Permission to dock", ShipAction.RequestDocking)]
    [TestCase("Bring her alongside", ShipAction.RequestDocking)]
    [TestCase("Make fast", ShipAction.MakeFast)]
    [TestCase("Make her fast!", ShipAction.MakeFast)]
    [TestCase("Tie up the ship", ShipAction.MakeFast)]
    [TestCase("All fast", ShipAction.MakeFast)]
    [TestCase("Where is the dock?", ShipAction.QueryDestination)]
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

    // Whisper's sound tags are not speech.
    [TestCase("(chimes)")]
    [TestCase("[Music]")]
    [TestCase("(wind blowing) [BLANK_AUDIO]")]
    [TestCase("*coughs*")]
    public void SoundTagsAloneAreNoise(string transcript)
    {
        Assert.IsTrue(CommandRouter.LooksLikeNoise(transcript));
    }

    [TestCase("(wind blowing) turn left")]
    [TestCase("slow ahead")]
    public void SpeechWithSoundTagsIsNotNoise(string transcript)
    {
        Assert.IsFalse(CommandRouter.LooksLikeNoise(transcript));
    }

    // Crew read-backs quote the speed the order is heading for, so engine upgrades show.
    [Test]
    public void ReadBackQuotesOrderedSpeed()
    {
        var t = new ShipTelemetry { OrderedKn = 9.6f, Gear = "FULL AHEAD (9.6 kn)" };
        Assert.AreEqual("Full ahead, Sir. 9.6 knots. Over.", CrewBarkLibrary.Fill("Full ahead, Sir. {12} knots. Over.", t));
    }

    // Beginners shout the same word, and use everyday words for stopping and turning back.
    [TestCase("left left left", ShipAction.TurnPort)]
    [TestCase("Right, right, right!", ShipAction.TurnStarboard)]
    [TestCase("slow, slow, slow", ShipAction.EngineSlow)]
    [TestCase("stop stop stop stop", ShipAction.EngineStop)]
    [TestCase("go go", ShipAction.EngineFull)]
    [TestCase("go go go", ShipAction.EngineFull)]
    [TestCase("Brake!", ShipAction.EngineStop)]
    [TestCase("Hit the brakes", ShipAction.EngineStop)]
    [TestCase("brake hard", ShipAction.EngineFullAstern)]
    [TestCase("Don't move!", ShipAction.EngineStop)]
    [TestCase("Stay here", ShipAction.EngineStop)]
    public void EverydayPanicWords(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    // Questions asked in full sentences get an answer, whatever the grammar around the topic.
    [TestCase("Is there any danger ahead?", ShipAction.QueryHazard)]
    [TestCase("Is there any danger?", ShipAction.QueryHazard)]
    [TestCase("Any danger ahead?", ShipAction.QueryHazard)]
    [TestCase("Do you see any danger?", ShipAction.QueryHazard)]
    [TestCase("Are we in danger?", ShipAction.QueryHazard)]
    [TestCase("Are we safe?", ShipAction.QueryHazard)]
    [TestCase("How far are the rocks?", ShipAction.QueryHazard)]
    [TestCase("Are we close?", ShipAction.QueryDestination)]
    [TestCase("Are we there yet?", ShipAction.QueryDestination)]
    [TestCase("Where are we?", ShipAction.QueryDestination)]
    [TestCase("Where are we heading?", ShipAction.QueryHeading)]
    [TestCase("Which direction are we going?", ShipAction.QueryHeading)]
    [TestCase("What is the heading?", ShipAction.QueryHeading)]
    [TestCase("Is the engine okay?", ShipAction.QueryStatus)]
    [TestCase("Is everything okay?", ShipAction.QueryStatus)]
    public void NaturalQuestionsGetAnAnswer(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    // Questions about an order still ask "is that an order?", never answer something else.
    [TestCase("Should we speed up?")]
    [TestCase("Can we go faster?")]
    public void QuestionAboutAnOrderIsNotAnswered(string speech)
    {
        var r = CommandParser.Parse(speech);
        Assert.IsFalse(r.HasConfidentOrder, speech);
        Assert.IsFalse(r.Matches.Exists(m => m.Action == ShipAction.QuerySpeed), speech + " answered a speed question");
    }

    // One order per breath when the second word is vague, and accented or plain-English forms.
    [TestCase("Straight ahead", ShipAction.RudderMidships)]
    [TestCase("Right ahead", ShipAction.TurnStarboard)]
    [TestCase("Full right", ShipAction.HardStarboard)]
    [TestCase("Full left", ShipAction.HardPort)]
    [TestCase("for ahead", ShipAction.EngineFull)]
    [TestCase("four ahead", ShipAction.EngineFull)]
    [TestCase("more slow", ShipAction.EngineDecrease)]
    [TestCase("speed down", ShipAction.EngineDecrease)]
    [TestCase("Turn off the engine", ShipAction.EngineStop)]
    [TestCase("engine off", ShipAction.EngineStop)]
    public void CompoundsAndAccents(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    // "Stay" alone isn't an order: "stay on course" holds the course, it doesn't stop the ship.
    [TestCase("stay on course", ShipAction.RudderMidships)]
    [TestCase("Stay straight", ShipAction.RudderMidships)]
    [TestCase("stay left", ShipAction.TurnPort)]
    [TestCase("Stay here", ShipAction.EngineStop)]
    public void StayIsNotABareStop(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [TestCase("Wait!", true)]
    [TestCase("Wait, wait!", true)]
    [TestCase("wait wait wait", true)]
    [TestCase("Hold on a second", true)]
    [TestCase("Hold on, hold on!", true)]
    [TestCase("Please wait", true)]
    [TestCase("Harris, wait", true)]
    [TestCase("wait, turn left", false)]
    [TestCase("no wait", false)]
    public void WaitShortcut(string speech, bool expected)
    {
        Assert.AreEqual(expected, CommandRouter.IsWait(speech), speech);
    }

    [TestCase("Continue.", true)]
    [TestCase("Please continue", true)]
    [TestCase("Yes, continue", true)]
    [TestCase("Continue sailing", true)]
    [TestCase("Harris, continue", true)]
    [TestCase("Go on then", true)]
    [TestCase("keep going", true)]
    [TestCase("go ahead", false)]
    [TestCase("continue and turn left", false)]
    public void ContinueShortcut(string speech, bool expected)
    {
        Assert.AreEqual(expected, CommandRouter.IsContinue(speech), speech);
    }

    // Politeness and accents: a trailing "sorry" keeps the order, r/l swaps still steer.
    [TestCase("Turn left, sorry", ShipAction.TurnPort)]
    [TestCase("Sorry, turn right", ShipAction.TurnStarboard)]
    [TestCase("turn light", ShipAction.TurnStarboard)]
    [TestCase("hard light", ShipAction.HardStarboard)]
    public void PolitenessAndAccents(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    [Test]
    public void HarryIsHarris()
    {
        Assert.AreEqual(RadioChannel.ChiefEngineer, CommandParser.Parse("Harry, status report").AddressedCrew);
    }

    // Key names come from the physical key, whatever the keyboard layout.
    [TestCase("<Keyboard>/t", "T")]
    [TestCase("<Keyboard>/y", "Y")]
    [TestCase("<Keyboard>/leftShift", "LEFT SHIFT")]
    [TestCase("<Keyboard>/f1", "F1")]
    [TestCase("<Keyboard>/numpad5", "NUMPAD 5")]
    [TestCase("<Keyboard>/space", "SPACE")]
    [TestCase("<Keyboard>/backquote", "`")]
    public void KeyNamesArePhysical(string path, string expected)
    {
        Assert.AreEqual(expected, KeyboardLayouts.PhysicalKeyName(path, "?"));
    }

    // An order typed with the Thai layout still on is read as the English keys.
    [TestCase("ะีพื สำดะ", "turn left")]
    [TestCase("turn left", null)]
    public void ThaiLayoutTypingIsReadAsEnglish(string typed, string expected)
    {
        Assert.AreEqual(expected, KeyboardLayouts.FromThaiKedmanee(typed));
        Assert.AreEqual(47, KeyboardLayouts.ThaiKeys.Length);
        Assert.AreEqual(KeyboardLayouts.ThaiKeys.Length, KeyboardLayouts.UsKeys.Length);
    }

    // Beginners mean the dock when they ask "how far?".
    [TestCase("How far?", ShipAction.QueryDestination)]
    [TestCase("How far is it?", ShipAction.QueryDestination)]
    [TestCase("How close is the reef?", ShipAction.QueryHazard)]
    [TestCase("What's our heading?", ShipAction.QueryHeading)]
    public void HowFarAsksAboutTheDock(string speech, ShipAction expected)
    {
        AssertOrders(speech, expected);
    }

    // Crew answers say directions in plain words, never "fine on the starboard bow" or "NE".
    [Test]
    public void AnswersUsePlainDirections()
    {
        var t = new ShipTelemetry
        {
            Compass = "NE", CompassWords = "north-east", DestinationKnown = true, DestinationDistanceM = 300, DestinationBearing = 12,
            DestinationRelative = "fine on the starboard bow", DestinationWhere = "ahead on the right",
        };
        foreach (var crew in new[] { RadioChannel.Lookout, RadioChannel.ChiefEngineer })
            foreach (var action in new[] { ShipAction.QueryDestination, ShipAction.QueryHeading })
                foreach (var line in CrewBarkLibrary.Lines(crew, action, CrewTension.Calm, t))
                {
                    string said = CrewBarkLibrary.Fill(line, t);
                    StringAssert.DoesNotContain("bow", said, said);
                    StringAssert.DoesNotContain(" NE", said, said);
                    StringAssert.DoesNotContain("012", said, said);
                }
    }

    // A radio check off-voyage says what was heard, and that orders work at sea.
    [Test]
    public void RadioCheckSaysWhatWasHeard()
    {
        Assert.AreEqual("Radio check OK. I heard: \"radio check\". Orders work at sea.",
            ShipyardManager.RadioCheckText(RadioStatus.Tested, "radio check", null));
        Assert.AreEqual("Radio check: listening... Let go of T to finish.",
            ShipyardManager.RadioCheckText(RadioStatus.Recording, "", null));
        Assert.IsNull(ShipyardManager.RadioCheckText(RadioStatus.Heard, "slow ahead", null));
    }

    // Progress calls on a good line: every placeholder is filled, and the dock is placed in plain words.
    [Test]
    public void ProgressCallsFillEveryPlaceholder()
    {
        var t = new ShipTelemetry
        {
            SpeedKn = 7, FuelPct = 80, DestinationKnown = true, DestinationDistanceM = 420,
            DestinationRelative = "fine on the port bow", DestinationWhere = "ahead on the left",
        };
        var sets = new List<string[]>
        {
            CrewBarkLibrary.HazardPassed(), CrewBarkLibrary.Progress(1), CrewBarkLibrary.Progress(2),
            CrewBarkLibrary.Progress(3), CrewBarkLibrary.DockSighted(),
            CrewBarkLibrary.QuietReport(RadioChannel.Lookout), CrewBarkLibrary.QuietReport(RadioChannel.ChiefEngineer),
        };
        foreach (var lines in sets)
            foreach (var line in lines)
                StringAssert.DoesNotContain("{", CrewBarkLibrary.Fill(line, t), line);

        Assert.AreEqual("A quarter of the way, Captain. The dock is 420 metres ahead on the left. Over.",
            CrewBarkLibrary.Fill(CrewBarkLibrary.Progress(1)[0], t));
    }
}
