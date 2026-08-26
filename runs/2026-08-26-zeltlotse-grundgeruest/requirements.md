# Anforderungen — Zeltlotse: Grundgerüst

**Lauf:** 2026-08-26-zeltlotse-grundgeruest
**Herkunft der Entscheidungen:** Grilling-Sitzung vom 2026-08-26, vom Nutzer
freigegeben. Was dort entschieden wurde, ist hier zitiert und wird nicht erneut
erhoben. Technische Umsetzung steht in `design.md`, nicht hier.

## Problem

Christliche Freizeiten — Kinder-, Jugend- und Gemeindefreizeiten — werden von
wechselnden ehrenamtlichen Teams geplant, verteilt über Tabellen, Ordner und
private Postfächer. Zeltlotse soll dafür die gemeinsame Plattform werden, die
mehrere Träger nebeneinander bedient.

Dieser Lauf baut bewusst noch keine Fachlichkeit. Er baut das tragende Gerüst:
**wer darf was, im Namen welcher Organisation, und wo leben die Daten.** Diese
Fragen lassen sich später nicht nachrüsten — eine Anwendung, die erst Funktionen
bekommt und dann Mandantentrennung, ist entweder mandantenblind oder muss
komplett umgebaut werden. Deshalb zuerst das Fundament, sichtbar und benutzbar
an genau zwei Dingen: Organisationen und Freizeiten.

## Nutzer und Kontext

| Rolle | Wer das ist | Situation |
|---|---|---|
| Betreiber (GlobalAdmin) | der Anbieter der Plattform | nimmt neue Träger auf, verwaltet Konten, sieht keine Inhalte |
| Organisationsleitung (OrgAdmin) | Hauptamtliche oder Verantwortliche einer Gemeinde bzw. eines Werks | legt die Freizeiten des Jahres an und besetzt die Leitungen |
| Freizeitleitung | verantwortet eine einzelne Freizeit | stellt ihr Team zusammen, pflegt die Eckdaten |
| Freizeitmitarbeiter | ehrenamtlich im Team einer Freizeit | schaut nach, wozu er gehört |

**Geräte:** Gestaltet für den Laptop — dort wird verwaltet. Auf dem Handy bleibt
alles bedienbar und lesbar, ohne eigene mobile Ansichten.

**Sprache:** Deutsch, einsprachig.

**Größenordnung:** rund 300 Menschen auf der Plattform, etwa 30 gleichzeitig.
Eine Organisation hat unter 20 Freizeiten im Blick, Vergangenheit eingeschlossen.

## Ziele

1. Der Betreiber kann eine Organisation aufnehmen und ihr eine verantwortliche
   Person zuweisen.
2. Eine Organisation kann ihre Freizeiten anlegen, benennen, schließen, löschen
   und innerhalb von 30 Tagen wiederherstellen.
3. Menschen kommen ohne E-Mail-Versand ins System — über einen Einladungslink,
   der von Hand weitergegeben wird.
4. Niemand sieht Daten, zu denen keine Zuordnung besteht — der Betreiber
   eingeschlossen.
5. Eine Organisation ist vollständig und nachweisbar löschbar — beantragt von
   ihrer Leitung, ausgeführt vom Betreiber.
6. Wer angemeldet ist, bleibt es — auch nach Tagen und nach einem Neuladen.

## Rollen und Rechte (entschieden)

Rollen sind **additiv**: Wer mehrere hat, dem gilt die jeweils weitergehende.
Jede Ebene vergibt nur die nächste — es gibt keinen Durchgriff von oben.

| Rolle | darf |
|---|---|
| GlobalAdmin | Konten verwalten, Organisationen anlegen, Organisationsleitung einsetzen, beantragte Löschungen ausführen. **Kein** Einblick in Freizeiten oder Mitgliederlisten. |
| OrgAdmin | Mitglieder seiner Organisation verwalten und einladen, Freizeiten anlegen, deren Eckdaten und Status ändern, Leitung einsetzen, löschen und wiederherstellen, die Löschung der eigenen Organisation beantragen. Automatisch Leserecht auf alle Freizeiten der eigenen Organisation. |
| Freizeitleitung | die eigene Freizeit führen, Mitarbeiter zuordnen, Personen neu in die Organisation einladen. |
| Freizeitmitarbeiter | ausschließlich die Freizeiten sehen, denen er zugeordnet ist. |

## Szenarien

**S1 — Erste Inbetriebnahme.** Die Anwendung läuft, die Datenbank ist leer. Der
Betreiber ruft die Einrichtungsseite auf, legt sein Konto an und ist
GlobalAdmin. Danach ist diese Seite dauerhaft verschwunden.

**S2 — Eine Gemeinde wird aufgenommen.** Der Betreiber legt „Ev. Kirchengemeinde
Musterstadt" an; das System schlägt die Adresse `/o/ev-kirchengemeinde-musterstadt`
vor. Er lädt die zuständige Person als Organisationsleitung ein, kopiert den
Einladungslink und schickt ihn über seinen eigenen Kanal. Die Person öffnet den
Link, setzt ihr Kennwort und steht in ihrer Organisation.

**S3 — Sommerfreizeit einrichten.** Die Organisationsleitung legt „Sommerfreizeit
2027" an. Mehr als der Name ist nicht nötig — Zeitraum und Ort stehen noch nicht
fest und bleiben leer. Sie setzt eine Freizeitleitung ein.

**S4 — Team zusammenstellen.** Die Freizeitleitung fügt drei Mitarbeiter hinzu.
Zwei sind bereits in der Organisation, die dritte Person ist neu — für sie
erzeugt die Leitung eine Einladung und gibt den Link im Vorbereitungstreffen
weiter.

**S5 — Versehentlich gelöscht.** Eine Freizeit wurde irrtümlich gelöscht. Die
Organisationsleitung findet sie im Papierkorb der Organisation und stellt sie
wieder her. Nach 30 Tagen wäre das nicht mehr möglich gewesen.

**S6 — In zwei Organisationen.** Jemand leitet eine Freizeit in seiner Gemeinde
und arbeitet in einer Freizeit des Bezirksjugendwerks mit. Nach der Anmeldung
sieht er beide, jeweils mit der Organisation daneben. Er muss nichts auswählen.

**S7 — Träger kündigt.** Die Organisationsleitung beantragt die Löschung ihrer
Organisation. Der Betreiber sieht den Antrag und führt ihn aus. Die Organisation
verschwindet sofort aus der Anwendung; nach 30 Tagen ist sie samt allen
zugehörigen Daten endgültig entfernt.

## Erlebnisqualität

**Der eine mühelose Moment:** Eine Freizeit anlegen. Name eintippen, speichern,
fertig — kein Formular, das erst ausgefüllt sein will, bevor es weitergeht.
Alles Weitere ist nachtragbar.

**Was nie passieren darf:**
- Daten einer Organisation erscheinen in einer anderen.
- Der Betreiber liest Inhalte einer Organisation mit.
- Jemand verliert Arbeit durch einen einzigen Klick ohne Rückweg.
- Ein Einladungslink führt jemanden in eine falsche Organisation.

## Abnahmekriterien

1. Auf einer leeren Datenbank führt die Einrichtungsseite zu genau einem
   GlobalAdmin; danach ist sie nicht mehr erreichbar.
2. Der GlobalAdmin kann eine Organisation anlegen; ihre Adresse wird aus dem
   Namen erzeugt, ist eindeutig und ändert sich danach nicht mehr.
3. Der GlobalAdmin kann in der gesamten Anwendung keine Freizeit und keine
   Mitgliederliste einer Organisation aufrufen — auch nicht über eine direkt
   eingegebene Adresse.
4. Ein Einladungslink erzeugt beim Einlösen ein Konto in genau der vorgesehenen
   Organisation mit genau der vorgesehenen Rolle; ein abgelaufener oder bereits
   eingelöster Link tut nichts.
5. Eine Freizeit lässt sich allein mit einem Namen anlegen. Zeitraum und Ort
   bleiben leer, der Status ist `Offen`.
6. Ein Freizeitmitarbeiter sieht nach der Anmeldung ausschließlich die
   Freizeiten, denen er zugeordnet ist — organisationsübergreifend, jeweils mit
   der Organisation benannt.
7. Der Versuch, eine Freizeit einer fremden Organisation über ihre Adresse
   aufzurufen, führt zu einer Absage, nicht zu Inhalten.
8. Eine gelöschte Freizeit ist aus der Liste verschwunden, im Papierkorb der
   Organisation sichtbar und dort wiederherstellbar.
9. Eine Organisation wird nur gelöscht, wenn ihre Leitung es beantragt hat und
    der Betreiber es ausführt; keiner der beiden Schritte allein genügt.
10. Was länger als 30 Tage gelöscht ist, wird ohne Zutun endgültig entfernt —
   Freizeiten wie Organisationen.
11. Nach dem Schließen und erneuten Öffnen des Browsers ist man noch angemeldet.

## Nicht-Ziele

Ausdrücklich **nicht** Gegenstand dieses Laufs:

- Teilnehmerverwaltung, Anmeldestrecke, Warteliste, Zahlungen
- Programm-, Dienst-, Raum- und Materialplanung
- Dateiverwaltung und Uploads; automatischer E-Mail-Versand
- Auswertungen und Exporte; eigenes Logo oder Farbschema je Organisation
- Mehrsprachigkeit
- Offene Selbstregistrierung
- Rollen unterhalb des Freizeitmitarbeiters oder frei konfigurierbare Rechte

## Voraussetzung außerhalb des Codes

Für den produktiven Betrieb wird eine eigene Domain benötigt (z. B.
`zeltlotse.de`), unter der Oberfläche und Schnittstelle als Unterdomänen liegen.
Die lokale Entwicklung ist davon nicht betroffen.
