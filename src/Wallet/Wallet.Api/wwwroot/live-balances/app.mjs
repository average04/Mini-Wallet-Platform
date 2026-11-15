import { createBalanceSync } from './balance-sync.mjs';

const element = id => document.getElementById(id);
let connection = null;
let sync = null;
let poll = null;
let expiry = null;
let sessionAbort = null;

function status(text, message) {
  element('status').textContent = text;
  element('status').dataset.connected = String(text === 'Connected');
  element('message').textContent = message;
}
function log(text) {
  const line = document.createElement('li');
  line.textContent = `${new Date().toLocaleTimeString()} · ${text}`;
  element('events').prepend(line);
  while (element('events').children.length > 30) element('events').lastChild.remove();
}
function clearSession() {
  clearInterval(poll);
  clearTimeout(expiry);
  sessionAbort?.abort();
  sessionAbort = null;
  sync?.stop();
  sync = null;
  element('connect').disabled = false;
  element('disconnect').disabled = true;
}
async function disconnect(message = 'Connection stopped.') {
  const previous = connection;
  connection = null;
  clearSession();
  status('Disconnected', message);
  log(message);
  await previous?.stop();
}

element('disconnect').addEventListener('click', () => void disconnect());
element('connect').addEventListener('click', async () => {
  const token = element('token').value.trim().replace(/^Bearer\s+/i, '');
  if (!token) { status('Disconnected', 'Paste a player access token first.'); return; }
  element('token').value = '';
  element('connect').disabled = true;
  element('disconnect').disabled = false;
  for (const currency of ['USD', 'EUR', 'BTC']) element(currency).textContent = '—';
  element('updated').textContent = 'Loading your balances';
  status('Connecting', 'Authenticating with the Wallet API…');
  sessionAbort = new AbortController();
  const sessionSignal = sessionAbort.signal;
  const current = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/balances', {
      accessTokenFactory: () => token,
      transport: signalR.HttpTransportType.WebSockets,
      skipNegotiation: true
    })
    // The transport URL contains the browser token. Keep it out of console logs.
    .configureLogging(signalR.LogLevel.None)
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .build();
  connection = current;

  current.on('Ready', () => {
    if (connection !== current) return;
    sync?.stop();
    sync = createBalanceSync({
      load: async () => {
        const response = await fetch('/api/v1/wallets/me', {
          headers: { Authorization: `Bearer ${token}` }, cache: 'no-store',
          signal: AbortSignal.any([sessionSignal, AbortSignal.timeout(5000)])
        });
        if (!response.ok) { const error = new Error('Balance read failed'); error.status = response.status; throw error; }
        return response.json();
      },
      render: balances => {
        for (const balance of balances) {
          if (['USD', 'EUR', 'BTC'].includes(balance.currency)) element(balance.currency).textContent = balance.balance;
        }
        element('updated').textContent = `Updated ${new Date().toLocaleTimeString()}`;
        status('Connected', 'Listening for your balance changes.');
      },
      onError: error => {
        if (error.status === 401 || error.status === 403) void disconnect('Session ended. Paste a fresh player token.');
        else if (!sessionSignal.aborted) {
          element('message').textContent = 'Balance refresh failed. Retrying on the next update or periodic refresh.';
          log('Balance refresh unavailable.');
        }
      }
    });
    status('Connected', 'Loading your current balances…');
    log('Player connected. Reloading balances.');
    void sync.refresh();
  });
  current.on('BalanceChanged', message => {
    if (connection !== current) return;
    log(`${message.currency} balance changed to ${message.balanceAfter}. Reloading current balances.`);
    // Notifications are advisory and can arrive out of order. Never overwrite a snapshot with one.
    void sync?.refresh();
  });
  current.onreconnecting(() => {
    if (connection !== current) return;
    sync?.stop();
    sync = null;
    status('Reconnecting', 'Waiting for the Wallet API. Balances reload when the connection returns.');
    log('Connection interrupted.');
  });
  current.onclose(() => {
    if (connection !== current) return;
    connection = null;
    clearSession();
    status('Disconnected', 'Live updates unavailable. Check balances in Swagger or Postman; retry with a valid player token when the connection is available.');
    log('Connection closed.');
  });
  try {
    await current.start();
    if (connection !== current) return;
    poll = setInterval(() => void sync?.refresh(), 30000);
    try {
      const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const claims = JSON.parse(atob(payload.padEnd(Math.ceil(payload.length / 4) * 4, '=')));
      if (Number.isFinite(claims.exp)) expiry = setTimeout(() => void disconnect('Token expired. Paste a fresh player token.'), Math.max(0, claims.exp * 1000 - Date.now()));
    } catch { /* Only the server decides whether a token is valid. */ }
  } catch {
    if (connection === current) await disconnect('Live updates unavailable. Check balances in Swagger or Postman; retry with a valid player token when the connection is available.');
  }
});
