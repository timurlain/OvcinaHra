// OvčinaHra Sken — QR scanner JS interop. Wraps html5-qrcode (loaded in
// index.html via CDN). Ported verbatim from Glejt with namespace renamed
// from glejtQr → ohQr.

window.ohQr = {
    _scanner: null,
    _lastElementId: null,
    _lastCallback: null,
    _starting: false,

    async start(elementId, dotnetCallback) {
        if (typeof Html5Qrcode === 'undefined') {
            console.error('html5-qrcode not loaded');
            return false;
        }
        // Remember params so the visibilitychange listener (below) can re-start
        // the camera when the user returns from min/maxing or backgrounding.
        this._lastElementId = elementId;
        this._lastCallback = dotnetCallback;
        return await this._startInternal();
    },

    async _startInternal() {
        if (this._starting) return true;
        this._starting = true;
        let scanner = null;
        try {
            scanner = new Html5Qrcode(this._lastElementId);
            this._scanner = scanner;
            await scanner.start(
                { facingMode: 'environment' },
                { fps: 10, qrbox: { width: 250, height: 250 } },
                (decodedText) => {
                    if (this._lastCallback) {
                        this._lastCallback.invokeMethodAsync('OnQrDecoded', decodedText);
                    }
                },
                (_errorMessage) => { /* ignore per-frame parse errors */ }
            );
            return true;
        } catch (err) {
            console.error('QR start failed', err);
            // Clear _scanner on failure so visibilitychange + manual restart
            // paths can retry. Defensive clear() in case Html5Qrcode allocated
            // a video element before throwing.
            if (scanner) {
                try { await scanner.clear(); } catch (_) { /* ignore */ }
            }
            this._scanner = null;
            return false;
        } finally {
            this._starting = false;
        }
    },

    async stop() {
        if (this._scanner) {
            try {
                await this._scanner.stop();
                await this._scanner.clear();
            } catch (_) { /* ignore */ }
            this._scanner = null;
        }
    },

    // Internal — release the camera stream without forgetting _lastElementId
    // so the visibilitychange listener can restart on foreground.
    async _suspend() {
        if (this._scanner) {
            try {
                await this._scanner.stop();
                await this._scanner.clear();
            } catch (_) { /* ignore */ }
            this._scanner = null;
        }
    }
};

// Wake the camera up when the tab returns to the foreground. Mobile browsers
// release the getUserMedia stream when the page is backgrounded, and
// html5-qrcode doesn't auto-resume. We actively tear down the scanner on
// hide so the visible-branch can restart cleanly — gating on _scanner==null
// alone wouldn't fire because _scanner stays non-null even after the OS
// has revoked the camera stream.
document.addEventListener('visibilitychange', async () => {
    const q = window.ohQr;
    if (!q || !q._lastElementId) return;

    if (document.visibilityState !== 'visible') {
        await q._suspend();
        return;
    }

    if (q._scanner || q._starting) return;
    const el = document.getElementById(q._lastElementId);
    if (el) {
        await q._startInternal();
    }
});
