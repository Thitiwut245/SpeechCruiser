using NUnit.Framework;

/// <summary>The evaluation record: CSV escaping, and the timings and fields of one radio call.</summary>
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
}
