# 🍑 Peach Client

Windows-Mod-Client: Mods in vielen Sprachen schreiben (JS, TS, Python, PowerShell, Batch, VBScript, AutoHotkey, Lua, Ruby, Go, C# …).

## Peach Addons (die App)

Menü (Strg+Alt+P) → **1 Addons & Einstellungen…** (oder Tray-Symbol). Ein Fenster mit Karten für alles, was Peach kann:

- **Eingebaut**: Controller-Modus, Controller → Maus, Bildschirmtastatur, Fast Type, Hotkeys, Peach-Leiste, Discord – jeweils mit Schalter und **Anpassen**-Knopf (schöne Editoren statt Standard-Dialoge; die Leiste mit Live-Vorschau, Farbschemata und Hex-Farben).
- **Script-Addons** (deine eigenen Mods in `mods\`): Schalter = starten/stoppen, Sprach-Badge, Rechte als Chips, **Bearbeiten** (Code-Editor, siehe unten), **Log**, **Entfernen**. Oben: **Neues Addon** (Name, Sprache, Rechte → fertiger Ordner mit `mod.json` + Startdatei) und **Installieren…** (aus ZIP oder Ordner, mit Schutz gegen unsichere ZIP-Pfade). Unten: Addon-Ordner öffnen, Web-Dashboard, Neu laden, „Addon-Host mit Peach starten“ (damit Addons mit `autostart` nach dem Anmelden laufen).
- Der Addon-Host (`bin\peach.js`, Node.js) startet automatisch im Hintergrund, sobald du die Addons-App öffnest. Ohne Node.js laufen die eingebauten Funktionen trotzdem.

### Der Code-Editor (Addons → **Bearbeiten**, oder direkt nach „Neues Addon“)

Ein richtiger Editor für den Ordner des Addons:

- **Dateireiter** (Startdatei, `mod.json`, eigene Dateien; „+“ öffnet weitere oder legt neue an), **Zeilennummern**, **Syntaxfarben** (JS/TS, Python, PowerShell, Batch, VBScript, JSON), Rückgängig/Wiederholen, Tab/Umschalt+Tab, Auto-Einrücken, Strg+/ für Kommentare.
- **Vorschläge beim Tippen** (oder Strg+Leertaste): die **Peach-API** (`peach.` → notify, storage, hotkey, clipboard …) und die **normalen Module der installierten Sprachen**, direkt aus Node.js (alle eingebauten Module mit ihren Funktionen, z. B. `fs.`), Python (komplette Standardbibliothek, `import …`, `from … import …`) und PowerShell (alle Befehle). Aliase wie `const cp = require('child_process')` oder `import json as j` werden verstanden. Die Listen werden einmal aus den Programmen gelesen und zwei Wochen zwischengespeichert (`%APPDATA%\PeachClient\api-*.json`).
- **API-Panel rechts**: durchsuchbar, mit Signatur, Beschreibung und nötigem Recht. Doppelklick fügt den Eintrag ein und ergänzt den Import (`const peach = require('peach');`, `import os` …) automatisch.
- **Peach-Rechte-Check**: Nutzt dein Code z. B. `peach.notify`, aber `notify` fehlt in `mod.json`, warnt der Editor und ergänzt es mit **Rechte ergänzen**. Aufrufe in Kommentaren zählen nicht.
- **Syntaxprüfung beim Speichern** mit den echten Programmen (`node --check`, Python, PowerShell-Parser, JSON): „✓ Syntax in Ordnung“ oder „✗ Zeile 3: …“.
- **Speichern & Starten (F5)** speichert, lädt das Addon neu und startet es; das **Log läuft live** unten mit.
### Discord in Peach

Menü → **Discord**, oder in der Addons-App die Karte **Discord → Öffnen**. Das ist das **offizielle Discord (discord.com/app)** in einem eigenen Peach-Fenster (Größe ändern, minimieren, maximieren):

- Chat, Server, **Sprach- und Videochat**, Benachrichtigungen – alles, was die Web-Version kann. Du meldest dich **wie im Browser** an (Passwort, QR-Code oder Passkey). Peach sieht und speichert deine Zugangsdaten nie.
- Eigenes Browserprofil in `%APPDATA%\PeachClient\webview2\discord`: du bleibst angemeldet. **Abmelden & Daten löschen** räumt es auf (dein Konto bleibt unverändert).
- **Nur Discord-Seiten** laden im Fenster (`discord.com`, `discordapp.com`, `discord.gg` …, dazu der hCaptcha-Login). Alle anderen Links öffnen im normalen Browser; keine Zusatzfenster, keine Programmstarts aus Webseiten (nur http/https/mailto).
- Mikrofon, Kamera, Benachrichtigungen und Einfügen werden **nur Discord selbst** erlaubt.
- Das **X blendet das Fenster nur aus** – ein laufender Sprachchat läuft weiter. **Beenden** (im Fenster oder per Schalter in den Addons) beendet die Sitzung.
- **In Leiste und Launcher**: im App-Editor (Installierte Apps durchsuchen) steht ganz oben **„Discord (in Peach)“**. Technisch ist das der Pfad `peach:discord`; `peach:addons` öffnet die Addons-App.
- Technik: Microsofts WebView2 (die Laufzeit steckt in Windows 11 / Edge). Die kleinen Bibliotheken liegen in `lib\webview2` (aus dem offiziellen NuGet-Paket `Microsoft.Web.WebView2` 1.0.4314.35, von Microsoft signiert, Lizenz in `lib\webview2\LICENSE.txt`). Der Ordner `lib` muss neben `peach.exe` bleiben. Fehlt er, baut `build.ps1` Peach ohne Discord-Fenster.
#### Einstellungen, Befehle und Skripte (wie bei Vencord)

Im Discord-Fenster **Einstellungen** (oder Addons → Karte Discord → **Einstellungen**, oder im Chatfeld `/peach settings`). Alles wirkt **sofort**, ohne Neuladen:

- **Aussehen**: 5 Themes (Peach, Mitternacht, Wald, AMOLED, Violett), eigene **Akzentfarbe** (Knöpfe, Schalter, Links …), **Schrift**, **Zoom** 60–160 %, „Animationen reduzieren“. Die Themes setzen Discords eigene Design-Variablen (aus der Live-Seite gemessen); wenn Discord sein Design ändert, hilft eigenes CSS.
- **Befehle im Chatfeld**: tippen + Enter, Peach fängt sie ab, bevor Discord sie sieht. Eingebaut: `/peach help`, `/peach settings`, `/peach theme <name>`, `/peach commands`. **Eigene Befehle** (Reiter „Befehle“): `/name` wird zu deinem Text, mit `{args}` (was hinter dem Befehl steht), `{date}`, `{time}`. Der Text landet im Chatfeld und wartet auf Enter; optional „Befehle sofort senden“. Mitgeliefert: `/zeit`, `/gg`. Unbekannte `/befehle` bleiben bei Discord.
- **Wie die normalen Peach-Funktionen, nur für Discord**: Ein Befehl kann statt Text auch ein **Programm, eine Datei oder eine URL öffnen** (bei `https://…`-Adressen wird `{args}` URL-kodiert eingesetzt, z. B. `https://www.google.com/search?q={args}`; bei Programmen gibt es nur feste Argumente – Chat-Text wird nie zur Kommandozeile). Dazu optional ein **Tastenkürzel pro Befehl** (Strg/Alt nötig), das **nur bei aktivem Discord-Fenster** greift, und im Reiter „Kürzel“ **Abkürzungen** wie bei Fast Type, die direkt beim Tippen im Chatfeld ersetzt werden (`{date}`, `{time}` möglich). Nichts davon ist systemweit registriert.
- **Eigenes CSS** (Reiter „Skripte & CSS“): freies CSS, wirkt beim Tippen.
- **Skripte** (eigene Plugins): `*.js` in `%APPDATA%\PeachClient\discord\scripts`. **Opt-in**: eine neue Datei läuft erst, wenn du den Schalter einschaltest (selbst angelegte Skripte starten eingeschaltet). API im Skript: `peach.css('…')`, `peach.toast('…')`, `peach.command('hallo', args => 'Hallo ' + args)`, `peach.ready(fn)`. Jedes Skript ist einzeln gekapselt: ein Fehler in einem bricht die anderen nicht. **Achtung:** Skripte laufen im Discord-Fenster und können dort alles, auch auf dein Konto zugreifen – nur eigene oder vertrauenswürdige einschalten.
- **Wichtig zu wissen**: Clientmodifikationen (Vencord, BetterDiscord und auch dies hier) sind in den Discord-Nutzungsbedingungen nicht vorgesehen. Durchgesetzt wird das selten, ein Restrisiko für dein Konto bleibt. Peach beschränkt sich deshalb bewusst auf Aussehen und Komfort: nichts automatisiert dein Konto, nichts liest dein Token aus.
- **Nicht** enthalten und absichtlich nicht gebaut: ein eigener Discord-Client, der sich mit deinem Konto/Token anmeldet (verstößt gegen die Discord-Bedingungen).
Selbst testen: siehe `TESTEN.md`. Automatische Prüfungen ohne Tasten/Klicks: `peach.exe --selftest out.txt` (über 200 Checks inkl. Live-Tests gegen den Addon-Host und die installierten Node/Python/PowerShell) und `powershell -STA -File test\bar-test.ps1`. Vorschaubilder der Fenster: `peach.exe --config c.json --render-ui addons|bar-editor|hotkeys|snippets|apps|picker|controller|dialog|editor|discord|discord-settings|discord-settings-commands|discord-settings-css out.png`.
### Game-Boost (CPU, GPU, RAM live + Boost)

Menü → **Game-Boost öffnen** (oder Addons → Karte *Game-Boost* → Öffnen): drei Live-Anzeigen für **CPU, GPU und RAM** (dieselben Zahlen wie im Task-Manager, mit Verlauf der letzten Minute). Der **Boost** macht, was Windows von sich aus nicht tut, wenn du spielst:

- **Energieplan** auf *Höchstleistung* (bzw. *Ultimative Leistung*, falls vorhanden) – vorher steht oft „Ausbalanciert“.
- **Hohe Priorität** für das Spiel (nie „Echtzeit“).
- **Apps schließen**: Programme, die du selbst einträgst (z. B. Browser), werden höflich zum Schließen aufgefordert – nie zwangsweise beendet, ungespeicherte Arbeit bleibt geschützt.
- **Automatisch**: Trage unter „Spiele“ deine Spiele ein; sobald eins im Vordergrund läuft, boostet Peach, und nach dem Beenden wird alles zurückgestellt. Alternativ von Hand: Menü → *Game-Boost* oder der Knopf im Fenster.

Ehrlich: Ein Booster macht die Hardware nicht schneller – er nimmt Windows nur Bremsen weg (der Effekt ist je nach PC klein bis spürbar). Windows-Prozesse, Sicherheitsprogramme und Peach selbst werden nie angefasst. Falls Peach mitten im Boost beendet wird, stellt es den alten Energieplan beim nächsten Start wieder her.

### Minecraft (Profile, Mod-Manager, Widgets)

Menü → **Minecraft** (oder Addons → Karte *Minecraft* → Öffnen). Für **Minecraft Java Edition** mit dem offiziellen Launcher (einmal gestartet):

- **Eigener Launcher** (kein offizieller Launcher nötig): Peach lädt Spieldateien, Bibliotheken und Sounds direkt von Mojang (nur von deren Servern, jede Datei mit Prüfsumme), findet Java selbst (passende Version, z. B. Java 21 für 1.21) und startet Minecraft. Jedes **Profil** hat einen eigenen Spielordner (`%APPDATA%\PeachClient\minecraft\<profil>`) mit eigenen Mods und Welten; Spieldateien werden zwischen Profilen geteilt. Fabric wird automatisch eingerichtet, die Version ist frei wählbar, auch ältere Versionen gehen. Spiel-Log: `<Profilordner>\logs\peach-launch.log`. Speicher einstellbar (Reiter „Konto“).
- **Anmeldung** (Reiter „Konto“): Microsoft-Gerätecode – du gibst einen Code auf microsoft.com ein, Peach bekommt nie dein Passwort; das Merk-Token wird mit Windows-DPAPI verschlüsselt. Dafür brauchst du eine **eigene Azure-App** (kostenlos, Anleitung im Reiter) und die **Freigabe von Mojang** für den Minecraft-Zugang neuer Apps (Antrag: aka.ms/AppRegInfo). Ohne Freigabe geht nur der **Demo-Modus** (Mojangs offizielle Demo, kein Konto nötig).
- **Mod-Manager**: Mods von **Modrinth** suchen (passend zu Profil-Version und Fabric), installieren inkl. benötigter Abhängigkeiten, ein-/ausschalten, löschen. Geladen wird nur von Modrinths CDN (https), die Datei wird gegen die SHA-512-Prüfsumme geprüft.
- **Widgets über dem Spiel** (ohne Mod, **mit jeder Version**): Spotify (Song aus dem Fenstertitel der Spotify-App, Mediatasten-Knöpfe), CPU, GPU, RAM, Uhrzeit, Spielzeit. Durchklickbares Fenster in einer Ecke, nur sichtbar wenn Minecraft vorn ist (einstellbar). Minecraft muss im Fenster oder Randlos laufen (nicht exklusives Vollbild).
- **Noch nicht dabei**: Dinge, die nur in Minecraft selbst gehen – FPS-Anzeige, Koordinaten, Build-Modus (Fliegen, Fast-Place, Blöcke ziehen, Bridge-Builder). Das braucht eine Mod pro Minecraft-Version.

## peach.exe (Tray-App mit Menü)

`peach.exe` doppelklicken → läuft im Tray. **Strg+Alt+P** öffnet das Menü (nochmal drücken oder Esc schließt es; Pfeile/Enter/Zifferntasten/Maus gehen).
Ein zweiter Start von `peach.exe` öffnet das Menü der laufenden Instanz.

| Funktion | Was sie macht |
|---|---|
| **Controller-Modus** | Vollbild-Oberfläche mit großen Kacheln für deine Apps (wie bei einer Konsole). **Geht nur mit angeschlossenem Controller**: ohne Controller lehnt Peach das Einschalten ab (Menü zeigt „kein Controller“). Wird der Controller getrennt, pausiert der Modus (Launcher weg, Desktop normal nutzbar, Einstellung bleibt gespeichert) und startet von selbst wieder, sobald ein Controller verbunden ist – auch nach einem Neustart von Peach. An: Menüpunkt 2 oder am Controller **Home-Taste / Back + Y**. Steuerkreuz/linker Stick wählt, **A** startet die App. Der Launcher bleibt dauerhaft offen (immer Vollbild, nicht „immer im Vordergrund“): die App öffnet sich einfach davor, und mit ihr im Vordergrund steuert der Controller wie gewohnt die Maus. Die **Home-Taste (Xbox-Taste)** – oder **Back + Y** – holt den Launcher aus jeder App wieder nach vorn. Beendet wird er nur über die letzte Kachel **„Modus beenden“** (B und Esc schließen ihn nicht). Windows öffnet bei der Xbox-Taste standardmäßig die Game Bar: Beim ersten Einschalten des Controller-Modus fragt Peach einmal, ob es das ausschalten soll; später im Menü „Xbox-Taste: Game Bar sperren“ (schreibt dieselbe Einstellung wie Windows → Einstellungen → Spiele → Xbox Game Bar → „Xbox Game Bar über diese Taste am Controller öffnen“). Solange der Modus an ist, kann Back + Start die Controller-Maus nicht ausschalten. Der Modus bleibt über Neustarts erhalten (mit „Mit Windows starten“ startest du direkt im Launcher). |
| **Apps für den Modus** | Addons → Controller-Modus → „Anpassen“. **„Installierte Apps durchsuchen…“** listet alles aus dem Startmenü (Discord, Steam, Spotify, Chrome, Store-Apps …) mit Suche und Mehrfachauswahl, die Icons kommen automatisch. Außerdem: Programme, Verknüpfungen oder Ordner per Drag & Drop ins Fenster ziehen, „Hinzufügen“ (mit Suchen-Knopf), Argumente, Reihenfolge mit Hoch/Runter. Auch URLs und Ziele wie `ms-settings:` gehen. |
| **Peach-Leiste** | Deine eigene Leiste am unteren oder oberen Bildschirmrand (Addons → Peach-Leiste → „Anpassen“ mit Live-Vorschau; Menüpunkt 7 schaltet sie an/aus; Rechtsklick auf die Leiste geht auch). Sie reserviert ihren Platz wie eine Taskbar, maximierte Fenster bleiben davor. Inhalt: Peach-Knopf (öffnet das Menü), angeheftete Apps (inkl. Discord, Steam & Co. über „Installierte Apps durchsuchen“), offene Fenster zum Wechseln (Klick auf das aktive minimiert, Rechtsklick → Fenster schließen), Uhr und Datum. Einstellbar: Position, Höhe, Hintergrund- und Akzentfarbe, Deckkraft, welche Teile sichtbar sind – alles wirkt sofort. Nur für den normalen Desktop: Im Controller-Modus (Controller verbunden) wird sie ausgeblendet, bei Pause/ohne Controller ist sie wieder da. Die Windows-Taskbar bleibt unangetastet (die Peach-Leiste liegt darüber). Test: `powershell -STA -File test\bar-test.ps1` (ohne Tasten/Klicks). |
| **Controller → Maus** | Linker Stick = Zeiger, rechter Stick = Scrollen, A/B/X = Links-/Rechts-/Mittelklick (A halten = ziehen), LB/RB = langsam/schnell, Steuerkreuz = Pfeiltasten, Y = Bildschirmtastatur, **Back + Start** = ein/aus. Geschwindigkeit: `MouseSpeed` in der Config. |
| **Bildschirmtastatur** | Erscheint automatisch, wenn du mit dem Controller in ein Textfeld klickst (Menüpunkt 4 schaltet das ab), oder mit **Y** bzw. Menüpunkt 5. Nimmt dem Textfeld nie den Fokus. Steuerkreuz = Taste wählen, **A** tippt, **B** löschen, **X** Leerzeichen, **Y** Shift, **LB/RB** Cursor links/rechts, **Start** Enter, **RT** Mausklick (anderes Feld wählen), **Back** schließt. Zweite Ebene `?123` für Symbole. Mit der Maus geht sie auch. |
| **Fast Type** | Kürzel tippen → wird sofort ersetzt (z. B. `;mfg` → „Mit freundlichen Grüßen“). Platzhalter `{date}`, `{time}`. |
| **Hotkeys** | Eigene Tastenkürzel: Programm/Datei/URL öffnen oder Text tippen. |
| **Script-Addons** | Mods in JS, TS, Python, PowerShell, Batch, VBScript … – verwaltest du in der Addons-App (Node.js nötig, siehe unten). |
| **Mit Windows starten** | Autostart per Registry (HKCU). |

Einstellungen: `%APPDATA%\PeachClient\config.json`. Neu bauen (ohne SDK, nur Windows-Bordmittel): `powershell -File build.ps1`.
Tests: `peach.exe --selftest out.txt` (Logik) und `powershell -STA -File test\exe-test.ps1` (echte Tastendrücke – nur laufen lassen, wenn du gerade nicht tippst).

Fast Type nutzt einen Tastatur-Hook. Es werden nur die letzten ~48 Zeichen im Arbeitsspeicher gehalten, nichts wird gespeichert; der Puffer wird bei Enter, Tab, Shortcuts und Fensterwechsel gelöscht.

## Start (Script-Mod-Host)

```
node bin/peach.js start --open     # Client + Dashboard (http://127.0.0.1:47800)
node bin/peach.js new mein-mod --lang py
node bin/peach.js list
node bin/peach.js langs            # welche Interpreter gefunden wurden
npm test                           # Smoke-Test aller Beispiel-Mods
```

## Wie es funktioniert

Jeder Mod ist ein Ordner in `mods/` mit einer `mod.json` und einer Einstiegsdatei. Peach startet ihn als eigenen Prozess
(Interpreter anhand der Dateiendung) und gibt ihm eine lokale HTTP-API (`PEACH_API`) plus ein Token (`PEACH_TOKEN`).
Dadurch kann **jede** Sprache Mods schreiben, die HTTP sprechen kann. SDKs für JS/TS, Python und PowerShell liegen in `sdk/`.

```json
{ "name": "Mein Mod", "version": "1.0.0", "entry": "main.py", "permissions": ["notify", "clipboard"], "autostart": true }
```

## API (POST `<PEACH_API>/<methode>`, Header `Authorization: Bearer <PEACH_TOKEN>`)

| Methode | Recht | Beschreibung |
|---|---|---|
| `log`, `system.info`, `storage.get/set` | – | Log, Systeminfos, Key-Value-Speicher pro Mod |
| `notify` | `notify` | Windows-Benachrichtigung |
| `clipboard.get/set` | `clipboard` | Zwischenablage |
| `windows.list` | `windows` | Offene Fenster |
| `registry.get` | `registry` | Registry lesen (nur lesen) |
| `wallpaper.set` | `wallpaper` | Hintergrundbild setzen |
| `shell.run`, `shell.open` | `shell` | Programm starten / URL oder Datei öffnen |

| `hotkey.register/unregister` | `hotkeys` | Globale Hotkeys, z. B. `ctrl+alt+p` (mind. ein Modifier) |
| `events.subscribe` | `windows` / `clipboard` | Events `focus` (Fenster-/Titelwechsel) und `clipboard` (Inhalt geändert) |
| `events.poll` | – | Long-Poll: liefert wartende Events (`hotkey`, `focus`, `clipboard`) |

Ohne das passende Recht in `mod.json` antwortet Peach mit 403.

## Hotkeys & Events

```js
const peach = require('peach');
await peach.hotkey('ctrl+alt+p', () => peach.notify('Peach', 'Hallo'));
await peach.on('focus', ev => peach.log(ev.process, ev.title));
```
```python
import peach
peach.hotkey("ctrl+alt+u", lambda ev: peach.log("gedrückt"))
peach.run()          # blockiert und verteilt Events
```
```powershell
. "$env:PEACH_SDK\peach.ps1"
Peach-Hotkey 'ctrl+alt+h' { Peach-Log 'gedrückt' }
Peach-Run
```
Andere Sprachen rufen `events.poll` selbst in einer Schleife auf. Ein Hotkey gehört genau einem Mod; er wird freigegeben, sobald der Mod endet.
Hinweis: PowerShell-Mods mit Umlauten als UTF-8 **mit BOM** speichern (Windows PowerShell 5.1 liest sonst ANSI).

## Weitere Sprache hinzufügen

`peach.config.json` im Projektordner:

```json
{ "runners": { ".rb": [["ruby", "{entry}"]], ".nim": [["nim", "r", "{entry}"]] } }
```

## Sicherheit

API lauscht nur auf `127.0.0.1`, prüft den Host-Header (DNS-Rebinding), Tokens gelten nur für die Laufzeit des jeweiligen Mod-Prozesses.
Mods laufen aber mit deinen Windows-Rechten – installiere nur Mods, denen du vertraust.
