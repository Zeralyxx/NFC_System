import assert from 'node:assert/strict';

const project = 'demo-nfc-remote-tests';
if (process.env.GCLOUD_PROJECT !== project || process.env.FIRESTORE_EMULATOR_HOST !== '127.0.0.1:18085') {
  throw new Error('Run only with the dedicated demo-project Firestore emulator configuration.');
}
const base = `http://127.0.0.1:18085/v1/projects/${project}/databases/(default)/documents`;
const site = '12345678901234567890123456789012';
const root = `remote_sites/${site}`;
const text = stringValue => ({ stringValue });
const member = (role, email, active = true) => ({ role: text(role), email: text(email), name: text('Approved Account'), active: { booleanValue: active } });
function token(uid, verified = true) {
  const encoded = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  return `${encoded({ alg: 'none', typ: 'JWT' })}.${encoded({ aud: project, iss: `https://securetoken.google.com/${project}`, sub: uid,
    user_id: uid, email: `${uid}@example.test`, email_verified: verified, iat: now, exp: now + 3600,
    firebase: { sign_in_provider: 'password', identities: {} } })}.`;
}
async function call(method, path, fields, identity, expected) {
  const headers = { 'Content-Type': 'application/json' };
  if (identity) headers.Authorization = `Bearer ${identity}`;
  const response = await fetch(`${base}/${path}`, { method, headers, body: fields ? JSON.stringify({ fields }) : undefined });
  const body = await response.text();
  assert.equal(response.status, expected, `${method} ${path}: ${body}`);
}
const seed = (path, fields) => call('PATCH', path, fields, 'owner', 200);
await seed(`${root}/members/server`, member('server', 'server@example.test'));
await seed(`${root}/members/admin`, member('administrator', 'admin@example.test'));
await seed(`${root}/members/other`, member('administrator', 'other@example.test'));
await seed('cloud_configuration/primary', { siteId: text(site), serverUid: text('server') });
await seed(`${root}/profiles/one`, { profile: text('{"StudentId":"one"}') });
const server = token('server'), admin = token('admin'), other = token('other');
let passed = 0;
async function test(name, action) { await action(); passed++; process.stdout.write(`PASS ${name}\n`); }
const request = (uid = 'admin') => ({ ownerUid: text(uid), status: text('Pending'), envelope: text('encrypted-request'), createdAt: text(new Date().toISOString()) });
const id = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
await test('Unauthenticated and unapproved accounts cannot read profiles', async () => {
  await call('GET', `${root}/profiles/one`, null, null, 403);
  await call('GET', `${root}/profiles/one`, null, token('stranger'), 403);
  await call('GET', `${root}/profiles/one`, null, token('admin', false), 403);
});
await test('Approved remote admin can read but cannot overwrite a server profile', async () => {
  await call('GET', `${root}/profiles/one`, null, admin, 200);
  await call('PATCH', `${root}/profiles/one`, { profile: text('forged') }, admin, 403);
});
await test('Admin submits only its own pending request', async () => {
  await call('PATCH', `${root}/requests/${id}`, request(), admin, 200);
  await call('PATCH', `${root}/requests/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb`, request('other'), admin, 403);
});
await test('Admin cannot edit a submitted envelope or forge an applied result', async () => {
  await call('PATCH', `${root}/requests/${id}`, { ...request(), envelope: text('changed') }, admin, 403);
  await call('PATCH', `${root}/requests/${id}?updateMask.fieldPaths=status`, { status: text('Applied') }, admin, 403);
});
await test('Request reads are owner-scoped', async () => {
  await call('GET', `${root}/requests/${id}`, null, admin, 200);
  await call('GET', `${root}/requests/${id}`, null, other, 403);
  await call('GET', `${root}/requests/${id}`, null, server, 200);
});
await test('Queries must restrict request ownership unless made by the server', async () => {
  async function query(identity, field, value, expected) {
    const structuredQuery = { from: [{ collectionId: 'requests' }], limit: 100,
      orderBy: [{ field: { fieldPath: '__name__' }, direction: 'ASCENDING' }] };
    if (field) structuredQuery.where = { fieldFilter: { field: { fieldPath: field }, op: 'EQUAL', value: text(value) } };
    const response = await fetch(`${base}/${root}:runQuery`, { method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${identity}` }, body: JSON.stringify({ structuredQuery }) });
    const body = await response.text(); assert.equal(response.status, expected, body);
  }
  await query(admin, 'ownerUid', 'admin', 200);
  await query(admin, null, null, 403);
  await query(server, 'status', 'Pending', 200);
});
await test('Server can acknowledge without replacing the envelope', async () => {
  await call('PATCH', `${root}/requests/${id}?updateMask.fieldPaths=status&updateMask.fieldPaths=message`,
    { status: text('Applied'), message: text('Confirmed by main server') }, server, 200);
  await call('PATCH', `${root}/requests/${id}?updateMask.fieldPaths=envelope`, { envelope: text('tampered') }, server, 403);
});
await test('Admins cannot approve themselves or grant server privileges', async () => {
  await call('PATCH', `${root}/members/admin`, member('server', 'admin@example.test'), admin, 403);
  await call('PATCH', `${root}/members/stranger`, member('administrator', 'stranger@example.test'), admin, 403);
  await call('PATCH', `${root}/members/server`, member('administrator', 'server@example.test'), server, 403);
});
await test('Main server approves and revokes remote administrators', async () => {
  await call('PATCH', `${root}/members/new-admin`, member('administrator', 'new-admin@example.test'), server, 200);
  await call('PATCH', `${root}/members/admin`, member('administrator', 'admin@example.test', false), server, 200);
  await call('GET', `${root}/profiles/one`, null, admin, 403);
  await call('PATCH', `${root}/requests/cccccccccccccccccccccccccccccccc`, request(), admin, 403);
});
await test('Legacy archives require the designated server account and cannot be deleted', async () => {
  await call('PATCH', 'alerts/test', { message: text('Audit record') }, server, 200);
  await call('GET', 'alerts/test', null, other, 403);
  await call('GET', 'alerts/test', null, null, 403);
  await call('DELETE', 'alerts/test', null, server, 403);
});
await test('Site membership does not grant access to another site', async () => {
  await call('GET', 'remote_sites/ffffffffffffffffffffffffffffffff/profiles/one', null, other, 403);
});
process.stdout.write(`${passed} rule groups passed. No production services contacted.\n`);
