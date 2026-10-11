# SpeechCruiser — Voice Pipeline Setup

Set this up in **four stages**, testing after each one. Do not skip the staging — if you
wire everything at once and it doesn't work, you won't know which layer broke. Stage 1
alone gives you a fully playable game with no AI model running at all.

```
Stage 1   Commands + crew voice        no model needed        ~10 min
Stage 2   Whisper check                already configured     ~5 min
Stage 3   LLMUnity conversation        in-process llama.cpp   ~20 min
Stage 4   Tuning + RAM                                        ~5 min
```

---

## How the pipeline works

Four tiers. Each one only handles what the tier below it couldn't.

| Tier | What | Latency | Needs a model? |
|------|------|---------|----------------|
| **0** | `CommandParser` — phrase + phonetic matching | <1 ms | No |
| **1** | `IntentClassifier` — grammar-constrained enum | ~200 ms | Yes |
| **2** | `CrewBarkLibrary` / `CrewVoice` — authored lines | 0 ms | No |
| **3** | `LLMUnityChat` or `OllamaClient` — open conversation | 1–3 s | Yes |

Tier 0 resolves roughly 90–95% of orders and Tier 2 answers every one of them, so the
ship reacts on the same frame the transcription lands. Tiers 1 and 3 are optional
enhancements — with no model available at all, you lose only free-form conversation.

**The LLM cannot move the ship.** Only `CommandParser.Execute` touches `ShipNavigator`,
and the conversation prompts contain no action tags. This is structural, not a guardrail.

---

## Stage 1 — Commands and crew voice

### 1.1 Add `CrewVoice`

**This is the only genuinely required new component.** Without it the crew are silent —
every acknowledgement in the game routes through it.

1. Open `Assets/Scenes/Main.unity`
2. Select the **GameManager** object in the Hierarchy
3. **Add Component → Crew Voice**

Leave the defaults:

| Field | Value | Why |
|---|---|---|
| Radio Source | *(empty)* | Only needed for voice audio later |
| Use Voice Clips | ☐ off | Turn on when you have recorded lines |
| Log Barks | ☑ on | Shows every line in the Console |

### 1.2 Delete the dead `LLMManager` object

The Hierarchy has a **LLMManager** GameObject holding an obsolete script — it hardcodes
`phi3` and feeds raw JSON back into the command router. Nothing calls it any more, but
it will confuse you later. **Delete the GameObject.**

You can also delete `Assets/Script/LLMManager/LLMManager.cs`.

### 1.3 Test it

Press **Play**, hold **T**, say *"slow ahead"*, release.

Expected Console output:

```
[RADIO]: using microphone 'Microphone (USB Audio Device)'
[WHISPER]: ready (model loaded)
[RADIO]: 1.24s, rms 0.0421, peak 0.310
[WHISPER]: "Slow ahead."
[ROUTER]: "slow ahead" -> EngineSlow @ 1.00 | target ChiefEngineer
[ENGINE TELEGRAPH]: SLOW AHEAD (3 kn) -> Target: 3.0 kn
[BARK CHIEF HARRIS]: Slow ahead, aye. Over.
```

The ship should start moving and the subtitle should show Harris's line **instantly** —
no waiting. If you see that, Tier 0 and Tier 2 are working and the game is playable.

### 1.4 Try these

All of these work with no model running:

| Say | Expect |
|---|---|
| `all stop` | ALL STOP |
| `hard to port` | rudder hard left |
| `turn part` | rudder left *(mishear handled)* |
| `star board` | rudder right |
| `frank speed` | FLANK |
| `full ahead and hard to port` | both, one engine + one helm order |
| `how far` | Finn reports hazard distance |
| `fuel status` | Harris reports real fuel % |
| `press T, say nothing` | `discarded (silence: rms ...)` — Whisper never called |

Press **Tab** (or **1** / **2**) to change radio channel. Saying `"Finn"` or `"Chief"`
retunes it automatically.

---

## Stage 2 — Confirm Whisper

Already configured in the scene, but verify these on the **WhisperManager** component:

| Field | Value | Why |
|---|---|---|
| Model Path | `Whisper/ggml-base.en.bin` | |
| Use Gpu | ☑ | |
| Audio Ctx | **512** | Biggest latency win — without it the encoder pads every clip to 30 s |
| Single Segment | ☑ | Stops multi-segment hallucination |
| Initial Prompt | *(nautical vocabulary)* | Biases decoding toward marine terms |

And on **RadioMic**:

| Field | Value |
|---|---|
| Max Record Duration | **8** |
| Min Speech Seconds | 0.35 |
| Silence Rms Threshold | 0.006 |

> **`Audio Ctx` and `Max Record Duration` are coupled.** 512 covers about 10 seconds of
> audio. If you raise the recording limit past that, raise `audioCtx` too (768 ≈ 15 s) or
> the tail of long transmissions is silently dropped.

**Check the GPU actually engaged.** Search the Console for the whisper init lines. Your
project ships `ggml-vulkan.dll` (20 MB) so a Vulkan backend is available, but
whisper.unity falls back to CPU *silently* if it can't initialise. If it's on CPU, that's
your remaining latency and no amount of tuning elsewhere will fix it.

If quiet speech gets dropped, lower `Silence Rms Threshold` to `0.003`. If you get
phantom transcriptions from room noise, raise it to `0.01`. The `rms` value printed on
every transmission tells you where to set it.

---

## Stage 3 — LLMUnity conversation

LLMUnity bundles llama.cpp with your build — no install step for players, no 794 MB
Ollama host process.

> **It is not in-process.** LLMUnity v3 spawns `llamalib_win-x64_server.exe` as a child
> process and talks to it locally. You can see the exact command in `Logs/Editor.log`:
> `Deploy server command: llamalib_win-x64_server.exe -m <model>.gguf -t -1 -np 2 -c 8192 -b 512`
> The advantage over Ollama is that it ships with the game and manages its own lifecycle,
> not that it avoids a separate process.

### 3.0 Do Stage 1 first

Add `CrewVoice`, confirm commands and barks work with no model at all, and **save the
scene**. If you jump straight here and something goes wrong, you won't know whether the
problem is the model, the package, or the wiring.

### 3.1 Let the package resolve

`Packages/manifest.json` already contains:

```json
"ai.undream.llm": "https://github.com/undreamai/LLMUnity.git#v3.0.3"
```

Open Unity and wait. Confirm in **Window → Package Manager → In Project** that
**LLM for Unity** appears. First import downloads LlamaLib (the llama.cpp backend), which
takes a few minutes.

> LlamaLib extracts to `StreamingAssets/.../runtimes/`, *not* `Assets/Plugins/`, so it
> does **not** collide with whisper.unity's `Plugins/Windows/ggml.dll`. This was checked.

### 3.2 Add the scripting define — then save the project immediately

**Project Settings → Player → Other Settings → Scripting Define Symbols**, add:

```
LLMUNITY
```

Three things matter here, in order:

1. **Press Enter (or Tab) after typing it.** If you click away without committing the
   field, Unity silently discards it.
2. **Wait for the recompile to finish.**
3. **File → Save Project.** ← do not skip this

Step 3 writes `ProjectSettings.asset` to disk. Until you do, the define lives only in the
Editor's memory, and a crash loses it. That is exactly what happened on the first attempt
here: the define was lost, `LLMUnityChat` stopped compiling, and the component that had
been added to GameManager became `The referenced script (Unknown) on this Behaviour is
missing!`

Verify it stuck — this should print `LLMUNITY`:

```bash
grep -A3 scriptingDefineSymbols ProjectSettings/ProjectSettings.asset
```

The component is now written so that a missing define only makes it **inert with a clear
warning**, never a missing script. But you still need the define for it to do anything.

If Unity reports errors *inside* `LLMUnityChat.cs` after adding the define, the package
hasn't finished importing — wait, then **Assets → Reimport All**.

### 3.3 Add the `LLM` engine and a model

1. Create a new empty GameObject, name it **LLMEngine**
2. **Add Component → LLM**
3. On that component, either:
   - click **Download model** and pick from the list, or
   - click **Load model** and browse to a `.gguf` you already have

**Model choice:** a **1–2B instruct model at Q4_K_M** is right — around 0.8–1.1 GB.
Good options: `Qwen2.5-1.5B-Instruct`, `Llama-3.2-1B-Instruct`.

Do not reach for something bigger. Replies are capped at 25 words, the model is never in
the control path, and on a 15.7 GB machine sharing RAM with the Editor a larger model
costs you more than it gives.

> **Check what you actually downloaded.** Models land in
> `%APPDATA%\LLMUnity\models\`. A `Qwen3.5-4B-Q4_K_M.gguf` is **2.55 GB** — roughly triple
> the recommendation, and with the default `-c 8192 -np 2` the KV cache is sized for two
> concurrent slots you will never use. Delete it and fetch a 1–2B instead.

### 3.3b Trim the context and slots

On the `LLM` component, before you run it:

| Field | Default | Set to | Why |
|---|---|---|---|
| Context Size | 8192 | **2048** | Your prompts are ~200 tokens; 8192 allocates KV cache you never touch |
| Parallel Prompts / slots | 2 | **1** | One player, one conversation at a time |

These two alone can be the difference between stable and thrashing on 15.7 GB.

Also check which native library loaded — `Logs/Editor.log` will show a line like
`Trying .../LlamaLib-v2.0.5/win-x64/native/llamalib_win-x64_avx2.dll`. An `avx2` build
means it is running on **CPU**. That's fine for a 1B at this reply length, but if you want
GPU, look for the CUDA/Vulkan variant and enable GPU layers on the `LLM` component.

### 3.4 Add two `LLMAgent` components

Each crew member needs his own agent so each keeps a separate conversation history
(handled inside LlamaLib — you don't manage transcripts).

On the **LLMEngine** object, **Add Component → LLM Agent** twice. For each:

| Field | Value |
|---|---|
| LLM | → the `LLM` component on this object |
| Temperature | **0.6** |
| Num Predict | **60** |
| Top P | 0.9 |
| Repeat Penalty | 1.1 |

Do **not** fill in the system prompt — `LLMUnityChat` sets it at startup from
`CrewPersona`, so both backends produce identical characterisation.

### 3.5 Add `LLMUnityChat`

Select **GameManager** → **Add Component → LLM Unity Chat**:

| Field | Value |
|---|---|
| Harris Agent | → first `LLMAgent` |
| Finn Agent | → second `LLMAgent` |
| Llm | → the `LLM` component |
| Warm Up On Start | ☑ |
| Stream Reply | ☑ — text appears as it generates |
| Priority | 10 *(beats Ollama's 0)* |

### 3.6 Test

Press **Play** and wait for:

```
[LLMUNITY]: warmed up in 2.4s.
[LLMUNITY]: crew conversation ready (in-process).
[CREW CHAT]: conversation backend is now LLMUnity (in-process llama.cpp).
```

Now hold **T** and ask something conversational — *"Finn, how long have you been at sea?"*

```
[ROUTER]: "finn how long have you been at sea" -> no match | target Lookout
[LOOKOUT FINN 1840ms]: Three years, Sir, since I was sixteen. Over.
```

The key line is `-> no match`. Tier 0 correctly declined to treat it as an order and
passed it to conversation. Commands still bypass this path entirely.

---

## Stage 4 — Tuning and RAM

### If you are dropping Ollama entirely

Tier 1 (`IntentClassifier`) still talks to Ollama over HTTP. Two options:

**a) Turn off the classifier.** On **CommandRouter**, uncheck **Use Classifier Fallback**.
Tier 0 handles 90–95% and the remainder falls through to conversation. Simplest.

**b) Port it to GBNF.** `LLMClient` exposes `SetGrammar()` / `LoadGrammar()`, so the
action enum can be grammar-constrained in-process — strictly better than the JSON schema
Ollama uses. Not written yet; ask and it can be done.

Then remove `OllamaClient`, `OllamaLauncher`, and `IntentClassifier` from GameManager.

### If you are keeping Ollama as well

Add **OllamaLauncher** to GameManager. It probes port 11434, starts `ollama serve` if
nothing is listening, applies `OLLAMA_MAX_LOADED_MODELS=1` / `NUM_PARALLEL=1` /
`KEEP_ALIVE=30m` to the process it spawns, and warms the model.

`Stop Policy` defaults to **OnlyInBuilds** — leaves the server up when you exit Play so
the model stays warm between iterations, shuts it down in a shipped build. It never
touches an Ollama you were already running.

Set `IntentClassifier.model` to `gemma3:1b` after `ollama pull gemma3:1b` — 815 MB
instead of 3.3 GB for a job that picks one enum value.

Both backends can coexist; `LLMUnityChat` wins on priority. Disable its component to fall
back to Ollama without deleting anything.

### RAM checklist

Your machine is **15.7 GB** and Memory Compression was already at ~1 GB before the game
ran. Going in-process with a 1B model instead of Ollama + gemma3:4b should return roughly
**3 GB** during play.

- [ ] Quit the Ollama tray app if unused — measured at **794 MB with zero models loaded**
- [ ] Use a 1–2B GGUF, not a 4B
- [ ] Install **Window → Package Manager → Memory Profiler**, snapshot mid-playtest, check the **AudioClip** category stays flat across many `T` presses

That last one verifies the mic leak fix. It used to grow ~1–2 MB per press forever.

---

## CommandRouter options

| Field | Default | Effect |
|---|---|---|
| Auto Relay Machinery To Engineer | ☑ | Engine order given to Finn is passed to Harris. Uncheck for strict roles — Finn refuses. |
| Use Classifier Fallback | ☑ | Ask Tier 1 when Tier 0 is unsure |
| Use Conversation Fallback | ☑ | Send non-orders to Tier 3 |
| Log Routing | ☑ | Print every routing decision |

---

## Adding new commands

**Never add wordings to a prompt.** Edit `CommandVocabulary.Entries`:

```csharp
new Entry(ShipAction.EngineStop,
    "stop", "all stop", "halt", "cut engine",
    "your new phrasing here"),
```

Longer phrases beat shorter ones, so `"hard to port"` wins over `"port"` automatically.
Aliases match exactly and cost nothing; the phonetic matcher catches variants you didn't
list. Adding a value to `ShipAction` automatically extends the Tier 1 classifier's schema
too — the enum is the single source of truth.

Regression suite: 46 cases covering mishears, filler, compound orders, and near-miss
words that must *not* match (`"sort the cargo"` must never read as `"port"`).

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| Crew completely silent, ship still moves | `CrewVoice` not added to GameManager |
| Nothing happens on T | Check `[RADIO]: using microphone` names the right device; set `Selected Device Name` on RadioMic |
| `discarded (too short)` | Hold T longer, or lower `Min Speech Seconds` |
| `discarded (silence: rms ...)` on real speech | Lower `Silence Rms Threshold` toward 0.003 |
| Commands work, conversation doesn't | No backend registered — check for `[CREW CHAT]: conversation backend is now ...` |
| `LLMUnityChat.cs` errors | Package not finished importing; **Assets → Reimport All** |
| `The referenced script (Unknown) on this Behaviour is missing!` | The `LLMUNITY` define was lost — see 3.2, and **File → Save Project** after setting it. Recover by reopening `Main.unity` from disk and choosing **Don't Save**. |
| `[LLMUNITY]: the LLMUNITY scripting define is not set` | Expected when the define is missing. The component is inert, not broken. Set the define and save the project. |
| `LLMUNITY` define added but nothing happens | `Harris Agent` / `Finn Agent` unassigned — check Console for the error |
| Editor unstable / crashes after adding the LLM | Model too large, or context 8192 × 2 slots. Use a 1–2B, context 2048, 1 slot. |
| Everything slow, GPU idle | Whisper fell back to CPU; check the init lines |
| Long orders get cut off | `Max Record Duration` exceeds what `Audio Ctx` covers |
| Wrong crew answers a query | Expected — `"report"` means hazards to Finn, engine status to Harris |

---

## Files

```
Assets/Script/
├── Command/
│   ├── ShipAction.cs           closed action enum + categories
│   ├── Phonetics.cs            consonant skeleton + Levenshtein
│   ├── CommandVocabulary.cs    ← add new command wordings HERE
│   ├── CommandParser.cs        Tier 0
│   └── ShipTelemetry.cs        rounded state snapshot
├── Crew/
│   ├── CrewBarkLibrary.cs      ← add new crew lines HERE
│   └── CrewVoice.cs            Tier 2 playback  [REQUIRED COMPONENT]
├── LLMManager/
│   ├── ICrewChat.cs            backend interface + registry
│   ├── CrewPersona.cs          shared personas (both backends)
│   ├── IntentClassifier.cs     Tier 1
│   ├── LLMUnityChat.cs         Tier 3 in-process   [#if LLMUNITY]
│   ├── OllamaClient.cs         Tier 3 over HTTP
│   ├── OllamaLauncher.cs       auto-start + availability
│   └── OllamaTypes.cs          JSON helpers
├── RadioMic.cs                 push-to-talk + silence gates
├── CommandRouter.cs            tier orchestration
└── CrewAlertSystem.cs          unprompted warnings (barks)
```

Recorded voice lines, when you have them:

```
Assets/Resources/CrewVoice/<Crew>/<Action>_<n>.wav
e.g. Assets/Resources/CrewVoice/ChiefEngineer/EngineSlow_0.wav
```

Then tick **Use Voice Clips** on `CrewVoice` and assign an `AudioSource`. Missing clips
are cached as absent, so text-only costs nothing.
