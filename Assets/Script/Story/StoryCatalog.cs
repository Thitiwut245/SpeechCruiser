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
    private const CharacterId Tomas = CharacterId.Tomas, Mia = CharacterId.Mia, Nora = CharacterId.Nora, Leo = CharacterId.Leo, Sam = CharacterId.Sam;

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
                if (m.GuideLines == null) m.GuideLines = new RadioCue[0];
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
            Focus = "Orders: slow ahead, half ahead, stop, turn left, turn right.",
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
                        R(0.15f, Finn, "Follow the channel, Captain. Say \"turn left\" or \"turn right\" to steer."),
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
                    Client = Tomas,
                    Job = Job("1-2", rows: 24, containers: 6, reward: 350, density: 0.75f, mines: false, weave: 1.1f, towing: 80, seed: 1201),
                    Phrases = new[] { "half ahead", "full ahead", "slow down" },
                    Intro = new[]
                    {
                        L(Tomas, "Hello, Captain! I'm Tomas. I catch fish, but my boat is broken."),
                        L(Tomas, "My fish must get to the market today. Fresh fish, fresh money!"),
                        L(Ada, "Tomas is an old friend. Please help him. And go a little faster today."),
                        L(Harris, "Say \"half ahead\" for more speed. \"Full ahead\" is our top speed."),
                        L(Finn, "There are more rocks on this route. If we are too fast, say \"slow down\"."),
                    },
                    Radio = new[]
                    {
                        R(0.06f, Harris, "Want more speed? Say \"half ahead\". Too fast? \"Slow down\"."),
                        R(0.2f, Finn, "Rocks on both sides, Captain. Stay in the channel."),
                        R(0.85f, Harris, "The market is close. Slow down before the dock."),
                    },
                    Sights = new[]
                    {
                        new SightSpec
                        {
                            Kind = SightKind.DeepShadow, At = 0.5f, Side = 1,
                            Lines = new[]
                            {
                                R(0f, Finn, "Captain... look to the right. Something big is in the water!"),
                                R(0f, Harris, "Big? It's a whale, lad. Or the shadow of a cloud."),
                                R(0f, Finn, "That was not a cloud..."),
                            },
                        },
                    },
                    Outro = new[]
                    {
                        L(Tomas, "The fish are here, and they are still fresh! Thank you!"),
                        L(Tomas, "Captain, did you see the big shadow in the water?"),
                        L(Tomas, "My grandfather talked about it. He called it the Old One of the Deep."),
                        L(Harris, "Stories, Tomas. Just old sailors' stories."),
                        L(Ada, "Stories or not, we got paid today. Well done, Captain."),
                    },
                },
                new StoryMission
                {
                    Title = "Adrift",
                    Summary = "A student is lost at sea in a rowboat. Find her, stop beside her, and bring her home.",
                    Goal = MissionGoal.Rescue,
                    Client = Mia,
                    Job = Job("1-3", rows: 26, containers: 5, reward: 400, density: 0.8f, mines: false, weave: 1.12f, towing: 80, seed: 1301),
                    Phrases = new[] { "stop", "slow ahead", "turn around to port" },
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
                        R(0.85f, Harris, "There's the dock. \"Slow ahead\", Captain. Mia has had enough adventure today."),
                    },
                    Outro = new[]
                    {
                        L(Mia, "Thank you, Captain! I was so scared out there."),
                        L(Mia, "And... the water went dark under my boat. Something huge swam past me."),
                        L(Finn, "The shadow! I saw it too, near the market!"),
                        L(Harris, "Hmm. Two people, the same story..."),
                        L(Ada, "Mia is safe. That's what matters. And the whole town is talking about Marlow and Company."),
                        L(Ada, "Captain, you've earned bigger work. Much bigger."),
                    },
                },
            },
        };
    }

    // ------------------------------------------------------------ chapter 2

    private static StoryChapter ChapterTwo()
    {
        return new StoryChapter
        {
            Number = 2,
            Title = "Reefs and Rumours",
            Blurb = "Longer routes, old mines from the war, and strange stories in every port. When you can't see, ask your crew.",
            Focus = "Questions: What's ahead? How far is the reef? Is it safe?",
            Missions = new[]
            {
                new StoryMission
                {
                    Title = "Medicine Run",
                    Summary = "Dr. Nora Reyes needs medicine on her island before night. Watch out for old mines.",
                    Goal = MissionGoal.Delivery,
                    Client = Nora,
                    Job = Job("2-1", rows: 30, containers: 8, reward: 600, density: 0.9f, mines: true, weave: 1.15f, towing: 150, seed: 2101),
                    Phrases = new[] { "what's ahead?", "any danger?", "how far is the reef?" },
                    Intro = new[]
                    {
                        L(Nora, "Hello, Captain. I'm Doctor Reyes. Many people on my island are sick."),
                        L(Nora, "This medicine must be on the island tonight. Can you help me?"),
                        L(Ada, "Of course we can. Captain, this route has old mines from the war. Be careful."),
                        L(Finn, "Mines blink red. If you can't see well, ask me: \"What's ahead?\" or \"Any danger?\""),
                        L(Harris, "And ask him how far things are. Finn loves numbers."),
                        L(Finn, "I do love numbers."),
                    },
                    Radio = new[]
                    {
                        R(0.05f, Finn, "Long route today. Ask me anything, Captain. What's ahead, any danger, how far."),
                        R(0.4f, Harris, "Steady, Captain. A mine is worse than a reef. Go around them wide."),
                        R(0.88f, Finn, "I can see the island! The doctor's dock is just ahead."),
                    },
                    Sights = new[]
                    {
                        new SightSpec
                        {
                            Kind = SightKind.DeepShadow, At = 0.62f, Side = -1,
                            Lines = new[]
                            {
                                R(0f, Finn, "Captain, on the left! The big shadow again! Is it following us?"),
                                R(0f, Harris, "Or maybe we are following it, lad."),
                            },
                        },
                    },
                    Outro = new[]
                    {
                        L(Nora, "The medicine is here, and it's in time. Thank you, Captain. You were fast and careful."),
                        L(Nora, "On my island, the old people say the sea is alive. They put flowers on the water for it."),
                        L(Finn, "Flowers for the shadow?"),
                        L(Nora, "They call it the Old One of the Deep. They say it protects kind sailors."),
                        L(Ada, "Then let's be kind sailors. Doctor Reyes, thank you for choosing Marlow and Company."),
                    },
                },
                new StoryMission
                {
                    Title = "Stuck on the Reef",
                    Summary = "Leo's tour boat is stuck on a reef, full of scared tourists. Find it and bring them home.",
                    Goal = MissionGoal.Rescue,
                    Client = Leo,
                    Job = Job("2-2", rows: 30, containers: 6, reward: 650, density: 0.95f, mines: true, weave: 1.15f, towing: 150, seed: 2201),
                    Phrases = new[] { "where is the boat?", "how far is it?", "slow ahead" },
                    RescueAt = 0.6f,
                    RescueName = "the tour boat",
                    RescueSpotted = R(0f, Finn, "I see the tour boat! Stop the ship next to it, Captain."),
                    RescueAboard = R(0f, Finn, "Everyone is aboard! Twelve tourists and one very happy Leo."),
                    RescueMissed = "Not yet, Captain! The tourists are still on the reef. Turn around and find them.",
                    Intro = new[]
                    {
                        L(Leo, "Hello? Hello! This is Leo, from Sunny Tours. My boat is stuck on a reef!"),
                        L(Leo, "I have twelve tourists, and they are very scared. Please hurry!"),
                        L(Ada, "We're coming, Leo. Captain, you don't know where the boat is. Ask Finn."),
                        L(Finn, "Ask me \"Where is the boat?\" and \"How far is it?\" I'll tell you."),
                        L(Harris, "When you are close, go slow. \"Slow ahead\", then \"stop\"."),
                    },
                    Radio = new[]
                    {
                        R(0.08f, Finn, "The tour boat is somewhere ahead. Ask me where it is."),
                        R(0.85f, Harris, "Nearly home. Our passengers look green. Gently now."),
                    },
                    Sights = new[]
                    {
                        new SightSpec
                        {
                            Kind = SightKind.SerpentCoils, At = 0.35f, Side = -1,
                            Lines = new[]
                            {
                                R(0f, Finn, "Captain! Something long in the water on the left! Like a snake!"),
                                R(0f, Harris, "A sea serpent? Rubbish. It's a log. A very long... moving... log."),
                                R(0f, Finn, "Logs don't have eyes, Harris!"),
                            },
                        },
                    },
                    Outro = new[]
                    {
                        L(Leo, "Captain, you saved my business! And my tourists!"),
                        L(Leo, "They took photos of a sea serpent! Nobody at home will believe them."),
                        L(Harris, "I don't believe them either."),
                        L(Finn, "Harris! You saw it too!"),
                        L(Harris, "...I saw something. That's all I'm saying."),
                        L(Ada, "Captain, Old Sam at the lighthouse needs lamp oil tonight. And the fog is coming in."),
                    },
                },
                new StoryMission
                {
                    Title = "Lights in the Fog",
                    Summary = "A night run through thick fog to Old Sam's lighthouse. You can't see far: ask.",
                    Goal = MissionGoal.Delivery,
                    Client = Sam,
                    Job = Job("2-3", rows: 30, containers: 6, reward: 750, density: 0.9f, mines: true, weave: 1.15f, towing: 200, seed: 2301),
                    Weather = Weather.NightFog,
                    Phrases = new[] { "what can you see?", "is it safe?", "where is the dock?" },
                    Intro = new[]
                    {
                        L(Sam, "Good evening, Captain. I am Sam. I keep the lighthouse on Grey Rock."),
                        L(Sam, "My lamp oil is almost gone. Without the light, ships will hit the rocks tonight."),
                        L(Finn, "The fog is very thick, Captain. You won't see much. But I have the radar."),
                        L(Finn, "Ask me: \"What can you see?\" or \"Is it safe?\" And I'll be your eyes."),
                        L(Harris, "Go slow in the fog. Slow is safe."),
                    },
                    Radio = new[]
                    {
                        R(0.04f, Finn, "Fog everywhere. Ask me what I can see, Captain."),
                        R(0.3f, Harris, "Nice and slow. I can't see the front of my own ship."),
                        R(0.85f, Finn, "A light! The lighthouse! Ask me where the dock is."),
                    },
                    Sights = new[]
                    {
                        new SightSpec
                        {
                            Kind = SightKind.GhostShip, At = 0.55f, Side = 1,
                            Lines = new[]
                            {
                                R(0f, Finn, "Captain... a ship, on the right. It's glowing. It has old sails."),
                                R(0f, Harris, "There are no other ships out tonight. Not one."),
                                R(0f, Finn, "Then what is that?"),
                                R(0f, Harris, "...Keep going, Captain. Don't stop."),
                            },
                        },
                    },
                    Outro = new[]
                    {
                        L(Sam, "You came through the fog. Thank you. The light will burn all night."),
                        L(Sam, "Tell me, Captain. Did you see her out there? The ship of light?"),
                        L(Finn, "We saw it! Glowing sails, and nobody on board!"),
                        L(Sam, "That is the Lantern. Long ago, she guided lost sailors home."),
                        L(Sam, "She belongs to the Old One of the Deep, the spirit of this sea. It helps kind sailors."),
                        L(Sam, "But not everything in the deep is kind. Out past the Deep Water lives the Kraken."),
                        L(Harris, "...Well. I need a cup of tea."),
                    },
                },
                new StoryMission
                {
                    Title = "The Legend",
                    Summary = "Take Sam's supplies across the Deep Water. Open sea, no rocks. And maybe something else.",
                    Goal = MissionGoal.Kraken,
                    Client = Sam,
                    Job = Job("2-4", rows: 30, containers: 5, reward: 800, density: 0f, mines: false, weave: 1.05f, towing: 200, seed: 2401),
                    EncounterAt = 0.22f,
                    Phrases = new[] { "hard a port", "hard a starboard", "sound the horn" },
                    Intro = new[]
                    {
                        L(Sam, "Captain, my island needs food and oil. The fastest way is across the Deep Water."),
                        L(Sam, "No rocks out there. Only deep, deep sea. And the Kraken, if you believe old stories."),
                        L(Finn, "A sea monster? With giant arms?"),
                        L(Sam, "Old sailors say it hates one thing: the sound of a ship's horn."),
                        L(Harris, "If an arm comes up, turn away fast. Say \"hard a port\" or \"hard a starboard\"."),
                        L(Finn, "And if it shows its face, Captain, say \"sound the horn\"! Loud!"),
                    },
                    Radio = new[]
                    {
                        R(0.04f, Finn, "Deep Water, Captain. No rocks for miles. Very quiet."),
                        R(0.15f, Harris, "Too quiet. I don't like it."),
                        R(0.9f, Finn, "Sam's island! We made it across!"),
                    },
                    Outro = new[]
                    {
                        L(Sam, "You met the Kraken... and you drove it away with a horn! Ha!"),
                        L(Sam, "My grandfather said only a calm captain can do that. You listen, and you speak clearly."),
                        L(Finn, "My legs are still shaking."),
                        L(Harris, "That's the engine, lad. ...No, it's my legs too."),
                        L(Ada, "Get some sleep, everyone. Tomorrow is the most important day for this company."),
                    },
                },
            },
        };
    }

    // ------------------------------------------------------------ chapter 3

    private static StoryChapter ChapterThree()
    {
        return new StoryChapter
        {
            Number = 3,
            Title = "Captain",
            Blurb = "One big contract can save the company. Then the storm comes, and only the sea can show you home.",
            Focus = "Polite, full sentences: Please turn to port. Could you slow down, please?",
            Missions = new[]
            {
                new StoryMission
                {
                    Title = "The Big Contract",
                    Summary = "Fifteen containers for the city port. Deliver them safely, and the company is saved.",
                    Goal = MissionGoal.Delivery,
                    Client = Ada,
                    Job = Job("3-1", rows: 34, containers: 15, reward: 1500, density: 1.05f, mines: true, weave: 1.2f, towing: 400, seed: 3101),
                    Phrases = new[] { "please turn to port", "could you slow down, please?", "thank you, Harris" },
                    Intro = new[]
                    {
                        L(Ada, "Captain, today is the big day. The city port wants fifteen containers."),
                        L(Ada, "If they arrive safely, the city will give us a contract for a whole year."),
                        L(Ada, "That would save Marlow and Company."),
                        L(Harris, "Big day, big cargo. So talk to us like a real captain. \"Please turn to port.\""),
                        L(Finn, "And say thank you. Harris works much better when you say thank you."),
                        L(Harris, "That is true."),
                    },
                    Radio = new[]
                    {
                        R(0.04f, Harris, "Fifteen containers, Captain. She's heavy today. Turns are slow."),
                        R(0.5f, Finn, "Halfway there. The city is waiting for us."),
                        R(0.86f, Finn, "I can see the city port! Please bring her in slowly, Captain."),
                    },
                    Sights = new[]
                    {
                        new SightSpec
                        {
                            Kind = SightKind.DeepShadow, At = 0.66f, Side = -1,
                            Lines = new[]
                            {
                                R(0f, Finn, "Captain, on the left. The Old One. It's swimming next to us."),
                                R(0f, Harris, "Like it's... keeping us company."),
                            },
                        },
                    },
                    Outro = new[]
                    {
                        L(Ada, "Fifteen containers, and every one is safe! The city signed the contract!"),
                        L(Ada, "Marlow and Company is saved. Thank you, Captain. Thank you, all of you."),
                        L(Finn, "We did it!"),
                        L(Ada, "One more job before we celebrate. The city's first payment must go to our bank in Southport."),
                        L(Ada, "The way is through the Strait. Ships say there are pirates there now."),
                    },
                },
                new StoryMission
                {
                    Title = "Pirate Waters",
                    Summary = "Carry the city's payment through the Strait. Pirates hunt there: run, and call for help.",
                    Goal = MissionGoal.Pirates,
                    Client = Ada,
                    Job = Job("3-2", rows: 32, containers: 6, reward: 1000, density: 0f, mines: false, weave: 1.05f, towing: 300, seed: 3151),
                    EncounterAt = 0.16f,
                    Phrases = new[] { "flank speed", "mayday, under attack by pirates", "hard a starboard" },
                    Intro = new[]
                    {
                        L(Ada, "Captain, the payment is in a locked box on deck. Please take it to Southport."),
                        L(Ada, "If pirates come, do not fight. Run, and call for help."),
                        L(Harris, "Our old girl is fast at \"flank speed\". Say it, and I give you everything."),
                        L(Finn, "And the call for help is a Mayday: \"Mayday, under attack by pirates\"."),
                        L(Finn, "The navy listens on the radio. They will come."),
                        L(Harris, "If a boat comes close, turn away hard. They can't climb on a ship that moves."),
                    },
                    Radio = new[]
                    {
                        R(0.04f, Finn, "Entering the Strait. Open water, Captain. I'm watching every side."),
                        R(0.9f, Finn, "Southport! Bring her in, Captain."),
                    },
                    Outro = new[]
                    {
                        L(Ada, "The payment is in the bank. And you kept everyone safe. Well done, Captain."),
                        L(Finn, "Did you see the navy ship? Huge! The pirates ran like rabbits!"),
                        L(Harris, "Hmm. The sky in the west is very dark. I don't like it."),
                        L(Ada, "A storm? Then everyone stays in port tonight. Everyone."),
                    },
                },
                new StoryMission
                {
                    Title = "Storm Warning",
                    Summary = "The storm is here, and Tomas is still at sea with a dead radio. Find him.",
                    Goal = MissionGoal.Rescue,
                    Client = Tomas,
                    Job = Job("3-3", rows: 30, containers: 4, reward: 900, density: 1f, mines: true, weave: 1.15f, towing: 300, seed: 3201),
                    Weather = Weather.Storm,
                    Phrases = new[] { "please stop the engine", "turn to starboard, please", "is everyone safe?" },
                    RescueAt = 0.5f,
                    RescueName = "Tomas's boat",
                    RescueSpotted = R(0f, Finn, "Tomas's boat! There! Please stop the engine next to it, Captain!"),
                    RescueAboard = R(0f, Finn, "Tomas is aboard! He's cold, but he's safe!"),
                    RescueMissed = "Captain, Tomas is still out there! Please, we have to go back for him.",
                    Intro = new[]
                    {
                        L(Ada, "Captain, bad news. Tomas went fishing this morning, and now the storm is here."),
                        L(Ada, "His radio is dead. Nobody else will go out in this weather."),
                        L(Harris, "Then we go. The wind will push us sideways, Captain. Keep turning back to the channel."),
                        L(Finn, "I'll find him. Please be careful, Captain."),
                    },
                    Radio = new[]
                    {
                        R(0.06f, Harris, "This wind is strong! It's pushing us. Watch the reefs."),
                        R(0.78f, Finn, "The storm is getting worse. Gull Island harbour is close. Let's get in!"),
                    },
                    Outro = new[]
                    {
                        L(Tomas, "You came for me, Captain. In this storm. Thank you."),
                        L(Tomas, "When my boat broke, something huge came up under it. It held my boat up until you came."),
                        L(Finn, "The Old One!"),
                        L(Ada, "Captain, can you hear me? The storm will last all night. You must come home now."),
                        L(Harris, "Captain... the compass is going crazy. I don't know where home is."),
                    },
                },
                new StoryMission
                {
                    Title = "The Way Home",
                    Summary = "Lost in the storm, with a crazy compass. A light in the water shows the way.",
                    Goal = MissionGoal.Delivery,
                    Client = Tomas,
                    Job = Job("3-4", rows: 34, containers: 4, reward: 1200, density: 1.1f, mines: true, weave: 1.2f, towing: 300, seed: 3301),
                    Weather = Weather.Storm,
                    Guide = true,
                    GuideLines = new[]
                    {
                        R(0f, Finn, "There it is! The light! Please follow the light, Captain!"),
                        R(0f, Finn, "The light is going down into the water... Captain, we're home!"),
                    },
                    Phrases = new[] { "follow the light", "please keep this course", "take us home" },
                    Intro = new[]
                    {
                        L(Harris, "Captain, the compass is spinning. The storm is too strong. I can't tell where home is."),
                        L(Finn, "Captain... there's a light in the water. It's moving. I think it wants us to follow."),
                        L(Tomas, "The Lantern's light. The old stories are true."),
                        L(Harris, "Stories don't steer ships, Tomas."),
                        L(Finn, "Then let's follow the story. Please, Captain. Take us home."),
                    },
                    Radio = new[]
                    {
                        R(0.3f, Harris, "The light is staying away from the rocks. It knows the way!"),
                        R(0.55f, Finn, "Please keep this course, Captain. Follow it!"),
                    },
                    Sights = new[]
                    {
                        new SightSpec
                        {
                            Kind = SightKind.DeepShadow, At = 0.75f, Side = 1,
                            Lines = new[]
                            {
                                R(0f, Finn, "Look, on the right! The Old One is swimming with us!"),
                                R(0f, Harris, "...All right. All right. I believe it now."),
                            },
                        },
                    },
                    OutroTitle = "EPILOGUE  ·  THE LAST SHIP OF MARLOW & CO.",
                    Outro = new[]
                    {
                        L(Ada, "You're home! You're all home!"),
                        L(Tomas, "The sea took care of us tonight."),
                        L(Harris, "Forty years at sea, and tonight I saw the Old One of the Deep."),
                        L(Finn, "And it helped us. Because we helped Mia, and Leo, and Tomas, and everyone."),
                        L(Ada, "Captain, Marlow and Company has a future now. And it needs a captain."),
                        L(Ada, "This ship is yours. Welcome to the family, Captain."),
                        L(Harris, "Captain. I like the sound of that."),
                        L(Finn, "Same time tomorrow, Captain?"),
                    },
                },
            },
        };
    }
}
