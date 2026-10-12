import assert from 'node:assert/strict';
const base = 'http://127.0.0.1:18084/accmgr-api/';
async function request(path, token, method='GET', body, origin='http://127.0.0.1:18084', expected=200) {
  const headers = {Origin:origin}; if(token) headers.Authorization = 'Bearer '+token;
  if(body) headers['Content-Type']='application/json';
  const res=await fetch(base+path,{method,headers,body:body?JSON.stringify(body):undefined});
  assert.equal(res.status,expected,path); assert.equal(res.headers.get('cache-control'),'no-store');
  assert.equal(res.headers.get('access-control-allow-origin'),null); return res.json();
}
await request('api/login',null,'POST',{username:'desktop-test',password:'test123456'},'https://untrusted.example',403);
await request('api/login',null,'POST',{username:'desktop-test',password:'test123456'},'null',403);
await request('api/login',null,'POST',{username:'desktop-test',password:'test123456'},'http://127.0.0.1:18084.evil.example',403);
const owner=(await request('api/login',null,'POST',{username:'desktop-test',password:'test123456'})).token;
const receiver=(await request('api/login',null,'POST',{username:'other-test',password:'test123456'})).token;
const receiverId=(await request('api/me',receiver)).id;
const platform=(await request('api/platforms',owner,'POST',{name:'移动端验证平台',url:'https://example.com'})).id;
const tag=await request('api/tags',owner,'POST',{name:'测试帐号',color:'blue'});
const account=(await request('api/accounts',owner,'POST',{platform_id:platform,name:'手机端测试帐号',login:'test@example.com',password:'mobile-test-secret',notes:'仅为本地测试数据 <script>alert(1)</script>',tag_ids:[tag.id]})).id;
await request(`api/accounts/${account}/shares`,owner,'POST',{recipient_id:receiverId});
assert.equal((await request('api/accounts',owner))[0].share_count,1);
assert.equal((await request('api/shared/accounts',receiver))[0].id,account);
assert.equal((await request(`api/shared/accounts/${account}/password`,receiver,'POST',{action:'copy'})).password,'mobile-test-secret');
await request(`api/accounts/${account}/shares/${receiverId}`,owner,'DELETE');
await request(`api/shared/accounts/${account}/password`,receiver,'POST',{action:'view'},undefined,404);
assert.equal((await request('api/shared/accounts',receiver)).length,0);
// Leave only disposable fixture records so the browser can test both sides of a share.
await request(`api/accounts/${account}/shares`,owner,'POST',{recipient_id:receiverId});
console.log('PASS H5 same-origin proxy, rejected foreign/null origins, login, outgoing count, grant, shared password and revocation.');
