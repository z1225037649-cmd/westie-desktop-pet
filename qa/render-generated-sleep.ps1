param([switch]$Neck)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkPath = Split-Path -Parent $compilerPath
$compilerArgs = @('/nologo', '/optimize+', '/codepage:65001', '/target:exe', '/main:GeneratedChecks', "/out:$PSScriptRoot\GeneratedChecks.exe")
foreach ($resource in @('westie-home.ico,WestiePet.HomeIcon', 'corgi-animation.frames,WestiePet.CorgiAnimation', 'westie-generated.mesh,WestiePet.GeneratedMesh', 'westie-generated.png,WestiePet.GeneratedTexture')) {
    $compilerArgs += "/resource:$projectRoot\assets\$resource"
}
foreach ($name in @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')) {
    $compilerArgs += "/reference:$frameworkPath\$name"
}
foreach ($name in @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'System.Xaml.dll')) {
    $assemblyPath = Join-Path $frameworkPath "WPF\$name"
    if (-not (Test-Path -LiteralPath $assemblyPath)) { $assemblyPath = Join-Path $frameworkPath $name }
    $compilerArgs += "/reference:$assemblyPath"
}
foreach ($name in @('Program.cs', 'PetWindow.cs', 'WestieModel.cs', 'GeneratedWestie.cs', 'SculptedCoat.cs', 'CorgiSprite.cs', 'CorgiFrames.cs')) {
    $compilerArgs += "$projectRoot\$name"
}
& $compilerPath ($compilerArgs + @("$PSScriptRoot\GeneratedChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Sleep diagnostic did not compile.' }
if ($Neck) { & "$PSScriptRoot\GeneratedChecks.exe" --sleep-neck-view }
else { & "$PSScriptRoot\GeneratedChecks.exe" --sleep-view }
if ($LASTEXITCODE -ne 0) { throw 'Sleep diagnostic did not render.' }
