// Needs Node >= 23.6 (runs TypeScript natively)
const peach = require('peach') as any;

(async (): Promise<void> => {
  await peach.log('Hello from TypeScript!');
  await peach.notify('hello-ts', 'TypeScript mod is running');
})();
