$ErrorActionPreference = 'Stop'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$output = Join-Path $PSScriptRoot '小白-西高地桌面宠物.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '需要 Windows 内置的 .NET Framework 4.x。' }
$references = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')
$arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/codepage:65001', "/out:$output", "/win32manifest:$PSScriptRoot\app.manifest")
$homeIcon = Join-Path $PSScriptRoot 'assets\westie-home.ico'
$arguments += @("/win32icon:$homeIcon", "/resource:$homeIcon,WestiePet.HomeIcon")
foreach ($reference in $references) { $arguments += "/reference:$framework\$reference" }
foreach ($reference in @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'System.Xaml.dll')) {
    $path = Join-Path $framework "WPF\$reference"
    if (-not (Test-Path -LiteralPath $path)) { $path = Join-Path $framework $reference }
    $arguments += "/reference:$path"
}
$arguments += @("$PSScriptRoot\Program.cs", "$PSScriptRoot\WestieModel.cs", "$PSScriptRoot\SculptedCoat.cs", "$PSScriptRoot\PetWindow.cs")
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
Write-Host "构建完成：$output"
