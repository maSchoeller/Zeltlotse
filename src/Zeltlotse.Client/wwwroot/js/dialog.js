// Merkt sich, wer den Dialog geöffnet hat, und gibt den Fokus dorthin zurück.
// Ohne das verliert eine Tastaturbedienung nach dem Schließen ihre Stelle.
window.zeltlotseDialog = {
    merker: [],

    merkeFokus() {
        this.merker.push(document.activeElement);
    },

    stelleFokusHer() {
        const element = this.merker.pop();

        if (element && document.contains(element) && typeof element.focus === 'function') {
            element.focus();
        }
    },
};
