param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist\AoEDE-Chinese-Font-Fix-0.1.0'),
    [switch]$SkipFontCheck
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$buildOutput = [IO.Path]::GetFullPath($OutputDirectory)
if ($buildOutput.TrimEnd('\') -eq $repoRoot.TrimEnd('\')) {
    throw 'Choose a separate build output directory, not the repository root.'
}
if ((Test-Path -LiteralPath $buildOutput) -and @(Get-ChildItem -LiteralPath $buildOutput -Force).Count -gt 0) {
    throw 'Choose an empty output directory. Stale build files or local logs must not enter a release package.'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw '.NET Framework 4.x C# compiler was not found. Build on 64-bit Windows with .NET Framework 4.8 installed.'
}
foreach ($fontName in @('AoeCjkSans-Regular.ttf', 'AoeCjkSans-Bold.ttf')) {
    if (-not $SkipFontCheck -and -not (Test-Path -LiteralPath (Join-Path $repoRoot "fonts\$fontName") -PathType Leaf)) {
        throw "Missing bundled font: $fontName. See the font preparation instructions."
    }
}
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
if ($sourceFiles.Count -lt 3) { throw 'The source directory is incomplete.' }
New-Item -ItemType Directory -Path $buildOutput -Force | Out-Null
$exePath = Join-Path $buildOutput 'AoEDE-Font-Fix.exe'
$compileArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/debug-', '/utf8output', "/out:$exePath", '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Runtime.Serialization.dll') + $sourceFiles
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
$configuration = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
  <runtime>
    <AppContextSwitchOverrides value="Switch.System.IO.UseLegacyPathHandling=false;Switch.System.IO.BlockLongPaths=false" />
  </runtime>
</configuration>
'@
[IO.File]::WriteAllText((Join-Path $buildOutput 'AoEDE-Font-Fix.exe.config'), $configuration, [Text.UTF8Encoding]::new($false))
foreach ($folderName in @('fonts', 'licenses')) {
    $sourceFolder = Join-Path $repoRoot $folderName
    if (Test-Path -LiteralPath $sourceFolder -PathType Container) {
        $targetFolder = Join-Path $buildOutput $folderName
        New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
        Get-ChildItem -LiteralPath $sourceFolder -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $targetFolder $_.Name) -Force
        }
    }
}
foreach ($fileName in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CHANGELOG.md', 'FONT-NOTICES.md')) {
    $sourcePath = Join-Path $repoRoot $fileName
    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) { Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $buildOutput $fileName) -Force }
}
foreach ($docName in @('USER-GUIDE.zh-CN.md', 'SECURITY.md')) {
    $sourcePath = Join-Path $repoRoot "docs\$docName"
    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) { Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $buildOutput $docName) -Force }
}
$docOutput = Join-Path $buildOutput 'docs'
New-Item -ItemType Directory -Path $docOutput -Force | Out-Null
foreach ($docName in @('BUILD.md', 'TESTING.md', 'USER-GUIDE.zh-CN.md', 'SECURITY.md', 'SCREENSHOT-NOTICE.md')) {
    $sourcePath = Join-Path $repoRoot "docs\$docName"
    if (Test-Path -LiteralPath $sourcePath -PathType Leaf) { Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $docOutput $docName) -Force }
}
$quickStart = @'
AoEDE 中文字体修复 0.1.0（非官方社区预览版）

1. 完整解压，保留 fonts 和 licenses 文件夹。
2. 关闭游戏，等待 Steam 更新结束。
3. 打开 AoEDE-Font-Fix.exe，检查游戏目录。
4. 预览默认字体（也可选择有转换许可的静态 TTF），然后应用。
5. 等待完成提示后再打开游戏。

撤销：关闭游戏，在工具中点击“恢复原样”。
不要删除游戏目录下的 AoEDE-Chinese-Font-Fix-backup 备份。
未知资源或冲突会被拒绝，请勿强制覆盖。

只支持已验证资源的第一代决定版 Windows Steam 版（1017900）。
详细使用方法见 USER-GUIDE.zh-CN.md；开源与许可见 README.md、LICENSE、FONT-NOTICES.md。
'@
[IO.File]::WriteAllText((Join-Path $buildOutput '开始使用.txt'), $quickStart, [Text.UTF8Encoding]::new($true))
$hashLines = Get-ChildItem -LiteralPath $buildOutput -File -Recurse | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object {
    $relativeName = $_.FullName.Substring($buildOutput.TrimEnd('\').Length + 1).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relativeName
}
[IO.File]::WriteAllLines((Join-Path $buildOutput 'SHA256SUMS.txt'), [string[]]$hashLines, [Text.UTF8Encoding]::new($false))
Write-Output "Build complete: $exePath"
Write-Output 'The compiler output is not claimed to be bit-for-bit reproducible. Inspect the public source and build steps.'
