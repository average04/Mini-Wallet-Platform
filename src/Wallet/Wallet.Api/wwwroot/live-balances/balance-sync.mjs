// Serialize reads and discard a snapshot if a newer event arrived while it was loading.
// A separate instance is used after every connection/reconnection.
export function createBalanceSync({ load, render, onError }) {
  let active = true;
  let revision = 0;
  let running = null;

  async function drain() {
    try {
      while (active) {
        const requested = revision;
        try {
          const balances = await load();
          if (active && requested === revision) render(balances);
        } catch (error) {
          if (active && requested === revision) onError(error);
        }
        if (requested === revision) break;
      }
    } finally { running = null; }
  }

  return {
    refresh() {
      if (!active) return Promise.resolve();
      revision++;
      if (!running) running = Promise.resolve().then(drain);
      return running;
    },
    stop() { active = false; }
  };
}
