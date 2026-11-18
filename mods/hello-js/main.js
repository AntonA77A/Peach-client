const peach = require('peach');

(async () => {
  await peach.log('Hello from JavaScript!');
  await peach.notify('hello-js', 'JavaScript mod is running');
})();
