using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GrandiaModloader;

/// <summary>
/// Win32 LoadLibraryW injector (same bitness as grandia.exe — app is built x86).
/// </summary>
public static class DllInjector
{
    private const uint ProcessCreateThread = 0x0002;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessAll =
        ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation | ProcessVmWrite | ProcessVmRead;

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    public static int? FindProcessId(string processName)
    {
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                return process.Id;
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    public static async Task<int> WaitForProcessAsync(
        string processName,
        CancellationToken token,
        int pollMs = 1000,
        Action<string>? log = null)
    {
        log?.Invoke($"Waiting for process {processName} …");
        while (!token.IsCancellationRequested)
        {
            var pid = FindProcessId(processName);
            if (pid is int found)
            {
                log?.Invoke($"Found {processName} (pid {found}).");
                return found;
            }

            await Task.Delay(pollMs, token).ConfigureAwait(false);
        }

        token.ThrowIfCancellationRequested();
        throw new OperationCanceledException(token);
    }

    public static bool IsModuleLoaded(int processId, string moduleFileName, Action<string>? log = null)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var target = Path.GetFileName(moduleFileName);
            foreach (ProcessModule module in process.Modules)
            {
                if (string.Equals(module.ModuleName, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(module.FileName), target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Could not enumerate modules (will inject anyway): {ex.Message}");
        }

        return false;
    }

    public static void Inject(int processId, string dllAbsolutePath, Action<string>? log = null)
    {
        if (!File.Exists(dllAbsolutePath))
        {
            throw new FileNotFoundException("DLL not found", dllAbsolutePath);
        }

        var fullPath = Path.GetFullPath(dllAbsolutePath);
        log?.Invoke($"Injecting {fullPath} into pid {processId} …");

        TryEnableDebugPrivilege();

        var process = OpenProcess(ProcessAll, false, processId);
        if (process == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");
        }

        var pathBytes = Encoding.Unicode.GetBytes(fullPath + '\0');
        var remote = IntPtr.Zero;
        var thread = IntPtr.Zero;

        try
        {
            remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)pathBytes.Length, MemCommit | MemReserve,
                PageReadWrite);
            if (remote == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed");
            }

            if (!WriteProcessMemory(process, remote, pathBytes, (UIntPtr)pathBytes.Length, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed");
            }

            var kernel32 = GetModuleHandleW("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleHandleW(kernel32) failed");
            }

            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcAddress(LoadLibraryW) failed");
            }

            thread = CreateRemoteThread(process, IntPtr.Zero, UIntPtr.Zero, loadLibrary, remote, 0, out _);
            if (thread == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed");
            }

            if (WaitForSingleObject(thread, 60000) != 0)
            {
                throw new TimeoutException("Remote LoadLibraryW timed out");
            }

            if (!GetExitCodeThread(thread, out var exitCode))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeThread failed");
            }

            if (exitCode == 0)
            {
                throw new InvalidOperationException(
                    "LoadLibraryW returned NULL (bad path, wrong bitness, or missing VC++ runtime). " +
                    "Modloader and DLL must be Win32/x86 to match grandia.exe.");
            }

            if (exitCode == 193)
            {
                throw new InvalidOperationException(
                    "ERROR_BAD_EXE_FORMAT — DLL bitness does not match grandia.exe (need Win32/x86 DLL).");
            }

            log?.Invoke($"Injection succeeded (module handle=0x{exitCode:X8}).");
        }
        finally
        {
            if (thread != IntPtr.Zero)
            {
                CloseHandle(thread);
            }

            if (remote != IntPtr.Zero)
            {
                VirtualFreeEx(process, remote, UIntPtr.Zero, MemRelease);
            }

            CloseHandle(process);
        }
    }

    private static void TryEnableDebugPrivilege()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token))
            {
                return;
            }

            try
            {
                if (!LookupPrivilegeValueW(null, "SeDebugPrivilege", out var luid))
                {
                    return;
                }

                var tp = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = 0x00000002,
                };
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        UIntPtr nSize,
        out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr hProcess,
        IntPtr lpThreadAttributes,
        UIntPtr dwStackSize,
        IntPtr lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out Luid lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TokenPrivileges NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);
}
