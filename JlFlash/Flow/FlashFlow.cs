using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JlFlash.Protocol;
using JlFlash.Transport;

namespace JlFlash.Flow
{
    /// <summary>
    /// Full flashing flow: handshake with ROM, download the loader into RAM,
    /// jump, then erase/write/verify the flash image and reset.
    /// No chipkey is required: the loader writes whatever it is given, and the
    /// image itself (jl_isd.bin) is already encrypted by the SDK packaging tools.
    /// </summary>
    public enum JlChip
    {
        Wl82,       // AC791N - wl82loader.bin @ 0x1C02000
        Br23,       // AC695N - br23loader.bin @ 0x12000 (MengLi-stored blob)
    }

    public sealed class FlashFlow
    {
        private JlChip _chip = JlChip.Wl82;

        /// <summary>Target chip family - selects loader file / RAM address / jump args.</summary>
        public JlChip Chip
        {
            get { return _chip; }
            set { _chip = value; _loaderBlob = null; }
        }

        public uint LoaderAddress
        {
            get { return _chip == JlChip.Br23 ? 0x12000u : 0x1C02000u; }
        }

        public int LoaderBlockSize = 512;

        private readonly Action<string> _log;
        private byte[] _loaderBlob;

        public FlashFlow(Action<string> log) { _log = log ?? delegate { }; }

        /// <summary>Enumerate candidate volumes and find one answering as a JieLi device.</summary>
        public static List<string> FindDevices()
        {
            var found = new List<string>();
            for (char c = 'C'; c <= 'Z'; c++)
            {
                string path = $"\\\\.\\{c}:";
                try
                {
                    using (var dev = new ScsiTransport(path))
                    {
                        var info = UbootRom.Inquiry(dev);
                        string product = info.Item2;
                        if (product.StartsWith("UBOOT") || product.StartsWith("UDISK") || product.StartsWith("DEVICE"))
                            found.Add(path);
                    }
                }
                catch { /* not present / not accessible */ }
            }
            return found;
        }

        /// <summary>Wait until a device answers Inquiry with a UBOOT product string.</summary>
        public string WaitForDevice(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var path in FindDevices())
                {
                    using (var dev = new ScsiTransport(path))
                    {
                        var info = UbootRom.Inquiry(dev);
                        if (info.Item2.StartsWith("UBOOT")) return path;
                    }
                }
                Thread.Sleep(300);
            }
            throw new TimeoutException("No JieLi UBOOT device found");
        }

        /// <summary>Download the loader into RAM via the ROM (UBOOT1.00).</summary>
        private byte[] DownloadLoader(UbootRom uboot)
        {
            string loaderName = _chip == JlChip.Br23 ? "br23loader.bin" : "wl82loader.bin";
            _log($"      downloading {loaderName} into RAM @ 0x{LoaderAddress:X8} ...");
            byte[] loader = LoadLoaderBlob();
            for (int off = 0; off < loader.Length; off += LoaderBlockSize)
            {
                int n = Math.Min(LoaderBlockSize, loader.Length - off);
                var block = new byte[n];
                Array.Copy(loader, off, block, 0, n);
                // The loader blob is ALREADY stored in MengLi-encrypted form; the
                // ROM decrypts it in place while writing, so send it as-is.
                uboot.MemWrite(LoaderAddress + (uint)off, block, encrypt: false);
            }
            _log($"      {loader.Length} bytes written to 0x{LoaderAddress:X8}");
            return loader;
        }

        /// <summary>
        /// Jump into the loader and wait for it to take over the USB stack.
        /// The FB08 response may time out and the device re-enumerates (possibly
        /// under a different drive letter), so we rescan until the loader answers
        /// FC14 (GetUsbBufferSize). Jump-arg encodes target|div|spi-mode.
        /// </summary>
        private Tuple<LoaderV2, ScsiTransport> JumpToLoader(UbootRom uboot, byte[] loader)
        {
            // arg bit0-3 = target (1 = SPI NOR), bit4-11 = clk div, bit12-15 = spi mode.
            // br23: kagaimiq default 0x0011; plain 0 also tried.
            ushort[] args = _chip == JlChip.Br23
                ? new ushort[] { 0x0011, 0x0000 }
                : new ushort[] { 0x0011, 0x1011, 0x2011, 0x3011 };
            foreach (ushort arg in args)
            {
                try { uboot.MemJump(LoaderAddress, arg); }
                catch (IOException ex)
                {
                    // error 121 (timeout) / 1167 (re-enumeration) means it likely ran
                    _log($"      jump arg=0x{arg:X4}: {ex.Message} (may still have executed)");
                }

                for (int attempt = 0; attempt < 25; attempt++)
                {
                    Thread.Sleep(400);
                    foreach (var path in FindDevices())
                    {
                        try
                        {
                            var transport = new ScsiTransport(path);
                            var ld = new LoaderV2(transport);
                            int bufSize = ld.GetUsbBufferSize();       // throws if not the loader
                            var online = ld.OnlineDevice();
                            _log($"      loader alive at {path} (arg=0x{arg:X4}), usbBuf={bufSize}, " +
                                 $"online type=0x{online.Item1:X2} id=0x{online.Item2:X8}");
                            return Tuple.Create(ld, transport);
                        }
                        catch { /* not up yet / rom still enumerating */ }
                    }
                }
                _log($"      loader not found with arg=0x{arg:X4}, retrying with next mode ...");
            }
            return null;
        }

        private ScsiTransport _cur;
        private LoaderV2 _ld;

        /// <summary>Re-open the loader transport after a USB re-enumeration.</summary>
        private void Reacquire()
        {
            for (int i = 0; i < 40; i++)
            {
                foreach (var p in FindDevices())
                {
                    try
                    {
                        _cur?.Dispose();
                        _cur = new ScsiTransport(p);
                        _ld = new LoaderV2(_cur);
                        _ld.GetUsbBufferSize();     // sanity: loader still answering
                        return;
                    }
                    catch { }
                }
                Thread.Sleep(300);
            }
            throw new IOException("Lost contact with the loader");
        }

        /// <summary>Run a loader operation, re-acquiring the device on USB errors.</summary>
        private void Retry(Action<LoaderV2> op, int attempts = 4)
        {
            for (int a = 0; ; a++)
            {
                try
                {
                    if (_ld == null) Reacquire();
                    op(_ld);
                    return;
                }
                catch (IOException)
                {
                    _log($"      [retry {a + 1}] USB error, re-acquiring device ...");
                    _ld = null;
                    if (a >= attempts) throw;
                }
            }
        }

        /// <summary>Full flow: download image and reset.</summary>
        public void FlashImage(string devicePath, byte[] image, bool verify = true)
        {
            _cur = new ScsiTransport(devicePath);
            {
                var inquiry = UbootRom.Inquiry(_cur);
                _log($"Device: {inquiry.Item1} / {inquiry.Item2} / {inquiry.Item3}");

                var uboot = new UbootRom(_cur);

                _log("[1/5] Downloading wl82loader.bin into RAM ...");
                DownloadLoader(uboot);

                _log("[2/5] Jumping to loader ...");
                var up = JumpToLoader(uboot, null);
                if (up == null) throw new IOException("Loader did not come up after jump");
                _ld = up.Item1;

                var online = _ld.OnlineDevice();
                _log($"      flash id=0x{online.Item2:X8}");
                if (online.Item2 == 0)
                    throw new IOException("Flash ID is 0. Remove the short on the flash chip, then flash again.");

                // NOTE: no chipkey check; the image is already encrypted by the SDK tools.

                _log("[3/5] Erasing flash ...");
                uint eraseSize = (uint)((image.Length + 0xFFFF) & ~0xFFFF);
                for (uint addr = 0; addr < eraseSize; addr += 0x10000)
                {
                    uint a = addr;
                    byte r = 0xFF;
                    Retry(l => r = l.FlashEraseBlock(a));
                    _log($"      erase 0x{addr:X8} result={r}");
                }

                _log("[4/5] Writing image ...");
                int[] chunkSize = { Math.Max(64, Math.Min(512, _ld.GetUsbBufferSize())) };
                for (int off = 0; off < image.Length; off += chunkSize[0])
                {
                    int n = Math.Min(chunkSize[0], image.Length - off);
                    var chunk = new byte[n];
                    Array.Copy(image, off, chunk, 0, n);
                    int o = off;
                    Retry(l => l.FlashWrite((uint)o, chunk));
                    if (((off / chunkSize[0]) & 0x3F) == 0)
                        _log($"      0x{off:X8} / 0x{image.Length:X8}");
                }
                _log("      write done");

                if (verify)
                {
                    _log("[5/5] Verifying (FD05 read-back) ...");
                    int verified = 0;
                    for (int off = 0; off < image.Length; off += 512)
                    {
                        int n = Math.Min(512, image.Length - off);
                        byte[] back = null;
                        int o = off;
                        try { Retry(l => back = l.FlashRead((uint)o, n)); }
                        catch { _log($"      read-back stopped at 0x{off:X8}"); break; }
                        bool same = true;
                        for (int i = 0; i < n; i++) if (back[i] != image[off + i]) { same = false; break; }
                        if (!same) throw new IOException($"Verify failed at 0x{off:X8}");
                        verified += n;
                    }
                    _log(verified == image.Length ? "      verify OK" : $"      partial verify {verified}/{image.Length}");
                }
                else _log("[5/5] Verify skipped");

                try { _ld.RunApp(1); } catch { }
                _log("Done. Device reset.");
            }
        }


        /// <summary>
        /// Raw flash write: erase the covering region and write a raw binary
        /// (e.g. a full flash dump) at the given offset. No isd packaging.
        /// Tries a full chip-erase first, falls back to per-block erasing.
        /// </summary>
        public void WriteRaw(string devicePath, byte[] data, uint offset, bool verify)
        {
            _cur = new ScsiTransport(devicePath);
            var inquiry = UbootRom.Inquiry(_cur);
            _log($"Device: {inquiry.Item1} / {inquiry.Item2} / {inquiry.Item3}");

            var uboot = new UbootRom(_cur);
            _log("[1/4] Downloading wl82loader.bin into RAM ...");
            DownloadLoader(uboot);

            _log("[2/4] Jumping to loader ...");
            var up = JumpToLoader(uboot, null);
            if (up == null) throw new IOException("Loader did not come up after jump");
            _ld = up.Item1;

            int chunkSize = Math.Max(64, Math.Min(4096, _ld.GetUsbBufferSize()));

            _log("[3/4] Erasing ...");
            // NOTE: FB02 (chip erase) is a silent no-op on wl82loader - never trust it.
            uint eraseEnd = (uint)((offset + (uint)data.Length + 0xFFFF) & ~0xFFFF);
            for (uint addr = offset & ~0xFFFFu; addr < eraseEnd; addr += 0x10000)
            {
                uint a = addr;
                byte r = 0xFF;
                Retry(l => r = l.FlashEraseBlock(a));
                if (r != 0) _log($"      erase 0x{addr:X8} result={r}");
                if (((addr >> 16) & 0x1F) == 0) _log($"      erased 0x{addr:X8} ...");
            }
            _log("      erase done");

            _log($"[4/4] Writing {data.Length} bytes at 0x{offset:X8} ...");
            for (int off = 0; off < data.Length; off += chunkSize)
            {
                int n = Math.Min(chunkSize, data.Length - off);
                var chunk = new byte[n];
                Array.Copy(data, off, chunk, 0, n);
                int o = off;
                Retry(l => l.FlashWrite(offset + (uint)o, chunk));
                if (((off / chunkSize) & 0xFF) == 0)
                    _log($"      0x{(offset + off):X8} / 0x{(offset + data.Length):X8}");
            }
            _log("      write done");

            if (verify)
            {
                _log("Verifying (FD05 read-back) ...");
                int verified = 0;
                for (int off = 0; off < data.Length; off += 512)
                {
                    int n = Math.Min(512, data.Length - off);
                    byte[] back = null;
                    int o = off;
                    try { Retry(l => back = l.FlashRead(offset + (uint)o, n)); }
                    catch { _log($"      read-back stopped at 0x{(offset + off):X8}"); break; }
                    for (int i = 0; i < n; i++)
                        if (back[i] != data[off + i])
                            throw new IOException($"Verify failed at 0x{(offset + off):X8}");
                    verified += n;
                    if ((verified & 0xFFFFF) == 0) _log($"      verified {verified / 1024} KB");
                }
                _log(verified == data.Length ? "      verify OK" : $"      partial verify {verified}/{data.Length}");
            }
        }

        /// <summary>
        /// Download the loader, then read the chipkey (eFuse) via FC09.
        /// The key is MengLi-encrypted on the wire and decoded here.
        /// Decodes the authoritative raw capture (key at raw[7],[6] BE);
        /// the loader-path [5],[4] parse yields a wrong value (see note below).
        /// </summary>
        public Tuple<ushort, byte[]> ReadChipKey(string devicePath)
        {
            return ReadChipKeyImpl(devicePath, resetAfter: false);
        }

        /// <summary>Same as ReadChipKey but reboots the chip into its
        /// firmware afterwards (FC0C), leaving UBOOT loader mode.</summary>
        public Tuple<ushort, byte[]> ReadChipKeyAndReset(string devicePath)
        {
            return ReadChipKeyImpl(devicePath, resetAfter: true);
        }

        /// <summary>Download the loader and jump into it; returns the loader
        /// protocol handle and its USB transport (for MemWrite/FlashRead ...).
        /// Public helper so GUI code does not need reflection.</summary>
        public Tuple<LoaderV2, ScsiTransport> EnterLoader(string devicePath)
        {
            var dev = new ScsiTransport(devicePath);
            var inquiry = UbootRom.Inquiry(dev);
            _log($"Device: {inquiry.Item1} / {inquiry.Item2} / {inquiry.Item3}");
            var uboot = new UbootRom(dev);
            DownloadLoader(uboot);
            var up = JumpToLoader(uboot, null);
            if (up == null) throw new IOException("Loader did not come up after jump");
            return up;
        }

        /// <summary>Download loader and read a flash range via FD05
        /// (device stays in loader mode; pair with RunApp to reset).</summary>
        public byte[] ReadFlash(string devicePath, uint addr, int len)
        {
            using (var dev = new ScsiTransport(devicePath))
            {
                var inquiry = UbootRom.Inquiry(dev);
                _log($"Device: {inquiry.Item1} / {inquiry.Item2} / {inquiry.Item3}");
                var uboot = new UbootRom(dev);
                DownloadLoader(uboot);
                var up = JumpToLoader(uboot, null);
                if (up == null) throw new IOException("Loader did not come up after jump");
                var ld = up.Item1;
                var online = ld.OnlineDevice();
                _log($"      flash id=0x{online.Item2:X8}");
                var buf = new byte[len];
                for (int off = 0; off < len; off += 4096)
                {
                    int n = Math.Min(4096, len - off);
                    var part = ld.FlashRead(addr + (uint)off, n);
                    Array.Copy(part, 0, buf, off, n);
                    if (((off >> 12) & 0xF) == 0) _log($"      read 0x{addr + (uint)off:X8}");
                }
                return buf;
            }
        }

        private Tuple<ushort, byte[]> ReadChipKeyImpl(string devicePath, bool resetAfter)
        {
            ushort key;
            byte[] resp;
            using (var dev = new ScsiTransport(devicePath))
            {
                var inquiry = UbootRom.Inquiry(dev);
                _log($"Device: {inquiry.Item1} / {inquiry.Item2} / {inquiry.Item3}");

                var uboot = new UbootRom(dev);
                _log("[1/3] Downloading loader into RAM ...");
                DownloadLoader(uboot);

                _log("[2/3] Jumping to loader ...");
                var up = JumpToLoader(uboot, null);
                if (up == null) throw new IOException("Loader did not come up after jump");
                var ld = up.Item1;

                _log("[3/3] Reading chipkey (FC09) ...");
                // issue the raw command to capture the full 16-byte response
                var cdb = new byte[16];
                for (int i = 0; i < 16; i++) cdb[i] = 0xFF;
                cdb[0] = 0xFC; cdb[1] = 0x09;
                cdb[2] = 0x00; cdb[3] = 0xAC; cdb[4] = 0x69; cdb[5] = 0x00; // arg 0xAC6900 BE
                dev.Dispose(); // done with ROM transport; loader lives on its own
                resp = new byte[16];
                up.Item2.Execute(cdb, null, resp);
                _log($"      raw response: {BitConverter.ToString(resp)}");
                // key = MengLi-CrcCipher over raw[7],[6] (big-endian pair at offset 6).
                // NOTE: the loader-path ld.ReadChipKey() parses its own CmdExec
                // response at [5],[4] and yields a WRONG value (e.g. 0x7E2C);
                // the raw capture is authoritative (0xB165 on the Y20 unit).
                var bePair = new[] { resp[7], resp[6] };
                var dec = MengLi.CrcCipher(bePair);
                key = (ushort)(dec[0] | (dec[1] << 8));
                if (resetAfter)
                {
                    try { ld.RunApp(1); _log("      chip reset (FC0C)."); } catch { }
                }
            }
            return Tuple.Create(key, resp);
        }

        private byte[] LoadLoaderBlob()
        {
            if (_loaderBlob != null) return _loaderBlob;
            // search order: alongside exe, then known SDK/toolchain trees.
            // NOTE: br23 uses the kagaimiq usb-loader blob (11KB, MengLi-stored) -
            // the SDK's cpu/br23/tools/br23loader.bin (24KB) is the UART variant
            // and must NOT be used here.
            string name = _chip == JlChip.Br23 ? "br23loader.bin" : "wl82loader.bin";
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, name),
                name == "br23loader.bin"
                    ? @"J:\witerpaper\jl-uboot-tool\data\loaderblobs\usb\br23loader.bin"
                    : @"D:\fw-AC79_AIoT_SDK\cpu\wl82\tools\wl82loader.bin",
            };
            foreach (var p in candidates)
                if (File.Exists(p)) { _loaderBlob = File.ReadAllBytes(p); return _loaderBlob; }
            throw new FileNotFoundException($"{name} not found next to JlFlash.exe or in the known tool trees");
        }
    }
}
