import test from 'node:test';
import assert from 'node:assert/strict';
import { createBalanceSync } from '../src/Wallet/Wallet.Api/wwwroot/live-balances/balance-sync.mjs';

function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

test('an update during a pending read discards the old result and reloads', async () => {
  const first = deferred(), second = deferred();
  const started = deferred();
  const reads = [first, second];
  const rendered = [];
  const sync = createBalanceSync({ load: () => { started.resolve(); return reads.shift().promise; }, render: value => rendered.push(value), onError: assert.fail });
  const finished = sync.refresh();
  await started.promise;
  sync.refresh();
  first.resolve([{ currency: 'USD', balance: '90' }]);
  await Promise.resolve();
  assert.deepEqual(rendered, []);
  second.resolve([{ currency: 'USD', balance: '105' }]);
  await finished;
  assert.deepEqual(rendered, [[{ currency: 'USD', balance: '105' }]]);
});

test('disconnect ignores an in-flight balance response', async () => {
  const pending = deferred();
  const started = deferred();
  const rendered = [];
  const sync = createBalanceSync({ load: () => { started.resolve(); return pending.promise; }, render: value => rendered.push(value), onError: assert.fail });
  const finished = sync.refresh();
  await started.promise;
  sync.stop();
  pending.resolve([{ currency: 'USD', balance: '90' }]);
  await finished;
  await sync.refresh();
  assert.deepEqual(rendered, []);
});

test('a failed read can recover on the next notification or periodic refresh', async () => {
  const failure = new Error('temporarily unavailable');
  let attempt = 0;
  const errors = [], rendered = [];
  const sync = createBalanceSync({
    load: async () => { if (++attempt === 1) throw failure; return [{ currency: 'BTC', balance: '0.00000001' }]; },
    render: value => rendered.push(value), onError: error => errors.push(error)
  });
  await sync.refresh();
  await sync.refresh();
  assert.deepEqual(errors, [failure]);
  assert.deepEqual(rendered, [[{ currency: 'BTC', balance: '0.00000001' }]]);
});

test('an update queued as a read completes still triggers another read', async () => {
  let reads = 0;
  const rendered = [];
  const sync = createBalanceSync({
    load: async () => ++reads,
    render: value => {
      rendered.push(value);
      if (value === 1) queueMicrotask(() => sync.refresh());
    },
    onError: assert.fail
  });
  await sync.refresh();
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(rendered, [1, 2]);
});
