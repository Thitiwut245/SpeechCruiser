using UnityEngine;

/// <summary>Everyone who speaks in the story. The player is the captain and doesn't speak.</summary>
public enum CharacterId
{
    Ada,      // Ada Marlow, owner of Marlow & Co.
    Finn,     // lookout
    Harris,   // chief engineer
    Tomas,    // fisherman (client)
    Mia,      // student adrift in a rowboat (client)
    Nora,     // island doctor (client)
    Leo,      // tour-boat skipper (client)
    Sam,      // old lighthouse keeper (client)
}

/// <summary>How a character looks in a portrait and sounds in a dialogue scene.</summary>
public class CharacterProfile
{
    public CharacterId Id;
    public string Name;
    /// <summary>One line under the name, e.g. "Owner, Marlow &amp; Co."</summary>
    public string Role;
    /// <summary>The name plate and frame colour.</summary>
    public Color Accent;

    // Voice (Windows voices through CrewSpeech).
    public string Voice;
    public float Rate = 1f;
    public float Pitch = 1f;

    // Portrait (built by PortraitStudio).
    public Color Skin;
    public Color HairColour;
    public HairStyle Hair;
    public Color Clothes;
    public Color Trim;
    public HatStyle Hat;
    public Color HatColour;
    public bool Beard;
    public bool Glasses;
    /// <summary>Behind the bust in the portrait.</summary>
    public Color Backdrop;

    public enum HairStyle { None, Short, Bun, Long, Curly }
    public enum HatStyle { None, Beanie, CaptainCap, SouWester, Scarf }

    public CrewSpeech.Voice SpeechVoice => new CrewSpeech.Voice { Name = Voice, Rate = Rate, Pitch = Pitch };
}

/// <summary>One line of a dialogue scene.</summary>
public struct DialogueLine
{
    public CharacterId Speaker;
    public string Text;

    public DialogueLine(CharacterId speaker, string text)
    {
        Speaker = speaker;
        Text = text;
    }
}

/// <summary>A crew line over the radio once the ship is far enough along the route.</summary>
public struct RadioCue
{
    /// <summary>Share of the route sailed, 0 (just left the dock) to 1 (at the destination).</summary>
    public float At;
    /// <summary>Finn or Harris: only the crew are on the radio.</summary>
    public CharacterId Speaker;
    public string Text;

    public RadioCue(float at, CharacterId speaker, string text)
    {
        At = at;
        Speaker = speaker;
        Text = text;
    }
}

/// <summary>Strange things seen at sea. Scenery only; the crew talk about them.</summary>
public enum SightKind
{
    /// <summary>A huge dark shape gliding under the water.</summary>
    DeepShadow,
    /// <summary>A pale, glowing old sailing ship (chapter 2).</summary>
    GhostShip,
    /// <summary>Coils of a sea serpent's back breaking the surface (chapter 2).</summary>
    SerpentCoils,
    /// <summary>A soft light that leads the way home (chapter 3).</summary>
    SpiritLight,
}

/// <summary>A sight placed beside the route, and what the crew say when it comes into view.</summary>
public struct SightSpec
{
    public SightKind Kind;
    /// <summary>Share of the route where it appears.</summary>
    public float At;
    /// <summary>-1 to port (left), +1 to starboard (right).</summary>
    public int Side;
    /// <summary>Said in order when the ship comes near.</summary>
    public RadioCue[] Lines;
}

public enum MissionGoal
{
    /// <summary>Sail to the dock.</summary>
    Delivery,
    /// <summary>Stop beside someone adrift on the way, then sail to the dock.</summary>
    Rescue,
    /// <summary>Open water: the Kraken strikes; dodge it, then drive it off with the horn (KrakenFight).</summary>
    Kraken,
    /// <summary>Open water: pirates give chase; outrun them and call the navy with a Mayday (PirateChase).</summary>
    Pirates,
}

public class StoryMission
{
    /// <summary>"1-1", "1-2"... Also the save key.</summary>
    public string Id;
    public int Chapter;
    public int Number;
    public string Title;
    /// <summary>One sentence for the mission card.</summary>
    public string Summary;
    public MissionGoal Goal;
    /// <summary>Who the job is for; shown on the card.</summary>
    public CharacterId Client;
    public JobSpec Job;
    /// <summary>The new English for this mission, shown on the briefing card.</summary>
    public string[] Phrases;
    public DialogueLine[] Intro;
    public DialogueLine[] Outro;
    public RadioCue[] Radio;
    public SightSpec[] Sights;
    /// <summary>Rescue missions: share of the route where the boat is adrift.</summary>
    public float RescueAt;
    /// <summary>Rescue missions: what the crew call it ("the rowboat").</summary>
    public string RescueName;
    /// <summary>Rescue missions: said when it comes into view, and once they are aboard.</summary>
    public RadioCue RescueSpotted;
    public RadioCue RescueAboard;
    /// <summary>Said at the dock if the rescue was missed.</summary>
    public string RescueMissed;
    /// <summary>Fog, night or storm (Clear by default).</summary>
    public Weather Weather;
    /// <summary>A spirit light rises at the start and leads the ship along the channel.</summary>
    public bool Guide;
    /// <summary>Said by the crew when the guiding light appears, and when it leaves at the dock.</summary>
    public RadioCue[] GuideLines;
    /// <summary>The closing dialogue's title, if not "Mission ... complete" (the ending).</summary>
    public string OutroTitle;

    /// <summary>Kraken and pirate missions: share of the route where the encounter begins.</summary>
    public float EncounterAt;

    public string Label => "Mission " + Chapter + "-" + Number;
}

public class StoryChapter
{
    public int Number;
    public string Title;
    /// <summary>One or two sentences for the chapter card.</summary>
    public string Blurb;
    /// <summary>The English this chapter teaches, in a few words.</summary>
    public string Focus;
    public StoryMission[] Missions;
}
