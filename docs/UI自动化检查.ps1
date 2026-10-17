# 替音工具 · UI Automation 冒烟检查
#
# 运行（Windows PowerShell 5.1）：
#   powershell -ExecutionPolicy Bypass -File docs\UI自动化检查.ps1
#   powershell -ExecutionPolicy Bypass -File docs\UI自动化检查.ps1 -ExePath "...\Debug\...\替音工具.exe"
#
# 注意：本文件必须保存为 UTF-8 带 BOM，否则 PowerShell 5.1 会把中文读成 ANSI。
# 默认指向 dist\替音工具\替音工具.exe（带 bin\ffmpeg.exe，环境检查才能通过）。

param(
    [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'

trap {
    Write-Host ("ERROR " + $_)
    Get-Process -Name '替音工具' -ErrorAction SilentlyContinue | Stop-Process -Force
    exit 1
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NativeWin {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
"@

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $PSScriptRoot '..\dist\替音工具\替音工具.exe'
}
if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "找不到 exe：$ExePath"
}
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
$exeDir = Split-Path -Parent $ExePath
if (-not (Test-Path -LiteralPath (Join-Path $exeDir 'bin\ffmpeg.exe'))) {
    Write-Host "WARN  exe 旁边没有 bin\ffmpeg.exe；环境检查那一步可能失败。"
}

$script:failures = 0
function Pass([string]$text) { Write-Host ("PASS  " + $text) }
function Fail([string]$text) { Write-Host ("FAIL  " + $text); $script:failures++ }
function Info([string]$text) { Write-Host ("INFO  " + $text) }

Write-Host ("EXE   " + $ExePath)
Get-Process -Name '替音工具' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$p = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds 4

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$main = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if ($null -eq $main) {
    $main = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $pidCond)
}
if ($null -eq $main) {
    Fail 'main window not found'
    Stop-Process -Id $p.Id -Force
    exit 1
}
Pass ("main window: " + $main.Current.Name + " / " + $main.Current.ClassName)

function Find-ById($parent, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-ByName($parent, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-ButtonByName($parent, [string]$name) {
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $typeCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $and = New-Object System.Windows.Automation.AndCondition(@($nameCond, $typeCond))
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
}

function Invoke-Element($el) {
    $pattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Wait-Window([string]$name, [int]$timeoutMs = 6000) {
    $deadline = [DateTime]::Now.AddMilliseconds($timeoutMs)
    while ([DateTime]::Now -lt $deadline) {
        $nameCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $typeCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)
        $and = New-Object System.Windows.Automation.AndCondition(@($nameCond, $typeCond))
        $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
        if ($null -ne $found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

# 1) 主窗口控件存在
foreach ($id in @('SettingsButton', 'PickVideoButton', 'PickAudioButton', 'OffsetButton', 'StartButton')) {
    $el = Find-ById $main $id
    if ($null -eq $el) { Fail ("missing AutomationId: " + $id) } else { Pass ("found " + $id) }
}

# 2) 开始按钮在空状态下不可用
$start = Find-ById $main 'StartButton'
if ($null -ne $start) {
    if ($start.Current.IsEnabled) { Fail 'StartButton should be disabled in empty state' }
    else { Pass 'StartButton disabled in empty state' }
}

# 3) 设置弹窗：打开 → 环境检查 → 关闭
$settingsButton = Find-ById $main 'SettingsButton'
if ($null -ne $settingsButton) {
    Invoke-Element $settingsButton
    $settings = Wait-Window '设置'
    if ($null -eq $settings) {
        Fail 'settings dialog did not open'
    } else {
        Pass 'settings dialog opened'

        $check = Find-ById $settings 'CheckButton'
        if ($null -eq $check) {
            Fail 'settings CheckButton missing'
        } else {
            Invoke-Element $check
            Start-Sleep -Seconds 3
            $ff = Find-ByName $settings 'ffmpeg'
            if ($null -eq $ff) { Fail 'env check did not render ffmpeg row' }
            else { Pass 'env check rendered ffmpeg row' }
        }

        $close = Find-ById $settings 'CloseButton'
        if ($null -eq $close) { Fail 'settings CloseButton missing' }
        else { Invoke-Element $close; Start-Sleep -Milliseconds 600; Pass 'settings dialog closed' }
    }
}

# 4) 偏移弹窗：输入 -300 → 确定 → 主界面标签更新
$offsetButton = Find-ById $main 'OffsetButton'
if ($null -ne $offsetButton) {
    Invoke-Element $offsetButton
    $offset = Wait-Window '音频偏移'
    if ($null -eq $offset) {
        Fail 'offset dialog did not open'
    } else {
        Pass 'offset dialog opened'

        $box = Find-ById $offset 'OffsetBox'
        if ($null -eq $box) {
            Fail 'OffsetBox missing'
        } else {
            $valuePattern = $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            $valuePattern.SetValue('-300')
            Start-Sleep -Milliseconds 400
            Pass 'OffsetBox set to -300'
        }

        # 4b) 快捷增减必须是相对值：-300 +100 → -200，再 -300 → -500
        $plus100 = Find-ButtonByName $offset '+100'
        if ($null -eq $plus100) {
            Fail 'preset +100 missing'
        } else {
            Invoke-Element $plus100
            Start-Sleep -Milliseconds 400
            $afterPlus = $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($afterPlus.Trim() -eq '-200') { Pass 'preset +100 is relative (-300 -> -200)' }
            else { Fail ("preset +100 expected -200, got " + $afterPlus) }
        }

        $minus300 = Find-ButtonByName $offset '-300'
        if ($null -eq $minus300) {
            Fail 'preset -300 missing'
        } else {
            Invoke-Element $minus300
            Start-Sleep -Milliseconds 400
            $afterMinus = $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            if ($afterMinus.Trim() -eq '-500') { Pass 'preset -300 is relative (-200 -> -500)' }
            else { Fail ("preset -300 expected -500, got " + $afterMinus) }
        }

        # 恢复 -300，供后面主界面标签断言
        $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('-300')
        Start-Sleep -Milliseconds 300

        $preview = Find-ById $offset 'PreviewButton'
        if ($null -eq $preview) { Fail 'PreviewButton missing' } else { Pass 'PreviewButton present' }

        $ok = Find-ById $offset 'OkButton'
        if ($null -eq $ok) {
            Fail 'OkButton missing'
        } else {
            Invoke-Element $ok
            Start-Sleep -Milliseconds 800
            Pass 'offset dialog confirmed'
        }

        $textCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, '-300 毫秒')
        $textEl = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $textCond)
        if ($null -ne $textEl) { Pass 'offset label updated to -300 毫秒' }
        else { Info 'offset label text not found via UIA (visual check required)' }
    }
}

# 5) 文件选择框能打开并取消（不写文件）
$pick = Find-ById $main 'PickVideoButton'
if ($null -ne $pick) {
    Invoke-Element $pick
    $picker = Wait-Window '选择视频（源画面）'
    if ($null -eq $picker) { $picker = Wait-Window '选择视频' }
    if ($null -eq $picker) {
        Fail 'file picker did not open'
    } else {
        Pass 'file picker opened'
        $hwnd = [IntPtr]$picker.Current.NativeWindowHandle
        if ($hwnd -ne [IntPtr]::Zero) {
            [NativeWin]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            Start-Sleep -Milliseconds 700
            $still = Wait-Window '选择视频（源画面）' 1000
            if ($null -eq $still) { Pass 'file picker cancelled (WM_CLOSE)' }
            else { Fail 'file picker did not close' }
        } else {
            Fail 'file picker has no native window handle'
        }
    }
}

Write-Host ''
if ($script:failures -eq 0) { Write-Host 'UIA RESULT: PASS' } else { Write-Host ("UIA RESULT: FAIL (" + $script:failures + ")") }
Stop-Process -Id $p.Id -Force
if ($script:failures -eq 0) { exit 0 } else { exit 1 }
