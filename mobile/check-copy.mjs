import {readFile} from 'node:fs/promises';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';

const source = await readFile(new URL('app.js', import.meta.url), 'utf8');
const functions = source.slice(source.indexOf('async function copy('), source.indexOf('async function password('))
  + source.slice(source.indexOf('function loadDingSdk('), source.indexOf('function dingCode('));
const sample = '【平台名称】测试\n【地址】http://example.com\n【帐号】admin\n【密码】示例🔐';
function fixture({native, browser, legacy = false, inDingTalk = false} = {}) {
  const messages = [], nodes = [], target = {hidden: true, textContent: '', scrollIntoView() {}};
  let legacyCalls = 0;
  const context = vm.createContext({
    inDingTalk, secretVersion: 1, sdkLoading: undefined,
    window: {dd: native ? {setClipboard: native} : undefined, getSelection: () => ({removeAllRanges() {}, addRange() {}})},
    navigator: {clipboard: browser && {writeText: browser}},
    document: {hidden: false, body: {append(node) { nodes.push(node); }},
      execCommand(command) { assert.equal(command, 'copy'); legacyCalls++; return legacy; },
      createRange: () => ({selectNodeContents() {}})},
    element: () => ({value: '', focus() {}, select() {}, setSelectionRange() {}, remove() { this.removed = true; }}),
    message: text => messages.push(text), setTimeout, clearTimeout
  });
  vm.runInContext(functions, context);
  return {context, target, messages, nodes, legacyCalls: () => legacyCalls, copy: () => context.copy(sample, target)};
}
test('DingTalk callback succeeds without browser Clipboard API', async () => {
  let received; const f = fixture({inDingTalk: true, native: options => { received = options.text; options.success(); }});
  await f.copy(); assert.equal(received, sample); assert.deepEqual(f.messages, ['已复制。']); assert.equal(f.target.hidden, true);
});
test('DingTalk promise result is supported', async () => {
  const f = fixture({inDingTalk: true, native: () => Promise.resolve()}); await f.copy(); assert.equal(f.messages[0], '已复制。');
});
test('Native failure falls back to browser copy', async () => {
  let received; const f = fixture({inDingTalk: true, native: o => o.fail(), browser: async text => { received = text; }});
  await f.copy(); assert.equal(received, sample); assert.equal(f.legacyCalls(), 0); assert.equal(f.messages[0], '已复制。');
});
test('HTTP legacy copy cleans temporary plaintext after success', async () => {
  const f = fixture({legacy: true}); await f.copy(); assert.equal(f.messages[0], '已复制。');
  assert.equal(f.nodes.length, 1); assert.equal(f.nodes[0].value, ''); assert.equal(f.nodes[0].removed, true);
});
test('All methods failing selects manual text, never reports success', async () => {
  const f = fixture({browser: async () => { throw Error('denied'); }}); await f.copy();
  assert.equal(f.target.textContent, sample); assert.equal(f.target.hidden, false);
  assert.match(f.messages[0], /长按复制/); assert.equal(f.nodes[0].value, ''); assert.equal(f.nodes[0].removed, true);
});
test('Hidden or superseded request does not expose text after asynchronous failure', async () => {
  for (const hide of [c => c.document.hidden = true, c => c.secretVersion++]) {
    let reject; const f = fixture({browser: () => new Promise((_, fail) => { reject = fail; })});
    const pending = f.copy(); hide(f.context); reject(Error('denied')); await pending;
    assert.equal(f.messages.length, 0); assert.equal(f.nodes.length, 0); assert.equal(f.target.textContent, '');
  }
});
