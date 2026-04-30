// OvčinaHra Sken — camera capture JS interop. Captures a still photo and
// returns a JPEG dataURL. Ported verbatim from Glejt with namespace renamed
// from glejtCamera → ohCamera.

window.ohCamera = {
    _stream: null,
    _videoEl: null,

    async openOn(videoElementId) {
        try {
            // Defensive cleanup — if openOn is called twice (retake flow,
            // re-init after navigation), don't leak the previous stream.
            await this.close();

            this._videoEl = document.getElementById(videoElementId);
            if (!this._videoEl) {
                return { ok: false, error: `video element '${videoElementId}' not found` };
            }
            this._stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: { ideal: 'environment' } },
                audio: false
            });
            this._videoEl.srcObject = this._stream;
            await this._videoEl.play();
            return { ok: true };
        } catch (err) {
            console.error('camera open failed', err);
            return { ok: false, error: err.message };
        }
    },

    capture() {
        if (!this._videoEl) return null;
        const canvas = document.createElement('canvas');
        canvas.width = this._videoEl.videoWidth;
        canvas.height = this._videoEl.videoHeight;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(this._videoEl, 0, 0, canvas.width, canvas.height);
        return canvas.toDataURL('image/jpeg', 0.9);
    },

    async close() {
        if (this._stream) {
            this._stream.getTracks().forEach(t => t.stop());
            this._stream = null;
        }
        if (this._videoEl) {
            this._videoEl.srcObject = null;
            this._videoEl = null;
        }
    }
};
