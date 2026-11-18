import peach


def shout(ev):
    text = peach.clipboard_get()
    peach.clipboard_set(text.upper())
    peach.log("Clipboard in Großbuchstaben umgewandelt")
    peach.notify("Peach", "Zwischenablage: " + text.upper()[:60])


peach.hotkey("ctrl+alt+u", shout)
peach.log("Bereit: Text kopieren, dann Ctrl+Alt+U")
peach.run()
