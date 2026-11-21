[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0'
)

# Read-only audit. This script never builds, extracts, rewrites or publishes files.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$root = Split-Path -Parent $PSScriptRoot
$executableName = '小白-西高地桌面宠物.exe'
$packageName = "WestiePet-v$Version-windows"
$dist = Join-Path $root 'dist'
$packageDirectory = Join-Path $dist $packageName
$archive = Join-Path $dist "$packageName.zip"
$rootExecutable = Join-Path $root $executableName
$packageExecutable = Join-Path $packageDirectory $executableName
$packageReadme = Join-Path $packageDirectory 'README.txt'
$checksums = Join-Path $dist 'SHA256SUMS.txt'
$releaseNotes = Join-Path $dist 'release-notes.md'
$readmeTemplate = Join-Path $root 'docs\portable-readme.txt'
$notesTemplate = Join-Path $root 'docs\release-notes.md'
$checks = New-Object 'System.Collections.Generic.List[object]'

function Add-Check {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    $status = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "[$status] ${Name}: $Detail"
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-ExpandedTemplate {
    param([string]$Name, [string]$Template, [string]$Actual)
    try {
        $source = [System.IO.File]::ReadAllText($Template)
        $text = [System.IO.File]::ReadAllText($Actual)
        # Set-Content adds a final newline; tolerate only terminal CR/LF differences.
        $expected = $source.Replace('{{VERSION}}', $Version).TrimEnd([char[]]"`r`n")
        $content = $text.TrimEnd([char[]]"`r`n")
        $expanded = $source.Contains('{{VERSION}}') -and
            -not [regex]::IsMatch($text, '\{\{[^\r\n]*?\}\}') -and
            [string]::Equals($expected, $content, [StringComparison]::Ordinal)
        Add-Check $Name $expanded "Must equal the UTF-8 template expanded with version $Version (apart from terminal newlines)."
    } catch {
        Add-Check $Name $false $_.Exception.Message
    }
}

$requiredFiles = @($rootExecutable, $packageExecutable, $packageReadme, $archive,
    $checksums, $releaseNotes, $readmeTemplate, $notesTemplate)
$missing = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
Add-Check 'Required files' ($missing.Count -eq 0) $(if ($missing.Count) { $missing -join '; ' } else { 'All required files exist.' })
if (-not (Test-Path -LiteralPath $packageDirectory -PathType Container)) {
    throw "Portable directory is missing: $packageDirectory"
}
if ($missing.Count) { throw 'Portable audit failed: required files are missing.' }

# Capture metadata so a concurrent rebuild cannot silently mix audit generations.
$initialMetadata = @{}
foreach ($path in $requiredFiles) {
    $item = Get-Item -LiteralPath $path
    $initialMetadata[$path] = "$($item.Length):$($item.LastWriteTimeUtc.Ticks)"
}
$rootHash = Get-Sha256 $rootExecutable
$packageHash = Get-Sha256 $packageExecutable
$readmeHash = Get-Sha256 $packageReadme
$archiveHash = Get-Sha256 $archive
Add-Check 'Root and portable EXE' ($rootHash -ceq $packageHash) "Root SHA-256=$rootHash; portable SHA-256=$packageHash"

$zipExecutableHash = $null
$zipReadmeHash = $null
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entries = @($zip.Entries)
        $exeEntries = @($entries | Where-Object { $_.FullName -ceq $executableName })
        $readmeEntries = @($entries | Where-Object { $_.FullName -ceq 'README.txt' })
        $exactContents = $entries.Count -eq 2 -and $exeEntries.Count -eq 1 -and $readmeEntries.Count -eq 1
        Add-Check 'ZIP allowlist' $exactContents ("Exactly two root entries required: {0}, README.txt. Found: {1}" -f $executableName, (($entries | ForEach-Object { $_.FullName }) -join ', '))
        foreach ($entry in $entries) {
            if (($entry.FullName -ceq $executableName -and $exeEntries.Count -eq 1) -or
                ($entry.FullName -ceq 'README.txt' -and $readmeEntries.Count -eq 1)) {
                $stream = $entry.Open()
                $sha = [System.Security.Cryptography.SHA256]::Create()
                try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
                finally { $stream.Dispose(); $sha.Dispose() }
                if ($entry.FullName -ceq $executableName) { $zipExecutableHash = $hash }
                else { $zipReadmeHash = $hash }
            }
        }
    } finally { $zip.Dispose() }
    Add-Check 'Three EXE copies' ($null -ne $zipExecutableHash -and $rootHash -ceq $packageHash -and $rootHash -ceq $zipExecutableHash) "ZIP EXE SHA-256=$zipExecutableHash"
    Add-Check 'Portable and ZIP README bytes' ($null -ne $zipReadmeHash -and $readmeHash -ceq $zipReadmeHash) "Portable SHA-256=$readmeHash; ZIP SHA-256=$zipReadmeHash"
} catch {
    Add-Check 'ZIP readable' $false $_.Exception.Message
}

$checksumLines = @(Get-Content -LiteralPath $checksums | Where-Object { $_.Trim().Length -gt 0 })
$checksumMatches = $checksumLines.Count -eq 1 -and
    $checksumLines[0] -cmatch ('^(?i:' + [regex]::Escape($archiveHash) + ')  ' + [regex]::Escape("$packageName.zip") + '$')
Add-Check 'SHA256SUMS' $checksumMatches "Expected a single record: $archiveHash  $packageName.zip"
Test-ExpandedTemplate 'Portable README version/template' $readmeTemplate $packageReadme
Test-ExpandedTemplate 'Release notes version/template' $notesTemplate $releaseNotes

$folderTime = (Get-Item -LiteralPath $packageDirectory).LastWriteTimeUtc
$zipTime = (Get-Item -LiteralPath $archive).LastWriteTimeUtc
$exeTime = (Get-Item -LiteralPath $packageExecutable).LastWriteTimeUtc
$readmeTime = (Get-Item -LiteralPath $packageReadme).LastWriteTimeUtc
$latestPublicTime = @($zipTime, $exeTime, $readmeTime) | Sort-Object -Descending | Select-Object -First 1
# Allow two seconds for filesystems with coarser timestamp precision. Later folder
# changes from normal use are valid; timestamps alone cannot prove build provenance.
$folderFresh = $folderTime -ge $latestPublicTime.AddSeconds(-2)
Add-Check 'Portable directory timestamp' $folderFresh ("Directory UTC={0:o}; ZIP UTC={1:o}; latest public file UTC={2:o}; tolerance=2s." -f $folderTime, $zipTime, $latestPublicTime)

$extraFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Force |
    Where-Object { $_.Name -cne $executableName -and $_.Name -cne 'README.txt' } |
    ForEach-Object { $_.Name })
if ($extraFiles.Count) {
    Write-Host ('[INFO] Existing portable-directory extras were left untouched (ZIP contents are checked separately): ' + ($extraFiles -join ', '))
}

# Freshness is deliberately separate from package consistency: even an older
# source timestamp or identical EXE copies cannot establish what source was built.
$sourceInputs = @('Program.cs', 'WestieModel.cs', 'SculptedCoat.cs', 'CorgiSprite.cs', 'assets\corgi-hd.png', 'PetWindow.cs',
    'app.manifest', 'build.ps1', 'assets\westie-home.ico')
$rootTime = (Get-Item -LiteralPath $rootExecutable).LastWriteTimeUtc
$newerSources = @($sourceInputs | ForEach-Object {
    $path = Join-Path $root $_
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        if ((Get-Item -LiteralPath $path).LastWriteTimeUtc -gt $rootTime) { $_ }
    }
})
Write-Host '[INFO] Source packaging status: UNVERIFIED. Passing checks establish only consistency of files currently on disk.'
if ($newerSources.Count) {
    Write-Warning ('Application/build inputs newer than the root EXE: ' + ($newerSources -join ', ') + '. Rebuild in the main workflow before claiming these changes are packaged.')
}

$changedDuringAudit = @($requiredFiles | Where-Object {
    if (-not (Test-Path -LiteralPath $_ -PathType Leaf)) { $true }
    else {
        $item = Get-Item -LiteralPath $_
        $initialMetadata[$_] -cne "$($item.Length):$($item.LastWriteTimeUtc.Ticks)"
    }
})
Add-Check 'Stable audit inputs' ($changedDuringAudit.Count -eq 0) $(if ($changedDuringAudit.Count) { 'Files changed during verification; rerun after the build finishes: ' + ($changedDuringAudit -join '; ') } else { 'Required file sizes and modification times stayed unchanged during verification.' })
$failures = @($checks | Where-Object { -not $_.Passed })
[pscustomobject]@{
    Success = $failures.Count -eq 0
    Version = $Version
    Archive = $archive
    ExeSha256 = $rootHash
    ZipSha256 = $archiveHash
    DirectoryLastWriteTimeUtc = $folderTime
    SourcePackagingStatus = 'UNVERIFIED'
    InputsNewerThanExe = $newerSources
    PreservedDirectoryExtras = $extraFiles
    Checks = $checks.ToArray()
}
if ($failures.Count) { throw "Portable audit failed: $($failures.Count) check(s). No release files were changed." }
Write-Host '[PASS] Current portable files are consistent. This does not certify that current source changes have been built.'
