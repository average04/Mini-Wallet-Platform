// Run with Node 18+: node postman/verify-auth-scripts.mjs
// Executes the collection's real token-capture scripts against controlled OIDC responses.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const collection = JSON.parse(fs.readFileSync(new URL('./MiniWallet.postman_collection.json', import.meta.url)));
const loginItems = collection.item.flatMap(folder => folder.item).filter(item => item.request.auth?.type === 'oauth2');
assert.equal(loginItems.length, 2, 'Both player and admin need a login request');
const issuer = 'http://localhost:8080/realms/mini-wallet';
const makeToken = (roles, changes = {}) => ['header', Buffer.from(JSON.stringify({
  sub: 'subject-1', iss: issuer, aud: 'mini-wallet-api', exp: Math.floor(Date.now() / 1000) + 300,
  realm_access: {roles}, ...changes,
})).toString('base64url'), 'signature'].join('.');
let cases = 0;
for (const role of ['player', 'admin']) {
  const item = loginItems.find(item => item.name.toLowerCase().includes(`as ${role}`));
  assert(item, `Missing ${role} login`);
  const script = item.event.find(event => event.listen === 'test').script.exec.join('\n');
  function execute(token, status = 200, subject = 'subject-1') {
    const variables = new Map([['oidc_issuer', issuer], ['api_audience', 'mini-wallet-api']]);
    const failures = [];
    const pm = {
      request: {auth: {oauth2: {get: key => key === 'accessToken' ? token : undefined}}},
      response: {code: status, json: () => ({sub: subject, preferred_username: `test-${role}`, email_verified: true})},
      environment: {get: key => variables.get(key), set: (key, value) => variables.set(key, value)},
      variables: {replaceIn: value => value, get: key => variables.get(key)},
      test: (name, fn) => { try { fn(); } catch (error) { failures.push(`${name}: ${error.message}`); } },
    };
    let error;
    try { vm.runInNewContext(script, {pm, atob, Date}); } catch (e) { error = e; }
    return {variables, failures, error};
  }
  const correct = makeToken(role === 'admin' ? ['player', 'admin'] : ['player']);
  const success = execute(correct);
  assert.ifError(success.error);
  assert.deepEqual(success.failures, []);
  assert.equal(success.variables.get(`${role}_token`), correct);
  assert.equal(success.variables.has(`${role === 'player' ? 'admin' : 'player'}_token`), false);
  cases++;
  for (const [token, status, subject] of [
    [correct, 401],
    [makeToken(role === 'admin' ? ['player'] : ['player', 'admin']), 200],
    [correct, 200, 'different-subject'],
    [makeToken(['player','admin'], {aud:'wrong-api'}), 200],
    [makeToken(['player','admin'], {exp:1}), 200],
    ['', 200],
  ]) {
    const rejected = execute(token, status, subject);
    assert.equal(rejected.variables.has(`${role}_token`), false, 'Invalid login must not save a token');
    assert(rejected.error || rejected.failures.length, 'Invalid login must report a failure');
    cases++;
  }
}
console.log(`${cases} token-capture checks passed.`);
