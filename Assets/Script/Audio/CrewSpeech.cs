using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Spoken crew lines with Windows' built-in voices (offline, nothing to download or pay for).
/// One PowerShell helper runs for the whole session and turns each line into a WAV file in about
/// a tenth of a second; AudioService plays it over the radio. Off Windows, or if the helper can't
/// start, Available stays false and the crew keep their radio babble.
/// </summary>
public sealed class CrewSpeech : IDisposable
{
    public struct Voice
    {
        /// <summary>Part of the Windows voice name, e.g. "Mark" or "George".</summary>
        public string Name;
        /// <summary>Speaking rate: 1 = normal.</summary>
        public float Rate;
        /// <summary>Voice pitch: 1 = normal (0 to 2).</summary>
        public float Pitch;
    }

    public struct Result
    {
        public int Id;
        public AudioClip Clip;
    }

    /// <summary>The helper is running and has found at least one English voice.</summary>
    public bool Available { get; private set; }

    /// <summary>The voices the helper found, for the log.</summary>
    public string Voices { get; private set; } = "";

    private Process _process;
    private readonly ConcurrentQueue<string> _replies = new ConcurrentQueue<string>();
    private readonly string _folder;
    private bool _starting;

    // Reads one line per request ("id <tab> voice <tab> rate <tab> pitch <tab> text"), writes
    // id.wav and answers "OK <tab> id <tab> path". Uses the WinRT voices (Mark, George, ...),
    // which System.Speech can't see.
    private const string Script = @"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType = WindowsRuntime]
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, [Type]$t) { $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); $task.Wait(-1) | Out-Null; $task.Result }
$synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
$english = @([Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object { $_.Language -like 'en*' })
[Console]::Out.WriteLine('READY' + [char]9 + (($english | ForEach-Object { $_.DisplayName }) -join ', '))
[Console]::Out.Flush()
$dir = $args[0]
while (($line = [Console]::In.ReadLine()) -ne $null) {
  $p = $line.Split([char]9)
  if ($p.Count -lt 5) { continue }
  try {
    $v = $english | Where-Object { $_.DisplayName -like ('*' + $p[1] + '*') } | Select-Object -First 1
    if ($v -eq $null) { $v = $english | Where-Object { $_.Gender -eq 'Male' } | Select-Object -First 1 }
    if ($v -eq $null) { $v = $english | Select-Object -First 1 }
    $synth.Voice = $v
    try { $synth.Options.SpeakingRate = [double]::Parse($p[2], [Globalization.CultureInfo]::InvariantCulture) } catch {}
    try { $synth.Options.AudioPitch = [double]::Parse($p[3], [Globalization.CultureInfo]::InvariantCulture) } catch {}
    $stream = Await ($synth.SynthesizeTextToStreamAsync($p[4])) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
    $reader = New-Object Windows.Storage.Streams.DataReader($stream.GetInputStreamAt(0))
    $size = [uint32]$stream.Size
    $null = Await ($reader.LoadAsync($size)) ([uint32])
    $bytes = New-Object byte[] $size
    $reader.ReadBytes($bytes)
    $path = Join-Path $dir ($p[0] + '.wav')
    [IO.File]::WriteAllBytes($path, $bytes)
    [Console]::Out.WriteLine('OK' + [char]9 + $p[0] + [char]9 + $path)
  } catch {
    [Console]::Out.WriteLine('ERR' + [char]9 + $p[0] + [char]9 + $_.Exception.Message)
  }
  [Console]::Out.Flush()
}
";

    public CrewSpeech(string folder)
    {
        _folder = folder;
    }

    /// <summary>Starts the helper (Windows only). Available turns true once it reports its voices.</summary>
    public void Start()
    {
        if (_process != null || _starting) return;
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return;
        _starting = true;
        try
        {
            Directory.CreateDirectory(_folder);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
            var info = new ProcessStartInfo
            {
                // Full path: Unity's PATH doesn't always include the PowerShell folder.
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                // -EncodedCommand can't take arguments, so the folder is passed through the script's $args via -Command.
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"& ([scriptblock]::Create([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + encoded + "')))) '" + _folder.Replace("'", "''") + "'\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) _replies.Enqueue(e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _replies.Enqueue("ERR\t-1\t" + e.Data); };
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CREW VOICE]: spoken voices unavailable (" + ex.Message + "); the crew will babble.");
            _process = null;
        }
        _starting = false;
    }

    /// <summary>Asks for a line to be spoken. The clip arrives later through Poll.</summary>
    public void Request(int id, Voice voice, string text)
    {
        if (!Available || _process == null || _process.HasExited) return;
        string line = id + "\t" + voice.Name + "\t" +
                      voice.Rate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "\t" +
                      voice.Pitch.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "\t" + Speakable(text);
        try
        {
            _process.StandardInput.WriteLine(line);
            _process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CREW VOICE]: lost the voice helper (" + ex.Message + ").");
            Available = false;
        }
    }

    /// <summary>Call every frame on the main thread. Returns true when a spoken line is ready.</summary>
    public bool Poll(out Result result)
    {
        result = default;
        while (_replies.TryDequeue(out string reply))
        {
            var p = reply.Split('\t');
            if (p[0] == "READY")
            {
                Voices = p.Length > 1 ? p[1] : "";
                Available = Voices.Length > 0;
                Debug.Log(Available ? "[CREW VOICE]: spoken crew voices ready (" + Voices + ")."
                                    : "[CREW VOICE]: no English voices installed; the crew will babble.");
                continue;
            }
            if (p[0] == "ERR")
            {
                Debug.LogWarning("[CREW VOICE]: " + (p.Length > 2 ? p[2] : reply));
                continue;
            }
            if (p[0] != "OK" || p.Length < 3 || !int.TryParse(p[1], out int id)) continue;

            var clip = LoadWav(p[2], "crew" + id);
            try { File.Delete(p[2]); } catch { /* temp file; harmless if it stays */ }
            if (clip == null) continue;
            result = new Result { Id = id, Clip = clip };
            return true;
        }
        return false;
    }

    /// <summary>Words the voice reads well: plain ASCII, symbols spelled out.</summary>
    public static string Speakable(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length + 16);
        foreach (char c in text)
        {
            switch (c)
            {
                case '—': case '–': sb.Append(", "); break;
                case '’': case '‘': sb.Append('\''); break;
                case '“': case '”': sb.Append('"'); break;
                case '°': sb.Append(" degrees"); break;
                case '%': sb.Append(" percent"); break;
                case '\t': case '\r': case '\n': sb.Append(' '); break;
                default: if (c < 128) sb.Append(c); break;
            }
        }
        return sb.ToString().Trim();
    }

    private const float SilenceLevel = 0.004f;

    // The voices pause almost a second at every full stop; on a ship's radio that drags.
    private const float MaxPauseSeconds = 0.25f;

    /// <summary>Copies the clip with every silence inside it cut down to MaxPauseSeconds.</summary>
    private static float[] ShortenPauses(float[] all, int first, int length, int samplesPerSecond)
    {
        int window = Mathf.Max(1, samplesPerSecond / 100);              // 10 ms
        int maxQuietWindows = Mathf.RoundToInt(MaxPauseSeconds * 100f);
        var output = new System.Collections.Generic.List<float>(length);
        int quiet = 0;
        for (int start = first; start < first + length; start += window)
        {
            int end = Mathf.Min(first + length, start + window);
            float peak = 0f;
            for (int i = start; i < end; i++) peak = Mathf.Max(peak, Mathf.Abs(all[i]));
            quiet = peak < SilenceLevel * 2f ? quiet + 1 : 0;
            if (quiet > maxQuietWindows) continue; // past the allowed pause: drop this stretch
            for (int i = start; i < end; i++) output.Add(all[i]);
        }
        return output.ToArray();
    }

    /// <summary>16-bit PCM WAV (what Windows writes) into an AudioClip, silence trimmed.</summary>
    private static AudioClip LoadWav(string path, string name)
    {
        byte[] b;
        try { b = File.ReadAllBytes(path); }
        catch { return null; }
        if (b.Length < 44 || b[0] != 'R' || b[8] != 'W') return null;

        int channels = 1, rate = 16000, bits = 16, pos = 12;
        while (pos + 8 <= b.Length)
        {
            string id = Encoding.ASCII.GetString(b, pos, 4);
            int size = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(b, pos + 10);
                rate = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToInt16(b, pos + 22);
            }
            else if (id == "data")
            {
                if (bits != 16) return null;
                int count = Mathf.Min(size, b.Length - pos - 8) / 2;
                var all = new float[count];
                for (int i = 0; i < count; i++) all[i] = BitConverter.ToInt16(b, pos + 8 + i * 2) / 32768f;

                // Windows pads the speech with silence; trimmed, the radio closes when the words end.
                int first = 0, last = count - 1;
                while (first < count && Mathf.Abs(all[first]) < SilenceLevel) first++;
                while (last > first && Mathf.Abs(all[last]) < SilenceLevel) last--;
                int keep = Mathf.RoundToInt(0.06f * rate) * channels; // a breath either side
                first = Mathf.Max(0, first - keep);
                last = Mathf.Min(count - 1, last + keep);
                first -= first % Mathf.Max(1, channels);
                int length = last - first + 1;
                length -= length % Mathf.Max(1, channels);
                if (length <= 0) return null;

                var samples = ShortenPauses(all, first, length, rate * Mathf.Max(1, channels));
                length = samples.Length - samples.Length % Mathf.Max(1, channels);
                var clip = AudioClip.Create(name, length / Mathf.Max(1, channels), channels, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            pos += 8 + size + (size & 1);
        }
        return null;
    }

    public void Dispose()
    {
        Available = false;
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(500)) _process.Kill();
            }
        }
        catch { /* already gone */ }
        _process = null;
    }
}
