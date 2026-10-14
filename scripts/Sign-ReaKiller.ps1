#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateScript({Test-Path $_ -PathType Container})][string]$BundlePath,
    [switch]$CreateCertificate,
    [switch]$TrustOnThisMachine
)
$ErrorActionPreference = "Stop"
$subject = "CN=ZH-Systems"
$bundle = (Resolve-Path $BundlePath).Path
$names = @("ReaKiller.exe", "ReaKiller.Desktop.exe")
foreach ($name in $names) {
    if (-not (Test-Path (Join-Path $bundle $name) -PathType Leaf)) {
        throw "Missing $name in $bundle. Download and extract the full desktop artifact first."
    }
}
$certs = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
    $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date)
} | Sort-Object NotAfter -Descending)
if ($CreateCertificate) {
    if ($certs.Count -gt 0) {
        Write-Host "Reusing existing certificate $($certs[0].Thumbprint)"
    } else {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
            -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 `
            -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(3) -KeyExportPolicy NonExportable
        $certs = @($cert)
        Write-Host "Created local, non-exportable code-signing certificate."
    }
}
if ($certs.Count -eq 0) {
    throw "No ZH-Systems code-signing certificate found. Run with -CreateCertificate."
}
$cert = $certs[0]
$cer = Join-Path $bundle "ZH-Systems-Public-CodeSigning.cer"
Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
Write-Host "Public certificate exported to $cer (contains NO private key)."
if ($TrustOnThisMachine) {
    Write-Warning "Trusting a self-signed publisher weakens the trust boundary on this Windows user profile. Only do this on PCs you control."
    $confirmation = Read-Host "Type TRUST to add the certificate to CurrentUser Trusted Root and Trusted Publishers"
    if ($confirmation -ne "TRUST") { throw "Trust operation cancelled." }
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
}
foreach ($name in $names) {
    $path = Join-Path $bundle $name
    $result = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256
    if ($result.Status -notin @("Valid", "UntrustedRoot")) {
        throw "Signing failed for $name : $($result.Status) $($result.StatusMessage)"
    }
    Write-Host "$name signed: $($result.Status)"
    $check = Get-AuthenticodeSignature -FilePath $path
    if ($check.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
        throw "Signature verification found an unexpected certificate on $name."
    }
}
Write-Host "Both files signed by $subject ($($cert.Thumbprint))."
Write-Warning "Other PCs will not trust this certificate automatically. Self-signing does not build public SmartScreen reputation."
