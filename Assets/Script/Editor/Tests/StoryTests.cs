using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class StoryTests
{
    // The tests touch the story's saved keys: keep the player's own progress and put it back.
    private readonly Dictionary<string, int> _saved = new Dictionary<string, int>();

    [SetUp]
    public void SaveProgress()
    {
        _saved.Clear();
        foreach (var m in StoryCatalog.Missions)
            if (PlayerPrefs.HasKey("story.done." + m.Id)) _saved[m.Id] = PlayerPrefs.GetInt("story.done." + m.Id);
        foreach (var m in StoryCatalog.Missions) PlayerPrefs.DeleteKey("story.done." + m.Id);
    }

    [TearDown]
    public void RestoreProgress()
    {
        foreach (var m in StoryCatalog.Missions)
        {
            if (_saved.TryGetValue(m.Id, out int v)) PlayerPrefs.SetInt("story.done." + m.Id, v);
            else PlayerPrefs.DeleteKey("story.done." + m.Id);
        }
        StoryProgress.Leave();
    }

    [Test]
    public void ThreeChaptersOfThreeMissions()
    {
        Assert.AreEqual(3, StoryCatalog.Chapters.Length);
        foreach (var c in StoryCatalog.Chapters) Assert.AreEqual(3, c.Missions.Length, "chapter " + c.Number);
        Assert.AreEqual(9, StoryCatalog.Missions.Count);
        Assert.AreEqual("1-1", StoryCatalog.Missions[0].Id);
        Assert.AreEqual("3-3", StoryCatalog.Missions[8].Id);
    }

    [Test]
    public void EveryMissionHasAFixedStoryMap()
    {
        var seeds = new HashSet<int>();
        foreach (var m in StoryCatalog.Missions)
        {
            Assert.IsTrue(m.Job.Story, m.Id);
            Assert.Greater(m.Job.Rows, 5, m.Id);
            Assert.AreNotEqual(0, m.Job.Seed, m.Id);
            Assert.IsTrue(seeds.Add(m.Job.Seed), "seed shared: " + m.Id);
            Assert.Greater(m.Job.Containers, 0, m.Id);
            Assert.Greater(m.Job.Reward, 0, m.Id);
        }
    }

    [Test]
    public void EveryMissionHasWordsAndDialogue()
    {
        foreach (var m in StoryCatalog.Missions)
        {
            Assert.IsNotEmpty(m.Title, m.Id);
            Assert.IsNotEmpty(m.Summary, m.Id);
            Assert.IsNotEmpty(m.Phrases, m.Id);
            Assert.IsNotEmpty(m.Intro, m.Id);
            Assert.IsNotEmpty(m.Outro, m.Id);
        }
    }

    [Test]
    public void RescueMissionsSayWhatToFind()
    {
        foreach (var m in StoryCatalog.Missions)
        {
            if (m.Goal != MissionGoal.Rescue) continue;
            Assert.That(m.RescueAt, Is.InRange(0.2f, 0.8f), m.Id);
            Assert.IsNotEmpty(m.RescueName, m.Id);
            Assert.IsNotEmpty(m.RescueSpotted.Text, m.Id);
            Assert.IsNotEmpty(m.RescueAboard.Text, m.Id);
            Assert.IsNotEmpty(m.RescueMissed, m.Id);
        }
    }

    [Test]
    public void OnlyTheCrewSpeakOnTheRadio()
    {
        foreach (var m in StoryCatalog.Missions)
        {
            var cues = new List<RadioCue>(m.Radio);
            foreach (var s in m.Sights) cues.AddRange(s.Lines);
            if (m.Goal == MissionGoal.Rescue) { cues.Add(m.RescueSpotted); cues.Add(m.RescueAboard); }
            foreach (var c in cues)
            {
                Assert.That(c.Speaker == CharacterId.Finn || c.Speaker == CharacterId.Harris, m.Id + ": " + c.Text);
                Assert.That(c.At, Is.InRange(0f, 1f), m.Id + ": " + c.Text);
            }
        }
    }

    [Test]
    public void EverySpeakerHasAVoiceAndALook()
    {
        foreach (var m in StoryCatalog.Missions)
            foreach (var line in m.Intro.Concat(m.Outro))
            {
                var p = StoryCast.Get(line.Speaker);
                Assert.IsNotEmpty(p.Name);
                Assert.IsNotEmpty(p.Voice, p.Name);
                Assert.Greater(p.Rate, 0.5f, p.Name);
            }
    }

    [Test]
    public void LinesAreShortEnoughForBeginners()
    {
        foreach (var m in StoryCatalog.Missions)
            foreach (var line in m.Intro.Concat(m.Outro))
                Assert.LessOrEqual(line.Text.Length, 110, m.Id + ": " + line.Text);
    }

    [Test]
    public void MissionsOpenInOrder()
    {
        var all = StoryCatalog.Missions;
        Assert.IsTrue(StoryProgress.IsUnlocked(all[0]));
        Assert.IsFalse(StoryProgress.IsUnlocked(all[1]));
        Assert.AreSame(all[0], StoryProgress.NextToPlay);

        StoryProgress.MarkDone(all[0]);
        Assert.IsTrue(StoryProgress.IsUnlocked(all[1]));
        Assert.IsFalse(StoryProgress.IsUnlocked(all[2]));
        Assert.AreSame(all[1], StoryProgress.NextToPlay);
        Assert.AreEqual(1, StoryProgress.DoneCount);

        // Chapter 2 opens with the last mission of chapter 1.
        StoryProgress.MarkDone(all[1]);
        Assert.IsFalse(StoryProgress.IsUnlocked(StoryCatalog.Chapters[1]));
        StoryProgress.MarkDone(all[2]);
        Assert.IsTrue(StoryProgress.IsUnlocked(StoryCatalog.Chapters[1]));
        Assert.IsFalse(StoryProgress.IsUnlocked(StoryCatalog.Chapters[2]));
    }

    [Test]
    public void FinishedWhenAllAreDone()
    {
        foreach (var m in StoryCatalog.Missions) StoryProgress.MarkDone(m);
        Assert.IsTrue(StoryProgress.IsFinished);
        Assert.IsNull(StoryProgress.NextToPlay);
        Assert.IsNull(StoryCatalog.Next(StoryCatalog.Missions[8]));
    }

    [Test]
    public void BriefingPlaysOncePerSession()
    {
        var m = StoryCatalog.Find("2-2");
        StoryProgress.TakeBriefing(m); // may already be taken earlier this session
        Assert.IsFalse(StoryProgress.TakeBriefing(m));
    }

    [Test]
    public void EveryPhrasePassesWhenSaidAsWritten()
    {
        foreach (var m in StoryCatalog.Missions)
            foreach (var p in m.Phrases)
                Assert.AreEqual(PhraseCheck.Verdict.Pass, PhraseCheck.Check(p, p), m.Id + ": " + p);
    }

    [TestCase("turn left", "turn to the left")]
    [TestCase("turn left", "Turn left, please.")]
    [TestCase("slow ahead", "slow ahead now")]
    [TestCase("stop", "Stop the ship!")]
    [TestCase("what's ahead?", "What is ahead?")]
    [TestCase("how far is the reef?", "How far is the reef")]
    [TestCase("is it safe?", "is it safe")]
    [TestCase("thank you, Harris", "Thank you Harris.")]
    [TestCase("please turn to port", "Please turn to port.")]
    public void SayingItCounts(string phrase, string heard)
    {
        Assert.AreEqual(PhraseCheck.Verdict.Pass, PhraseCheck.Check(phrase, heard));
    }

    [TestCase("turn left", "turn right")]
    [TestCase("slow ahead", "full ahead")]
    [TestCase("how far is the reef?", "stop")]
    [TestCase("where is the dock?", "what can you see")]
    [TestCase("stop", "")]
    public void SomethingElseDoesNot(string phrase, string heard)
    {
        Assert.AreEqual(PhraseCheck.Verdict.Missed, PhraseCheck.Check(phrase, heard));
    }

    [Test]
    public void PoliteOrdersNeedPlease()
    {
        Assert.AreEqual(PhraseCheck.Verdict.NeedsPlease, PhraseCheck.Check("please turn to port", "turn to port"));
        Assert.AreEqual(PhraseCheck.Verdict.NeedsPlease, PhraseCheck.Check("please stop the engine", "stop the engine"));
    }

    [Test]
    public void ActiveMissionSetsTheJob()
    {
        var m = StoryCatalog.Find("1-2");
        StoryProgress.Begin(m);
        Assert.AreSame(m, StoryProgress.ActiveMission);
        StoryProgress.Leave();
        Assert.IsNull(StoryProgress.ActiveMission);
    }
}

internal static class StoryTestExtensions
{
    public static IEnumerable<DialogueLine> Concat(this DialogueLine[] a, DialogueLine[] b)
    {
        foreach (var x in a) yield return x;
        foreach (var x in b) yield return x;
    }
}
