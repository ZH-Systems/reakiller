using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Transflux.Core;

// Windows kernel job object; not a filesystem, identity or network sandbox.
public sealed class WindowsProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;
    private WindowsProcessJob(SafeFileHandle handle) => this.handle = handle;

    public static WindowsProcessJob Create(long memoryBytes, uint maxProcesses)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (memoryBytes <= 0 || maxProcesses == 0) throw new ArgumentOutOfRangeException();
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(); }
        try
        {
            var info = new ExtendedLimits
            {
                Basic = new BasicLimits { Flags = 0x2000 | 0x200 | 0x8, ActiveProcessLimit = maxProcesses },
                JobMemoryLimit = checked((UIntPtr)(ulong)memoryBytes)
            };
            if (!SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf<ExtendedLimits>()))
                throw new Win32Exception();
            return new WindowsProcessJob(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(handle, process.SafeHandle)) throw new Win32Exception();
    }
    public void Dispose() => handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint Flags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount,
            ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
}
