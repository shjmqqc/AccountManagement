'use strict';
// Same-origin Apache proxy; no credentials or DingTalk app secrets belong in this file.
const API = '/accmgr-api/';
const $ = id => document.getElementById(id);
let token = '', user = null, scope = 'own', accounts = [], platforms = [], current = null;
let busy = false, mustChange = false, secretVersion = 0;
const errors = {
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
function hideSecret() { secretVersion++; $('secret').textContent = '••••••••'; $('reveal').textContent = '显示密码'; }
function show(panel) {
  hideSecret(); for (const id of ['login-panel', 'password-panel', 'library', 'detail']) $(id).hidden = id !== panel;
  $('logout').hidden = !token;
}
function clearSession() {
  token = ''; user = null; current = null; accounts = []; platforms = []; mustChange = false;
  $('accounts').replaceChildren(); $('recipients').replaceChildren(); $('recipient-result').replaceChildren();
  for (const id of ['detail-title','detail-platform','account-login','account-url','account-notes','identity']) $(id).textContent = '';
  $('detail-tags').replaceChildren();
  $('login-form').reset(); $('password-form').reset(); $('recipient-form').reset();
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
  show('library'); renderAccounts();
}
function renderAccounts() {
  const q = $('search').value.trim().toLocaleLowerCase(), platform = $('platform-filter').value, tag = $('tag-filter').value;
  const filtered = accounts.filter(a => (q ? [a.platform_name,a.name,a.login,a.notes].some(s => (s || '').toLocaleLowerCase().includes(q)) : !platform || a.platform_id === platform)
    && (!tag || (a.tags || []).some(t => t.id === tag)));
  $('accounts').replaceChildren(); $('count').textContent = filtered.length ? `${filtered.length} 个帐号` : '暂无符合条件的帐号';
  for (const a of filtered) {
    const card = element('button', null, 'card'); card.append(element('strong', a.name || a.login));
    card.append(element('span', `${a.platform_name} · ${a.login}`, 'muted'));
    if (scope === 'received') card.append(element('span', '分享人：' + a.owner_username, 'muted'));
    if (scope === 'outgoing') card.append(element('span', `已分享给 ${a.share_count} 人`, 'muted'));
    const badges = element('div'); tags(badges, a.tags); card.append(badges);
    card.onclick = () => run(() => openAccount(a)); $('accounts').append(card);
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
  $('sharing').hidden = scope === 'received'; $('recipient-result').replaceChildren(); $('recipient-form').reset();
  if (scope !== 'received') await loadRecipients();
}
async function loadRecipients() {
  const recipients = await api('api/accounts/' + encodeURIComponent(current.id) + '/shares');
  $('recipients').replaceChildren();
  if (!recipients.length) $('recipients').append(element('p', '尚未分享给其他人', 'muted'));
  for (const recipient of recipients) {
    const row = element('div', null, 'recipient'), button = element('button', '撤销');
    row.append(element('span', recipient.username + (recipient.active === 0 ? '（已停用）' : '')), button);
    button.onclick = () => run(async () => {
      if (!confirm(`撤销对 ${recipient.username} 的分享？已复制的内容无法收回。`)) return;
      await api(`api/accounts/${encodeURIComponent(current.id)}/shares/${encodeURIComponent(recipient.id)}`, 'DELETE');
      await loadRecipients(); message('已撤销分享。');
    });
    $('recipients').append(row);
  }
}
async function copy(text, target) {
  try {
    if (!navigator.clipboard?.writeText) throw new Error('unavailable');
    await navigator.clipboard.writeText(text); message('已复制。');
  } catch (_) {
    if (document.hidden) return;
    target.textContent = text;
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
$('login-form').onsubmit = event => { event.preventDefault(); run(async () => {
  const username = $('username').value.trim(), password = $('login-password').value; $('login-password').value = '';
  const result = await api('api/login', 'POST', {username, password}); token = result.token; mustChange = result.must_change_password;
  if (mustChange) { show('password-panel'); $('cancel-password').hidden = true; return; }
  user = await api('api/me'); $('identity').textContent = user.username;
  try { sessionStorage.setItem('accmgr.session', JSON.stringify({token, expires: result.expires_at})); } catch (_) { }
  scope = 'own'; updateTabs(); await loadLibrary();
}); };
$('logout').onclick = () => run(async () => { try { await api('api/logout', 'POST'); } finally { clearSession(); } });
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
  if (!matches.length) { message('未找到可分享的用户，请输入完整用户名。'); return; }
  for (const recipient of matches) {
    const button = element('button', '分享给 ' + recipient.username, 'full');
    button.onclick = () => run(async () => {
      if (!confirm(`将此帐号分享给 ${recipient.username}？对方可查看和复制密码。`)) return;
      await api('api/accounts/' + encodeURIComponent(current.id) + '/shares', 'POST', {recipient_id: recipient.id});
      $('recipient-result').replaceChildren(); await loadRecipients(); message('分享成功。');
    }); $('recipient-result').append(button);
  }
}); };
$('change-password').onclick = () => { $('password-form').reset(); $('cancel-password').hidden = false; show('password-panel'); };
$('cancel-password').onclick = () => { $('password-form').reset(); show('library'); };
$('password-form').onsubmit = event => { event.preventDefault(); run(async () => {
  const old_password = $('old-password').value, new_password = $('new-password').value;
  if ([...new_password].length < 6) throw new Error('新密码至少 6 位。');
  if (new_password !== $('confirm-password').value) throw new Error('两次新密码不一致。');
  try { await api('api/password', 'POST', {old_password, new_password}); clearSession(); message('密码已修改，请重新登录。'); }
  finally { $('password-form').reset(); }
}); };
document.addEventListener('visibilitychange', () => { if (document.hidden) hideSecret(); });
window.addEventListener('pagehide', hideSecret);
run(async () => {
  let saved; try { saved = JSON.parse(sessionStorage.getItem('accmgr.session')); } catch (_) { }
  if (!saved || typeof saved.token !== 'string' || typeof saved.expires !== 'number' || saved.expires <= Date.now()) { clearSession(); return; }
  token = saved.token; user = await api('api/me'); $('identity').textContent = user.username; await loadLibrary();
});
