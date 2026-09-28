"""
Synthesises every sound effect and music loop SpeechCruiser uses, as 44.1 kHz mono WAVs in
Assets/Audio. Nothing is downloaded: every sound is built from sine waves and filtered noise,
so the files are ours to ship. Loops (engine, wash, static, music) are rendered circularly so
they repeat without a click.

Usage:  python Tools/Audio/generate_audio.py
Needs:  numpy

Replace any file with a recorded sound of the same name and Unity picks it up; the
SfxLibrary asset references the files, not this script.
"""
import os
import wave

import numpy as np

SR = 44100
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Audio"))
SFX = os.path.join(ROOT, "Sfx")
MUSIC = os.path.join(ROOT, "Music")
rng = np.random.default_rng(1805)


# ---------------------------------------------------------------- helpers

def secs(d):
    return np.arange(int(round(SR * d))) / SR


def silence(d):
    return np.zeros(int(round(SR * d)))


def save(folder, name, x, peak=0.89):
    x = np.asarray(x, dtype=float)
    m = np.max(np.abs(x))
    if m > 0:
        x = x / m * peak
    data = (np.clip(x, -1.0, 1.0) * 32767).astype("<i2")
    os.makedirs(folder, exist_ok=True)
    with wave.open(os.path.join(folder, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print("  %-22s %5.2fs" % (name, len(x) / SR))


def spectral(x, lo=None, hi=None, tilt=0.0, order=4):
    """Circular FFT filter: zero-phase high/low pass plus a spectral tilt (-0.5 = pink)."""
    X = np.fft.rfft(x)
    f = np.maximum(np.fft.rfftfreq(len(x), 1.0 / SR), 1e-3)
    g = np.ones_like(f)
    if lo:
        g /= np.sqrt(1.0 + (lo / f) ** (2 * order))
    if hi:
        g /= np.sqrt(1.0 + (f / hi) ** (2 * order))
    if tilt:
        g *= (np.maximum(f, 20.0) / 1000.0) ** tilt
    return np.fft.irfft(X * g, len(x))


def filt(x, lo=None, hi=None, tilt=0.0, order=4):
    """Linear (non-wrapping) version for one-shots: pads so the filter tail doesn't wrap."""
    pad = int(0.25 * SR)
    y = spectral(np.concatenate([x, np.zeros(pad)]), lo, hi, tilt, order)
    return y[: len(x)]


def noise(d):
    return rng.standard_normal(int(round(SR * d)))


def decay(d, tau, attack=0.002):
    t = secs(d)
    env = np.exp(-t / tau)
    if attack > 0:
        env *= np.clip(t / attack, 0, 1)
    return env


def fade(x, fin=0.005, fout=0.02):
    n_in, n_out = int(SR * fin), int(SR * fout)
    y = x.copy()
    if n_in:
        y[:n_in] *= np.linspace(0, 1, n_in)
    if n_out:
        y[-n_out:] *= np.linspace(1, 0, n_out)
    return y


def place(buf, x, at, wrap=False):
    """Mix x into buf starting at `at` seconds. With wrap, overflow continues at the start."""
    i = int(round(at * SR))
    if wrap:
        idx = (np.arange(len(x)) + i) % len(buf)
        np.add.at(buf, idx, x)
    else:
        end = min(len(buf), i + len(x))
        if end > i:
            buf[i:end] += x[: end - i]
    return buf


def tone(freq, d, harmonics=(1.0,), phase=0.0):
    t = secs(d)
    y = np.zeros_like(t)
    for k, a in enumerate(harmonics, start=1):
        y += a * np.sin(2 * np.pi * freq * k * t + phase * k)
    return y


def sweep(f0, f1, d, harmonics=(1.0,), curve="exp"):
    t = secs(d)
    if curve == "exp":
        f = f0 * (f1 / f0) ** (t / d)
    else:
        f = f0 + (f1 - f0) * t / d
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = np.zeros_like(t)
    for k, a in enumerate(harmonics, start=1):
        y += a * np.sin(ph * k)
    return y


def reverb(x, t60=1.2, mix=0.22, circular=False, tone_hi=5000):
    """Convolution with a decaying-noise impulse response. Circular keeps loops seamless."""
    n_ir = int(SR * t60)
    ir = rng.standard_normal(n_ir) * np.exp(-6.9 * np.arange(n_ir) / n_ir)
    ir = filt(ir, lo=200, hi=tone_hi)
    ir[: int(0.012 * SR)] = 0.0  # pre-delay
    ir /= np.sqrt(np.sum(ir ** 2))
    if circular:
        n = len(x)
        irc = np.zeros(n)
        irc[: min(n, n_ir)] = ir[: min(n, n_ir)]
        wet = np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(irc), n)
        return x + mix * wet
    n = len(x) + n_ir
    wet = np.fft.irfft(np.fft.rfft(x, n) * np.fft.rfft(ir, n), n)
    out = np.concatenate([x, np.zeros(n_ir)]) + mix * wet
    return out


def trim_tail(x, threshold=0.0008):
    m = np.max(np.abs(x)) or 1.0
    idx = np.where(np.abs(x) > threshold * m)[0]
    end = (idx[-1] + int(0.02 * SR)) if len(idx) else len(x)
    return fade(x[: min(end, len(x))], 0.0, 0.03)


SQUARE = (1.0, 0.0, 1 / 3, 0.0, 1 / 5, 0.0, 1 / 7)
SAW = tuple(1.0 / k for k in range(1, 13))


def radio_band(x, drive=1.6):
    """Walkie-talkie colour: narrow band and a little clipping."""
    y = filt(x, lo=380, hi=3100)
    y = np.tanh(drive * y / (np.max(np.abs(y)) or 1.0))
    return y


# ---------------------------------------------------------------- radio

def radio_sounds():
    print("radio")
    # Key up: a click and a short burst of squelch.
    click = np.zeros(int(0.14 * SR))
    click[:120] = np.sin(2 * np.pi * 2200 * secs(0.14)[:120]) * np.exp(-np.arange(120) / 25)
    burst = filt(noise(0.14), lo=900, hi=4200) * decay(0.14, 0.035, attack=0.004) * 0.7
    save(SFX, "radio_key_on", click + burst, peak=0.7)

    # Key down: the classic "kssh" squelch tail.
    d = 0.34
    env = np.minimum(1.0, secs(d) / 0.004) * np.where(secs(d) < 0.05, 1.0, np.exp(-(secs(d) - 0.05) / 0.07))
    tail = filt(noise(d), lo=500, hi=5200) * env
    tail[:90] += np.sin(2 * np.pi * 1800 * secs(d)[:90]) * np.exp(-np.arange(90) / 20) * 2
    save(SFX, "radio_key_off", fade(tail, 0, 0.03), peak=0.75)

    # Roger beep at the end of every crew line.
    b = np.concatenate([fade(tone(1750, 0.075), 0.003, 0.006), fade(tone(1320, 0.085), 0.003, 0.01)])
    b += filt(noise(len(b) / SR), lo=800, hi=3500) * 0.05
    b = fade(b, 0.002, 0.01)
    save(SFX, "radio_roger", b, peak=0.55)

    # Static bed under a crew line (loops).
    d = 2.0
    bed = spectral(noise(d), lo=400, hi=3600)
    crackle = np.zeros(int(d * SR))
    for _ in range(26):
        i = rng.integers(0, len(crackle))
        n = rng.integers(40, 260)
        idx = (np.arange(n) + i) % len(crackle)
        crackle[idx] += rng.standard_normal(n) * np.exp(-np.arange(n) / (n / 4)) * rng.uniform(1.5, 4)
    bed = bed + spectral(crackle, lo=900, hi=6000)
    save(SFX, "radio_static_loop", bed, peak=0.6)

    # Radio busy / refused.
    on = fade(tone(480, 0.22) + tone(620, 0.22), 0.004, 0.01)
    busy = np.concatenate([on, silence(0.16), on, silence(0.05)])
    save(SFX, "radio_busy", radio_band(busy, 1.2), peak=0.6)

    # Didn't catch that: two falling tones.
    e = np.concatenate([fade(tone(520, 0.12, SQUARE), 0.004, 0.02), silence(0.03), fade(tone(349, 0.2, SQUARE), 0.004, 0.06)])
    save(SFX, "radio_error", filt(e, hi=2500), peak=0.55)


VOWELS = {
    "a": (730, 1090, 2440),
    "e": (530, 1840, 2480),
    "i": (300, 2200, 2950),
    "o": (570, 840, 2410),
    "u": (320, 870, 2240),
    "ae": (660, 1720, 2410),
    "er": (490, 1350, 1690),
    "uh": (640, 1190, 2390),
}


def syllable(vowel, f0, d, consonant=None):
    """One muffled radio syllable: harmonics shaped by three formants, then radio band."""
    t = secs(d)
    f1, f2, f3 = VOWELS[vowel]
    contour = f0 * (1.0 + 0.06 * np.sin(np.pi * t / d) - 0.05 * t / d)
    ph = 2 * np.pi * np.cumsum(contour) / SR
    y = np.zeros_like(t)
    for k in range(1, 40):
        fk = f0 * k
        if fk > 4200:
            break
        a = 0.0
        for fc, bw, g in ((f1, 90, 1.0), (f2, 120, 0.55), (f3, 160, 0.3)):
            a += g / (1.0 + ((fk - fc) / bw) ** 2)
        y += a * np.sin(ph * k) / (k ** 0.3)
    env = np.minimum(1.0, t / 0.018) * np.minimum(1.0, (d - t) / 0.05)
    y *= np.clip(env, 0, 1)
    if consonant == "s":
        c = filt(noise(0.05), lo=3000, hi=7000) * decay(0.05, 0.02, 0.004) * 0.35
        y = np.concatenate([c, y])
    elif consonant == "t":
        c = filt(noise(0.02), lo=1500, hi=5000) * decay(0.02, 0.006) * 0.6
        y = np.concatenate([c, silence(0.01), y])
    elif consonant == "m":
        m = tone(f0, 0.04, (1, 0.3)) * np.linspace(0.2, 0.6, int(0.04 * SR))
        y = np.concatenate([filt(m, hi=500), y])
    return fade(radio_band(y + noise(len(y) / SR) * 0.015, 1.8), 0.002, 0.012)


def voice_sounds():
    print("voice")
    plan = [("a", None, 0.15), ("e", "t", 0.13), ("o", None, 0.17), ("i", "s", 0.12),
            ("uh", "m", 0.14), ("ae", None, 0.16), ("er", "t", 0.15), ("u", None, 0.13)]
    for i, (v, c, d) in enumerate(plan):
        save(SFX, "voice_syllable_%d" % i, syllable(v, 140, d, c), peak=0.7)


# ---------------------------------------------------------------- ship

def bell_strike(f0, d=1.6):
    ratios = (1.0, 2.0, 2.76, 5.40, 8.93)
    amps = (1.0, 0.35, 0.55, 0.22, 0.1)
    taus = (1.1, 0.8, 0.55, 0.3, 0.15)
    t = secs(d)
    y = np.zeros_like(t)
    for r, a, tau in zip(ratios, amps, taus):
        y += a * np.sin(2 * np.pi * f0 * r * t) * np.exp(-t / tau)
    y += 0.5 * np.sin(2 * np.pi * (f0 + 2.5) * t) * np.exp(-t / 1.1)  # beating
    return y * np.minimum(1.0, t / 0.0015)


def ship_sounds():
    print("ship")
    # Engine-room telegraph: lever clack, then the double ring.
    out = silence(2.0)
    place(out, filt(noise(0.03), lo=300, hi=3000) * decay(0.03, 0.006) * 0.8, 0.0)
    place(out, tone(160, 0.08) * decay(0.08, 0.02) * 0.8, 0.0)
    place(out, bell_strike(1180), 0.06)
    place(out, bell_strike(1180) * 0.85, 0.32)
    save(SFX, "telegraph_bell", trim_tail(reverb(out, 1.0, 0.18)), peak=0.8)

    # Diesel engine loop: everything is periodic in 2 s, so it loops cleanly.
    d = 2.0
    t = secs(d)
    y = np.zeros_like(t)
    base = 15.0  # half the firing rate: uneven cylinders give the rumble character
    for k in range(1, 70):
        f = base * k
        a = 1.0 / (1.0 + (f / 320.0) ** 2) * rng.uniform(0.5, 1.0)
        if f < 90:
            a *= 0.5  # laptop speakers can't play the lowest rumble anyway
        if k % 2 == 0:
            a *= 1.8
        y += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 2 * np.pi))
    y += 0.35 * spectral(noise(d), hi=350) * np.std(y) / 1.0
    clatter = spectral(noise(d), lo=1200, hi=3500) * (0.5 + 0.5 * np.sin(2 * np.pi * 30 * t)) ** 4
    y += clatter * np.std(y) * 0.3
    save(SFX, "engine_loop", y, peak=0.8)

    # Bow wash loop, 4 s.
    d = 4.0
    t = secs(d)
    wash = spectral(noise(d), lo=150, hi=2400, tilt=-0.4)
    wash *= 0.75 + 0.2 * np.sin(2 * np.pi * 0.5 * t) + 0.1 * np.sin(2 * np.pi * 1.25 * t + 1.0)
    fizz = spectral(noise(d), lo=3000, hi=8000) * (0.6 + 0.4 * np.sin(2 * np.pi * 0.25 * t + 2.0))
    save(SFX, "wash_loop", wash + fizz * 0.25, peak=0.75)

    # Hull strike: thud, crunch, metal groan, slosh.
    d = 1.3
    out = silence(d)
    place(out, sweep(140, 55, 0.35, (1, 0.5, 0.25)) * decay(0.35, 0.16) * 0.6, 0.0)
    crunch = filt(noise(0.4), lo=200, hi=2600)
    grains = np.zeros(len(crunch))
    for _ in range(30):
        i = int(rng.integers(0, len(grains) - 400))
        grains[i:i + 400] += np.exp(-np.arange(400) / 60) * rng.uniform(0.3, 1.0)
    place(out, crunch * (grains + 0.3) * decay(0.4, 0.14) * 1.6, 0.005)
    groan = sum(np.sin(2 * np.pi * np.cumsum(f * np.linspace(1.0, 0.94, int(0.9 * SR))) / SR) * a
                for f, a in ((182, 0.5), (268, 0.35), (411, 0.2)))
    place(out, groan * decay(0.9, 0.35, 0.03) * 0.6, 0.05)
    place(out, filt(noise(0.9), hi=800) * decay(0.9, 0.3, 0.05) * 0.35, 0.1)
    save(SFX, "hull_crunch", trim_tail(reverb(out, 0.8, 0.15)), peak=0.9)

    # Mine: crack, boom, rumble, falling spray.
    d = 2.8
    out = silence(d)
    place(out, noise(0.03) * decay(0.03, 0.01), 0.0)
    place(out, filt(noise(2.0), hi=180, order=3) * decay(2.0, 0.6, 0.004) * 1.8, 0.0)
    place(out, sweep(58, 28, 1.4) * decay(1.4, 0.5, 0.004) * 0.9, 0.0)
    place(out, filt(noise(2.4), lo=120, hi=1100) * decay(2.4, 0.9, 0.02) * 1.3, 0.02)
    spray = filt(noise(2.0), lo=1000, hi=6000) * np.minimum(1, secs(2.0) / 0.3) * np.exp(-secs(2.0) / 0.55)
    place(out, spray * 0.35, 0.25)
    out = np.tanh(out / np.max(np.abs(out)) * 2.2)
    save(SFX, "mine_explosion", trim_tail(reverb(out, 1.6, 0.25)), peak=0.95)

    # Containers going overboard.
    d = 1.1
    out = silence(d)
    place(out, filt(noise(0.8), lo=120, hi=1600) * decay(0.8, 0.22, 0.008), 0.0)
    for _ in range(9):
        f0 = rng.uniform(380, 700)
        b = sweep(f0, f0 * rng.uniform(1.4, 2.0), 0.035) * decay(0.035, 0.012, 0.002)
        place(out, b * rng.uniform(0.1, 0.25), rng.uniform(0.12, 0.7))
    save(SFX, "cargo_splash", trim_tail(out), peak=0.75)

    # General alarm: two rising whoops.
    w = sweep(380, 920, 0.55, SAW[:8]) * np.minimum(1, secs(0.55) / 0.02) * np.minimum(1, (0.55 - secs(0.55)) / 0.05)
    klax = np.concatenate([w, silence(0.08), w, silence(0.1)])
    save(SFX, "klaxon", np.tanh(1.5 * filt(klax, lo=250, hi=4500)), peak=0.6)

    # Ship's horn: a low fifth, like a real cargo horn.
    d = 3.0
    t = secs(d)
    vib = 1.0 + 0.003 * np.sin(2 * np.pi * 5.0 * t)
    horn = np.zeros_like(t)
    for f0, g in ((98.0, 1.0), (146.8, 0.7)):
        ph = 2 * np.pi * np.cumsum(f0 * vib) / SR
        for k in range(1, 30):
            horn += g * np.sin(ph * k) / k ** 0.8
    horn = filt(horn, hi=1100, order=2)
    env = np.minimum(1, t / 0.18) * np.where(t < 2.2, 1.0, np.exp(-(t - 2.2) / 0.18))
    horn = horn * env + filt(noise(d), lo=200, hi=900) * env * 0.02 * np.max(np.abs(horn))
    save(SFX, "ship_horn", trim_tail(reverb(horn, 2.2, 0.35, tone_hi=2500)), peak=0.85)


# ---------------------------------------------------------------- alerts & UI

def alert_sounds():
    print("alerts / ui")
    n1 = tone(880, 0.5, (1, 0.25)) * decay(0.5, 0.18, 0.003)
    n2 = tone(1175, 0.7, (1, 0.25)) * decay(0.7, 0.28, 0.003)
    out = silence(1.0)
    place(out, n1, 0.0)
    place(out, n2, 0.16)
    save(SFX, "alert_warn", trim_tail(reverb(out, 0.7, 0.15)), peak=0.6)

    beep = fade(tone(1000, 0.1, SQUARE), 0.003, 0.012)
    save(SFX, "alert_closing", filt(np.concatenate([beep, silence(0.07), beep, silence(0.05)]), hi=4000), peak=0.6)

    fast = fade(tone(1400, 0.06, SQUARE), 0.002, 0.008)
    seq = np.concatenate([np.concatenate([fast, silence(0.04)]) for _ in range(5)])
    save(SFX, "alert_brace", filt(seq, hi=5000), peak=0.65)

    click = tone(2600, 0.045) * decay(0.045, 0.006, 0.0005) + filt(noise(0.045), lo=2000) * decay(0.045, 0.003) * 0.4
    save(SFX, "ui_click", click, peak=0.5)


def brass(freq, d, bright=1.0):
    t = secs(d)
    y = np.zeros_like(t)
    for k in range(1, 14):
        onset = np.minimum(1, t / (0.02 + 0.012 * k / bright))
        y += onset * np.sin(2 * np.pi * freq * k * t) / k ** 1.1
    env = np.minimum(1, t / 0.02) * np.minimum(1, (d - t) / 0.08)
    return filt(y * np.clip(env, 0, 1), hi=3500)


def note(name):
    names = {"C": -9, "C#": -8, "D": -7, "D#": -6, "E": -5, "F": -4, "F#": -3, "G": -2, "G#": -1, "A": 0, "A#": 1, "B": 2}
    pitch, octave = name[:-1], int(name[-1])
    return 440.0 * 2 ** ((names[pitch] + 12 * (octave - 4)) / 12)


def stings():
    print("stings")
    out = silence(2.6)
    for i, n in enumerate(("C5", "E5", "G5")):
        place(out, brass(note(n), 0.16) * 0.7, i * 0.13)
    for n, g in (("C6", 0.8), ("G5", 0.5), ("E5", 0.45), ("C4", 0.5)):
        place(out, brass(note(n), 1.3) * g * decay(1.3, 0.9, 0.01), 0.39)
    save(SFX, "victory_sting", trim_tail(reverb(out, 1.6, 0.3)), peak=0.8)

    out = silence(3.0)
    for i, n in enumerate(("G4", "F#4", "F4")):
        place(out, brass(note(n), 0.38, 0.5) * 0.8, i * 0.42)
    t = secs(1.5)
    last = brass(note("E4"), 1.5, 0.5) * (1 + 0.25 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(1, t / 0.3))
    place(out, last * 0.85 * decay(1.5, 1.0, 0.01), 1.26)
    save(SFX, "fail_sting", trim_tail(reverb(filt(out, hi=1800), 1.4, 0.3)), peak=0.8)


# ---------------------------------------------------------------- music

BPM = 84.0
BEAT = 60.0 / BPM
BARS = 16
LOOP = BARS * 4 * BEAT
# Two bars per chord; second half resolves through E major so the loop turns over.
PROGRESSION = [("A", "m"), ("F", ""), ("C", ""), ("G", ""), ("A", "m"), ("F", ""), ("G", ""), ("E", "")]


def chord_notes(root, quality, octave):
    semis = {"C": -9, "D": -7, "E": -5, "F": -4, "G": -2, "A": 0, "B": 2}[root]
    base = 440.0 * 2 ** ((semis + 12 * (octave - 4)) / 12)
    third = 3 if quality == "m" else 4
    return [base, base * 2 ** (third / 12), base * 2 ** (7 / 12)]


def pad_voice(freq, d):
    t = secs(d)
    y = np.zeros_like(t)
    for det in (-0.12, 0.0, 0.12):
        f = freq * 2 ** (det / 12 / 4)
        y += np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * 2 * f * t) + 0.1 * np.sin(2 * np.pi * 3 * f * t)
    env = np.minimum(1, t / 0.9) * np.minimum(1, (d - t) / 0.9)
    return y * np.clip(env, 0, 1)


def pluck(freq, d=0.9):
    t = secs(d)
    return (np.sin(2 * np.pi * freq * t) + 0.35 * np.sin(2 * np.pi * 2 * freq * t) + 0.1 * np.sin(2 * np.pi * 3 * freq * t)) \
        * np.exp(-t / 0.3) * np.minimum(1, t / 0.004)


def music_calm():
    n = int(round(LOOP * SR))
    buf = np.zeros(n)
    arp_pattern = [0, 1, 2, 3, 2, 1, 2, 1]
    for ci, (root, q) in enumerate(PROGRESSION):
        start = ci * 8 * BEAT
        for f in chord_notes(root, q, 3):
            place(buf, pad_voice(f, 8 * BEAT + 1.0) * 0.16, start - 0.5, wrap=True)
        bass = chord_notes(root, q, 2)[0]
        for b in (0, 4):
            y = tone(bass, 3.6, (1, 0.25)) * decay(3.6, 1.4, 0.02)
            place(buf, y * 0.32, start + b * BEAT, wrap=True)
        tones = chord_notes(root, q, 4) + [chord_notes(root, q, 5)[0]]
        for step in range(16):
            if ci % 4 == 0 and step < 4:
                continue  # let the phrase breathe
            f = tones[arp_pattern[step % 8]]
            place(buf, pluck(f) * (0.11 if step % 2 == 0 else 0.07), start + step * BEAT / 2, wrap=True)
    return reverb(buf, 2.4, 0.35, circular=True, tone_hi=4000)


def music_tension():
    n = int(round(LOOP * SR))
    buf = np.zeros(n)
    for ci, (root, q) in enumerate(PROGRESSION):
        start = ci * 8 * BEAT
        bass = chord_notes(root, q, 1)[0] * 2
        for step in range(16):
            g = 0.55 if step % 2 == 1 else 0.4
            y = filt(tone(bass, 0.3, SAW), hi=1300) * decay(0.3, 0.1, 0.004)
            place(buf, y * g, start + step * BEAT / 2, wrap=True)
        for beat in range(8):
            for off, g in ((0.0, 0.7), (0.5, 0.45)):
                if off and beat % 2:
                    continue
                k = sweep(62, 38, 0.22) * decay(0.22, 0.09, 0.002)
                place(buf, k * g * 0.5, start + (beat + off) * BEAT, wrap=True)
        for s in range(32):
            h = filt(noise(0.05), lo=6500) * decay(0.05, 0.012 if s % 4 else 0.03, 0.001)
            place(buf, h * (0.07 if s % 4 else 0.12), start + s * BEAT / 4, wrap=True)
        high = chord_notes(root, q, 5)[2]
        t = secs(8 * BEAT)
        trem = np.sin(2 * np.pi * high * t) * (0.5 + 0.5 * np.sin(2 * np.pi * 8 * t)) \
            * np.minimum(1, t / 1.0) * np.minimum(1, (8 * BEAT - t) / 0.4)
        place(buf, trem * 0.045, start, wrap=True)
    return reverb(buf, 1.4, 0.18, circular=True)


def music():
    print("music (%.1f s loops)" % LOOP)
    save(MUSIC, "music_calm", music_calm(), peak=0.8)
    save(MUSIC, "music_tension", music_tension(), peak=0.8)


if __name__ == "__main__":
    print("Writing to " + ROOT)
    radio_sounds()
    voice_sounds()
    ship_sounds()
    alert_sounds()
    stings()
    music()
