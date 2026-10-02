# 生成一批"故意带问题"的测试文件
#
# 目的：让你不用自己剪素材就能把工具的每条分支都走一遍。
# 每个文件针对一个具体的故障模式，文件名直接说明它测什么。
#
# 用法（在仓库里直接跑，不需要任何参数）：
#     powershell -ExecutionPolicy Bypass -File docs\生成测试素材.ps1
#
# 默认行为：
#   · 素材输出到 <仓库根>\测试素材\
#   · ffmpeg / ffprobe 依次从 tools\、dist\替音工具\bin\、系统 PATH 里找
# 全部路径都由脚本自身位置推导，换台机器、换个目录都不用改。

param(
    # 留空表示 <仓库根>\测试素材
    [string]$OutDir = '',
    # 留空表示自动查找（tools\ → dist\替音工具\bin\ → PATH）
    [string]$Ffmpeg = '',
    [string]$Ffprobe = ''
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot 是本脚本所在目录（<仓库根>\docs），仓库根就是它的上一层。
# 用这个而不是相对路径，是因为相对路径取决于"用户从哪个目录调用的脚本"。
$RepoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = $PSScriptRoot }

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    # 默认放在项目【上一级】，也就是和仓库并排：
    #   <某目录>\MkvAudioSwap\   ← 仓库
    #   <某目录>\测试素材\        ← 素材
    # 这样素材不会混进代码仓库，也符合"测试用的东西放项目外面"的习惯。
    # 取不到父目录时（例如仓库就在盘符根目录）退回仓库根下。
    $parent = Split-Path -Parent $RepoRoot
    $base = if ([string]::IsNullOrWhiteSpace($parent)) { $RepoRoot } else { $parent }
    $OutDir = Join-Path $base '测试素材'
}

# 在候选位置里找可执行文件：仓库内优先，最后回退到 PATH
function Resolve-Tool([string]$Explicit, [string]$ExeName) {
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        if (Test-Path $Explicit) { return (Resolve-Path $Explicit).Path }
        throw "指定的 $ExeName 不存在: $Explicit"
    }

    $candidates = @(
        (Join-Path $RepoRoot "tools\$ExeName"),
        (Join-Path $RepoRoot "dist\替音工具\bin\$ExeName"),
        (Join-Path $RepoRoot "dist\$ExeName")
    )

    foreach ($c in $candidates) {
        if (Test-Path $c) { return (Resolve-Path $c).Path }
    }

    $onPath = Get-Command $ExeName -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw @"
找不到 $ExeName。

请任选一种方式提供：
  1. 先跑一次 build.bat（它会把 ffmpeg 放到 tools\）；
  2. 把 ffmpeg.exe / ffprobe.exe 放进 $(Join-Path $RepoRoot 'tools')；
  3. 用参数指定：-$(if ($ExeName -like 'ffmpeg*') { 'Ffmpeg' } else { 'Ffprobe' }) "完整路径\$ExeName"；
  4. 装好 ffmpeg 并加入系统 PATH。
"@
}

$Ffmpeg = Resolve-Tool $Ffmpeg 'ffmpeg.exe'
$Ffprobe = Resolve-Tool $Ffprobe 'ffprobe.exe'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Write-Host "仓库根目录: $RepoRoot" -ForegroundColor Cyan
Write-Host "输出目录  : $OutDir" -ForegroundColor Cyan
Write-Host "ffmpeg    : $Ffmpeg" -ForegroundColor DarkGray
Write-Host "ffprobe   : $Ffprobe" -ForegroundColor DarkGray
Write-Host ''

function FF([string[]]$FfArgs) {
    # 用 Start-Process 而不是 & 调用：
    # Windows PowerShell 5.1 会把原生命令写到 stderr 的内容提升成 ErrorRecord，
    # 在 $ErrorActionPreference='Stop' 下直接中断脚本（ffmpeg 的 banner 就写 stderr）。
    # 重定向到文件可以彻底绕开这个机制，同时也便于失败时把真实原因打出来。
    $log = Join-Path $env:TEMP 'mkvswap_ff.log'
    $p = Start-Process -FilePath $Ffmpeg -ArgumentList $FfArgs -NoNewWindow -Wait -PassThru `
        -RedirectStandardError $log -RedirectStandardOutput 'NUL'
    if ($p.ExitCode -ne 0) {
        $tail = (Get-Content $log -Tail 6 -ErrorAction SilentlyContinue) -join ' / '
        throw "ffmpeg 失败 (exit $($p.ExitCode)): $($FfArgs -join ' ')`n  $tail"
    }
}

function Probe([string]$File) {
    $json = (& $Ffprobe -v quiet -print_format json -show_streams -show_format $File 2>$null | Out-String)
    return $json | ConvertFrom-Json
}

# 故意损坏的文件不能被 probe（本来就读不出来），只列出文件本身
function ReportBroken([string]$File, [string]$What) {
    $size = [math]::Round((Get-Item $File).Length / 1KB, 1)
    Write-Host ("  {0,-40} {1,7} KB | （预期不可读）" -f (Split-Path $File -Leaf), $size)
    Write-Host ("        用途: $What") -ForegroundColor DarkGray
}

function Report([string]$File, [string]$What) {
    $o = Probe $File
    $v = $o.streams | Where-Object { $_.codec_type -eq 'video' } | Select-Object -First 1
    $a = $o.streams | Where-Object { $_.codec_type -eq 'audio' } | Select-Object -First 1
    $aCount = ($o.streams | Where-Object { $_.codec_type -eq 'audio' }).Count
    $size = [math]::Round((Get-Item $File).Length / 1MB, 2)

    $vPart = if ($v) { "$($v.codec_name) $($v.width)x$($v.height)" } else { '无视频' }
    $aPart = if ($a) {
        "$($a.codec_name)" +
        $(if ($a.sample_rate) { " $($a.sample_rate)Hz" }) +
        $(if ($a.bits_per_raw_sample) { " $($a.bits_per_raw_sample)bit" }) +
        $(if ($a.channels) { " $($a.channels)ch" })
    } else { '无音频' }

    Write-Host ("  {0,-40} {1,7} MB | 视频: {2} | 音频: {3} | 音轨数: {4}" -f `
        (Split-Path $File -Leaf), $size, $vPart, $aPart, $aCount)
    Write-Host ("        用途: $What") -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────
Write-Host '[1/12] 正常视频 + 正常音频（全绿基线）' -ForegroundColor Yellow
$v1 = Join-Path $OutDir '01_正常视频_30秒.mp4'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','testsrc=size=1280x720:rate=30:duration=30',
      '-f','lavfi','-i','sine=frequency=330:sample_rate=48000:duration=30',
      '-map','0:v:0','-map','1:a:0',
      '-c:v','libx264','-crf','30','-pix_fmt','yuv420p',
      '-c:a','aac','-b:a','128k','-n',$v1)

$a1 = Join-Path $OutDir '02_正常音频_30秒.caf'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=30',
      '-af','aformat=channel_layouts=stereo',
      '-c:a','pcm_s24le','-n',$a1)
Report $v1 '两个文件时长一致 → 应显示 ✓ 时长一致，按钮可点'
Report $a1 '规格应为 pcm_s24le / 48000Hz / 24bit / 立体声'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[2/12] 音频比视频短（最常见的真实事故）' -ForegroundColor Yellow
$v2 = Join-Path $OutDir '03_视频30秒.mp4'
Copy-Item $v1 $v2 -Force
$a2 = Join-Path $OutDir '04_音频只有8秒.caf'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=8',
      '-af','aformat=channel_layouts=stereo',
      '-c:a','pcm_s24le','-n',$a2)
Report $v2 '视频 30 秒'
Report $a2 '配 03 使用 → 应显示 ⚠ 音频短 22 秒，并提示"成品结尾会静音 0:22"'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[3/12] 音频比视频长' -ForegroundColor Yellow
$a3 = Join-Path $OutDir '05_音频45秒比视频长.caf'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=45',
      '-af','aformat=channel_layouts=stereo',
      '-c:a','pcm_s24le','-n',$a3)
Report $a3 '配 03 使用 → 应显示 ⚠ 音频长 15 秒'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[4/12] 无损压缩编码（放行，且仍然无损）' -ForegroundColor Yellow
Write-Host '       实测：FLAC / ALAC / WavPack 都能 -c copy 进 MKV'

$flac = Join-Path $OutDir '06_音频是FLAC_无损放行.flac'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=30',
      '-af','aformat=channel_layouts=stereo','-c:a','flac','-n',$flac)
Report $flac '应放行。FLAC 是无损压缩，搬进 MKV 后仍然无损'

$alac = Join-Path $OutDir '07_音频是ALAC_无损放行.m4a'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=30',
      '-af','aformat=channel_layouts=stereo','-c:a','alac','-n',$alac)
Report $alac '同上，ALAC 也是无损压缩'

$wv = Join-Path $OutDir '08_音频是WavPack_无损放行.wv'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=30',
      '-af','aformat=channel_layouts=stereo','-c:a','wavpack','-n',$wv)
Report $wv '同上，WavPack 也是无损压缩（比较冷门，一并覆盖）'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[4b/12] 有损压缩编码（放行，但应明确警示）' -ForegroundColor Yellow

$mp3 = Join-Path $OutDir '06b_音频是MP3_有损但放行.mp3'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=44100:duration=30',
      '-af','aformat=channel_layouts=stereo','-c:a','libmp3lame','-b:a','192k','-n',$mp3)
Report $mp3 '应放行，但黄字说明"音频已经有损，成品会有损"'

$m4a = Join-Path $OutDir '07b_音频是AAC_有损但放行.m4a'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=44100:duration=30',
      '-af','aformat=channel_layouts=stereo','-c:a','aac','-b:a','192k','-n',$m4a)
Report $m4a '同上，检测到 aac'

$opus = Join-Path $OutDir '08b_音频是Opus_有损但放行.opus'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=30',
      '-af','aformat=channel_layouts=stereo','-c:a','libopus','-b:a','128k','-n',$opus)
Report $opus '同上，检测到 opus。注意 Opus 采样率固定 48kHz'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[4c/12] 唯一装不进 MKV 的编码：裸 TrueHD' -ForegroundColor Yellow
Write-Host '       实测 matroska 写不了头（sample rate not set）'
$thd = Join-Path $OutDir '08c_音频是TrueHD_会被拦.thd'
    # truehd 是实验性编码器，必须加 -strict -2，否则报 'experimental codecs are not enabled'
    # 用 FF 包装而不是直接 & 调用 —— 见文件开头关于 stderr 被提升成异常的那条说明
    try {
        FF @('-hide_banner','-loglevel','error','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=10','-af','aformat=channel_layouts=stereo','-c:a','truehd','-strict','-2','-y',$thd)
    } catch { Write-Host ('  truehd 生成失败，跳过：' + $_.Exception.Message) -ForegroundColor DarkGray }
if (Test-Path $thd) {
    Report $thd '应红字拦住，说明这个编码无法原样封进 MKV'
} else {
    Write-Host '  （本机 ffmpeg 没编 truehd 编码器，跳过此用例）' -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[5/12] 采样率与声道异常（应只提醒、不拦）' -ForegroundColor Yellow
$odd = Join-Path $OutDir '09_音频22050Hz单声道_低采样率.wav'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=22050:duration=30',
      '-af','aformat=channel_layouts=mono','-c:a','pcm_s16le','-n',$odd)
Report $odd '配 03 使用 → 应提示采样率不同（22050 vs 源音轨 48000），但按钮仍可点'

$u8 = Join-Path $OutDir '10_音频只有8位_音质低.wav'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=30',
      '-af','aformat=channel_layouts=stereo','-c:a','pcm_u8','-n',$u8)
Report $u8 '应提示"只有 8 位，音质偏低"，但按钮仍可点'

$f32 = Join-Path $OutDir '11_浮点PCM_建议改定点.wav'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=30',
      '-af','aformat=channel_layouts=stereo','-c:a','pcm_f32le','-n',$f32)
Report $f32 '应提示"浮点 PCM，建议导出定点整数"，但按钮仍可点'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[6/12] 文件里有两条音轨（提醒丢弃）' -ForegroundColor Yellow
$multi = Join-Path $OutDir '12_视频有两条音轨.mkv'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','testsrc=size=1280x720:rate=30:duration=30',
      '-f','lavfi','-i','sine=frequency=330:sample_rate=48000:duration=30',
      '-f','lavfi','-i','sine=frequency=550:sample_rate=48000:duration=30',
      '-map','0:v:0','-map','1:a:0','-map','2:a:0',
      '-c:v','libx264','-crf','30','-pix_fmt','yuv420p',
      '-c:a','aac','-b:a','128k','-n',$multi)
Report $multi '应提示"有 2 条音轨，全部会被丢弃"，输出只保留你选的音频'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[7/12] 时长一致但内容错位（工具看不出来，只能靠耳朵）' -ForegroundColor Yellow
$off = Join-Path $OutDir '13_音频前3秒是静音_工具查不出.wav'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','anullsrc=r=48000:cl=stereo:d=3',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=27',
      '-filter_complex','[1:a]aformat=channel_layouts=stereo[a1];[0:a][a1]concat=n=2:v=0:a=1[out]',
      '-map','[out]','-c:a','pcm_s24le','-n',$off)
Report $off '时长正好 30 秒 → 工具会说"时长一致"，但声音整体晚了 3 秒。用偏移 -3000 可救'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[8/12] 纯音频 / 无音轨视频（应被拦住）' -ForegroundColor Yellow
$audioOnly = Join-Path $OutDir '14_纯音频拖进视频槽会被拦.mp3'
Copy-Item $mp3 $audioOnly -Force
Report $audioOnly '拖进【视频】槽 → 应提示"没有找到视频画面"'

$noAudio = Join-Path $OutDir '15_视频没有音轨.mp4'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','testsrc=size=1280x720:rate=30:duration=30',
      '-map','0:v:0','-an','-c:v','libx264','-crf','30','-pix_fmt','yuv420p','-n',$noAudio)
Report $noAudio '配 02 使用 → 应提示"这个视频没有音轨"，其余正常'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[9/12] 损坏 / 未下载完成的文件' -ForegroundColor Yellow
$broken = Join-Path $OutDir '16_损坏的文件_拷到一半.mp4'
$bytes = [IO.File]::ReadAllBytes($v1)
$half = [int]($bytes.Length * 0.35)
[IO.File]::WriteAllBytes($broken, $bytes[0..($half - 1)])
ReportBroken $broken '拖进去 → 应给出"没有下载完整或已损坏"之类的人话提示，而不是卡住'

$empty = Join-Path $OutDir '17_空文件.mp4'
[IO.File]::WriteAllBytes($empty, @())
ReportBroken $empty '拖进去 → 应提示无法读取，程序不应崩溃'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[10/12] 源文件名已含"替换音频"（测命名去重）' -ForegroundColor Yellow
$dupName = Join-Path $OutDir '18_名字已带后缀_替换音频.mp4'
Copy-Item $v1 $dupName -Force
Report $dupName '替换后应生成 "18_名字已带后缀_替换音频_2.mkv"（不是 _替换音频_替换音频）'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[11/12] 输出已存在（测自动加 (1)）' -ForegroundColor Yellow
$pre = Join-Path $OutDir '19_已有同名输出_替换音频.mkv'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','testsrc=size=320x240:rate=25:duration=2',
      '-c:v','libx264','-crf','35','-pix_fmt','yuv420p','-n',$pre)
Report $pre '用 03+02 替换后，输出应叫 "19_已有同名输出_替换音频(1).mkv"，而不是覆盖它'

# ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[12/12] 很短的视频（测预览时长标签）' -ForegroundColor Yellow
$short = Join-Path $OutDir '20_只有4秒的短视频.mp4'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','testsrc=size=640x360:rate=30:duration=4',
      '-f','lavfi','-i','sine=frequency=330:sample_rate=48000:duration=4',
      '-map','0:v:0','-map','1:a:0','-c:v','libx264','-crf','30','-pix_fmt','yuv420p',
      '-c:a','aac','-b:a','128k','-n',$short)
$aShort = Join-Path $OutDir '21_音频4秒配短视频.caf'
FF @('-hide_banner','-loglevel','error',
      '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=4',
      '-af','aformat=channel_layouts=stereo','-c:a','pcm_s24le','-n',$aShort)
Report $short '预览只有 4 秒长 → 确认预览不会因为"前 10 秒"标签而说谎'

# ─────────────────────────────────────────────────────────────
# 顺手写一份对照清单。
# 放在这里而不是单独维护一个文档，是为了保证它和实际生成的文件永远同步 ——
# 之前手写的那份在我清理目录时被误删过。
$readme = @'
# 测试素材说明

这批文件是**故意做出来的问题文件**，用来把工具的每条分支都走一遍。
由仓库里的 `docs\生成测试素材.ps1` 生成，可以随时重新生成（会覆盖同名文件）。
**下面这份清单也是那个脚本自动写的**，所以不会和实际文件脱节。

> 这些文件都是 ffmpeg 合成的**测试图案和正弦波**，不是真实音乐/画面。
> 它们的作用是触发工具的各种判断分支，不是用来听音质的。

---

## 怎么用

把文件拖进 `替音工具.exe` 的对应框里，对照下表的"预期表现"看是否一致。
表里凡是写"应被拦住"的，**「开始」按钮应变灰**，且卡片里有红色说明。

---

## 一、正常路径

| 文件 | 预期表现 |
|:--|:--|
| `01_正常视频_30秒.mp4` + `02_正常音频_30秒.caf` | 全绿。`✓ 时长一致`，按钮可点。<br>音频规格应显示 `pcm_s24le · 48000Hz · 24bit · 立体声` |

这是唯一一组"全绿基线"。点开始应生成 `01_正常视频_30秒_替换音频.mkv`。

---

## 二、时长不一致（只提醒、不拦）

| 文件 | 预期表现 |
|:--|:--|
| `03_视频30秒.mp4` + `04_音频只有8秒.caf` | `⚠ 音频短 22 秒`，下方摘要 `成品结尾会静音 0:22`。**按钮仍可点** |
| `03_视频30秒.mp4` + `05_音频45秒比视频长.caf` | `⚠ 音频长 15 秒`，摘要 `超出画面的 0:15 听不到`。**按钮仍可点** |

这是最值得测的一组：**程序不替你做判断**，黄字只是把事实摆出来。

---

## 三、不是线性 PCM（应被拦住）

| 文件 | 检测到的编码 | 预期表现 |
|:--|:--|:--|
| `06_音频是MP3_会被拦.mp3` | mp3 | 红字：不是线性 PCM（检测到 mp3），请在 DAW 重导 |
| `07_音频是AAC_会被拦.m4a` | aac | 同上，检测到 aac |
| `08_音频是FLAC_会被拦.flac` | flac | 同上，检测到 flac |

⚠️ **`08` 这个最值得试**：FLAC 是无损压缩，很多人会以为"无损就能用"。
但它是压缩过的，不能直接 `-c copy`，所以必须拦住。

---

## 四、规格异常（只提醒、不拦）

| 文件 | 预期表现 |
|:--|:--|
| `09_音频22050Hz单声道_低采样率.wav` | 配 `03` 使用 → 提醒采样率不同（22050 vs 源音轨 48000）。按钮**仍可点** |
| `10_音频只有8位_音质低.wav` | 提醒"只有 8 位，音质偏低"。按钮**仍可点** |
| `11_浮点PCM_建议改定点.wav` | 提醒"浮点 PCM，建议导出定点整数"。按钮**仍可点** |

这三个都是"能用但不推荐"。工具只提醒，不阻止 —— 这是刻意设计。

---

## 五、结构特殊

| 文件 | 预期表现 |
|:--|:--|
| `12_视频有两条音轨.mkv` | 提示"有 2 条音轨，全部会被丢弃"。配 `02` 使用 → 输出里**只保留你选的音频** |
| `15_视频没有音轨.mp4` | 提示"这个视频没有音轨"。配 `02` 使用 → 其余正常，按钮可点 |
| `14_纯音频拖进视频槽会被拦.mp3` | 拖进**视频**槽 → 红字"没有找到视频画面" |

---

## 六、错误处理

| 文件 | 预期表现 |
|:--|:--|
| `16_损坏的文件_拷到一半.mp4` | 给一句人话（"没有下载完整或已损坏"），**不要卡住转圈** |
| `17_空文件.mp4` | 同样给人话提示，程序**不应崩溃** |

这两个是测"最坏情况下的表现"。如果程序卡死、或者弹英文报错，那就是 bug。

---

## 七、输出命名

| 场景 | 预期表现 |
|:--|:--|
| `18_名字已带后缀_替换音频.mp4` + `02` | 输出应是 `18_名字已带后缀_替换音频_2.mkv`<br>**不能**是 `..._替换音频_替换音频.mkv` |
| `19_已有同名输出_替换音频.mkv` 已在目录里 + 用 `03`+`02` | 输出应是 `19_已有同名输出_替换音频(1).mkv`<br>原来那个文件**不能被覆盖** |

---

## 八、预览与偏移

| 场景 | 预期表现 |
|:--|:--|
| `20_只有4秒的短视频.mp4` + `21_音频4秒配短视频.caf` | 点"预览前 10 秒" → 预览只有 4 秒长，不应报错 |

### `13` 这个文件最值得琢磨

`13_音频前3秒是静音_工具查不出.wav` 配 `03_视频30秒.mp4`：
它的时长**正好 30 秒**，和视频完全一致，所以工具会说 `✓ 时长一致`。
但它的**前 3 秒是静音**，声音整体晚了 3 秒。

**工具查不出这个** —— 它只比对时长，不比对内容。这正是要让你亲眼看到的局限：

1. 直接替换 → 成品里人声比画面晚 3 秒，而且工具全程说"没问题"；
2. 在「音频偏移」里填 `-3000`（也就是 -3 秒），再替换 → 对齐了；
3. 或者点「预览前 10 秒」先听一遍，就能发现不对。

**第 2 条同时验证了偏移功能真的生效**：如果负偏移被静默吃掉（这是实测发现并专门防住的
ffmpeg 行为），成品不会有任何变化 —— 那样你就知道回归了。

---

## 建议的测试顺序

1. **先跑 `01` + `02`**，确认基线是好的、能出成品；
2. **再跑 `03` + `04`**，确认黄字提醒不阻止你点开始；
3. **拖 `08`（FLAC）**，确认拦住且说明是人话；
4. **拖 `16`（损坏文件）**，确认不卡死；
5. **`13` + `03` 配偏移**，确认偏移生效；
6. 其余按兴趣挑。

前 5 步覆盖了最重要的分支。

---

> 想改这个脚本、或者想知道参数怎么传，看 `docs\生成测试素材.md`。
'@

$readmePath = Join-Path $OutDir '说明.md'
# -Encoding UTF8 在 Windows PowerShell 5.1 下会写 BOM，正好避免中文被当 ANSI 读
Set-Content -Path $readmePath -Value $readme -Encoding UTF8
Write-Host ''
Write-Host ("  已写出对照清单: {0}" -f $readmePath) -ForegroundColor DarkGray

Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
$files = Get-ChildItem $OutDir -File
$totalMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ("  生成完成：{0} 个文件，共 {1} MB" -f $files.Count, $totalMb) -ForegroundColor Green
Write-Host ("  目录：{0}" -f $OutDir)
Write-Host '============================================================' -ForegroundColor Green
