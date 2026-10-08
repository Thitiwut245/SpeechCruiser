using NUnit.Framework;

/// <summary>
/// The crew as teachers: which orders count as standard radio English (and what the crew
/// model instead), the word help ("what does starboard mean?", "how do I say slow down?"),
/// and the language report's counting.
/// </summary>
public class TeachingTests
{
    [TestCase(ShipAction.TurnPort, "port", true)]
    [TestCase(ShipAction.TurnPort, "turn left", false)]
    [TestCase(ShipAction.TurnStarboard, "starboard", true)]
    [TestCase(ShipAction.TurnStarboard, "go right", false)]
    [TestCase(ShipAction.HardPort, "hard a port", true)]
    [TestCase(ShipAction.HardPort, "hard left", false)]
    [TestCase(ShipAction.EngineIncrease, "increase speed", true)]
    [TestCase(ShipAction.EngineIncrease, "more fast", false)]
    [TestCase(ShipAction.EngineSlow, "slow ahead", true)]
    [TestCase(ShipAction.EngineSlow, "go", false)]
    [TestCase(ShipAction.EngineStop, "stop engine", true)]
    [TestCase(ShipAction.EngineStop, "brake", false)]
    [TestCase(ShipAction.QueryHazard, "is it safe", true)]
    public void StandardOrders(ShipAction action, string phrase, bool standard)
    {
        Assert.AreEqual(standard, StandardPhrases.IsStandard(action, phrase), phrase);
    }

    [Test]
    public void TheTaughtPhraseKeepsTheDegrees()
    {
        Assert.AreEqual("port 15", StandardPhrases.For(ShipAction.TurnPort, 15f));
        Assert.AreEqual("starboard twenty", StandardPhrases.For(ShipAction.TurnStarboard));
        Assert.AreEqual("reduce speed", StandardPhrases.For(ShipAction.EngineDecrease));
    }

    [Test]
    public void EveryRecastQuotesThePhraseAndSignsOff()
    {
        foreach (var crew in new[] { RadioChannel.ChiefEngineer, RadioChannel.Lookout })
            foreach (var line in StandardPhrases.Recast(crew, "port twenty"))
            {
                StringAssert.Contains("\"port twenty\"", line);
                StringAssert.EndsWith("Over.", line);
                StringAssert.DoesNotContain("wrong", line.ToLowerInvariant());
            }
    }

    [TestCase("What does starboard mean?", "Starboard means right")]
    [TestCase("Finn, what is port?", "Port means left")]
    [TestCase("what is a reef", "reef is rock")]
    [TestCase("what's the meaning of astern", "backwards")]
    [TestCase("How do I say slow down?", "\"reduce speed\"")]
    [TestCase("how can I tell you to go faster", "\"increase speed\"")]
    [TestCase("how do I stop the ship", "\"stop engine\"")]
    [TestCase("what should I say to turn left", "\"port twenty\"")]
    [TestCase("how do I park the ship", "\"request docking\"")]
    [TestCase("what does flibber mean", "Don't know that word")]
    public void TheCrewExplainWords(string speech, string expected)
    {
        Assert.IsTrue(Glossary.TryAnswer(RadioChannel.Lookout, speech, out var lines), speech);
        StringAssert.Contains(expected, string.Join(" | ", lines), speech);
    }

    [TestCase("what is our heading?")]
    [TestCase("what does that mean?")]
    [TestCase("full ahead")]
    [TestCase("hard to port")]
    [TestCase("any danger?")]
    [TestCase("what's the weather?")]
    public void OrdersAndShipQuestionsAreNotWordQuestions(string speech)
    {
        Assert.IsFalse(Glossary.TryAnswer(RadioChannel.Lookout, speech, out _), speech);
    }

    [Test]
    public void TheLanguageReportCounts()
    {
        LanguageLog.Begin();
        for (int i = 0; i < 5; i++) LanguageLog.Call();
        LanguageLog.Order(true, "slow ahead", 1f);
        LanguageLog.Warned(10f);
        LanguageLog.Order(false, "port twenty", 12.5f);
        LanguageLog.Order(false, "port twenty", 20f);
        LanguageLog.Unclear();
        LanguageLog.WordAsked();

        var s = LanguageLog.Read();
        Assert.AreEqual(5, s.Calls);
        Assert.AreEqual(4, s.Understood);
        Assert.AreEqual(80, s.UnderstoodPercent);
        Assert.AreEqual(3, s.Orders);
        Assert.AreEqual(1, s.StandardOrders);
        Assert.AreEqual(1, s.Unclear);
        Assert.AreEqual(1, s.WordsAsked);
        Assert.AreEqual(2.5f, s.AverageReaction, 0.001f);
        CollectionAssert.AreEqual(new[] { "port twenty" }, s.Practise);

        LanguageLog.Begin();
        Assert.AreEqual(0, LanguageLog.Read().Calls);
        Assert.Less(LanguageLog.Read().AverageReaction, 0f);
    }

    [TestCase("I'm fine, thanks", SmallTalk.Topic.Reply)]
    [TestCase("I'm from Khon Kaen", SmallTalk.Topic.Reply)]
    [TestCase("my favourite food is pad thai", SmallTalk.Topic.Reply)]
    public void TheCaptainCanAnswerTheCrewBack(string speech, SmallTalk.Topic topic)
    {
        Assert.AreEqual(topic, SmallTalk.Classify(speech));
        Assert.IsTrue(CommandRouter.IsSmallTalk(CommandParser.Parse(speech), speech), speech);
    }

    [Test]
    public void ThePersonaTeaches()
    {
        string prompt = CrewPersona.SystemPrompt(RadioChannel.Lookout);
        StringAssert.Contains("learning English", prompt);
        StringAssert.Contains("recast", prompt);
    }
}
