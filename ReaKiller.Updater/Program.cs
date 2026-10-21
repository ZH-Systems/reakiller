using System.Diagnostics;
using System.Security.Cryptography;

if (args.Length != 4 || args[0] != "--install")
    return 2;
var msi = Path.GetFullPath(args[1]);
var expectedSha = args[2];
if (!int.TryParse(args[3], out var parentPid) || parentPid <= 0 || expectedSha.Length != 64)
    return 2;
try
{
    using (var parent = Process.GetProcessById(parentPid))
        if (!parent.WaitForExit(60000)) return 3;
}
catch (ArgumentException) { /* Already exited. */ }
if (!File.Exists(msi)) return 4;
using (var stream = File.OpenRead(msi))
{
    var actual = Convert.ToHexString(SHA256.HashData(stream));
    if (!actual.Equals(expectedSha, StringComparison.OrdinalIgnoreCase)) return 5;
}
// Windows validates the publisher's Authenticode signature before running the installer.
// Do not auto-elevate or run unsigned or untrusted MSI packages.
var signatureCheck = new ProcessStartInfo("powershell.exe")
{
    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
    RedirectStandardError = true
};
signatureCheck.ArgumentList.Add("-NoProfile");
signatureCheck.ArgumentList.Add("-NonInteractive");
signatureCheck.ArgumentList.Add("-Command");
signatureCheck.Environment["REAKILLER_MSI"] = msi;
signatureCheck.ArgumentList.Add("$s=Get-AuthenticodeSignature -LiteralPath $env:REAKILLER_MSI; if($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match 'ZH Systems') {exit 0} else {exit 1}");
using (var check = Process.Start(signatureCheck)!)
{
    await check.WaitForExitAsync();
    if (check.ExitCode != 0) return 6;
}
var info = new ProcessStartInfo("msiexec.exe")
{
    UseShellExecute = true,
    Verb = "runas",
    Arguments = $"/i \"{msi}\" /passive /norestart"
};
try { Process.Start(info); return 0; }
catch (System.ComponentModel.Win32Exception) { return 7; }
