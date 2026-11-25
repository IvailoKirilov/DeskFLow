const refresh = document.querySelector('[data-refresh-ms]')?.dataset.refreshMs;
if (refresh && Number(refresh) > 0) {
  window.setTimeout(() => window.location.reload(), Number(refresh));
}
