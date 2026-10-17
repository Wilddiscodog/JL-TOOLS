using System;

namespace JlFlash.Protocol
{
    /// <summary>JieLi CRC routines (poly 0x11021 non-reflected / 0x104C11DB7 reflected).</summary>
    public static class JlCrc
    {
        private static readonly ushort[] Crc16Table = BuildCrc16();

        private static ushort[] BuildCrc16()
        {
            var t = new ushort[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i << 8;
                for (int k = 0; k < 8; k++)
                    c = (c & 0x8000) != 0 ? (c << 1) ^ 0x1021 : c << 1;
                t[i] = (ushort)(c & 0xFFFF);
            }
            return t;
        }

        /// <summary>jl_crc16: CRC-16/XMODEM (poly 0x11021, init 0, no reflection, no xorout).</summary>
        public static ushort Crc16(byte[] data, int off, int len, ushort crc = 0)
        {
            for (int i = 0; i < len; i++)
                crc = (ushort)((crc << 8) ^ Crc16Table[(crc >> 8) ^ data[off + i]]);
            return crc;
        }

        public static ushort Crc16(byte[] data) => Crc16(data, 0, data.Length);
    }
}
