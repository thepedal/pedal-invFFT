# Pedal invFFT — v2.2

K5000-inspired polyphonic additive synth for ReBuzz, built on inverse
FFT with overlap-add resynthesis.

Up to 8 simultaneous voices, one per tracker column. Each voice
independently manages its pitch, glide, amp + brightness envelopes,
and key-synced LFO; global parameters (volume, ADSR shapes, spectrum
shaping, formant, glide, partial stretch, animation, LFO routing
depths) apply uniformly across voices. Sixteen partials of a 1/n
harmonic series form each voice's base spectrum; static and
time-varying parameters reshape it before each iFFT.

v2.2 adds a per-voice LFO with four shapes (sine, triangle, square,
sample-and-hold) and six routing destinations (Pitch, Brightness,
Stretch, Volume, Formant Centre, Animation Depth) — turning every
existing parameter into a possible modulation target. Ships with a
64-preset bank as a starting point.

## Files

- `FFT.cs` — radix-2 in-place complex FFT, allocation-free after
  construction. Shared by all voices (no per-call state beyond the
  read-only twiddle tables).
- `Envelope.cs` — linear-attack, exponential-decay/release ADSR.
  Each voice has two instances (amp + brightness).
- `Voice.cs` — per-voice state and rendering. Owns the OLA accumulator,
  per-partial spectral phases, per-partial animation phases, LFO state
  (phase + sync offset + S&H value + private RNG), envelopes, glide
  one-pole, and pending-event queue.
- `PedalInvFFT.cs` — the machine: parameters, audio loop, voice
  orchestration, mix accumulation, transport handling, chord-delivery
  workaround.
- `PedalInvFFT.NET.csproj` — net10.0-windows, post-build copy to
  `Gear\Generators`.
- `PedalInvFFT_Presets.prs.xml` — 64-preset bank (pads, leads, plucks,
  bells, basses, vocal/formant patches, animated textures, FX). Lives
  alongside the DLL in `Gear\Generators` so ReBuzz auto-loads it.

## Build & deploy

```
dotnet build -c Release
```

Defaults to `C:\Program Files\ReBuzz`; override with
`/p:ReBuzzPath="D:\Apps\ReBuzz"` (or wherever yours lives). Restart
ReBuzz to pick up the new DLL.

The preset bank `PedalInvFFT_Presets.prs.xml` should be copied
alongside the DLL into the same `Gear\Generators` folder. ReBuzz
auto-loads any `.prs.xml` next to a managed machine and shows the
preset names in the right-click menu.

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
buffer, partial phases, animation phases, LFO phase. Holding a note
on track 0 while retriggering on track 1 leaves track 0 unaffected.
Glide, animation, and LFO are all per-voice — each voice's pitch,
shimmer, and modulation evolve independently. The LFO key-syncs on
each voice's NoteOn to a per-voice random offset, so polyphonic chord
voices retrigger to genuinely different LFO starting phases — chords
sound like an ensemble rather than one voice multiplied.

Shared across voices: the FFT instance, the spectrum scratch, the
parameter set. Voices process sequentially within each `Work()` call
and accumulate into a mix buffer before the final `Sample[]` write.

## Parameters

Twenty-eight globals plus a Note track parameter.

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

- `Brightness` (0–127, default 127) — soft lowpass cutoff.
- `Tilt` (0–127, default 64) — broadband spectral slope.
- `Balance` (0–127, default 64) — even/odd partial balance.

**Brightness envelope** — modulates Brightness over the note. Per-voice.

- `Bright Attack` (0–127, default 32)
- `Bright Decay` (0–127, default 80)
- `Bright Sustain` (0–127, default 0)
- `Bright Release` (0–127, default 48)
- `Bright Amount` (0–127, default 64) — bipolar modulation depth
  around 64 (no env modulation at the default).

**Formant filter** — Gaussian bell on each partial in log-frequency.

- `Formant Centre` (0–127, default 64) — peak frequency, log-mapped
  100 Hz to ~6 kHz.
- `Formant Width` (0–127, default 64) — bandwidth, 0.1 to 2 octaves.
- `Formant Amount` (0–127, default 0) — peak gain, 0 to +18 dB.
  Default 0 means "off".

**Glide**

- `Glide` (0–127, default 0) — pitch slide time between notes;
  per-voice; first note from rest snaps regardless.

**Inharmonic stretch** — power-curve warping of the partial series.

- `Stretch` (0–127, default 64) — 64 is harmonic; below 64 compresses
  partials toward the fundamental, above 64 stretches them. Default
  preserves the harmonic series.

**Harmonic micro-animation** — per-partial slow amplitude modulation.

- `Anim Rate` (0–127, default 32) — base modulation rate, log-mapped
  0.1 to 5 Hz.
- `Anim Depth` (0–127, default 0) — modulation amount; 0 = off.

**LFO** — per-voice, key-synced on each voice's NoteOn to that voice's
own random sync offset. Computed once per hop; value reused across
all routing destinations within that hop.

- `LFO Rate` (0–127, default 64) — base rate, log-mapped 0.1 to 10 Hz.
- `LFO Shape` (0–127, default 0) — quarter-banded shape selector:
  0–31 sine, 32–63 triangle, 64–95 square, 96–127 sample-and-hold.

**LFO routing depths** — all bipolar around 64 (so 64 = no modulation,
0 and 127 are full negative and positive deflection respectively).

- `LFO Pitch` (0–127, default 64) — full deflection ±3 semitones.
- `LFO Bright` (0–127, default 64) — ±63 in the Brightness param space.
- `LFO Stretch` (0–127, default 64) — ±32 in the Stretch param space
  (half-swing — full ±63 from default 64 would push to extremes).
- `LFO Volume` (0–127, default 64) — ±50% multiplicative amplitude
  swing.
- `LFO Formant` (0–127, default 64) — ±63 in the Formant Centre param
  space (modulated before the log-to-Hz mapping, so wah sweeps stay
  perceptually even).
- `LFO Anim` (0–127, default 64) — ±63 in the Anim Depth param space.
  Lets the LFO switch animation on and off when base depth is near 0,
  or wobble it within its range when base depth is mid.

## Quick test

Drop into a song, route to master, and play notes from a pattern.

**Presets first**: right-click the machine, browse the 64 preset bank
(Pad / Lead / Pluck / Bell / Bass / Vox / Anim / FX categories). Each
demonstrates a different facet of the synth's range — the bell and
animated patches are particularly distinctive of the architecture.

**At factory defaults**: pitched saw drone at the played pitch with a
smooth ADSR shape and clean release tail.

**Spectrum shaping**:

- Sweep `Brightness` 127 → 0: bright saw → mellow → dark sine-ish.
- `Tilt` away from 64 emphasizes or de-emphasizes highs.
- `Balance` < 64 yields a hollow, square-like character; > 64 a saw.

**Envelopes**:

- `Amp Attack` ~127: fade-in over a few seconds.
- `Bright Amount` ~30 with `Bright Decay` ~80: filter-sweep envelope.

**Formant**:

- `Formant Amount` ~80 with `Formant Centre` ~64: vowel coloration.

**Glide**:

- `Glide` ~64 with overlapping notes: pitch slides between them.

**Polyphony**:

- Add tracks, place a chord on row 0. All notes ring simultaneously.
- LFO with vibrato + chord: voices have independent random LFO sync
  offsets, so a chord with vibrato sounds like an ensemble rather
  than one voice multiplied.

**Stretch**:

- Stretch ~95 for bell character; ~127 for full gong/glass.
- Stretch ~30 for compressed/squashed timbres.

**Animation**:

- Anim Depth ~30: held pad starts breathing.
- Higher with slow Rate: partials drift in and out of audibility.

**LFO**:

- LFO Pitch ~80 (default Rate): gentle classical vibrato.
- LFO Bright ~92 with slow Rate (~22): filter sweep wah.
- LFO Stretch ~88 with slow Rate: breathing inharmonicity. The
  spectrum cycles between harmonic and bell-like — particularly
  distinctive of this architecture.
- LFO Shape S&H + LFO Stretch ~96: each cycle picks a fresh random
  bell tuning (try with a bell-pluck patch as the base).
- LFO Shape Square + LFO Pitch ~92: hard-switching trill.
- Stack multiple destinations: LFO Pitch ~75 + LFO Bright ~75 + slow
  rate gives an evolving "ensemble pad" character.

## Architecture

`FFT_SIZE = 2048`, `HOP_SIZE = 512` (75% overlap, Hann window, COLA
constant 2). Each hop, for each of 16 partials: compute the partial's
amplitude (1/n base, modified by Brightness, Tilt, Balance, Formant,
Animation, and LFO Volume), deposit the Hann main-lobe shape across
5 bins centered on the fractional frequency bin, with phase tracked
across hops for continuity. Inverse FFT yields a Hann-windowed
time-domain frame directly. Overlap-add into a length-FFT_SIZE
accumulator.

The partial frequency formula is `_freqHz · (p+1)^stretchExp` where
both `_freqHz` (for LFO Pitch) and `stretchExp` (for LFO Stretch)
can be modulated per hop. Per-partial amplitudes are optionally
modulated by independent slow LFOs (Animation), with the depth itself
optionally modulated by the LFO. Brightness and Formant Centre are
modulated in their parameter spaces before being mapped to per-partial
amplitudes or formant Hz. LFO Volume scales all partial amplitudes
uniformly.

The LFO is computed once per RunHop per voice and the resulting value
is reused across all six routing destinations — one Sin call per voice
per hop covers the cost. Volume modulation is applied at deposit time
(scaling the per-partial amplitudes), so the OLA's 4-frame averaging
naturally smooths fast LFO modulations.

Polyphony layers on top of this without changing the per-voice DSP:
each voice owns its own OLA accumulator, partial phase array,
animation phase array, LFO state, hop schedule, and envelope state.
Voices process sequentially within each `Work()` call, sharing the
spectrum scratch (`_specRe`, `_specIm`) and FFT instance. Drained
samples accumulate into a `float[]` mix buffer before the final pass
converts to `Sample[]`.

A silent-fast-path check skips voices that are inaudible — Idle, or
parked in Sustain at level zero — so percussive patches drop CPU to
near-zero between notes.

ReBuzz drops sibling-track notes at chord rows due to a
`parametersChanged` dictionary collision; the machine works around
this by polling `pvalues` directly via reflection inside `SetNote`.

## Architecture limits worth knowing

- **Intrinsic OLA ramp is ~32 ms.** Suits pads and leads; not ideal
  for percussion.
- **~21 ms inherent latency.** Half the FFT window.
- **Hop-rate spectrum updates.** Brightness env, formant coefficients,
  pitch, stretch, animation, LFO — all update once per hop (~94 Hz at
  48 kHz).
- **LFO rate capped at 10 Hz.** Above that, hop-rate sampling produces
  audible stair-stepping rather than smooth modulation.
- **No anti-clip.** All shaping is multiplicative on a 1/n saw base;
  Tilt, Formant, Animation peaks, or LFO Volume swings can push the
  output past ±32768 at high `Volume` settings. Default `Volume` of 64
  leaves ~6 dB headroom for a single voice; for full eight-voice
  chords on bright presets with LFO modulation engaged, drop Volume
  to ~32 to be safe.
- **Per-voice CPU scales linearly.** ~5% of one core for 8 voices at
  48 kHz, with all features active. LFO adds one Sin call per voice
  per hop — vanishing cost.

## Future work

In rough priority order:

1. **Spectral morph.** Define two complete partial-amplitude vectors
   as "shape A" / "shape B" (initially via presets — saw, square,
   bell, vowel, etc.) and crossfade between them. With the LFO
   routing now in place, morph would slot in as a seventh routing
   destination for free, giving spectral motion no subtractive synth
   can produce — partials don't all move together, each evolves along
   its own A→B path.
2. **Phase animation.** Slow per-partial *phase* modulation, separate
   from v2.1's amplitude animation. Subtle textural movement that
   interacts with the OLA crossfade in ways amplitude animation
   doesn't reach.
3. **Per-harmonic sliders.** Replace the procedural 1/n base with 16
   individual amplitude sliders, in the spirit of the K5000's
   harmonic editor.
4. **Spectrum shape presets.** Saw, square, triangle, organ-stop,
   formant-vowel, bell-partial-ratios, etc. as a Spectrum Shape enum
   that swaps the 1/n base for a preset table.
5. **Stretch presets.** Beyond the smooth power-curve, specific
   inharmonic ratios — piano (Railsback), bell (1, 2, 2.4, 3, 4.5),
   gamelan, marimba — selectable via a Stretch Mode parameter.
6. **Anti-clip / soft saturation.** Output stage tanh or polynomial
   soft-knee — increasingly relevant as 8 voices stack and LFO
   destinations push partials around.
7. **Per-voice parameter offsets.** Velocity-driven Volume, per-track
   Detune, or per-track Brightness offsets. Requires extending the
   chord-delivery workaround in `SetNote` to poll the new track
   parameters too.
8. **Second LFO and free-run mode.** A second per-voice LFO at slower
   rates would let users stack two modulations (vibrato + slow drift,
   for example). A free-run mode parameter on the existing LFO would
   complement key-sync for cases where the LFO should keep its phase
   across notes.
