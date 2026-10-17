using System;
using System.IO;
using System.Runtime.InteropServices;

namespace JlFlash.Transport
{
    /// <summary>
    /// Raw SCSI pass-through to a Mass-Storage volume (the JieLi chip in
    /// download mode shows up as a USB MSC device, "UBOOT..." product string).
    /// Port of kagaimiq/jl-uboot-tool scsiio/win32port.py.
    /// </summary>
    public sealed class ScsiTransport : IDisposable
    {
        private const uint IOCTL_SCSI_PASS_THROUGH_DIRECT = 0x4D014;
        private const byte SCSI_IOCTL_DATA_OUT = 0;
        private const byte SCSI_IOCTL_DATA_IN = 1;
        private const byte SCSI_IOCTL_DATA_UNSPECIFIED = 2;

        private readonly IntPtr _handle;

        [StructLayout(LayoutKind.Sequential)]
        private struct SCSI_PASS_THROUGH_DIRECT
        {
            public ushort Length;
            public byte Status;
            public byte PathId;
            public byte TargetId;
            public byte Lun;
            public byte CdbLength;
            public byte SenseLength;
            public byte DataIn;
            public uint DataTransferLength;
            public uint TimeOutValue;
            public IntPtr DataBuffer;
            public uint SenseInfoOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Cdb;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string name, uint access, uint share,
            IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr h, uint ioctl,
            IntPtr inBuf, uint inSize, IntPtr outBuf, uint outSize,
            out uint returned, IntPtr overlapped);

        public ScsiTransport(string path)
        {
            // path like \\.\E:  or  \\.\PhysicalDrive2
            _handle = CreateFileW(path, 0x80000000u | 0x40000000u /*GENERIC_READ|WRITE*/, 1 | 2,
                IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0, IntPtr.Zero);
            if (_handle == (IntPtr)(-1))
                throw new IOException($"Cannot open {path}: Win32 error {Marshal.GetLastWin32Error()}");
        }

        /// <summary>Send a CDB. Exactly one of dataOut/dataIn may be non-null.</summary>
        /// <returns>residue (bytes not transferred)</returns>
        public int Execute(byte[] cdb, byte[] dataOut, byte[] dataIn, int timeoutSec = 5)
        {
            if (cdb.Length > 16) throw new ArgumentException("CDB max 16 bytes");
            if (dataOut != null && dataIn != null) throw new ArgumentException("Only one direction allowed");

            var sptd = new SCSI_PASS_THROUGH_DIRECT();
            sptd.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>();
            sptd.PathId = 0;
            sptd.TargetId = 1;
            sptd.Lun = 0;
            sptd.TimeOutValue = (uint)timeoutSec;
            sptd.CdbLength = (byte)cdb.Length;
            sptd.Cdb = new byte[16];
            Array.Copy(cdb, sptd.Cdb, cdb.Length);

            byte direction;
            byte[] data;
            if (dataOut != null) { direction = SCSI_IOCTL_DATA_OUT; data = dataOut; }
            else if (dataIn != null) { direction = SCSI_IOCTL_DATA_IN; data = dataIn; }
            else { direction = SCSI_IOCTL_DATA_UNSPECIFIED; data = null; }
            sptd.DataIn = direction;

            int headerSize = Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>();
            int totalSize = headerSize + (data?.Length ?? 0);

            IntPtr buffer = Marshal.AllocHGlobal(totalSize);
            try
            {
                // struct then data buffer (DataBuffer must point into the same buffer)
                Marshal.StructureToPtr(sptd, buffer, false);
                if (data != null)
                {
                    Marshal.WriteInt32(buffer, (int)Marshal.OffsetOf<SCSI_PASS_THROUGH_DIRECT>("DataTransferLength"), data.Length);
                    IntPtr dataPtr = (IntPtr)((long)buffer + headerSize);
                    Marshal.WriteIntPtr(buffer, (int)Marshal.OffsetOf<SCSI_PASS_THROUGH_DIRECT>("DataBuffer"), dataPtr);
                    if (dataOut != null) Marshal.Copy(data, 0, dataPtr, data.Length);
                }

                bool ok = DeviceIoControl(_handle, IOCTL_SCSI_PASS_THROUGH_DIRECT,
                    buffer, (uint)totalSize, buffer, (uint)totalSize, out _, IntPtr.Zero);
                if (!ok)
                    throw new IOException($"SCSI transfer failed: Win32 error {Marshal.GetLastWin32Error()}");

                var sptdOut = Marshal.PtrToStructure<SCSI_PASS_THROUGH_DIRECT>(buffer);
                if (dataIn != null && sptdOut.DataTransferLength > 0)
                {
                    int n = (int)Math.Min(sptdOut.DataTransferLength, dataIn.Length);
                    IntPtr dataPtr = (IntPtr)((long)buffer + headerSize);
                    byte[] tmp = new byte[n];
                    Marshal.Copy(dataPtr, tmp, 0, n);
                    Array.Copy(tmp, dataIn, n);
                    return dataIn.Length - n;
                }
                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose() { if (_handle != IntPtr.Zero) CloseHandle(_handle); }
    }
}
