using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace BareBonesDownloader
{
    internal sealed class ProcessJob : IDisposable
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const int JobObjectBasicAccountingInformation = 1;
        private const uint KillOnJobClose = 0x00002000;
        private readonly object _gate = new object();
        private IntPtr _handle;

        public ProcessJob()
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a Windows process job.");

            ExtendedLimitInformation info = new ExtendedLimitInformation();
            info.BasicLimitInformation.LimitFlags = KillOnJobClose;
            int size = Marshal.SizeOf(typeof(ExtendedLimitInformation));
            IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, memory, false);
                if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, memory, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Could not configure process-tree cleanup.");
            }
            catch
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
                throw;
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        public void AssignProcess(Process process)
        {
            if (process == null)
                throw new ArgumentNullException("process");
            lock (_gate)
            {
                if (_handle == IntPtr.Zero)
                    throw new ObjectDisposedException("ProcessJob");
                if (!AssignProcessToJobObject(_handle, process.Handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Windows could not attach the downloader to its process job.");
            }
        }

        public void Terminate()
        {
            lock (_gate)
            {
                if (_handle == IntPtr.Zero)
                    return;
                if (!TerminateJobObject(_handle, 1))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 5 && error != 6)
                        throw new Win32Exception(error, "Could not stop the downloader process tree.");
                }
            }
        }

        public bool WaitUntilEmpty(int timeoutMilliseconds)
        {
            if (_handle == IntPtr.Zero)
                return true;

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow <= deadline)
            {
                AccountingInformation info;
                lock (_gate)
                {
                    if (_handle == IntPtr.Zero)
                        return true;
                    uint returned;
                    uint size = (uint)Marshal.SizeOf(typeof(AccountingInformation));
                    if (!QueryInformationJobObject(_handle, JobObjectBasicAccountingInformation,
                        out info, size, out returned))
                        throw new Win32Exception(Marshal.GetLastWin32Error(),
                            "Could not check whether the downloader process tree had stopped.");
                }

                if (info.ActiveProcesses == 0)
                    return true;
                Thread.Sleep(100);
            }
            return false;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_handle != IntPtr.Zero)
                {
                    CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccountingInformation
        {
            public long TotalUserTime;
            public long TotalKernelTime;
            public long ThisPeriodTotalUserTime;
            public long ThisPeriodTotalKernelTime;
            public uint TotalPageFaults;
            public uint TotalProcesses;
            public uint ActiveProcesses;
            public uint TotalTerminatedProcesses;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job, int informationClass, IntPtr information, uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(
            IntPtr job, int informationClass, out AccountingInformation information,
            uint informationLength, out uint returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
