# Pedal invFFT — v0.4

A K5000-inspired inverse-FFT additive synth for ReBuzz, in the Pedal series.

- **v0.1** — iFFT-OLA path verified as a fixed 16-partial saw drone.
- **v0.2** — Amp ADSR envelope + transport-stop handling.
- **v0.3** — static spectrum-shape parameters: Brightness, Tilt, Balance.
- **v0.3.1** — bugfix: parameter names/descriptions made XML/XAML-safe
  (Core §28).
- **v0.4** — Spectrum envelope modulating Brightness. The synth now
  *breathes*.

## Files

- `FFT.cs` — radix-2 in-place complex FFT.
- `Envelope.cs` — linear-attack, exponential-decay/release ADSR with
  `ForcedRelease()` for transport-stop handling. Used unmodified for
  both amp and spec envelopes.
- `PedalInvFFT.cs` — the machine.
- `PedalInvFFT.NET.csproj` — net10.0-windows, post-build copy to
  `Gear\Generators`.

## Build & deploy

```
dotnet build -c Release
```

Defaults to `C:\Program Files\ReBuzz`; override with `/p:ReBuzzPath=...`.

## How to verify v0.4

All earlier tests still apply. With the v0.4 defaults, **Spec Amount=64**
means no spec-env modulation, so the synth should sound identical to
v0.3.1 until you dial Spec Amount above (or below) 64.

**Classic K5000 filter envelope (the one to hear first):**
- Brightness=64, Spec Amount=127, Spec Attack=0, Spec Decay=80,
  Spec Sustain=0, Spec Release=48
- Hold a note. Should attack bright (env opens brightness from 64 up
  to 127 within a few ms), then decay to dark (back to 64 over the
  decay time, since sustain is 0). On note-off, the release is mostly
  cosmetic since brightness is already low.

**Slow brightness swell (pad):**
- Brightness=32, Spec Amount=127, Spec Attack=120 (~3 s),
  Spec Sustain=127 (full hold)
- Hold a long note. Brightness should slowly rise from 32 (dark) to
  127 (bright) over the attack time, then hold. Release closes back
  down on note-off.

**Inverted envelope (filter closes during attack):**
- Brightness=127, Spec Amount=0 (full negative), Spec Attack=24,
  Spec Sustain=127
- Note attacks bright, env immediately ducks brightness down to 0
  (env=1 with negative amount = full negative mod = brightness=0),
  holds dark through sustain. The "filter closes on note" sound.

**No-op sanity check:**
- Spec Amount=64. Synth should sound exactly like v0.3.1 — confirms
  the modulation cleanly short-circuits at the neutral point.

**If something sounds wrong:**
- *Brightness doesn't move when Spec Amount goes above 64*: spec env
  isn't being advanced or its `Level` isn't being read in `RunHop`.
  Check the `_specEnv.Process()` call in the drain loop and the
  `_specEnv.Level` read at the top of `RunHop`.
- *Brightness modulates but synth tone seems wrong*: the
  `effectiveBright` clamp may be wrong. Verify it stays in [0, 127].
- *Note-off doesn't release the spec env (brightness sticks)*: missing
  `_specEnv.NoteOff()` alongside `_ampEnv.NoteOff()` in the note-off
  branch.
- *Voice rings on after Stop*: `ForcedRelease(sr)` not being called on
  `_specEnv` alongside `_ampEnv`. Check Core §27 transport-stop block.
- *Click on retrigger of just-released note*: spec env's level state
  on retrigger should continue from where it was; should not reset to
  zero. (Same click-free retrigger pattern as amp env.)

## What v0.4 still does NOT do

- **No formant filter.** Coming next (v0.5).
- **No glide.** Pitch jumps instantly between notes.
- **No polyphony.** Monophonic.
- **No GUI.** Just rack parameters and the Note track parameter.
- **Spec env is hardwired to Brightness only.** Tilt and Balance can't
  be modulated by the env — K5000-style design choice.
- **No per-harmonic sliders.** Spectrum shape is parametric.
- **No anti-clip.** A 1/n saw at `Volume=127` will clip; default 64
  is safe.

## Architecture

The synthesis path is unchanged from v0.1: `FFT_SIZE = 2048`,
`HOP_SIZE = 512` (75% overlap). Each hop, for each partial, compute a
modulated amplitude (base saw 1/n, multiplied by Brightness curve,
Tilt curve, Balance factor) and deposit the Hann main-lobe shape
across 5 bins. Inverse FFT yields a Hann-windowed time-domain frame
directly. Overlap-add into a length-FFT_SIZE accumulator.

**Amp env** evaluates per sample at the OLA drain stage:
`output = olaBuf[i] · gain · ampEnv.Process()`. The OLA buffer always
holds "what the signal would be at env=1" so envelope changes don't
invalidate the tail.

**Spec env** (new) advances per sample alongside the amp env (same
`Process()` call cadence), but its `Level` is only consulted at hop
boundaries — `RunHop()` reads `_specEnv.Level` and combines with the
static `Brightness` parameter to produce an effective brightness value
for that frame's spectrum build:

```
specEnvAmount   = (SpecAmount − 64) / 64       ∈ [−1, +1]
specMod         = specEnvAmount · _specEnv.Level · 127
effectiveBright = clamp(Brightness + specMod, 0, 127)
brightCutoff    = (effectiveBright / 127) · N_PARTIALS
```

This means brightness changes are quantized to hop rate (~94 Hz at
48 kHz), which is plenty for envelope shapes — anything faster would
be modulation rather than envelope.

Both envelopes share lifecycle: `NoteOn`, `NoteOff`, `ForcedRelease`
(on transport stop) all apply to both. The silent fast-path is gated
on `_ampEnv.IsActive` only — if the amp env is Idle, no audio is
coming out regardless of spec env state.

## Architecture limits worth knowing

- **OLA intrinsic ramp ~32 ms** at 48 kHz — see v0.2 README.
- **Spec env updates Brightness at hop rate** (~94 Hz). Slow envelopes
  fine; fast LFO-style modulation above ~30 Hz would be audibly
  stepped — not a use case for v0.4.
- **No env-to-env modulation.** Spec env can only be triggered by
  note events, not by other LFO/env sources.
- **Brightness clamp at extremes loses sweep range.** With static
  Brightness=127 and positive Spec Amount, the env has no headroom to
  open further; brightness modulation is a no-op. Symmetric at
  Brightness=0 with negative Spec Amount. Dial static brightness to
  the middle of where you want the sweep to land.

## Next steps toward v1.0

1. ✅ ~~Envelopes — Amp ADSR + transport-stop handling.~~ **(v0.2)**
2. ✅ ~~Static spectrum shape — Brightness, Tilt, Balance.~~ **(v0.3)**
3. ✅ ~~Spectrum envelope modulating Brightness.~~ **(v0.4)**
4. **Formant filter.** Centre + amount, applied as a multiplicative
   bell on the spectrum during the deposit step. Vowel-like character.
5. **Glide.** Smoothed `_freqHz` between notes; one-pole on the target.
6. **Polyphony.** Promote voice state into a `Voice` class, run N
   voices, sum their OLA outputs.

GUI lands once the sound is musically usable.
