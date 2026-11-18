# Selbst testen – Checkliste

Alles Automatische hat bestanden (`peach.exe --selftest`: 350 Checks). Die Fenster sehen wie vorgesehen aus (als Bilder geprüft).
**Nicht** getestet, weil dafür deine Maus/Tastatur oder ein Gamepad nötig sind: das Klicken selbst. Bitte einmal durchgehen:

## Start
1. `peach.exe` doppelklicken → Peach-Symbol im Tray, Hinweis „Peach läuft“.
2. **Strg + Alt + P** → Menü. Mit Pfeiltasten/Enter/Ziffern bedienen, Esc schließt.

## Addons-App  (Menü → **1**)
- [ ] Fenster mit Karten „Eingebaut“ (6 Stück) und „Script-Addons“. Oben „● Addon-Host läuft“ (nach 1–2 Sekunden; braucht Node.js).
- [ ] **Filter** Alle / Eingebaut / Script-Addons umschalten.
- [ ] Schalter bei **Fast Type** aus/an → im Menü (Strg+Alt+P) steht dasselbe.
- [ ] **Anpassen** bei Fast Type: Kürzel hinzufügen (`;test` → „Hallo“), bearbeiten, löschen. Danach in Notepad `;test` tippen.
- [ ] **Anpassen** bei Hotkeys: Hotkey anlegen (Feld anklicken, Kombination drücken), Aktion „Programm öffnen“ (z. B. `calc.exe`), speichern, Hotkey drücken.
- [ ] **Anpassen** bei Peach-Leiste: Live-Vorschau, Position, Höhe, Deckkraft, Farbschema anklicken, Hex-Farbe tippen, Schalter. Die echte Leiste folgt sofort.
- [ ] **Neues Addon**: Name, Sprache (z. B. Python), Rechte → „Anlegen“ → Ordner wird angeboten. Karte erscheint; **Schalter** startet es; **Log** zeigt „Hello from Python!“.
- [ ] **Installieren… → Aus ZIP/Ordner** mit einem Addon (muss `mod.json` haben).
- [ ] **Entfernen** beim selbst angelegten Addon → Rückfrage → Ordner ist weg.
- [ ] Fenster schließen (X) und über das Menü wieder öffnen (geht sofort).

## Code-Editor  (Addons → bei einem Script-Addon **Bearbeiten**, oder nach „Neues Addon“ → „Im Editor öffnen“)
- [ ] Fenster mit Reitern `main.js` und `mod.json`, Zeilennummern, farbigem Code.
- [ ] `peach.` tippen → Liste mit notify, storage, hotkey … ; mit Pfeil + Enter/Tab übernehmen.
- [ ] `const fs = require('fs');` tippen, dann `fs.` → Funktionen von fs (readFileSync …). Bei `require('` erscheinen alle Node-Module.
- [ ] Python-Addon: `import os` dann `os.` → Funktionen; `import ` + Buchstaben → Standardbibliothek. PowerShell: `Get-` → Befehle.
- [ ] Strg + Leertaste öffnet die Liste auch ohne Tippen. Esc schließt sie.
- [ ] Rechts im API-Panel suchen (z. B. `readFile`), Doppelklick → Eintrag samt `require`-Zeile wird eingefügt.
- [ ] `peach.notify('a','b')` schreiben, aber `notify` nicht in `mod.json` → oben erscheint die orange Warnung; **Rechte ergänzen** trägt es ein.
- [ ] Absichtlich einen Syntaxfehler einbauen, **Speichern** → oben „✗ Zeile …“. Fehler beheben, speichern → „✓ Syntax in Ordnung“.
- [ ] Tab/Umschalt+Tab einrücken (auch mehrere markierte Zeilen), Enter nach `{` rückt ein, Strg+/ kommentiert, Strg+Z / Strg+Y.
- [ ] **F5**: speichert, startet das Addon, Log unten zeigt die `peach.log`-Ausgabe live. Bei einem Absturz steht der Fehler im Log.
- [ ] „+“ im Reitermenü: neue Datei anlegen (z. B. `helfer.js`) und mit `require('./helfer')` benutzen.
- [ ] Fenster schließen mit ungespeicherten Änderungen → Rückfrage Speichern/Verwerfen.

## Discord  (Menü → **Discord**, oder Addons → Karte Discord → **Öffnen**)
- [ ] Fenster öffnet sich mit der Discord-Anmeldeseite (Peach-Titelleiste mit Minimieren / Maximieren / Schließen, Größe an den Rändern änderbar).
- [ ] Anmelden wie im Browser (Passwort, QR-Code oder Passkey). Danach Fenster schließen (X) und wieder öffnen → du bist noch angemeldet.
- [ ] Chat schreiben. Ein Link zu einer fremden Seite → öffnet im normalen Browser, nicht im Fenster.
- [ ] Sprachkanal beitreten: beim ersten Mal fragt Windows/Discord nach dem Mikrofon – erlauben. Fenster mit X ausblenden → du bleibst im Sprachchat. **Beenden** trennt.
- [ ] Peach-Leiste / Launcher: im App-Editor → „Installierte Apps durchsuchen“ → ganz oben **Discord (in Peach)** hinzufügen; Klick auf den Knopf öffnet das Fenster.
- [ ] **Abmelden & Daten löschen** (Rückfrage) → danach wieder die Anmeldeseite.
- [ ] Addons → Karte Discord: Schalter an = Fenster offen, aus = Sitzung beendet.
- [ ] **Einstellungen** (Knopf im Discord-Fenster, oder Addons → Discord → Einstellungen): Theme anklicken → Discord färbt sich sofort um (nach der Anmeldung: Serverleiste, Kanäle, Chat). Falls an einer Stelle noch Original-Farbe bleibt: eigenes CSS im Reiter „Skripte & CSS“.
- [ ] Akzentfarbe (z. B. `#33aaff`) → Knöpfe, Schalter, Links ändern sich. „Zurücksetzen“ nimmt sie wieder weg. Schrift „Segoe UI“, Zoom-Regler.
- [ ] **Befehle**: im Chatfeld `/zeit` tippen + Enter → statt Senden steht „Es ist 14:05 Uhr (…)“ im Chatfeld; Enter sendet. `/gg Anton` → „gg wp Anton“. Eigenen Befehl im Reiter „Befehle“ anlegen und ausprobieren. Ein Befehl, den es nicht gibt (z. B. `/giphy x`), geht an Discord wie sonst.
- [ ] **Befehle mit Aktion**: im Reiter „Befehle“ → Hinzufügen → „Programm / Datei / URL öffnen“, z. B. Name `google`, Ziel `https://www.google.com/search?q={args}`. Im Chatfeld `/google hallo welt` + Enter → Browser öffnet die Suche. Mit Programm (z. B. `notepad.exe`) öffnet sich nur das Programm, Text aus dem Chat wird nie als Argument eingesetzt.
- [ ] **Tastenkürzel pro Befehl** (z. B. Strg+Alt+1 auf einem Text-Befehl): im Discord-Fenster drücken → Text steht im Chatfeld. In anderen Programmen passiert nichts (gilt nur bei aktivem Discord-Fenster). Umschalt allein, Strg+Alt+P oder ein globaler Peach-Hotkey werden abgelehnt.
- [ ] **Kürzel** (Reiter „Kürzel“): z. B. `btw` → „by the way“. Im Chatfeld `btw` tippen → wird sofort ersetzt; `abtw` (mitten im Wort) nicht; `;gn` geht auch direkt nach einem Buchstaben. In anderen Programmen/Chats im Browser passiert nichts.
- [ ] `/peach help`, `/peach theme wald`, `/peach settings` im Chatfeld → kleine Meldung unten rechts bzw. Theme wechselt bzw. Einstellungen öffnen sich.
- [ ] **Skripte**: „Neues Skript“ → Vorlage (Befehl `/uhr`), „Bearbeiten“ ändert sie, nach „Fertig“ lädt Discord neu. Schalter aus → `/uhr` geht nicht mehr. Eine `.js` per Hand in den Ordner legen → startet **aus**.
## Game-Boost  (Menü → **Game-Boost öffnen**, oder Addons → Karte Game-Boost → Öffnen)
- [ ] Drei Kacheln CPU / GPU / RAM bewegen sich live (GPU zeigt „–“, falls dein PC keine Zähler liefert). Zahlen grob wie im Task-Manager.
- [ ] **Boost starten**: Knopf wird „Boost beenden“, grüner Status, Meldung unten rechts. Prüfen: Systemsteuerung → Energieoptionen zeigt *Höchstleistung*. **Boost beenden** → alter Plan (z. B. Ausbalanciert) ist zurück.
- [ ] Reiter „Spiele“ → Hinzufügen → ein laufendes Spiel aus der Liste wählen (oder Namen tippen). „Automatisch boosten“ einschalten, das Spiel starten/in den Vordergrund holen → nach wenigen Sekunden „Game-Boost an“. Spiel beenden → „Game-Boost aus“, Energieplan zurück.
- [ ] Task-Manager → Details: das Spiel läuft mit Priorität „Hoch“ während des Boosts, danach wieder „Normal“.
- [ ] Reiter „Apps schließen“: z. B. `notepad.exe` eintragen (mit offenem Notepad), Boost starten → Notepad fragt wie beim normalen Schließen nach (nichts geht verloren). Windows-Programme (`explorer.exe` …) lassen sich nicht eintragen.
- [ ] Peach im Boost über das Tray-Menü beenden und neu starten → der alte Energieplan ist wieder da.

## Minecraft  (Menü → **Minecraft**, oder Addons → Karte Minecraft → Öffnen)
Peachs eigener Launcher, kein offizieller Launcher nötig. Voraussetzung: Java (z. B. Temurin 21), für „Spielen“ ein Microsoft-Konto mit Minecraft Java.
- [ ] **Neues Profil** → Name, Version anklicken (z. B. 1.21.1), „Fabric“ → Anlegen. Es erscheint in der Liste mit „0 Mods“.
- [ ] **Demo** (ohne Konto): beim ersten Mal lädt Peach ~800 MB (Statuszeile zählt hoch), dann öffnet sich Minecraft im Demo-Modus mit Fabric. Beim zweiten Mal startet es sofort. (Dieser Ablauf wurde automatisch geprüft: Fenster „Minecraft* 1.21.1“ erscheint, Fabric lädt.)
- [ ] Reiter **Konto**: Client-ID einer eigenen Azure-App eintragen → „Anmelden“ → Code erscheint (liegt in der Zwischenablage), Browser öffnet microsoft.com/link, Code eingeben, anmelden → „Angemeldet als <Name>“. Wenn Mojang die App noch nicht freigegeben hat, endet es mit einer Fehlermeldung beim letzten Schritt (dann geht nur die Demo). **Das konnte ich nicht selbst testen** – kein Konto, keine Freigabe.
- [ ] **Spielen** mit angemeldetem Konto → Minecraft startet mit deinem Namen/Skin, Multiplayer geht. „Abmelden“ löscht das gespeicherte Token. Speicher-Regler (GB) wirkt beim nächsten Start.
- [ ] Reiter **Mods** → „Mods suchen & installieren“ → z. B. `sodium` suchen, auswählen, Installieren → Datei steht in der Liste (plus ggf. Fabric API als benötigte Mod). Schalter aus → Mod wird beim Start nicht geladen. Löschen fragt nach.
- [ ] Nach dem Start: Mods sind aktiv (Sodium-Grafikoptionen bzw. Fabric im Titelbild).
- [ ] Reiter **Widgets**: einschalten, Minecraft im Fenster/Randlos starten → kleines Fenster in der Ecke mit Spotify (Spotify-App öffnen, Song abspielen → „Titel · Künstler“), CPU/GPU/RAM, Uhrzeit, Spielzeit. Klicks gehen durch das Fenster hindurch ins Spiel. Mit „Nur zeigen, wenn Minecraft vorn ist“ verschwindet es beim Alt-Tab.
- [ ] ⏮ ⏯ ⏭ steuern die Spotify-App. Position und Deckkraft ändern sich sofort.
- [ ] Profil löschen fragt nach und löscht den Spielordner des Profils (Welten + Mods).

## Peach-Leiste
- [ ] Anschalten (Addons oder Menü **7**): Leiste unten, maximierte Fenster überdecken sie nicht.
- [ ] Klick auf ein Fenster in der Leiste wechselt dorthin; Klick aufs aktive minimiert; Rechtsklick → Fenster schließen / anpassen / ausblenden.
- [ ] Angeheftete App (z. B. Discord) startet per Klick.

## Controller (mit Gamepad)
- [ ] Menü **3**: Controller → Maus an. Linker Stick bewegt den Zeiger, A klickt, rechter Stick scrollt.
- [ ] Klick in ein Textfeld → Bildschirmtastatur erscheint (D-Pad wählen, A tippt, B löscht, Start = Enter, Back schließt).
- [ ] Menü **2**: Controller-Modus an → Vollbild-Launcher, Apps starten mit A, **Home/Xbox-Taste** (oder Back + Y) holt ihn zurück, „Modus beenden“ beendet.
- [ ] Erste Nachfrage zur Game Bar: „Ja“ → Xbox-Taste öffnet nur noch Peach.
- [ ] Controller abziehen → Launcher verschwindet, Leiste ist wieder da; anstecken → Launcher kommt zurück.
- [ ] Ohne Controller: Menü **2** zeigt „kein Controller“ und lehnt ab.

Wenn etwas anders aussieht oder sich anders verhält als beschrieben: kurz sagen, was genau – dann fixe ich es.
