// Service Worker in der Entwicklung (Vorlage): immer aus dem Netz, ohne Offline-Unterstützung, damit Änderungen
// sofort sichtbar sind. Veröffentlicht gilt service-worker.published.js. /api/** bedient er nie.
self.addEventListener('fetch', () => { });
self.addEventListener('message', event => {
    if (event.data && event.data.typ === 'aktivieren') {
        self.skipWaiting();
    }
});

// Story 6-1 (AD-21): Ein Push zeigt eine Benachrichtigung (Titel, Text, Ziel aus der eigenen App) und sagt offenen Fenstern Bescheid, damit die
// Kanal-Seite neu lädt; ein Tipp auf die Benachrichtigung öffnet oder zeigt die Kanal-Seite. Fremde Adressen öffnet er nie.
self.addEventListener('push', event => {
    let daten = {};
    try {
        daten = event.data ? event.data.json() : {};
    } catch {
        daten = {};
    }
    const url = eigeneAdresse(daten.url);
    event.waitUntil((async () => {
        await self.registration.showNotification(daten.titel || 'Zeltlotse', { body: daten.text || '', icon: 'icon-192.png', data: { url } });
        const fenster = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const f of fenster) {
            f.postMessage({ typ: 'push', url });
        }
    })());
});
self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = eigeneAdresse(event.notification.data && event.notification.data.url);
    event.waitUntil((async () => {
        const fenster = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const f of fenster) {
            if (f.url === url && 'focus' in f) {
                return f.focus();
            }
        }
        return self.clients.openWindow(url);
    })());
});

// Nur Adressen der eigenen App unter /app/; sonst die Startseite der App.
function eigeneAdresse(pfad) {
    try {
        const adresse = new URL(pfad || '/app/', self.location.origin);
        if (adresse.origin === self.location.origin && adresse.pathname.startsWith('/app/')) {
            return adresse.href;
        }
    } catch {
        // Unlesbar: Startseite.
    }
    return new URL('/app/', self.location.origin).href;
}
