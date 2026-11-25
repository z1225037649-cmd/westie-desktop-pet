param([switch]$RenderDetails, [switch]$VisualStudy, [switch]$SkipRegistryChecks, [switch]$GeneratedOnly, [switch]$InspectGenerated, [switch]$CorgiOnly, [switch]$FrameOnly, [switch]$CompileInteraction)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x compiler was not found.' }
$frameworkPath = Split-Path -Parent $compilerPath
$commonArgs = @('/nologo', '/optimize+', '/codepage:65001', "/resource:$projectRoot\assets\westie-home.ico,WestiePet.HomeIcon")
$commonArgs += "/resource:$projectRoot\assets\corgi-animation.frames,WestiePet.CorgiAnimation"
$commonArgs += "/resource:$projectRoot\assets\westie-generated.mesh,WestiePet.GeneratedMesh"
$commonArgs += "/resource:$projectRoot\assets\westie-generated.png,WestiePet.GeneratedTexture"
foreach ($name in @('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Drawing.dll','System.Windows.Forms.dll')) {
    $commonArgs += "/reference:$frameworkPath\$name"
}
foreach ($name in @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','System.Xaml.dll')) {
    $assemblyPath = Join-Path $frameworkPath "WPF\$name"
    if (-not (Test-Path -LiteralPath $assemblyPath)) { $assemblyPath = Join-Path $frameworkPath $name }
    $commonArgs += "/reference:$assemblyPath"
}
$sources = @("$projectRoot\Program.cs", "$projectRoot\PetWindow.cs", "$projectRoot\WestieModel.cs", "$projectRoot\GeneratedWestie.cs", "$projectRoot\SculptedCoat.cs", "$projectRoot\CorgiSprite.cs", "$projectRoot\CorgiFrames.cs")
if ($CompileInteraction) {
    $interactionPath = Join-Path $PSScriptRoot 'InteractionHarness.exe'
    & $compilerPath ($commonArgs + @('/target:winexe','/main:InteractionHarness',"/out:$interactionPath") + $sources + @("$PSScriptRoot\InteractionHarness.cs"))
    if ($LASTEXITCODE -ne 0) { throw 'Interaction harness did not compile.' }
    return
}
if ($FrameOnly) {
    $framePath = Join-Path $PSScriptRoot 'GeneratedFrameChecks.exe'
    & $compilerPath ($commonArgs + @('/target:exe','/main:GeneratedFrameChecks',"/out:$framePath") + $sources + @("$PSScriptRoot\GeneratedFrameChecks.cs"))
    if ($LASTEXITCODE -ne 0) { throw 'Generated frame checks did not compile.' }
    & $framePath
    if ($LASTEXITCODE -ne 0) { throw 'Generated frame checks failed.' }
    return
}
if ($CorgiOnly) {
    foreach ($entry in @('CorgiAnimationChecks','BreedChecks','ArtworkUiChecks')) {
        $checkPath = Join-Path $PSScriptRoot "$entry.exe"
        & $compilerPath ($commonArgs + @('/target:exe',"/main:$entry","/out:$checkPath") + $sources + @("$PSScriptRoot\$entry.cs"))
        if ($LASTEXITCODE -ne 0) { throw "$entry did not compile." }
        & $checkPath
        if ($LASTEXITCODE -ne 0) { throw "$entry failed." }
    }
    Write-Output 'All Corgi animation, breed and production UI checks passed.'
    return
}
if ($GeneratedOnly) {
    $generatedPath = Join-Path $PSScriptRoot 'GeneratedChecks.exe'
    & $compilerPath ($commonArgs + @('/target:exe','/main:GeneratedChecks',"/out:$generatedPath") + $sources + @("$PSScriptRoot\GeneratedChecks.cs"))
    if ($LASTEXITCODE -ne 0) { throw 'Generated checks did not compile.' }
    if ($InspectGenerated) { & $generatedPath --inspect } else { & $generatedPath }
    if ($LASTEXITCODE -ne 0) { throw 'Generated checks failed.' }
    $framePath = Join-Path $PSScriptRoot 'GeneratedFrameChecks.exe'
    & $compilerPath ($commonArgs + @('/target:exe','/main:GeneratedFrameChecks',"/out:$framePath") + $sources + @("$PSScriptRoot\GeneratedFrameChecks.cs"))
    if ($LASTEXITCODE -ne 0) { throw 'Generated frame checks did not compile.' }
    & $framePath
    if ($LASTEXITCODE -ne 0) { throw 'Generated frame checks failed.' }
    return
}
$inputPath = Join-Path $PSScriptRoot 'InputChecks.exe'
Write-Output 'Checking shared pointer gestures with the archived procedural silhouette; shipping silhouette is checked by GeneratedChecks.'
& $compilerPath ($commonArgs + @('/define:LEGACY_WESTIE_QA','/target:exe','/main:InputChecks',"/out:$inputPath") + $sources + @("$PSScriptRoot\InputChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Pointer input checks did not compile.' }
& $inputPath
if ($LASTEXITCODE -ne 0) { throw 'Pointer input checks failed.' }
$settingsPath = Join-Path $PSScriptRoot 'SettingsChecks.exe'
& $compilerPath ($commonArgs + @('/target:exe','/main:SettingsChecks',"/out:$settingsPath") + $sources + @("$PSScriptRoot\SettingsChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Settings checks did not compile.' }
& $settingsPath
if ($LASTEXITCODE -ne 0) { throw 'Settings checks failed.' }
if ($SkipRegistryChecks) { Write-Output 'SKIP: isolated registry startup checks explicitly skipped in this restricted environment.' }
else {
$startupPath = Join-Path $PSScriptRoot 'StartupChecks.exe'
& $compilerPath ($commonArgs + @('/target:exe','/main:StartupChecks',"/out:$startupPath") + $sources + @("$PSScriptRoot\StartupChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Startup checks did not compile.' }
& $startupPath
if ($LASTEXITCODE -ne 0) { throw 'Startup checks failed.' }
}
$motionPath = Join-Path $PSScriptRoot 'MotionChecks.exe'
# These two historical checks deliberately instantiate the archived sculpted
# appearance. GeneratedChecks below verifies the actual shipping Westie.
Write-Output 'Checking archived procedural Westie motion and eye research.'
& $compilerPath ($commonArgs + @('/define:LEGACY_WESTIE_QA','/target:exe','/main:MotionChecks',"/out:$motionPath") + $sources + @("$PSScriptRoot\MotionChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Motion checks did not compile.' }
& $motionPath
if ($LASTEXITCODE -ne 0) { throw 'Motion checks failed.' }
$eyePath = Join-Path $PSScriptRoot 'EyeChecks.exe'
& $compilerPath ($commonArgs + @('/define:LEGACY_WESTIE_QA','/target:exe','/main:EyeChecks',"/out:$eyePath") + $sources + @("$PSScriptRoot\EyeChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Eye checks did not compile.' }
& $eyePath
if ($LASTEXITCODE -ne 0) { throw 'Eye visibility and proportion checks failed.' }
$generatedPath = Join-Path $PSScriptRoot 'GeneratedChecks.exe'
Write-Output 'Checking the shipping generated Westie mesh, eyes, interactions and input silhouette.'
& $compilerPath ($commonArgs + @('/target:exe','/main:GeneratedChecks',"/out:$generatedPath") + $sources + @("$PSScriptRoot\GeneratedChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Generated Westie checks did not compile.' }
& $generatedPath
if ($LASTEXITCODE -ne 0) { throw 'Generated Westie rendering and interaction checks failed.' }
$framePath = Join-Path $PSScriptRoot 'GeneratedFrameChecks.exe'
& $compilerPath ($commonArgs + @('/target:exe','/main:GeneratedFrameChecks',"/out:$framePath") + $sources + @("$PSScriptRoot\GeneratedFrameChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Generated frame checks did not compile.' }
& $framePath
if ($LASTEXITCODE -ne 0) { throw 'Generated frame checks failed.' }
$breedPath = Join-Path $PSScriptRoot 'BreedChecks.exe'
$corgiPath = Join-Path $PSScriptRoot 'CorgiAnimationChecks.exe'
& $compilerPath ($commonArgs + @('/target:exe','/main:CorgiAnimationChecks',"/out:$corgiPath") + $sources + @("$PSScriptRoot\CorgiAnimationChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Corgi animation checks did not compile.' }
& $corgiPath
if ($LASTEXITCODE -ne 0) { throw 'Corgi frame playback or transparent input checks failed.' }
& $compilerPath ($commonArgs + @('/target:exe','/main:BreedChecks',"/out:$breedPath") + $sources + @("$PSScriptRoot\BreedChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Breed checks did not compile.' }
& $breedPath
if ($LASTEXITCODE -ne 0) { throw 'Breed rendering, switching or persistence checks failed.' }
$artworkUiPath = Join-Path $PSScriptRoot 'ArtworkUiChecks.exe'
& $compilerPath ($commonArgs + @('/target:exe','/main:ArtworkUiChecks',"/out:$artworkUiPath") + $sources + @("$PSScriptRoot\ArtworkUiChecks.cs"))
if ($LASTEXITCODE -ne 0) { throw 'Artwork UI checks did not compile.' }
& $artworkUiPath
if ($LASTEXITCODE -ne 0) { throw 'Artwork UI checks failed.' }
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
    Write-Output 'Rendering archived procedural Westie details; generated model views are in qa/generated.'
    $renderPath = Join-Path $PSScriptRoot 'RenderDetails.exe'
    & $compilerPath ($commonArgs + @('/define:LEGACY_WESTIE_QA','/target:exe','/main:RenderDetails',"/out:$renderPath") + $sources + @("$PSScriptRoot\RenderDetails.cs"))
    if ($LASTEXITCODE -ne 0) { throw 'Model renderer did not compile.' }
    & $renderPath --quick
    if ($LASTEXITCODE -ne 0) { throw 'Model render checks failed.' }
}
if ($VisualStudy) {
    Write-Output 'Rendering archived procedural appearance research; shipping Westie views are in qa/generated.'
    $studyPath = Join-Path $PSScriptRoot 'VisualStudy.exe'
    & $compilerPath ($commonArgs + @('/define:LEGACY_WESTIE_QA','/target:exe','/main:VisualStudy',"/out:$studyPath") + $sources + @("$PSScriptRoot\VisualStudy.cs"))
    if ($LASTEXITCODE -ne 0) { throw 'Visual study renderer did not compile.' }
    & $studyPath
    if ($LASTEXITCODE -ne 0) { throw 'Visual study render checks failed.' }
}
if ($SkipRegistryChecks) { Write-Output 'All requested non-registry checks passed; isolated startup registry checks were skipped.' }
else { Write-Output 'All checks passed.' }
