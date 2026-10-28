const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", recover);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", recover);

let state = "hide";
let recovering = false;
let retryTimer;

document.addEventListener("visibilitychange", recoverWhenVisible);
window.addEventListener("online", recoverWhenVisible);
window.addEventListener("pageshow", recoverWhenVisible);
reconnectModal.addEventListener("cancel", event => event.preventDefault());

function handleReconnectStateChanged(event) {
    state = event.detail.state;
    clearTimeout(retryTimer);
    if (state === "hide") {
        reconnectModal.close();
        return;
    }

    if (!reconnectModal.open) {
        reconnectModal.showModal();
    }

    // Let Blazor finish its initial retries before taking over recovery.
    recoverWhenVisible();
}

function canRecover() {
    return document.visibilityState === "visible"
        && navigator.onLine !== false
        && ["failed", "rejected", "paused", "resume-failed"].includes(state);
}

function recoverWhenVisible() {
    clearTimeout(retryTimer);
    if (canRecover()) {
        void recover();
    }
}

async function recover() {
    if (recovering || !canRecover()) {
        return;
    }

    clearTimeout(retryTimer);
    recovering = true;
    retryButton.disabled = true;
    resumeButton.disabled = true;
    try {
        const successful = state === "failed"
            ? await Blazor.reconnect() || await Blazor.resumeCircuit()
            : await Blazor.resumeCircuit();

        if (!successful) {
            // The server was reached but no longer has this circuit. Never replay a form submission.
            state = "hide";
            location.reload();
            return;
        }

        state = "hide";
        reconnectModal.close();
    } catch (error) {
        console.warn("Session recovery failed; retrying when online and visible.", error);
    } finally {
        recovering = false;
        retryButton.disabled = false;
        resumeButton.disabled = false;
        if (canRecover()) {
            retryTimer = setTimeout(recoverWhenVisible, 5000);
        }
    }
}
