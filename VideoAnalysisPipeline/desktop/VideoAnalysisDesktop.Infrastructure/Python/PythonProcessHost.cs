using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VideoAnalysisDesktop.Infrastructure.Python;

/// <summary>
/// Starts the Python engine with an explicit argument list and puts it in a
/// Windows Job Object. Disposing the host or cancelling a job terminates the
/// complete child process tree, including ffmpeg/demucs descendants.
/// </summary>
public sealed class PythonProcessHost : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private Process? _process;
    private SafeFileHandle? _jobHandle;
    private readonly TaskCompletionSource _stdoutCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stderrCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    public event DataReceivedEventHandler? OutputDataReceived;
    public event DataReceivedEventHandler? ErrorDataReceived;
    public event EventHandler? ProcessExited;

    public int? ExitCode => _process is { HasExited: true } process ? process.ExitCode : null;
    public int? ProcessId => _process is null ? null : _process.Id;
    public bool IsRunning => _process is { HasExited: false };

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(
            SafeFileHandle hJob,
            JobObjectInfoType infoType,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(SafeFileHandle hJob, IntPtr hProcess);
    }

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
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
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public long IoReadOperationCount;
        public long IoWriteOperationCount;
        public long IoOtherOperationCount;
        public long IoReadTransferCount;
        public long IoWriteTransferCount;
        public long IoOtherTransferCount;
        public long ProcessMemoryLimit;
        public long JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    /// <summary>
    /// Starts a single process. Callers must pass each argument as an individual
    /// item; this deliberately avoids the unsafe string-arguments overload.
    /// </summary>
    public void Start(
        string executable,
        IReadOnlyList<string> argumentList,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(argumentList);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (_process is not null)
            throw new InvalidOperationException("This PythonProcessHost has already started a process.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PythonProcessHost requires Windows Job Objects.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in argumentList)
            startInfo.ArgumentList.Add(argument);

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;
        }

        try
        {
            _jobHandle = CreateKillOnCloseJobObject();
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += OnOutputDataReceived;
            _process.ErrorDataReceived += OnErrorDataReceived;
            _process.Exited += OnProcessExited;

            if (!_process.Start())
                throw new InvalidOperationException($"Could not start Python engine: {executable}");

            if (!NativeMethods.AssignProcessToJobObject(_jobHandle, _process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not assign Python engine to its Job Object.");

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch
        {
            CleanupFailedStart();
            throw;
        }
    }

    /// <summary>
    /// Waits for process exit and both redirected streams. The latter is
    /// essential: Process.Exited can fire before the final stdout/stderr lines
    /// have reached the host's subscribers.
    /// </summary>
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var process = _process ?? throw new InvalidOperationException("The process has not been started.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(_stdoutCompleted.Task, _stderrCompleted.Task)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return process.ExitCode;
    }

    public void Cancel() => Kill(entireProcessTree: true);

    public void Kill(bool entireProcessTree = true)
    {
        var process = _process;
        if (process is null || process.HasExited)
            return;

        try
        {
            process.Kill(entireProcessTree);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }

    private static SafeFileHandle CreateKillOnCloseJobObject()
    {
        var handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create Windows Job Object.");

        var info = new JobObjectExtendedLimitInformation
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
            Marshal.StructureToPtr(info, pointer, false);
            if (!NativeMethods.SetInformationJobObject(
                    handle,
                    JobObjectInfoType.ExtendedLimitInformation,
                    pointer,
                    (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure Windows Job Object.");
            }
        }
        catch
        {
            handle.Dispose();
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        return handle;
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            _stdoutCompleted.TrySetResult();
        else
            OutputDataReceived?.Invoke(this, e);
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            _stderrCompleted.TrySetResult();
        else
            ErrorDataReceived?.Invoke(this, e);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    private void CleanupFailedStart()
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort; the original start error is more useful.
            }

            _process.Dispose();
            _process = null;
        }

        _jobHandle?.Dispose();
        _jobHandle = null;
        _stdoutCompleted.TrySetResult();
        _stderrCompleted.TrySetResult();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            Kill(entireProcessTree: true);
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _jobHandle?.Dispose();
            _jobHandle = null;
            _stdoutCompleted.TrySetResult();
            _stderrCompleted.TrySetResult();
        }
    }
}
