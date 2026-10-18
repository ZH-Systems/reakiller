param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $SourceDirectory).Path
$files = @(Get-ChildItem -Path $source -File -Recurse | Sort-Object FullName)
if ($files.Count -eq 0) { throw 'Bundle is empty.' }
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$lines.Add('  <Fragment><ComponentGroup Id="PublishedFiles">')
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($source, $file.FullName)
    if ($relative.Contains([System.IO.Path]::DirectorySeparatorChar)) {
        throw "Nested bundle paths need additional directory components: $relative"
    }
    $id = 'File_' + ([Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($relative)))).Substring(0,16)
    $escaped = [System.Security.SecurityElement]::Escape($file.FullName)
    $lines.Add("    <Component Id=`"$id`" Directory=`"INSTALLFOLDER`" Guid=`"*`"><File Id=`"F_$id`" Source=`"$escaped`" KeyPath=`"yes`" /></Component>")
}
$lines.Add('  </ComponentGroup></Fragment>')
$lines.Add('</Wix>')
[System.IO.File]::WriteAllLines($OutputFile, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated WiX payload for $($files.Count) files"
