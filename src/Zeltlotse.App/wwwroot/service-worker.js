// Service Worker in der Entwicklung (Vorlage): immer aus dem Netz, ohne Offline-Unterstützung, damit Änderungen
// sofort sichtbar sind. Veröffentlicht gilt service-worker.published.js. /api/** bedient er nie.
self.addEventListener('fetch', () => { });
self.addEventListener('message', event => {
    if (event.data && event.data.typ === 'aktivieren') {
        self.skipWaiting();
    }
});
