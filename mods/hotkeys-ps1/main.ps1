. "$env:PEACH_SDK\peach.ps1"

Peach-Hotkey 'ctrl+alt+h' {
    Peach-Log 'Hotkey ctrl+alt+h pressed'
    Peach-Notify 'Peach' 'Hotkey gedrückt (PowerShell)'
}
Peach-On 'clipboard' { Peach-Log 'Zwischenablage hat sich geändert' }

Peach-Log 'Bereit: Ctrl+Alt+H drücken'
Peach-Run
