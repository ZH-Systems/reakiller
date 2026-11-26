using System.Diagnostics;
using System.Security.Cryptography;

if (!OperatingSystem.IsWindows() || args.Length != 4 || args[0] != "--install")
    return 2;
if (!Path.IsPathFullyQualified(args[1])) return 2;
var msi = Path.GetFullPath(args[1]);
var expectedSha = args[2];
if (!int.TryParse(args[3], out var parentPid) || parentPid <= 0 ||
    expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit))
    return 2;
var self = Environment.ProcessPath;
if (self is null || !Path.GetFileName(self).Equals("ReaKiller.Updater.exe", StringComparison.OrdinalIgnoreCase) ||
    !Path.GetFileName(msi).Equals("ReaKiller-win-x64.msi", StringComparison.OrdinalIgnoreCase) ||
    !string.Equals(Path.GetDirectoryName(msi), Path.GetDirectoryName(self), StringComparison.OrdinalIgnoreCase))
    return 2;
try
{
    using var parent = Process.GetProcessById(parentPid);
    if (!parent.WaitForExit(60000)) return 3;
}
catch (ArgumentException) { /* Already exited. */ }

try
{
    // Keep a read-only handle open from verification until the installer exits.
    // Windows sharing rules deny subsequent write/delete opens while it is held.
    using var stream = new FileStream(msi, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (stream.Length > 512L * 1024 * 1024) return 4;
    var actual = Convert.ToHexString(SHA256.HashData(stream));
    if (!actual.Equals(expectedSha, StringComparison.OrdinalIgnoreCase)) return 5;

    // Require Windows to trust both signatures and require the same signing key.
    // Certificate/key rotation deliberately requires an independently verified manual update.
    var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    var signatureCheck = new ProcessStartInfo(shell)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
        "$ErrorActionPreference='Stop'; $s=Get-AuthenticodeSignature -LiteralPath $env:REAKILLER_MSI; " +
        "$u=Get-AuthenticodeSignature -LiteralPath $env:TRANSFLUX_UPDATER; " +
        "if($s.Status -ne 'Valid' -or $u.Status -ne 'Valid' -or !$s.SignerCertificate -or !$u.SignerCertificate){exit 1}; " +
        "if($s.SignerCertificate.GetPublicKeyString() -ceq $u.SignerCertificate.GetPublicKeyString() -and " +
        "$s.SignerCertificate.PublicKey.Oid.Value -ceq $u.SignerCertificate.PublicKey.Oid.Value){exit 0}; exit 1" })
        signatureCheck.ArgumentList.Add(arg);
    signatureCheck.Environment["REAKILLER_MSI"] = msi;
    signatureCheck.Environment["TRANSFLUX_UPDATER"] = self;
    using (var check = Process.Start(signatureCheck) ?? throw new IOException("Unable to validate signatures."))
    {
        var stdout = check.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = check.StandardError.BaseStream.CopyToAsync(Stream.Null);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await check.WaitForExitAsync(deadline.Token); }
        finally
        {
            if (!check.HasExited) check.Kill(entireProcessTree: true);
            await check.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
        if (check.ExitCode != 0) return 6;
    }
    var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
    { UseShellExecute = true, Verb = "runas" };
    foreach (var arg in new[] { "/i", msi, "/passive", "/norestart" }) info.ArgumentList.Add(arg);
    using var installer = Process.Start(info);
    if (installer is null) return 7;
    await installer.WaitForExitAsync();
    return installer.ExitCode;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException)
{ return 7; }
