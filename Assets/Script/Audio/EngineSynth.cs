using UnityEngine;

/// <summary>
/// A ship's diesel, made as it plays: a low chug (one thump per cylinder firing) over a deep
/// rumble, the firing rate and body note rising with the revs. Being generated, it never
/// loops audibly and follows the throttle smoothly. ShipAudio sets Revs and Gain every frame;
/// the sound is made on the audio thread in OnAudioFilterRead.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class EngineSynth : MonoBehaviour
{
    /// <summary>0 = idle, 1 = full revs.</summary>
    public float Revs { get; set; }

    /// <summary>Overall level, already including the player's volume setting.</summary>
    public float Gain { get; set; }

    private const float IdleFiring = 9f, FullFiring = 30f;     // thumps per second
    private const float IdleBody = 36f, FullBody = 68f;        // hull-resonance note, Hz

    private volatile float _revs, _gain;
    private float _revsNow, _gainNow;
    private double _firePhase, _bodyPhase;
    private float _env, _brown, _lowpass;
    private int _rate;
    private System.Random _rng;

    /// <summary>A child object that plays the engine; it starts silent.</summary>
    public static EngineSynth Create(Transform parent)
    {
        var go = new GameObject("EngineSynth");
        go.transform.SetParent(parent, false);
        var source = Silence(go);
        var synth = go.AddComponent<EngineSynth>();
        source.Play();
        return synth;
    }

    /// <summary>An AudioSource looping one second of silence: OnAudioFilterRead writes the sound into it.</summary>
    public static AudioSource Silence(GameObject go)
    {
        var source = go.AddComponent<AudioSource>();
        int rate = AudioSettings.outputSampleRate;
        source.clip = AudioClip.Create("Silence", rate, 1, rate, false);
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        return source;
    }

    private void Awake()
    {
        _rate = AudioSettings.outputSampleRate;
        _rng = new System.Random(4242);
    }

    private void Update()
    {
        _revs = Mathf.Clamp01(Revs);
        _gain = Mathf.Max(0f, Gain);
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_rate <= 0) return;
        float targetRevs = _revs, targetGain = _gain;
        int frames = data.Length / channels;
        // Ramp across the buffer, so changes never click.
        float dRevs = (targetRevs - _revsNow) / frames, dGain = (targetGain - _gainNow) / frames;

        for (int i = 0; i < frames; i++)
        {
            _revsNow += dRevs;
            _gainNow += dGain;
            float revs = _revsNow;

            // A cylinder fires: a thump that dies away quickly.
            _firePhase += Mathf.Lerp(IdleFiring, FullFiring, revs) / _rate;
            if (_firePhase >= 1.0)
            {
                _firePhase -= 1.0;
                _env = 0.75f + 0.25f * (float)_rng.NextDouble(); // no two thumps the same
            }
            _env *= 1f - Mathf.Lerp(32f, 55f, revs) / _rate;

            // The hull's low note, swelled by each thump.
            _bodyPhase += Mathf.Lerp(IdleBody, FullBody, revs) / _rate;
            if (_bodyPhase >= 1.0) _bodyPhase -= 1.0;
            float body = Mathf.Sin((float)(_bodyPhase * 2.0 * Mathf.PI));
            body += 0.35f * Mathf.Sin((float)(_bodyPhase * 4.0 * Mathf.PI)); // a little growl

            // A rumble under it all: brown noise, low-passed.
            float white = (float)_rng.NextDouble() * 2f - 1f;
            _brown = Mathf.Clamp(_brown * 0.995f + white * 0.06f, -1f, 1f);
            _lowpass += (_brown - _lowpass) * 0.08f;

            float s = body * (0.25f + 0.75f * _env) * 0.55f + _lowpass * (0.9f + 0.6f * revs);
            s = Tanh(s * 1.4f) * _gainNow;

            for (int c = 0; c < channels; c++) data[i * channels + c] = s;
        }
    }

    // A soft clip, so loud thumps round off instead of crackling.
    private static float Tanh(float x)
    {
        if (x > 3f) return 1f;
        if (x < -3f) return -1f;
        float x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }
}
