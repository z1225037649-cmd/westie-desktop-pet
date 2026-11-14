param([switch]$RenderDetails)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x compiler was not found.' }
$frameworkPath = Split-Path -Parent $compilerPath
$commonArgs = @('/nologo', '/optimize+', '/codepage:65001', "/resource:$projectRoot\assets\westie-home.ico,WestiePet.HomeIcon")
foreach ($name in @('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Drawing.dll','System.Windows.Forms.dll')) {
    $commonArgs += "/reference:$frameworkPath\$name"
}
foreach ($name in @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','System.Xaml.dll')) {
    $assemblyPath = Join-Path $frameworkPath "WPF\$name"
    if (-not (Test-Path -LiteralPath $assemblyPath)) { $assemblyPath = Join-Path $frameworkPath $name }
    $commonArgs += "/reference:$assemblyPath"
}
$sources = @("$projectRoot\Program.cs", "$projectRoot\PetWindow.cs", "$projectRoot\WestieModel.cs", "$projectRoot\SculptedCoat.cs")
$inputPath = Join-Path $PSScriptRoot 'InputChecks.exe'
& $compilerPath ($commonArgs + @('/target:exe','/main:InputChecks',"/out:$inputPath") + $sources + @("$PSScriptRoot\InputChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Pointer input checks did not compile.' }
& $inputPath
if ($LASTEXITCODE -ne 0) { throw 'Pointer input checks failed.' }
$startupPath = Join-Path $PSScriptRoot 'StartupChecks.exe'
& $compilerPath ($commonArgs + @('/target:exe','/main:StartupChecks',"/out:$startupPath") + $sources + @("$PSScriptRoot\StartupChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Startup checks did not compile.' }
& $startupPath
if ($LASTEXITCODE -ne 0) { throw 'Startup checks failed.' }
$motionPath = Join-Path $PSScriptRoot 'MotionChecks.exe'
& $compilerPath ($commonArgs + @('/target:exe','/main:MotionChecks',"/out:$motionPath") + $sources + @("$PSScriptRoot\MotionChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Motion checks did not compile.' }
& $motionPath
if ($LASTEXITCODE -ne 0) { throw 'Motion checks failed.' }
$validationPath = Join-Path $PSScriptRoot 'WestiePet.Validation.exe'
& $compilerPath ($commonArgs + @('/target:winexe','/main:WestiePet.Program',"/out:$validationPath", "/win32manifest:$projectRoot\app.manifest") + $sources)
if ($LASTEXITCODE -ne 0) { throw 'Application validation did not compile.' }
$validationLog = Join-Path $PSScriptRoot 'qa\results.txt'
$validationStarted = Get-Date
$validation = Start-Process -FilePath $validationPath -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
if (Test-Path -LiteralPath $validationLog) { Get-Content -LiteralPath $validationLog }
if ($validation.ExitCode -ne 0) { throw "Application validation failed (exit $($validation.ExitCode))." }
if (-not (Test-Path -LiteralPath $validationLog) -or (Get-Item -LiteralPath $validationLog).LastWriteTime -lt $validationStarted) {
    throw 'Application validation did not produce a fresh result log.'
}
if ($RenderDetails) {
    $renderPath = Join-Path $PSScriptRoot 'RenderDetails.exe'
    & $compilerPath ($commonArgs + @('/target:exe','/main:RenderDetails',"/out:$renderPath") + $sources + @("$PSScriptRoot\RenderDetails.cs"))
    if ($LASTEXITCODE -ne 0) { throw 'Model renderer did not compile.' }
    & $renderPath --quick
    if ($LASTEXITCODE -ne 0) { throw 'Model render checks failed.' }
}
Write-Output 'All checks passed.'
