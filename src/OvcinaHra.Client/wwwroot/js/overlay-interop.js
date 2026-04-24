// Vector overlay editor for the MapLibre map — issue #96, Phase 1+2.
//
// Exposes a `window.ovcinaOverlay` global. Pairs with the main `window.ovcinaMap`
// MapLibre instance (MapPage.razor / TreasurePlanning.razor both boot one map).
// Operates on the same MapLibre instance — doesn't manage its own map.
//
// Shape wire format matches `OvcinaHra.Shared.Dtos.MapOverlayShape`:
//   { type: "text"|"freehand"|"polyline"|"rectangle"|"circle"|"polygon",
//     id, color, strokeWidth?, fillColor?, ... geometry ... }
//
// Phase 3 will add icons + arrows + select-to-edit; this file intentionally
// has no selection/hit-test helpers yet.

(function () {
    const SRC_SAVED = 'oh-overlay-src';
    const SRC_PREVIEW = 'oh-overlay-preview-src';
    const LYR_FILLS = 'oh-overlay-fills';
    const LYR_STROKES = 'oh-overlay-strokes';
    const LYR_TEXT = 'oh-overlay-text';
    const LYR_PREVIEW_FILLS = 'oh-overlay-preview-fills';
    const LYR_PREVIEW_STROKES = 'oh-overlay-preview-strokes';
    const LYR_PREVIEW_TEXT = 'oh-overlay-preview-text';
    const CIRCLE_SEGMENTS = 32;

    // Singleton state, keyed by mapId so future multi-map callers don't collide.
    const instances = {};

    function getMap(mapId) {
        // Phase 1+2: MapView only boots the global `ovcinaMap`. If we ever host
        // overlays on a second MapLibre instance, widen this lookup to a
        // per-mapId registry the client side registers via a hook.
        return (window.ovcinaMap && window.ovcinaMap._map) ? window.ovcinaMap._map : null;
    }

    function ensureInstance(mapId) {
        if (!instances[mapId]) {
            instances[mapId] = {
                mapId: mapId,
                tool: null,
                dotnetRef: null,
                handlers: [],           // { target, event, fn } for removal
                tempPoints: [],         // in-progress polyline/polygon vertices
                dragStart: null,        // rectangle/circle drag origin {lat, lng}
                previewFeature: null,
                keydownHandler: null
            };
        }
        return instances[mapId];
    }

    // ---- GeoJSON feature builders -----------------------------------------

    function circleToPolygonRing(centerLat, centerLng, radiusMeters, segments) {
        // Geodesic circle on a sphere (Earth radius 6,371 km). 32 segments
        // reads visually round down to ~100 m radius; below that users rarely
        // draw circles anyway. Aviation Formulary §5.
        const R = 6371000.0;
        const ring = [];
        const lat1 = centerLat * Math.PI / 180;
        const lng1 = centerLng * Math.PI / 180;
        const d = radiusMeters / R;
        for (let i = 0; i <= segments; i++) {
            const brng = (i / segments) * 2 * Math.PI;
            const lat2 = Math.asin(
                Math.sin(lat1) * Math.cos(d) +
                Math.cos(lat1) * Math.sin(d) * Math.cos(brng));
            const lng2 = lng1 + Math.atan2(
                Math.sin(brng) * Math.sin(d) * Math.cos(lat1),
                Math.cos(d) - Math.sin(lat1) * Math.sin(lat2));
            ring.push([lng2 * 180 / Math.PI, lat2 * 180 / Math.PI]);
        }
        return ring;
    }

    function shapeToFeature(shape) {
        const base = {
            type: 'Feature',
            properties: {
                shapeType: shape.type,
                color: shape.color,
                strokeWidth: shape.strokeWidth || 2,
                fillColor: shape.fillColor || null,
                text: shape.text || null,
                fontSize: shape.fontSize || 14
            }
        };
        switch (shape.type) {
            case 'text':
                base.geometry = { type: 'Point', coordinates: [shape.coord.lng, shape.coord.lat] };
                break;
            case 'freehand':
            case 'polyline':
                base.geometry = {
                    type: 'LineString',
                    coordinates: (shape.points || []).map(p => [p.lng, p.lat])
                };
                break;
            case 'rectangle': {
                const sw = shape.sw, ne = shape.ne;
                base.geometry = {
                    type: 'Polygon',
                    coordinates: [[
                        [sw.lng, sw.lat],
                        [ne.lng, sw.lat],
                        [ne.lng, ne.lat],
                        [sw.lng, ne.lat],
                        [sw.lng, sw.lat]
                    ]]
                };
                break;
            }
            case 'circle': {
                base.geometry = {
                    type: 'Polygon',
                    coordinates: [circleToPolygonRing(shape.center.lat, shape.center.lng,
                        shape.radiusMeters, CIRCLE_SEGMENTS)]
                };
                break;
            }
            case 'polygon': {
                const pts = (shape.points || []).map(p => [p.lng, p.lat]);
                if (pts.length > 0) {
                    const first = pts[0], last = pts[pts.length - 1];
                    if (first[0] !== last[0] || first[1] !== last[1]) pts.push([first[0], first[1]]);
                }
                base.geometry = { type: 'Polygon', coordinates: [pts] };
                break;
            }
            default:
                return null;
        }
        return base;
    }

    // ---- Source + layer management ----------------------------------------

    function ensureLayers(map, sourceId, fillsId, strokesId, textId) {
        if (!map.getSource(sourceId)) {
            map.addSource(sourceId, {
                type: 'geojson',
                data: { type: 'FeatureCollection', features: [] }
            });
        }
        if (!map.getLayer(fillsId)) {
            map.addLayer({
                id: fillsId,
                type: 'fill',
                source: sourceId,
                filter: ['all',
                    ['in', ['get', 'shapeType'], ['literal', ['rectangle', 'circle', 'polygon']]],
                    ['!=', ['get', 'fillColor'], null]
                ],
                paint: {
                    'fill-color': ['coalesce', ['get', 'fillColor'], '#000000'],
                    'fill-opacity': 0.3
                }
            });
        }
        if (!map.getLayer(strokesId)) {
            map.addLayer({
                id: strokesId,
                type: 'line',
                source: sourceId,
                filter: ['!=', ['get', 'shapeType'], 'text'],
                paint: {
                    'line-color': ['coalesce', ['get', 'color'], '#000000'],
                    'line-width': ['coalesce', ['get', 'strokeWidth'], 2]
                }
            });
        }
        if (!map.getLayer(textId)) {
            map.addLayer({
                id: textId,
                type: 'symbol',
                source: sourceId,
                filter: ['==', ['get', 'shapeType'], 'text'],
                layout: {
                    'text-field': ['get', 'text'],
                    'text-size': ['coalesce', ['get', 'fontSize'], 14],
                    'text-anchor': 'left',
                    'text-offset': [0.5, 0],
                    'text-allow-overlap': true
                },
                paint: {
                    'text-color': ['coalesce', ['get', 'color'], '#000000'],
                    'text-halo-color': '#ffffff',
                    'text-halo-width': 2
                }
            });
        }
    }

    function setSourceData(map, sourceId, features) {
        const src = map.getSource(sourceId);
        if (src) src.setData({ type: 'FeatureCollection', features: features });
    }

    function teardownPreview(map) {
        setSourceData(map, SRC_PREVIEW, []);
    }

    // ---- Public API -------------------------------------------------------

    function render(mapId, shapes) {
        const map = getMap(mapId);
        if (!map) return;
        const paint = () => {
            ensureLayers(map, SRC_SAVED, LYR_FILLS, LYR_STROKES, LYR_TEXT);
            ensureLayers(map, SRC_PREVIEW, LYR_PREVIEW_FILLS, LYR_PREVIEW_STROKES, LYR_PREVIEW_TEXT);
            const features = (shapes || [])
                .map(shapeToFeature)
                .filter(f => f !== null);
            setSourceData(map, SRC_SAVED, features);
        };
        if (map.isStyleLoaded()) paint(); else map.once('styledata', paint);
    }

    function enterEditMode(mapId, tool, dotnetRef) {
        const map = getMap(mapId);
        if (!map) return;
        // If the editor is invoked before `render()` has been called (e.g.
        // game has no saved overlay yet) the source+layer pair is missing and
        // preview updates would no-op — make sure both exist before wiring
        // handlers.
        const setup = () => {
            ensureLayers(map, SRC_SAVED, LYR_FILLS, LYR_STROKES, LYR_TEXT);
            ensureLayers(map, SRC_PREVIEW, LYR_PREVIEW_FILLS, LYR_PREVIEW_STROKES, LYR_PREVIEW_TEXT);
            const inst = ensureInstance(mapId);
            inst.tool = tool;
            inst.dotnetRef = dotnetRef;
            map.getCanvas().style.cursor = 'crosshair';
            // Disable map-level double-click zoom so polyline/polygon "dblclick
            // to finish" doesn't also zoom the viewport.
            if (map.doubleClickZoom && map.doubleClickZoom.disable) map.doubleClickZoom.disable();
            attachHandlers(map, inst);
        };
        if (map.isStyleLoaded()) setup(); else map.once('styledata', setup);
    }

    function switchTool(mapId, tool) {
        const inst = instances[mapId];
        if (!inst) return;
        const map = getMap(mapId);
        if (!map) return;
        detachHandlers(map, inst);
        resetInProgress(inst);
        teardownPreview(map);
        inst.tool = tool;
        attachHandlers(map, inst);
    }

    function exitEditMode(mapId) {
        const inst = instances[mapId];
        if (!inst) return;
        const map = getMap(mapId);
        if (map) {
            detachHandlers(map, inst);
            map.getCanvas().style.cursor = '';
            if (map.doubleClickZoom && map.doubleClickZoom.enable) map.doubleClickZoom.enable();
            teardownPreview(map);
        }
        resetInProgress(inst);
        inst.tool = null;
        inst.dotnetRef = null;
    }

    // ---- Handler plumbing -------------------------------------------------

    function attachHandlers(map, inst) {
        switch (inst.tool) {
            case 'text':      attachTextTool(map, inst); break;
            case 'freehand':  attachFreehandTool(map, inst); break;
            case 'polyline':  attachPolylineTool(map, inst, /*closeLoop*/ false); break;
            case 'polygon':   attachPolylineTool(map, inst, /*closeLoop*/ true); break;
            case 'rectangle': attachRectangleTool(map, inst); break;
            case 'circle':    attachCircleTool(map, inst); break;
        }
    }

    function detachHandlers(map, inst) {
        // Always restore map panning — if the user interrupted a freehand,
        // rectangle, or circle drag mid-stroke (tool switch / exit / nav away),
        // the matching mouseup never fired and `dragPan` would stay disabled,
        // leaving the map stuck.
        if (map && map.dragPan && map.dragPan.enable) map.dragPan.enable();
        for (const h of inst.handlers) {
            try {
                if (h.target === map) map.off(h.event, h.fn);
                else h.target.removeEventListener(h.event, h.fn);
            } catch (e) { /* swallow — removing handlers is best-effort */ }
        }
        inst.handlers = [];
        if (inst.keydownHandler) {
            window.removeEventListener('keydown', inst.keydownHandler);
            inst.keydownHandler = null;
        }
    }

    function resetInProgress(inst) {
        inst.tempPoints = [];
        inst.dragStart = null;
        inst.previewFeature = null;
    }

    function onMap(map, inst, evt, fn) {
        map.on(evt, fn);
        inst.handlers.push({ target: map, event: evt, fn: fn });
    }

    function emitShape(inst, shape) {
        if (!inst.dotnetRef) return;
        try {
            inst.dotnetRef.invokeMethodAsync('OnShapeComplete', JSON.stringify(shape));
        } catch (e) {
            console.warn('Overlay shape emit failed:', e);
        }
    }

    function uuid() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }
        // RFC4122 v4 fallback for older browsers.
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
            const r = Math.random() * 16 | 0;
            const v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    // ---- Tool: text -------------------------------------------------------

    function attachTextTool(map, inst) {
        onMap(map, inst, 'click', function (e) {
            // Phase 1 uses window.prompt; Phase 3 replaces with an inline editor.
            const text = window.prompt('Text:');
            if (!text) return;
            emitShape(inst, {
                id: uuid(),
                type: 'text',
                color: currentStyle().color,
                coord: { lat: e.lngLat.lat, lng: e.lngLat.lng },
                text: text,
                fontSize: currentStyle().fontSize || 14
            });
        });
    }

    // ---- Tool: freehand ---------------------------------------------------

    function attachFreehandTool(map, inst) {
        let drawing = false;
        const onDown = function (e) {
            drawing = true;
            inst.tempPoints = [{ lat: e.lngLat.lat, lng: e.lngLat.lng }];
            if (map.dragPan && map.dragPan.disable) map.dragPan.disable();
        };
        const onMove = function (e) {
            if (!drawing) return;
            inst.tempPoints.push({ lat: e.lngLat.lat, lng: e.lngLat.lng });
            updatePreview(map, {
                type: 'freehand', color: currentStyle().color,
                strokeWidth: currentStyle().strokeWidth, points: inst.tempPoints
            });
        };
        const onUp = function () {
            if (!drawing) return;
            drawing = false;
            if (map.dragPan && map.dragPan.enable) map.dragPan.enable();
            if (inst.tempPoints.length >= 2) {
                emitShape(inst, {
                    id: uuid(),
                    type: 'freehand',
                    color: currentStyle().color,
                    strokeWidth: currentStyle().strokeWidth,
                    points: inst.tempPoints.slice()
                });
            }
            inst.tempPoints = [];
            teardownPreview(map);
        };
        onMap(map, inst, 'mousedown', onDown);
        onMap(map, inst, 'mousemove', onMove);
        onMap(map, inst, 'mouseup', onUp);
    }

    // ---- Tool: polyline / polygon ----------------------------------------

    function attachPolylineTool(map, inst, closeLoop) {
        const onClick = function (e) {
            inst.tempPoints.push({ lat: e.lngLat.lat, lng: e.lngLat.lng });
            refreshVertexPreview(map, inst, closeLoop);
        };
        const onDbl = function () {
            finalizePolyOrLine(inst, closeLoop);
            teardownPreview(map);
        };
        const onMove = function (e) {
            if (inst.tempPoints.length === 0) return;
            const preview = inst.tempPoints.concat([{ lat: e.lngLat.lat, lng: e.lngLat.lng }]);
            updatePreview(map, closeLoop ? {
                type: 'polygon', color: currentStyle().color,
                fillColor: currentStyle().useFill ? currentStyle().fillColor : null,
                strokeWidth: currentStyle().strokeWidth, points: preview
            } : {
                type: 'polyline', color: currentStyle().color,
                strokeWidth: currentStyle().strokeWidth, points: preview
            });
        };
        onMap(map, inst, 'click', onClick);
        onMap(map, inst, 'dblclick', onDbl);
        onMap(map, inst, 'mousemove', onMove);
        inst.keydownHandler = function (ev) {
            if (ev.key === 'Enter') { finalizePolyOrLine(inst, closeLoop); teardownPreview(map); }
            else if (ev.key === 'Escape') { inst.tempPoints = []; teardownPreview(map); }
        };
        window.addEventListener('keydown', inst.keydownHandler);
    }

    function refreshVertexPreview(map, inst, closeLoop) {
        if (inst.tempPoints.length === 0) { teardownPreview(map); return; }
        updatePreview(map, closeLoop ? {
            type: 'polygon', color: currentStyle().color,
            fillColor: currentStyle().useFill ? currentStyle().fillColor : null,
            strokeWidth: currentStyle().strokeWidth, points: inst.tempPoints
        } : {
            type: 'polyline', color: currentStyle().color,
            strokeWidth: currentStyle().strokeWidth, points: inst.tempPoints
        });
    }

    function finalizePolyOrLine(inst, closeLoop) {
        if (inst.tempPoints.length < (closeLoop ? 3 : 2)) { inst.tempPoints = []; return; }
        const points = inst.tempPoints.slice();
        emitShape(inst, closeLoop ? {
            id: uuid(), type: 'polygon',
            color: currentStyle().color,
            fillColor: currentStyle().useFill ? currentStyle().fillColor : null,
            strokeWidth: currentStyle().strokeWidth,
            points: points
        } : {
            id: uuid(), type: 'polyline',
            color: currentStyle().color,
            strokeWidth: currentStyle().strokeWidth,
            points: points
        });
        inst.tempPoints = [];
    }

    // ---- Tool: rectangle --------------------------------------------------

    function attachRectangleTool(map, inst) {
        const onDown = function (e) {
            inst.dragStart = { lat: e.lngLat.lat, lng: e.lngLat.lng };
            if (map.dragPan && map.dragPan.disable) map.dragPan.disable();
        };
        const onMove = function (e) {
            if (!inst.dragStart) return;
            const sw = {
                lat: Math.min(inst.dragStart.lat, e.lngLat.lat),
                lng: Math.min(inst.dragStart.lng, e.lngLat.lng)
            };
            const ne = {
                lat: Math.max(inst.dragStart.lat, e.lngLat.lat),
                lng: Math.max(inst.dragStart.lng, e.lngLat.lng)
            };
            updatePreview(map, {
                type: 'rectangle', color: currentStyle().color,
                fillColor: currentStyle().useFill ? currentStyle().fillColor : null,
                strokeWidth: currentStyle().strokeWidth,
                sw: sw, ne: ne
            });
        };
        const onUp = function (e) {
            if (!inst.dragStart) return;
            const sw = {
                lat: Math.min(inst.dragStart.lat, e.lngLat.lat),
                lng: Math.min(inst.dragStart.lng, e.lngLat.lng)
            };
            const ne = {
                lat: Math.max(inst.dragStart.lat, e.lngLat.lat),
                lng: Math.max(inst.dragStart.lng, e.lngLat.lng)
            };
            if (map.dragPan && map.dragPan.enable) map.dragPan.enable();
            if (Math.abs(sw.lat - ne.lat) > 1e-6 && Math.abs(sw.lng - ne.lng) > 1e-6) {
                emitShape(inst, {
                    id: uuid(), type: 'rectangle',
                    color: currentStyle().color,
                    fillColor: currentStyle().useFill ? currentStyle().fillColor : null,
                    strokeWidth: currentStyle().strokeWidth,
                    sw: sw, ne: ne
                });
            }
            inst.dragStart = null;
            teardownPreview(map);
        };
        onMap(map, inst, 'mousedown', onDown);
        onMap(map, inst, 'mousemove', onMove);
        onMap(map, inst, 'mouseup', onUp);
    }

    // ---- Tool: circle -----------------------------------------------------

    function attachCircleTool(map, inst) {
        const onDown = function (e) {
            inst.dragStart = { lat: e.lngLat.lat, lng: e.lngLat.lng };
            if (map.dragPan && map.dragPan.disable) map.dragPan.disable();
        };
        const onMove = function (e) {
            if (!inst.dragStart) return;
            const r = haversineMeters(inst.dragStart, { lat: e.lngLat.lat, lng: e.lngLat.lng });
            updatePreview(map, {
                type: 'circle', color: currentStyle().color,
                fillColor: currentStyle().useFill ? currentStyle().fillColor : null,
                strokeWidth: currentStyle().strokeWidth,
                center: inst.dragStart, radiusMeters: r
            });
        };
        const onUp = function (e) {
            if (!inst.dragStart) return;
            const r = haversineMeters(inst.dragStart, { lat: e.lngLat.lat, lng: e.lngLat.lng });
            if (map.dragPan && map.dragPan.enable) map.dragPan.enable();
            if (r >= 5) {
                emitShape(inst, {
                    id: uuid(), type: 'circle',
                    color: currentStyle().color,
                    fillColor: currentStyle().useFill ? currentStyle().fillColor : null,
                    strokeWidth: currentStyle().strokeWidth,
                    center: inst.dragStart, radiusMeters: r
                });
            }
            inst.dragStart = null;
            teardownPreview(map);
        };
        onMap(map, inst, 'mousedown', onDown);
        onMap(map, inst, 'mousemove', onMove);
        onMap(map, inst, 'mouseup', onUp);
    }

    function haversineMeters(a, b) {
        const R = 6371000.0;
        const lat1 = a.lat * Math.PI / 180, lat2 = b.lat * Math.PI / 180;
        const dLat = (b.lat - a.lat) * Math.PI / 180;
        const dLng = (b.lng - a.lng) * Math.PI / 180;
        const s = Math.sin(dLat / 2) * Math.sin(dLat / 2)
            + Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLng / 2) * Math.sin(dLng / 2);
        return 2 * R * Math.atan2(Math.sqrt(s), Math.sqrt(1 - s));
    }

    // ---- Preview rendering ------------------------------------------------

    function updatePreview(map, shape) {
        const feature = shapeToFeature(shape);
        if (!feature) { teardownPreview(map); return; }
        setSourceData(map, SRC_PREVIEW, [feature]);
    }

    // ---- Style config set by the toolbar ----------------------------------

    let _style = {
        color: '#242F3D',
        fillColor: '#242F3D',
        strokeWidth: 2,
        useFill: false,
        fontSize: 14
    };

    function currentStyle() { return _style; }

    function setStyle(partial) {
        _style = Object.assign({}, _style, partial || {});
    }

    // ---- Exports ----------------------------------------------------------

    function dispose(mapId) {
        const inst = instances[mapId];
        if (!inst) return;
        const map = getMap(mapId);
        if (map) {
            // detachHandlers also re-enables dragPan if it was disabled mid-draw.
            detachHandlers(map, inst);
            try { teardownPreview(map); } catch (e) { /* map may be gone */ }
            // Remove overlay source + layers so a subsequent remount starts
            // clean — avoids orphaned style-dependent layers when the user
            // navigates away and back.
            try {
                for (const lyr of [
                    LYR_FILLS, LYR_STROKES, LYR_TEXT,
                    LYR_PREVIEW_FILLS, LYR_PREVIEW_STROKES, LYR_PREVIEW_TEXT
                ]) {
                    if (map.getLayer(lyr)) map.removeLayer(lyr);
                }
                for (const src of [SRC_SAVED, SRC_PREVIEW]) {
                    if (map.getSource(src)) map.removeSource(src);
                }
            } catch (e) { /* best-effort */ }
            try {
                map.getCanvas().style.cursor = '';
                if (map.doubleClickZoom && map.doubleClickZoom.enable) map.doubleClickZoom.enable();
            } catch (e) { /* best-effort */ }
        }
        delete instances[mapId];
    }

    window.ovcinaOverlay = {
        render: render,
        enterEditMode: enterEditMode,
        switchTool: switchTool,
        exitEditMode: exitEditMode,
        dispose: dispose,
        setStyle: setStyle,
        // Exposed for unit-testability / future tools; not called by Blazor.
        _circleToPolygonRing: circleToPolygonRing
    };
})();
