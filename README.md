# Pedal invFFT — v2.1

K5000-inspired polyphonic additive synth for ReBuzz, built on inverse
FFT with overlap-add resynthesis.

Up to 8 simultaneous voices, one per tracker column. Each voice
independently manages its pitch, glide, and amp + brightness envelope
state; global parameters (volume, ADSR shapes, spectrum shaping,
formant, glide time, partial stretch, animation) apply uniformly
across voices. Sixteen partials of a 1/n harmonic series form each
voice's base spectrum; static and time-varying parameters reshape it
before each iFFT.

v2.1 adds inharmonic-partial stretching and per-partial slow amplitude
animation, opening up bell, gong, and breathing-pad timbres beyond
the strictly harmonic palette of v2.0.

## Files

- `FFT.cs` — radix-2 in-place complex FFT, allocation-free after
  construction. Shared by all voices (no per-call state beyond the
  read-only twiddle tables).
- `Envelope.cs` — linear-attack, exponential-decay/release ADSR.
  Each voice has two instances (amp + brightness).
- `Voice.cs` — per-voice state and rendering. Owns the OLA accumulator,
  per-partial spectral phases, per-partial animation phases,
  envelopes, glide one-pole, and pending-event queue.
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
buffer, partial phases, animation phases. Holding a note on track 0
while retriggering on track 1 leaves track 0 unaffected. Glide and
animation are per-voice — each voice's pitch and shimmer evolve
independently.

Shared across voices: the FFT instance, the spectrum scratch, the
parameter set. Voices process sequentially within each `Work()` call
and accumulate into a mix buffer before the final `Sample[]` write.

## Parameters

Nineteen globals plus a Note track parameter.

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

**Inharmonic stretch** — power-curve warping of the partial series.

- `Stretch` (0–127, default 64) — 64 is pure harmonic. The mapped
  exponent runs 0.7 (heavy compression) at Stretch=0 through 1.0
  (harmonic) at 64 to 1.3 (heavy stretch) at 127. Below 64
  compresses partials toward the fundamental for hollow, squashed
  timbres; above 64 stretches them progressively, giving metallic
  and bell-like character at the upper end. The fundamental itself
  (`p=0`) is unaffected — the played note stays put; only the
  spectrum around it changes.

**Harmonic micro-animation** — per-partial slow amplitude modulation.
Each partial wobbles at its own rate, with rates spread across
partials (0.7×–1.3× of base) so they never synchronize. Default off;
raise Anim Depth to engage.

- `Anim Rate` (0–127, default 32) — base modulation rate, log-mapped
  0.1 Hz to 5 Hz.
- `Anim Depth` (0–127, default 0) — modulation amount. 0 means
  feature is off entirely; 127 gives ±50% amplitude swing per
  partial. Useful range is roughly 20–80 — strong enough to feel
  alive without becoming an obvious tremolo. Animation phases are
  randomized at voice construction and not reset on retrigger
  (background process, decoupled from note timing).

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

**Stretch (inharmonic partials)**:

- Default Stretch=64 sounds like v2.0.
- ~80: subtle metallic detuning of upper partials (out-of-tune piano
  feel).
- ~95: pronounced bell character.
- ~127: full gong/glass — perceived pitch becomes ambiguous because
  the upper partials no longer reinforce the fundamental.
- ~30: heavy compression, hollow and squashed.
- Particularly nice combination: `Stretch` ~110 with `Bright Decay`
  ~80 and `Bright Amount` ~30 — bell-pluck patches where the
  brightness env damps high partials over time, mimicking how real
  bells lose their high overtones first.

**Harmonic micro-animation**:

- Anim Depth=0 by default — feature off.
- Anim Depth ~30 with default Anim Rate 32: held pad starts breathing,
  spectrum slowly evolving. A/B against Depth=0 to confirm.
- Higher Depth with slower Rate gives partials drifting in and out
  of audibility. Long pads feel orchestral.
- Combines beautifully with Stretch — bell tones get their own slow
  shimmer, mimicking real bell physics where decoupled modes interact.
- Polyphonic chords with animation engaged: each voice's
  animation phases are independent, so chord textures feel richer
  than just "the same patch played at three pitches".

## Architecture

`FFT_SIZE = 2048`, `HOP_SIZE = 512` (75% overlap, Hann window, COLA
constant 2). Each hop, for each of 16 partials: compute the partial's
amplitude (1/n base, modified by Brightness, Tilt, Balance, Formant,
and Animation), deposit the Hann main-lobe shape across 5 bins
centered on the fractional frequency bin, with phase tracked across
hops for continuity. Inverse FFT yields a Hann-windowed time-domain
frame directly — no extra time-domain windowing needed. Overlap-add
into a length-FFT_SIZE accumulator.

The partial frequency formula is `_freqHz · (p+1)^stretchExp`. At
default Stretch=64 the exponent is 1.0 and the formula reduces to
the harmonic series; at other settings each partial sits at a
non-integer multiple of the fundamental. Per-partial amplitudes are
optionally modulated by independent slow LFOs (Anim Rate/Depth);
each voice carries 16 animation phases that advance at hop rate
with a linear rate spread to prevent synchronization.

Polyphony layers on top of this without changing the per-voice DSP:
each voice owns its own OLA accumulator, partial phase array,
animation phase array, hop schedule, and envelope state. Voices
process sequentially within each `Work()` call, sharing the spectrum
scratch (`_specRe`, `_specIm`) and FFT instance — each voice's
RunHop clears the scratch, fills it from its own state, and
inverse-transforms into its own OLA. Drained samples accumulate
into a `float[]` mix buffer before the final pass converts to
`Sample[]`.

The amp envelope multiplies at the per-sample drain stage (per
voice). Hop-rate updates happen at the top of each voice's
`RunHop()`: glide advances `_currentMidi` toward `_targetMidi` via a
one-pole, the brightness envelope state is sampled to compute the
effective Brightness, the per-hop formant coefficients are
precomputed, the stretch exponent is computed once, and the
animation depth/rate constants are hoisted. The brightness envelope
is advanced HOP_SIZE samples in a batched loop at the end of
`RunHop()` rather than per-sample, since its only consumer (the
spectrum builder) reads it once per hop.

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
  coefficients, pitch (during glide), stretch exponent, and
  animation phases all update once per hop (~94 Hz at 48 kHz).
  Plenty for any musical envelope curve, but spectral changes
  faster than ~10 ms can't be expressed.
- **No anti-clip.** All shaping is multiplicative on a 1/n saw base;
  Tilt, Formant, or Animation peaks can push the output past ±32768
  at high `Volume` settings. Default `Volume` of 64 leaves ~6 dB
  headroom for a single voice; dense polyphonic chords shrink that
  headroom proportionally — drop Volume to ~32 for full eight-voice
  chords on bright presets, lower still with Animation engaged.
- **Per-voice CPU scales linearly.** One FFT per hop per voice, plus
  the partial deposit loop and drain loop. At 48 kHz with 8 voices
  active simultaneously, expect ~5% of one core on a modern desktop.
  Stretch and Animation add negligible overhead (one Pow per
  partial per hop, optionally one Sin per partial per hop). Half-
  active voices (tail in Release) cost the same as fully active
  ones; only Idle and Sustain-at-zero are skipped.

## Future work

In rough priority order:

1. **LFO + mod routing.** A per-voice LFO with key-sync, plus
   per-destination depth knobs to route it (and the brightness env,
   and eventually velocity) to Tilt, Balance, Formant Centre,
   Stretch, Anim Depth, pitch, and others. The v2.2 work — turns
   every existing parameter into ten parameters and is where this
   synth gets really expressive.
2. **Spectral morph.** Define two complete partial-amplitude vectors
   as "shape A" / "shape B" (initially via presets — saw, square,
   bell, vowel, etc.) and crossfade between them. Combined with the
   LFO routing from #1, gives spectral motion that no subtractive
   synth can do — partials don't all move together, each evolves
   along its own A→B path.
3. **Phase animation.** Slow per-partial *phase* modulation, separate
   from v2.1's amplitude animation. Subtle textural movement that
   interacts with the OLA crossfade in ways amplitude animation
   doesn't reach. Per-partial phases are already tracked for
   continuity; this would just add a slow drift offset.
4. **Per-harmonic sliders.** Replace the procedural 1/n base with 16
   individual amplitude sliders, in the spirit of the K5000's
   harmonic editor. Becomes more useful once at least some preset
   spectra exist (#5).
5. **Spectrum shape presets.** Saw, square, triangle, organ-stop,
   formant-vowel, etc. as a Spectrum Shape enum that swaps the 1/n
   base for a preset table. Trivial DSP-wise; the design challenge
   is the preset selection.
6. **Stretch presets.** Beyond the smooth power-curve, specific
   inharmonic ratios — piano (Railsback), bell (1, 2, 2.4, 3, 4.5),
   gamelan, marimba — selectable via a Stretch Mode parameter, with
   power-curve as the default mode.
7. **Anti-clip / soft saturation.** Output stage tanh or polynomial
   soft-knee to absorb over-range from extreme parameter
   combinations without hard clipping. Increasingly relevant as
   8 voices stack and Stretch + Animation push partials around.
8. **Per-voice parameter offsets.** Velocity-driven Volume, per-track
   Detune, or per-track Brightness offsets would let polyphonic
   patches feel less mechanically uniform. Requires careful design
   of pattern-data delivery (extra track parameters mean extra
   sibling-poll work in `SetNote`).
