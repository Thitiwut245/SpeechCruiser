using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// The evaluation record: every radio call at sea and every finished voyage, written to CSV
/// files for the study (speech accuracy, error rate and latency; chapter 3.1.6 of the report).
///
///   turns.csv:   one row per radio call — what the speech system heard, what it became
///                (order, question, small talk, word help, unclear or rejected), whether the
///                order was standard radio English, the crew's reply, and the timings:
///                speech-to-text, understanding and reply, and until the ship responded.
///   voyages.csv: one row per voyage — the result and the language report.
///
/// Files live in Application.persistentDataPath/Sessions (shown on the F8 timing panel) and
/// are appended, with a session id per run of the game and an optional participant code
/// (Settings > Study, or the command line "-participant P07"). Writing never interrupts
/// play: a failure is logged once and the game goes on.
/// </summary>
public static class ResearchLog
{
    public class Turn
    {
        public string Input = "voice";     // voice | typed
        public string Key = "order";       // order (T) | talk (Y)
        public float SpeechSeconds;
        public string Heard = "";
        public string Outcome = "";        // order | answered | word | unclear | rejected:<reason>
        public string Route = "local";     // local | classifier | chat
        public readonly List<string> Actions = new List<string>();
        public bool? Standard;
        public string Taught = "";
        public string ReplyBy = "", Reply = "";
        public bool Danger;
        public int Hull = -1;

        // Real seconds since startup; -1 when it didn't happen.
        public double Released = -1, Transcribed = -1, Executed = -1, Replied = -1;

        public int SttMs => Ms(Released, Transcribed);
        public int ReplyMs => Ms(Transcribed, Replied);
        public int TotalMs => Ms(Released, Replied);
        public int ShipMs => Ms(Released, Executed);

        private static int Ms(double a, double b) => a >= 0 && b >= 0 ? (int)Math.Round((b - a) * 1000.0) : -1;
    }

    public const string TurnsHeader =
        "session,participant,voyage,mission,time_utc,input,key,speech_s,heard,outcome,route,actions,standard,taught," +
        "reply_by,reply,stt_ms,reply_ms,total_ms,ship_ms,danger,hull";
    public const string VoyagesHeader =
        "session,participant,voyage,mission,time_utc,delivered,seconds,par_seconds,hits,mine_hits,cargo_loaded,cargo_delivered," +
        "calls,understood,understood_pct,orders,standard_orders,say_again,words_asked,avg_reaction_s,practise," +
        "avg_stt_ms,avg_total_ms,avg_ship_ms";

    public static readonly string SessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    public static string Folder => Path.Combine(Application.persistentDataPath, "Sessions");

    /// <summary>The last finished call, for the F8 panel.</summary>
    public static Turn Last { get; private set; }

    private static Turn _open;
    private static readonly List<Turn> _voyageTurns = new List<Turn>();
    private static readonly List<Turn> _sessionTurns = new List<Turn>();
    private static bool _warned;

    /// <summary>Only calls made at sea are recorded (not the menu's radio check).</summary>
    private static bool AtSea => GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing;

    private static double Now => Time.realtimeSinceStartupAsDouble;

    private const string ParticipantPref = "research.participant";
    public const int ParticipantMaxLength = 24;

    /// <summary>The participant's code: the command line wins, then what Settings saved.</summary>
    public static string Participant
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-participant") return args[i + 1];
            return PlayerPrefs.GetString(ParticipantPref, "");
        }
    }

    /// <summary>The code typed in Settings (trimmed, at most 24 characters); empty clears it.</summary>
    public static void SetParticipant(string code)
    {
        code = (code ?? "").Trim();
        if (code.Length > ParticipantMaxLength) code = code.Substring(0, ParticipantMaxLength);
        PlayerPrefs.SetString(ParticipantPref, code);
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------ the call's steps

    /// <summary>The talk key was let go: a spoken call starts.</summary>
    public static void BeginVoice(bool talkKey)
    {
        Flush();
        if (!AtSea) return;
        _open = new Turn { Key = talkKey ? "talk" : "order", Released = Now };
        Snapshot(_open);
    }

    /// <summary>An order typed into the radio box.</summary>
    public static void BeginTyped(string text)
    {
        Flush();
        if (!AtSea) return;
        double now = Now;
        _open = new Turn { Input = "typed", Released = now, Transcribed = now, Heard = text ?? "" };
        Snapshot(_open);
    }

    public static void SpeechLength(float seconds) { if (_open != null) _open.SpeechSeconds = seconds; }

    /// <summary>The speech system's text arrived.</summary>
    public static void Heard(string text)
    {
        if (_open == null) return;
        _open.Heard = text ?? "";
        _open.Transcribed = Now;
    }

    /// <summary>The call never reached the crew (too short, too quiet, noise...).</summary>
    public static void Rejected(string reason)
    {
        if (_open == null) return;
        _open.Outcome = "rejected:" + reason;
        Flush();
    }

    /// <summary>Which path understood it: the parser and the crew's own lines (local), the classifier, or the chat model.</summary>
    public static void Route(string route) { if (_open != null) _open.Route = route; }

    public static void Order(ShipAction action, bool standard, string taught)
    {
        if (_open == null) return;
        if (action != ShipAction.None) _open.Actions.Add(action.ToString());
        _open.Standard = (_open.Standard ?? true) && standard;
        if (!standard && !string.IsNullOrEmpty(taught)) _open.Taught = taught;
        if (_open.Executed < 0) _open.Executed = Now;
        Mark("order");
    }

    public static void Answered() => Mark("answered");
    public static void WordAsked() => Mark("word");
    public static void Unclear() => Mark("unclear");

    /// <summary>
    /// A crew line appeared: the first one after the call is its reply. The call is written a
    /// frame later (Tick), once the router has also said what kind of call it was.
    /// </summary>
    public static void Replied(string by, string line)
    {
        if (_open == null || _open.Transcribed < 0 || _open.Replied >= 0 || string.IsNullOrEmpty(line) || line == "...") return;
        _open.ReplyBy = by ?? "";
        _open.Reply = line;
        _open.Replied = Now;
    }

    /// <summary>Each frame: writes an answered call, or one with no reply after <paramref name="timeout"/> seconds.</summary>
    public static void Tick(double timeout)
    {
        if (_open == null) return;
        if (_open.Replied >= 0 || Now - _open.Released > timeout) Flush();
    }

    // ------------------------------------------------------------------ voyages

    /// <summary>A voyage starts: its calls are averaged on its own row.</summary>
    public static void BeginVoyage()
    {
        Flush();
        _voyageTurns.Clear();
    }

    /// <summary>A voyage ended with <paramref name="r"/>: its row goes to voyages.csv.</summary>
    public static void EndVoyage(MissionResult r)
    {
        Flush();
        if (r == null) return;
        var s = LanguageLog.Read();
        var row = new List<string>(Common())
        {
            r.Delivered ? "1" : "0", F(r.Seconds), F(r.ParSeconds), I(r.Hits), I(r.MineHits), I(r.CargoLoaded), I(r.CargoDelivered),
            I(s.Calls), I(s.Understood), I(s.UnderstoodPercent), I(s.Orders), I(s.StandardOrders), I(s.Unclear), I(s.WordsAsked),
            s.AverageReaction >= 0f ? F(s.AverageReaction) : "", string.Join("; ", s.Practise ?? new List<string>()),
            AvgText(_voyageTurns, t => t.SttMs), AvgText(_voyageTurns, t => t.TotalMs), AvgText(_voyageTurns, t => t.ShipMs),
        };
        Append("voyages.csv", VoyagesHeader, row);
    }

    // ------------------------------------------------------------------ averages for the panel

    public static int SessionCount => _sessionTurns.Count;

    /// <summary>Average of a timing over this run's calls that have it; -1 if none do.</summary>
    public static int Average(Func<Turn, int> pick) => Avg(_sessionTurns, pick);

    // ------------------------------------------------------------------ internals

    private static void Mark(string outcome)
    {
        if (_open == null) return;
        // An order outranks an answer to a question in the same call; "unclear" never overwrites.
        if (_open.Outcome == "" || (_open.Outcome != "order" && outcome == "order")) _open.Outcome = outcome;
    }

    private static void Snapshot(Turn t)
    {
        var telemetry = ShipTelemetry.Capture();
        t.Danger = telemetry.HazardOnCourse;
        var hull = UnityEngine.Object.FindAnyObjectByType<ShipHull>();
        t.Hull = hull != null ? hull.CurrentHealth : -1;
    }

    /// <summary>Writes the open call (if anything happened in it).</summary>
    public static void Flush()
    {
        var t = _open;
        _open = null;
        if (t == null) return;
        if (t.Outcome == "") t.Outcome = string.IsNullOrEmpty(t.Heard) ? "rejected:none" : "no reply";
        Last = t;
        _voyageTurns.Add(t);
        _sessionTurns.Add(t);
        Append("turns.csv", TurnsHeader, TurnRow(t));
    }

    /// <summary>The CSV fields for one call (public for the tests).</summary>
    public static List<string> TurnRow(Turn t)
    {
        return new List<string>(Common())
        {
            t.Input, t.Key, F(t.SpeechSeconds), t.Heard, t.Outcome, t.Route, string.Join(" + ", t.Actions),
            t.Standard.HasValue ? (t.Standard.Value ? "1" : "0") : "", t.Taught, t.ReplyBy, t.Reply,
            Ms(t.SttMs), Ms(t.ReplyMs), Ms(t.TotalMs), Ms(t.ShipMs), t.Danger ? "1" : "0", t.Hull >= 0 ? I(t.Hull) : "",
        };
    }

    private static string[] Common()
    {
        int voyage = GameSession.Instance != null ? GameSession.Instance.VoyagesStarted : 0;
        var mission = StoryProgress.ActiveMission;
        string what = mission != null ? mission.Label
                    : GameSession.Instance != null ? GameSession.Instance.CurrentJob.Name : "career";
        return new[] { SessionId, Participant, I(voyage), what ?? "", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
    }

    private static void Append(string file, string header, List<string> fields)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, file);
            var sb = new StringBuilder();
            if (!File.Exists(path)) sb.Append(header).Append("\r\n");
            sb.Append(Csv(fields)).Append("\r\n");
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("[RESEARCH]: could not write " + file + ": " + e.Message);
        }
    }

    /// <summary>One CSV line: fields with commas, quotes or line breaks are quoted (public for the tests).</summary>
    public static string Csv(IEnumerable<string> fields)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var raw in fields)
        {
            if (!first) sb.Append(',');
            first = false;
            string f = raw ?? "";
            if (f.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) f = "\"" + f.Replace("\"", "\"\"") + "\"";
            sb.Append(f);
        }
        return sb.ToString();
    }

    private static int Avg(List<Turn> turns, Func<Turn, int> pick)
    {
        long sum = 0; int n = 0;
        foreach (var t in turns) { int v = pick(t); if (v >= 0) { sum += v; n++; } }
        return n > 0 ? (int)(sum / n) : -1;
    }

    private static string AvgText(List<Turn> turns, Func<Turn, int> pick) { int v = Avg(turns, pick); return v >= 0 ? I(v) : ""; }
    private static string Ms(int v) => v >= 0 ? I(v) : "";
    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
