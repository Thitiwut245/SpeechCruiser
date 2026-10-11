using System;
using System.IO;
using NUnit.Framework;

/// <summary>The evaluation record: CSV escaping, the timings and fields of one radio call, and files with old columns.</summary>
public class ResearchLogTests
{
    [Test]
    public void CsvQuotesOnlyWhatNeedsIt()
    {
        Assert.AreEqual("a,b", ResearchLog.Csv(new[] { "a", "b" }));
        Assert.AreEqual("\"Aye, Sir\",x", ResearchLog.Csv(new[] { "Aye, Sir", "x" }));
        Assert.AreEqual("\"Say \"\"slow ahead\"\"\"", ResearchLog.Csv(new[] { "Say \"slow ahead\"" }));
        Assert.AreEqual("\"two\nlines\",", ResearchLog.Csv(new[] { "two\nlines", null }));
    }

    [Test]
    public void TimingsComeFromTheSteps()
    {
        var t = new ResearchLog.Turn { Released = 10.0, Transcribed = 10.8, Executed = 10.85, Replied = 11.0 };
        Assert.AreEqual(800, t.SttMs);
        Assert.AreEqual(200, t.ReplyMs);
        Assert.AreEqual(1000, t.TotalMs);
        Assert.AreEqual(850, t.ShipMs);

        var noReply = new ResearchLog.Turn { Released = 5.0, Transcribed = 5.5 };
        Assert.AreEqual(-1, noReply.TotalMs);
        Assert.AreEqual(-1, noReply.ShipMs);
    }

    [Test]
    public void ARowHasAFieldForEveryColumn()
    {
        var t = new ResearchLog.Turn { Heard = "turn left", Outcome = "order", Standard = false, Taught = "port twenty",
                                       Released = 1, Transcribed = 1.5, Executed = 1.6, Replied = 1.7 };
        t.Actions.Add("TurnPort");
        var row = ResearchLog.TurnRow(t);
        Assert.AreEqual(ResearchLog.TurnsHeader.Split(',').Length, row.Count);
        CollectionAssert.Contains(row, "turn left");
        CollectionAssert.Contains(row, "TurnPort");
        CollectionAssert.Contains(row, "port twenty");
        CollectionAssert.Contains(row, "500"); // speech to text
        CollectionAssert.Contains(row, "700"); // total
    }

    private string _dir;

    [SetUp]
    public void MakeFolder()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ResearchLogTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void RemoveFolder()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public void AStaleHeaderIsRenamedAsideAndANewFileStarted()
    {
        string path = Path.Combine(_dir, "voyages.csv");
        string oldHeader = "session,participant,voyage";
        File.WriteAllText(path, oldHeader + "\r\nS1,P01,1\r\n");

        ResearchLog.AppendRow(path, "session,participant,voyage,emergencies", new[] { "S2", "P01", "2", "3" });

        var old = Directory.GetFiles(_dir, "voyages.old-*.csv");
        Assert.AreEqual(1, old.Length);
        Assert.AreEqual(new[] { oldHeader, "S1,P01,1" }, File.ReadAllLines(old[0]));
        Assert.AreEqual(new[] { "session,participant,voyage,emergencies", "S2,P01,2,3" }, File.ReadAllLines(path));
    }

    [Test]
    public void AMatchingHeaderIsAppendedInPlace()
    {
        string path = Path.Combine(_dir, "turns.csv");
        ResearchLog.AppendRow(path, "a,b", new[] { "1", "2" });
        ResearchLog.AppendRow(path, "a,b", new[] { "3", "4" });

        Assert.AreEqual(new[] { "a,b", "1,2", "3,4" }, File.ReadAllLines(path));
        Assert.AreEqual(1, Directory.GetFiles(_dir).Length);
    }
}
