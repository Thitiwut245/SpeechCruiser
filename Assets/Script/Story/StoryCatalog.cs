using System.Collections.Generic;

/// <summary>
/// "The Last Ship of Marlow &amp; Co.": three chapters of three missions. Each mission is a
/// fixed map (Job.Seed and Job.Rows), a goal, the new English it teaches, and its dialogue.
/// Lines are short and plain: the players are learning English.
/// </summary>
public static class StoryCatalog
{
    public const string StoryTitle = "The Last Ship of Marlow & Co.";

    private static StoryChapter[] _chapters;
    private static List<StoryMission> _missions;

    public static StoryChapter[] Chapters
    {
        get
        {
            if (_chapters == null) Build();
            return _chapters;
        }
    }

    /// <summary>Every mission in story order.</summary>
    public static IReadOnlyList<StoryMission> Missions
    {
        get
        {
            if (_missions == null) Build();
            return _missions;
        }
    }

    public static StoryMission Find(string id)
    {
        foreach (var m in Missions) if (m.Id == id) return m;
        return null;
    }

    /// <summary>The mission after this one, or null after the last.</summary>
    public static StoryMission Next(StoryMission mission)
    {
        int i = IndexOf(mission);
        return i >= 0 && i + 1 < Missions.Count ? Missions[i + 1] : null;
    }

    public static int IndexOf(StoryMission mission)
    {
        for (int i = 0; i < Missions.Count; i++) if (Missions[i] == mission) return i;
        return -1;
    }

    // ------------------------------------------------------------ helpers

    private static DialogueLine L(CharacterId who, string text) => new DialogueLine(who, text);
    private static RadioCue R(float at, CharacterId who, string text) => new RadioCue(at, who, text);
    private const CharacterId Ada = CharacterId.Ada, Finn = CharacterId.Finn, Harris = CharacterId.Harris;

    private static JobSpec Job(string id, int rows, int containers, int reward, float density, bool mines, float weave, int towing, int seed)
    {
        return new JobSpec
        {
            Name = "STORY " + id, Containers = containers, Reward = reward, DensityScale = density, ExtraRows = 0,
            Mines = mines, MinWeave = weave, UnlockDeliveries = 0, TowingFee = towing,
            Rows = rows, Seed = seed, Story = true,
        };
    }

    private static void Build()
    {
        _chapters = new[] { ChapterOne(), ChapterTwo(), ChapterThree() };
        _missions = new List<StoryMission>();
        foreach (var chapter in _chapters)
        {
            for (int i = 0; i < chapter.Missions.Length; i++)
            {
                var m = chapter.Missions[i];
                m.Chapter = chapter.Number;
                m.Number = i + 1;
                m.Id = chapter.Number + "-" + (i + 1);
                if (m.Phrases == null) m.Phrases = new string[0];
                if (m.Intro == null) m.Intro = new DialogueLine[0];
                if (m.Outro == null) m.Outro = new DialogueLine[0];
                if (m.Radio == null) m.Radio = new RadioCue[0];
                if (m.Sights == null) m.Sights = new SightSpec[0];
                _missions.Add(m);
            }
        }
    }

    // ------------------------------------------------------------ chapter 1

    private static StoryChapter ChapterOne()
    {
        return new StoryChapter
        {
            Number = 1,
            Title = "The Rookie",
            Blurb = "Marlow & Co. has one old ship left, and a new captain: you. Learn to sail her, and keep the company alive.",
            Focus = "Speed and steering: slow ahead, stop, turn left, turn right.",
            Missions = new[]
            {
                new StoryMission
                {
                    Title = "First Delivery",
                    Summary = "Four containers to the dock at the end of the channel. Calm water.",
                    Goal = MissionGoal.Delivery,
                    Client = Ada,
                    Job = Job("1-1", rows: 20, containers: 4, reward: 250, density: 0.6f, mines: false, weave: 1.05f, towing: 50, seed: 1101),
                    Phrases = new[] { "slow ahead", "stop", "turn left", "turn right" },
                    Intro = new[]
                    {
                        L(Ada, "Good morning! You must be the new captain. I'm Ada Marlow."),
                        L(Ada, "Marlow and Company is a small shipping company. We have one ship left. This one."),
                        L(Ada, "Business is hard. If we don't deliver, we close. So... no pressure!"),
                        L(Harris, "Don't worry, Captain. I run the engine. You give the orders, and I make her go."),
                        L(Finn, "And I'm Finn, the lookout. I watch the water and tell you what I see."),
                        L(Ada, "Today is easy. Take four containers to the dock at the end of the channel."),
                        L(Harris, "Talk to us on the radio. Say \"slow ahead\" to start, and \"stop\" to stop."),
                        L(Finn, "To steer, say \"turn left\" or \"turn right\". Simple!"),
                        L(Ada, "Good luck, Captain. Bring my ship back in one piece."),
                    },
                    Radio = new[]
                    {
                        R(0.03f, Harris, "Engine is warm, Captain. Ready when you are."),
                        R(0.4f, Finn, "The water is calm today. Nice and easy."),
                        R(0.75f, Finn, "I can see the dock ahead! Keep going."),
                    },
                    Outro = new[]
                    {
                        L(Ada, "You did it! Four containers, safe and sound."),
                        L(Ada, "That is our first payment this month. Thank you, Captain."),
                        L(Harris, "Not bad for a rookie. Not bad at all."),
                        L(Finn, "Same time tomorrow?"),
                        L(Ada, "Yes. Tomorrow you have a real customer. Get some rest."),
                    },
                },
                new StoryMission
                {
                    Title = "Fish to Market",
                    Summary = "Tomas the fisherman needs his catch at the market today. More rocks, and a little faster.",
                    Goal = MissionGoal.Delivery,
                    Client = CharacterId.Tomas,
                    Job = Job("1-2", rows: 24, containers: 6, reward: 350, density: 0.75f, mines: false, weave: 1.1f, towing: 80, seed: 1201),
                    Phrases = new[] { "half ahead", "full ahead", "slow down" },
                    Intro = new[]
                    {
                        L(CharacterId.Tomas, "Hello, Captain! I'm Tomas. I catch fish, but my boat is broken."),
                        L(CharacterId.Tomas, "My fish must get to the market today. Fresh fish, fresh money!"),
                        L(Ada, "Tomas is an old friend. Please help him. And go a little faster today."),
                        L(Harris, "Say \"half ahead\" for more speed. \"Full ahead\" is our top speed."),
                        L(Finn, "There are more rocks on this route. If we are too fast, say \"slow down\"."),
                    },
                    Radio = new[]
                    {
                        R(0.15f, Finn, "Rocks on both sides, Captain. Stay in the channel."),
                        R(0.85f, Harris, "The market is close. Slow down before the dock."),
                    },
                    Sights = new[]
                    {
                        new SightSpec
                        {
                            Kind = SightKind.DeepShadow, At = 0.5f, Side = 1,
                            Lines = new[]
                            {
                                R(0f, Finn, "Captain... look to the right. Something big is under the water!"),
                                R(0f, Harris, "Big? It's a whale, lad. Or the shadow of a cloud."),
                                R(0f, Finn, "That was not a cloud..."),
                            },
                        },
                    },
                    Outro = new[]
                    {
                        L(CharacterId.Tomas, "The fish are here, and they are still fresh! Thank you!"),
                        L(CharacterId.Tomas, "Captain, did you see the big shadow in the water?"),
                        L(CharacterId.Tomas, "My grandfather talked about it. He called it the Old One of the Deep."),
                        L(Harris, "Stories, Tomas. Just old sailors' stories."),
                        L(Ada, "Stories or not, we got paid today. Well done, Captain."),
                    },
                },
                new StoryMission
                {
                    Title = "Adrift",
                    Summary = "A student is lost at sea in a rowboat. Find her, stop beside her, and bring her home.",
                    Goal = MissionGoal.Rescue,
                    Client = CharacterId.Mia,
                    Job = Job("1-3", rows: 26, containers: 5, reward: 400, density: 0.8f, mines: false, weave: 1.12f, towing: 80, seed: 1301),
                    Phrases = new[] { "stop", "slow ahead", "turn around" },
                    RescueAt = 0.55f,
                    RescueName = "the rowboat",
                    RescueSpotted = R(0f, Finn, "There! A rowboat, straight ahead! Stop the ship next to it."),
                    RescueAboard = R(0f, Finn, "We've got her! Mia is on board. Now to the dock!"),
                    RescueMissed = "We can't go in yet, Captain! Mia is still out there. Turn around and find the rowboat.",
                    Intro = new[]
                    {
                        L(Ada, "Captain, we have a problem. A student went out in a small rowboat this morning."),
                        L(Ada, "Her name is Mia. She lost her oars, and the wind is taking her out to sea."),
                        L(Finn, "I'll find her. When we see her, stop the ship right next to her boat."),
                        L(Harris, "Say \"stop\" early, Captain. The ship needs time to stop."),
                        L(Ada, "Find Mia, then take her to the dock. Please hurry."),
                    },
                    Radio = new[]
                    {
                        R(0.08f, Finn, "Eyes open. A small boat is hard to see."),
                    },
                    Outro = new[]
                    {
                        L(CharacterId.Mia, "Thank you, Captain! I was so scared out there."),
                        L(CharacterId.Mia, "And... the water went dark under my boat. Something huge swam past me."),
                        L(Finn, "The shadow! I saw it too, near the market!"),
                        L(Harris, "Hmm. Two people, the same story..."),
                        L(Ada, "Mia is safe. That's what matters. And the whole town is talking about Marlow and Company."),
                        L(Ada, "Captain, you've earned bigger work. Much bigger."),
                    },
                },
            },
        };
    }

    // ------------------------------------------------------------ chapter 2 (draft: phase 3)

    private static StoryChapter ChapterTwo()
    {
        return new StoryChapter
        {
            Number = 2,
            Title = "Reefs and Rumours",
            Blurb = "Longer routes, mines from an old war, and strange stories from every port. Ask your crew what they see.",
            Focus = "Questions and reports: What's ahead? How far is the reef?",
            Missions = new[]
            {
                new StoryMission
                {
                    Title = "Medicine Run",
                    Summary = "Dr. Nora Reyes needs medicine on her island before night.",
                    Goal = MissionGoal.Delivery,
                    Client = CharacterId.Nora,
                    Job = Job("2-1", rows: 30, containers: 8, reward: 600, density: 0.9f, mines: true, weave: 1.15f, towing: 150, seed: 2101),
                    Phrases = new[] { "what's ahead?", "how far is the reef?", "any danger?" },
                    Intro = new[]
                    {
                        L(CharacterId.Nora, "Captain, I'm Dr. Reyes. People on my island are sick, and I need this medicine today."),
                        L(Finn, "There are mines on this route, Captain. Old ones, from the war."),
                        L(Harris, "If you can't see well, ask Finn: \"What's ahead?\" or \"Any danger?\""),
                    },
                    Outro = new[]
                    {
                        L(CharacterId.Nora, "Thank you, Captain. You were fast and careful."),
                        L(Ada, "Doctor Reyes will tell everyone about us. Good work."),
                    },
                },
                new StoryMission
                {
                    Title = "Stuck on the Reef",
                    Summary = "Leo's tour boat is stuck on a reef with tourists aboard.",
                    Goal = MissionGoal.Rescue,
                    Client = CharacterId.Leo,
                    Job = Job("2-2", rows: 30, containers: 6, reward: 650, density: 0.95f, mines: true, weave: 1.15f, towing: 150, seed: 2201),
                    Phrases = new[] { "where is the boat?", "how far is it?", "slow ahead" },
                    RescueAt = 0.6f,
                    RescueName = "the tour boat",
                    RescueSpotted = R(0f, Finn, "I see the tour boat! Stop the ship next to it, Captain."),
                    RescueAboard = R(0f, Finn, "Everyone is aboard! Let's take them home."),
                    RescueMissed = "Not yet, Captain! The tourists are still on the reef. Turn around.",
                    Intro = new[]
                    {
                        L(CharacterId.Leo, "Help! This is Leo. My tour boat is stuck on a reef, and the tourists are scared!"),
                        L(Ada, "Go now, Captain. Ask Finn where the boat is."),
                    },
                    Outro = new[]
                    {
                        L(CharacterId.Leo, "Thank you, Captain! A tourist said she saw a ship made of light in the mist."),
                        L(Harris, "A ghost ship? Now I've heard everything."),
                    },
                },
                new StoryMission
                {
                    Title = "Lights in the Fog",
                    Summary = "A night run through thick fog to Old Sam's lighthouse.",
                    Goal = MissionGoal.Delivery,
                    Client = CharacterId.Sam,
                    Job = Job("2-3", rows: 32, containers: 8, reward: 750, density: 1f, mines: true, weave: 1.2f, towing: 200, seed: 2301),
                    Phrases = new[] { "what can you see?", "is it safe?", "where is the dock?" },
                    Intro = new[]
                    {
                        L(CharacterId.Sam, "The lamp oil for my lighthouse, Captain. Without it, ships will be lost tonight."),
                        L(Finn, "The fog is thick. I'll be your eyes. Just ask me."),
                    },
                    Outro = new[]
                    {
                        L(CharacterId.Sam, "You came through the fog. Did you see her? The ship of light?"),
                        L(CharacterId.Sam, "The old ones say the sea has a spirit. It helps sailors who are kind."),
                    },
                },
            },
        };
    }

    // ------------------------------------------------------------ chapter 3 (draft: phase 4)

    private static StoryChapter ChapterThree()
    {
        return new StoryChapter
        {
            Number = 3,
            Title = "Captain",
            Blurb = "One big contract can save the company. Then the storm comes.",
            Focus = "Full, polite sentences: Please turn to port and slow down.",
            Missions = new[]
            {
                new StoryMission
                {
                    Title = "The Big Contract",
                    Summary = "Fifteen containers for the big city port. This job saves the company.",
                    Goal = MissionGoal.Delivery,
                    Client = Ada,
                    Job = Job("3-1", rows: 36, containers: 15, reward: 1500, density: 1.1f, mines: true, weave: 1.25f, towing: 400, seed: 3101),
                    Phrases = new[] { "please turn to port", "could you slow down, please?", "thank you, Harris" },
                    Intro = new[]
                    {
                        L(Ada, "Captain, this is it. If we deliver this cargo, Marlow and Company is saved."),
                        L(Harris, "Talk to us like a real captain today. \"Please turn to port.\" Nice and clear."),
                    },
                    Outro = new[]
                    {
                        L(Ada, "We did it! The company is saved!"),
                    },
                },
                new StoryMission
                {
                    Title = "Storm Warning",
                    Summary = "A storm hits the coast. A fishing boat is in trouble.",
                    Goal = MissionGoal.Rescue,
                    Client = CharacterId.Tomas,
                    Job = Job("3-2", rows: 34, containers: 6, reward: 900, density: 1.1f, mines: true, weave: 1.2f, towing: 300, seed: 3201),
                    Phrases = new[] { "please stop the engine", "turn to starboard, please", "is everyone safe?" },
                    RescueAt = 0.5f,
                    RescueName = "Tomas's boat",
                    RescueSpotted = R(0f, Finn, "Tomas's boat! Stop next to it, Captain, please!"),
                    RescueAboard = R(0f, Finn, "Tomas is aboard! Get us out of this storm!"),
                    RescueMissed = "Captain, Tomas is still out there! We have to go back.",
                    Intro = new[]
                    {
                        L(Ada, "A storm is coming, and Tomas is still at sea. His radio is dead."),
                    },
                    Outro = new[]
                    {
                        L(CharacterId.Tomas, "You came for me, Captain. In this storm. Thank you."),
                    },
                },
                new StoryMission
                {
                    Title = "The Way Home",
                    Summary = "Lost in the storm, far from port. Something in the water wants to help.",
                    Goal = MissionGoal.Delivery,
                    Client = Ada,
                    Job = Job("3-3", rows: 38, containers: 4, reward: 1200, density: 1.15f, mines: true, weave: 1.25f, towing: 300, seed: 3301),
                    Phrases = new[] { "follow the light", "please keep this course", "take us home" },
                    Intro = new[]
                    {
                        L(Harris, "Captain, the compass is spinning. I don't know where home is."),
                        L(Finn, "Captain... there's a light in the water. It's moving. I think it wants us to follow."),
                    },
                    Outro = new[]
                    {
                        L(Ada, "You brought everyone home. Captain, the company is yours to sail. Welcome to the family."),
                    },
                },
            },
        };
    }
}
