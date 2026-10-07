using UnityEngine;

/// <summary>
/// The sound of the sea, made as it plays so it never loops: a slow swell of surf rising and
/// falling every several seconds, small waves lapping at the hull, and (in harbour and fair
/// weather) gulls crying now and then, left or right. AudioService owns one for the whole game
/// and sets the mood: harbour, open sea, fog, storm.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SeaAmbience : MonoBehaviour
{
    /// <summary>Overall level: the mood's loudness times the player's volume setting.</summary>
    public float Level { get; set; }

    /// <summary>How rough the sea is, 0..1: calm harbour lapping up to storm surf.</summary>
    public float Surf { get; set; }

    /// <summary>How often gulls cry: 0 = never, 1 = a harbour full of them.</summary>
    public float Gulls { get; set; }

    private volatile float _level, _surf, _gulls;
    private float _levelNow, _surfNow;
    private int _rate;
    private System.Random _rng;

    // Surf: brown noise per ear (so it's wide), low-passed, on a slow swell.
    private float _brownL, _brownR, _lpL, _lpR;
    private double _swellPhase;
    private float _swellPeriod = 7f;

    // Lapping: short bursts of brighter noise.
    private float _lapEnv, _lapGain, _lapPan, _lapHp, _lapPrev;
    private int _lapWait;

    // Gulls: a falling "kee-ow" glide, a few in a row.
    private int _gullWait, _gullCalls, _gullSample, _gullLength;
    private double _gullPhase;
    private float _gullPan, _gullFrom, _gullTo;

    public static SeaAmbience Create(Transform parent)
    {
        var go = new GameObject("SeaAmbience");
        go.transform.SetParent(parent, false);
        var source = EngineSynth.Silence(go);
        var sea = go.AddComponent<SeaAmbience>();
        source.Play();
        return sea;
    }

    private void Awake()
    {
        _rate = AudioSettings.outputSampleRate;
        _rng = new System.Random(1717);
        _lapWait = _rate;
        _gullWait = _rate * 4;
    }

    private void Update()
    {
        _level = Mathf.Max(0f, Level);
        _surf = Mathf.Clamp01(Surf);
        _gulls = Mathf.Clamp01(Gulls);
    }

    private float Rand() => (float)_rng.NextDouble();

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (_rate <= 0) return;
        int frames = data.Length / channels;
        float gulls = _gulls;
        float dSurf = (_surf - _surfNow) / frames, dLevel = (_level - _levelNow) / frames;

        for (int i = 0; i < frames; i++)
        {
            _surfNow += dSurf;
            _levelNow += dLevel;

            // The swell: surf rises and falls; each swell is a little longer or shorter.
            _swellPhase += 1.0 / (_swellPeriod * _rate);
            if (_swellPhase >= 1.0)
            {
                _swellPhase -= 1.0;
                _swellPeriod = 5.5f + 4f * Rand();
            }
            float swell = 0.45f + 0.55f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin((float)(_swellPhase * 2.0 * Mathf.PI)), 1.6f);

            _brownL = Mathf.Clamp(_brownL * 0.997f + (Rand() * 2f - 1f) * 0.05f, -1f, 1f);
            _brownR = Mathf.Clamp(_brownR * 0.997f + (Rand() * 2f - 1f) * 0.05f, -1f, 1f);
            _lpL += (_brownL - _lpL) * 0.12f;
            _lpR += (_brownR - _lpR) * 0.12f;
            // Rougher seas: louder surf and a deeper swell.
            float rough = 0.6f + 0.9f * _surfNow;
            float surfL = _lpL * swell * 1.6f * rough, surfR = _lpR * swell * 1.6f * rough;

            // A wave laps at the hull now and then.
            if (--_lapWait <= 0)
            {
                _lapWait = (int)(_rate * (0.9f + 2.6f * Rand()));
                _lapEnv = 1f;
                _lapGain = (0.25f + 0.35f * Rand()) * (1f - 0.5f * _surfNow); // a storm drowns the small laps
                _lapPan = Rand();
            }
            float w = Rand() * 2f - 1f;
            _lapHp = 0.85f * (_lapHp + w - _lapPrev); // high-pass: a splashy hiss
            _lapPrev = w;
            _lapEnv *= 1f - 5f / _rate;
            float lap = _lapHp * _lapEnv * _lapGain * 0.35f;

            float left = surfL + lap * (1f - _lapPan);
            float right = surfR + lap * _lapPan;

            // Gulls.
            float gull = 0f;
            if (_gullLength > 0)
            {
                float t = _gullSample / (float)_gullLength;
                float f = Mathf.Lerp(_gullFrom, _gullTo, Mathf.Pow(t, 0.7f)) * (1f + 0.03f * Mathf.Sin(_gullSample * 2f * Mathf.PI * 28f / _rate));
                _gullPhase += f / _rate;
                float p = (float)(_gullPhase * 2.0 * Mathf.PI);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) * (t < 0.15f ? t / 0.15f : 1f);
                gull = (Mathf.Sin(p) + 0.45f * Mathf.Sin(2f * p) + 0.2f * Mathf.Sin(3f * p)) * env * 0.05f;
                if (++_gullSample >= _gullLength)
                {
                    _gullLength = 0;
                    if (--_gullCalls > 0) _gullWait = (int)(_rate * (0.12f + 0.25f * Rand()));
                    else _gullWait = gulls > 0f ? (int)(_rate * (6f + 22f * Rand()) / Mathf.Max(0.2f, gulls)) : _rate * 5;
                }
            }
            else if (--_gullWait <= 0)
            {
                if (gulls > 0f)
                {
                    if (_gullCalls <= 0)
                    {
                        _gullCalls = 2 + _rng.Next(3);
                        _gullPan = 0.15f + 0.7f * Rand();
                    }
                    _gullLength = (int)(_rate * (0.22f + 0.2f * Rand()));
                    _gullSample = 0;
                    _gullFrom = 1900f + 500f * Rand();
                    _gullTo = _gullFrom * (0.55f + 0.1f * Rand());
                }
                else _gullWait = _rate * 5;
            }
            left = (left + gull * (1f - _gullPan)) * _levelNow;
            right = (right + gull * _gullPan) * _levelNow;

            if (channels >= 2)
            {
                data[i * channels] = left;
                data[i * channels + 1] = right;
                for (int c = 2; c < channels; c++) data[i * channels + c] = 0f;
            }
            else data[i] = 0.5f * (left + right);
        }
    }
}
