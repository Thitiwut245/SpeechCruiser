using System.Collections.Generic;

/// <summary>
/// "Try saying" for beginners: two or three phrases that fit what is happening right now,
/// shown as chips beside the radio. A rock ahead suggests the turn away from it and slowing
/// down; a stopped ship suggests getting going; fog suggests asking the lookout; the dock in
/// sight suggests slowing down. In chapter 3 the orders come with "please", as that chapter
/// teaches. Pure logic over a snapshot, so it can be tested.
/// </summary>
public static class PhraseHints
{
    public struct Situation
    {
        /// <summary>A hazard is on the ship's course.</summary>
        public bool Threat;
        /// <summary>Which way to turn away from it: "left" or "right".</summary>
        public string AvoidSide;
        /// <summary>The engine order: -2 full astern .. 0 stop .. 5 flank.</summary>
        public int Gear;
        public int SpeedKn;
        public bool DockClose;
        public bool Fog;
        /// <summary>A rescue is waiting and the ship is near it.</summary>
        public bool RescueNear;
        /// <summary>Chapter 3 of the story: orders said politely.</summary>
        public bool Polite;
    }

    public const int Max = 3;

    public static List<string> For(Situation s)
    {
        var list = new List<string>(4);

        if (s.RescueNear)
        {
            Add(list, s, "stop", true);
            Add(list, s, "slow ahead", true);
        }
        else if (s.Threat)
        {
            string side = s.AvoidSide == "left" ? "left" : "right";
            Add(list, s, "turn " + side, true);
            if (s.Gear >= 3) Add(list, s, "slow down", true);
            Add(list, s, "hard " + side, true);
        }
        else if (s.Gear <= 0 && s.SpeedKn <= 1)
        {
            Add(list, s, "slow ahead", true);
            Add(list, s, "half ahead", true);
        }
        else if (s.DockClose)
        {
            if (s.Gear >= 2) Add(list, s, "slow down", true);
            Add(list, s, "where is the dock?", false);
            Add(list, s, "stop", true);
        }

        if (s.Fog)
        {
            Add(list, s, "what can you see?", false);
            Add(list, s, "is it safe?", false);
        }

        // Nothing pressing: keep the voyage moving and keep asking.
        if (list.Count < 2)
        {
            Add(list, s, "any danger?", false);
            if (s.Gear > 0 && s.Gear < 4) Add(list, s, "full ahead", true);
            Add(list, s, "where is the dock?", false);
        }

        if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
        return list;
    }

    private static void Add(List<string> list, Situation s, string phrase, bool order)
    {
        if (order && s.Polite) phrase = "please " + phrase;
        if (!list.Contains(phrase)) list.Add(phrase);
    }
}
