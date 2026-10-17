using System;


namespace JlFlash.Protocol
{
    /// <summary>
    /// JieLi "MengLi" (孟黎) ciphers, port of jltech/cipher.py.
    /// Used for the on-wire encryption of loader downloads and chipkey values.
    /// </summary>
    public static class MengLi
    {
        // "孟黎我爱你，玉林" in GB2312 (hardcoded: .NET 8 has no GB2312 codepage by default)
        private static readonly byte[] Magic =
            { 0xC3, 0xCF, 0xC0, 0xE8, 0xCE, 0xD2, 0xB0, 0xAE, 0xC4, 0xE3, 0xA3, 0xAC, 0xD3, 0xF1, 0xC1, 0xD6 };

        /// <summary>
        /// "CrcDecode" cipher: rolling CRC-16 seeded from key, per-byte XOR with
        /// crc16 chained over the repeating magic string. Self-inverse.
        /// </summary>
        public static byte[] CrcCipher(byte[] data, uint key = 0xFFFFFFFF)
        {
            var outBuf = new byte[data.Length];
            ushort magicPos = 0;
            ushort crc = JlCrc.Crc16(BitConverter.GetBytes((ushort)(key >> 16)), 0, 2, (ushort)(key & 0xFFFF));
            for (int i = 0; i < data.Length; i++)
            {
                crc = JlCrc.Crc16(Magic, magicPos, 1, crc);
                magicPos = (ushort)((magicPos + 1) % Magic.Length);
                outBuf[i] = (byte)(data[i] ^ (crc & 0xFF));
            }
            return outBuf;
        }

        /// <summary>"ENC" cipher: XOR with low byte of a rolling CRC-16 LFSR. Self-inverse.</summary>
        public static byte[] EncCipher(byte[] data, ushort key = 0xFFFF)
        {
            var outBuf = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                outBuf[i] = (byte)(data[i] ^ (key & 0xFF));
                key = (ushort)((key << 1) ^ ((key & 0x8000) != 0 ? 0x1021 : 0));
            }
            return outBuf;
        }
    }
}
