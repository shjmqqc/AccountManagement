// Run via playwright-cli run-code --filename mobile/check-ui.js against the disposable fixture.
// Requires desktop Fixture with args: 18083 http://127.0.0.1:18084 ding, then check-api.mjs.
async page => {
  const check = (ok, message) => { if (!ok) throw new Error(message); };
  const idle = () => page.waitForFunction(() => !document.body.classList.contains('busy'));
  const click = async selector => { await page.locator(selector).click(); await idle(); };
  const noOverflow = async () => check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Horizontal overflow');
  const clipboard = async () => (await page.evaluate(() => navigator.clipboard.readText())).replace(/\r\n/g, '\n');
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.goto('http://127.0.0.1:18084/'); await idle();
  await page.evaluate(() => { window.confirm = () => true; });
  if (!(await page.locator('#login-panel').isVisible()) && await page.locator('#identity').textContent() !== 'desktop-test') {
    await click('[data-page=my]'); await click('#logout');
  }
  if (await page.locator('#login-panel').isVisible()) {
    await page.locator('#username').fill('desktop-test');
    await page.locator('#login-password').fill('test123456');
    await click('#login-form button[type=submit]');
  }
  let owner = await page.evaluate(() => JSON.parse(sessionStorage.getItem('accmgr.session')).token);
  const request = async (path, token = owner, method = 'GET', data) => {
    const response = await page.request.fetch('http://127.0.0.1:18084/accmgr-api/' + path,
      {method, headers: {Authorization: 'Bearer ' + token}, data});
    check(response.ok(), 'Fixture API failed: ' + path); return response.json();
  };
  const records = await request('api/accounts'), account = records.find(a => a.login === 'test@example.com');
  check(!!account, 'Run check-api.mjs before UI checks');
  if (records.length === 1) for (let i = 1; i <= 7; i++) {
    await request('api/accounts', owner, 'POST', {platform_id: account.platform_id, name: '测试环境 ' + i,
      login: 'admin', password: 'fixture-only', notes: '', tag_ids: account.tags.map(t => t.id)});
  }
  let passwordRequests = 0;
  const watch = req => { if (/\/accounts\/[^/]+\/password$/.test(req.url())) passwordRequests++; };
  page.on('request', watch);
  await click('#refresh');
  check(passwordRequests === 0, 'List must not prefetch secrets');
  check(!(await page.locator('#filter-box').evaluate(node => node.open)), 'Filters start collapsed');
  await page.setViewportSize({width: 390, height: 844}); await noOverflow();
  await page.screenshot({path: 'output/playwright/mobile-list.png', fullPage: true});
  await page.setViewportSize({width: 320, height: 740}); await noOverflow();
  await page.locator('#search').fill('test@example.com');
  check(await page.locator('.card').count() === 1, 'Search narrows rows');
  await click('.quick');
  check(await page.evaluate(() => navigator.clipboard.readText()) === 'mobile-test-secret', 'Quick copy');
  await click('.account-main'); await noOverflow();
  await click('#copy-all');
  const expected = '【平台名称】移动端验证平台\n【地址】https://example.com\n【帐号】test@example.com\n【密码】mobile-test-secret';
  check(await clipboard() === expected, 'Exact four-line whole copy');
  await page.screenshot({path: 'output/playwright/mobile-detail.png', fullPage: true});
  await page.evaluate(async () => {
    await navigator.clipboard.writeText('before-legacy-copy');
    const readText = navigator.clipboard.readText.bind(navigator.clipboard);
    Object.defineProperty(navigator, 'clipboard', {configurable: true, value: {readText, writeText: () => Promise.reject(new Error('denied'))}});
  });
  await click('#copy-all');
  check(await clipboard() === expected, 'Legacy browser copy on Clipboard API failure');
  check(await page.locator('#copy-fallback').isHidden(), 'Successful legacy copy needs no manual selection');
  await page.evaluate(() => { document.execCommand = () => false; });
  await click('#copy-all');
  check(await page.locator('#copy-fallback').innerText() === expected, 'Manual copy fallback preserves all lines');
  await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
  check(await page.locator('#copy-fallback').textContent() === '', 'Leaving page clears fallback secrets');
  await click('#open-sharing'); await noOverflow();
  await page.locator('#recipient-name').fill('张'); await click('#recipient-form button');
  check(await page.locator('#recipient-result button').count() === 1, 'Name search excludes current user');
  check((await page.locator('#recipient-result').innerText()).includes('张三（other-test）'), 'Full name and username distinguish recipient');
  await page.screenshot({path: 'output/playwright/mobile-share.png', fullPage: true});
  await click('#recipients button');
  check(await page.locator('#recipients button').count() === 0, 'Revoke removes recipient');
  await click('#recipient-result button');
  check(await page.locator('#recipients button').count() === 1, 'Grant restores recipient');
  await click('[data-page=share]');
  check(await page.locator('#scope-tabs').isHidden(), 'Share tab has its own scope');
  check(await page.locator('.card').count() === 1, 'Share tab contains outgoing account only');
  await click('[data-page=my]'); await noOverflow();
  check(await page.locator('#ding-name').innerText() === '张三', 'My page shows full bound DingTalk name');
  await page.screenshot({path: 'output/playwright/mobile-my.png', fullPage: true});
  await click('#logout');
  owner = (await request('api/login', '', 'POST', {username: 'desktop-test', password: 'test123456'})).token;
  await page.reload(); await idle(); // Restore the real browser clipboard after fallback mock.
  await page.locator('#username').fill('other-test'); await page.locator('#login-password').fill('test123456');
  await click('#login-form button[type=submit]'); await click('[data-scope=received]');
  await click('.account-main');
  check(await page.locator('#open-sharing').isHidden(), 'Recipient cannot reshare');
  await click('#copy-all');
  check(await clipboard() === expected, 'Recipient whole copy');
  const receiver = await request('api/me', await page.evaluate(() => JSON.parse(sessionStorage.getItem('accmgr.session')).token));
  await request(`api/accounts/${account.id}/shares/${receiver.id}`, owner, 'DELETE');
  await page.evaluate(() => navigator.clipboard.writeText('unchanged'));
  await click('#copy-all');
  check(await page.evaluate(() => navigator.clipboard.readText()) === 'unchanged', 'Revoked copy leaves clipboard unchanged');
  check(!(await page.locator('#message').innerText()).includes('已复制'), 'Revoked copy never reports success');
  await request(`api/accounts/${account.id}/shares`, owner, 'POST', {recipient_id: receiver.id});
  page.off('request', watch);
  return 'PASS compact layout 390/320px, no prefetch, search, quick/whole copy, fallback cleanup, name search, grant/revoke, outgoing scope, My name, recipient copy and revoked-copy denial.';
}
