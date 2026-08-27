// Fokusverwaltung für Dialoge. Ohne das wandert Tab hinter den Dialog und der
// Hintergrund scrollt mit — beides macht die Tastaturbedienung unbrauchbar.
window.zeltlotseDialog = (() => {
    const merker = [];
    let taste = null;

    const fokussierbare = (wurzel) => [...wurzel.querySelectorAll(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
        'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
        .filter(e => e.offsetParent !== null);

    return {
        oeffnen() {
            merker.push(document.activeElement);
            document.body.style.overflow = 'hidden';

            // Tab am Rand des Dialogs auf die andere Seite umlenken.
            taste = (e) => {
                if (e.key !== 'Tab') {
                    return;
                }

                const dialog = document.querySelector('.zl-dialog');

                if (!dialog) {
                    return;
                }

                const elemente = fokussierbare(dialog);

                if (elemente.length === 0) {
                    return;
                }

                const erstes = elemente[0];
                const letztes = elemente[elemente.length - 1];

                if (!dialog.contains(document.activeElement)) {
                    e.preventDefault();
                    erstes.focus();
                    return;
                }

                if (e.shiftKey && document.activeElement === erstes) {
                    e.preventDefault();
                    letztes.focus();
                } else if (!e.shiftKey && document.activeElement === letztes) {
                    e.preventDefault();
                    erstes.focus();
                }
            };

            document.addEventListener('keydown', taste, true);
        },

        schliessen() {
            if (taste) {
                document.removeEventListener('keydown', taste, true);
                taste = null;
            }

            document.body.style.overflow = '';

            const element = merker.pop();

            if (element && document.contains(element) && typeof element.focus === 'function') {
                element.focus();
            }
        },
    };
})();
