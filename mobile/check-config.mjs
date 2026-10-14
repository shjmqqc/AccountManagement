import {readFile} from 'node:fs/promises';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';

const source = await readFile(new URL('app.js', import.meta.url), 'utf8');
const context = vm.createContext({});
vm.runInContext(source.slice(source.indexOf('function configuredApiBase('), source.indexOf('const API =')), context);
test('deployment proxy path supports custom same-origin prefix', () => {
  assert.equal(context.configuredApiBase({apiBase:'/team/accounts-api/'}), '/team/accounts-api/');
});
test('missing configuration, external origins and URL tricks are rejected', () => {
  for (const path of [undefined, '', '/', 'https://other.example/api/', '//other.example/api/', '/api/?x=1', '/a/../api/', '/api/#x', '/a\\b/', '/%2fother/'])
    assert.throws(() => context.configuredApiBase({apiBase:path}));
  assert.throws(() => context.configuredApiBase(undefined));
});
test('packaged config and script order agree', async () => {
  const config = await readFile(new URL('config.js', import.meta.url), 'utf8');
  const html = await readFile(new URL('index.html', import.meta.url), 'utf8');
  const c = vm.createContext({window:{}}); vm.runInContext(config, c);
  assert.equal(context.configuredApiBase(c.window.ACCMGR_CONFIG), '/accmgr-api/');
  assert.ok(html.indexOf('src="config.js') < html.indexOf('src="app.js'));
});
