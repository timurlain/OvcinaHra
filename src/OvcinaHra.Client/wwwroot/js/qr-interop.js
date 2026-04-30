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
        try {
            this._scanner = new Html5Qrcode(this._lastElementId);
            await this._scanner.start(
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
    }
};

// Wake the camera up when the tab returns to the foreground. Mobile browsers
// release the getUserMedia stream when the page is backgrounded, and html5-qrcode
// doesn't auto-resume — without this the user has to min/max the app to wake it.
document.addEventListener('visibilitychange', async () => {
    if (document.visibilityState !== 'visible') return;
    const q = window.ohQr;
    if (!q || !q._lastElementId || q._scanner || q._starting) return;
    const el = document.getElementById(q._lastElementId);
    if (el) {
        await q._startInternal();
    }
});
