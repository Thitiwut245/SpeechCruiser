using System;
using NUnit.Framework;

/// <summary>
/// The crew's everyday talk: what counts as small talk, what stays an order, and that every
/// line is finished (no unfilled placeholders, signed off with "Over.").
/// </summary>
public class SmallTalkTests
{
    [TestCase("What's the weather?", SmallTalk.Topic.Weather)]
    [TestCase("how's the sea looking", SmallTalk.Topic.Weather)]
    [TestCase("hello", SmallTalk.Topic.Greeting)]
    [TestCase("Good morning Harris", SmallTalk.Topic.Greeting)]
    [TestCase("hi, how are you?", SmallTalk.Topic.HowAreYou)]
    [TestCase("who are you", SmallTalk.Topic.WhoAreYou)]
    [TestCase("where are you from?", SmallTalk.Topic.Home)]
    [TestCase("tell me a joke", SmallTalk.Topic.Joke)]
    [TestCase("what are we carrying", SmallTalk.Topic.Cargo)]
    [TestCase("thank you, good job", SmallTalk.Topic.Thanks)]
    [TestCase("are you a robot?", SmallTalk.Topic.Robot)]
    [TestCase("I'm hungry", SmallTalk.Topic.Food)]
    [TestCase("what time is it", SmallTalk.Topic.Time)]
    [TestCase("what is the capital of France", SmallTalk.Topic.None)]
    [TestCase("slow ahead", SmallTalk.Topic.None)]
    public void Classifies(string speech, SmallTalk.Topic expected)
    {
        Assert.AreEqual(expected, SmallTalk.Classify(speech));
    }

    [Test]
    public void UnknownQuestionsAreOffTopicAndRemarksOnlyChatOnTheTalkKey()
    {
        Assert.AreEqual(SmallTalk.Topic.OffTopic, SmallTalk.Decide("what is the capital of France", true, false));
        Assert.AreEqual(SmallTalk.Topic.Chat, SmallTalk.Decide("I like turtles", false, true));
        Assert.AreEqual(SmallTalk.Topic.None, SmallTalk.Decide("I like turtles", false, false));
    }

    [TestCase("What's the weather?", true)]
    [TestCase("hello", true)]
    [TestCase("thanks", true)]
    [TestCase("where are you from?", true)]
    [TestCase("how far is the dock?", false)]
    [TestCase("any danger?", false)]
    [TestCase("full ahead", false)]
    [TestCase("slow ahead, thanks", false)]
    [TestCase("hello, turn left", false)]
    public void SmallTalkNeverSwallowsAnOrderOrAShipQuestion(string speech, bool smallTalk)
    {
        Assert.AreEqual(smallTalk, CommandRouter.IsSmallTalk(CommandParser.Parse(speech), speech), speech);
    }

    [Test]
    public void EveryLineIsFilledAndSignedOff()
    {
        var calm = new ShipTelemetry { SeaState = "calm", WeatherWords = "clear skies", Containers = 6, HazardKind = "Reef" };
        var danger = calm;
        danger.HazardOnCourse = true;
        danger.Weather = Weather.Storm;
        foreach (RadioChannel crew in Enum.GetValues(typeof(RadioChannel)))
            foreach (SmallTalk.Topic topic in Enum.GetValues(typeof(SmallTalk.Topic)))
            {
                if (topic == SmallTalk.Topic.None) continue;
                foreach (var t in new[] { calm, danger })
                    foreach (var line in SmallTalk.Lines(crew, topic, t))
                    {
                        string filled = CrewBarkLibrary.Fill(line, t);
                        StringAssert.DoesNotContain("{", filled, crew + " " + topic);
                        StringAssert.EndsWith("Over.", filled, crew + " " + topic);
                    }
            }
    }

    [Test]
    public void ThePersonaAllowsSmallTalkButNotMadeUpFacts()
    {
        foreach (RadioChannel crew in Enum.GetValues(typeof(RadioChannel)))
        {
            string prompt = CrewPersona.SystemPrompt(crew);
            StringAssert.Contains("Small talk is welcome", prompt);
            StringAssert.Contains("you don't know", prompt);
        }
    }

    [Test]
    public void StatusCarriesTheSeaWeatherAndCargo()
    {
        var t = new ShipTelemetry { Gear = "STOP", Compass = "N", HazardKind = "Hazard", SeaState = "gentle", WeatherWords = "thick fog", Containers = 4 };
        string line = t.ToPromptLine();
        StringAssert.Contains("sea gentle", line);
        StringAssert.Contains("weather thick fog", line);
        StringAssert.Contains("cargo 4 containers", line);
    }
}
