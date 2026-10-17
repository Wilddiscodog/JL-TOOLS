using System;
using System.Linq;
using System.Threading;
using JlFlash.Protocol;
using JlFlash.Transport;

namespace JlFlash.Flow
{
    /// <summary>
    /// SRAM-run flow: ROM-only (FB06 write / FD07 verify / FB08 jump).
    /// Never touches flash - the existing flash content is preserved.
    /// The wire format (plain vs MengLi crc_cipher) is probed at runtime,
    /// so the same flow works on wl82-style and br23-style ROMs.
    /// </summary>
    public static class SramRun
    {
        // ---- SRAM-run defaults (br23/AC695N; overridable per call) ----
        public const uint PayloadAddrDefault = 0x00010000;
        public const uint MailboxAddrDefault = 0x0002FC00;

        public sealed class Options
        {
            public uint LoadAddr = PayloadAddrDefault;
            public ushort JumpArg = 0;
            public uint Mailbox = MailboxAddrDefault;
            public int ForcedEnc = 0;       // 0=auto, 1=force crc_cipher wire, -1=force plain
            public bool NoVerify = false;
        }

        /// <summary>Ensure a JL chip is in UBOOT download mode:
        /// 1. if a UBOOT MSC is already present, use it;
        /// 2. otherwise kick any firmware MSC (UDISK/DEVICE) with the private
        ///    SCSI upgrade command (opcode 0xFB -> firmware reboots into
        ///    download mode, msd_upgrade.c go_mask_usb_updata);
        /// 3. wait for the UBOOT device to (re)enumerate.</summary>
        public static string EnterDownloadMode(FlashFlow flow, Action<string> log, int timeoutMs)
        {
            string FindUboot()
            {
                foreach (var path in FlashFlow.FindDevices())
                {
                    try
                    {
                        using (var dev = new ScsiTransport(path))
                        {
                            if (UbootRom.Inquiry(dev).Item2.StartsWith("UBOOT")) return path;
                        }
                    }
                    catch { }
                }
                return null;
            }

            var ubootPath = FindUboot();
            if (ubootPath != null) return ubootPath;

            // kick every firmware MSC we can see, then wait for UBOOT to appear
            foreach (var path in FlashFlow.FindDevices())
            {
                try
                {
                    using (var dev = new ScsiTransport(path))
                    {
                        var inf = UbootRom.Inquiry(dev);
                        log($"kick: {path} '{inf.Item2}' - sending SCSI 0xFB upgrade command ...");
                        var cdb = new byte[16];
                        for (int i = 0; i < 16; i++) cdb[i] = 0xFF;
                        cdb[0] = 0xFB;
                        try { dev.Execute(cdb, null, new byte[16]); }
                        catch (Exception ex) { log($"      ({ex.Message} - expected, chip is rebooting)"); }
                    }
                }
                catch { }
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(400);
                ubootPath = FindUboot();
                if (ubootPath != null) { log("kick: UBOOT download mode entered."); return ubootPath; }
            }
            return flow.WaitForDevice(5000); // last resort: wait for a plain plug-in
        }

        /// <summary>Write a 16-byte marker with the given wire encryption and read
        /// it back through FD07; returns true when the roundtrip can be explained.</summary>
        public static bool ProbeCipher(UbootRom uboot, uint addr, bool encWrite, out bool encView)
        {
            encView = false;
            var marker = new byte[16];
            for (int i = 0; i < marker.Length; i++) marker[i] = (byte)(0xA5 ^ i);
            try
            {
                uboot.MemWrite(addr, marker, encWrite);
                var back = uboot.MemRead(addr, marker.Length);
                if (back.SequenceEqual(marker)) { encView = false; return true; }
                var dec = MengLi.CrcCipher(back);
                if (dec.SequenceEqual(marker)) { encView = true; return true; }
                return false;
            }
            catch { return false; }
        }

        /// <summary>Try all four (write, view) combinations; returns the first that
        /// round-trips. Order: plain/plain (standard UBOOT1.00), then the wl82
        /// in-place-decrypt variants.</summary>
        public static bool AutoProbeCipher(UbootRom uboot, uint addr, out bool encWrite, out bool encView)
        {
            foreach (var w in new[] { false, true })
            {
                if (ProbeCipher(uboot, addr, w, out var v)) { encWrite = w; encView = v; return true; }
            }
            encWrite = false; encView = false;
            return false;
        }

        /// <summary>Full payload run: probe wire format, FB06-write, FD07-verify,
        /// FB08-jump, then try to read back the JLRM mailbox. Throws on abort.</summary>
        public static void RunPayload(string devPath, byte[] code, Options opt, Action<string> log)
        {
            var transport = new ScsiTransport(devPath);
            var uboot = new UbootRom(transport);
            var info = UbootRom.Inquiry(transport);
            // BR23 read-safe mode (measured by BR23工具包): UBOOT1.00 ROM hangs
            // on mem_read >16B or consecutive reads -> use known MengLi wire
            // mode without probing, skip verify, single 16B mailbox read.
            bool br23 = info.Item1.Equals("BR23", StringComparison.OrdinalIgnoreCase);
            log($"Device: {devPath}  '{info.Item1}' / '{info.Item2}' / '{info.Item3}'" +
                (br23 ? "  [BR23 read-safe mode]" : ""));

            bool encWrite, encView;
            bool verified;
            if (br23)
            {
                encWrite = true; encView = true; verified = true;
                log("wire mode: write=crc_cipher, FD07 view=crc_cipher (BR23 known mode, probe skipped)");
            }
            else
            {
                if (opt.ForcedEnc == 1) { encWrite = true; encView = true; verified = ProbeCipher(uboot, opt.LoadAddr, true, out encView); }
                else if (opt.ForcedEnc == -1) { encWrite = false; encView = false; verified = ProbeCipher(uboot, opt.LoadAddr, false, out encView); }
                else verified = AutoProbeCipher(uboot, opt.LoadAddr, out encWrite, out encView);
                log($"wire mode: write={(encWrite ? "crc_cipher" : "plain")}, FD07 view={(encView ? "crc_cipher" : "plain")}" +
                    (verified ? " (probed OK)" : " (UNVERIFIED - FD07 not answering, continuing blind)"));
            }

            // ---- write payload in 512-byte chunks
            log("[1] writing payload via FB06 ...");
            for (int off = 0; off < code.Length; off += 512)
            {
                int n = Math.Min(512, code.Length - off);
                var chunk = new byte[n];
                Array.Copy(code, off, chunk, 0, n);
                uboot.MemWrite(opt.LoadAddr + (uint)off, chunk, encWrite);
            }
            log($"    {code.Length} bytes written");

            // ---- verify via FD07 (never on BR23: >16B / consecutive reads hang it)
            if (br23)
            {
                log("[2] verify skipped (BR23: FD07 reads >16B hang the device)");
            }
            else if (!opt.NoVerify && verified)
            {
                log("[2] verifying via FD07 ...");
                for (int off = 0; off < code.Length; off += 512)
                {
                    int n = Math.Min(512, code.Length - off);
                    var back = uboot.MemRead(opt.LoadAddr + (uint)off, n);
                    if (encView) back = MengLi.CrcCipher(back);
                    for (int i = 0; i < n; i++)
                        if (back[i] != code[off + i])
                            throw new InvalidOperationException(
                                $"RAM verify failed at 0x{opt.LoadAddr + (uint)(off + i):X8}: want {code[off + i]:X2} got {back[i]:X2}");
                }
                log("    verify OK");
            }
            else log("[2] verify skipped");

            // ---- jump
            log($"[3] FB08 jump to 0x{opt.LoadAddr:X8} (arg=0x{opt.JumpArg:X4}) ...");
            bool jumpBroken = false;
            try { uboot.MemJump(opt.LoadAddr, opt.JumpArg); log("    FB08 returned -> ROM alive, payload ran as a call"); }
            catch (Exception ex) { jumpBroken = true; log($"    FB08 no answer ({ex.Message}) -> payload owns the CPU now"); }

            // ---- mailbox check (only possible if the ROM kept USB alive)
            if (!jumpBroken)
            {
                Thread.Sleep(800);
                try
                {
                    // BR23: exactly ONE read, max 16 bytes
                    var raw = uboot.MemRead(opt.Mailbox, br23 ? 16 : 256);
                    if (encView) raw = MengLi.CrcCipher(raw);
                    if (raw.Length >= 3 && raw[0] == 'J' && raw[1] == 'L')
                    {
                        // mailbox: 'J','L' + NUL-terminated message from offset 2
                        int end = Array.IndexOf(raw, (byte)0, 2, raw.Length - 2);
                        if (end < 0) end = raw.Length;
                        string text = System.Text.Encoding.ASCII.GetString(raw, 2, end - 2);
                        log($"[4] MAILBOX magic OK at 0x{opt.Mailbox:X8}:");
                        log("    >>> " + text);
                    }
                    else
                    {
                        log($"[4] mailbox at 0x{opt.Mailbox:X8} has no JL magic: {BitConverter.ToString(raw, 0, Math.Min(16, raw.Length))}");
                    }
                }
                catch (Exception ex)
                {
                    log($"[4] mailbox read failed ({ex.Message}) - device went silent after the jump");
                }
            }
        }
    }
}
