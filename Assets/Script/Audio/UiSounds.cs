using UnityEngine;

/// <summary>
/// The menus' transition sounds, synthesised once in code like the rest of the game's audio:
/// a soft whoosh for the ink wipe between pages, a deep swell under SET SAIL, and the chime
/// of a story coin picked up.
/// </summary>
public static class UiSounds
{
    private static AudioClip _whoosh, _swell;
    private static readonly AudioClip[] _coins = new AudioClip[6];

    /// <summary>
    /// A bright two-note chime for a coin; each coin of a run (<paramref name="run"/> 1, 2, ...)
    /// rings a step higher, up to the sixth, so a good run climbs.
    /// </summary>
    public static AudioClip Coin(int run)
    {
        int step = Mathf.Clamp(run - 1, 0, _coins.Length - 1);
        return _coins[step] ?? (_coins[step] = Chime("Coin " + step, 1318.5f * Mathf.Pow(2f, step * 2f / 12f)));
    }

    /// <summary>Filtered noise rising and falling, its brightness sweeping up then down: a sheet passing.</summary>
    public static AudioClip Whoosh => _whoosh ?? (_whoosh = Noise("Whoosh", 0.5f, 300f, 2400f, 0.5f, 7));

    /// <summary>A longer, darker rush with a low hum under it, for setting sail.</summary>
    public static AudioClip Swell => _swell ?? (_swell = Noise("Swell", 1.4f, 120f, 900f, 0.8f, 11, hum: 55f));

    public static void Play(AudioClip clip, float volume)
    {
        var audio = AudioService.Instance;
        if (audio != null && clip != null) audio.Play(clip, AudioBus.Effects, volume);
    }

    // A short note, then one a fifth above it, each a sine with a soft octave and a bell's quick decay.
    private static AudioClip Chime(string name, float hz)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(0.32f * rate), second = Mathf.CeilToInt(0.07f * rate);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float s = Mathf.Sin(2f * Mathf.PI * hz * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * hz * t);
            float env = Mathf.Exp(-t * 18f) * Mathf.Clamp01(t * 800f);
            if (i >= second)
            {
                float u = (i - second) / (float)rate;
                s = s * env + (Mathf.Sin(2f * Mathf.PI * hz * 1.5f * u) + 0.25f * Mathf.Sin(6f * Mathf.PI * hz * u)) * Mathf.Exp(-u * 11f) * Mathf.Clamp01(u * 800f);
                env = 1f;
            }
            data[i] = Mathf.Clamp(s * env * 0.4f, -1f, 1f);
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip Noise(string name, float seconds, float lowHz, float highHz, float gain, int seed, float hum = 0f)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(seconds * rate);
        var data = new float[n];
        var rng = new System.Random(seed);
        float low = 0f, band = 0f;
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)n;
            // Envelope: quick rise, longer fall.
            float env = k < 0.35f ? Mathf.SmoothStep(0f, 1f, k / 0.35f) : Mathf.SmoothStep(1f, 0f, (k - 0.35f) / 0.65f);
            // Brightness sweeps up to the peak and back down: a one-pole low-pass on white noise,
            // minus a slower one, which leaves a band that moves.
            float cutoff = Mathf.Lerp(lowHz, highHz, Mathf.Sin(k * Mathf.PI));
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / rate);
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += a * (white - low);
            band += 0.02f * (low - band);
            float s = (low - band) * 2.2f;
            if (hum > 0f) s += 0.35f * Mathf.Sin(2f * Mathf.PI * hum * i / rate) * env;
            data[i] = Mathf.Clamp(s * env * gain, -1f, 1f);
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
