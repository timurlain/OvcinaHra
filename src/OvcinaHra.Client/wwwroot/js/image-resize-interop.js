// [loc-placement] client-side image resize before upload.
// Phone cameras commonly produce 10–20 MB JPEGs which blow past
// the server's accept limit; we resize to maxDim on the longest
// side and re-encode as JPEG at the requested quality.
window.ovcinaImageResize = window.ovcinaImageResize || {};

window.ovcinaImageResize.fromBase64 = async function (dataUrl, maxDim, quality) {
    return new Promise((resolve, reject) => {
        if (typeof dataUrl !== "string" || dataUrl.length === 0) {
            console.log("[loc-placement] resize.failed reason=empty-input");
            reject(new Error("Empty data URL"));
            return;
        }
        const img = new Image();
        img.onload = function () {
            try {
                const w = img.width;
                const h = img.height;
                let nw = w;
                let nh = h;
                if (Math.max(w, h) > maxDim) {
                    if (w >= h) {
                        nw = maxDim;
                        nh = Math.round(h * (maxDim / w));
                    } else {
                        nh = maxDim;
                        nw = Math.round(w * (maxDim / h));
                    }
                }
                const canvas = document.createElement("canvas");
                canvas.width = nw;
                canvas.height = nh;
                const ctx = canvas.getContext("2d");
                ctx.drawImage(img, 0, 0, nw, nh);
                const out = canvas.toDataURL("image/jpeg", quality);
                console.log(
                    `[loc-placement] resize.done original=${w}x${h} resized=${nw}x${nh} ` +
                    `originalDataUrlBytes=${dataUrl.length} resizedDataUrlBytes=${out.length}`
                );
                resolve(out);
            } catch (err) {
                console.log(`[loc-placement] resize.failed reason=draw-error detail=${err && err.message}`);
                reject(err);
            }
        };
        img.onerror = function () {
            console.log("[loc-placement] resize.failed reason=image-load-error");
            reject(new Error("Failed to load image for resize"));
        };
        img.src = dataUrl;
    });
};
