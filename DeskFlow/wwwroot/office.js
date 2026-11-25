const refresh = document.querySelector('[data-refresh-ms]')?.dataset.refreshMs;
if (refresh && Number(refresh) > 0) {
  window.setTimeout(() => window.location.reload(), Number(refresh));
}

const loadingStatus = document.createElement('span');
loadingStatus.className = 'sr-only';
loadingStatus.setAttribute('role', 'status');
document.body.append(loadingStatus);

let loadingTimer;

function showLoadingIfSlow() {
  if (loadingTimer !== undefined) return;
  // Give fast navigations time to finish without flashing a loading bar.
  loadingTimer = window.setTimeout(() => {
    document.body.classList.add('is-loading');
    loadingStatus.textContent = 'Зареждане…';
  }, 180);
}

window.addEventListener('submit', event => {
  if (!event.defaultPrevented && event.target.method === 'post') {
    showLoadingIfSlow();
  }
});

window.addEventListener('click', event => {
  if (event.defaultPrevented || event.button !== 0
      || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  if (event.target.closest('.dates a')) showLoadingIfSlow();
});

window.addEventListener('pagehide', () => {
  window.clearTimeout(loadingTimer);
  loadingTimer = undefined;
  document.body.classList.remove('is-loading');
  loadingStatus.textContent = '';
});
