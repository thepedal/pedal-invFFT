// Envelope.cs — Linear-attack, exponential-decay/release ADSR envelope.
//
// Time-constant convention for exponential stages (Decay, Release): the
// param value maps to "T_ms = time for the level to decay by a factor of
// 1/e from its current value toward the target." So at T_ms the level
// has covered 63% of the distance to target; at 4·T_ms, ~98%.
//
// Attack is linear (slope = 1/N_attack per sample) — snappier than
// exponential, no infinite-time-to-hit-target issue. NoteOn() does NOT
// reset the level — retriggering during a release segment continues the
// attack from the current level, click-free (PedalSH101 §6.2).
//
// All coefficient setup is gated by a dirty check on (sr, attack, decay,
// sustain, release); changes propagate at the next Work() call without
// recomputing exp() when nothing's changed.
//
// ForcedRelease() handles ReBuzz transport-stop (PedalTracker §3): on
// the falling edge of IBuzz.Playing the pattern engine pauses but Work()
// keeps running, so a sustained note would ring forever without explicit
// intervention. ForcedRelease overrides the user's Release time with a
// short fixed fade so the voice silences quickly without clicking.

using System;

namespace PedalInvFFT
{
    public sealed class Envelope
    {
        public enum Stage { Idle, Attack, Decay, Sustain, Release }

        // Time-param mapping: 0 → MIN_MS, 127 → MAX_MS, exponential interp.
        const float MIN_MS =    0.5f;
        const float MAX_MS = 5000f;

        // Threshold below which Release is considered done and the env
        // returns to Idle (≈ -80 dB FS, well below noise floor).
        const float IDLE_THRESHOLD = 1e-4f;

        // Forced-release fade duration when transport stops. 5 ms is
        // fast enough to feel immediate, slow enough to avoid clicking
        // on whatever amplitude the OLA buffer happens to hold.
        const float FORCED_RELEASE_MS = 5f;

        Stage _stage = Stage.Idle;
        float _level = 0f;

        float _attackInc    = 0.001f;
        float _decayCoef    = 0.999f;
        float _releaseCoef  = 0.999f;
        float _sustainLevel = 1f;

        // Forced-release state: when set, Process() uses _forcedReleaseCoef
        // in place of the user's _releaseCoef during Release stage. Cleared
        // by NoteOn(), HardReset(), and on transition to Idle.
        bool  _forcedRelease     = false;
        float _forcedReleaseCoef = 0.99f;

        // Cached parameter values for dirty-check.
        int _cSr      = 0;
        int _cAttack  = -1;
        int _cDecay   = -1;
        int _cSustain = -1;
        int _cRelease = -1;

        public Stage CurrentStage => _stage;
        public bool  IsActive     => _stage != Stage.Idle;
        public float Level        => _level;

        // NoteOn does NOT reset _level — click-free retrigger from current
        // value. For a fresh note (env was Idle), _level is already 0.
        // Clears any in-progress forced release so a new pattern after
        // ReBuzz Play attacks normally.
        public void NoteOn()
        {
            _forcedRelease = false;
            _stage = Stage.Attack;
        }

        public void NoteOff()
        {
            if (_stage != Stage.Idle) _stage = Stage.Release;
        }

        // Transport-stop handler. Forces an immediate fast fade to silence
        // regardless of where the envelope was, ignoring the user's
        // Release setting. Idempotent — calling twice in a row is fine.
        public void ForcedRelease(int sr)
        {
            if (_stage == Stage.Idle) return;
            _forcedRelease     = true;
            _forcedReleaseCoef = MathF.Exp(-1000f / (FORCED_RELEASE_MS * sr));
            _stage             = Stage.Release;
        }

        public void HardReset()
        {
            _stage         = Stage.Idle;
            _level         = 0f;
            _forcedRelease = false;
        }

        // Recompute envelope coefficients when sr or any param changes.
        // Cheap no-op (5 int comparisons) when nothing changed.
        public void UpdateCoefs(int sr, int attack, int decay, int sustain, int release)
        {
            if (sr == _cSr && attack == _cAttack && decay == _cDecay
                && sustain == _cSustain && release == _cRelease) return;

            _cSr = sr; _cAttack = attack; _cDecay = decay;
            _cSustain = sustain; _cRelease = release;

            float attackMs  = ParamToMs(attack);
            float decayMs   = ParamToMs(decay);
            float releaseMs = ParamToMs(release);

            _sustainLevel = sustain / 127f;

            // Linear attack: slope = 1 / (T_ms · sr / 1000).
            int attackSamples = Math.Max(1, (int)(attackMs * 0.001f * sr));
            _attackInc = 1f / attackSamples;

            // Exponential decay/release: per-sample coef c such that
            // c^(T_ms · sr / 1000) = 1/e  →  c = exp(-1000 / (T_ms · sr)).
            _decayCoef   = MathF.Exp(-1000f / (decayMs   * sr));
            _releaseCoef = MathF.Exp(-1000f / (releaseMs * sr));
        }

        // 0..127 → 0.5..5000 ms, exponential interpolation.
        static float ParamToMs(int v)
        {
            if (v <= 0) return MIN_MS;
            float t = v / 127f;
            return MIN_MS * MathF.Pow(MAX_MS / MIN_MS, t);
        }

        // Advance one sample, returning the current level. Idle = 0.
        public float Process()
        {
            switch (_stage)
            {
                case Stage.Idle:
                    return 0f;

                case Stage.Attack:
                    _level += _attackInc;
                    if (_level >= 1f) { _level = 1f; _stage = Stage.Decay; }
                    return _level;

                case Stage.Decay:
                    _level = _sustainLevel + (_level - _sustainLevel) * _decayCoef;
                    if (MathF.Abs(_level - _sustainLevel) < IDLE_THRESHOLD)
                    { _level = _sustainLevel; _stage = Stage.Sustain; }
                    return _level;

                case Stage.Sustain:
                    _level = _sustainLevel;
                    return _level;

                case Stage.Release:
                    _level *= _forcedRelease ? _forcedReleaseCoef : _releaseCoef;
                    if (_level < IDLE_THRESHOLD)
                    {
                        _level         = 0f;
                        _stage         = Stage.Idle;
                        _forcedRelease = false;
                    }
                    return _level;
            }
            return 0f;
        }
    }
}
