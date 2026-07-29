using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Locker;

internal sealed class ProcessTreeGuard : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const int SigKill = 9;
    private const int NoSuchProcess = 3;

    private SafeFileHandle? jobHandle;
    private bool usesUnixProcessGroup;
    private int unixProcessGroupId;

    private ProcessTreeGuard()
    {
    }

    internal static ProcessTreeGuard Prepare(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        var guard = new ProcessTreeGuard();
        if (OperatingSystem.IsWindows())
        {
            guard.jobHandle = CreateKillOnCloseJob();
            return guard;
        }

        var setsidPath = FindSetsid();
        if (setsidPath is null)
        {
            return guard;
        }

        var executable = startInfo.FileName;
        var arguments = startInfo.ArgumentList.ToArray();
        startInfo.FileName = setsidPath;
        startInfo.ArgumentList.Clear();
        startInfo.ArgumentList.Add(executable);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        guard.usesUnixProcessGroup = true;
        return guard;
    }

    internal void Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (jobHandle is not null)
        {
            if (!AssignProcessToJobObject(jobHandle, process.Handle))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Locker CLI could not be assigned to a process job.");
            }
        }
        else if (usesUnixProcessGroup)
        {
            unixProcessGroupId = process.Id;
        }
    }

    internal void Terminate(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (jobHandle is not null)
        {
            _ = TerminateJobObject(jobHandle, 1);
        }
        else if (usesUnixProcessGroup && unixProcessGroupId > 0)
        {
            var result = Kill(-unixProcessGroupId, SigKill);
            if (result != 0 && Marshal.GetLastWin32Error() != NoSuchProcess)
            {
                // Fall through to the runtime's best-effort tree termination.
            }
        }

        try
        {
            // This remains useful on platforms without a process-group
            // launcher and also handles a root process not yet in the job.
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    public void Dispose()
    {
        jobHandle?.Dispose();
        jobHandle = null;
    }

    private static string? FindSetsid()
    {
        foreach (var candidate in new[] { "/usr/bin/setsid", "/bin/setsid" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static SafeFileHandle CreateKillOnCloseJob()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Locker CLI process job could not be created.");
        }

        var information = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };
        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(information, pointer, fDeleteOld: false);
            if (!SetInformationJobObject(
                    handle,
                    JobObjectExtendedLimitInformationClass,
                    pointer,
                    (uint)size))
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(
                    error,
                    "Locker CLI process job could not be configured.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(
        IntPtr jobAttributes,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        SafeFileHandle job,
        IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(
        SafeFileHandle job,
        uint exitCode);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }
}
