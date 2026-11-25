param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$output = Join-Path $PSScriptRoot '小白-西高地桌面宠物.exe'
$buildDirectory = Join-Path $PSScriptRoot ('bin\build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
$compiledOutput = Join-Path $buildDirectory (Split-Path -Leaf $output)
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 内置的 .NET Framework 4.x。' }
$references = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')
$arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/codepage:65001', "/out:$compiledOutput", "/win32manifest:$PSScriptRoot\app.manifest")
$homeIcon = Join-Path $PSScriptRoot 'assets\westie-home.ico'
$arguments += @("/win32icon:$homeIcon", "/resource:$homeIcon,WestiePet.HomeIcon")
$arguments += "/resource:$PSScriptRoot\assets\corgi-animation.frames,WestiePet.CorgiAnimation"
$arguments += "/resource:$PSScriptRoot\assets\westie-generated.mesh,WestiePet.GeneratedMesh"
$arguments += "/resource:$PSScriptRoot\assets\westie-generated.png,WestiePet.GeneratedTexture"
foreach ($reference in $references) { $arguments += "/reference:$framework\$reference" }
foreach ($reference in @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'System.Xaml.dll')) {
    $path = Join-Path $framework "WPF\$reference"
    if (-not (Test-Path -LiteralPath $path)) { $path = Join-Path $framework $reference }
    $arguments += "/reference:$path"
}
$arguments += @("$PSScriptRoot\Program.cs", "$PSScriptRoot\WestieModel.cs", "$PSScriptRoot\GeneratedWestie.cs", "$PSScriptRoot\SculptedCoat.cs", "$PSScriptRoot\CorgiSprite.cs", "$PSScriptRoot\CorgiFrames.cs", "$PSScriptRoot\PetWindow.cs")
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }

function Install-Executable([string]$Source, [string]$Destination) {
    $incoming = $Destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $backup = $null
    try {
        # Complete the copy before touching the old entry point. The final
        # same-volume rename cannot leave a partially copied executable.
        Copy-Item -LiteralPath $Source -Destination $incoming -ErrorAction Stop
        if (Test-Path -LiteralPath $Destination) {
            $backupDirectory = Join-Path (Split-Path -Parent $Destination) ('backup-running-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $backupDirectory | Out-Null
            $backup = Join-Path $backupDirectory (Split-Path -Leaf $Destination)
            Move-Item -LiteralPath $Destination -Destination $backup -ErrorAction Stop
        }
        try { Move-Item -LiteralPath $incoming -Destination $Destination -ErrorAction Stop }
        catch {
            if ($backup -and (Test-Path -LiteralPath $backup)) { Move-Item -LiteralPath $backup -Destination $Destination -ErrorAction Stop }
            throw
        }
    }
    finally { if (Test-Path -LiteralPath $incoming) { [System.IO.File]::Delete($incoming) } }
    if ($backup) {
        # A mapped old image stays available until its existing process exits.
        try { [System.IO.File]::Delete($backup); [System.IO.Directory]::Delete($backupDirectory) }
        catch [System.IO.IOException] { Write-Host "现有进程继续运行；旧映像保留：$backup" }
        catch [System.UnauthorizedAccessException] { Write-Host "旧映像保留：$backup" }
    }
}
Install-Executable $compiledOutput $output
Write-Host "构建完成：$output"

# Always refresh the shareable package after a successful compilation.
$dist = Join-Path $PSScriptRoot 'dist'
$packageName = "WestiePet-v$Version-windows"
$packageDirectory = Join-Path $dist $packageName
$archive = Join-Path $dist "$packageName.zip"
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
$packagedExecutable = Join-Path $packageDirectory (Split-Path -Leaf $output)
$packagedReadme = Join-Path $packageDirectory 'README.txt'
Install-Executable $compiledOutput $packagedExecutable
$readme = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'docs\portable-readme.txt') -Raw -Encoding UTF8
Set-Content -LiteralPath $packagedReadme -Value $readme.Replace('{{VERSION}}', $Version) -Encoding UTF8
# Explicit inputs keep personal settings, logs and QA files out of the archive.
Compress-Archive -LiteralPath @($packagedExecutable, $packagedReadme) -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$checksums = Join-Path $dist 'SHA256SUMS.txt'
Set-Content -LiteralPath $checksums -Value "$hash  $packageName.zip" -Encoding ASCII
$releaseNotes = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'docs\release-notes.md') -Raw -Encoding UTF8
Set-Content -LiteralPath (Join-Path $dist 'release-notes.md') -Value $releaseNotes.Replace('{{VERSION}}', $Version) -Encoding UTF8
# Replacing existing files does not refresh the folder date in Explorer.
(Get-Item -LiteralPath $packageDirectory).LastWriteTime = (Get-Item -LiteralPath $archive).LastWriteTime

# Verify the portable archive is limited to its two public files and matches the extracted copy.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    $expectedEntries = @((Split-Path -Leaf $packagedExecutable), 'README.txt') | Sort-Object
    $actualEntries = @($zip.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    if (Compare-Object -ReferenceObject $expectedEntries -DifferenceObject $actualEntries) { throw 'Portable ZIP contains unexpected or missing files.' }
    foreach ($entry in $zip.Entries) {
        $stream = $entry.Open()
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
        $source = if ($entry.FullName -eq 'README.txt') { $packagedReadme } else { $packagedExecutable }
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($entryHash -ne $sourceHash) { throw "Portable ZIP entry differs from extracted file: $($entry.FullName)" }
    }
} finally { $zip.Dispose() }
$expectedChecksum = "$hash  $packageName.zip"
if ((Get-Content -LiteralPath $checksums -Raw).Trim() -ne $expectedChecksum) { throw 'SHA256SUMS.txt does not match the portable ZIP.' }
Write-Host "解压目录已更新：$packageDirectory"
Write-Host "分享压缩包已更新：$archive"
[System.IO.File]::Delete($compiledOutput)
[System.IO.Directory]::Delete($buildDirectory)
