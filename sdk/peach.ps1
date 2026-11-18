# Peach SDK for PowerShell mods.   . "$env:PEACH_SDK\peach.ps1"
$ErrorActionPreference = 'Stop'

function Invoke-Peach {
    param([Parameter(Mandatory)][string]$Method, [hashtable]$Params = @{})
    $body = [Text.Encoding]::UTF8.GetBytes(($Params | ConvertTo-Json -Compress -Depth 8))
    try {
        $res = Invoke-RestMethod -Method Post -Uri "$env:PEACH_API/$Method" -Body $body `
            -ContentType 'application/json; charset=utf-8' -Headers @{ Authorization = "Bearer $env:PEACH_TOKEN" }
    } catch {
        $detail = $_.ErrorDetails.Message
        if ($detail) { throw ($detail | ConvertFrom-Json).error } else { throw }
    }
    return $res.result
}

function Peach-Log($Message)                 { Invoke-Peach 'log' @{ message = "$Message" } | Out-Null }
function Peach-Notify($Title, $Message)      { Invoke-Peach 'notify' @{ title = $Title; message = $Message } | Out-Null }
function Peach-SystemInfo                    { Invoke-Peach 'system.info' }
function Peach-StorageGet($Key)              { Invoke-Peach 'storage.get' @{ key = $Key } }
function Peach-StorageSet($Key, $Value)      { Invoke-Peach 'storage.set' @{ key = $Key; value = $Value } | Out-Null }
function Peach-ClipboardGet                  { Invoke-Peach 'clipboard.get' }
function Peach-ClipboardSet($Text)           { Invoke-Peach 'clipboard.set' @{ text = $Text } | Out-Null }
function Peach-WindowsList                   { Invoke-Peach 'windows.list' }
function Peach-RegistryGet($Path, $Name)     { Invoke-Peach 'registry.get' @{ path = $Path; name = $Name } }
function Peach-WallpaperSet($Path)           { Invoke-Peach 'wallpaper.set' @{ path = $Path } | Out-Null }
function Peach-ShellRun($Command, [string[]]$Args = @()) { Invoke-Peach 'shell.run' @{ command = $Command; args = $Args } }
function Peach-ShellOpen($Target)            { Invoke-Peach 'shell.open' @{ target = $Target } | Out-Null }

# ---- hotkeys & events ------------------------------------------------------
# Peach-Hotkey 'ctrl+alt+h' { ... }; Peach-On 'focus' { param($e) ... }; then Peach-Run
$script:PeachHandlers = @{}
function Peach-On($Type, [scriptblock]$Handler) {
    if ($Type -in 'focus', 'clipboard') { Invoke-Peach 'events.subscribe' @{ events = @($Type) } | Out-Null }
    if (-not $script:PeachHandlers.ContainsKey($Type)) { $script:PeachHandlers[$Type] = @() }
    $script:PeachHandlers[$Type] += $Handler
}
function Peach-Hotkey($Combo, [scriptblock]$Handler) {
    $norm = (Invoke-Peach 'hotkey.register' @{ combo = $Combo }).combo
    $h = $Handler.GetNewClosure()
    Peach-On 'hotkey' ({ param($e) if ($e.combo -eq $norm) { & $h $e } }.GetNewClosure())
}
function Peach-Run {
    while ($true) {
        try { $events = Invoke-Peach 'events.poll' @{ timeoutMs = 25000 } }
        catch { if ("$_" -match 'invalid token|Unable to connect|connect to the remote') { exit 1 }; Start-Sleep 1; continue }
        foreach ($e in @($events)) {
            foreach ($h in @($script:PeachHandlers[$e.type])) { try { & $h $e } catch { Write-Error $_ } }
        }
    }
}
