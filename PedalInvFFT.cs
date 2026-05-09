// PedalInvFFT.cs — v2.1.
//
// Two features added on top of v2.0's polyphony:
//
//   • Stretch (inharmonic partials). Power-curve warping of the
//     partial series: partialFreq = _freqHz * (p+1)^stretchExp.
//     Stretch=64 ⇒ exponent 1.0 ⇒ pure harmonic; below 64
//     compresses partials toward the fundamental, above 64
//     stretches them. Default value preserves v2.0 sound.
//
//   • Harmonic micro-animation. Per-partial slow amplitude
//     modulation; each partial wobbles at its own rate (spread
//     0.7..1.3 × base) so partials never synchronize. Anim Depth=0
//     by default ⇒ feature off ⇒ v2.0-equivalent sound.
//
// Polyphonic K5000-inspired additive synth using inverse FFT with
// overlap-add resynthesis. Up to 8 simultaneous voices, one per
// tracker column, mapping SetNote(value, track) → _voices[track].
//
// Per-voice state (OLA buffer, partial phases, animation phases,
// envelopes, glide, hop scheduling, pending events) lives in Voice.cs.
// This machine class owns:
//
//   • Parameters (v2.1: Stretch, Anim Rate, Anim Depth appended
//     after Glide per Build §3.3 — preset-compatible)
//   • Shared spectrum scratch (_specRe, _specIm) and the FFT
//     instance, all reused per voice's RunHop
//   • The voice array
//   • The mix buffer (float[] accumulator, summed into Sample[]
//     output at the end of Work)
//   • Transport edge detection
//   • The reflection-cached pvalues handle for the chord-delivery
//     workaround (Core §14)
//
// Architecture and conventions documented in
// ReBuzz_ManagedMachine_Notes_PedalInvFFT.md and the Pedal-series
// build notes.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using BuzzGUI.Interfaces;
using Buzz.MachineInterface;

namespace PedalInvFFT
{
    [MachineDecl(Name        = "Pedal invFFT",
                 ShortName   = "invFFT",
                 Author      = "Pedal",
                 MaxTracks   = 8,
                 InputCount  = 0,
                 OutputCount = 2)]
    public class PedalInvFFTMachine : IBuzzMachine
    {
        // ── DSP constants (shared with Voice via public consts) ───────────
        public const int FFT_SIZE   = 2048;
        public const int HOP_SIZE   = 512;     // 75% overlap (H = N/4)
        public const int N_PARTIALS = 16;

        // Polyphony — eight independent voices, one per tracker column.
        public const int N_VOICES = 8;

        // Mix buffer ceiling. Realistic max ReBuzz buffer is ~4096 samples
        // (~85 ms at 48 kHz); 16384 gives 4× headroom. Allocated at
        // construction so no audio-thread alloc.
        const int MAX_BUF = 16384;

        // COLA correction at H = N/4 with Hann is 1/2 (notes §5).
        const float COLA_INV = 1f / 2f;

        // ── Shared scratch (used by all voices via Voice.RenderAccumulate) ─
        readonly float[] _specRe = new float[FFT_SIZE];
        readonly float[] _specIm = new float[FFT_SIZE];
        readonly FFT     _fft    = new FFT(FFT_SIZE);
        readonly float[] _mixBuf = new float[MAX_BUF];

        // ── Voices ────────────────────────────────────────────────────────
        readonly Voice[] _voices;

        // ── Transport state ───────────────────────────────────────────────
        bool _wasPlaying = false;     // edge detection (Core §27)

        // ── Chord-delivery workaround (Core §14) ──────────────────────────
        // ReBuzz's parametersChanged dictionary is keyed by parameter, not
        // by (parameter, track), so when multiple tracks have notes on the
        // same row, only the last track's setter fires. We work around this
        // by polling pvalues directly in SetNote — by the time any setter
        // fires for this row, the pattern editor has already written every
        // track's value into pvalues. Reflection-cached lazily on first
        // SetNote because ParameterGroups isn't populated until after the
        // constructor (Core §15).
        IParameter _ownNoteParam = null;
        ConcurrentDictionary<int,int> _ownNotePValues = null;

        // ── Parameters ────────────────────────────────────────────────────
        // Order matters for preset compatibility (Build §3.3): always
        // append new parameters at the end. Order unchanged from v1.0.

        [ParameterDecl(Name        = "Volume",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Output level (64 ≈ unity for a single partial)")]
        public int Volume { get; set; } = 64;

        // ── Amp ADSR ──────────────────────────────────────────────────────
        [ParameterDecl(Name        = "Amp Attack",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 24,
                       Description = "Amp envelope attack time (linear ramp)")]
        public int AmpAttack { get; set; } = 24;

        [ParameterDecl(Name        = "Amp Decay",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Amp envelope decay time (1 over e to sustain)")]
        public int AmpDecay { get; set; } = 64;

        [ParameterDecl(Name        = "Amp Sustain",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 100,
                       Description = "Amp envelope sustain level")]
        public int AmpSustain { get; set; } = 100;

        [ParameterDecl(Name        = "Amp Release",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 48,
                       Description = "Amp envelope release time (1 over e to zero)")]
        public int AmpRelease { get; set; } = 48;

        // ── Static spectrum shaping ───────────────────────────────────────
        [ParameterDecl(Name        = "Brightness",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 127,
                       Description = "Soft lowpass cutoff (127 is fully open)")]
        public int Brightness { get; set; } = 127;

        [ParameterDecl(Name        = "Tilt",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Spectral tilt (64 is flat, less attenuates highs, more boosts highs)")]
        public int Tilt { get; set; } = 64;

        [ParameterDecl(Name        = "Balance",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Even/odd partial balance (64 is neutral)")]
        public int Balance { get; set; } = 64;

        // ── Brightness ADSR ───────────────────────────────────────────────
        [ParameterDecl(Name        = "Bright Attack",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 32,
                       Description = "Bright envelope attack time")]
        public int BrightAttack { get; set; } = 32;

        [ParameterDecl(Name        = "Bright Decay",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 80,
                       Description = "Bright envelope decay time (1 over e to sustain)")]
        public int BrightDecay { get; set; } = 80;

        [ParameterDecl(Name        = "Bright Sustain",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 0,
                       Description = "Bright envelope sustain level")]
        public int BrightSustain { get; set; } = 0;

        [ParameterDecl(Name        = "Bright Release",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 48,
                       Description = "Bright envelope release time (1 over e to zero)")]
        public int BrightRelease { get; set; } = 48;

        [ParameterDecl(Name        = "Bright Amount",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Bright envelope modulation depth (64 is neutral)")]
        public int BrightAmount { get; set; } = 64;

        // ── Formant filter ────────────────────────────────────────────────
        [ParameterDecl(Name        = "Formant Centre",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Formant centre frequency (100 Hz to 6 kHz, log)")]
        public int FormantCentre { get; set; } = 64;

        [ParameterDecl(Name        = "Formant Width",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Formant peak width (0 narrow, 127 broad)")]
        public int FormantWidth { get; set; } = 64;

        [ParameterDecl(Name        = "Formant Amount",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 0,
                       Description = "Formant peak gain (0 off, 127 maximum boost)")]
        public int FormantAmount { get; set; } = 0;

        // ── Glide ─────────────────────────────────────────────────────────
        [ParameterDecl(Name        = "Glide",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 0,
                       Description = "Pitch glide time (0 instant, 127 slow)")]
        public int Glide { get; set; } = 0;

        // ── Inharmonic stretch ────────────────────────────────────────────
        // Power-curve stretch of the partial series. 64 = neutral
        // (harmonic). Below 64 compresses partials toward the fundamental
        // (squashed timbres, hollow attack). Above 64 stretches them
        // (metallic/bell character at the extreme). Mapped to exponent
        // 0.7..1.3 in Voice.RunHop.
        [ParameterDecl(Name        = "Stretch",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Partial stretch (64=harmonic, less=compressed, more=bell-like)")]
        public int Stretch { get; set; } = 64;

        // ── Harmonic micro-animation ──────────────────────────────────────
        // Per-partial slow amplitude modulation. Each partial wobbles at
        // its own rate (rates spread across [0.7×, 1.3×] of the base
        // rate so partials never synchronize). Default Anim Depth = 0
        // turns the feature off entirely; raise it for "alive"
        // sustained tones.
        [ParameterDecl(Name        = "Anim Rate",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 32,
                       Description = "Per-partial animation rate (0.1 Hz to 5 Hz, log)")]
        public int AnimRate { get; set; } = 32;

        [ParameterDecl(Name        = "Anim Depth",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 0,
                       Description = "Per-partial animation depth (0 off, 127 ±50% amplitude swing)")]
        public int AnimDepth { get; set; } = 0;

        // ── Track parameter ───────────────────────────────────────────────
        [ParameterDecl(Name        = "Note",
                       IsStateless = true,
                       Description = "Note trigger")]
        public void SetNote(Note value, int track)
        {
            // Handle the firing track normally.
            byte v = value.Value;
            if (v == Note.Off)        _voices[track].QueueNoteOff();
            else if (v != 0)          _voices[track].QueueNoteOn(v);

            // Recover sibling tracks' notes that ReBuzz dropped due to the
            // parametersChanged dictionary collision (Core §14). Without
            // this, chord rows with multiple tracks at the same row
            // degenerate to last-track-only playback.
            if (_ownNotePValues == null) TryInitPValues();
            if (_ownNotePValues != null)
            {
                int noVal = _ownNoteParam.NoValue;     // 0 for Note type
                for (int t = 0; t < _voices.Length; t++)
                {
                    if (t == track) continue;
                    if (_ownNotePValues.TryGetValue(t, out int pv)
                        && pv != noVal && pv != 0)
                    {
                        if (pv == Note.Off) _voices[t].QueueNoteOff();
                        else                _voices[t].QueueNoteOn((byte)pv);
                    }
                }
            }
        }

        // Lazy reflection bootstrap — runs once on first SetNote, after
        // ParameterGroups has been populated (which happens after the
        // constructor; see Core §15). On any failure we leave the fields
        // null and silently skip polling on subsequent calls; chord
        // playback would then degrade to last-track-only, but the rest
        // of the synth keeps working.
        void TryInitPValues()
        {
            try
            {
                if (_ownNoteParam == null)
                {
                    var pg = host?.Machine?.ParameterGroups;
                    if (pg == null || pg.Count < 3) return;
                    _ownNoteParam = pg[2].Parameters.FirstOrDefault(
                        p => p?.Type == ParameterType.Note);
                }
                if (_ownNoteParam == null || _ownNotePValues != null) return;

                // pvalues is ConcurrentDictionary<int,int> on
                // ParameterCore — not on the IParameter interface (Core §14).
                var fi = _ownNoteParam.GetType().GetField("pvalues",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null)
                    _ownNotePValues = fi.GetValue(_ownNoteParam)
                        as ConcurrentDictionary<int,int>;
            }
            catch { /* leave fields null; subsequent SetNote calls skip polling */ }
        }

        // ── Construction ──────────────────────────────────────────────────
        IBuzzMachineHost host;

        public PedalInvFFTMachine(IBuzzMachineHost host)
        {
            this.host = host;

            var rng = new Random();
            _voices = new Voice[N_VOICES];
            for (int v = 0; v < N_VOICES; v++)
                _voices[v] = new Voice(this, rng);
        }

        // ── Audio loop ────────────────────────────────────────────────────
        public bool Work(Sample[] output, int n, WorkModes mode)
        {
            // Safety: if a buffer larger than _mixBuf ever comes through,
            // bail with silence rather than corrupting memory. Should
            // never happen with realistic ReBuzz buffer sizes.
            if (n > _mixBuf.Length)
            {
                for (int i = 0; i < n; i++) output[i] = new Sample(0f, 0f);
                return true;
            }

            // 1. Drain pending events on each voice. Each voice owns its
            //    own NoteOn/NoteOff queue; the SetNote setter routes
            //    inputs to the right voice based on track index.
            for (int v = 0; v < _voices.Length; v++)
                _voices[v].DrainEvents();

            int sr = host?.MasterInfo?.SamplesPerSec ?? 48000;

            // 2. Transport stop — force-release all active voices on the
            //    falling edge of IBuzz.Playing (Core §27).
            bool nowPlaying = _wasPlaying;
            try { nowPlaying = host?.Machine?.Graph?.Buzz?.Playing ?? false; }
            catch { /* keep nowPlaying = _wasPlaying */ }
            if (_wasPlaying && !nowPlaying)
                for (int v = 0; v < _voices.Length; v++)
                    _voices[v].ForceReleaseAll(sr);
            _wasPlaying = nowPlaying;

            // 3. Refresh env coefficients on every voice. UpdateCoefs is
            //    dirty-checked, so unchanged params return immediately.
            for (int v = 0; v < _voices.Length; v++)
                _voices[v].UpdateEnvCoefs(sr);

            // 4. Silent fast-path — if no voice is *audible* (Idle, or
            //    parked in Sustain at level 0), skip all hop machinery.
            //    Distinct from IsActive: a voice in Sustain-at-zero is
            //    "active" but produces only silence, and there's no work
            //    to do until NoteOff arrives via DrainEvents next Work().
            bool anyAudible = false;
            for (int v = 0; v < _voices.Length; v++)
                if (_voices[v].IsAudible) { anyAudible = true; break; }
            if (!anyAudible)
            {
                for (int i = 0; i < n; i++) output[i] = new Sample(0f, 0f);
                return true;
            }

            // 5. Mix loop — clear mix buffer, each active voice
            //    accumulates, final pass converts to Sample[].
            Array.Clear(_mixBuf, 0, n);
            float gain = (Volume / 64f) * 32768f * COLA_INV;

            for (int v = 0; v < _voices.Length; v++)
            {
                if (!_voices[v].IsAudible) continue;
                _voices[v].RenderAccumulate(_mixBuf, n, sr, gain,
                                             _specRe, _specIm, _fft);
            }

            for (int i = 0; i < n; i++)
                output[i] = new Sample(_mixBuf[i], _mixBuf[i]);

            return true;
        }
    }
}
