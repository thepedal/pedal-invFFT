// Voice.cs — per-voice state and rendering for Pedal invFFT polyphony.
//
// Promoted out of the machine class in v2.0 (was monophonic in v1.0,
// with all of this lying directly on PedalInvFFTMachine). One Voice
// per tracker column; the machine owns an array of N_VOICES of these
// and routes SetNote(value, track) to _voices[track].
//
// Voice owns:
//   • The OLA accumulator buffer (residual ringing of past frames)
//   • Per-partial phase array (continuous across hops, see notes §6)
//   • Hop scheduling (_samplesUntilNextHop, _olaReadPos)
//   • Pitch state — _freqHz plus glide one-pole on
//     _currentMidi/_targetMidi
//   • Both ADSR envelope instances
//   • Pending event flags (per-track NoteOn/NoteOff queue,
//     PedalSH101 §6.3)
//
// Voice borrows from the machine each render call:
//   • Shared spectrum scratch (_specRe/_specIm) — reused per voice's
//     RunHop, no cross-talk because voices process sequentially
//   • Shared FFT instance (twiddle tables read-only after ctor;
//     Inverse() is in-place on input arrays — safe to share when
//     voices run sequentially)
//   • The PedalInvFFTMachine reference for parameter reads
//     (Brightness, Tilt, Balance, BrightAmount, Formant*, Glide,
//     and the env time/level params via UpdateEnvCoefs)
//
// If we ever parallelize voice processing, the shared scratch and
// FFT need either per-voice copies or explicit synchronization.
// For now the foreach loop in PedalInvFFTMachine.Work guarantees
// sequential execution.

using System;

namespace PedalInvFFT
{
    sealed class Voice
    {
        // ── Constants used inside RunHop ──────────────────────────────────
        const int FFT_SIZE   = PedalInvFFTMachine.FFT_SIZE;
        const int HOP_SIZE   = PedalInvFFTMachine.HOP_SIZE;
        const int N_PARTIALS = PedalInvFFTMachine.N_PARTIALS;

        const float DEPOSIT_GAIN         = FFT_SIZE / 4f;   // notes §5
        const float BRIGHTNESS_STEEPNESS = 0.7f;
        const float MAX_TILT_AMOUNT      = 0.5f;

        // ── Per-voice state ───────────────────────────────────────────────
        readonly float[]   _olaBuf     = new float[FFT_SIZE];
        readonly float[]   _phases     = new float[N_PARTIALS];
        readonly float[]   _animPhases = new float[N_PARTIALS];   // §20
        readonly Envelope  _ampEnv     = new Envelope();
        readonly Envelope  _brightEnv  = new Envelope();   // modulates Brightness

        int   _samplesUntilNextHop = 0;
        int   _olaReadPos          = 0;
        float _freqHz              = 440f;

        // Glide state — _currentMidi follows _targetMidi via per-hop one-pole.
        // Continuous semitones (float) keeps the slide linear in pitch space.
        float _currentMidi = 60f;
        float _targetMidi  = 60f;

        // Pending events (drained at top of Work, PedalSH101 §6.3).
        bool _hasNoteOn       = false;
        bool _hasNoteOff      = false;
        byte _pendingBuzzNote = 0;

        readonly PedalInvFFTMachine _machine;

        public Voice(PedalInvFFTMachine machine, Random rng)
        {
            _machine = machine;
            for (int p = 0; p < N_PARTIALS; p++)
            {
                _phases[p]     = (float)(rng.NextDouble() * 2.0 * Math.PI);
                _animPhases[p] = (float)(rng.NextDouble() * 2.0 * Math.PI);
            }
        }

        // ── Event queue (called from setter on UI thread) ─────────────────
        public void QueueNoteOn(byte buzzNote)
        {
            _pendingBuzzNote = buzzNote;
            _hasNoteOn       = true;
        }

        public void QueueNoteOff() { _hasNoteOff = true; }

        // ── Audio thread interface ────────────────────────────────────────
        public bool IsActive => _ampEnv.IsActive;

        // IsAudible: should this voice contribute to the mix this Work()?
        // True when the amp envelope is producing non-zero output OR is in
        // a stage whose level is changing (Attack/Decay/Release — those need
        // to keep ticking even at zero, e.g. Release entered at level 0
        // needs one Process call to transition to Idle). False when the
        // envelope is parked: Idle, or Sustain with sustainLevel == 0.
        //
        // The two-predicate split saves a chunk of CPU for percussive
        // patches (AmpSustain=0): once Decay finishes the env sits in
        // Sustain at level 0 forever-pending-NoteOff, IsActive stays true,
        // but no work is needed until NoteOff drains in via the next
        // Work()'s DrainEvents call.
        public bool IsAudible
        {
            get
            {
                if (!_ampEnv.IsActive) return false;
                if (_ampEnv.CurrentStage == Envelope.Stage.Sustain
                    && _ampEnv.Level <= 0f)
                    return false;
                return true;
            }
        }

        public void DrainEvents()
        {
            if (_hasNoteOn)
            {
                TriggerNote(_pendingBuzzNote);
                _hasNoteOn = false;
            }
            if (_hasNoteOff)
            {
                _ampEnv.NoteOff();
                _brightEnv.NoteOff();
                _hasNoteOff = false;
            }
        }

        public void UpdateEnvCoefs(int sr)
        {
            _ampEnv.UpdateCoefs(sr,
                _machine.AmpAttack,    _machine.AmpDecay,
                _machine.AmpSustain,   _machine.AmpRelease);
            _brightEnv.UpdateCoefs(sr,
                _machine.BrightAttack, _machine.BrightDecay,
                _machine.BrightSustain, _machine.BrightRelease);
        }

        public void ForceReleaseAll(int sr)
        {
            _ampEnv.ForcedRelease(sr);
            _brightEnv.ForcedRelease(sr);
        }

        public void RenderAccumulate(float[] mixBuf, int n, int sr, float gain,
                                      float[] specRe, float[] specIm, FFT fft)
        {
            int produced = 0;
            while (produced < n)
            {
                if (_samplesUntilNextHop <= 0)
                {
                    RunHop(sr, specRe, specIm, fft);
                    _samplesUntilNextHop = HOP_SIZE;
                    _olaReadPos          = 0;
                }

                int batch = Math.Min(n - produced, _samplesUntilNextHop);
                for (int i = 0; i < batch; i++)
                {
                    float aEnv = _ampEnv.Process();
                    // _brightEnv NOT advanced here — see RunHop, notes §11.
                    mixBuf[produced + i] += _olaBuf[_olaReadPos + i] * gain * aEnv;
                }
                produced            += batch;
                _olaReadPos         += batch;
                _samplesUntilNextHop -= batch;
            }
        }

        // ── Note triggering ───────────────────────────────────────────────
        void TriggerNote(byte buzzNote)
        {
            int octave = (buzzNote >> 4);
            int semi   = (buzzNote & 0xF) - 1;
            int midi   = octave * 12 + semi;

            // Per-voice "first note from rest doesn't glide" (notes §12,
            // PedalSH101 §6.1). Each voice tracks its own idle state, so a
            // fresh trigger on this track snaps regardless of what other
            // voices are doing.
            bool wasIdle = !_ampEnv.IsActive;

            _targetMidi = midi;
            if (_machine.Glide == 0 || wasIdle)
                _currentMidi = midi;
            // else: leave _currentMidi to glide via per-hop one-pole in RunHop.

            if (wasIdle)
            {
                Array.Clear(_olaBuf, 0, FFT_SIZE);
                _samplesUntilNextHop = 0;
                _olaReadPos          = 0;
            }

            _ampEnv   .NoteOn();
            _brightEnv.NoteOn();
        }

        // ── Per-hop synthesis ─────────────────────────────────────────────
        void RunHop(int sr, float[] specRe, float[] specIm, FFT fft)
        {
            // Shift OLA buffer left by HOP_SIZE; zero the new tail.
            Array.Copy (_olaBuf, HOP_SIZE, _olaBuf, 0, FFT_SIZE - HOP_SIZE);
            Array.Clear(_olaBuf, FFT_SIZE - HOP_SIZE, HOP_SIZE);

            // Clear the SHARED spectrum scratch — voices reuse this across
            // sequential RunHop calls, so each voice must clear it first.
            Array.Clear(specRe, 0, FFT_SIZE);
            Array.Clear(specIm, 0, FFT_SIZE);

            float nyquist     = sr * 0.5f;
            float binsPerHz   = (float)FFT_SIZE / sr;
            float phaseAdvCo  = 2f * MathF.PI * HOP_SIZE / sr;
            int   posBinLimit = FFT_SIZE / 2;

            // Glide — advance _currentMidi toward _targetMidi by one hop.
            if (_machine.Glide > 0 && _currentMidi != _targetMidi)
            {
                float glideMs = 5f * MathF.Pow(400f, _machine.Glide / 127f);
                float coef    = MathF.Exp(-1000f * HOP_SIZE / (glideMs * sr));
                _currentMidi  = _targetMidi + (_currentMidi - _targetMidi) * coef;
                if (MathF.Abs(_currentMidi - _targetMidi) < 0.001f)
                    _currentMidi = _targetMidi;   // sub-cent residual snap
            }
            _freqHz = 440f * MathF.Pow(2f, (_currentMidi - 69f) / 12f);

            // Bright env modulation of brightness.
            float brightEnvAmount = (_machine.BrightAmount - 64) / 64f;
            float brightMod       = brightEnvAmount * _brightEnv.Level * 127f;
            float effectiveBright = MathF.Max(0f, MathF.Min(127f,
                                              _machine.Brightness + brightMod));

            // Hop-constant spectrum-shape modifiers.
            float brightCutoff = (effectiveBright / 127f) * N_PARTIALS;
            float tiltAmount   = (_machine.Tilt - 64) / 64f * MAX_TILT_AMOUNT;
            float balOffset    = (_machine.Balance - 64) / 64f;
            float evenScale    = MathF.Max(0f, MathF.Min(1f, 1f + balOffset));
            float oddScale     = MathF.Max(0f, MathF.Min(1f, 1f - balOffset));

            // Formant — Gaussian bell on each partial in log-frequency.
            // FormantAmount=0 short-circuits the per-partial branch.
            bool  useFormant       = _machine.FormantAmount > 0;
            float formantCenterHz  = 0f;
            float formantInvWidth  = 0f;
            float formantPeakMinus = 0f;
            if (useFormant)
            {
                formantCenterHz  = 100f * MathF.Pow(60f, _machine.FormantCentre / 127f);
                float widthOct   = 0.1f + (_machine.FormantWidth / 127f) * 1.9f;
                formantInvWidth  = 1f / widthOct;
                float peakDb     = (_machine.FormantAmount / 127f) * 18f;
                formantPeakMinus = MathF.Pow(10f, peakDb / 20f) - 1f;
            }

            // Partial-stretch exponent. Mapped from the 0..127 Stretch
            // parameter to 0.7..1.3 around a neutral 1.0. At Stretch=64
            // the exponent is exactly 1.0 ⇒ Pow((p+1), 1.0) ≈ p+1, so
            // the harmonic series is preserved (within float precision)
            // for the default value. Hoisted out of the partial loop —
            // unchanged within a hop. Per-hop changes are at hop rate
            // (~94 Hz), well below any audible transient.
            float stretchExp = 1f + (_machine.Stretch - 64) / 64f * 0.3f;

            // Harmonic micro-animation (§20). Hoisted out of the
            // partial loop — depth and base rate are constant within a
            // hop, only the per-partial phases advance. animOn=false at
            // depth 0 takes a fast path that skips the per-partial Sin
            // and the phase advance, so default-value cost is essentially
            // zero.
            int   animDepthInt     = _machine.AnimDepth;
            bool  animOn           = animDepthInt > 0;
            float animDepth        = 0f;
            float animPhaseAdvBase = 0f;
            if (animOn)
            {
                animDepth        = (animDepthInt / 127f) * 0.5f;          // 0..0.5
                float animBaseHz = 0.1f * MathF.Pow(50f, _machine.AnimRate / 127f);
                animPhaseAdvBase = 2f * MathF.PI * HOP_SIZE / sr * animBaseHz;
            }

            for (int p = 0; p < N_PARTIALS; p++)
            {
                float partialFreq = _freqHz * MathF.Pow(p + 1, stretchExp);
                if (partialFreq >= nyquist) break;

                float kStar = partialFreq * binsPerHz;
                int   kCent = (int)MathF.Round(kStar);

                float amp = 1f / (p + 1);

                if (p > brightCutoff)
                    amp *= MathF.Exp(-(p - brightCutoff) * BRIGHTNESS_STEEPNESS);

                if (tiltAmount != 0f)
                    amp *= MathF.Pow(p + 1, tiltAmount);

                if (balOffset != 0f)
                    amp *= ((p + 1) % 2 == 0) ? evenScale : oddScale;

                if (useFormant)
                {
                    float deltaOct = MathF.Log2(partialFreq / formantCenterHz);
                    float u        = deltaOct * formantInvWidth;
                    float weight   = MathF.Exp(-u * u);
                    amp *= 1f + formantPeakMinus * weight;
                }

                // Per-partial animation. Off-by-default; when on,
                // multiplies amp by (1 + depth × sin(phase[p])).
                if (animOn)
                {
                    float anim = 1f + animDepth * MathF.Sin(_animPhases[p]);
                    amp *= anim;
                }

                float dep   = DEPOSIT_GAIN * amp;
                float phase = _phases[p];
                float reC   = dep * MathF.Cos(phase);
                float imC   = dep * MathF.Sin(phase);

                for (int dk = -2; dk <= 2; dk++)
                {
                    int bin = kCent + dk;
                    if (bin <= 0 || bin >= posBinLimit) continue;

                    float w = HannLobeWeight(bin - kStar);

                    specRe[bin] += reC * w;
                    specIm[bin] += imC * w;

                    int negBin = FFT_SIZE - bin;
                    specRe[negBin] += reC * w;
                    specIm[negBin] -= imC * w;
                }

                _phases[p] += phaseAdvCo * partialFreq;
                while (_phases[p] >  MathF.PI) _phases[p] -= 2f * MathF.PI;
                while (_phases[p] < -MathF.PI) _phases[p] += 2f * MathF.PI;

                // Advance per-partial animation phase. Linear rate spread
                // (0.7..1.3 of base) ensures partials don't synchronize
                // — partial 15's period is ~1.86× partial 0's, irrational
                // enough in practice that they never realign over musical
                // timescales.
                if (animOn)
                {
                    float rate = 0.7f + p * 0.04f;
                    _animPhases[p] += animPhaseAdvBase * rate;
                    while (_animPhases[p] >  MathF.PI) _animPhases[p] -= 2f * MathF.PI;
                    while (_animPhases[p] < -MathF.PI) _animPhases[p] += 2f * MathF.PI;
                }
            }

            fft.Inverse(specRe, specIm);

            for (int i = 0; i < FFT_SIZE; i++)
                _olaBuf[i] += specRe[i];

            // Advance brightness env by HOP_SIZE samples — batched at end
            // of RunHop instead of per-sample in the drain loop, see
            // notes §11 for the v0.4 hang and why this dodges it.
            for (int s = 0; s < HOP_SIZE; s++)
                _brightEnv.Process();
        }

        // Hann window's frequency-domain main-lobe weight (notes §2).
        // Ŵ(d) = sinc(d) − 0.5·sinc(d−1) − 0.5·sinc(d+1)
        static float HannLobeWeight(float d)
        {
            return Sinc(d) - 0.5f * Sinc(d - 1f) - 0.5f * Sinc(d + 1f);
        }

        static float Sinc(float x)
        {
            if (MathF.Abs(x) < 1e-6f) return 1f;
            float px = MathF.PI * x;
            return MathF.Sin(px) / px;
        }
    }
}
