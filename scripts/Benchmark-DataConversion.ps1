param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [ValidateRange(1,1000)][int]$Files = 20,
    [ValidateRange(1,100000)][int]$Rows = 1000,
    [ValidateRange(1,16)][int[]]$Workers = @(1,2,4)
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('transflux-bench-' + [guid]::NewGuid().ToString('N'))
try {
    $source = Join-Path $root 'source'
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    for ($i=0; $i -lt $Files; $i++) {
        $writer = [System.IO.StreamWriter]::new((Join-Path $source "$i.xml"))
        try {
            $writer.WriteLine('<Records>')
            for ($j=0; $j -lt $Rows; $j++) { $writer.WriteLine("<Record><Id>$j</Id><Value>synthetic</Value></Record>") }
            $writer.WriteLine('</Records>')
        } finally { $writer.Dispose() }
    }
    foreach ($n in $Workers) {
        $out = Join-Path $root ("out-" + [guid]::NewGuid().ToString('N'))
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        & $exe xml-to-csv $source --output $out --threads $n | Out-Null
        $code = $LASTEXITCODE
        $clock.Stop()
        if ($code -ne 0) { throw "Benchmark conversion failed: $code" }
        $outputs = @(Get-ChildItem -LiteralPath $out -Filter '*.csv' -File)
        if ($outputs.Count -ne $Files) { throw 'Unexpected output count' }
        foreach ($file in $outputs) {
            $reader = [System.IO.StreamReader]::new($file.FullName)
            try {
                $header = $reader.ReadLine()
                if ($header -cne '"Id","Value"') {
                    throw "Unexpected CSV header in $($file.Name): $header"
                }
                $actualRows = 0
                while (($line = $reader.ReadLine()) -ne $null) {
                    $expected = '"{0}","synthetic"' -f $actualRows
                    if ($line -cne $expected) {
                        throw "Unexpected CSV data in $($file.Name) at record $($actualRows + 1): expected $expected; actual $line"
                    }
                    $actualRows++
                }
                if ($actualRows -ne $Rows) {
                    throw "Unexpected row count in $($file.Name) with $n workers: expected $Rows data rows, actual $actualRows"
                }
            } finally {
                $reader.Dispose()
            }
        }
        [pscustomobject]@{ Workers=$n; Files=$Files; Rows=($Files*$Rows); Seconds=[math]::Round($clock.Elapsed.TotalSeconds,3); RowsPerSecond=[math]::Round(($Files*$Rows)/[math]::Max(.001,$clock.Elapsed.TotalSeconds),0) }
    }
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
