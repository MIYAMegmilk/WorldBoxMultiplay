# WMod autotest runner. Launches two WorldBox instances with env vars
# instructing the mod to auto-host/join, then waits for them to settle
# and diffs their state dumps.
param(
    [int]$DumpsToCollect = 5,
    [float]$DumpIntervalSec = 2.0,
    [int]$MaxStartupSec = 120
)

$ErrorActionPreference = 'Stop'

$Exe        = "C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox.exe"
$DumpDir    = Join-Path $env:TEMP "wmod_autotest"
$LogA       = Join-Path $env:TEMP "wmod_inst_A.log"
$LogB       = Join-Path $env:TEMP "wmod_inst_B.log"
$InitialSave = "$env:USERPROFILE\AppData\LocalLow\mkarpenko\WorldBox\saves\save12\map.wbox"

function Stop-WorldBox {
    Get-Process -Name "worldbox*" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
}

function Launch-Instance([string]$role, [string]$logPath) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.Arguments = "-logFile `"$logPath`""
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["WMOD_AUTOTEST"] = "1"
    $psi.EnvironmentVariables["WMOD_ROLE"] = $role
    $psi.EnvironmentVariables["WMOD_DUMP_DIR"] = $DumpDir
    $psi.EnvironmentVariables["WMOD_DUMP_INTERVAL"] = $DumpIntervalSec.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    if (Test-Path $InitialSave) { $psi.EnvironmentVariables["WMOD_INITIAL_SAVE"] = $InitialSave }
    $p = [System.Diagnostics.Process]::Start($psi)
    return $p
}

function Wait-ForLog([string]$logPath, [string]$pattern, [int]$timeoutSec, [string]$label) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $logPath) {
            $hit = Get-Content $logPath -ErrorAction SilentlyContinue | Select-String $pattern | Select-Object -Last 1
            if ($hit) {
                Write-Host "  [$label] $($hit.Line.Trim())"
                return $true
            }
        }
        Start-Sleep -Milliseconds 800
    }
    Write-Host "  [$label] TIMEOUT waiting for '$pattern'"
    return $false
}

function Parse-Dump([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $units = @{}
    $meta = @{}
    foreach ($line in Get-Content $path) {
        if ($line.StartsWith("#") -or [string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line.Contains("=")) {
            $kv = $line.Split("=", 2)
            $meta[$kv[0]] = $kv[1]
            continue
        }
        $parts = $line.Split(" ")
        if ($parts.Length -ne 3) { continue }
        $id = [long]$parts[0]
        $x = [float]$parts[1]
        $y = [float]$parts[2]
        $units[$id] = @{ x = $x; y = $y }
    }
    return @{ meta = $meta; units = $units }
}

function Compare-Dumps($dumpA, $dumpB) {
    if (-not $dumpA -or -not $dumpB) { return $null }
    $idsA = [System.Collections.Generic.HashSet[long]]::new()
    foreach ($k in $dumpA.units.Keys) { $idsA.Add([long]$k) | Out-Null }
    $idsB = [System.Collections.Generic.HashSet[long]]::new()
    foreach ($k in $dumpB.units.Keys) { $idsB.Add([long]$k) | Out-Null }

    $common = [System.Collections.Generic.HashSet[long]]::new($idsA)
    $common.IntersectWith($idsB)
    $onlyA = [System.Collections.Generic.HashSet[long]]::new($idsA)
    $onlyA.ExceptWith($idsB)
    $onlyB = [System.Collections.Generic.HashSet[long]]::new($idsB)
    $onlyB.ExceptWith($idsA)

    $totalDiff = 0.0
    $maxDiff = 0.0
    $exactMatches = 0
    foreach ($id in $common) {
        $ua = $dumpA.units[$id]; $ub = $dumpB.units[$id]
        $dx = $ua.x - $ub.x; $dy = $ua.y - $ub.y
        $d = [Math]::Sqrt($dx*$dx + $dy*$dy)
        $totalDiff += $d
        if ($d -gt $maxDiff) { $maxDiff = $d }
        if ($d -lt 0.01) { $exactMatches++ }
    }
    $avgDiff = if ($common.Count -gt 0) { $totalDiff / $common.Count } else { 0 }

    return @{
        countA       = $idsA.Count
        countB       = $idsB.Count
        common       = $common.Count
        onlyA        = $onlyA.Count
        onlyB        = $onlyB.Count
        exactMatches = $exactMatches
        avgDiff      = $avgDiff
        maxDiff      = $maxDiff
        worldTimeA   = $dumpA.meta["world_time"]
        worldTimeB   = $dumpB.meta["world_time"]
    }
}

# ===== Run =====
Write-Host "=== WMod AutoTest ==="
Write-Host "Dumps: $DumpsToCollect at ${DumpIntervalSec}s intervals"
Write-Host "Dump dir: $DumpDir"
Write-Host ""

Stop-WorldBox
if (Test-Path $DumpDir) { Remove-Item "$DumpDir\*.txt" -ErrorAction SilentlyContinue }
Remove-Item $LogA, $LogB -ErrorAction SilentlyContinue

Write-Host "Launching A (host)..."
$pA = Launch-Instance -role "host" -logPath $LogA
Write-Host "  PID $($pA.Id)"

# Wait a beat so NML's first-run setup doesn't race with B
Start-Sleep -Seconds 5

Write-Host "Launching B (client)..."
$pB = Launch-Instance -role "client" -logPath $LogB
Write-Host "  PID $($pB.Id)"

Write-Host ""
Write-Host "Waiting for A to reach Running..."
$okA = Wait-ForLog $LogA "AutoTest. host pushing initial snapshot" $MaxStartupSec "A"
Write-Host "Waiting for B to reach Running..."
$okB = Wait-ForLog $LogB "AutoTest. connected to" $MaxStartupSec "B"

if (-not ($okA -and $okB)) {
    Write-Host ""
    Write-Host "Setup failed. Last lines of each log:"
    Write-Host "--- A ---"
    if (Test-Path $LogA) { Get-Content $LogA -Tail 10 }
    Write-Host "--- B ---"
    if (Test-Path $LogB) { Get-Content $LogB -Tail 10 }
    Stop-WorldBox
    exit 1
}

$collectSec = $DumpsToCollect * $DumpIntervalSec + 5
Write-Host ""
Write-Host "Both peers running. Collecting for ${collectSec}s..."
Start-Sleep -Seconds $collectSec

# Read latest dumps
$dumpsA = Get-ChildItem $DumpDir -Filter "host_*.txt" -ErrorAction SilentlyContinue | Sort-Object Name
$dumpsB = Get-ChildItem $DumpDir -Filter "client_*.txt" -ErrorAction SilentlyContinue | Sort-Object Name
Write-Host "Dumps collected: A=$($dumpsA.Count), B=$($dumpsB.Count)"

if ($dumpsA.Count -eq 0 -or $dumpsB.Count -eq 0) {
    Write-Host "No dumps written. Check logs."
    Stop-WorldBox
    exit 1
}

Write-Host ""
Write-Host "=== Per-pair diff ==="
$pairs = [Math]::Min($dumpsA.Count, $dumpsB.Count)
for ($i = 0; $i -lt $pairs; $i++) {
    $a = Parse-Dump $dumpsA[$i].FullName
    $b = Parse-Dump $dumpsB[$i].FullName
    $r = Compare-Dumps $a $b
    if ($r) {
        $matchPct = if ($r.common -gt 0) { ($r.exactMatches * 100.0 / $r.common) } else { 0 }
        Write-Host ("  #{0,-3} A={1,-4} B={2,-4} common={3,-4} onlyA={4,-3} onlyB={5,-3} exactMatch={6,-4}({7,5:F1}%) avgDiff={8,6:F3} maxDiff={9,6:F2}" -f ($i+1), $r.countA, $r.countB, $r.common, $r.onlyA, $r.onlyB, $r.exactMatches, $matchPct, $r.avgDiff, $r.maxDiff)
    }
}

Stop-WorldBox
Write-Host ""
Write-Host "Done."
