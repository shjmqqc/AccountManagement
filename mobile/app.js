'use strict';
// Same-origin Apache proxy; no credentials or DingTalk app secrets belong in this file.
function configuredApiBase(config) {
  const path = config?.apiBase;
  if (typeof path !== 'string' || !/^\/(?:[A-Za-z0-9_-]+\/)+$/.test(path))
    throw new Error('config.js 的 apiBase 须为同域代理路径，例如 /accmgr-api/。');
  return path;
}
const API = (() => {
  try { return configuredApiBase(window.ACCMGR_CONFIG); }
  catch (error) {
    document.getElementById('message').textContent = '服务器配置错误，请联系管理员检查 config.js。';
    throw error;
  }
})();
const $ = id => document.getElementById(id);
let token = '', user = null, scope = 'own', accounts = [], platforms = [], current = null;
let busy = false, mustChange = false, secretVersion = 0;
let dingConfig = null, page = 'find';
const inDingTalk = /DingTalk/i.test(navigator.userAgent);
let sdkLoading;
const errors = {
  dingtalk_disabled: '钉钉免登尚未启用，请使用帐号密码登录。',
  dingtalk_unavailable: '暂时无法验证钉钉身份，请使用帐号密码登录或稍后重试。',
  dingtalk_code_invalid: '钉钉授权已失效，请重新点击钉钉登录。',
  dingtalk_already_bound: '此钉钉身份或系统帐号已有其他绑定，请先解除原绑定。',
  dingtalk_login_denied: '帐号已停用或暂时锁定，无法免登。',
  dingtalk_password_required: '请先用临时密码登录并修改密码，再使用钉钉免登或绑定。',
  incorrect_password: '原密码不正确。',
  invalid_credentials: '用户名或密码不正确。', invalid_session: '登录已失效，请重新登录。',
  browser_origin_not_allowed: '服务器尚未允许此页面来源，请联系管理员配置。',
  password_change_required: '请先修改临时密码。', invalid_recipient: '接收人不存在、已停用或是你本人。',
  too_many_login_attempts: '登录尝试过于频繁，请稍后再试。', not_found: '记录不存在或授权已撤销。',
  new_password_must_differ: '新密码不能与原密码相同。', invalid_old_password: '原密码不正确。',
  concurrent_change_retry: '数据刚刚发生变化，请刷新后重试。'
};
function message(text = '') { $('message').textContent = text; }
function element(tag, text, className) {
  const node = document.createElement(tag); if (text != null) node.textContent = text;
  if (className) node.className = className; return node;
}
function hideSecret() { secretVersion++; $('copy-fallback').textContent = ''; $('copy-fallback').hidden = true; $('secret').textContent = '••••••••'; $('reveal').textContent = '显示密码'; }
function show(panel) {
  hideSecret(); for (const id of ['login-panel', 'password-panel', 'library', 'detail', 'unbind-panel', 'share-panel', 'my-panel']) $(id).hidden = id !== panel;
  $('logout').hidden = !token; $('bottom-nav').hidden = !token || mustChange;
  $('page-title').textContent = page === 'share' ? '分享管理' : page === 'my' ? '我的' : '帐号管理';
  for (const button of document.querySelectorAll('[data-page]')) button.setAttribute('aria-pressed', String(button.dataset.page === page));
}
function clearSession() {
  token = ''; user = null; current = null; accounts = []; platforms = []; mustChange = false; page = 'find';
  $('ding-name').textContent = ''; $('binding-status').textContent = '';
  $('accounts').replaceChildren(); $('recipients').replaceChildren(); $('recipient-result').replaceChildren();
  for (const id of ['detail-title','detail-platform','share-title','account-login','account-url','account-notes','identity']) $(id).textContent = '';
  $('detail-tags').replaceChildren();
  $('login-form').reset(); $('password-form').reset(); $('recipient-form').reset();
  $('unbind-form').reset(); $('unbind-dingtalk').hidden = true;
  try { sessionStorage.removeItem('accmgr.session'); } catch (_) { }
  show('login-panel');
}
async function api(path, method = 'GET', body) {
  const controller = new AbortController(); const timer = setTimeout(() => controller.abort(), 20000);
  try {
    const headers = {}; if (token) headers.Authorization = 'Bearer ' + token;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(API + path, {method, headers, body: body === undefined ? undefined : JSON.stringify(body),
      cache: 'no-store', credentials: 'omit', redirect: 'error', signal: controller.signal});
    const data = await response.json();
    if (!response.ok) {
      if (response.status === 401) clearSession();
      if (data.error === 'password_change_required') { mustChange = true; show('password-panel'); $('cancel-password').hidden = true; }
      if (response.status === 404 && current) { current = null; $('accounts').replaceChildren(); accounts = []; show('library'); }
      throw new Error(errors[data.error] || (response.status === 401 ? '登录已失效，请重新登录。' : '操作失败，请刷新后重试。'));
    }
    return data;
  } catch (error) {
    if (error.name === 'AbortError' || error instanceof TypeError || error instanceof SyntaxError)
      throw new Error(method === 'GET' ? '无法连接服务器，请稍后刷新。' : '未能确认操作结果，请先刷新确认，避免重复提交。');
    throw error;
  } finally { clearTimeout(timer); }
}
async function run(action) {
  if (busy) return; busy = true; message(); document.body.classList.add('busy');
  const controls = [...document.querySelectorAll('button,input,select')]; controls.forEach(n => n.disabled = true);
  try { await action(); } catch (error) { message(error.message); }
  finally { busy = false; document.body.classList.remove('busy'); controls.forEach(n => n.disabled = false); }
}
function tags(parent, values = []) {
  parent.replaceChildren(); for (const tag of values) {
    const color = ['blue','green','orange','red','purple','gray'].includes(tag.color) ? tag.color : 'blue';
    parent.append(element('span', tag.name, 'tag ' + color));
  }
}
function options(id, title, values) {
  const select = $(id), previous = select.value; select.replaceChildren(new Option(title, ''));
  for (const [key, label] of values) select.add(new Option(label, key));
  if ([...select.options].some(o => o.value === previous)) select.value = previous;
}
async function loadLibrary() {
  hideSecret(); current = null;
  const received = scope === 'received';
  const records = await api(received ? 'api/shared/accounts' : 'api/accounts');
  platforms = received ? [] : await api('api/platforms');
  if (scope === 'outgoing' && records.some(a => typeof a.share_count !== 'number')) throw new Error('请先更新后端，以支持分享给别人列表。');
  accounts = scope === 'outgoing' ? records.filter(a => a.share_count > 0) : records;
  const platformNames = new Map(), tagNames = new Map();
  for (const a of accounts) {
    platformNames.set(a.platform_id, a.platform_name + (received ? ' · ' + a.owner_username : ''));
    for (const tag of a.tags || []) tagNames.set(tag.id, tag.name + (received ? ' · ' + a.owner_username : ''));
  }
  options('platform-filter', '全部平台', platformNames); options('tag-filter', '全部标签', tagNames);
  $('scope-tabs').hidden = page === 'share';
  show('library'); renderAccounts();
}
function renderAccounts() {
  hideSecret();
  const q = $('search').value.trim().toLocaleLowerCase(), platform = $('platform-filter').value, tag = $('tag-filter').value;
  const filtered = accounts.filter(a => (q ? [a.platform_name,a.name,a.login,a.notes].some(s => (s || '').toLocaleLowerCase().includes(q)) : !platform || a.platform_id === platform)
    && (!tag || (a.tags || []).some(t => t.id === tag)));
  $('accounts').replaceChildren(); $('count').textContent = filtered.length ? `${filtered.length} 个帐号` : '暂无符合条件的帐号';
  for (const a of filtered) {
    const card = element('div', null, 'card'), open = element('button', null, 'account-main');
    open.append(element('strong', a.name || a.login));
    open.append(element('span', `${a.platform_name} · ${a.login}`, 'muted'));
    if (scope === 'received') open.append(element('span', '来自 ' + a.owner_username, 'muted small'));
    if (scope === 'outgoing') open.append(element('span', `已分享给 ${a.share_count} 人`, 'muted small'));
    const badges = element('div'); tags(badges, (a.tags || []).slice(0, 2));
    if ((a.tags || []).length > 2) badges.append(element('span', '+' + (a.tags.length - 2), 'tag gray'));
    open.append(badges); open.onclick = () => run(() => openAccount(a));
    const quick = element('button', scope === 'outgoing' ? '管理' : '复制密码', 'quick');
    quick.setAttribute('aria-label', (scope === 'outgoing' ? '管理分享：' : '复制密码：') + (a.name || a.login));
    quick.onclick = () => run(async () => {
      if (scope === 'outgoing') { await openAccount(a); await openSharing(); return; }
      hideSecret(); const version = secretVersion, selectedScope = scope;
      const data = await api(`api/${selectedScope === 'received' ? 'shared/' : ''}accounts/${encodeURIComponent(a.id)}/password`, 'POST', {action: 'copy'});
      if (version !== secretVersion || document.hidden || scope !== selectedScope) return;
      await copy(data.password, $('copy-fallback'));
    });
    card.append(open, quick); $('accounts').append(card);
  }
}
async function openAccount(listItem) {
  const path = scope === 'received' ? 'api/shared/accounts/' : 'api/accounts/';
  const detail = await api(path + encodeURIComponent(listItem.id));
  current = {...listItem, ...detail}; show('detail');
  $('detail-title').textContent = current.name || current.login;
  $('detail-platform').textContent = current.platform_name + (scope === 'received' ? ' · 来自 ' + current.owner_username : '');
  $('account-login').textContent = current.login; $('account-notes').textContent = current.notes || '暂无备注'; tags($('detail-tags'), current.tags);
  const url = scope === 'received' ? current.platform_url : platforms.find(p => p.id === current.platform_id)?.url;
  $('account-url').textContent = url || '未填写';
  $('open-sharing').hidden = scope === 'received'; $('recipient-result').replaceChildren(); $('recipient-form').reset();
  current.platform_url = url || '';
}
async function openSharing() {
  if (!current || scope === 'received') return;
  show('share-panel'); $('share-title').textContent = current.name || current.login;
  await loadRecipients();
}
async function loadRecipients() {
  const recipients = await api('api/accounts/' + encodeURIComponent(current.id) + '/shares');
  $('recipients').replaceChildren();
  if (!recipients.length) $('recipients').append(element('p', '尚未分享给其他人', 'muted'));
  for (const recipient of recipients) {
    const row = element('div', null, 'recipient'), button = element('button', '撤销');
    row.append(element('span', recipientLabel(recipient) + (recipient.active === 0 ? '（已停用）' : '')), button);
    button.onclick = () => run(async () => {
      if (!confirm(`撤销对 ${recipientLabel(recipient)} 的分享？已复制的内容无法收回。`)) return;
      await api(`api/accounts/${encodeURIComponent(current.id)}/shares/${encodeURIComponent(recipient.id)}`, 'DELETE');
      await loadRecipients(); message('已撤销分享。');
    });
    $('recipients').append(row);
  }
}
async function copy(text, target) {
  const version = secretVersion;
  const active = () => version === secretVersion && !document.hidden;
  if (!active()) return;
  if (inDingTalk) {
    try {
      await loadDingSdk();
      if (!active()) return;
      await new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('clipboard timeout')), 5000);
        const success = () => { clearTimeout(timer); resolve(); };
        const fail = () => { clearTimeout(timer); reject(new Error('clipboard unavailable')); };
        if (typeof window.dd?.setClipboard !== 'function') { fail(); return; }
        try {
          const pending = window.dd.setClipboard({text, success, fail});
          if (pending?.then) pending.then(success, fail);
        } catch (_) { fail(); }
      });
      if (active()) message('已复制。');
      return;
    } catch (_) { /* Try browser capabilities when the native bridge is unavailable. */ }
  }
  if (!active()) return;
  try {
    if (!navigator.clipboard?.writeText) throw new Error('unavailable');
    await navigator.clipboard.writeText(text);
    if (active()) message('已复制。');
    return;
  } catch (_) {
    if (!active()) return;
    // HTTP WebViews may support the selection-based copy command even without Clipboard API.
    const input = element('textarea'), focused = document.activeElement;
    let copied = false;
    input.className = 'clipboard-input'; input.value = text; input.readOnly = true;
    try {
      document.body.append(input); input.focus(); input.select(); input.setSelectionRange(0, text.length);
      copied = document.execCommand('copy') === true;
    } catch (_) { /* Manual selection remains available below. */ }
    finally { input.value = ''; input.remove(); if (focused?.isConnected) focused.focus({preventScroll: true}); }
    if (!active()) return;
    if (copied) { message('已复制。'); return; }
    target.hidden = false; target.textContent = text;
    target.scrollIntoView({block: 'nearest'});
    const selection = window.getSelection(), range = document.createRange(); range.selectNodeContents(target);
    selection.removeAllRanges(); selection.addRange(range); message('当前环境不支持直接复制，已选中文本，请长按复制。');
  }
}
async function password(copying) {
  if (!current) return;
  if (!copying && $('reveal').textContent === '隐藏密码') { hideSecret(); return; }
  const version = ++secretVersion, id = current.id;
  const data = await api(`api/${scope === 'received' ? 'shared/' : ''}accounts/${encodeURIComponent(id)}/password`, 'POST', {action: copying ? 'copy' : 'view'});
  if (version !== secretVersion || document.hidden || current?.id !== id) return;
  if (copying) { await copy(data.password, $('secret')); if ($('secret').textContent !== '••••••••') $('reveal').textContent = '隐藏密码'; }
  else { $('secret').textContent = data.password || '（空密码）'; $('reveal').textContent = '隐藏密码'; }
}
async function acceptLogin(result) {
  token = result.token; mustChange = result.must_change_password;
  try { sessionStorage.removeItem('accmgr.manualLogout'); } catch (_) { }
  if (mustChange) { show('password-panel'); $('cancel-password').hidden = true; return; }
  user = await api('api/me'); $('identity').textContent = user.username;
  try { sessionStorage.setItem('accmgr.session', JSON.stringify({token, expires: result.expires_at})); } catch (_) { }
  page = 'find'; scope = 'own'; updateTabs(); await loadLibrary();
}
async function loadBindingStatus() {
  $('unbind-dingtalk').hidden = true;
  $('binding-status').textContent = dingConfig?.enabled ? '未绑定' : '钉钉免登未启用';
  $('ding-name').textContent = '未绑定'; $('refresh-binding').hidden = !dingConfig?.enabled;
  if (dingConfig?.enabled) {
    const binding = await api('api/dingtalk/binding');
    $('unbind-dingtalk').hidden = !binding.bound;
    $('binding-status').textContent = binding.bound ? '已绑定' : '未绑定';
    $('ding-name').textContent = binding.bound ? binding.ding_name || '姓名暂未同步，请刷新或联系管理员检查钉钉通讯录权限。' : '未绑定';
  }
}
function recipientLabel(recipient) { return recipient.ding_name ? `${recipient.ding_name}（${recipient.username}）` : recipient.username; }
async function navigate(destination) {
  page = destination; hideSecret(); message();
  if (page === 'my') { current = null; show('my-panel'); await loadBindingStatus(); return; }
  scope = page === 'share' ? 'outgoing' : 'own';
  $('search').value = ''; $('platform-filter').value = ''; $('tag-filter').value = '';
  updateTabs(); await loadLibrary();
}
for (const button of document.querySelectorAll('[data-page]')) button.onclick = () => run(() => navigate(button.dataset.page));
$('refresh-binding').onclick = () => run(loadBindingStatus);
$('open-sharing').onclick = () => run(openSharing);
$('share-back').onclick = () => run(() => openAccount(current));
$('copy-all').onclick = () => run(async () => {
  if (!current) return;
  hideSecret(); const version = secretVersion, record = current;
  const data = await api(`api/${scope === 'received' ? 'shared/' : ''}accounts/${encodeURIComponent(record.id)}/password`, 'POST', {action: 'copy'});
  if (version !== secretVersion || document.hidden || current?.id !== record.id) return;
  await copy(`【平台名称】${record.platform_name || ''}\n【地址】${record.platform_url || ''}\n【帐号】${record.login}\n【密码】${data.password}`, $('copy-fallback'));
});

function suppressAutoLogin() { try { sessionStorage.setItem('accmgr.manualLogout', '1'); } catch (_) { } }
function loadDingSdk() {
  if (window.dd) return Promise.resolve();
  if (!sdkLoading) sdkLoading = new Promise((resolve, reject) => {
    const script = document.createElement('script');
    const timer = setTimeout(() => reject(new Error('钉钉组件加载超时，可使用帐号密码登录。')), 10000);
    script.src = 'https://g.alicdn.com/dingding/dingtalk-jsapi/3.0.31/dingtalk.open.js';
    script.onload = () => { clearTimeout(timer); resolve(); };
    script.onerror = () => { clearTimeout(timer); reject(new Error('钉钉组件加载失败，可使用帐号密码登录。')); };
    document.head.append(script);
  }).catch(error => { sdkLoading = null; throw error; });
  return sdkLoading;
}
function dingCode() {
  if (!inDingTalk || !dingConfig?.enabled) return Promise.reject(new Error('请从钉钉工作台进入此应用。'));
  return loadDingSdk().then(() => new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('获取钉钉身份超时，请重试或使用帐号密码登录。')), 10000);
    const success = result => { clearTimeout(timer); result?.code ? resolve(result.code) : reject(new Error('未获取到钉钉授权码。')); };
    const fail = () => { clearTimeout(timer); reject(new Error('无法获取钉钉身份，请检查应用配置或使用帐号密码登录。')); };
    if (typeof window.dd?.requestAuthCode !== 'function') { fail(); return; }
    try { window.dd.requestAuthCode({corpId: dingConfig.corp_id, clientId: dingConfig.client_id, success, fail}); } catch (_) { fail(); }
  }));
}
async function dingLogin() {
  const result = await api('api/dingtalk/login', 'POST', {code: await dingCode()});
  if (result.bound === false) {
    clearSession(); $('bind-choice').hidden = false;
    message('首次使用，请填写现有帐号密码并勾选绑定。也可以直接使用密码登录。'); return;
  }
  await acceptLogin(result);
}
$('ding-login').onclick = () => run(dingLogin);
$('login-form').onsubmit = event => { event.preventDefault(); run(async () => {
  const username = $('username').value.trim(), password = $('login-password').value; $('login-password').value = '';
  if ($('bind-dingtalk').checked && inDingTalk && dingConfig?.enabled) {
    if (!confirm(`将当前钉钉身份绑定到帐号 ${username}？之后可通过钉钉直接访问该帐号库。`)) return;
    await acceptLogin(await api('api/dingtalk/bind', 'POST', {code: await dingCode(), username, password}));
  } else await acceptLogin(await api('api/login', 'POST', {username, password}));
}); };
$('logout').onclick = () => run(async () => { suppressAutoLogin(); try { await api('api/logout', 'POST'); } finally { clearSession(); } });
$('unbind-dingtalk').onclick = () => { $('unbind-form').reset(); show('unbind-panel'); };
$('cancel-unbind').onclick = () => { $('unbind-form').reset(); show('my-panel'); };
$('unbind-form').onsubmit = event => { event.preventDefault(); run(async () => {
  const password = $('unbind-password').value; $('unbind-password').value = '';
  await api('api/dingtalk/binding', 'DELETE', {password}); suppressAutoLogin(); clearSession(); message('已解除钉钉绑定，请重新登录。');
}); };
function updateTabs() { for (const button of document.querySelectorAll('[data-scope]')) button.setAttribute('aria-pressed', String(button.dataset.scope === scope)); }
for (const button of document.querySelectorAll('[data-scope]')) button.onclick = () => run(async () => {
  scope = button.dataset.scope; updateTabs(); $('search').value = ''; $('platform-filter').value = ''; $('tag-filter').value = '';
  accounts = []; renderAccounts(); await loadLibrary();
});
$('refresh').onclick = () => run(loadLibrary); $('back').onclick = () => run(loadLibrary);
$('search').oninput = renderAccounts; $('platform-filter').onchange = renderAccounts; $('tag-filter').onchange = renderAccounts;
$('reveal').onclick = () => run(() => password(false)); $('copy-password').onclick = () => run(() => password(true));
$('copy-login').onclick = () => run(() => copy(current.login, $('account-login')));
$('recipient-form').onsubmit = event => { event.preventDefault(); run(async () => {
  $('recipient-result').replaceChildren(); const matches = await api('api/share-recipients?q=' + encodeURIComponent($('recipient-name').value.trim()));
  if (!matches.length) { message('未找到可分享的用户，请输入钉钉姓名片段或完整用户名。'); return; }
  for (const recipient of matches) {
    const button = element('button', '分享给 ' + recipientLabel(recipient), 'full');
    button.onclick = () => run(async () => {
      if (!confirm(`将此帐号分享给 ${recipientLabel(recipient)}？对方可查看和复制密码。`)) return;
      await api('api/accounts/' + encodeURIComponent(current.id) + '/shares', 'POST', {recipient_id: recipient.id});
      $('recipient-result').replaceChildren(); await loadRecipients(); message('分享成功。');
    }); $('recipient-result').append(button);
  }
}); };
$('change-password').onclick = () => { $('password-form').reset(); $('cancel-password').hidden = false; show('password-panel'); };
$('cancel-password').onclick = () => { $('password-form').reset(); show('my-panel'); };
$('password-form').onsubmit = event => { event.preventDefault(); run(async () => {
  const old_password = $('old-password').value, new_password = $('new-password').value;
  if ([...new_password].length < 6) throw new Error('新密码至少 6 位。');
  if (new_password !== $('confirm-password').value) throw new Error('两次新密码不一致。');
  try { await api('api/password', 'POST', {old_password, new_password}); suppressAutoLogin(); clearSession(); message('密码已修改，请重新登录。'); }
  finally { $('password-form').reset(); }
}); };
document.addEventListener('visibilitychange', () => { if (document.hidden) hideSecret(); });
window.addEventListener('pagehide', hideSecret);
run(async () => {
  try { dingConfig = await api('api/dingtalk/config'); } catch (_) { message('暂时无法加载钉钉登录配置，可使用帐号密码登录。'); }
  $('ding-login').hidden = !inDingTalk || !dingConfig?.enabled;
  $('bind-choice').hidden = !inDingTalk || !dingConfig?.enabled;
  $('ding-hint').hidden = !inDingTalk || !dingConfig?.enabled;
  $('ding-hint').textContent = '首次绑定需验证已有帐号密码；免登失败时仍可使用密码登录。';
  let manualLogout = false;
  try { manualLogout = sessionStorage.getItem('accmgr.manualLogout') === '1'; } catch (_) { }
  if (manualLogout) { clearSession(); return; }
  if (inDingTalk && dingConfig?.enabled) { clearSession(); await dingLogin(); return; }
  let saved; try { saved = JSON.parse(sessionStorage.getItem('accmgr.session')); } catch (_) { }
  if (!saved || typeof saved.token !== 'string' || typeof saved.expires !== 'number' || saved.expires <= Date.now()) { clearSession(); return; }
  token = saved.token; user = await api('api/me'); $('identity').textContent = user.username; await loadLibrary();
});
