using System;
using JlFlash.Transport;

namespace JlFlash.Protocol
{
    /// <summary>
    /// Loader v2 protocol (spoken by wl82loader.bin once running in RAM).
    /// Port of JL_LoaderV2 from jltech/uboot.py.
    /// </summary>
    public sealed class LoaderV2
    {
        private readonly ScsiTransport _dev;

        public LoaderV2(ScsiTransport dev) { _dev = dev; }

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

        // ---- commands ----

        /// <summary>FB00: erase 64K flash block. Returns result byte (0 = ok).</summary>
        public byte FlashEraseBlock(uint addr)
        {
            var args = new byte[4];
            UbootRom.Put32(args, 0, addr);
            return CmdExec(0xFB00, args)[2];
        }

        /// <summary>FB01: erase 4K flash sector.</summary>
        public void FlashEraseSector(uint addr)
        {
            var args = new byte[4];
            UbootRom.Put32(args, 0, addr);
            CmdExec(0xFB01, args);
        }

        /// <summary>FB02: erase whole chip (not implemented on all loaders).</summary>
        public void FlashEraseChip() => CmdExec(0xFB02, new byte[0]);

        /// <summary>FB04: write flash with CRC16.</summary>
        public void FlashWrite(uint addr, byte[] data)
        {
            var args = new byte[9];
            UbootRom.Put32(args, 0, addr);
            UbootRom.Put16(args, 4, (ushort)data.Length);
            args[6] = 0;
            UbootRom.Put16Le(args, 7, JlCrc.Crc16(data));
            CmdExecDataOut(0xFB04, args, data);
        }

        /// <summary>FD05: read flash.</summary>
        public byte[] FlashRead(uint addr, int len)
        {
            var args = new byte[6];
            UbootRom.Put32(args, 0, addr);
            UbootRom.Put16(args, 4, (ushort)len);
            return CmdExecDataIn(0xFD05, args, len);
        }


        /// <summary>FB06: write memory (loader variant; sends data as-is, no encryption).</summary>
        public void MemWrite(uint addr, byte[] data)
        {
            var args = new byte[9];
            UbootRom.Put32(args, 0, addr);
            UbootRom.Put16(args, 4, (ushort)data.Length);
            args[6] = 0;
            UbootRom.Put16Le(args, 7, JlCrc.Crc16(data));
            CmdExecDataOut(0xFB06, args, data);
        }

        /// <summary>FB08: jump to memory (loader variant: no arg field).</summary>
        public void MemJump(uint addr)
        {
            var args = new byte[4];
            UbootRom.Put32(args, 0, addr);
            CmdExec(0xFB08, args);
        }

        /// <summary>FD07: read memory (loader variant).</summary>
        public byte[] MemRead(uint addr, int len)
        {
            var args = new byte[6];
            UbootRom.Put32(args, 0, addr);
            UbootRom.Put16(args, 4, (ushort)len);
            return CmdExecDataIn(0xFD07, args, len);
        }
        /// <summary>FC03: read status.</summary>
        public byte ReadStatus() => CmdExec(0xFC03, new byte[0])[0];

        /// <summary>FC09: read chipkey. arg 0xAC6900 also unlocks flash-read on some loaders.</summary>
        public ushort ReadChipKey(uint arg = 0xAC6900)
        {
            var args = new byte[4];
            UbootRom.Put32(args, 0, arg);
            var resp = CmdExec(0xFC09, args);
            var be = new[] { resp[5], resp[4] };
            var dec = MengLi.CrcCipher(be);
            return (ushort)(dec[0] | (dec[1] << 8));
        }

        /// <summary>FC0A: get online device (type + id, e.g. JEDEC id for SPI NOR).</summary>
        public Tuple<byte, uint> OnlineDevice()
        {
            var resp = CmdExec(0xFC0A, new byte[0]);
            // resp: FC 0A <type> -- <id32 LE>
            uint id = resp[4] | (uint)(resp[5] << 8) | (uint)(resp[6] << 16) | (uint)(resp[7] << 24);
            return Tuple.Create(resp[2], id);
        }

        /// <summary>FC0B: read device id (24-bit JEDEC).</summary>
        public uint ReadId()
        {
            var resp = CmdExec(0xFC0B, new byte[0]);
            return (uint)((resp[0] << 16) | (resp[1] << 8) | resp[2]);
        }

        /// <summary>FC0C: run app / reset.</summary>
        public void RunApp(uint arg = 1)
        {
            var args = new byte[4];
            UbootRom.Put32(args, 0, arg);
            CmdExec(0xFC0C, args);
        }

        /// <summary>FC0E: flash CRC16 over a region.</summary>
        public ushort FlashCrc16(uint addr, ushort len)
        {
            var args = new byte[6];
            UbootRom.Put32(args, 0, addr);
            UbootRom.Put16(args, 4, len);
            var resp = CmdExec(0xFC0E, args);
            return (ushort)((resp[0] << 8) | resp[1]);
        }

        /// <summary>FC13: flash CRC16 (raw).</summary>
        public ushort FlashCrc16Raw(uint addr, ushort len)
        {
            var args = new byte[6];
            UbootRom.Put32(args, 0, addr);
            UbootRom.Put16(args, 4, len);
            var resp = CmdExec(0xFC13, args);
            return (ushort)((resp[0] << 8) | resp[1]);
        }

        /// <summary>FC14: get USB buffer size (assume 512 if unimplemented).</summary>
        public int GetUsbBufferSize()
        {
            try
            {
                var resp = CmdExec(0xFC14, new byte[0]);
                return (int)(resp[0] | (uint)(resp[1] << 8) | (uint)(resp[2] << 16) | (uint)(resp[3] << 24));
            }
            catch { return 512; }
        }

        /// <summary>FC15: get loader version string (16 raw bytes).</summary>
        public byte[] GetLoaderVersion() => CmdExec(0xFC15, new byte[0]);
    }
}
