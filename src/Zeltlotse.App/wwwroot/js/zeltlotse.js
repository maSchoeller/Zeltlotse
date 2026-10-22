// Das einzige Interop-Modul der Web-App (AD-29), nur über die C#-Schnittstelle IBrowser erreichbar.
// Inhalt: Online-Status mit Ereignissen, Zeitpunkt der letzten erfolgreichen API-Antwort (localStorage),
// Neu laden mit Aktivieren eines wartenden Service Workers, Drucken und Datei speichern (Datenauskunft). Story 6-1: Web-Push (Status, Abo lesen,
// abonnieren, abmelden, eingehende Pushes beobachten) und ans Ende scrollen. Die Erlaubnis für Benachrichtigungen fragt nur pushAbonnieren ab.
// Story 6-4: Text in die Zwischenablage kopieren (Link des Teilnehmer-Zugangs). Story 6-5: Offline-Speicher (AD-18) in IndexedDB, verschlüsselt
// mit einem nicht exportierbaren WebCrypto-Schlüssel des Geräts.

const zuletztSchluessel = 'zeltlotse.zuletztAktualisiert';

export function istOnline() {
    return navigator.onLine;
}

// Meldet Wechsel des Online-Status an .NET (Methode „OnlineGeaendert“); liefert ein Objekt zum Beenden.
export function onlineBeobachten(empfaenger) {
    const online = () => empfaenger.invokeMethodAsync('OnlineGeaendert', true);
    const offline = () => empfaenger.invokeMethodAsync('OnlineGeaendert', false);
    window.addEventListener('online', online);
    window.addEventListener('offline', offline);
    return {
        beenden: () => {
            window.removeEventListener('online', online);
            window.removeEventListener('offline', offline);
        }
    };
}

export function zuletztAktualisiertLesen() {
    try {
        return localStorage.getItem(zuletztSchluessel);
    } catch {
        return null;
    }
}

export function zuletztAktualisiertSchreiben(wert) {
    try {
        localStorage.setItem(zuletztSchluessel, wert);
    } catch {
        // Speicher nicht verfügbar (z. B. privater Modus): dann eben ohne Zeitpunkt.
    }
}

// Lädt die App neu; wartet ein neuer Service Worker, wird er vorher aktiviert.
export async function neuLaden() {
    const registrierung = 'serviceWorker' in navigator ? await navigator.serviceWorker.getRegistration() : undefined;
    if (registrierung && registrierung.waiting) {
        const gewechselt = new Promise(fertig => {
            navigator.serviceWorker.addEventListener('controllerchange', fertig, { once: true });
            setTimeout(fertig, 3000);
        });
        registrierung.waiting.postMessage({ typ: 'aktivieren' });
        await gewechselt;
    }
    location.reload();
}

// Öffnet den Druckdialog des Browsers; dort lässt sich „Als PDF speichern“ wählen (Konvention „Druck“, keine PDF-Bibliothek).
export function drucken() {
    window.print();
}

// Bietet Bytes als Datei an: Blob, Objekt-URL und Klick auf einen Link mit download-Attribut; kein Pop-up, kein Inline-Skript.
export function dateiSpeichern(name, medientyp, inhalt) {
    const blob = new Blob([inhalt], { type: medientyp });
    const adresse = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = adresse;
    link.download = name;
    link.hidden = true;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(adresse), 10000);
}

// ---------- Story 6-1: Web-Push (AD-21) ----------

function istIphoneOderIpad() {
    return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
}

function istVomHomeBildschirm() {
    return window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
}

function kannPush() {
    return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
}

// Die Erlaubnis für Mitteilungen: „granted“, „denied“ oder „default“. Manche Browser (z. B. ohne Bildschirm) melden über Notification.permission
// „denied“, obwohl die Permissions-API die Erlaubnis kennt; dann gilt die Permissions-API.
async function erlaubnis() {
    if (Notification.permission === 'granted') {
        return 'granted';
    }

    try {
        const status = await navigator.permissions.query({ name: 'notifications' });
        return status.state === 'prompt' ? 'default' : status.state;
    } catch {
        return Notification.permission;
    }
}

async function aboDesBrowsers() {
    const registrierung = await navigator.serviceWorker.getRegistration();
    return registrierung ? await registrierung.pushManager.getSubscription() : null;
}

function aboDaten(abo) {
    const json = abo.toJSON();
    return { endpunkt: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
}

function base64UrlZuBytes(text) {
    const base64 = (text + '='.repeat((4 - text.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
    return Uint8Array.from(atob(base64), zeichen => zeichen.charCodeAt(0));
}

// „an“, „aus“, „abgelehnt“, „homebildschirm“ (iPhone im Browser) oder „nicht“; fragt nie nach der Erlaubnis.
export async function pushStatus() {
    if (!kannPush()) {
        return istIphoneOderIpad() && !istVomHomeBildschirm() ? 'homebildschirm' : 'nicht';
    }

    const stand = await erlaubnis();
    if (stand === 'denied') {
        return 'abgelehnt';
    }

    try {
        return stand === 'granted' && await aboDesBrowsers() ? 'an' : 'aus';
    } catch {
        return 'aus';
    }
}

export async function pushAboLesen() {
    if (!kannPush() || await erlaubnis() !== 'granted') {
        return null;
    }

    try {
        const abo = await aboDesBrowsers();
        return abo ? aboDaten(abo) : null;
    } catch {
        return null;
    }
}

// Nur nach einem Tipp auf „Benachrichtigungen einschalten“: Erlaubnis erfragen, dann mit dem öffentlichen VAPID-Schlüssel abonnieren.
export async function pushAbonnieren(oeffentlicherSchluessel) {
    if (!kannPush() || (await erlaubnis() !== 'granted' && await Notification.requestPermission() !== 'granted')) {
        return null;
    }

    try {
        const registrierung = await navigator.serviceWorker.ready;
        const abo = await registrierung.pushManager.getSubscription()
            ?? await registrierung.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64UrlZuBytes(oeffentlicherSchluessel) });
        return aboDaten(abo);
    } catch {
        return null;
    }
}

export async function pushAbmelden() {
    if (!kannPush()) {
        return null;
    }

    try {
        const abo = await aboDesBrowsers();
        if (!abo) {
            return null;
        }

        const endpunkt = abo.endpoint;
        await abo.unsubscribe();
        return endpunkt;
    } catch {
        return null;
    }
}

// Meldet jeden Push, den der Service Worker an die offenen Fenster weitergibt (Methode „PushEingegangen“); liefert ein Objekt zum Beenden.
export function pushBeobachten(empfaenger) {
    const nachricht = event => {
        if (event.data && event.data.typ === 'push') {
            empfaenger.invokeMethodAsync('PushEingegangen');
        }
    };
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.addEventListener('message', nachricht);
    }
    return {
        beenden: () => {
            if ('serviceWorker' in navigator) {
                navigator.serviceWorker.removeEventListener('message', nachricht);
            }
        }
    };
}

export function ansEndeScrollen(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollIntoView({ block: 'end' });
    }
}

// ---------- Story 6-4: Link kopieren ----------

// Legt den Text in die Zwischenablage (nur nach einem Tipp, z. B. „Link kopieren“); liefert, ob es geklappt hat. Ohne Clipboard-API oder ohne Erlaubnis false.
export async function kopieren(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

// ---------- Story 6-5: Offline-Speicher (AD-18) ----------
// Datenbank „zeltlotse-offline“ mit zwei Ablagen: „schluessel“ hält den CryptoKey (AES-GCM 256, extractable: false, auf dem Gerät erzeugt),
// „daten“ die Datensätze { schluessel, freizeitId, ablauf, iv, inhalt }. Klartext liegt nie in IndexedDB: Jeder Datensatz hat einen eigenen
// zufälligen IV, und der Schlüssel des Datensatzes ist als zusätzliche Angabe gebunden (ein vertauschter Datensatz entschlüsselt nicht).
// Abgelaufenes (ablauf <= jetzt) wird vor jedem Lesen und beim Aufräumen gelöscht. Die Verbindung zur Datenbank lebt nur je Aufruf, damit das
// Löschen der ganzen Datenbank nie blockiert.

const offlineName = 'zeltlotse-offline';
const offlineDaten = 'daten';
const offlineSchluesselAblage = 'schluessel';
const offlineSchluesselId = 'geraet';

function anfrage(request) {
    return new Promise((fertig, fehler) => {
        request.onsuccess = () => fertig(request.result);
        request.onerror = () => fehler(request.error);
    });
}

function fertigGeschrieben(transaktion) {
    return new Promise((fertig, fehler) => {
        transaktion.oncomplete = () => fertig();
        transaktion.onerror = () => fehler(transaktion.error);
        transaktion.onabort = () => fehler(transaktion.error);
    });
}

function offlineOeffnen() {
    const request = indexedDB.open(offlineName, 1);
    request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains(offlineDaten)) {
            db.createObjectStore(offlineDaten, { keyPath: 'schluessel' }).createIndex('freizeitId', 'freizeitId');
        }
        if (!db.objectStoreNames.contains(offlineSchluesselAblage)) {
            db.createObjectStore(offlineSchluesselAblage);
        }
    };
    return anfrage(request);
}

async function mitDatenbank(arbeit) {
    const db = await offlineOeffnen();
    try {
        return await arbeit(db);
    } finally {
        db.close();
    }
}

function schluesselLesen(db) {
    return anfrage(db.transaction(offlineSchluesselAblage).objectStore(offlineSchluesselAblage).get(offlineSchluesselId));
}

// Der Schlüssel des Geräts; fehlt er, wird er erzeugt. Erzeugen zwei Fenster gleichzeitig einen, gilt der zuerst abgelegte (add statt put).
async function geraeteSchluessel(db) {
    const vorhanden = await schluesselLesen(db);
    if (vorhanden) {
        return vorhanden;
    }

    const neu = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
    try {
        const transaktion = db.transaction(offlineSchluesselAblage, 'readwrite');
        transaktion.objectStore(offlineSchluesselAblage).add(neu, offlineSchluesselId);
        await fertigGeschrieben(transaktion);
        return neu;
    } catch {
        return await schluesselLesen(db);
    }
}

function zusatz(schluessel) {
    return new TextEncoder().encode(schluessel);
}

async function entschluesseln(db, datensatz) {
    const schluessel = await schluesselLesen(db);
    if (!schluessel) {
        return null;
    }

    try {
        const klar = await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv: datensatz.iv, additionalData: zusatz(datensatz.schluessel) }, schluessel, datensatz.inhalt);
        return new TextDecoder().decode(klar);
    } catch {
        return null;
    }
}

async function entfernen(db, schluessel) {
    const transaktion = db.transaction(offlineDaten, 'readwrite');
    transaktion.objectStore(offlineDaten).delete(schluessel);
    await fertigGeschrieben(transaktion);
}

function abgelaufen(datensatz) {
    return datensatz.ablauf <= Date.now();
}

export function offlineSchreiben(schluessel, freizeitId, ablaufMs, text) {
    return mitDatenbank(async db => {
        const geraet = await geraeteSchluessel(db);
        const iv = crypto.getRandomValues(new Uint8Array(12));
        const inhalt = await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: zusatz(schluessel) }, geraet, new TextEncoder().encode(text));
        const transaktion = db.transaction(offlineDaten, 'readwrite');
        transaktion.objectStore(offlineDaten).put({ schluessel, freizeitId, ablauf: ablaufMs, iv, inhalt });
        await fertigGeschrieben(transaktion);
    });
}

// Der Text oder null; Abgelaufenes und nicht Lesbares wird dabei gelöscht.
export function offlineLesen(schluessel) {
    return mitDatenbank(async db => {
        const datensatz = await anfrage(db.transaction(offlineDaten).objectStore(offlineDaten).get(schluessel));
        if (!datensatz) {
            return null;
        }

        const text = abgelaufen(datensatz) ? null : await entschluesseln(db, datensatz);
        if (text === null) {
            await entfernen(db, schluessel);
        }
        return text;
    });
}

// Alle nicht abgelaufenen Datensätze mit diesem Präfix als [{ schluessel, text }], nach Schlüssel geordnet; Abgelaufenes wird dabei gelöscht.
export function offlineListe(praefix) {
    return mitDatenbank(async db => {
        const alle = await anfrage(db.transaction(offlineDaten).objectStore(offlineDaten).getAll());
        const passend = alle.filter(d => d.schluessel.startsWith(praefix)).sort((a, b) => (a.schluessel < b.schluessel ? -1 : 1));
        const ergebnis = [];
        for (const datensatz of passend) {
            const text = abgelaufen(datensatz) ? null : await entschluesseln(db, datensatz);
            if (text === null) {
                await entfernen(db, datensatz.schluessel);
            } else {
                ergebnis.push({ schluessel: datensatz.schluessel, text });
            }
        }
        return ergebnis;
    });
}

export function offlineEntfernen(schluessel) {
    return mitDatenbank(db => entfernen(db, schluessel));
}

export function offlineFreizeitLoeschen(freizeitId) {
    return mitDatenbank(async db => {
        const transaktion = db.transaction(offlineDaten, 'readwrite');
        const ablage = transaktion.objectStore(offlineDaten);
        const schluessel = await anfrage(ablage.index('freizeitId').getAllKeys(freizeitId));
        schluessel.forEach(s => ablage.delete(s));
        await fertigGeschrieben(transaktion);
    });
}

export function offlineAufraeumen() {
    return mitDatenbank(async db => {
        const transaktion = db.transaction(offlineDaten, 'readwrite');
        const ablage = transaktion.objectStore(offlineDaten);
        const alle = await anfrage(ablage.getAll());
        alle.filter(abgelaufen).forEach(d => ablage.delete(d.schluessel));
        await fertigGeschrieben(transaktion);
    });
}

// Löscht die ganze Datenbank samt Schlüssel. Hält ein anderes Fenster gerade eine Verbindung, läuft das Löschen nach deren Ende weiter.
export function offlineAllesLoeschen() {
    return new Promise((fertig, fehler) => {
        const request = indexedDB.deleteDatabase(offlineName);
        request.onsuccess = () => fertig();
        request.onerror = () => fehler(request.error);
    });
}

// Bittet den Browser, die Daten nicht vorzeitig zu räumen; die Antwort ändert nichts am Ablauf.
export async function speicherHalten() {
    try {
        if (navigator.storage && navigator.storage.persist) {
            await navigator.storage.persist();
        }
    } catch {
        // Nicht unterstützt: Die Daten bleiben trotzdem, solange der Browser sie lässt.
    }
}
