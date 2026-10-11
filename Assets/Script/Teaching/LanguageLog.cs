using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// How the Captain spoke on this voyage, for the language report on the results page (the
/// in-game evaluation): how many radio calls the crew understood, how many orders were in
/// standard radio English, how often the crew had to ask again, how fast the Captain gave
/// an order after a danger warning, and the standard phrases worth practising next time.
/// Cleared when a voyage starts. Pure counting, so it can be tested.
/// </summary>
public static class LanguageLog
{
    public struct Summary
    {
        public int Calls;          // radio calls that reached the crew
        public int Understood;     // orders carried out, questions answered, talk answered
        public int Orders;         // orders carried out
        public int StandardOrders; // ... of which in standard radio English
        public int Unclear;        // "say again"
        public int WordsAsked;     // "what does starboard mean?", "how do I say ...?"
        public float AverageReaction; // seconds from a danger warning to the next order; <0 if none
        public List<string> Practise; // standard phrases for orders said another way, most frequent first
        public int Emergencies;      // story emergency calls (EmergencyCall)
        public int EmergenciesSaved; // ... answered in time
        // What the certificates count (Progression): standard orders by kind, questions, talk.
        public int EngineStandard, HelmStandard, HelmDegrees, Questions, CrewTalk;

        public int UnderstoodPercent => Calls > 0 ? Mathf.RoundToInt(100f * Mathf.Min(Understood, Calls) / Calls) : 0;
    }

    private static int _calls, _understood, _orders, _standard, _unclear, _words, _emergencies, _saved;
    private static int _engineStd, _helmStd, _helmDeg, _questions, _talk;

    /// <summary>An order was carried out: which, and whether it was said in standard radio English.</summary>
    public static event System.Action<ShipAction, bool> OnOrder;
    private static readonly List<float> _reactions = new List<float>();
    private static readonly Dictionary<string, int> _practise = new Dictionary<string, int>();
    private static float _warnedAt = -1f;
    private static bool _hooked;

    /// <summary>A voyage starts: forget the last one.</summary>
    public static void Begin()
    {
        _calls = _understood = _orders = _standard = _unclear = _words = _emergencies = _saved = 0;
        _engineStd = _helmStd = _helmDeg = _questions = _talk = 0;
        _reactions.Clear();
        _practise.Clear();
        _warnedAt = -1f;
        if (!_hooked)
        {
            _hooked = true;
            RadarSystem.OnHazardDetected += _ => Warned(Time.time);
        }
    }

    /// <summary>The Captain made a radio call that got through to the crew.</summary>
    public static void Call() => _calls++;

    /// <summary>A question or a remark was answered.</summary>
    public static void Answered() { _understood++; ResearchLog.Answered(); }

    /// <summary>The crew had to ask again.</summary>
    public static void Unclear() { _unclear++; ResearchLog.Unclear(); }

    /// <summary>The Captain asked about a word or how to say something.</summary>
    public static void WordAsked() { _words++; _talk++; _understood++; ResearchLog.WordAsked(); }

    /// <summary>A report was asked for and given ("any danger?", "where is the dock?").</summary>
    public static void Question() { _questions++; Answered(); }

    /// <summary>The crew answered small talk or a chat.</summary>
    public static void Talked() { _talk++; Answered(); }

    /// <summary>A danger warning sounded at <paramref name="time"/> (game seconds).</summary>
    public static void Warned(float time)
    {
        if (_warnedAt < 0f) _warnedAt = time;
    }

    /// <summary>
    /// An order was carried out at <paramref name="time"/>. <paramref name="standardPhrase"/> is
    /// what it should be called when the Captain said it another way, else null.
    /// </summary>
    public static void Order(bool standard, string standardPhrase, float time, ShipAction action = ShipAction.None, bool degrees = false)
    {
        ResearchLog.Order(action, standard, standardPhrase);
        if (standard && action.IsHelm()) { _helmStd++; if (degrees) _helmDeg++; }
        else if (standard && action.Category() == ActionCategory.Engine) _engineStd++;
        _orders++;
        _understood++;
        if (standard) _standard++;
        else if (!string.IsNullOrEmpty(standardPhrase))
            _practise[standardPhrase] = _practise.TryGetValue(standardPhrase, out int n) ? n + 1 : 1;
        if (_warnedAt >= 0f)
        {
            _reactions.Add(Mathf.Max(0f, time - _warnedAt));
            _warnedAt = -1f;
        }
        OnOrder?.Invoke(action, standard);
    }

    /// <summary>A story emergency call ended: answered in time (<paramref name="saved"/>) or not.</summary>
    public static void Emergency(bool saved)
    {
        _emergencies++;
        if (saved) _saved++;
    }

    public static Summary Read()
    {
        return new Summary
        {
            Calls = _calls,
            Understood = _understood,
            Orders = _orders,
            StandardOrders = _standard,
            Unclear = _unclear,
            WordsAsked = _words,
            AverageReaction = _reactions.Count > 0 ? _reactions.Average() : -1f,
            Practise = _practise.OrderByDescending(p => p.Value).Select(p => p.Key).Take(3).ToList(),
            Emergencies = _emergencies,
            EmergenciesSaved = _saved,
            EngineStandard = _engineStd,
            HelmStandard = _helmStd,
            HelmDegrees = _helmDeg,
            Questions = _questions,
            CrewTalk = _talk,
        };
    }
}
