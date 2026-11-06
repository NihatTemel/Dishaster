using UnityEngine;

namespace Dishaster
{
    /// <summary>G.711 μ-law: 16 bit örneği 8 bite sıkıştırıyor (konuşma için yeterli, yarı boyut).</summary>
    public static class MuLaw
    {
        const int Bias = 0x84;
        const int Clip = 32635;

        static readonly float[] DecodeTable = BuildDecodeTable();

        public static byte Encode(float sample)
        {
            int pcm = Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * 32767f);
            int sign = (pcm >> 8) & 0x80;
            if (sign != 0) pcm = -pcm;
            if (pcm > Clip) pcm = Clip;
            pcm += Bias;

            int exponent = 7;
            for (int mask = 0x4000; (pcm & mask) == 0 && exponent > 0; mask >>= 1)
                exponent--;

            int mantissa = (pcm >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static float Decode(byte value) => DecodeTable[value];

        static float[] BuildDecodeTable()
        {
            var table = new float[256];
            for (int i = 0; i < 256; i++)
            {
                int mu = ~i & 0xFF;
                int sign = mu & 0x80;
                int exponent = (mu >> 4) & 0x07;
                int mantissa = mu & 0x0F;
                int pcm = (((mantissa << 3) + Bias) << exponent) - Bias;
                table[i] = (sign != 0 ? -pcm : pcm) / 32768f;
            }

            return table;
        }
    }
}
