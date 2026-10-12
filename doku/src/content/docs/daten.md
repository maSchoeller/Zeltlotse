---
title: Was passiert mit meinen Daten
description: Freigabe und Freigabefenster, Einsicht und Zugriffsprotokoll, Nachweise und Löschen – in Klartext.
---

Die Daten in Zeltlotse gehören der Person, nicht der Organisation und nicht der Freizeit. Eine Freizeit bekommt nur eine befristete Einsicht, und nur in das, was sie abgefragt hat. Diese Seite erklärt die Regeln, die für alle Organisationen gleich gelten.

## Freigabe: wer den Freizeitpass sieht

Der Freizeitpass enthält Angaben wie Allergien, Medikamente und Notfallkontakte. Mit der Anmeldung zu einer Freizeit wird er für diese Freizeit freigegeben, und zwar in zwei Stufen:

- **Leitung ab Eingang der Anmeldung.** Sobald die Anmeldung eingegangen ist, sieht die Leitung der Freizeit den Freizeitpass.
- **Team ab Bestätigung.** Erst wenn die Leitung die Anmeldung bestätigt hat, sieht auch das Team den Freizeitpass.
- **Absage beendet die Freigabe sofort.**

Die Freizeit sieht immer nur die Antworten auf die Fragen, die sie für den Freizeitpass ausgewählt hat. Andere Angaben bleiben verborgen. Erhoben wird nur, was die Leitung im Fragenbaukasten auswählt.

Org-Admin und Org-Überblick sehen keine Freizeitpässe, außer in Freizeiten, in denen sie selbst mitarbeiten.

## Freigabefenster: wann die Einsicht endet

Die Einsicht ist befristet. Das **Freigabefenster** endet nach einem festen Zeitraum, den die Organisation festlegt. Dieser Zeitraum beginnt am **Ende des letzten Freizeittages** (24:00 Uhr deutscher Zeit). Er wird beim Anlegen der Freizeit festgeschrieben und lässt sich danach nicht mehr ändern. Solange eine Organisation keinen eigenen Zeitraum festgelegt hat, gilt die Vorgabe von **30 Tagen**. Beispiel: Endet eine Freizeit am 13.08.2027, hat sie Einsicht bis einschließlich 12.09.2027.

Eltern und Teilnehmende sehen, bis wann die Freizeit Einsicht hat. Nach dem Ende des Freigabefensters sieht die Freizeit den Freizeitpass nicht mehr. Der Freizeitpass selbst bleibt bei der Person und wird bei der nächsten Freizeit vorausgefüllt.

Der Freizeitpass ist für eine Freizeit nötig und lässt sich daher nicht widerrufen, nur ändern. Freiwillige Einwilligungen, etwa für Fotos, werden einzeln erteilt und lassen sich jederzeit widerrufen.

## Einsicht und Zugriffsprotokoll

Jeder Blick in einen Freizeitpass wird festgehalten: **wer** wann **wessen** Angaben angesehen hat, in welcher Freizeit und wofür. Im **Zugriffsprotokoll** sieht jede Person, wer wann Einsicht in ihre Daten hatte, Eltern auch für ihre Kinder.

Außerdem gibt es per Klick:

- eine **Auskunft** über alle gespeicherten Daten,
- einen **Export** der eigenen Daten als PDF oder CSV.

## Nachweise: nur eine einmalige Einsicht

Nachweise wie Führungszeugnis, JuLeiCa (Jugendleiter-Card) oder Erste-Hilfe-Bescheinigung liegen nur am eigenen Profil. Eine Organisation bekommt davon keine Kopie:

- **Die Datei liegt nur am Profil,** verschlüsselt mit dem Schlüssel der Person. Zum Nachweis speichert Zeltlotse Art, Ausstellungsdatum und wann die Datei hochgeladen wurde.
- **Die Freigabe gilt höchstens 14 Tage und für genau eine prüfende Person.** Die Person gibt frei; eine Leitung der Freizeit oder ein Org-Admin übernimmt die Prüfung und ist ab dann die einzige, die die Dateien öffnen kann. Jede Datei lässt sich nur bis zur Entscheidung öffnen, danach endet der Zugriff.
- **Der Hinweis bei einer Ablehnung ist verschlüsselt** gespeichert, ebenfalls mit dem Schlüssel der Person. Er verschwindet, sobald eine neue Datei oder ein anderes Ausstellungsdatum da ist.
- **Die Organisation speichert nur die Bestätigung:** welcher Nachweis, eingesehen am, gültig bis und von wem. Keine Datei und kein Ausstellungsdatum. „Gültig bis“ rechnet Zeltlotse aus dem Ausstellungsdatum und der Richtlinie der Organisation aus.
- **Mehrere Organisationen:** Dieselben Nachweise lassen sich mehreren Organisationen zeigen, ohne sie neu hochzuladen. Jede Organisation sieht nur ihre eigene Bestätigung.

## Löschen

- **Eigenes Konto löschen:** Jede Person kann ihr Konto per Klick löschen. Persönliche Daten wie Profil, Freizeitpass und Nachweise werden dann endgültig gelöscht. Läuft gerade noch ein Freigabefenster, geschieht das nach dessen Ende.
- **Gast-Anmeldung ohne Konto:** Ein Profil ohne Konto wird gelöscht, sobald kein Freigabefenster mehr läuft.
- **Fristen der Organisation:** Daten, die eine Organisation eine Zeit lang aufbewahren muss, etwa Teilnehmerlisten, Belege oder archivierte Vorfall-Notizen, löscht Zeltlotse automatisch nach den Fristen, die die Organisation festlegt. Zeltlotse gibt dafür Vorgabewerte vor.
- **Endgültig, auch in Sicherungen:** Gelöschte Daten sind nicht nur ausgeblendet, sondern unlesbar – auch in Sicherungskopien.

## Konto und Anmeldung

Zum Konto speichert Zeltlotse die Mail-Adresse und, falls festgelegt, ein Passwort nur als Prüfwert, aus dem sich das Passwort nicht zurückrechnen lässt. Login-Links und Login-Codes stehen ebenfalls nur als Prüfwert in der Datenbank und gelten 15 Minuten.

## Organisationen und Rollen

- **Einladungen:** Eine Einladung als Org-Admin oder Org-Überblick speichert die **Mail-Adresse**, an die sie ging, die Rolle und wer eingeladen hat. Der Link selbst steht nur als Prüfwert in der Datenbank und gilt 7 Tage. Angenommen werden kann er nur mit einem Konto unter genau dieser Mail-Adresse.
- **Org-Admin:** sieht von den Personen mit einer Rolle in der Organisation nur **Vor- und Nachname** und die Rolle, keine Profile und keine Mail-Adressen. Bei offenen Einladungen sieht er die eingeladene Mail-Adresse.
- **Global Admin:** sieht nur die Namen der Organisationen und ob sie eingerichtet sind, **keine Personendaten**.
- **Rollenvergaben** und fehlgeschlagene Versuche, eine Einladung anzunehmen, stehen im Sicherheitsprotokoll, nur mit Kennungen.

## Freizeiten und Leitung

- **Leitungs-Einladungen:** Eine Einladung als Leitung einer Freizeit speichert die **Mail-Adresse**, an die sie ging, die Freizeit und wer eingeladen hat. Der Link steht nur als Prüfwert in der Datenbank, gilt 7 Tage und nur einmal und lässt sich nur mit einem Konto unter genau dieser Mail-Adresse annehmen.
- **Org-Admin:** sieht von der Leitung einer Freizeit nur **Vor- und Nachname**, keine Profile und keine Mail-Adressen, und keine Inhalte der Freizeit. Bei offenen Leitungs-Einladungen sieht er die eingeladene Mail-Adresse.
- **Org-Überblick:** sieht nur Name, Zeitraum und Phase der Freizeiten.

## Familie und Übergabe

- **Kinderprofile:** Zu einem Kind speichert Zeltlotse Vorname, Nachname (freiwillig) und **Geburtsdatum**. Das Geburtsdatum braucht es für die Grenze von 18 Jahren; daraus berechnet Zeltlotse, ab wann ein Profil übergeben werden kann.
- **Eltern sehen ihre Kinder nur bis zur Übergabe.** Beide Eltern eines Familienkontos sehen und ändern die Profile der Kinder. Übernimmt ein volljähriges Kind sein Profil, verliert das Familienkonto sofort jeden Zugriff darauf. Andere Personen, auch aus derselben Organisation, sehen die Kinder eines Familienkontos nicht.
- **Einladung und Übergabe speichern die Mail-Adresse,** an die sie gingen, dazu wer sie ausgelöst hat. Der Link selbst steht nur als Prüfwert in der Datenbank, gilt nur einmal (Einladung 7 Tage, Übergabe 14 Tage) und lässt sich nur mit einem Konto unter genau dieser Mail-Adresse annehmen. Eltern sehen die Mail-Adresse ihrer offenen Einladungen und Übergaben.
- **Das leere eigene Profil wird beim Übernehmen gelöscht.** Wer mit einer neuen Mail-Adresse ein Konto anlegt, um ein Profil zu übernehmen, bekommt dabei zunächst ein leeres Profil. Beim Übernehmen wird es samt Schlüssel endgültig gelöscht; das Konto gehört danach zum übernommenen Profil. Profile mit Inhalt werden nie zusammengeführt.
- **Erneute Anmeldung:** Eine Übergabe lässt sich nur auslösen, wenn die letzte Anmeldung höchstens 10 Minuten zurückliegt. Angenommene Einladungen, Übergaben und fehlgeschlagene Versuche stehen im Sicherheitsprotokoll, nur mit Kennungen.

## Wo die Daten liegen

Alle Daten und Sicherungen liegen **ausschließlich in der EU**. Besonders schützenswerte Angaben wie Freizeitpass-Antworten und Nachweise sind verschlüsselt gespeichert.
