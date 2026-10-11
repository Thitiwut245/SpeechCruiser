using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The story's characters: names, voices and looks. Voices are Windows' installed English
/// voices (CrewSpeech picks the first whose name contains Voice); several characters share
/// one voice with a different rate and pitch.
/// </summary>
public static class StoryCast
{
    private static Dictionary<CharacterId, CharacterProfile> _all;

    public static CharacterProfile Get(CharacterId id)
    {
        if (_all == null) Build();
        return _all[id];
    }

    public static IEnumerable<CharacterProfile> All
    {
        get
        {
            if (_all == null) Build();
            return _all.Values;
        }
    }

    private static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

    private static void Build()
    {
        _all = new Dictionary<CharacterId, CharacterProfile>();
        Add(new CharacterProfile
        {
            Id = CharacterId.Ada, Name = "Ada Marlow", Role = "Owner, Marlow & Co.", Accent = C(255, 199, 69),
            Voice = "Hazel", Rate = 0.95f, Pitch = 1f,
            Skin = C(232, 190, 160), HairColour = C(200, 200, 205), Hair = CharacterProfile.HairStyle.Bun,
            Clothes = C(32, 52, 92), Trim = C(220, 180, 70), Glasses = true, Backdrop = C(44, 38, 30),
        });
        Add(new CharacterProfile
        {
            Id = CharacterId.Finn, Name = "Finn", Role = "Lookout", Accent = new Color(0.30f, 0.76f, 0.70f), // the HUD's Finn colour
            Voice = "Mark", Rate = 1.08f, Pitch = 1.06f,
            Skin = C(240, 200, 170), HairColour = C(196, 98, 40), Hair = CharacterProfile.HairStyle.Short,
            Clothes = C(30, 130, 120), Trim = C(240, 240, 235), Hat = CharacterProfile.HatStyle.Beanie, HatColour = C(200, 60, 50),
            Backdrop = C(22, 48, 50),
        });
        Add(new CharacterProfile
        {
            Id = CharacterId.Harris, Name = "Harris", Role = "Chief engineer", Accent = new Color(0.86f, 0.64f, 0.25f), // the HUD's Harris colour
            Voice = "George", Rate = 0.94f, Pitch = 0.9f,
            Skin = C(214, 160, 130), HairColour = C(225, 225, 225), Hair = CharacterProfile.HairStyle.Short, Beard = true,
            Clothes = C(150, 90, 40), Trim = C(60, 50, 40), Hat = CharacterProfile.HatStyle.CaptainCap, HatColour = C(30, 36, 50),
            Backdrop = C(48, 36, 22),
        });
        Add(new CharacterProfile
        {
            Id = CharacterId.Tomas, Name = "Tomas", Role = "Fisherman", Accent = C(250, 210, 60),
            Voice = "David", Rate = 0.92f, Pitch = 0.92f,
            Skin = C(190, 135, 100), HairColour = C(40, 30, 25), Hair = CharacterProfile.HairStyle.Short, Beard = true,
            Clothes = C(240, 200, 40), Trim = C(60, 60, 60), Hat = CharacterProfile.HatStyle.SouWester, HatColour = C(240, 200, 40),
            Backdrop = C(30, 40, 52),
        });
        Add(new CharacterProfile
        {
            Id = CharacterId.Mia, Name = "Mia", Role = "Student", Accent = C(240, 120, 170),
            Voice = "Susan", Rate = 1.05f, Pitch = 1.08f,
            Skin = C(170, 115, 85), HairColour = C(30, 22, 20), Hair = CharacterProfile.HairStyle.Curly,
            Clothes = C(220, 90, 140), Trim = C(250, 240, 240),
            Backdrop = C(46, 30, 44),
        });
        Add(new CharacterProfile
        {
            Id = CharacterId.Nora, Name = "Dr. Nora Reyes", Role = "Island doctor", Accent = C(120, 200, 255),
            Voice = "Zira", Rate = 1f, Pitch = 1f,
            Skin = C(205, 150, 110), HairColour = C(70, 40, 25), Hair = CharacterProfile.HairStyle.Long,
            Clothes = C(235, 240, 245), Trim = C(60, 130, 200), Glasses = true,
            Backdrop = C(24, 40, 56),
        });
        Add(new CharacterProfile
        {
            Id = CharacterId.Leo, Name = "Leo", Role = "Tour-boat skipper", Accent = C(120, 220, 120),
            Voice = "David", Rate = 1.08f, Pitch = 1.08f,
            Skin = C(225, 180, 145), HairColour = C(230, 200, 120), Hair = CharacterProfile.HairStyle.Curly,
            Clothes = C(60, 160, 80), Trim = C(250, 250, 250), Hat = CharacterProfile.HatStyle.Scarf, HatColour = C(250, 130, 40),
            Backdrop = C(26, 46, 30),
        });
        Add(new CharacterProfile
        {
            Id = CharacterId.Sam, Name = "Old Sam", Role = "Lighthouse keeper", Accent = C(200, 180, 255),
            Voice = "George", Rate = 0.86f, Pitch = 0.82f,
            Skin = C(200, 160, 135), HairColour = C(240, 240, 240), Hair = CharacterProfile.HairStyle.Long, Beard = true,
            Clothes = C(70, 60, 110), Trim = C(200, 190, 230), Hat = CharacterProfile.HatStyle.Beanie, HatColour = C(90, 80, 70),
            Backdrop = C(34, 30, 50),
        });
    }

    private static void Add(CharacterProfile p) => _all[p.Id] = p;
}
