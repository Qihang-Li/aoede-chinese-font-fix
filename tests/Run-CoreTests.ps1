param(
    [Parameter(Mandatory = $true)]
    [string]$OriginalFontsDirectory,
    [switch]$Quick
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$originals = (Resolve-Path -LiteralPath $OriginalFontsDirectory).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AoEDE-FontFix-Tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 C# compiler was not found.' }
$testExe = Join-Path $testRoot 'CoreTests.exe'
$arguments = @(
    '/nologo', '/target:exe', '/platform:x64', ('/out:' + $testExe),
    '/r:System.Drawing.dll', '/r:System.Core.dll', '/r:System.Runtime.Serialization.dll',
    (Join-Path $project 'src\AtlasRenderer.cs'),
    (Join-Path $project 'src\PatchEngine.cs'),
    (Join-Path $PSScriptRoot 'CoreTests.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
$testArguments = @(
    (Join-Path $testRoot 'fixture-中文 path'),
    $originals,
    (Join-Path $project 'fonts\AoeCjkSans-Regular.ttf'),
    (Join-Path $project 'fonts\AoeCjkSans-Bold.ttf')
)
if ($Quick) { $testArguments += '--quick' }
& $testExe @testArguments 2>&1 | Tee-Object -FilePath (Join-Path $testRoot 'test-results.txt')
$result = $LASTEXITCODE
Write-Host ('Test files and results: ' + $testRoot)
if ($result -ne 0) { throw ('Core tests failed with exit code ' + $result) }
