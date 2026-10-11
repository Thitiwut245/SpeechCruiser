using UnityEngine;

/// <summary>
/// The menus' transition sounds, synthesised once in code like the rest of the game's audio:
/// a soft whoosh for the ink wipe between pages, and a deep swell under SET SAIL.
/// </summary>
public static class UiSounds
{
    private static AudioClip _whoosh, _swell;

    /// <summary>Filtered noise rising and falling, its brightness sweeping up then down: a sheet passing.</summary>
    public static AudioClip Whoosh => _whoosh ?? (_whoosh = Noise("Whoosh", 0.5f, 300f, 2400f, 0.5f, 7));

    /// <summary>A longer, darker rush with a low hum under it, for setting sail.</summary>
    public static AudioClip Swell => _swell ?? (_swell = Noise("Swell", 1.4f, 120f, 900f, 0.8f, 11, hum: 55f));

    public static void Play(AudioClip clip, float volume)
    {
        var audio = AudioService.Instance;
        if (audio != null && clip != null) audio.Play(clip, AudioBus.Effects, volume);
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
