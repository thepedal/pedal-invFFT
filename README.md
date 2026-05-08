# Pedal invFFT — v1.0

K5000-inspired additive synth for ReBuzz, built on inverse FFT with
overlap-add resynthesis.

A monophonic generator that builds its sound by depositing partials
directly into the frequency domain and inverse-transforming each hop
into the time domain. Sixteen partials of a 1/n harmonic series form
the base spectrum; static and time-varying parameters reshape it
before each iFFT.

## Files

- `FFT.cs` — radix-2 in-place complex FFT, allocation-free after
  construction.
- `Envelope.cs` — linear-attack, exponential-decay/release ADSR.
  Shared by both envelope instances.
- `PedalInvFFT.cs` — the machine: parameters, audio loop, per-hop
  spectrum builder, glide one-pole.
- `PedalInvFFT.NET.csproj` — net10.0-windows, post-build copy to
  `Gear\Generators`.

## Build & deploy

```
dotnet build -c Release
```

Defaults to `C:\Program Files\ReBuzz`; override with
`/p:ReBuzzPath="D:\Apps\ReBuzz"` (or wherever yours lives). Restart
ReBuzz to pick up the new DLL.

## Parameters

Sixteen globals plus a Note track parameter.

**Output**

- `Volume` (0–127, default 64) — output level. 64 is roughly unity
  for a single partial; the full 16-partial saw can clip near 127.

**Amp envelope** — gates the assembled signal at the OLA drain
stage, applied per sample.

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
style).

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
  pitch regardless of this setting.

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
  the next): pitch slides between them.
- First note from rest should snap directly to its pitch.

## Architecture

`FFT_SIZE = 2048`, `HOP_SIZE = 512` (75% overlap, Hann window, COLA
constant 2). Each hop, for each of 16 partials: compute the partial's
amplitude (1/n base, modified by Brightness, Tilt, Balance, and
Formant), deposit the Hann main-lobe shape across 5 bins centered on
the fractional frequency bin, with phase tracked across hops for
continuity. Inverse FFT yields a Hann-windowed time-domain frame
directly — no extra time-domain windowing needed. Overlap-add into a
length-FFT_SIZE accumulator.

`Work()` drains samples from the OLA accumulator, multiplied by
`gain × ampEnv.Process()` per sample. Hop-rate updates happen at the
top of each `RunHop()`: glide advances `_currentMidi` toward
`_targetMidi` via a one-pole, the brightness envelope state is
sampled to compute the effective Brightness, and the per-hop formant
coefficients are precomputed. The brightness envelope is advanced
HOP_SIZE samples in a batched loop at the end of `RunHop()` rather
than per-sample, since its only consumer (the spectrum builder)
reads it once per hop.

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
  `Volume` settings. Default `Volume` of 64 leaves ~6 dB headroom.

## Future work

In rough priority order:

1. **Polyphony.** Each voice needs its own OLA accumulator, FFT
   scratch, and envelope state (~32 KB plus FFT working set per
   voice). The FFT is the dominant cost, one per hop per voice;
   4–8 voices at the current hop rate should be comfortable on
   modern CPUs. Promote the voice state into a `Voice` class and
   sum their drain outputs.
2. **GUI.** A visual spectrum plot (per-partial amplitudes after all
   shaping) would make sound design dramatically more intuitive than
   sliders alone. Envelope curve editors and a formant-frequency
   display come next.
3. **Per-harmonic sliders.** With a GUI in place, replace the
   procedural 1/n base with 16 individual amplitude sliders, in the
   spirit of the K5000's harmonic editor.
4. **Spectrum shape presets.** Before or alongside per-harmonic
   editing, a `Spectrum Shape` enum could swap the 1/n base for
   square, triangle, pulse, organ-stop, formant-vowel, etc.
5. **Phase animation.** The K5000 had spectral phase modulation
   (slow random walks of partial phases) for subtle movement on
   sustained tones. Cheap to add given we already track phase per
   partial.
6. **Deeper modulation routing.** Currently only Brightness has an
   envelope. Routing the brightness env (or a third envelope, or an
   LFO) to Tilt, Balance, Formant Centre, or pitch would massively
   expand the sound design space.
7. **LFO.** Free-running LFO for vibrato, tremolo, and slow timbral
   wobble. Most useful as a modulation source once routing (#6) is
   in place.
8. **Anti-clip / soft saturation.** Output stage `tanh` or polynomial
   soft-knee to absorb over-range from extreme parameter combinations
   without hard clipping.
