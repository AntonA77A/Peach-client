const peach = require('peach');

(async () => {
  // Ctrl+Alt+P is reserved for the peach.exe menu
  await peach.hotkey('ctrl+alt+j', async () => {
    await peach.log('Hotkey ctrl+alt+j pressed');
    await peach.notify('Peach', 'Hotkey gedrückt (JavaScript)');
  });

  await peach.on('focus', ev => peach.log(`Fokus: ${ev.process} - ${ev.title}`));

  await peach.log('Bereit: Ctrl+Alt+J drücken');
})();
