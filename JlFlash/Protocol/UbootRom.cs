using System;
using JlFlash.Transport;

namespace JlFlash.Protocol
{
    /// <summary>
    /// UBOOT1.00 protocol (the on-chip Boot ROM), protocol v2.
    /// Used to download the loader into RAM and jump to it.
    /// Port of JL_UBOOT from jltech/uboot.py.
    /// </summary>
    public sealed class UbootRom
    {
        private readonly ScsiTransport _dev;

        public UbootRom(ScsiTransport dev) { _dev = dev; }

        /// <summary>SCSI Inquiry; returns (vendor, product, revision).</summary>
        public static Tuple<string, string, string> Inquiry(ScsiTransport dev)
        {
            var data = new byte[36];
            var cdb = new byte[16];
            cdb[0] = 0x12; // INQUIRY
            cdb[4] = 36;
            dev.Execute(cdb, null, data);
            return Tuple.Create(
                Ascii(data, 8, 8), Ascii(data, 16, 16), Ascii(data, 32, 4));
        }

        private static string Ascii(byte[] b, int off, int len)
        {
            var s = new char[len];
            for (int i = 0; i < len; i++) s[i] = (char)b[off + i];
            return new string(s).Trim();
        }

        private byte[] Cdb(ushort cmd, byte[] args)
        {
            var cdb = new byte[16];
            for (int i = 0; i < 16; i++) cdb[i] = 0xFF;
            cdb[0] = (byte)(cmd >> 8);
            cdb[1] = (byte)cmd;
            Array.Copy(args, 0, cdb, 2, args.Length);
            return cdb;
        }

        private byte[] CmdExec(ushort cmd, byte[] args)
        {
            var resp = new byte[16];
            _dev.Execute(Cdb(cmd, args), null, resp);
            ushort rcmd = (ushort)((resp[0] << 8) | resp[1]);
            if (rcmd != cmd) throw new InvalidOperationException(
                $"Command {cmd:X4} responded with {rcmd:X4}");
            return resp;
        }

        private byte[] CmdExecDataIn(ushort cmd, byte[] args, int len)
        {
            var data = new byte[len];
            _dev.Execute(Cdb(cmd, args), null, data);
            return data;
        }

        private void CmdExecDataOut(ushort cmd, byte[] args, byte[] data)
        {
            _dev.Execute(Cdb(cmd, args), data, null);
        }

        /// <summary>FB06: write memory (CRC-checked by the chip).</summary>
        public void MemWrite(uint addr, byte[] data, bool encrypt)
        {
            var payload = encrypt ? MengLi.CrcCipher(data) : data;
            var args = new byte[10];
            Put32(args, 0, addr);
            Put16(args, 4, (ushort)data.Length);
            args[6] = 0;
            Put16(args, 7, JlCrc.Crc16(data));
            CmdExecDataOut(0xFB06, args, payload);
        }

        /// <summary>FD07: read memory.</summary>
        public byte[] MemRead(uint addr, int len)
        {
            var args = new byte[8];
            Put32(args, 0, addr);
            Put16(args, 4, (ushort)len);
            return CmdExecDataIn(0xFD07, args, len);
        }

        /// <summary>FB08: jump to memory with 16-bit argument.</summary>
        public void MemJump(uint addr, ushort arg)
        {
            var args = new byte[6];
            Put32(args, 0, addr);
            Put16(args, 4, arg);
            CmdExec(0xFB08, args);
        }

        internal static void Put32(byte[] b, int off, uint v)
        { b[off] = (byte)(v >> 24); b[off + 1] = (byte)(v >> 16); b[off + 2] = (byte)(v >> 8); b[off + 3] = (byte)v; }
        internal static void Put16(byte[] b, int off, ushort v)
        { b[off] = (byte)(v >> 8); b[off + 1] = (byte)v; }
        internal static void Put16Le(byte[] b, int off, ushort v)
        { b[off] = (byte)v; b[off + 1] = (byte)(v >> 8); }
    }
}
