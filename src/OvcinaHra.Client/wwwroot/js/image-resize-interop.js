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

window.ovcinaExif = window.ovcinaExif || {};

const GPS_DRIFT_THRESHOLD_M = 50;
window.ovcinaExif.gpsDriftThresholdMeters = GPS_DRIFT_THRESHOLD_M;

window.ovcinaExif.haversineMeters = function (lat1, lng1, lat2, lng2) {
    const toRad = value => value * Math.PI / 180;
    const earthRadiusMeters = 6371000;
    const dLat = toRad(lat2 - lat1);
    const dLng = toRad(lng2 - lng1);
    const a =
        Math.sin(dLat / 2) * Math.sin(dLat / 2) +
        Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) *
        Math.sin(dLng / 2) * Math.sin(dLng / 2);
    return earthRadiusMeters * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
};

window.ovcinaExif.readGpsFromDataUrl = async function (dataUrl) {
    if (typeof dataUrl !== "string" || dataUrl.length === 0) {
        return null;
    }

    const buffer = dataUrlToArrayBuffer(dataUrl);
    return readExifGps(buffer);
};

function dataUrlToArrayBuffer(dataUrl) {
    const commaIdx = dataUrl.indexOf(",");
    if (commaIdx < 0) {
        throw new Error("Invalid data URL");
    }

    const metadata = dataUrl.slice(0, commaIdx);
    const payload = dataUrl.slice(commaIdx + 1);
    if (!/;base64/i.test(metadata)) {
        return new TextEncoder().encode(decodeURIComponent(payload)).buffer;
    }

    const binary = atob(payload);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
}

function readExifGps(buffer) {
    const view = new DataView(buffer);
    const tiffOffset = findTiffOffset(view);
    if (tiffOffset === null) {
        return null;
    }

    const byteOrder = readAscii(view, tiffOffset, 2);
    const littleEndian = byteOrder === "II";
    if (!littleEndian && byteOrder !== "MM") {
        return null;
    }

    if (view.getUint16(tiffOffset + 2, littleEndian) !== 42) {
        return null;
    }

    const firstIfdOffset = view.getUint32(tiffOffset + 4, littleEndian);
    const gpsIfdPointer = readIfdTag(view, tiffOffset, tiffOffset + firstIfdOffset, littleEndian, 0x8825);
    if (gpsIfdPointer === null) {
        return null;
    }

    return readGpsIfd(view, tiffOffset, tiffOffset + gpsIfdPointer, littleEndian);
}

function findTiffOffset(view) {
    if (view.byteLength < 4 || view.getUint16(0, false) !== 0xffd8) {
        return null;
    }

    let offset = 2;
    while (offset + 4 < view.byteLength) {
        if (view.getUint8(offset) !== 0xff) {
            return null;
        }

        const marker = view.getUint8(offset + 1);
        offset += 2;
        if (marker === 0xda || marker === 0xd9) {
            return null;
        }

        const segmentLength = view.getUint16(offset, false);
        if (segmentLength < 2 || offset + segmentLength > view.byteLength) {
            return null;
        }

        const segmentStart = offset + 2;
        if (marker === 0xe1
            && segmentLength >= 8
            && readAscii(view, segmentStart, 6) === "Exif\0\0") {
            return segmentStart + 6;
        }

        offset += segmentLength;
    }

    return null;
}

function readIfdTag(view, tiffOffset, ifdOffset, littleEndian, targetTag) {
    if (ifdOffset < 0 || ifdOffset + 2 > view.byteLength) {
        return null;
    }

    const entryCount = view.getUint16(ifdOffset, littleEndian);
    for (let i = 0; i < entryCount; i++) {
        const entryOffset = ifdOffset + 2 + i * 12;
        if (entryOffset + 12 > view.byteLength) {
            return null;
        }

        const tag = view.getUint16(entryOffset, littleEndian);
        if (tag !== targetTag) {
            continue;
        }

        const type = view.getUint16(entryOffset + 2, littleEndian);
        const count = view.getUint32(entryOffset + 4, littleEndian);
        return readExifValue(view, tiffOffset, entryOffset, type, count, littleEndian);
    }

    return null;
}

function readGpsIfd(view, tiffOffset, gpsIfdOffset, littleEndian) {
    if (gpsIfdOffset < 0 || gpsIfdOffset + 2 > view.byteLength) {
        return null;
    }

    const entryCount = view.getUint16(gpsIfdOffset, littleEndian);
    let latRef = null;
    let lngRef = null;
    let latDms = null;
    let lngDms = null;

    for (let i = 0; i < entryCount; i++) {
        const entryOffset = gpsIfdOffset + 2 + i * 12;
        if (entryOffset + 12 > view.byteLength) {
            return null;
        }

        const tag = view.getUint16(entryOffset, littleEndian);
        const type = view.getUint16(entryOffset + 2, littleEndian);
        const count = view.getUint32(entryOffset + 4, littleEndian);
        const valueOffset = valueDataOffset(view, tiffOffset, entryOffset, type, count, littleEndian);
        if (valueOffset === null) {
            continue;
        }

        switch (tag) {
            case 0x0001:
                latRef = readAscii(view, valueOffset, count).replace(/\0/g, "").trim();
                break;
            case 0x0002:
                latDms = readRationals(view, valueOffset, count, littleEndian);
                break;
            case 0x0003:
                lngRef = readAscii(view, valueOffset, count).replace(/\0/g, "").trim();
                break;
            case 0x0004:
                lngDms = readRationals(view, valueOffset, count, littleEndian);
                break;
        }
    }

    if (!latRef || !lngRef || !latDms || !lngDms || latDms.length < 3 || lngDms.length < 3) {
        return null;
    }

    let lat = dmsToDecimal(latDms);
    let lng = dmsToDecimal(lngDms);
    if (latRef.toUpperCase() === "S") {
        lat = -lat;
    }
    if (lngRef.toUpperCase() === "W") {
        lng = -lng;
    }

    if (!Number.isFinite(lat) || !Number.isFinite(lng)
        || Math.abs(lat) > 90 || Math.abs(lng) > 180) {
        return null;
    }

    return { lat, lng };
}

function readExifValue(view, tiffOffset, entryOffset, type, count, littleEndian) {
    const valueOffset = valueDataOffset(view, tiffOffset, entryOffset, type, count, littleEndian);
    if (valueOffset === null) {
        return null;
    }

    switch (type) {
        case 3:
            return view.getUint16(valueOffset, littleEndian);
        case 4:
            return view.getUint32(valueOffset, littleEndian);
        default:
            return view.getUint32(entryOffset + 8, littleEndian);
    }
}

function valueDataOffset(view, tiffOffset, entryOffset, type, count, littleEndian) {
    const typeSize = { 1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 7: 1 }[type];
    if (!typeSize) {
        return null;
    }

    const byteLength = typeSize * count;
    const offset = byteLength <= 4
        ? entryOffset + 8
        : tiffOffset + view.getUint32(entryOffset + 8, littleEndian);
    return offset >= 0 && offset + byteLength <= view.byteLength ? offset : null;
}

function readRationals(view, offset, count, littleEndian) {
    const values = [];
    for (let i = 0; i < count; i++) {
        const itemOffset = offset + i * 8;
        if (itemOffset + 8 > view.byteLength) {
            return null;
        }

        const numerator = view.getUint32(itemOffset, littleEndian);
        const denominator = view.getUint32(itemOffset + 4, littleEndian);
        if (denominator === 0) {
            return null;
        }

        values.push(numerator / denominator);
    }
    return values;
}

function dmsToDecimal(values) {
    return values[0] + values[1] / 60 + values[2] / 3600;
}

function readAscii(view, offset, length) {
    let value = "";
    const end = Math.min(offset + length, view.byteLength);
    for (let i = offset; i < end; i++) {
        value += String.fromCharCode(view.getUint8(i));
    }
    return value;
}
