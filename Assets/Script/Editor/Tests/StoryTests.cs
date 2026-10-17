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
    public void ThreeChaptersWithTheEncounters()
    {
        Assert.AreEqual(3, StoryCatalog.Chapters.Length);
        CollectionAssert.AreEqual(new[] { 3, 4, 4 }, System.Array.ConvertAll(StoryCatalog.Chapters, c => c.Missions.Length));
        Assert.AreEqual(11, StoryCatalog.Missions.Count);
        Assert.AreEqual("1-1", StoryCatalog.Missions[0].Id);
        Assert.AreEqual(MissionGoal.Kraken, StoryCatalog.Find("2-4").Goal);
        Assert.AreEqual(MissionGoal.Pirates, StoryCatalog.Find("3-2").Goal);
        Assert.AreEqual("3-4", StoryCatalog.Missions[10].Id);
        Assert.IsNotNull(StoryCatalog.Missions[10].OutroTitle, "the epilogue stays last");
    }

    [Test]
    public void EncounterMissionsAreOpenWater()
    {
        foreach (var m in StoryCatalog.Missions)
        {
            if (m.Goal != MissionGoal.Kraken && m.Goal != MissionGoal.Pirates) continue;
            Assert.AreEqual(0f, m.Job.DensityScale, m.Id);
            Assert.IsFalse(m.Job.Mines, m.Id);
            Assert.That(m.EncounterAt, Is.InRange(0.05f, 0.4f), m.Id);
        }
    }

    /// <summary>
    /// Every phrase a mission teaches does something when said at sea: an order the ship carries
    /// out, a question the crew answer, or talk the crew reply to. Never "say again".
    /// </summary>
    [Test]
    public void EveryMissionPhraseIsUnderstood()
    {
        foreach (var m in StoryCatalog.Missions)
            foreach (var phrase in m.Phrases)
            {
                var parsed = CommandParser.Parse(phrase);
                bool order = parsed.HasConfidentOrder;
                bool question = parsed.Matches.Exists(x => x.Action.IsQuery());
                bool talk = SmallTalk.Classify(phrase) != SmallTalk.Topic.None;
                Assert.IsTrue(order || question || talk, m.Id + ": \"" + phrase + "\" does nothing at sea");
            }
    }

    [Test]
    public void TurnAroundWithASideIsAHalfCircle()
    {
        var p = CommandParser.Parse("turn around to port");
        Assert.IsTrue(p.HasConfidentOrder);
        Assert.AreEqual(ShipAction.TurnPort, p.Matches[0].Action);
        Assert.AreEqual(CommandParser.TurnAroundDegrees, p.Degrees);
        Assert.IsTrue(CommandParser.Parse("turn around").SideConflict, "no side: the crew ask which way");
        Assert.AreEqual(ShipAction.FollowCourse, CommandParser.Parse("follow the light").Matches[0].Action);
        Assert.AreEqual(ShipAction.FollowCourse, CommandParser.Parse("take us home").Matches[0].Action);
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
        Assert.IsNull(StoryCatalog.Next(StoryCatalog.Missions[StoryCatalog.Missions.Count - 1]));
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

    // Coins on the channel: runs of five, clear of both harbours, worth more in a long run.
    [Test]
    public void CoinsLieInRunsClearOfTheHarbours()
    {
        var at = PathCoins.Layout(700f);
        Assert.AreEqual(PathCoins.FirstAt, at[0]);
        Assert.LessOrEqual(at[at.Count - 1], 700f - PathCoins.EndClear);
        Assert.AreEqual(PathCoins.Step, at[1] - at[0], 0.001f);
        Assert.AreEqual(PathCoins.Gap, at[PathCoins.RunLength] - at[PathCoins.RunLength - 1], 0.001f);
        Assert.AreEqual(21, at.Count); // seven runs of three, 90 m apart from 45 m, all before 640 m
        Assert.AreEqual(0, PathCoins.Layout(90f).Count, "a channel too short for coins has none");
    }

    [TestCase(0, 10)]
    [TestCase(4, 10)]
    [TestCase(5, 20)]
    [TestCase(9, 20)]
    [TestCase(10, 30)]
    [TestCase(40, 30)]
    public void LongRunsOfCoinsScoreMore(int run, int points)
    {
        Assert.AreEqual(points, StoryScore.ForCoin(run));
    }

    [Test]
    public void CoinsAreTakenByTheHullNotBesideIt()
    {
        Vector3 bow = new Vector3(0f, 1f, 9.3f), stern = new Vector3(0f, 1f, -8f);
        Assert.IsTrue(PathCoins.Reaches(bow, stern, new Vector3(4f, 3f, 0f)), "under the side of the hull");
        Assert.IsTrue(PathCoins.Reaches(bow, stern, new Vector3(0f, 3f, 12f)), "just off the bow");
        Assert.IsFalse(PathCoins.Reaches(bow, stern, new Vector3(6f, 3f, 0f)), "a cable's width off the side");
        Assert.IsFalse(PathCoins.Reaches(bow, stern, new Vector3(0f, 3f, 16f)), "well ahead");
    }

    [Test]
    public void TheReportListsOnlyTheBonusesEarned()
    {
        var all = StoryScore.Bonuses(hits: 0, onTime: true, allCargo: true, standardOrders: 7);
        CollectionAssert.AreEqual(new[] { "clean run", "on time", "all cargo", "standard orders" }, all.ConvertAll(b => b.Key));
        Assert.AreEqual(70, all[3].Value);
        Assert.AreEqual(0, StoryScore.Bonuses(hits: 2, onTime: false, allCargo: false, standardOrders: 0).Count);
    }

    // Finds off the route: open water two or three cells off the path, one per third, always reachable.
    private static List<Vector2Int> StraightPath(int column, int rows)
    {
        var path = new List<Vector2Int>();
        for (int y = 0; y < rows; y++) path.Add(new Vector2Int(column, y));
        return path;
    }

    [Test]
    public void FindsLieOffThePathOnePerThird()
    {
        var path = StraightPath(7, 30);
        var cells = SeaFinds.ChooseFinds(15, 30, (x, y) => false, new Vector2Int(7, 0), path, seed: 1201, count: 3);
        Assert.AreEqual(3, cells.Count);
        for (int i = 0; i < 3; i++)
        {
            int off = Mathf.Abs(cells[i].x - 7);
            Assert.That(off == 2 || off == 3, "find " + i + " is " + off + " cells off the path");
            Assert.That(cells[i].y >= 4 && cells[i].y <= 25, "clear of both harbours");
            Assert.That(cells[i].y >= 4 + i * 22 / 3 && cells[i].y < 4 + (i + 1) * 22 / 3, "one in each third");
        }
        CollectionAssert.AreEqual(cells, SeaFinds.ChooseFinds(15, 30, (x, y) => false, new Vector2Int(7, 0), path, 1201, 3), "same seed, same finds");
    }

    [Test]
    public void FindsAreNeverWalledOff()
    {
        // Rocks in columns 6 and 8 the whole way: only the path's own column is open water.
        var path = StraightPath(7, 30);
        var cells = SeaFinds.ChooseFinds(15, 30, (x, y) => x == 6 || x == 8, new Vector2Int(7, 0), path, 5, 3);
        Assert.AreEqual(0, cells.Count, "the water beyond the rocks can't be reached");
    }

    [Test]
    public void TightWaterHasRocksOnTwoSides()
    {
        var path = StraightPath(7, 30);
        // Two gaps between pairs of rocks, at (4, 10) and (4, 20); everywhere else open sea or one rock.
        bool Blocked(int x, int y) => (x == 3 || x == 5) && (y == 10 || y == 20);
        var spots = SeaFinds.ChooseRisk(15, 30, Blocked, new Vector2Int(7, 0), path, new List<Vector2Int>(), 9, 2);
        CollectionAssert.AreEqual(new[] { new Vector2Int(4, 10), new Vector2Int(4, 20) }, spots, "between the rocks");
    }

    [Test]
    public void TheLookoutGivesBridgeAndPlainWords()
    {
        Assert.AreEqual("Captain, I see a drifting container, fine on the starboard bow, ahead on the right. About 200 metres. Over.",
                        SeaFinds.ReportLine(FindKind.Container, 30f, 200, first: true));
        StringAssert.StartsWith("We are close to a wreck now, on the port beam, on the left, 80 metres.",
                                SeaFinds.ReportLine(FindKind.Wreck, -95f, 80, first: false));
        Assert.AreEqual("Captain, I see an old buoy, dead ahead. About 150 metres. Over.",
                        SeaFinds.ReportLine(FindKind.Buoy, 3f, 150, first: true), "said once when both wordings agree");
    }

    [Test]
    public void EachMissionHasThreeDifferentFinds()
    {
        foreach (var m in StoryCatalog.Missions)
        {
            var kinds = new HashSet<FindKind>();
            for (int i = 0; i < Logbook.FindsPerMission; i++) kinds.Add(Logbook.KindFor(m, i));
            Assert.AreEqual(Logbook.FindsPerMission, kinds.Count, m.Id);
        }
        Assert.AreEqual(StoryCatalog.Missions.Count * 3 + 4, Logbook.Entries);
    }

    [Test]
    public void TheLogbookWritesAFindOnce()
    {
        const string id = "test-log";
        PlayerPrefs.DeleteKey("log.find." + id + ".1");
        Assert.IsFalse(Logbook.HasFind(id, 1));
        Assert.IsTrue(Logbook.RecordFind(id, 1));
        Assert.IsFalse(Logbook.RecordFind(id, 1), "already in the book");
        Assert.IsTrue(Logbook.HasFind(id, 1));
        PlayerPrefs.DeleteKey("log.find." + id + ".1");
    }

    [Test]
    public void FindsScoreFirstOnTheReport()
    {
        var b = StoryScore.Bonuses(hits: 1, onTime: false, allCargo: false, standardOrders: 0, finds: 2);
        Assert.AreEqual("finds", b[0].Key);
        Assert.AreEqual(2 * StoryScore.FindPoints, b[0].Value);
    }

    [Test]
    public void BestScoreOnlyGoesUp()
    {
        const string id = "test-best";
        StoryScore.ClearBest(id);
        Assert.IsTrue(StoryScore.RecordBest(id, 500));
        Assert.IsFalse(StoryScore.RecordBest(id, 300));
        Assert.AreEqual(500, StoryScore.Best(id));
        StoryScore.ClearBest(id);
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
