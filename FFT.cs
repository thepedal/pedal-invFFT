// FFT.cs — Radix-2 in-place complex FFT for Pedal invFFT.
//
// Construct once with the desired N (power of two ≥ 2); reuse for all
// transforms. The instance owns precomputed twiddle and bit-reverse
// tables and is otherwise allocation-free. Caller owns the re/im
// buffers passed to Forward()/Inverse() — they're modified in place.
//
// The Forward() transform uses the standard NumPy/MATLAB convention
// with no normalization; Inverse() applies the canonical 1/N scaling.

using System;

namespace PedalInvFFT
{
    public sealed class FFT
    {
        readonly int     _n;
        readonly int     _bits;
        readonly float[] _cos;       // precomputed cos(-2π k / N), k=0..N/2-1
        readonly float[] _sin;       // precomputed sin(-2π k / N), k=0..N/2-1
        readonly int[]   _brev;      // bit-reverse permutation

        public int Size => _n;

        public FFT(int n)
        {
            if (n < 2 || (n & (n - 1)) != 0)
                throw new ArgumentException("FFT size must be a power of two ≥ 2", nameof(n));

            _n    = n;
            _bits = 0;
            for (int t = n; t > 1; t >>= 1) _bits++;

            // Twiddle factors for forward transform: e^(-i 2π k / N), k = 0..N/2-1.
            _cos = new float[n / 2];
            _sin = new float[n / 2];
            for (int i = 0; i < n / 2; i++)
            {
                double angle = -2.0 * Math.PI * i / n;
                _cos[i] = (float)Math.Cos(angle);
                _sin[i] = (float)Math.Sin(angle);
            }

            // Bit-reversal lookup.
            _brev = new int[n];
            for (int i = 0; i < n; i++)
            {
                int rev = 0, x = i;
                for (int b = 0; b < _bits; b++)
                {
                    rev = (rev << 1) | (x & 1);
                    x >>= 1;
                }
                _brev[i] = rev;
            }
        }

        // In-place forward FFT. Re and im must each be length N.
        public void Forward(float[] re, float[] im)
        {
            // Bit-reverse permutation.
            for (int i = 0; i < _n; i++)
            {
                int j = _brev[i];
                if (i < j)
                {
                    float t = re[i]; re[i] = re[j]; re[j] = t;
                    t       = im[i]; im[i] = im[j]; im[j] = t;
                }
            }

            // Cooley-Tukey butterflies, decimation-in-time.
            for (int size = 2; size <= _n; size <<= 1)
            {
                int half = size >> 1;
                int step = _n / size;
                for (int i = 0; i < _n; i += size)
                {
                    int k = 0;
                    for (int j = i; j < i + half; j++)
                    {
                        int   l   = j + half;
                        float c   = _cos[k];
                        float s   = _sin[k];
                        float tre = re[l] * c - im[l] * s;
                        float tim = re[l] * s + im[l] * c;
                        re[l] = re[j] - tre;
                        im[l] = im[j] - tim;
                        re[j] += tre;
                        im[j] += tim;
                        k += step;
                    }
                }
            }
        }

        // In-place inverse FFT, normalized by 1/N.
        // Identity:  IFFT(x) = (1/N) · conj(FFT(conj(x))).
        public void Inverse(float[] re, float[] im)
        {
            for (int i = 0; i < _n; i++) im[i] = -im[i];
            Forward(re, im);
            float inv = 1f / _n;
            for (int i = 0; i < _n; i++)
            {
                re[i] *=  inv;
                im[i] = -im[i] * inv;
            }
        }
    }
}
