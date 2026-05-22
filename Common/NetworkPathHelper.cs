using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamesDatabase.Api.Common;

/// <summary>
/// Authenticates UNC network paths on Windows.
/// Strategy: 1) cached access, 2) WNetAddConnection2, 3) WNetCancelConnection2+retry, 4) net use subprocess.
/// No-op on non-Windows platforms (Docker uses mounted volumes with host-level auth).
/// </summary>
public static class NetworkPathHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpLocalName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpRemoteName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpComment;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NETRESOURCE lpNetResource,
        string? lpPassword,
        string? lpUserName,
        int dwFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(
        string lpName,
        int dwFlags,
        bool fForce);

    private const int RESOURCETYPE_DISK = 1;
    private const int CONNECT_TEMPORARY = 4;
    private const int ERROR_ALREADY_ASSIGNED = 85;
    private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
    private const int NO_ERROR = 0;

    /// <summary>
    /// Authenticates to the UNC share so subsequent Directory.Exists / file I/O calls succeed.
    /// Tries four strategies in order: cached access, WNetAddConnection2, credential-conflict
    /// resolution (cancel + retry), and finally a 'net use' subprocess.
    /// </summary>
    /// <returns>Null on success, diagnostic error message on failure.</returns>
    public static string? EnsureAuthenticated(string networkPath, string? username, string? password)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null; // On Linux (Docker), the volume is mounted with host-level credentials.

        // Strategy 1: maybe the path is already accessible (cached Windows credentials).
        if (Directory.Exists(networkPath))
            return null;

        var uncRoot = GetUncRoot(networkPath);
        if (uncRoot is null)
            return $"Could not parse UNC root from '{networkPath}'";

        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        {
            // Strategy 2: WNetAddConnection2 with explicit credentials.
            var wnetResult = TryWNetConnect(uncRoot, username, password);
            if (wnetResult == NO_ERROR || wnetResult == ERROR_ALREADY_ASSIGNED)
                return null;

            // Strategy 3: credential conflict — cancel the existing session then retry.
            if (wnetResult == ERROR_SESSION_CREDENTIAL_CONFLICT)
            {
                WNetCancelConnection2(uncRoot, 0, true);
                wnetResult = TryWNetConnect(uncRoot, username, password);
                if (wnetResult == NO_ERROR || wnetResult == ERROR_ALREADY_ASSIGNED)
                    return null;
            }

            // Strategy 4: fall back to 'net use' subprocess.
            var netUseError = TryNetUse(uncRoot, username, password);
            if (netUseError == null)
                return null;

            return $"All auth strategies failed for '{uncRoot}'. " +
                   $"WNetAddConnection2 code={wnetResult}; net use error: {netUseError}";
        }

        // No credentials configured — let Windows use cached ones.
        return Directory.Exists(networkPath)
            ? null
            : $"Path '{networkPath}' not accessible and no credentials configured.";
    }

    private static int TryWNetConnect(string uncRoot, string username, string password)
    {
        var resource = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpRemoteName = uncRoot,
            lpLocalName = null,
        };
        return WNetAddConnection2(ref resource, password, username, CONNECT_TEMPORARY);
    }

    private static string? TryNetUse(string uncRoot, string username, string password)
    {
        try
        {
            var args = $"/c net use \"{uncRoot}\" \"{password}\" /user:\"{username}\" /persistent:no 2>&1";
            var psi = new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "Failed to start cmd.exe";
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(8000);
            return proc.ExitCode == 0 ? null : $"net use exited {proc.ExitCode}: {output.Trim()}";
        }
        catch (Exception ex)
        {
            return $"net use exception: {ex.Message}";
        }
    }

    private static string? GetUncRoot(string path)
    {
        // Normalise both \\server\share and //server/share forms
        var normalised = path.Replace('/', '\\');
        if (!normalised.StartsWith(@"\\"))
            return null;

        var parts = normalised.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        return $@"\\{parts[0]}\{parts[1]}";
    }
}
