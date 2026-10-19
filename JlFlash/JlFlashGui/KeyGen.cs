using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace JlFlashGui
{
    /// <summary>
    /// JieLi chipkey 文件（0xXXXX.key）生成器。
    ///
    /// 文件格式（72 个 ASCII 字符，已实测验证——用本生成器复现已知 0xB165.key
    /// 逐字节一致，且 0x7DF1.key 已在 AC7911 上通过 isd_download 接受并成功 recrypt 烧录）：
    ///   [0:64]  32 字节的小写 hex（64 字符）：
    ///             前 16B = AES-128-ECB 密文，明文 = chipkey 的 4 字符大写 hex
    ///                      + "A000" + 00 + FF×7（如 "B165A000" + 填充）
    ///             后 16B = AES 密钥（生成器自选；0xB165.key 用 6efec7fa...×2）
    ///   [64:72] CRC32 大写 hex（反射 poly 0x104C11DB7、init 0x26536734、无 xorout），
    ///           计算输入 = 前 64 个 hex 字符的 ASCII 编码
    /// </summary>
    public static class KeyGen
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
                t[i] = c;
            }
            return t;
        }

        /// <summary>JieLi jl_crc32：反射 0x104C11DB7、init 0x26536734、无 xorout。</summary>
        public static uint JlCrc32(byte[] data)
        {
            uint crc = 0x26536734u;
            foreach (var b in data)
                crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
            return crc;
        }

        /// <summary>
        /// 生成 key 文件内容（72 字符）。
        /// chipkeyHex：4 位 hex（如 "7DF1"），大小写均可。
        /// aesKeyHex：可选 32 位 hex；为空时由 chipkey 派生（SHA256，确定可复现）。
        /// </summary>
        public static string Make(string chipkeyHex, string aesKeyHex = null)
        {
            var key = (chipkeyHex ?? "").Trim().ToUpperInvariant();
            if (key.Length != 4 || !key.All(Uri.IsHexDigit))
                throw new ArgumentException("chipkey 必须是 4 位 hex（如 7DF1）");

            // 明文：chipkey 的 ASCII hex（4B）+ "A000"（4B）+ 00 + FF×7 = 16B
            var pt = Encoding.ASCII.GetBytes(key + "A000")
                    .Concat(new byte[] { 0x00 })
                    .Concat(Enumerable.Repeat((byte)0xFF, 7)).ToArray();

            byte[] aesKey;
            if (string.IsNullOrEmpty(aesKeyHex))
            {
                // 由 chipkey 派生（SHA256["JLKEY-"+key] 前 16B），确定可复现
                using (var sha = SHA256.Create())
                    aesKey = sha.ComputeHash(Encoding.ASCII.GetBytes("JLKEY-" + key)).Take(16).ToArray();
            }
            else
            {
                aesKey = StringToBytes(aesKeyHex);
            }

            byte[] ct;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = aesKey;
                using (var enc = aes.CreateEncryptor())
                    ct = enc.TransformFinalBlock(pt, 0, pt.Length);
            }

            var dataHex = BitConverter.ToString(ct.Concat(aesKey).ToArray()).Replace("-", "").ToLowerInvariant();
            return dataHex + JlCrc32(Encoding.ASCII.GetBytes(dataHex)).ToString("X");
        }

        /// <summary>写 key 文件（默认名 0xXXXX.key）。</summary>
        public static string Save(string dir, string chipkeyHex)
        {
            var content = Make(chipkeyHex);
            var path = Path.Combine(dir, "0x" + chipkeyHex.Trim().ToUpperInvariant() + ".key");
            File.WriteAllText(path, content);
            return path;
        }

        private static byte[] StringToBytes(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
