#requires -version 5.1
<#
    替音工具 — 一键构建可分发版本

    为什么构建逻辑放在 .ps1 而不是 .bat：
    cmd.exe 逐字节读取批处理文件，ANSI(GBK) 编码下某些汉字的尾字节是 0x5C（反斜杠），
    会被当成续行符，导致整个脚本解析错位（实测踩过：'https:' 被吃成 'ttps:'）。
    PowerShell 对 UTF-8 友好，所以中文逻辑全部放这里，.bat 只做纯 ASCII 转发。

    本文件必须以「UTF-8 带 BOM」保存：Windows PowerShell 5.1 在没有 BOM 时
    会按 ANSI 代码页读取 .ps1，中文会变成乱码。

    刻意不使用 here-string（@" ... "@）：在 PS 5.1 下它和编码问题叠加时
    会产生很难定位的解析错误，普通字符串更稳。
#>

[CmdletBinding()]
param(
    [switch]$SkipZip,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

# ────────────────────────────────────────────────────────────
# 编码自检：所有 .ps1 必须是「UTF-8 带 BOM」
#
# Windows PowerShell 5.1 在 .ps1 没有 BOM 时会按 ANSI 代码页读它，
# 中文全部变乱码，而报错信息完全指不到真正的原因 —— 只有一堆
# "Unexpected token / Missing closing '}'"，看起来像是脚本语法写错了。
#
# 这个坑在开发过程中踩过三次（编辑器、格式化工具、自动化改写都可能悄悄去掉 BOM），
# 每次都浪费时间去排查语法。所以在这里主动检查并直接修好。
# ────────────────────────────────────────────────────────────
foreach ($script in @('build.ps1', 'docs\生成测试素材.ps1')) {
    $full = Join-Path $PSScriptRoot $script
    if (-not (Test-Path $full)) { continue }

    $bytes = [IO.File]::ReadAllBytes($full)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF

    if (-not $hasBom) {
        Write-Host ('   [修复] ' + $script + ' 缺少 UTF-8 BOM，已自动补上') -ForegroundColor Yellow
        $text = [IO.File]::ReadAllText($full, [Text.Encoding]::UTF8)
        [IO.File]::WriteAllText($full, $text, (New-Object Text.UTF8Encoding($true)))
    }
}

$AppName   = '替音工具'
$DistDir   = Join-Path $PSScriptRoot 'dist'
$StageDir  = Join-Path $DistDir $AppName
$ToolsDir  = Join-Path $PSScriptRoot 'tools'
$FfmpegUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host ('-- ' + $Text) -ForegroundColor Cyan
}
function Write-Ok([string]$Text)    { Write-Host ('   [OK] ' + $Text) -ForegroundColor Green }
function Write-Warn2([string]$Text) { Write-Host ('   [!]  ' + $Text) -ForegroundColor Yellow }
function Fail([string]$Text) {
    Write-Host ('   [X]  ' + $Text) -ForegroundColor Red
    if (-not $NoPause) { Write-Host ''; Read-Host '按回车退出' }
    exit 1
}

Write-Host '============================================================'
Write-Host ('  ' + $AppName + ' - 一键构建可分发版本')
Write-Host '============================================================'

# ------------------------------------------------------------
Write-Step '1/5 检查 .NET SDK'
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Fail '没有找到 dotnet。请先安装 .NET 9 SDK：https://dotnet.microsoft.com/download/dotnet/9.0'
}
$dotnetVersion = (& dotnet --version) 2>&1 | Select-Object -First 1
Write-Ok ('.NET SDK ' + $dotnetVersion)

# ------------------------------------------------------------
Write-Step '2/5 准备 ffmpeg 与 ffprobe（查找顺序：tools\ -> PATH -> 自动下载）'

if (-not (Test-Path $ToolsDir)) { New-Item -ItemType Directory -Path $ToolsDir | Out-Null }

$ffmpegPath  = Join-Path $ToolsDir 'ffmpeg.exe'
$ffprobePath = Join-Path $ToolsDir 'ffprobe.exe'

if (-not (Test-Path $ffmpegPath)) {
    $found = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($found) {
        Write-Host ('   从 PATH 复制 ffmpeg: ' + $found.Source)
        Copy-Item $found.Source $ffmpegPath -Force
    }
}
if (-not (Test-Path $ffprobePath)) {
    $found = Get-Command ffprobe -ErrorAction SilentlyContinue
    if ($found) {
        Write-Host ('   从 PATH 复制 ffprobe: ' + $found.Source)
        Copy-Item $found.Source $ffprobePath -Force
    }
}

if (-not (Test-Path $ffmpegPath) -or -not (Test-Path $ffprobePath)) {
    Write-Host '   正在下载 ffmpeg essentials（约 30 MB）...'
    $zip = Join-Path $ToolsDir '_ffmpeg.zip'

    $downloaded = $false
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $FfmpegUrl -OutFile $zip -TimeoutSec 300
        $downloaded = $true
    } catch {
        Write-Warn2 ('下载失败：' + $_.Exception.Message)
    }

    if (-not $downloaded) {
        Fail ('请手动下载：' + $FfmpegUrl + "`n" +
              '       解压后把 bin\ffmpeg.exe 和 bin\ffprobe.exe 放进 tools\ 目录，然后重新运行本脚本。')
    }

    Write-Host '   正在解压...'
    $tmp = Join-Path $ToolsDir '_x'
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    Get-ChildItem $tmp -Directory | ForEach-Object {
        $b = Join-Path $_.FullName 'bin'
        if (Test-Path (Join-Path $b 'ffmpeg.exe'))  { Copy-Item (Join-Path $b 'ffmpeg.exe')  $ffmpegPath  -Force }
        if (Test-Path (Join-Path $b 'ffprobe.exe')) { Copy-Item (Join-Path $b 'ffprobe.exe') $ffprobePath -Force }
    }
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $ffmpegPath))  { Fail '缺少 tools\ffmpeg.exe' }
if (-not (Test-Path $ffprobePath)) { Fail '缺少 tools\ffprobe.exe' }

$ffSize  = (Get-Item $ffmpegPath).Length
$fpSize  = (Get-Item $ffprobePath).Length
Write-Ok ('ffmpeg  ' + $ffSize.ToString('N0') + ' 字节')
Write-Ok ('ffprobe ' + $fpSize.ToString('N0') + ' 字节')

# 快速验证能跑：下到残缺文件 / 被杀毒拦截的情况在这里就暴露出来
& $ffmpegPath -version 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Fail 'tools\ffmpeg.exe 存在但无法运行。可能被杀毒软件拦截，或者文件已损坏。'
}
$ffVersion = (& $ffmpegPath -version 2>&1 | Select-Object -First 1)
Write-Ok ('版本自检：' + $ffVersion)

# ------------------------------------------------------------
Write-Step '3/5 发布程序（自包含 win-x64，绿色目录不是单文件）'

if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }

& dotnet publish 'src\MkvAudioSwap.App\MkvAudioSwap.App.csproj' `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $StageDir `
    --nologo -v q

if ($LASTEXITCODE -ne 0) { Fail '发布失败。请检查上面的编译错误。' }
Write-Ok ('已发布到 ' + $StageDir)

# ------------------------------------------------------------
Write-Step '4/5 组装绿色目录'
$binDir = Join-Path $StageDir 'bin'
if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir | Out-Null }

Copy-Item $ffmpegPath  (Join-Path $binDir 'ffmpeg.exe')  -Force
Copy-Item $ffprobePath (Join-Path $binDir 'ffprobe.exe') -Force
Get-ChildItem $StageDir -Filter '*.pdb' -File | Remove-Item -Force -ErrorAction SilentlyContinue
Write-Ok 'bin\ffmpeg.exe 与 bin\ffprobe.exe 就位'

$exePath = Join-Path $StageDir ($AppName + '.exe')
if (-not (Test-Path $exePath)) { Fail ('没有生成 ' + $AppName + '.exe，发布结果不完整。') }

# ------------------------------------------------------------
Write-Step '5/5 打包 zip'

# zip 的名字刻意用 ASCII 的 Windows.zip，而不是跟着 AppName（替音工具.zip）：
#   1. GitHub Release 的资产名和 README 里的下载链接要保持一致，
#      改来改去早晚会对不上（曾经就对不上过）；
#   2. 中文资产名在 URL 里会被百分号转义，分享和引用都不方便。
# 用户解压后看到的仍然是 替音工具.exe —— 那只影响本地文件名，不影响下载。
$ZipName = 'Windows.zip'
$zipPath = Join-Path $DistDir $ZipName

if ($SkipZip) {
    Write-Warn2 '按参数要求跳过打包'
} else {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    try {
        Compress-Archive -Path (Join-Path $StageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
        $zipMb = (Get-Item $zipPath).Length / 1MB
        Write-Ok ($zipPath + '  (' + $zipMb.ToString('N1') + ' MB)')
    } catch {
        Write-Warn2 ('zip 打包失败：' + $_.Exception.Message)
        Write-Warn2 '绿色目录已经生成，可以直接分发文件夹。'
    }
}

# ------------------------------------------------------------
$totalMb = (Get-ChildItem $StageDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB

Write-Host ''
Write-Host '============================================================'
Write-Host '  🎉 构建完成' -ForegroundColor Green
Write-Host ''
Write-Host ('  绿色目录 : ' + $StageDir + '\   (' + $totalMb.ToString('N1') + ' MB)')
if (Test-Path $zipPath) { Write-Host ('  压缩包   : ' + $zipPath) }
Write-Host ''
Write-Host ('  用户拿到后：解压 -> 双击 ' + $AppName + '.exe')
Write-Host '  注意：bin\ 文件夹必须和 exe 在同一层，不要单独删除。'
Write-Host '============================================================'

if (-not $NoPause) {
    Write-Host ''
    if (Test-Path $DistDir) { & explorer.exe $DistDir }
    Read-Host '按回车退出'
}
