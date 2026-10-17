using System.Threading;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using JlFlash.Flow;

namespace JlFlash
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return;
            }

            AllocConsole();
            RunCommand(args);
        }


        /// <summary>Decode the encrypted 16-bit efuse word from an FC09 response
        /// (reference convention: crc_cipher over resp[4:6] reversed).</summary>
        private static ushort DecodeKeyWord(byte[] resp)
        {
            var dec = Protocol.MengLi.CrcCipher(new[] { resp[5], resp[4] });
            return (ushort)(dec[0] | (dec[1] << 8));
        }

        private static void RunCommand(string[] args)
        {
            Action<string> log = s => Console.WriteLine(s);
            try
            {
                if (args.Length == 0 || args[0] == "list")
                {
                    log("Scanning for JieLi UBOOT devices ...");
                    var devs = FlashFlow.FindDevices();
                    if (devs.Count == 0) { log("none found (connect the board, or it is not in download mode)"); return; }
                    foreach (var d in devs) log($"  {d}");
                    return;
                }

                if (args[0] == "flash")
                {
                    // flash <jl_isd.bin> [--br23] [--no-verify]
                    string image = args.Length > 1 ? args[1] : "jl_isd.bin";
                    bool br23 = args.Contains("--br23");
                    bool verify = !args.Contains("--no-verify");
                    byte[] bin = File.ReadAllBytes(image);
                    log($"Image: {image} ({bin.Length} bytes), chip = {(br23 ? "BR23/AC695N" : "WL82/AC791N")}");

                    var flow = new FlashFlow(log);
                    flow.Chip = br23 ? JlChip.Br23 : JlChip.Wl82;
                    string dev = Flow.SramRun.EnterDownloadMode(flow, log, 20000);
                    log($"Device: {dev}");
                    flow.FlashImage(dev, bin, verify);
                    return;
                }

                if (args[0] == "testwrite")
                {
                    // write first 2 blocks, read back via FD07 (ROM returns the
                    // MengLi-encrypted view), compare against candidate ciphers
                    using (var dev = new JlFlash.Transport.ScsiTransport(args.Length > 1 ? args[1] : "\\\\.\\K:"))
                    {
                        var uboot = new JlFlash.Protocol.UbootRom(dev);
                        byte[] loader = File.ReadAllBytes(
                            Path.Combine(AppContext.BaseDirectory, "wl82loader.bin"));
                        var plain = new byte[512];
                        Array.Copy(loader, plain, 512);
                        var crcEnc = JlFlash.Protocol.MengLi.CrcCipher(plain);
                        var encEnc = JlFlash.Protocol.MengLi.EncCipher(plain);

                        uboot.MemWrite(0x1C02000, plain, encrypt: true); // crc_cipher on wire
                        var back = uboot.MemRead(0x1C02000, 512);

                        Console.WriteLine("readback first 32: " + BitConverter.ToString(back, 0, 32));
                        Console.WriteLine("plain          32: " + BitConverter.ToString(plain, 0, 32));
                        Console.WriteLine("crcEnc         32: " + BitConverter.ToString(crcEnc, 0, 32));
                        Console.WriteLine("encEnc         32: " + BitConverter.ToString(encEnc, 0, 32));
                        Console.WriteLine("readback == crcEnc(wire form)? " + back.SequenceEqual(crcEnc));
                        Console.WriteLine("readback == plain?             " + back.SequenceEqual(plain));
                        Console.WriteLine("readback == encEnc?            " + back.SequenceEqual(encEnc));
                        // also check the crcEnc decode of readback == plain
                        var dec = JlFlash.Protocol.MengLi.CrcCipher(back);
                        Console.WriteLine("CrcCipher(readback) == plain?  " + dec.SequenceEqual(plain));
                    }
                    return;
                }

                if (args[0] == "scan")
                {
                    for (char c = 'C'; c <= 'Z'; c++)
                    {
                        try
                        {
                            using (var dev = new JlFlash.Transport.ScsiTransport($"\\\\.\\{c}:"))
                            {
                                var info = JlFlash.Protocol.UbootRom.Inquiry(dev);
                                Console.WriteLine($"{c}:  vendor='{info.Item1}' product='{info.Item2}' rev='{info.Item3}'");
                            }
                        }
                        catch { }
                    }
                    return;
                }

                if (args[0] == "ramprobe")
                {
                    var flow = new FlashFlow(log);
                    string devPath = flow.WaitForDevice(5000);
                    var t = typeof(FlashFlow);
                    var dev = new JlFlash.Transport.ScsiTransport(devPath);
                    var uboot = new JlFlash.Protocol.UbootRom(dev);
                    t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { uboot });
                    var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                        t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { uboot, null });
                    var ld = up.Item1;
                    foreach (uint a in new uint[] { 0x1C40000, 0x1C60000, 0x1C70000, 0x1C7F000, 0x1C80000, 0x1CA0000, 0x1CC0000, 0x1D00000, 0x1E00000, 0x1F00000, 0x2000000, 0x2200000, 0x2300000 })
                    {
                        var data = new byte[256];
                        for (int i = 0; i < 256; i++) data[i] = (byte)(i ^ (a >> 8));
                        try
                        {
                            ld.MemWrite(a, data);
                            var back = ld.MemRead(a, 256);
                            log("0x" + a.ToString("X8") + ": " + (back.SequenceEqual(data) ? "OK" : "MISMATCH"));
                        }
                        catch (Exception ex) { log("0x" + a.ToString("X8") + ": " + ex.Message); break; }
                    }
                    return;
                }

                if (args[0] == "call")
                {
                    // Direct ROM execution: FB06 write (encrypted) -> FB08 call (returns) -> FD07 read result
                    // NO loader needed - pure UBOOT1.00 interface
                    string file = args[1];
                    uint addr = args.Length > 2 ? Convert.ToUInt32(args[2], 16) : 0x1C40000u;
                    uint outAddr = args.Length > 3 ? Convert.ToUInt32(args[3], 16) : 0x1C60000u;
                    int outLen = args.Length > 4 ? Convert.ToInt32(args[4]) : 256;
                    byte[] code = File.ReadAllBytes(file);
                    log($"payload {file} ({code.Length} B) -> 0x{addr:X8}, output at 0x{outAddr:X8}");

                    string devPath = FlashFlow.FindDevices().FirstOrDefault() ?? "\\\\.\\K:";
                    using (var dev = new JlFlash.Transport.ScsiTransport(devPath))
                    {
                        var uboot = new JlFlash.Protocol.UbootRom(dev);
                        var info = JlFlash.Protocol.UbootRom.Inquiry(dev);
                        log("Device: " + info.Item1 + " / " + info.Item2);

                        // FB06: write payload (MengLi-encrypted for wl82 ROM)
                        log("[1] Writing payload via FB06 (encrypted)...");
                        for (int off = 0; off < code.Length; off += 512)
                        {
                            int n = Math.Min(512, code.Length - off);
                            var chunk = new byte[n];
                            Array.Copy(code, off, chunk, 0, n);
                            uboot.MemWrite(addr + (uint)off, chunk, encrypt: true);
                        }
                        log("    " + code.Length + " bytes written");

                        // Verify write via FD07 (returns encrypted view)
                        var check = uboot.MemRead(addr, Math.Min(code.Length, 64));
                        var decCheck = JlFlash.Protocol.MengLi.CrcCipher(check);
                        log("[2] Write verify: " + (decCheck.SequenceEqual(code.Take(decCheck.Length).ToArray()) ? "OK" : "MISMATCH"));

                        // Clear output buffer
                        uboot.MemWrite(outAddr, new byte[16], encrypt: true);

                        // FB08: call the payload (ROM FB08 supports return)
                        log("[3] Calling 0x" + addr.ToString("X8") + " via FB08...");
                        try { uboot.MemJump(addr, 0); log("    jump returned (payload completed!)"); }
                        catch (Exception ex) { log("    jump: " + ex.Message); }

                        Thread.Sleep(100); // brief pause

                        // FD07: read output buffer (encrypted view)
                        log("[4] Reading output from 0x" + outAddr.ToString("X8") + "...");
                        var raw = uboot.MemRead(outAddr, outLen);
                        var decoded = JlFlash.Protocol.MengLi.CrcCipher(raw);
                        string text = System.Text.Encoding.ASCII.GetString(decoded);
                        log("OUTPUT:");
                        log(text.Replace(" ", ""));
                        log("--- END ---");
                    }
                    return;
                }

                if (args[0] == "ramprobe2")
                {
                    // Fine-grained SDRAM probe: write+verify at every 4KB boundary
                    var flow = new FlashFlow(log);
                    string devPath = flow.WaitForDevice(5000);
                    var t = typeof(FlashFlow);
                    var dev = new JlFlash.Transport.ScsiTransport(devPath);
                    var uboot = new JlFlash.Protocol.UbootRom(dev);
                    t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { uboot });
                    var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                        t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { uboot, null });
                    var ld = up.Item1;

                    log("Probing SDRAM availability every 4KB from 0x1C00000...");
                    uint sdramBase = 0x1C00000;
                    uint maxOk = 0;
                    uint firstBad = 0;
                    bool foundBad = false;

                    for (uint addr = sdramBase; addr < sdramBase + 0x800000; addr += 0x1000)
                    {
                        var data = new byte[64];
                        for (int i = 0; i < 64; i++) data[i] = (byte)((addr >> 4) ^ i);
                        try
                        {
                            ld.MemWrite(addr, data);
                            var back = ld.MemRead(addr, 64);
                            bool ok = back.SequenceEqual(data);
                            if (ok)
                            {
                                maxOk = addr + 0x1000;
                            }
                            else if (!foundBad)
                            {
                                firstBad = addr;
                                foundBad = true;
                                log("  MISMATCH at 0x" + addr.ToString("X8"));
                            }
                        }
                        catch
                        {
                            if (!foundBad)
                            {
                                firstBad = addr;
                                foundBad = true;
                                log("  ERROR at 0x" + addr.ToString("X8"));
                            }
                            break;
                        }
                    }

                    log("Results:");
                    log("  Usable range: 0x" + sdramBase.ToString("X8") + " - 0x" + maxOk.ToString("X8"));
                    log("  Usable size: " + ((maxOk - sdramBase) / 1024) + " KB");
                    if (foundBad) log("  First bad address: 0x" + firstBad.ToString("X8"));

                    // Also probe internal SRAM
                    log("Probing internal SRAM from 0x00000000...");
                    uint sramMax = 0;
                    for (uint addr = 0x100; addr < 0x100000; addr += 0x1000)
                    {
                        var data = new byte[64];
                        for (int i = 0; i < 64; i++) data[i] = (byte)(i ^ 0xAA);
                        try
                        {
                            ld.MemWrite(addr, data);
                            var back = ld.MemRead(addr, 64);
                            if (back.SequenceEqual(data)) sramMax = addr + 0x1000;
                        }
                        catch { break; }
                    }
                    log("  SRAM usable: 0x00000000 - 0x" + sramMax.ToString("X8") + " (" + (sramMax / 1024) + " KB)");
                    return;
                }

                if (args[0] == "fb06probe")
                {
                    // ladder test: find max FB06 data size the loader accepts
                    var flow = new FlashFlow(log);
                    string devPath = flow.WaitForDevice(5000);
                    var t = typeof(FlashFlow);
                    var dev = new JlFlash.Transport.ScsiTransport(devPath);
                    var uboot = new JlFlash.Protocol.UbootRom(dev);
                    t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { uboot });
                    var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                        t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { uboot, null });
                    var ld = up.Item1;
                    var transport = up.Item2;

                    foreach (int n in new[] { 2, 64, 128, 256, 384, 512 })
                    {
                        var data = new byte[n];
                        for (int i = 0; i < n; i++) data[i] = (byte)(i & 0xFF);
                        try
                        {
                            ld.MemWrite(0x1C40000, data);
                            var back = ld.MemRead(0x1C40000, n);
                            bool ok = back.SequenceEqual(data);
                            log("FB06 size " + n + ": " + (ok ? "write+verify OK" : "verify MISMATCH"));
                            if (!ok) break;
                        }
                        catch (Exception ex)
                        {
                            log("FB06 size " + n + ": FAILED - " + ex.Message);
                            break;
                        }
                    }
                    return;
                }

                if (args[0] == "loadrun")
                {
                    // load a raw payload into SDRAM via the LOADER (FB06) and jump (FB08).
                    // After a successful jump expect the USB device to disappear (payload owns the CPU).
                    string file = args[1];
                    uint addr = args.Length > 2 ? Convert.ToUInt32(args[2], 16) : 0x1C40000;
                    byte[] code = File.ReadAllBytes(file);
                    log("payload " + file + " (" + code.Length + " B) -> 0x" + addr.ToString("X8"));
                    var flow = new FlashFlow(log);
                    string devPath = flow.WaitForDevice(5000);
                    var t = typeof(FlashFlow);
                    var dev = new JlFlash.Transport.ScsiTransport(devPath);
                    var uboot = new JlFlash.Protocol.UbootRom(dev);
                    t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { uboot });
                    var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                        t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { uboot, null });
                    var ld = up.Item1;
                    var transport = up.Item2;

                    for (int off = 0; off < code.Length; off += 256)
                    {
                        int n = Math.Min(256, code.Length - off);
                        var chunk = new byte[n];
                        Array.Copy(code, off, chunk, 0, n);
                        ld.MemWrite(addr + (uint)off, chunk);
                    }
                    log("written, verifying via FD07 ...");
                    var back = new byte[code.Length];
                    for (int roff = 0; roff < code.Length; roff += 256)
                    {
                        int n = Math.Min(256, code.Length - roff);
                        var part = ld.MemRead(addr + (uint)roff, n);
                        Array.Copy(part, 0, back, roff, n);
                    }
                    bool same = back.SequenceEqual(code);
                    log("readback match: " + same);
                    if (!same) { log("ABORT: memory not written correctly"); return; }
                    log("jumping to 0x" + addr.ToString("X8") + " (device should go silent) ...");
                    try { ld.MemJump(addr); log("jump command returned"); }
                    catch (Exception ex) { log("jump: " + ex.Message); }
                    Thread.Sleep(1500);
                    var still = FlashFlow.FindDevices();
                    log(still.Count == 0 ? "device GONE -> payload is running!" : "device still present: " + string.Join(",", still));
                    return;
                }

                if (args[0] == "erasetest")
                {
                    // diagnose erase commands: erase then read back raw
                    var flow = new FlashFlow(log);
                    string devPath = flow.WaitForDevice(5000);
                    var t = typeof(FlashFlow);
                    using (var dev = new JlFlash.Transport.ScsiTransport(devPath))
                    {
                        var uboot = new JlFlash.Protocol.UbootRom(dev);
                        t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(flow, new object[] { uboot });
                        var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                            t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(flow, new object[] { uboot, null });
                        var ld = up.Item1;
                        var transport = up.Item2;

                        byte[] ReadN(uint a, int n)
                        {
                            var cdb = new byte[16];
                            for (int i = 0; i < 16; i++) cdb[i] = 0xFF;
                            cdb[0] = 0xFD; cdb[1] = 0x05;
                            JlFlash.Protocol.UbootRom.Put32(cdb, 2, a);
                            JlFlash.Protocol.UbootRom.Put16(cdb, 6, (ushort)n);
                            var r = new byte[n];
                            transport.Execute(cdb, null, r);
                            return r;
                        }

                        log("before erase, flash[0..8]: " + BitConverter.ToString(ReadN(0, 8)));
                        byte r1 = ld.FlashEraseBlock(0);
                        log("FB00 block erase result=" + r1 + ", flash[0..8]: " + BitConverter.ToString(ReadN(0, 8)));

                        // sector erase whole first 64K
                        var cdb2 = new byte[16];
                        for (int i = 0; i < 16; i++) cdb2[i] = 0xFF;
                        cdb2[0] = 0xFB; cdb2[1] = 0x02;  // chip erase
                        var resp2 = new byte[16];
                        try { transport.Execute(cdb2, null, resp2); log("FB02 chip erase resp: " + BitConverter.ToString(resp2)); }
                        catch (Exception ex) { log("FB02 failed: " + ex.Message); }
                        log("after FB02, flash[0..8]: " + BitConverter.ToString(ReadN(0, 8)));

                        // sector erase 4K
                        for (uint sa = 0; sa < 0x10000; sa += 0x1000)
                        {
                            var a2 = new byte[4];
                            JlFlash.Protocol.UbootRom.Put32(a2, 0, sa);
                            var cdb3 = new byte[16];
                            for (int i = 0; i < 16; i++) cdb3[i] = 0xFF;
                            cdb3[0] = 0xFB; cdb3[1] = 0x01;
                            Array.Copy(a2, 0, cdb3, 2, 4);
                            transport.Execute(cdb3, null, new byte[16]);
                        }
                        log("after FB01 sector-erase 64K, flash[0..8]: " + BitConverter.ToString(ReadN(0, 8)));
                    }
                    return;
                }

                if (args[0] == "readflash")
                {
                    // readflash <addrHex> <len> [outfile]
                    uint addr = Convert.ToUInt32(args[1], 16);
                    int len = Convert.ToInt32(args[2]);
                    var flow = new FlashFlow(log);
                    string devPath = flow.WaitForDevice(5000);
                    var t = typeof(FlashFlow);
                    using (var dev = new JlFlash.Transport.ScsiTransport(devPath))
                    {
                        var uboot = new JlFlash.Protocol.UbootRom(dev);
                        t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(flow, new object[] { uboot });
                        var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                            t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(flow, new object[] { uboot, null });
                        var back = up.Item1.FlashRead(addr, len);
                        for (int i = 0; i < Math.Min(len, 128); i += 16)
                            log((addr + i).ToString("X8") + "  " + BitConverter.ToString(back, i, 16));
                        if (args.Length > 3) File.WriteAllBytes(args[3], back);
                    }
                    return;
                }

                if (args[0] == "writeflash")
                {
                    // raw write: writeflash <file> [offsetHex] [--no-verify]
                    string file = args[1];
                    uint offset = args.Length > 2 ? Convert.ToUInt32(args[2], 16) : 0u;
                    bool verify = !(args.Contains("--no-verify"));
                    byte[] data = File.ReadAllBytes(file);
                    log($"Raw write: {file} ({data.Length} bytes) at 0x{offset:X8}");
                    var flow = new FlashFlow(log);
                    string dev = flow.WaitForDevice(5000);
                    flow.WriteRaw(dev, data, offset, verify);
                    return;
                }

                if (args[0] == "readmem")
                {
                    // FD07 read-memory directly in ROM mode (no loader needed)
                    // usage: readmem [addrHex] [len] [path]
                    uint addr = args.Length > 1 ? Convert.ToUInt32(args[1], 16) : 0x0u;
                    int len = args.Length > 2 ? Convert.ToInt32(args[2]) : 512;
                    using (var dev = new JlFlash.Transport.ScsiTransport(FlashFlow.FindDevices().FirstOrDefault() ?? "\\\\.\\K:"))
                    {
                        var uboot = new JlFlash.Protocol.UbootRom(dev);
                        var info = JlFlash.Protocol.UbootRom.Inquiry(dev);
                        log("Device: " + info.Item1 + " / " + info.Item2);
                        var data = uboot.MemRead(addr, len);
                        log("read " + len + " bytes at 0x" + addr.ToString("X8") + ":");
                        for (int i = 0; i < Math.Min(len, 128); i += 16)
                            log("  " + (addr + i).ToString("X8") + "  " + BitConverter.ToString(data, i, 16));
                        if (args.Length > 3) File.WriteAllBytes(args[3], data);
                        else if (len > 128) File.WriteAllBytes("readmem_" + addr.ToString("X8") + ".bin", data);
                    }
                    return;
                }

                if (args[0] == "dumploader")
                {
                    var flow = new FlashFlow(log);
                    string devPath = flow.WaitForDevice(5000);
                    var t = typeof(FlashFlow);
                    using (var dev = new JlFlash.Transport.ScsiTransport(devPath))
                    {
                        var uboot = new JlFlash.Protocol.UbootRom(dev);
                        t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(flow, new object[] { uboot });
                        var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                            t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(flow, new object[] { uboot, null });
                        var transport = up.Item2;

                        const uint baseAddr = 0x1C02000;

                        // probe read size first (loader may dislike 512)
                        int rdLen = 0;
                        foreach (int n in new[] { 512, 64, 4096, 5372, 1024 })
                        {
                            try
                            {
                                var cdb = new byte[16];
                                for (int i = 0; i < 16; i++) cdb[i] = 0xFF;
                                cdb[0] = 0xFD; cdb[1] = 0x07;
                                JlFlash.Protocol.UbootRom.Put32(cdb, 2, baseAddr);
                                JlFlash.Protocol.UbootRom.Put16(cdb, 6, (ushort)n);
                                var t0 = new byte[n];
                                transport.Execute(cdb, null, t0, 15);
                                rdLen = n;
                                log("FD07 works with length " + n + ", first bytes: " + BitConverter.ToString(t0, 0, 16));
                                break;
                            }
                            catch (Exception ex) { log("FD07 len=" + n + " failed: " + ex.Message); }
                        }
                        if (rdLen == 0) throw new IOException("loader FD07 read not working");

                        const int total = 0x18000;
                        var img = new byte[total];
                        for (int off = 0; off < total; off += rdLen)
                        {
                            int n = Math.Min(rdLen, total - off);
                            var cdb = new byte[16];
                            for (int i = 0; i < 16; i++) cdb[i] = 0xFF;
                            cdb[0] = 0xFD; cdb[1] = 0x07;
                            JlFlash.Protocol.UbootRom.Put32(cdb, 2, baseAddr + (uint)off);
                            JlFlash.Protocol.UbootRom.Put16(cdb, 6, (ushort)n);
                            var chunk = new byte[n];
                            transport.Execute(cdb, null, chunk, 15);
                            Array.Copy(chunk, 0, img, off, n);
                        }
                        string outPath = Path.Combine(AppContext.BaseDirectory, "wl82loader_ram.bin");
                        File.WriteAllBytes(outPath, img);
                        log("dumped " + total + " bytes -> " + outPath);
                    }
                    return;
                }

                if (args[0] == "probe")
                {
                    // download loader, then probe FC09 with various address args
                    // (maybe they select different efuse words) + FC16/FC0F-11
                    var flow = new FlashFlow(log);
                    string devPath = flow.WaitForDevice(5000);
                    var t = typeof(FlashFlow);
                    using (var dev = new JlFlash.Transport.ScsiTransport(devPath))
                    {
                        var uboot = new JlFlash.Protocol.UbootRom(dev);
                        t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(flow, new object[] { uboot });
                        var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                            t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Invoke(flow, new object[] { uboot, null });
                        var transport = up.Item2;

                        byte[] RawCmd(ushort cmd, byte[] a)
                        {
                            var cdb = new byte[16];
                            for (int i = 0; i < 16; i++) cdb[i] = 0xFF;
                            cdb[0] = (byte)(cmd >> 8); cdb[1] = (byte)cmd;
                            Array.Copy(a, 0, cdb, 2, a.Length);
                            var r = new byte[16];
                            transport.Execute(cdb, null, r);
                            return r;
                        }

                        log("FC09 with different address args:");
                        foreach (var arg in new uint[] { 0x0, 0x1, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8, 0xAC6900 })
                        {
                            var a = new byte[4];
                            JlFlash.Protocol.UbootRom.Put32(a, 0, arg);
                            try
                            {
                                var r = RawCmd(0xFC09, a);
                                ushort decoded = DecodeKeyWord(r);
                                log($"  arg=0x{arg:X8}: raw={BitConverter.ToString(r)} word=0x{decoded:X4}");
                            }
                            catch (Exception ex) { log($"  arg=0x{arg:X8}: {ex.Message}"); }
                        }

                        foreach (var cmd in new ushort[] { 0xFC16, 0xFC15, 0xFC0F, 0xFC10, 0xFC11 })
                        {
                            try
                            {
                                var r = RawCmd(cmd, new byte[0]);
                                log($"cmd {cmd:X4}: {BitConverter.ToString(r)}");
                            }
                            catch (Exception ex) { log($"cmd {cmd:X4}: {ex.Message}"); }
                        }
                    }
                    return;
                }

                if (args[0] == "readkey")
                {
                    var flow = new FlashFlow(log);
                    string dev = flow.WaitForDevice(5000);
                    log($"Device: {dev}");
                    var result = flow.ReadChipKey(dev);
                    log($"ChipKey (MengLi-decoded): 0x{result.Item1:X4}");
                    return;
                }

                if (args[0] == "runram")
                {
                    // SRAM-run flow: FB06 write payload into chip RAM, FB08 jump.
                    // NO flash erase/write and NO loader download - the existing
                    // flash content is never touched.
                    //
                    // usage: runram <payload.bin> [loadAddrHex=10000] [--arg=XXXX]
                    //                 [--mailbox=XXXXXXXX] [--enc] [--plain] [--no-verify]
                    string file = args[1];
                    uint loadAddr = Flow.SramRun.PayloadAddrDefault;
                    if (args.Length > 2 && !args[2].StartsWith("--"))
                        loadAddr = Convert.ToUInt32(args[2], 16);
                    ushort arg = 0;
                    uint mailbox = Flow.SramRun.MailboxAddrDefault;
                    bool noVerify = false;
                    int forcedEnc = 0; // 0=auto, 1=force encrypted wire, -1=force plain
                    foreach (var a in args)
                    {
                        if (a.StartsWith("--arg=")) arg = Convert.ToUInt16(a.Substring(6), 16);
                        else if (a.StartsWith("--mailbox=")) mailbox = Convert.ToUInt32(a.Substring(10), 16);
                        else if (a == "--enc") forcedEnc = 1;
                        else if (a == "--plain") forcedEnc = -1;
                        else if (a == "--no-verify") noVerify = true;
                    }
                    byte[] code = File.ReadAllBytes(file);
                    log($"payload {file} ({code.Length} B) -> RAM 0x{loadAddr:X8}, jump arg=0x{arg:X4}");
                    log("mode: ROM-only (FB06/FD07/FB08) - FLASH IS NEVER TOUCHED");

                    var flow = new FlashFlow(log);
                    string devPath = SramRun.EnterDownloadMode(flow, log, 20000);
                    SramRun.RunPayload(devPath, code, new SramRun.Options
                    {
                        LoadAddr = loadAddr,
                        JumpArg = arg,
                        Mailbox = mailbox,
                        ForcedEnc = forcedEnc,
                        NoVerify = noVerify,
                    }, log);
                    log("Done. FLASH content was NOT modified. Power-cycle the board to boot the original firmware.");
                    return;
                }

                if (args[0] == "kick")
                {
                    // force a running chip into UBOOT download mode
                    var flow = new FlashFlow(log);
                    var dev = Flow.SramRun.EnterDownloadMode(flow, log, 15000);
                    log(dev != null ? $"UBOOT device ready: {dev}" : "failed: no UBOOT device appeared (try power-cycling with the USB data cable plugged)");
                    return;
                }

                log("Usage:");
                log("  JlFlash list                 - scan for JieLi UBOOT devices");
                log("  JlFlash kick                 - force a running chip into UBOOT download mode");
                log("  JlFlash flash <jl_isd.bin>   - flash the image [--no-verify]");
                log("  JlFlash runram <payload.bin> [addrHex=10000] [--arg=XXXX] [--mailbox=XXXXXXXX]");
                log("                                 [--enc|--plain] [--no-verify]");
                log("                               - load payload into RAM and jump (NO flash access)");
                log("  JlFlash readkey              - read the eFuse chipkey via FC09");
                log("  JlFlash writeflash <f> [off] - raw write file into flash at offset");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
                Environment.ExitCode = 1;
            }
        }
    }
}
