/// <summary>
/// Which voyage the radio is on. The radio, the classifier and the crew chat outlive scene
/// loads (they sit on the Services prefab), so anything that finishes later than it started
/// (a Whisper decode, a classifier call, a chat reply) checks this before acting: a result
/// from an earlier scene is dropped instead of steering the new ship.
/// </summary>
public static class RadioEpoch
{
    public static int Current { get; private set; }

    /// <summary>Called by SceneLoader when a scene change begins.</summary>
    public static void Advance()
    {
        Current++;
    }

    public static bool IsCurrent(int epoch) => epoch == Current;
}
