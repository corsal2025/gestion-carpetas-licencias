// Saving a row (or adding a case, or importing) posts and reloads the whole page. Without this,
// every one of those actions snaps the operator back to the top-left of a table they were working
// through halfway down — reads as the app "jumping" or restarting on every edit. Scroll position is
// kept in sessionStorage instead of the URL so a normal filter change (which does change the URL and
// really does show a different list) still starts fresh at the top, as it should.
(function () {
    var key = 'scroll:' + location.pathname + location.search;

    function tableCard() {
        return document.querySelector('.card.table-card');
    }

    function save() {
        var card = tableCard();
        var state = {
            windowY: window.scrollY,
            cardX: card ? card.scrollLeft : 0,
            cardY: card ? card.scrollTop : 0
        };
        try {
            sessionStorage.setItem(key, JSON.stringify(state));
        } catch (e) {
            // Private browsing or a full quota — losing the scroll memory is not worth breaking the page.
        }
    }

    function restore() {
        var raw;
        try {
            raw = sessionStorage.getItem(key);
        } catch (e) {
            return;
        }
        if (!raw) return;

        var state;
        try {
            state = JSON.parse(raw);
        } catch (e) {
            return;
        }

        window.scrollTo(0, state.windowY || 0);
        var card = tableCard();
        if (card) {
            card.scrollLeft = state.cardX || 0;
            card.scrollTop = state.cardY || 0;
        }
    }

    // A GET filter (office, año, "Ver N por página"...) genuinely shows a different list and should
    // start at the top, so only POSTs (save a row, add a case, toggle, import, delete) are remembered.
    document.addEventListener('submit', function (event) {
        if (event.target instanceof HTMLFormElement && event.target.method.toUpperCase() === 'POST') {
            save();
        }
    }, true);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', restore);
    } else {
        restore();
    }
})();
