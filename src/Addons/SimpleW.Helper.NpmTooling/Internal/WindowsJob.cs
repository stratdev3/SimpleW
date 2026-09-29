using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;


namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// A non-inherited kill-on-close job protects descendants even if the debugger kills C#.
    /// </summary>
    internal sealed class WindowsJob : IDisposable {
        private readonly SafeFileHandle _handle;

        /// <summary>
        /// Creates a non-inheritable job that terminates assigned processes when its last handle closes.
        /// </summary>
        /// <param name="process">Started supervisor to assign before it launches npm.</param>
        public WindowsJob(Process process) {
            _handle = CreateJobObject(IntPtr.Zero, null);

            if (_handle.IsInvalid) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try {
                ExtendedLimitInformation information = new();
                information.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                if (!SetInformationJobObject(_handle, 9, ref information, (uint)Marshal.SizeOf<ExtendedLimitInformation>())
                    || !AssignProcessToJobObject(_handle, process.Handle)
                ) {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot protect the npm process tree with a Windows Job Object.");
                }
            }
            catch { _handle.Dispose(); throw; }
        }

        /// <summary>
        /// Closes the job handle, terminating any remaining processes assigned to the job.
        /// </summary>
        public void Dispose() => _handle.Dispose();

        /// <summary>
        /// Matches the native basic job limits layout used to enable termination on handle closure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation {
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

        /// <summary>
        /// Matches the native I/O counters layout embedded in extended job limit information.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        /// <summary>
        /// Matches the native extended job limits layout passed to SetInformationJobObject.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        /// <summary>
        /// Creates or opens a Windows Job Object and returns its owned handle.
        /// </summary>
        /// <param name="attributes">Native security attributes pointer; zero uses defaults and a non-inheritable handle.</param>
        /// <param name="name">Optional job name; null creates an unnamed job.</param>
        /// <returns>A job handle, or an invalid handle when the native call fails.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
        
        /// <summary>
        /// Applies native limit information to a Windows Job Object.
        /// </summary>
        /// <param name="job">Job to configure.</param>
        /// <param name="informationClass">Native information class identifying the supplied structure.</param>
        /// <param name="information">Extended job limits to apply.</param>
        /// <param name="length">Size of the supplied structure in bytes.</param>
        /// <returns>True if the configuration succeeds; otherwise the native last error describes the failure.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimitInformation information, uint length);
        
        /// <summary>
        /// Assigns a process to a Windows Job Object so the job controls its lifetime.
        /// </summary>
        /// <param name="job">Job that will own the process.</param>
        /// <param name="process">Native process handle to assign.</param>
        /// <returns>True if assignment succeeds; otherwise the native last error describes the failure.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    }

}
