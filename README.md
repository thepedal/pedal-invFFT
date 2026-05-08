# Pedal invFFT — v2.0

K5000-inspired polyphonic additive synth for ReBuzz, built on inverse
FFT with overlap-add resynthesis.

Up to 8 simultaneous voices, one per tracker column. Each voice
independently manages its pitch, glide, and amp + brightness envelope
state; global parameters (volume, ADSR shapes, spectrum shaping,
formant, glide time) apply uniformly across voices. Sixteen partials
of a 1/n harmonic series form each voice's base spectrum; static and
time-varying parameters reshape it before each iFFT.

## Files

- `FFT.cs` — radix-2 in-place complex FFT, allocation-free after
  construction. Shared by all voices (no per-call state beyond the
  read-only twiddle tables).
- `Envelope.cs` — linear-attack, exponential-decay/release ADSR.
  Each voice has two instances (amp + brightness).
- `Voice.cs` — per-voice state and rendering. Owns the OLA accumulator,
  per-partial phases, envelopes, glide one-pole, and pending-event
  queue.
- `PedalInvFFT.cs` — the machine: parameters, audio loop, voice
  orchestration, mix accumulation, transport handling, chord-delivery
  workaround.
- `PedalInvFFT.NET.csproj` — net10.0-windows, post-build copy to
  `Gear\Generators`.

## Build & deploy

```
dotnet build -c Release
```

Defaults to `C:\Program Files\ReBuzz`; override with
`/p:ReBuzzPath="D:\Apps\ReBuzz"` (or wherever yours lives). Restart
ReBuzz to pick up the new DLL.

## Polyphony

A new instance starts with one visible track. Add more tracks via the
pattern editor's column controls (typically right-click on the column
header) up to a maximum of 8.

Each tracker column maps directly to one voice; track index *is* voice
index. Notes on multiple columns at the same row produce simultaneous
chord playback. There is no dynamic voice allocation or stealing —
chord polyphony comes from explicit per-track placement, which is the
standard ReBuzz tracker convention.

Per-voice state: pitch, glide, amp envelope, brightness envelope, OLA
buffer, partial phases. Holding a note on track 0 while retriggering
on track 1 leaves track 0 unaffected. Glide is per-voice — a slide on
track 1 doesn't drag track 0's pitch.

Shared across voices: the FFT instance, the spectrum scratch, the
parameter set. Voices process sequentially within each `Work()` call
and accumulate into a mix buffer before the final `Sample[]` write.

## Parameters

Sixteen globals plus a Note track parameter.

**Output**

- `Volume` (0–127, default 64) — output level. 64 is roughly unity
  for a single partial; the full 16-partial saw can clip near 127,
  and dense polyphonic chords push that ceiling lower still.

**Amp envelope** — gates the assembled signal at the OLA drain
stage, applied per sample, per voice.

- `Amp Attack` (0–127, default 24)
- `Amp Decay` (0–127, default 64)
- `Amp Sustain` (0–127, default 100)
- `Amp Release` (0–127, default 48)

Time params map exponentially: 0 → 0.5 ms, 64 → ~50 ms, 127 → 5 s.
Decay/Release are "time to 1/e of remaining distance to target".

**Static spectrum shaping** — multiplied per partial before iFFT.

- `Brightness` (0–127, default 127) — soft lowpass cutoff. Lower
  values attenuate higher partials with an exponential roll-off.
- `Tilt` (0–127, default 64) — broadband spectral slope. Below 64
  attenuates highs uniformly; above 64 boosts them.
- `Balance` (0–127, default 64) — even/odd partial balance. Below
  64 attenuates even partials (thin, hollow timbre); above 64
  attenuates odd partials.

**Brightness envelope** — modulates Brightness over the note (K5000
style). Per-voice — each voice's brightness env tracks its own
NoteOn/Off independently.

- `Bright Attack` (0–127, default 32)
- `Bright Decay` (0–127, default 80)
- `Bright Sustain` (0–127, default 0)
- `Bright Release` (0–127, default 48)
- `Bright Amount` (0–127, default 64) — bipolar mod depth: 64 is no
  modulation; below 64 pulls Brightness *down* during the envelope's
  attack/decay; above 64 pushes it *up*. Note: at high Amount with
  Brightness already at 127, positive modulation clamps flat.

**Formant filter** — Gaussian bell on each partial in log-frequency.

- `Formant Centre` (0–127, default 64) — peak frequency, log-mapped
  100 Hz to ~6 kHz.
- `Formant Width` (0–127, default 64) — bandwidth, 0.1 to 2 octaves.
- `Formant Amount` (0–127, default 0) — peak gain, 0 to +18 dB.
  Default 0 means "off" — the formant branch short-circuits and adds
  no per-partial cost.

**Glide**

- `Glide` (0–127, default 0) — pitch slide time between notes; 0
  means instant. Time mapping: ~5 ms at 1, ~100 ms at 64, ~2 s at
  127. First note from rest never glides — it snaps to the played
  pitch regardless of this setting. Per-voice: each voice tracks its
  own idle state, so a fresh trigger on track 1 still snaps even if
  track 0 is sustaining.

## Quick test

Drop into a song, route to master, and play notes from a pattern.

**At defaults**: pitched saw drone at the played pitch (A-4 = 440 Hz,
etc.) with a smooth ADSR shape and clean release tail.

**Spectrum shaping**:

- Sweep `Brightness` 127 → 0: bright saw → mellow → dark sine-ish.
- `Tilt` away from 64 emphasizes or de-emphasizes highs without
  changing perceived pitch.
- `Balance` < 64 yields a hollow, square-like character; > 64 is
  brighter and closer to a saw.

**Envelopes**:

- `Amp Attack` ~127: fade-in over a few seconds.
- `Bright Amount` ~30 with `Bright Decay` ~80: filter-sweep envelope
  on note onset — bright pluck collapsing to dark sustain, snapping
  bright again briefly at release.

**Formant**:

- `Formant Amount` ~80 with `Formant Centre` ~64: vowel-like
  coloration at ~765 Hz. Sweep `Formant Centre` for a wah effect.
  Particularly nice paired with the brightness envelope — the
  formant peak stays anchored while the brightness rolls past it.

**Glide**:

- `Glide` ~64 with overlapping notes (don't release before triggering
  the next) on a single track: pitch slides between them.
- First note from rest should snap directly to its pitch.

**Polyphony**:

- Add tracks, place a chord on row 0 (e.g. C4 / E4 / G4 across
  tracks 0/1/2). All three notes ring simultaneously.
- Hold a long sustain on track 0 (e.g. `Amp Sustain` 100, long
  Release), trigger short notes on track 1. Track 0's tail keeps
  ringing untouched while track 1 hammers.
- With `Glide` set, place a slide on track 0 and a separate static
  note on track 1. Each voice's pitch evolves independently.

## Architecture

`FFT_SIZE = 2048`, `HOP_SIZE = 512` (75% overlap, Hann window, COLA
constant 2). Each hop, for each of 16 partials: compute the partial's
amplitude (1/n base, modified by Brightness, Tilt, Balance, and
Formant), deposit the Hann main-lobe shape across 5 bins centered on
the fractional frequency bin, with phase tracked across hops for
continuity. Inverse FFT yields a Hann-windowed time-domain frame
directly — no extra time-domain windowing needed. Overlap-add into a
length-FFT_SIZE accumulator.

Polyphony layers on top of this without changing the per-voice DSP:
each voice owns its own OLA accumulator, partial phase array, hop
schedule, and envelope state. Voices process sequentially within
each `Work()` call, sharing the spectrum scratch (`_specRe`,
`_specIm`) and FFT instance — each voice's RunHop clears the scratch,
fills it from its own state, and inverse-transforms into its own OLA.
Drained samples accumulate into a `float[]` mix buffer before the
final pass converts to `Sample[]`.

The amp envelope multiplies at the per-sample drain stage (per voice).
Hop-rate updates happen at the top of each voice's `RunHop()`: glide
advances `_currentMidi` toward `_targetMidi` via a one-pole, the
brightness envelope state is sampled to compute the effective
Brightness, and the per-hop formant coefficients are precomputed.
The brightness envelope is advanced HOP_SIZE samples in a batched
loop at the end of `RunHop()` rather than per-sample, since its only
consumer (the spectrum builder) reads it once per hop.

A silent-fast-path check skips voices that are inaudible — Idle, or
parked in Sustain at level zero — so percussive patches drop CPU to
near-zero between notes.

ReBuzz drops sibling-track notes at chord rows due to a
`parametersChanged` dictionary collision; the machine works around
this by polling `pvalues` directly via reflection inside `SetNote`.

## Architecture limits worth knowing

- **Intrinsic OLA ramp is ~32 ms.** With FFT_SIZE=2048 and
  HOP_SIZE=512, the overlap-add buffer takes about 32 ms to build up
  to a full four-frame overlap. `Amp Attack` settings below ~32 ms
  saturate against this limit — the envelope hits unity fast but the
  OLA itself is still ramping. Suits pads and leads; not ideal for
  percussion.
- **~21 ms inherent latency.** Half the FFT window. Acceptable for
  slow-attack patches; noticeable for tight rhythmic playing.
- **Hop-rate spectrum updates.** The brightness envelope, formant
  coefficients, and pitch (during glide) update once per hop
  (~94 Hz at 48 kHz). Plenty for any musical envelope curve, but
  spectral changes faster than ~10 ms can't be expressed.
- **No anti-clip.** All shaping is multiplicative on a 1/n saw base;
  Tilt or Formant boosts can push the output past ±32768 at high
  `Volume` settings. Default `Volume` of 64 leaves ~6 dB headroom for
  a single voice; dense polyphonic chords shrink that headroom
  proportionally — drop Volume to ~32 for full eight-voice chords on
  bright presets.
- **Per-voice CPU scales linearly.** One FFT per hop per voice, plus
  the partial deposit loop and drain loop. At 48 kHz with 8 voices
  active simultaneously, expect ~5% of one core on a modern desktop.
  Half-active voices (tail in Release) cost the same as fully active
  ones; only Idle and Sustain-at-zero are skipped.

## Future work

In rough priority order:

1. **GUI.** A visual spectrum plot (per-partial amplitudes after all
   shaping) would make sound design dramatically more intuitive than
   sliders alone. Envelope curve editors and a formant-frequency
   display come next. Voice-activity LEDs would also be a nice touch
   for polyphonic playback.
2. **Per-harmonic sliders.** With a GUI in place, replace the
   procedural 1/n base with 16 individual amplitude sliders, in the
   spirit of the K5000's harmonic editor.
3. **Spectrum shape presets.** Before or alongside per-harmonic
   editing, a `Spectrum Shape` enum could swap the 1/n base for
   square, triangle, pulse, organ-stop, formant-vowel, etc.
4. **Phase animation.** The K5000 had spectral phase modulation
   (slow random walks of partial phases) for subtle movement on
   sustained tones. Cheap to add given we already track phase per
   partial per voice.
5. **Deeper modulation routing.** Currently only Brightness has an
   envelope. Routing the brightness env (or a third envelope, or an
   LFO) to Tilt, Balance, Formant Centre, or pitch would massively
   expand the sound design space.
6. **LFO.** Free-running LFO for vibrato, tremolo, and slow timbral
   wobble. Most useful as a modulation source once routing (#5) is
   in place. Per-voice with key-sync would feel most natural.
7. **Anti-clip / soft saturation.** Output stage `tanh` or polynomial
   soft-knee to absorb over-range from extreme parameter combinations
   without hard clipping. More important now that 8 voices can stack.
8. **Per-voice parameter offsets.** Velocity-driven Volume, per-track
   Detune, or per-track Brightness offsets would let polyphonic
   patches feel less mechanically uniform. Requires careful design
   of pattern-data delivery (extra track parameters mean extra
   sibling-poll work in `SetNote`).
