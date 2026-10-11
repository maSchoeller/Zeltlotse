---
title: PRD Zeltlotse
status: final
created: 2026-10-10
updated: 2026-10-11
---

# PRD: Zeltlotse

> Status: final. Zurückgestellt ist nur OP-6 (Datenschutzmodell vor dem Start als Dienst prüfen).

## Vision und Problem

**Zeltlotse** ist eine gemeinsame Plattform, auf der kirchliche Jugendverbände wie CVJM und EJW ihre Freizeiten planen und durchführen: von der Kalkulation über Anmeldung und Freizeitpass bis zu Programm, Küche und Finanzen.

**Problem:** Heute liegen die Informationen einer Freizeit verteilt in Excel-Tabellen, WhatsApp-Gruppen und Google Drive. Chaotisch ist das nicht, aber es fehlt eine gemeinsame Stelle. Zeltlotse hilft nicht an einem einzelnen Punkt, sondern durch die Summe vieler kleiner Dinge und durch das Gefühl, auch während der Freizeit den Überblick zu haben.

**Leitgedanke:** einfach, sorglos, schnell an die Information. Zeltlotse ist das Gegenteil eines Verwaltungsmolochs.

**Weg:** Zeltlotse startet als Hobbyprojekt für einen bekannten Kreis von Organisationen. Ziel ist ein Dienst, den andere Organisationen nutzen und kaufen können. Die erste vollständige Version (MVP) umfasst alle sechs Bausteine. Die Bausteine legen nur die Bau-Reihenfolge fest:

- **V1** Menschen und Organisationen
- **V2** Anmeldung und Freizeitpass
- **V3** Programmpunkt als Knotenpunkt
- **V4** Verpflegung
- **V5** Finanzen
- **V6** Gemeinsam unterwegs

## Leitprinzipien

1. **Ein Mensch, ein Profil, viele Rollen.** Daten gehören der Person; Organisationen bekommen befristete, protokollierte Einsicht.
2. **Der Programmpunkt ist der Knotenpunkt.** Material, Aufgaben und Mahlzeiten hängen am Wochenraster.
3. **Einmal erfassen, alles ableiten.** Eine Angabe wird nur einmal eingegeben; Listen, Hinweise und Übersichten ergeben sich daraus.
4. **Analog bleibt analog.** Was analog gut funktioniert, bleibt analog. Zeltlotse erzeugt nicht mehr Planungsaufwand als nötig.
5. **Simpel halten.** Für Sonderfälle gibt es die einfachste Lösung, keine Speziallogik.
6. **Kein Versand und keine Planungsänderung ohne Menschen.** Zeltlotse verschickt keine Nachrichten und ändert keine Planung ohne Bestätigung. Ableitungen wie Listen und Hinweise passieren automatisch.
7. **Jeder pflegt seinen Bereich.** Wer etwas verantwortet, pflegt es selbst; es gibt keine zentrale Datenpflege.
8. **Vertrauen statt Rechte-Matrix.** Innerhalb einer Freizeit sieht und darf das Team fast alles; nur wenige Dinge sind der Leitung vorbehalten.

## Rollen und Rechte

| Rolle | Wer | Darf |
|---|---|---|
| **Global Admin** | Betreiber von Zeltlotse | Organisationen anlegen, ersten Org-Admin einladen |
| **Org-Admin** | 1–2 pro Organisation | Freizeiten anlegen, Leitung einsetzen; Inventar, Vorlagen, Org-Dokumente und Richtlinien (Nachweise, Freigabefenster) verwalten; weitere Org-Admins ernennen |
| **Org-Überblick** | z. B. Leitungsgremium, Hauptamt | Inventar, Anschaffungswünsche, Übersicht der Freizeiten – keine Freizeit-Inhalte, außer in Freizeiten, in denen die Person selbst mitarbeitet |
| **Leitung** | meist 2 pro Freizeit, gleiche Rechte | alles, was das Team darf; **nur sie:** Anmeldungen bestätigen, Absagen eintragen, bestimmen, wer nachrückt, Erinnerungen auslösen, Nachweis-Status sehen, Module und Fragen festlegen, Freizeit abschließen |
| **Team** | Mitarbeitende einer Freizeit | Programm, Material, Aufgaben, Finanzen, Belege, Beiträge abhaken, Kanäle, Vorfall-Protokoll; vollständige Freizeitpässe aller Personen der Freizeit |
| **Eltern** | Familienkonto | Kinder unter 18 anmelden, Freizeitpass pflegen, Profil mit 18 übergeben |
| **Volljährige Teilnehmende** | eigenes Konto | sich selbst anmelden, eigenen Freizeitpass pflegen |
| **Teilnehmende mit Teilnehmer-Zugang** | auf der Freizeit, per QR-Code | Wochenübersicht, allgemeine Infos, Kanal „Alle“ lesen |

Küche, Einkauf und Tagesleitung sind Aufgaben im Team, keine eigenen Rollen; jedes Teammitglied kann sie übernehmen.

## Begriffe

| Begriff | Bedeutung |
|---|---|
| **Freizeit** | Ein Angebot einer Organisation wie ein Zeltlager, das mit Zeltlotse geplant und durchgeführt wird. |
| **Freizeitpass** | Die Angaben einer Person für eine Freizeit, zum Beispiel Allergien, Medikamente und Notfallkontakte; sie gehören der Person und werden bei der nächsten Freizeit vorausgefüllt. |
| **Freigabefenster** | Der Zeitraum, in dem eine Freizeit Einsicht in die Freizeitpässe hat; die Organisation legt seine Länge fest, und es läuft ab Ende des letzten Freizeittages. |
| **Nachweis** | Ein Dokument, das Mitarbeitende an ihrem Profil hochladen, zum Beispiel Führungszeugnis, JuLeiCa, Erste Hilfe oder „Menschenskinder“. |
| **Einsicht (einmalige)** | Die prüfende Person sieht die Nachweise einmal an und bestätigt oder lehnt ab; gespeichert wird nur die Bestätigung, danach endet der Zugriff. |
| **Fragenbaukasten** | Die Fragen, aus denen die Leitung den Freizeitpass einer Freizeit zusammenstellt, getrennt für Teilnehmende und Mitarbeitende. |
| **Programmpunkt** | Ein Eintrag im Wochenraster, an dem Material, Aufgaben und Mahlzeiten hängen. |
| **Wochenraster** | Der Wochenplan einer Freizeit mit allen Programmpunkten; Frühstück, Mittag- und Abendessen sind feste Programmpunkte. |
| **Leitungs-Cockpit** | Die Seite, auf der die Leitung den Stand der Freizeit und Warnhinweise sieht. |
| **Vorfall-Protokoll** | Die Aufzeichnung eines Vorfalls: was passiert ist, welches Medikament wann gegeben und was aus dem Erste-Hilfe-Kasten entnommen wurde. |
| **Vorfall-Notiz** | Die Notiz an die Leitung, die aus dem Vorfall-Protokoll entsteht und nach der Freizeit in der Organisation archiviert wird. |
| **Notfallordner** | Der ausdruckbare Ordner mit Notfallkontakten und relevanten Freizeitpass-Angaben, die Rückfallebene bei fehlendem Netz. |
| **Notfallkarte** | Die verschlüsselte, offline verfügbare Ansicht der Notfallangaben (V6). |
| **Sicherheitskette** | Die Anleitung hinter dem Notfall-Button: was jetzt zu tun ist und wen man anruft. |
| **Schutzkonzept** | Das Dokument, das die Leitung an die Freizeit anhängt und dessen Lektüre jedes Teammitglied mit „gelesen“ bestätigt. |
| **Anmeldelink für Teilnehmende / für Mitarbeitende** | Die zwei teilbaren Links einer Freizeit, über die sich Teilnehmende beziehungsweise Mitarbeitende ohne Installation anmelden. |
| **Teilnehmer-Zugang (per QR-Code)** | Das Modul, mit dem Teilnehmende auf der Freizeit ohne Konto Wochenübersicht, allgemeine Infos und den Kanal „Alle“ sehen. |
| **Programmbibliothek** | Die Sammlung gelungener Programmpunkte einer Organisation, die Mitarbeitende bewusst ablegen und in andere Freizeiten übernehmen können. |
| **Inventar** | Das Material der Organisation, das Freizeiten für einen Zeitraum reservieren. |
| **Anschaffungswünsche** | Die aus erfassten Schäden und Verlusten entstehenden Wünsche an die Organisation. |
| **Modul** | Ein Funktionsbereich, den die Leitung pro Freizeit an- oder abschaltet, zum Beispiel Verpflegung, Finanzen oder Teilnehmer-Zugang. |
| **Familienkonto** | Das Konto, in dem ein Elternteil die Profile seiner Kinder unter 18 verwaltet. |
| **Org-Überblick** | Die Rolle für zum Beispiel Leitungsgremium oder Hauptamt: Inventar, Anschaffungswünsche und Übersicht der Freizeiten, aber keine Freizeit-Inhalte außer bei eigener Mitarbeit. |

**Abkürzungen und ID-Präfixe**

| Abkürzung | Bedeutung |
|---|---|
| **CVJM** | Christlicher Verein Junger Menschen |
| **EJW** | Evangelisches Jugendwerk |
| **JuLeiCa** | Jugendleiter-Card |
| **„Menschenskinder“** | Präventionsschulung |
| **MVP** | Kleinste vollständige Version; hier umfasst sie alle Bausteine V1–V6. |
| **PWA** | Installierbare Web-App |
| **DSGVO** | Datenschutz-Grundverordnung |
| **AVV** | Auftragsverarbeitungsvertrag |
| **Mandantentrennung** | Strikte Trennung der Daten verschiedener Organisationen |
| **NS** | Nutzungsszenario |
| **FR** | Funktionale Anforderung |
| **NFR** | Übergreifende (nicht funktionale) Anforderung |
| **OP** | Offener Punkt |

## Erfolgskriterien

Nach dem ersten Sommer gilt Zeltlotse als erfolgreich, wenn:

- **1–2 Freizeiten** komplett mit Zeltlotse geplant und durchgeführt wurden,
- für diese Freizeiten **keine Excel-Tabelle** mehr nötig war,
- die Leitung sagt: **„Ich hatte die ganze Zeit den Überblick.“**

**Gegensignale** (es läuft in die falsche Richtung):

- Das Team pflegt Informationen doppelt, in Zeltlotse und nebenbei in WhatsApp.
- Eltern brechen die Anmeldung ab.
- Jemand beschwert sich, „schon wieder ein Formular“ ausfüllen zu müssen.
- Das Tool wird zum Selbstzweck hochgezüchtet: Es wächst um Funktionen, die niemand vermisst hat.

## Nutzungsszenarien

### NS-1: Sabine meldet ihren Sohn zum Zeltlager an

*Sabine, Mutter von zwei Kindern, sieht im WhatsApp-Status der Gemeinde den Anmeldelink für Teilnehmende zum Zeltlager.*

1. Sabine öffnet den Link auf dem Handy. Sie sieht die Freizeitseite mit Beschreibung und Team (Bild, Herkunft). Sie muss nichts installieren.
2. Sie meldet ihren Sohn Paul als Gast an, ohne Konto. Beim Absenden bestätigt sie die Teilnahmebedingungen digital, ohne Papier.
3. Zeltlotse bietet an: „Konto aus diesen Daten erstellen?“ Ein Klick genügt. Künftig meldet sie sich per Login-Link aus einer Mail am Konto an oder, wenn sie möchte, mit Passwort.
4. Sabine sieht: „Anmeldung eingegangen, wir melden uns in Kürze.“
5. Zeltlotse fordert sie auf, Pauls Freizeitpass auszufüllen (→ NS-2).
6. Für Pauls Schwester ruft sie den Anmeldelink einfach noch einmal auf.
7. Die Leitung bestätigt die Anmeldung. Sabine bekommt eine Mail „Paul ist Teilnehmer“; den Status sieht sie auch in Zeltlotse.
8. Pauls Vater kann sie in ihr Familienkonto holen, sie muss aber nicht.
9. Möchte Sabine absagen, zeigt Zeltlotse nur Mail und Telefon der Leitung. Die Leitung trägt die Absage ein und entscheidet selbst, wessen eingegangene Anmeldung sie stattdessen bestätigt.

**Erfolg:** Im nächsten Jahr ist die Anmeldung in unter 3 Minuten erledigt, weil alles vorausgefüllt ist.

**Anforderungen:** FR-2, FR-3, FR-15, FR-16, FR-17, FR-18, FR-19, FR-20, FR-21, FR-23

### NS-2: Sabine hält Pauls Freizeitpass aktuell

*Paul ist angemeldet. Sabine soll seinen Freizeitpass ausfüllen.*

1. Sabine sieht, welche Angaben diese Freizeit braucht. Die Leitung hat die Fragen aus dem Fragenbaukasten ausgewählt.
2. Antworten vom letzten Jahr sind vorausgefüllt. Sabine prüft sie, korrigiert sie und ergänzt, was fehlt.
3. Ist eine Angabe älter als 12 Monate, fragt Zeltlotse: „Stimmt das noch?“
4. Die Antworten gehören zu Paul und werden bei der nächsten Freizeit wiederverwendet.
5. Sabine sieht, bis wann die Freizeit Einsicht hat (Freigabefenster). Im Zugriffsprotokoll sieht sie, wer wann Einsicht hatte.
6. Füllt Sabine nichts aus, sieht die Leitung „Freizeitpass fehlt noch“ und schickt per Knopfdruck eine Erinnerung (Leitprinzip 6).
7. Was das Team auf der Freizeit über Paul notiert, bleibt in der Freizeit. Es landet nicht in Pauls Freizeitpass. Rückmeldungen an Sabine laufen im persönlichen Gespräch.
8. Nach Ende des Freigabefensters verliert die Freizeit automatisch die Einsicht. Die Länge des Fensters legt die Organisation einmal für alle ihre Freizeiten fest.

**Erfolg:** Sabine füllt Angaben einmal aus und bestätigt sie danach nur noch.

**Anforderungen:** FR-13, FR-22, FR-23, FR-24, FR-25, FR-26

### NS-3: Jonas arbeitet zum ersten Mal beim Zeltlager mit

*Jonas, 19, war früher selbst Teilnehmer. Die Freizeitleitung schickt ihm den Anmeldelink für Mitarbeitende.*

1. Jonas meldet sich über den Anmeldelink für Mitarbeitende an, genau wie Teilnehmende über den Anmeldelink für Teilnehmende.
2. Er füllt seinen Freizeitpass aus. Der ist kleiner als bei Kindern; die Fragen für Mitarbeitende wählt die Leitung im Fragenbaukasten. Wie bei Teilnehmenden sieht die Freizeit den Pass nur innerhalb ihres Freigabefensters.
3. An seinem Profil lädt Jonas seine Nachweise hoch, zum Beispiel Führungszeugnis, JuLeiCa, Erste Hilfe und „Menschenskinder“. Zu jedem Dokument trägt er das Ausstellungsdatum ein; das Hochladedatum speichert Zeltlotse selbst.
4. Die Leitung bestätigt Jonas als Mitarbeiter. Damit wird er automatisch als Mitarbeiter der Organisation geführt. Er sieht nur die Freizeiten, in denen er mitarbeitet.
5. Mit der Teilnahme fordert Zeltlotse automatisch eine **einmalige Einsicht** in seine Nachweise an. Jonas gibt sie frei.
6. Die prüfende Person (Leitung oder Organisation, je nachdem, wer angefordert hat) sieht die Dokumente einmal an und **bestätigt oder lehnt ab**. Passen Datum und Dokument nicht zusammen, meldet sie es Jonas. Er korrigiert, und die Einsicht wird neu angefordert.
7. Die Organisation speichert **nur die Bestätigung**: welcher Nachweis, eingesehen am, gültig bis, von wem. Eine Kopie behält sie nicht. Das Ablaufdatum berechnet Zeltlotse aus Ausstellungsdatum und Gültigkeitsdauer laut Richtlinie der Organisation. Die Richtlinie legt fest, welche Nachweise Pflicht sind und wie lange sie gelten.
8. Danach hat die Organisation keinen Zugriff mehr auf die Dokumente. Braucht sie später erneut Einsicht, fragt sie an, und Jonas bestätigt.
9. Läuft eine Bestätigung ab oder fehlt ein Nachweis, sieht die Leitung das. Sie spricht Jonas direkt an oder schickt ihm per Knopfdruck eine Erinnerung (Leitprinzip 6).
10. Meldet Jonas sich im nächsten Jahr für eine andere Freizeit derselben Organisation an, ist er dort schon Mitarbeiter. Der Schritt „Organisation beitreten“ entfällt; die Leitung bestätigt ihn trotzdem für die neue Freizeit.
11. Arbeitet Jonas auch beim EJW mit, gibt er dort dieselben Dokumente zur Einsicht frei und muss nichts erneut hochladen.

**Erfolg:** Jonas lädt jeden Nachweis genau einmal hoch, und keine Organisation behält mehr als die Bestätigung.

**Anforderungen:** FR-7, FR-8, FR-9, FR-10, FR-11, FR-12, FR-16, FR-22, FR-24

### NS-4: Miriam und Tobias planen das Zeltlager

*Der Zeltplatz ist seit einem Jahr gebucht. Ein Dreivierteljahr vor dem Lager legt die Org-Admin die Freizeit „Zeltlager 2027“ an und setzt Miriam und Tobias als Leitungsteam ein. Beide haben die gleichen Rechte.*

1. Miriam und Tobias konzipieren die Freizeit: Ort, höchstens wie viele Teilnehmende, höchstens wie viele Mitarbeitende.
2. Falls sie kalkulieren: Sie rechnen aus, was die Freizeit pro Teilnehmenden kostet. Daraus ergibt sich der Beitrag.
3. Sie schalten die Module an, die sie brauchen. Kalkulation und Finanzen sind optional; einfache Freizeiten kommen ohne aus. Außerdem wählen sie die Fragen für den Freizeitpass, getrennt für Teilnehmende und Mitarbeitende.
4. Sie fragen mögliche Mitarbeitende persönlich an. Wer zusagt, bekommt den Anmeldelink für Mitarbeitende (→ NS-3). Mitarbeitende können jederzeit dazukommen, auch wenn die Freizeit schon voll ist.
5. Parallel dazu oder später veröffentlichen sie den Anmeldelink für Teilnehmende (→ NS-1) und bestätigen eingehende Anmeldungen.
6. Sie hängen das Schutzkonzept an; das Team bestätigt „gelesen“.
7. Sie legen Aufgaben an; die Vorlagen der Organisation sind schon übernommen.
8. Im Leitungs-Cockpit sehen sie Warnhinweise, zum Beispiel „Freizeitpass fehlt“ oder „Nachweis läuft ab“. Sie schicken Erinnerungen per Knopfdruck oder sprechen die Person direkt an.

Was nur die Leitung darf, steht unter Rollen und Rechte.

**Erfolg:** Miriam und Tobias sehen jederzeit auf einer Seite, wo die Freizeit steht und was noch fehlt.

**Anforderungen:** FR-6, FR-11, FR-16, FR-20, FR-22, FR-25, FR-27, FR-30, FR-39, FR-40, FR-44, FR-54

### NS-5: Das Team plant das Geländespiel

*Das Team trifft sich zur Programmplanung. Im Wochenraster stehen Frühstück, Mittag- und Abendessen schon als feste Programmpunkte, weil sie den Tag takten.*

1. Das Team füllt den Wochenraster gemeinsam mit Programmpunkten. Die Leitung teilt nichts zu.
2. Jonas übernimmt das „Geländespiel Dienstag“ selbst („Ich übernehme“).
3. Am Programmpunkt hängt er das benötigte Material an. Jedes Teil ist gebraucht, vorhanden, besorgt oder eingepackt. Ein Teil davon kommt aus dem Inventar der Organisation; für anderes sagen Mitarbeitende: „Ich bringe das mit.“
4. Ist ein Teil aus dem Inventar zur gleichen Zeit für eine andere Freizeit reserviert, zeigt Zeltlotse einen Konflikthinweis. Die Leitungen klären das außerhalb von Zeltlotse.
5. Aufgaben am Programmpunkt brauchen nur einen Titel. Wer eine erledigen will, übernimmt sie selbst.
6. War das Geländespiel ein Erfolg, legt Jonas es bewusst in der Programmbibliothek der Organisation ab. Von selbst landet dort nichts.

**Erfolg:** Aus dem Programmpunkt ergibt sich, welches Material gebraucht wird und wer was erledigt, ohne zusätzliche Listen.

**Anforderungen:** FR-34, FR-35, FR-36, FR-37, FR-39, FR-41, FR-69

### NS-6: Lea verletzt sich beim Geländespiel

*Mittwochnachmittag, Geländespiel im Wald. Lea, 11, stürzt und schneidet sich am Knie. Jonas ist als Erster bei ihr.*

1. Jonas tippt auf dem Handy auf den Notfall-Button. Zeltlotse zeigt die Sicherheitskette: Was ist jetzt zu tun, wen rufe ich an (Leitung, Ansprechperson am Heimatort)?
2. Über die Schnellsuche öffnet er Leas Freizeitpass: Allergien, Medikamente, Notfallkontakte. Alle Mitarbeitenden der Freizeit sehen die Notfallkontakte aller Teilnehmenden.
3. Jonas verarztet Lea und führt nebenbei das **Vorfall-Protokoll**: was passiert ist, welches Medikament wann gegeben wurde, was aus dem Erste-Hilfe-Kasten entnommen wurde. Entnommenes landet automatisch auf der Schadens- und Verlustliste und wird so nachgekauft.
4. Aus dem Protokoll entsteht die **Vorfall-Notiz**. Sie landet bei der Leitung.
5. Die Leitung entscheidet über die nächsten Schritte, zum Beispiel einen Anruf bei Leas Eltern.
6. Die Notiz bleibt während der Freizeit bei der Leitung. Danach wird sie in der Organisation archiviert und bleibt dort zugänglich, falls später Eltern oder eine Versicherung nachfragen (Löschfrist der Organisation, NFR-5).
7. Hat Jonas keinen Empfang, nutzt er den ausgedruckten Notfallordner. Die verschlüsselte Offline-Notfallkarte kommt mit Baustein V6; er gehört zur MVP, wird aber zuletzt gebaut.

**Erfolg:** Jonas findet Leas Angaben in Sekunden, und hinterher ist lückenlos dokumentiert, was passiert ist und was gegeben wurde.

**Anforderungen:** FR-28, FR-29, FR-31, FR-32, FR-33, FR-65

### NS-7: Uwe kocht für 60 Leute

*Uwe ist Küchenchef beim Zeltlager. Er weiß, was zu tun ist. Von Zeltlotse braucht er Überblick, keine Prozesssteuerung.*

1. Vor der Freizeit legt Uwe seine Rezepte an: Zutaten mit Menge und Einheit, dazu die Zubereitung als Freitext. Jedes Rezept gilt für eine Basis-Personenzahl, zum Beispiel für 3. Zeltlotse rechnet es auf die Freizeit hoch.
2. Er merkt Gerichte für die Mahlzeiten im Wochenraster vor, zum Beispiel „Dienstag Mittag: Chili“. Das ist ein Plan, keine feste Zusage.
3. Aus dem Plan berechnet Zeltlotse die Einkaufsliste. Dabei gleicht es die Allergien aller Teilnehmenden und Mitarbeitenden ab und plant Ersatzportionen ein.
4. Uwe passt die Liste an: Er ergänzt Posten und überschreibt Mengen, wenn er es besser weiß.
5. Vor der Freizeit wird so viel wie möglich in einem großen Einkauf besorgt. Uwe hakt an, für welche Tage und Posten eingekauft werden soll, und gibt frei. Wer einkauft, hakt ab, was gekauft ist. Vor Ort wird nachgekauft.
6. Auf der Freizeit entscheidet Uwe ein, zwei Tage vorher, was wirklich gekocht wird. Er tauscht Gerichte, je nach Resten und Programmänderungen. Zeltlotse zeigt ihm dann offen, wie sich die noch nicht gekauften Posten der Einkaufsliste ändern würden. Uwe bestätigt oder passt an. Von selbst ändert sich nichts.
7. Bleibt viel übrig, erklärt Uwe spontan einen Restetag.
8. In der Tagesansicht sieht er auf einen Blick, was heute ansteht und welche Allergien zu beachten sind.

**Erfolg:** Uwe muss die Einkaufsliste nicht von Hand zusammenrechnen, und Zeltlotse hält mit, wenn er vor Ort umplant.

**Anforderungen:** FR-46, FR-47, FR-48, FR-49, FR-50, FR-51, FR-52, FR-53

### NS-8: Miriam kalkuliert, das Team sammelt Belege

*Miriam aus dem Leitungsteam rechnet aus, was das Zeltlager kostet. Uwe aus der Küche rechnet mit. Das ganze Team sieht die Finanzen.*

1. Miriam legt die Kalkulation an: Fixkosten und Kosten pro Person und Tag in sechs Kategorien. Die Kosten der Mitarbeitenden werden auf die Teilnehmerbeiträge umgelegt.
2. Für die Verpflegung setzt sie mit Uwe einen Tagessatz pro Person und Tag an, zum Beispiel 7 €. Tagessatz mal Gesamtzahl der Personen (Teilnehmende und Mitarbeitende) mal 14 Tage ergibt das Verpflegungsbudget. Mit dem Essensplan ist das nicht verknüpft.
3. Sie trägt Einnahmen ein, zum Beispiel Zuschüsse oder einen Rabatt der Organisation.
4. Im Beitragsrechner verschiebt sie Regler und sieht als Balken, wie sich der Teilnehmerbeitrag ändert.
5. Vor und während der Freizeit laden alle Mitarbeitenden ihre Belege selbst hoch, als Foto oder von Hand. Dazu tragen sie ein, wer bezahlt hat und ob schon erstattet wurde. Das dient der Dokumentation; überwiesen wird von der Organisation.
6. Alle im Team können von Hand abhaken, welche Teilnehmenden ihren Beitrag bezahlt haben. Zeltlotse vermerkt, wer abgehakt hat.
7. Im Soll-Ist-Vergleich sehen alle, wo die Freizeit steht. Uwe prüft dort selbst, ob die Küche im Tagessatz bleibt.
8. Die Erstattungsübersicht zeigt pro Person, wer noch Geld bekommt. Einen Abschluss gibt es nicht; es gilt immer der aktuelle Stand.

**Erfolg:** Alle sehen jederzeit, ob die Freizeit im Budget liegt und wer noch Geld bekommt, ohne Tabellen zusammenzuführen.

**Anforderungen:** FR-54, FR-55, FR-56, FR-57, FR-58, FR-59, FR-60

### NS-9: Das Mittagessen verschiebt sich

*Dienstag, das Geländespiel dauert länger. Jonas verschiebt im Wochenraster das Mittagessen von 13:00 auf 13:30.*

1. Jonas verschiebt den Programmpunkt „Mittagessen“ in Zeltlotse.
2. Zeltlotse fragt: „Alle informieren?“ Jonas bestätigt. Ohne Bestätigung geht nichts raus.
3. Die Nachricht „Essen 13:30 statt 13:00“ erscheint im Kanal **Alle** und kommt als Push-Nachricht aufs Handy. In „Alle“ schreiben alle Mitarbeitenden. Teilnehmende mit Teilnehmer-Zugang lesen mit (→ NS-10).
4. Absprachen nur fürs Team laufen im Kanal **Team**.
5. Was beim nächsten Essen angesagt werden soll, sammelt das Team auf dem **Ansagen-Zettel**.
6. Was in der abendlichen Mitarbeiterbesprechung auf den Tisch muss, notieren alle im Lauf des Tages. In der Besprechung wird es abgehakt.
7. Wer an welchem Tag Tagesleitung ist, regelt das Team in der Besprechung. Zeltlotse muss das nicht wissen.

**Erfolg:** Eine Änderung im Wochenraster erreicht mit einer einzigen Bestätigung alle Betroffenen.

**Anforderungen:** FR-61, FR-62, FR-63

### NS-10: Paul schaut, wann es Essen gibt

*Paul, 12, ist im Zeltlager. Die Leitung hat für diese Freizeit den Teilnehmer-Zugang eingeschaltet.*

1. Am ersten Abend hängt ein QR-Code am Infobrett. Paul scannt ihn mit seinem Handy und ist drin, ohne Konto.
2. Er sieht die Wochenübersicht: wann was ansteht und wann es Essen gibt. Details zu den Programmpunkten sieht er nicht.
3. Er findet allgemeine Infos, die das Team bereitstellt, zum Beispiel Seiten oder Dokumente wie die Hausordnung.
4. Im Kanal „Alle“ liest er mit und bekommt Änderungen als Push-Nachricht (→ NS-9).
5. Mit dem Ende der Freizeit endet auch der Teilnehmer-Zugang.

**Erfolg:** Paul weiß jederzeit, was als Nächstes ansteht, ohne dass jemand ihm ein Konto einrichten muss.

**Anforderungen:** FR-43, FR-44, FR-61, FR-64

### NS-11: Paul wird 18

*Paul hat Geburtstag. Seine Daten liegen noch im Familienkonto von Sabine.*

1. Ab Pauls 18. Geburtstag zeigt Zeltlotse im Familienkonto bei Paul den Knopf „Profil an Paul übergeben“ (Leitprinzip 6).
2. Sabine drückt den Knopf und gibt Pauls E-Mail-Adresse ein.
3. Paul bekommt einen Link und übernimmt sein Profil mit Freizeitpass und Verlauf.
4. Ab jetzt gehört das Profil Paul. Sabine sieht es nicht mehr.
5. Übergibt Sabine nicht, legt Paul bei seiner nächsten Anmeldung ein eigenes Konto an und füllt neu aus.

**Erfolg:** Pauls Angaben gehen mit ihm mit, und ab 18 sieht nur noch er seine Gesundheitsdaten.

**Anforderungen:** FR-3, FR-4

### NS-12: Miriam und Tobias schließen das Zeltlager ab

*Das Lager ist vorbei. Die letzten Kisten sind ausgeräumt.*

1. Das Team erledigt die Aufgaben der Nachbereitung.
2. Was kaputt oder verloren ging oder aus dem Erste-Hilfe-Kasten verbraucht wurde, steht auf der Schadens- und Verlustliste. Daraus werden Anschaffungswünsche an die Organisation.
3. Miriam schließt die Freizeit ausdrücklich ab; ihr Status wechselt auf „gelaufen“ (FR-45). Kosten, die danach noch anfallen, werden außerhalb von Zeltlotse geregelt.
4. Die Vorfall-Notizen werden in der Organisation archiviert (→ NS-6).
5. Nach Ablauf des Freigabefensters verschwinden die Freizeitpässe aus der Freizeit (→ NS-2).

**Erfolg:** Nach dem Abschluss liegt nichts mehr offen herum, und die Organisation weiß, was angeschafft werden muss.

**Anforderungen:** FR-24, FR-31, FR-32, FR-38, FR-39, FR-45

### NS-13: Das EJW-Leitungsgremium nach dem Sommer

*Das Leitungsgremium des EJW trifft sich im September. Gerd hat die Rolle Org-Überblick. Er war selbst auf keiner Freizeit.*

1. Gerd filtert die Übersicht der Freizeiten auf „gelaufen“.
2. Er sieht die gesammelten Anschaffungswünsche aller Freizeiten und das Inventar der Organisation. Das Gremium berät darüber, außerhalb von Zeltlotse.
3. In die Freizeiten selbst schaut Gerd nicht: keine Teilnehmenden, keine Freizeitpässe, keine Finanzen. Das darf er nur bei Freizeiten, in denen er selbst mitarbeitet.

**Erfolg:** Das Gremium hat alles für seine Sitzung auf einen Blick, ohne Zugriff auf persönliche Daten.

**Anforderungen:** FR-5, FR-37, FR-38, FR-45

## Funktionale Anforderungen

Gruppiert nach den Bausteinen V1–V6 (siehe Vision). IDs sind dauerhaft und werden nie neu vergeben: Später ergänzte Anforderungen stehen bei ihrem Thema und tragen die nächste freie Nummer, gestrichene IDs bleiben als Platzhalter stehen. Die Nummern sind deshalb nicht durchgehend aufsteigend.

### V1 – Menschen und Organisationen

- **FR-1** Jede Person hat genau ein Profil, das ihr gehört, auch wenn sie mehrere Rollen in mehreren Organisationen hat.
- **FR-2** Am Konto meldet man sich standardmäßig per Login-Link aus einer Mail an; wer möchte, legt zusätzlich ein Passwort fest.
- **FR-3** Ein Elternteil verwaltet im Familienkonto die Profile seiner Kinder unter 18 und kann einen weiteren Elternteil hinzufügen. Patchwork-Konstellationen sind nicht abgebildet.
- **FR-4** Ab dem 18. Geburtstag eines Kindes können die Eltern das Profil per Knopf an die Mail-Adresse des Kindes übergeben. Danach haben sie keinen Zugriff mehr.
- **FR-5** Der Global Admin legt Organisationen an und lädt per Link den ersten Org-Admin ein. Org-Admins ernennen weitere Org-Admins und Personen mit der Rolle Org-Überblick.
- **FR-6** Org-Admins legen Freizeiten an und setzen eine oder mehrere Personen als Leitung mit gleichen Rechten ein.
- **FR-7** Mitarbeitende sehen nur die Freizeiten, in denen sie mitarbeiten. Mit der ersten bestätigten Mitarbeit werden sie automatisch als Mitarbeitende der Organisation geführt.
- **FR-8** Personen laden Nachweise an ihrem Profil hoch und tragen das Ausstellungsdatum ein; das Hochladedatum speichert Zeltlotse.
- **FR-9** Jede Organisation legt als Richtlinie fest, welche Nachweise Pflicht sind und wie lange sie gelten. Zeltlotse berechnet daraus das Ablaufdatum.
- **FR-10** Einsicht in Nachweise ist einmalig. Sie wird bei Mitarbeit an einer Freizeit automatisch angefordert oder später von Organisation oder Leitung angefragt. Die Person gibt frei; die prüfende Person bestätigt oder lehnt mit Hinweis ab. Gespeichert wird nur die Bestätigung (Nachweis, eingesehen am, gültig bis, von wem), keine Kopie. Danach endet der Zugriff.
- **FR-11** Die Leitung sieht fehlende und ablaufende Nachweise ihres Teams und kann per Knopfdruck eine Erinnerung an die Person schicken (Leitprinzip 6).
- **FR-12** Eine Person kann dieselben Nachweise mehreren Organisationen zur Einsicht freigeben, ohne sie erneut hochzuladen.
- **FR-13** Personen sehen im Zugriffsprotokoll, wer wann Einsicht in ihre Daten hatte.
- **FR-14** Jede Person kann per Klick Auskunft über ihre gespeicherten Daten erhalten, ihr Konto löschen und ihre Daten als PDF oder CSV exportieren.

### V2 – Anmeldung und Freizeitpass

#### Anmeldung

- **FR-15** Jede Freizeit hat eine öffentliche Freizeitseite mit Beschreibung und Team (Bild, Herkunft).
- **FR-16** Jede Freizeit hat zwei teilbare Anmeldelinks: den Anmeldelink für Teilnehmende und den Anmeldelink für Mitarbeitende. Beide funktionieren ohne Installation.
- **FR-67** Ist die Höchstzahl an Teilnehmenden erreicht, zeigt die Freizeitseite „ausgebucht“. Anmelden ist trotzdem möglich, aber ohne Aussicht auf Zusage, bis die Leitung jemanden bestätigt.
- **FR-17** Anmeldung ist als Gast ohne Konto möglich. Danach bietet Zeltlotse an, mit einem Klick ein Konto aus den Daten zu erstellen. Wer Gast bleibt, erhält mit der Eingangsmail einen Gast-Link: Darüber füllt er den Freizeitpass aus und ändert ihn, sieht den Anmeldestatus und liest die Elterninfo (FR-68). Der Link gilt bis zum Ende des Freigabefensters.
- **FR-18** Unter 18 meldet ein Elternteil an, Volljährige melden sich selbst an. Geschwister werden nacheinander über denselben Anmeldelink angemeldet.
- **FR-19** Beim Absenden werden die Teilnahmebedingungen digital bestätigt.
- **FR-20** Nach dem Absenden sieht die anmeldende Person „Anmeldung eingegangen“. Die Leitung bestätigt die Anmeldung oder sagt ab. Die Person erhält eine Bestätigungs- oder Absage-Mail und sieht den Status in Zeltlotse. Eine Warteliste gibt es nicht.
- **FR-21** Absagen sind in Zeltlotse nicht möglich; angezeigt werden Mail und Telefon der Leitung. Die Leitung trägt Absagen ein und bestätigt selbst, wer nachrückt.
- **FR-66** Jede Freizeit hat eine Vertrauensseite. Sie erklärt in Klartext, welche Daten erhoben werden, wer sie sieht und wann sie verschwinden. Zeltlotse erzeugt sie aus den Einstellungen der Freizeit und der Organisation.
- **FR-68** Elterninfo: Infobrief, Packliste und Notfallnummern der Freizeit (Sichtbarkeit „Eltern und Teilnehmende“, FR-43) sehen Eltern in ihrem Konto. Wer als Gast angemeldet hat, sieht sie über den Gast-Link (FR-17).

#### Freizeitpass

- **FR-22** Die Leitung wählt die Fragen des Freizeitpasses frei aus einem Fragenbaukasten, getrennt für Teilnehmende und Mitarbeitende. Es gibt keinen Pflichtkern: Fragt die Leitung zum Beispiel keine Allergien ab, bleibt der Allergen-Abgleich der Küche leer.
- **FR-23** Antworten im Freizeitpass gehören der Person und werden bei der nächsten Freizeit vorausgefüllt. Angaben älter als 12 Monate werden mit „Stimmt das noch?“ markiert.
- **FR-24** Sichtbarkeit des Freizeitpasses:
  - a) Die Leitung sieht den Freizeitpass ab Eingang der Anmeldung.
  - b) Das ganze Team sieht die vollständigen Freizeitpässe ab Bestätigung der Anmeldung und nur bis zum Ende des Freigabefensters.
  - c) Für das Freigabefenster legt die Organisation ein festes Intervall fest, das ab Ende des letzten Freizeittages läuft.
  - d) Beim Anlegen einer Freizeit wird das Intervall für diese Freizeit fixiert und ist danach nicht mehr änderbar.
  - e) Eltern sehen, bis wann die Freizeit Einsicht hat.
- **FR-25** Die Leitung sieht, wessen Freizeitpass unvollständig ist, und löst Erinnerungen per Knopfdruck aus.
- **FR-26** Notizen des Teams zu Teilnehmenden bleiben in der Freizeit und fließen nicht in den Freizeitpass.

#### Leitung und Sicherheit

- **FR-27** Ein Leitungs-Cockpit zeigt den Stand der Freizeit: Anmeldungen (eingegangen, bestätigt) im Verhältnis zu den Plätzen sowie Warnhinweise zu fehlenden Freizeitpässen, fehlenden oder abgelaufenen Nachweisen und unbesetzten Aufgaben.
- **FR-28** Schnellsuche nach Personen der Freizeit; der Freizeitpass ist in Sekunden geöffnet.
- **FR-29** Ein Notfall-Button führt zur Sicherheitskette: was jetzt zu tun ist und wen man anruft (Leitung, Ansprechperson am Heimatort).
- **FR-30** Die Leitung hängt das Schutzkonzept an; jedes Teammitglied bestätigt „gelesen“.
- **FR-31** Vorfall-Protokoll: was passiert ist, welches Medikament wann gegeben wurde, was aus dem Erste-Hilfe-Kasten entnommen wurde. Daraus entsteht eine Vorfall-Notiz an die Leitung. Nach Abschluss der Freizeit wird sie in der Organisation archiviert und nach der Löschfrist der Organisation gelöscht (NFR-5).
- **FR-32** Entnahmen aus dem Erste-Hilfe-Kasten landen auf der Schadens- und Verlustliste.
- **FR-33** Der Notfallordner (Notfallkontakte, relevante Freizeitpass-Angaben) lässt sich ausdrucken.

### V3 – Programmpunkt als Knotenpunkt

#### Programm und Material

- **FR-34** Jede Freizeit hat einen Wochenraster. Frühstück, Mittag- und Abendessen sind feste Programmpunkte.
- **FR-35** Das Team plant Programmpunkte gemeinsam; Mitarbeitende übernehmen Programmpunkte selbst. Die Leitung teilt nichts zu.
- **FR-36** Material hängt am Programmpunkt und hat vier Zustände: gebraucht, vorhanden, besorgt, eingepackt. Mitarbeitende können „Ich bringe das mit“ angeben.
- **FR-69** Eine Gesamt-Materialliste führt das Material aller Programmpunkte der Freizeit zusammen, mit Mengen und Status.
- **FR-37** Die Organisation pflegt ein Inventar. Material aus dem Inventar wird für einen Zeitraum reserviert. Überschneiden sich Reservierungen paralleler Freizeiten, zeigt Zeltlotse einen Konflikthinweis. Gelöst wird der Konflikt außerhalb von Zeltlotse.
- **FR-38** Schäden und Verluste werden erfasst und zu Anschaffungswünschen. Diese sehen Org-Admins, Leitungen und Org-Überblick.

#### Aufgaben und Bibliothek

- **FR-39** Aufgaben brauchen nur einen Titel und haben die Phasen Vorbereitung, Während und Nachbereitung. Sie können selbst übernommen werden und erscheinen unter „Meine Aufgaben“.
- **FR-40** Die Organisation pflegt Aufgaben-Vorlagen; neue Freizeiten übernehmen sie. Aufgaben lassen sich in eine Vorlage übernehmen.
- **FR-41** Mitarbeitende legen gelungene Programmpunkte bewusst in der Programmbibliothek der Organisation ab; automatisch landet dort nichts. Aus der Bibliothek lässt sich ein Programmpunkt samt Material und Aufgaben in eine Freizeit übernehmen.
- **FR-42** *(gestrichen, aufgegangen in FR-41)*

#### Dokumente, Module, Status

- **FR-43** Dokumente:
  - a) Dokumente werden am Kontext abgelegt (Freizeit, Programmpunkt), mit drei Sichtbarkeiten: Team; Eltern und Teilnehmende (auch Teilnehmende mit Teilnehmer-Zugang), zum Beispiel Infobrief und Packliste; öffentlich auf der Freizeitseite.
  - b) Org-Dokumente werden eingebunden statt kopiert.
  - c) Eine Suche läuft über alle Dokumente.
- **FR-44** Module sind pro Freizeit an- oder abschaltbar, zum Beispiel Verpflegung, Finanzen, Teilnehmer-Zugang.
- **FR-45** Freizeiten haben die Status „in Vorbereitung“, „läuft“ und „gelaufen“. Ab dem Startdatum zeigt Zeltlotse automatisch „läuft“. Die Leitung schließt eine Freizeit ab und setzt sie damit auf „gelaufen“. Gelaufene Freizeiten sind nur noch lesbar, bleiben einsehbar und lassen sich per Filter ausblenden.

### V4 – Verpflegung (Modul)

- **FR-46** Die Küche legt Rezepte mit einer Basis-Personenzahl an: Zutaten als Liste (Menge, Einheit, Zutat, Allergene), die Zubereitung als Freitext. Zeltlotse skaliert sie auf die Personenzahl der Freizeit; jede Person zählt gleich.
- **FR-47** Gerichte werden an Mahlzeiten-Programmpunkten vorgemerkt und können jederzeit getauscht werden.
- **FR-48** Zeltlotse berechnet die Einkaufsliste aus den vorgemerkten Gerichten, mit Allergen-Abgleich gegen alle Freizeitpässe und mit Ersatzportionen.
- **FR-49** Die Küche ergänzt Posten der Einkaufsliste und überschreibt Mengen von Hand.
- **FR-50** Wird ein Gericht getauscht, zeigt Zeltlotse, wie sich die noch nicht gekauften Posten ändern würden. Die Küche bestätigt oder passt an; ohne Bestätigung ändert sich nichts.
- **FR-51** Die Küche gibt frei, für welche Tage und Posten eingekauft wird; wer einkauft, hakt Gekauftes ab.
- **FR-52** Küchen-Tagesansicht: was heute ansteht, welche Allergien und Sonderkost zu beachten sind.
- **FR-53** Die Küche kann eine Mahlzeit spontan als Restetag markieren.

### V5 – Finanzen (Modul)

- **FR-54** Kalkulation in sechs Kategorien mit Fixkosten und Kosten pro Person und Tag; Kosten der Mitarbeitenden werden auf die Teilnehmerbeiträge umgelegt.
- **FR-55** Verpflegung wird als Tagessatz pro Person und Tag kalkuliert (Tagessatz × alle Personen × Tage), ohne Kopplung an den Essensplan.
- **FR-56** Freie Einnahmeposten, zum Beispiel Zuschüsse oder Rabatt der Organisation.
- **FR-57** Beitragsrechner: Schieberegler und Balken zeigen, wie sich der Teilnehmerbeitrag ändert.
- **FR-58** Jedes Teammitglied erfasst Belege als Foto oder von Hand, mit Kategorie (eine der sechs Kalkulationskategorien), wer bezahlt hat und ob erstattet wurde.
- **FR-59** Soll-Ist-Vergleich und Erstattungsübersicht pro Person, immer auf dem aktuellen Stand; es gibt keinen Abrechnungsabschluss.
- **FR-60** Jedes Teammitglied kann „Beitrag bezahlt“ abhaken; Zeltlotse vermerkt, wer abgehakt hat.

### V6 – Gemeinsam unterwegs

- **FR-61** Kurznachrichten in zwei Kanälen: „Team“ und „Alle“. In „Alle“ schreiben alle Mitarbeitenden; Teilnehmende mit Teilnehmer-Zugang lesen mit. Nachrichten kommen als Push-Nachricht aufs Handy: für das Team verlässlich, für Teilnehmende mit Teilnehmer-Zugang nur, wo das Gerät es erlaubt (auf dem iPhone erst nach Hinzufügen zum Home-Bildschirm).
- **FR-62** Wird ein Programmpunkt verschoben, fragt Zeltlotse „Alle informieren?“; ohne Bestätigung wird nichts verschickt.
- **FR-63** Ansagen-Zettel für Ansagen beim Essen; Notizen für die Mitarbeiterbesprechung, die dort abgehakt werden.
- **FR-64** Die Leitung schaltet den Teilnehmer-Zugang pro Freizeit an. Teilnehmende kommen per QR-Code ohne Konto hinein und sehen nur Wochenübersicht, allgemeine Infos und Kanal „Alle“. Der Teilnehmer-Zugang endet mit der Freizeit.
- **FR-65** Verschlüsselter Offline-Modus für Küchen-Tagesansicht und Notfallkarte; die Offline-Daten werden nach Freizeitende gelöscht.

## Übergreifende Anforderungen

### Datenschutz und Sicherheit

- **NFR-1** Daten gehören der Person. Organisationen und Freizeiten erhalten nur befristete Einsicht; jede Einsicht wird protokolliert (FR-10, FR-13, FR-24).
- **NFR-2** Organisationen sind strikt voneinander getrennt (Mandantentrennung). Keine Organisation sieht Daten einer anderen, außer eine Person gibt sie selbst frei.
- **NFR-3** Datenhaltung ausschließlich in der EU.
- **NFR-4** Zeltlotse wird nach der DSGVO (Datenschutz-Grundverordnung) gebaut, als gemeinsame Grundlage für kirchliche und nichtkirchliche Organisationen. Eine Auswahl des Rechtsrahmens pro Organisation gibt es nicht. Die Verantwortung ist geteilt: Für Profile und Nachweise ist der Betreiber verantwortlich (eigene Datenschutzerklärung). Für die Daten einer Freizeit ist die Organisation verantwortlich; der Betreiber verarbeitet sie in ihrem Auftrag. Zeltlotse stellt dafür eine Vorlage für den Auftragsverarbeitungsvertrag (AVV) bereit. Fachlich geprüft wird dieses Modell vor dem Start als Dienst.
- **NFR-5** Zeltlotse löscht personenbezogene Daten automatisch nach Fristen. Jede Organisation legt die Fristen selbst fest, zum Beispiel für Teilnehmerlisten, Belege und archivierte Vorfall-Notizen. Zeltlotse gibt sinnvolle Vorgabewerte vor.
- **NFR-6** Der Freizeitpass ist für eine Freizeit zwingend nötig und daher nicht widerrufbar, nur änderbar. Optionale Einwilligungen, zum Beispiel für Fotos, werden einzeln erteilt und sind jederzeit widerrufbar.
- **NFR-7** Datensparsamkeit: Erhoben wird nur, was die Leitung im Fragenbaukasten auswählt.

### Geräte und Erreichbarkeit

- **NFR-8** Zeltlotse läuft im Browser am Laptop (Vorbereitung, Leitung) und auf dem Handy (schnelle Handgriffe während der Freizeit). Auf dem Handy ist Zeltlotse eine installierbare Web-App (PWA); eine native App gibt es in der MVP nicht. Später lässt sich die PWA bei Bedarf in eine App-Store-Hülle packen. Das Hinzufügen zum Home-Bildschirm ist Teil des Onboardings für Mitarbeitende.
- **NFR-9** Freizeitseite, Anmeldelinks und Teilnehmer-Zugang (per QR-Code) funktionieren ohne Installation.
- **NFR-10** Kanal-Nachrichten kommen als Push-Nachricht aufs Handy (Einschränkung für Teilnehmende mit Teilnehmer-Zugang siehe FR-61). Sonst ist Zeltlotse still: Es gibt keine Benachrichtigungsflut.
- **NFR-11** Bis zum Offline-Modus (FR-65) ist der ausgedruckte Notfallordner (FR-33) die Rückfallebene bei fehlendem Netz.

### Bedienung

- **NFR-12** Höchstens 1–2 Pflichtfelder pro Objekt.
- **NFR-13** Bearbeiten direkt an Ort und Stelle, wo das sinnvoll und möglich ist; sonst in einer Detailansicht. Keine Pop-up-Fenster.
- **NFR-14** Jede wichtige Information ist höchstens 3–4 Klicks von der Startseite der Freizeit entfernt.
- **NFR-15** Neue Mitarbeitende sind nach 2 Minuten arbeitsfähig, ohne Anleitung.
- **NFR-16** Zahlen werden grafisch dargestellt, zum Beispiel als Balken, statt in Zahlenkolonnen.
- **NFR-17** Die Wiederanmeldung eines Kindes dauert unter 3 Minuten.

### Betrieb und Wachstum

- **NFR-18** Zeltlotse lässt sich lokal betreiben und testen; später läuft es als Dienst in der Cloud.
- **NFR-19** Die Architektur verbaut keinen späteren Dienst für viele Organisationen mit Bezahlmodell. Gebaut wird beides in der MVP nicht.

## Nicht im Umfang

### Bewusst nie

Widerspricht den Leitprinzipien und kommt nicht:

- eine ganze Freizeit kopieren (Wiederverwendung läuft über Vorlagen und Bibliothek)
- Resteregister mit automatischem Reste-Vorschlag
- Kisteninhalte einzeln erfassen; formaler Rückgabe-Check für Material
- Live-Kalkulation über Anmeldungen
- personenbezogene Massen-Aufgaben, persönliche Checklisten, feste Aufgaben-Startlisten pro Freizeittyp
- Mitarbeiterpool oder Mitarbeiter-Anfragen über die Plattform
- feingranulare Rechte-Matrix, frei baubare Gruppen, Unterkalender, Bearbeitung in Pop-ups
- Papier- oder Offline-Anmeldung
- generierte Rezepte; Kanban-Board

### Für diese Version ausgeschlossen

- automatische Warteliste und automatisches Nachrücken
- Absage durch Eltern direkt in Zeltlotse
- automatisch verschickte Erinnerungen und Nachrichten ohne Bestätigung
- Patchwork-Familien als eigenes Modell
- eigenes Konto für Kinder unter 18
- Tagesleitung als Rolle oder Übergabe in der App
- Abschlussmappe; Abrechnungsabschluss
- Lösung von Inventar-Konflikten in Zeltlotse

### Später (nach der MVP)

- Zelt- und Zimmereinteilung, inklusive Suche nach Zeltnummer
- erweiterte Infos auf der Freizeitseite (Zeitraum, Preis, Altersgruppe, Ort, Packliste, häufige Fragen)
- digitale Bestätigung weiterer Dokumente neben den Teilnahmebedingungen
- Cockpit-Hinweis „Schutzkonzept nicht gelesen“; Übersicht aller Freizeiten mit Warnhinweisen für Org-Admins
- Ansprechperson im Inventar-Konflikthinweis
- Zahlungsinfo für Eltern (Betrag, Bankverbindung, Verwendungszweck); Beitragsstatus „erlassen“; Teilnehmerbeiträge im Soll-Ist-Vergleich
- Abrechnung als PDF; weitere Druckansichten (Küchen-Tagesansicht, Wochenraster, Teilnehmerliste, Materialliste)
- Sonderkost-Karte mit Foto; Zähler für Mitesser und Abwesende; Mengenfaktoren nach Alter
- Anbindung eines Zahlungssystems; Unterorganisationen (Bezirk, Ortsverein)
- Bezahlmodell für Zeltlotse als Dienst
- übriger Ausbau-Pool: Lunchpakete, Buffet-Schilder, Restevermerk, Open-Food-Anbindung, Kosten-Donut, Szenarienvergleich, Vorjahreskalkulation als Startwert, Zuschuss-Exportlisten, Jahresrückblick, Volltextsuche per Texterkennung

## Offene Punkte

Keine Punkte blockieren UX, Architektur oder die Aufteilung in Epics. OP-1 bis OP-5 sind geklärt und in die Anforderungen eingeflossen. Zurückgestellt:

| # | Punkt | Verantwortlich | Wann |
|---|---|---|---|
| OP-6 | Datenschutzmodell (NFR-4) fachlich prüfen lassen: geteilte Verantwortung, AVV-Vorlage, Datenschutzerklärung | Marvin | vor dem Start als Dienst für fremde Organisationen |
