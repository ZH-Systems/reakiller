param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$Msi
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$msiPath = (Resolve-Path -LiteralPath $Msi).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase($msiPath, 0)
try {
    $expected = @{
        ProductName = 'Transflux'
        Manufacturer = 'ZH Systems'
        UpgradeCode = '{5D7C9311-54B1-4E3E-8398-1E7637097B25}'
    }
    foreach ($key in $expected.Keys) {
        $query = 'SELECT `Value` FROM `Property` WHERE `Property` = ''{0}''' -f $key
        $view = $db.OpenView($query)
        try {
            $view.Execute()
            $record = $view.Fetch()
            if ($null -eq $record -or $record.StringData(1) -ne $expected[$key]) {
                throw "Unexpected MSI property: $key"
            }
        } finally { $view.Close() }
    }
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) ('transflux-release-test-' + [guid]::NewGuid().ToString('N'))
try {
    $source = Join-Path $root 'source'
    $out = Join-Path $root 'csv'
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $source 'valid.xml') -Value '<root><row><id>1</id><value>sample</value></row></root>'
    Set-Content -LiteralPath (Join-Path $source 'invalid.xml') -Value '<!DOCTYPE root [<!ENTITY x "blocked">]><root><row>&x;</row></root>'
    $old = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $json = & $exe xml-to-csv $source --output $out --summary-json
        $code = $LASTEXITCODE
    } finally { $PSNativeCommandUseErrorActionPreference = $old }
    if ($code -ne 1) { throw "Expected partial failure exit 1, got $code" }
    $summary = $json | ConvertFrom-Json
    if ($summary.Succeeded -ne 1 -or $summary.Failed -ne 1 -or $summary.Skipped -ne 0) {
        throw 'Incorrect conversion summary'
    }
    if (!(Test-Path -LiteralPath (Join-Path $out 'valid.csv')) -or (Test-Path -LiteralPath (Join-Path $out 'invalid.csv'))) {
        throw 'Invalid output publication'
    }
    $preview = & $exe xml-to-csv (Join-Path $source 'valid.xml') --preview
    if ($LASTEXITCODE -ne 0) { throw 'Preview failed' }
    $rows = ConvertFrom-Json -InputObject ($preview -join '') -NoEnumerate
    if ($rows.Count -ne 2 -or $rows[1][0] -ne '1') { throw 'Unexpected preview' }
    Write-Output 'MSI identity and published CLI smoke checks passed.'
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
