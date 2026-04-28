// Vector overlay editor for the MapLibre map — issue #96, Phase 1+2 (+3).
//
// Exposes a `window.ovcinaOverlay` global. Pairs with the main `window.ovcinaMap`
// MapLibre instance (MapPage.razor / TreasurePlanning.razor both boot one map).
// Operates on the same MapLibre instance — doesn't manage its own map.
//
// Shape wire format matches `OvcinaHra.Shared.Dtos.MapOverlayShape`:
//   { type: "text"|"freehand"|"polyline"|"rectangle"|"circle"|"polygon"|"icon",
//     id, color, strokeWidth?, fillColor?, ... geometry ... }
//
// Phase 3 adds: icon primitive, select-to-edit (hit-test → property panel),
// shape update / delete via Blazor → JS interop. Arrows skipped (MapLibre
// has no native arrow head support and lines suffice).

(function () {
    const SRC_SAVED = 'oh-overlay-src';
    const SRC_PREVIEW = 'oh-overlay-preview-src';
    const SRC_SELECTION = 'oh-overlay-selection-src';
    const LYR_FILLS = 'oh-overlay-fills';
    const LYR_STROKES = 'oh-overlay-strokes';
    const LYR_TEXT = 'oh-overlay-text';
    const LYR_ICONS = 'oh-overlay-icons';
    const LYR_PREVIEW_FILLS = 'oh-overlay-preview-fills';
    const LYR_PREVIEW_STROKES = 'oh-overlay-preview-strokes';
    const LYR_PREVIEW_TEXT = 'oh-overlay-preview-text';
    const LYR_PREVIEW_ICONS = 'oh-overlay-preview-icons';
    const LYR_SELECTION_FILL = 'oh-overlay-selection-fill';
    const LYR_SELECTION_LINE = 'oh-overlay-selection-line';
    const LYR_SELECTION_PT = 'oh-overlay-selection-point';
    const CIRCLE_SEGMENTS = 32;
    // Phase 3 icon palette — slugs match wwwroot/img/overlay-icons/{key}.svg.
    // SVGs use fill="currentColor"; we rasterize them with a black silhouette
    // and add to MapLibre as SDF icons so `icon-color` tints them per shape.
    const ICON_ASSETS = ['flag', 'tent', 'chest', 'skull', 'door', 'fire'];

    // Singleton state, keyed by mapId so future multi-map callers don't collide.
    const instances = {};

    function mapDiag(event, data) {
        try {
            console.log('[map-diag] overlay.' + event + ' ' + JSON.stringify(data || {}));
        } catch (e) { }
    }

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
                keydownHandler: null,
                shapes: [],
                textMarkers: {},
                visible: true
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

    function pick(obj, name, fallback) {
        if (!obj) return fallback;
        if (obj[name] !== undefined) return obj[name];
        const pascal = name.charAt(0).toUpperCase() + name.slice(1);
        return obj[pascal] !== undefined ? obj[pascal] : fallback;
    }

    function numberOr(value, fallback) {
        const n = Number(value);
        return Number.isFinite(n) ? n : fallback;
    }

    function normalizeCoord(coord) {
        if (!coord) return null;
        const lat = numberOr(pick(coord, 'lat', NaN), NaN);
        const lng = numberOr(pick(coord, 'lng', NaN), NaN);
        return Number.isFinite(lat) && Number.isFinite(lng) ? { lat, lng } : null;
    }

    function normalizePoints(points) {
        return (points || [])
            .map(normalizeCoord)
            .filter(p => p !== null);
    }

    function normalizeShape(shape) {
        const type = pick(shape, 'type', null);
        const id = pick(shape, 'id', null);
        if (!shape || !type || !id) return null;

        const normalized = {
            id: id,
            type: type,
            color: pick(shape, 'color', '#000000') || '#000000',
            strokeWidth: numberOr(pick(shape, 'strokeWidth', 2), 2),
            fillColor: pick(shape, 'fillColor', null)
        };

        switch (type) {
            case 'text': {
                const coord = normalizeCoord(pick(shape, 'coord', null));
                if (!coord) return null;
                normalized.coord = coord;
                normalized.text = pick(shape, 'text', '') || '';
                normalized.fontSize = numberOr(pick(shape, 'fontSize', 14), 14);
                return normalized;
            }
            case 'icon': {
                const coord = normalizeCoord(pick(shape, 'coord', null));
                if (!coord) return null;
                normalized.coord = coord;
                normalized.assetKey = pick(shape, 'assetKey', null);
                normalized.rotation = numberOr(pick(shape, 'rotation', 0), 0);
                normalized.scale = numberOr(pick(shape, 'scale', 1), 1);
                return normalized;
            }
            case 'freehand':
            case 'polyline':
            case 'polygon':
                normalized.points = normalizePoints(pick(shape, 'points', []));
                return normalized;
            case 'rectangle': {
                const sw = normalizeCoord(pick(shape, 'sw', null));
                const ne = normalizeCoord(pick(shape, 'ne', null));
                if (!sw || !ne) return null;
                normalized.sw = sw;
                normalized.ne = ne;
                return normalized;
            }
            case 'circle': {
                const center = normalizeCoord(pick(shape, 'center', null));
                if (!center) return null;
                normalized.center = center;
                normalized.radiusMeters = numberOr(pick(shape, 'radiusMeters', 0), 0);
                return normalized;
            }
            default:
                return null;
        }
    }

    function shapeToFeature(shape) {
        shape = normalizeShape(shape);
        if (!shape) return null;

        const base = {
            type: 'Feature',
            id: shape.id,
            properties: {
                shapeId: shape.id,
                shapeType: shape.type,
                color: shape.color,
                strokeWidth: shape.strokeWidth || 2,
                fillColor: shape.fillColor || null,
                text: shape.text || null,
                fontSize: shape.fontSize || 14,
                iconAsset: shape.assetKey || null,
                iconRotation: shape.rotation || 0,
                iconScale: shape.scale || 1.0
            }
        };
        switch (shape.type) {
            case 'text':
            case 'icon':
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

    function clearTextMarkers(inst) {
        for (const marker of Object.values(inst.textMarkers || {})) {
            try { marker.remove(); } catch (e) { /* best-effort */ }
        }
        inst.textMarkers = {};
    }

    function renderTextMarkers(map, inst, shapes) {
        clearTextMarkers(inst);
        const canDrag = inst.editMode === true && inst.tool === 'select';
        for (const shape of shapes.filter(s => s.type === 'text')) {
            const el = document.createElement('div');
            el.className = 'oh-overlay-text-marker';
            if (canDrag) el.classList.add('oh-overlay-text-marker-draggable');
            el.textContent = shape.text;
            el.style.color = shape.color;
            el.style.fontSize = `${shape.fontSize}px`;
            el.style.display = inst.visible === false ? 'none' : '';
            el.title = canDrag ? 'Přetáhnout text' : '';
            el.addEventListener('click', function (ev) {
                ev.stopPropagation();
                if (inst.editMode === true && inst.tool === 'select') {
                    emitSelection(inst, shape.id);
                }
            });

            const marker = new maplibregl.Marker({ element: el, draggable: canDrag })
                .setLngLat([shape.coord.lng, shape.coord.lat])
                .addTo(map);

            if (canDrag) {
                marker.on('dragstart', function () {
                    emitSelection(inst, shape.id);
                });
                marker.on('dragend', function () {
                    const lngLat = marker.getLngLat();
                    emitShapeMoved(inst, shape.id, lngLat.lat, lngLat.lng);
                });
            }

            inst.textMarkers[shape.id] = marker;
        }
    }

    function setSavedOverlayVisibility(map, inst) {
        const isVisible = inst.visible !== false;
        const visibility = isVisible ? 'visible' : 'none';
        for (const layerId of [LYR_FILLS, LYR_STROKES, LYR_TEXT, LYR_ICONS]) {
            if (map.getLayer(layerId)) {
                try { map.setLayoutProperty(layerId, 'visibility', visibility); }
                catch (e) { /* style may be changing; next render reapplies */ }
            }
        }
        for (const marker of Object.values(inst.textMarkers || {})) {
            const el = marker && marker.getElement ? marker.getElement() : null;
            if (el) el.style.display = isVisible ? '' : 'none';
        }
    }

    // ---- Source + layer management ----------------------------------------

    function ensureLayers(map, sourceId, fillsId, strokesId, textId, iconsId) {
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
                filter: ['all',
                    ['!=', ['get', 'shapeType'], 'text'],
                    ['!=', ['get', 'shapeType'], 'icon']
                ],
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
        if (iconsId && !map.getLayer(iconsId)) {
            map.addLayer({
                id: iconsId,
                type: 'symbol',
                source: sourceId,
                filter: ['==', ['get', 'shapeType'], 'icon'],
                layout: {
                    'icon-image': ['get', 'iconAsset'],
                    'icon-size': ['coalesce', ['get', 'iconScale'], 1.0],
                    'icon-rotate': ['coalesce', ['get', 'iconRotation'], 0],
                    'icon-allow-overlap': true,
                    'icon-ignore-placement': true
                },
                paint: {
                    'icon-color': ['coalesce', ['get', 'color'], '#000000']
                }
            });
        }
    }

    // ---- Icon image loader ------------------------------------------------

    // Rasterize each /img/overlay-icons/{key}.svg into a 64×64 ImageData and
    // register it with MapLibre as an SDF image so `icon-color` tints it per
    // shape. Idempotent — guarded by `map.hasImage`. Triggered on first render
    // / first edit; tracked on `map.__ovcinaOverlayIconsLoaded` to short-circuit
    // subsequent calls without re-decoding the SVGs.
    function loadIconImage(map, key) {
        return new Promise(function (resolve) {
            if (map.hasImage(key)) { resolve(); return; }
            const img = new Image();
            img.crossOrigin = 'anonymous';
            const finalize = function () {
                try {
                    const size = 64;
                    const canvas = document.createElement('canvas');
                    canvas.width = size; canvas.height = size;
                    const ctx = canvas.getContext('2d');
                    ctx.clearRect(0, 0, size, size);
                    ctx.drawImage(img, 0, 0, size, size);
                    if (!map.hasImage(key)) {
                        map.addImage(key, ctx.getImageData(0, 0, size, size), { sdf: true });
                    }
                } catch (e) { console.warn('Overlay icon load failed:', key, e); }
                resolve();
            };
            img.onload = finalize;
            img.onerror = function () { console.warn('Overlay icon missing:', key); resolve(); };
            img.src = '/img/overlay-icons/' + encodeURIComponent(key) + '.svg';
        });
    }

    function ensureIconsLoaded(map) {
        // Re-check the registry every call: a basemap `setStyle()` wipes
        // `map.images`, so a once-cached promise would short-circuit and the
        // icons never re-register on the new style. If every asset is already
        // present we hand back the cached promise (resolved); otherwise a
        // fresh Promise.all repopulates only the missing keys (loadIconImage
        // is itself guarded by `map.hasImage`).
        const allRegistered = ICON_ASSETS.every(function (k) { return map.hasImage(k); });
        if (allRegistered && map.__ovcinaOverlayIconsLoaded) {
            return map.__ovcinaOverlayIconsLoaded;
        }
        map.__ovcinaOverlayIconsLoaded = Promise.all(ICON_ASSETS.map(function (k) {
            return loadIconImage(map, k);
        }));
        return map.__ovcinaOverlayIconsLoaded;
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
        if (!map) {
            mapDiag('render.no-map', { mapId: mapId });
            return;
        }
        const inst = ensureInstance(mapId);
        const normalizedShapes = (shapes || [])
            .map(normalizeShape)
            .filter(s => s !== null);
        inst.shapes = normalizedShapes;
        mapDiag('render', { mapId: mapId, shapeCount: normalizedShapes.length });

        const paint = () => {
            ensureLayers(map, SRC_SAVED, LYR_FILLS, LYR_STROKES, LYR_TEXT, LYR_ICONS);
            ensureLayers(map, SRC_PREVIEW, LYR_PREVIEW_FILLS, LYR_PREVIEW_STROKES, LYR_PREVIEW_TEXT, LYR_PREVIEW_ICONS);
            ensureSelectionLayers(map);
            const features = normalizedShapes
                .filter(s => s.type !== 'text')
                .map(shapeToFeature)
                .filter(f => f !== null);
            setSourceData(map, SRC_SAVED, features);
            renderTextMarkers(map, inst, normalizedShapes);
            setSavedOverlayVisibility(map, inst);
            // Icons load asynchronously — MapLibre will silently fail to render
            // a symbol whose `icon-image` isn't registered yet, so we trigger
            // load + force a re-paint when ready.
            ensureIconsLoaded(map).then(function () { try { map.triggerRepaint(); } catch (e) { } });
        };
        if (map.isStyleLoaded()) paint(); else map.once('styledata', paint);
    }

    function enterEditMode(mapId, tool, dotnetRef) {
        const map = getMap(mapId);
        if (!map) {
            mapDiag('enter-edit.no-map', { mapId: mapId, tool: tool });
            return;
        }
        const inst = ensureInstance(mapId);
        inst.tool = tool;
        inst.dotnetRef = dotnetRef;
        inst.editMode = true;
        mapDiag('enter-edit', {
            mapId: mapId,
            tool: tool,
            shapeCount: inst.shapes ? inst.shapes.length : 0,
            hasDotNetRef: !!dotnetRef
        });
        // If the editor is invoked before `render()` has been called (e.g.
        // game has no saved overlay yet) the source+layer pair is missing and
        // preview updates would no-op — make sure both exist before wiring
        // handlers.
        const setup = () => {
            ensureLayers(map, SRC_SAVED, LYR_FILLS, LYR_STROKES, LYR_TEXT, LYR_ICONS);
            ensureLayers(map, SRC_PREVIEW, LYR_PREVIEW_FILLS, LYR_PREVIEW_STROKES, LYR_PREVIEW_TEXT, LYR_PREVIEW_ICONS);
            ensureSelectionLayers(map);
            ensureIconsLoaded(map).then(function () { try { map.triggerRepaint(); } catch (e) { } });
            map.getCanvas().style.cursor = tool === 'select' ? '' : 'crosshair';
            // Suspend MapLibre handlers that compete with click-to-draw: drag-pan
            // would steal mousedown+mousemove from freehand/rectangle/circle and
            // box-zoom would steal Shift+drag (#159 sub-fix 2). Restored on
            // exitEditMode / dispose. Wheel zoom stays on — users still want
            // to zoom while drawing big shapes.
            suspendMapInteractions(map, inst);
            // Lock location markers so users can't accidentally relocate a pin
            // while drawing on top of it (#159 sub-fix 5). Same restore path.
            setLocationMarkersDraggable(false);
            // Tell Blazor that overlay edit-mode is active so any markers
            // re-added during the edit (e.g. SetStageFilter repopulates after
            // a filter toggle) come up locked too.
            window.__ovcinaOverlayEditMode = true;
            // Disable map-level double-click zoom so polyline/polygon "dblclick
            // to finish" doesn't also zoom the viewport.
            if (map.doubleClickZoom && map.doubleClickZoom.disable) map.doubleClickZoom.disable();
            renderTextMarkers(map, inst, inst.shapes || []);
            detachHandlers(map, inst);
            attachHandlers(map, inst);
        };
        if (map.isStyleLoaded()) setup();
        else {
            mapDiag('enter-edit.defer-style', { mapId: mapId, tool: tool });
            map.once('styledata', setup);
        }
    }

    function switchTool(mapId, tool) {
        const inst = instances[mapId];
        if (!inst) {
            mapDiag('switch-tool.no-instance', { mapId: mapId, tool: tool });
            return;
        }
        const map = getMap(mapId);
        if (!map) {
            mapDiag('switch-tool.no-map', { mapId: mapId, tool: tool });
            return;
        }
        detachHandlers(map, inst);
        resetInProgress(inst);
        teardownPreview(map);
        inst.tool = tool;
        mapDiag('switch-tool', { mapId: mapId, tool: tool });
        // Cursor: drawing tools get crosshair, select gets default (handler swaps
        // to pointer on hover-over-shape). Idle (null) also gets default.
        try { map.getCanvas().style.cursor = (tool && tool !== 'select') ? 'crosshair' : ''; } catch (e) { }
        renderTextMarkers(map, inst, inst.shapes || []);
        attachHandlers(map, inst);
    }

    function exitEditMode(mapId) {
        const inst = instances[mapId];
        if (!inst) return;
        const map = getMap(mapId);
        // Clear editMode FIRST so detachHandlers' generic dragPan.enable()
        // path can fire on its own without fighting the suspension flag.
        inst.editMode = false;
        if (map) {
            detachHandlers(map, inst);
            map.getCanvas().style.cursor = '';
            if (map.doubleClickZoom && map.doubleClickZoom.enable) map.doubleClickZoom.enable();
            restoreMapInteractions(map, inst);
            teardownPreview(map);
            renderTextMarkers(map, inst, inst.shapes || []);
        }
        // Restore location markers and the global edit-mode flag regardless
        // of map presence — a SwitchStyle race could leave markers locked.
        setLocationMarkersDraggable(true);
        window.__ovcinaOverlayEditMode = false;
        resetInProgress(inst);
        inst.tool = null;
        inst.dotnetRef = null;
    }

    // ---- Map-handler suspension (#159 sub-fix 2) -------------------------

    function suspendMapInteractions(map, inst) {
        // Snapshot prior on/off state so we restore exactly what was there.
        // MapLibre's handler objects each expose .isEnabled() in v3+; default
        // to true if missing so we err on the side of restoring.
        const snap = {};
        try { snap.dragPan = map.dragPan ? safeIsEnabled(map.dragPan, true) : false; } catch (e) { snap.dragPan = false; }
        try { snap.boxZoom = map.boxZoom ? safeIsEnabled(map.boxZoom, true) : false; } catch (e) { snap.boxZoom = false; }
        try { snap.touchRotate = !!(map.touchZoomRotate); } catch (e) { snap.touchRotate = false; }
        inst.suspendedHandlers = snap;
        try { if (snap.dragPan && map.dragPan.disable) map.dragPan.disable(); } catch (e) { }
        try { if (snap.boxZoom && map.boxZoom.disable) map.boxZoom.disable(); } catch (e) { }
        try { if (snap.touchRotate && map.touchZoomRotate.disableRotation) map.touchZoomRotate.disableRotation(); } catch (e) { }
    }

    function restoreMapInteractions(map, inst) {
        const snap = inst.suspendedHandlers || { dragPan: true, boxZoom: true, touchRotate: true };
        try { if (snap.dragPan && map.dragPan && map.dragPan.enable) map.dragPan.enable(); } catch (e) { }
        try { if (snap.boxZoom && map.boxZoom && map.boxZoom.enable) map.boxZoom.enable(); } catch (e) { }
        try { if (snap.touchRotate && map.touchZoomRotate && map.touchZoomRotate.enableRotation) map.touchZoomRotate.enableRotation(); } catch (e) { }
        inst.suspendedHandlers = null;
    }

    function safeIsEnabled(handler, fallback) {
        if (!handler) return fallback;
        if (typeof handler.isEnabled === 'function') return handler.isEnabled();
        return fallback;
    }

    // ---- Location-marker draggable toggle (#159 sub-fix 5) ----------------

    // Bridge to the main `ovcinaMap` JS API. The marker registry lives in
    // `wwwroot/js/maplibre-interop.js`; we go through a public method so
    // edit-mode coupling stays one-way (overlay → map, never the reverse).
    function setLocationMarkersDraggable(enabled) {
        try {
            if (window.ovcinaMap && typeof window.ovcinaMap.setLocationMarkersDraggable === 'function') {
                window.ovcinaMap.setLocationMarkersDraggable(!!enabled);
            }
        } catch (e) { /* best-effort — never block the editor on marker plumbing */ }
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
            case 'icon':      attachIconTool(map, inst); break;
            case 'select':    attachSelectTool(map, inst); break;
        }
    }

    function detachHandlers(map, inst) {
        // Skip the per-tool dragPan re-enable while we're still in overlay
        // edit mode — `enterEditMode` globally suspended dragPan/boxZoom and
        // `exitEditMode` restores them via restoreMapInteractions. If we
        // re-enabled here on every tool switch we'd undo that suspension and
        // map panning would steal click+drag again (#159 sub-fix 2).
        // Outside edit mode (e.g. dispose-without-exit edge case), keep the
        // legacy "always re-enable" so an interrupted draw can never strand
        // the map.
        if (!inst.editMode && map && map.dragPan && map.dragPan.enable) map.dragPan.enable();
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
        if (!inst.dotnetRef) {
            mapDiag('shape.emit.no-dotnet', { mapId: inst.mapId, type: shape && shape.type, id: shape && shape.id });
            return;
        }
        try {
            // System.Text.Json's [JsonPolymorphic] requires the discriminator
            // ("type") to be the FIRST JSON property — otherwise it throws
            // NotSupportedException with DeserializationMustSpecifyTypeDiscriminator.
            // Call sites build literals with `id` first for readability; this
            // chokepoint reorders so `type` lands first regardless. DO NOT
            // remove without also reordering every emitShape() call site.
            const orderedShape = { type: shape.type, ...shape };
            mapDiag('shape.emit', { mapId: inst.mapId, type: orderedShape.type, id: orderedShape.id });
            inst.dotnetRef.invokeMethodAsync('OnShapeComplete', JSON.stringify(orderedShape));
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
            if (!inst.dotnetRef) {
                mapDiag('text.request.no-dotnet', { mapId: inst.mapId });
                return;
            }
            inst.dotnetRef.invokeMethodAsync('OnTextPlacementRequested', e.lngLat.lat, e.lngLat.lng);
        });
    }

    // ---- Tool: freehand ---------------------------------------------------

    function attachFreehandTool(map, inst) {
        let drawing = false;
        const onDown = function (e) {
            drawing = true;
            inst.tempPoints = [{ lat: e.lngLat.lat, lng: e.lngLat.lng }];
            mapDiag('freehand.down', { mapId: inst.mapId, lat: e.lngLat.lat, lng: e.lngLat.lng });
            if (map.dragPan && map.dragPan.disable) map.dragPan.disable();
        };
        const onMove = function (e) {
            if (!drawing) return;
            inst.tempPoints.push({ lat: e.lngLat.lat, lng: e.lngLat.lng });
            updatePreview(map, {
                type: 'freehand', color: currentStyle().color,
                strokeWidth: currentStyle().strokeWidth, points: inst.tempPoints
            });
            if (inst.tempPoints.length <= 5 || inst.tempPoints.length % 10 === 0) {
                mapDiag('freehand.move', { mapId: inst.mapId, pointCount: inst.tempPoints.length });
            }
        };
        const onUp = function () {
            if (!drawing) return;
            drawing = false;
            if (map.dragPan && map.dragPan.enable) map.dragPan.enable();
            mapDiag('freehand.up', { mapId: inst.mapId, pointCount: inst.tempPoints.length });
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

    // ---- Tool: icon -------------------------------------------------------

    function attachIconTool(map, inst) {
        // Click-to-drop. Asset key + rotation/scale come from the toolbar style.
        // Color tints the SDF icon — same color picker as other primitives.
        onMap(map, inst, 'click', function (e) {
            const s = currentStyle();
            const asset = (s.iconAsset && ICON_ASSETS.indexOf(s.iconAsset) >= 0)
                ? s.iconAsset : ICON_ASSETS[0];
            emitShape(inst, {
                id: uuid(),
                type: 'icon',
                color: s.color,
                assetKey: asset,
                coord: { lat: e.lngLat.lat, lng: e.lngLat.lng },
                rotation: s.iconRotation || 0,
                scale: s.iconScale || 1.0
            });
        });
    }

    // ---- Tool: select (hit-test → emit shapeId to .NET) -------------------

    function attachSelectTool(map, inst) {
        // Resolve the hit-test layer list per-call, not at attach time. A
        // basemap style switch removes/re-adds the overlay layers; capturing
        // IDs once would leave queryRenderedFeatures referencing missing
        // layers and throw, breaking selection.
        function activeLayers() {
            return [LYR_FILLS, LYR_STROKES, LYR_TEXT, LYR_ICONS]
                .filter(function (id) { return map.getLayer(id) != null; });
        }

        const onMove = function (e) {
            const layers = activeLayers();
            if (layers.length === 0) return;
            try {
                const f = map.queryRenderedFeatures(e.point, { layers: layers });
                map.getCanvas().style.cursor = (f && f.length > 0) ? 'pointer' : '';
            } catch (err) { /* layer disappeared mid-frame — best-effort */ }
        };
        const onClick = function (e) {
            const layers = activeLayers();
            if (layers.length === 0) return;
            let features;
            try {
                features = map.queryRenderedFeatures(e.point, { layers: layers });
            } catch (err) { return; }
            if (!features || features.length === 0) {
                emitSelection(inst, null);
                return;
            }
            // Topmost feature wins. Phase 1+2 properties carry shapeId; older
            // payloads without it would have to fall back on `feature.id`.
            const f = features[0];
            const id = (f.properties && f.properties.shapeId) || f.id || null;
            if (id) emitSelection(inst, id);
        };
        onMap(map, inst, 'mousemove', onMove);
        onMap(map, inst, 'click', onClick);
        // Delete key fires while the select tool is active and a shape is
        // selected — .NET decides whether to confirm + remove. Bail if the
        // user is typing in a text field (e.g. property panel TextShape input)
        // so Backspace doesn't double as "delete shape".
        inst.keydownHandler = function (ev) {
            if (ev.key !== 'Delete' && ev.key !== 'Backspace') return;
            if (isEditableTarget(ev.target) || isEditableTarget(document.activeElement)) return;
            if (!inst.dotnetRef) return;
            try { inst.dotnetRef.invokeMethodAsync('OnDeleteRequested'); }
            catch (e) { /* swallow */ }
        };
        window.addEventListener('keydown', inst.keydownHandler);
    }

    function isEditableTarget(el) {
        if (!el || !el.tagName) return false;
        const tag = el.tagName.toUpperCase();
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
        return el.isContentEditable === true;
    }

    function emitSelection(inst, shapeId) {
        if (!inst.dotnetRef) return;
        try {
            inst.dotnetRef.invokeMethodAsync('OnShapeSelected', shapeId);
        } catch (e) {
            console.warn('Overlay shape selection emit failed:', e);
        }
    }

    function emitShapeMoved(inst, shapeId, lat, lng) {
        if (!inst.dotnetRef) return;
        try {
            inst.dotnetRef.invokeMethodAsync('OnShapeMoved', shapeId, lat, lng);
        } catch (e) {
            console.warn('Overlay shape move emit failed:', e);
        }
    }

    // ---- Selection highlight overlay --------------------------------------

    function ensureSelectionLayers(map) {
        if (!map.getSource(SRC_SELECTION)) {
            map.addSource(SRC_SELECTION, {
                type: 'geojson',
                data: { type: 'FeatureCollection', features: [] }
            });
        }
        if (!map.getLayer(LYR_SELECTION_FILL)) {
            map.addLayer({
                id: LYR_SELECTION_FILL,
                type: 'fill',
                source: SRC_SELECTION,
                filter: ['in', ['geometry-type'], ['literal', ['Polygon', 'MultiPolygon']]],
                paint: { 'fill-color': '#FFD166', 'fill-opacity': 0.25 }
            });
        }
        if (!map.getLayer(LYR_SELECTION_LINE)) {
            map.addLayer({
                id: LYR_SELECTION_LINE,
                type: 'line',
                source: SRC_SELECTION,
                filter: ['!=', ['geometry-type'], 'Point'],
                paint: {
                    'line-color': '#FFD166',
                    'line-width': 3,
                    'line-dasharray': [2, 1.5]
                }
            });
        }
        if (!map.getLayer(LYR_SELECTION_PT)) {
            map.addLayer({
                id: LYR_SELECTION_PT,
                type: 'circle',
                source: SRC_SELECTION,
                filter: ['==', ['geometry-type'], 'Point'],
                paint: {
                    'circle-radius': 14,
                    'circle-color': 'rgba(255,209,102,0.20)',
                    'circle-stroke-color': '#FFD166',
                    'circle-stroke-width': 2
                }
            });
        }
    }

    function highlightSelection(map, shape) {
        const f = shape ? shapeToFeature(shape) : null;
        setSourceData(map, SRC_SELECTION, f ? [f] : []);
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
        // Preview shapes are transient and intentionally never persisted.
        // Give them an ephemeral id so normalizeShape can render during drag
        // without weakening the saved-shape id contract.
        const previewShape = shape
            ? { ...shape, id: pick(shape, 'id', null) || '__preview-' + (pick(shape, 'type', 'shape') || 'shape') }
            : shape;
        const feature = shapeToFeature(previewShape);
        if (!feature) {
            mapDiag('preview.empty', { type: shape && shape.type });
            teardownPreview(map);
            return;
        }
        setSourceData(map, SRC_PREVIEW, [feature]);
        if (shape && shape.type === 'freehand') {
            mapDiag('preview.update', { type: shape.type, pointCount: shape.points ? shape.points.length : 0 });
        }
    }

    // ---- Style config set by the toolbar ----------------------------------

    let _style = {
        color: '#242F3D',
        fillColor: '#242F3D',
        strokeWidth: 2,
        useFill: false,
        fontSize: 14,
        iconAsset: 'flag',
        iconRotation: 0,
        iconScale: 1.0
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
        // Drop the editMode flag before detachHandlers so its dragPan re-enable
        // path fires unconditionally (the user navigated away — they want a
        // working map, suspension or no suspension).
        inst.editMode = false;
        if (map) {
            // detachHandlers also re-enables dragPan if it was disabled mid-draw.
            detachHandlers(map, inst);
            try { restoreMapInteractions(map, inst); } catch (e) { /* best-effort */ }
            try { teardownPreview(map); } catch (e) { /* map may be gone */ }
            try { clearTextMarkers(inst); } catch (e) { /* map may be gone */ }
            // Remove overlay source + layers so a subsequent remount starts
            // clean — avoids orphaned style-dependent layers when the user
            // navigates away and back.
            try {
                for (const lyr of [
                    LYR_FILLS, LYR_STROKES, LYR_TEXT, LYR_ICONS,
                    LYR_PREVIEW_FILLS, LYR_PREVIEW_STROKES, LYR_PREVIEW_TEXT, LYR_PREVIEW_ICONS,
                    LYR_SELECTION_FILL, LYR_SELECTION_LINE, LYR_SELECTION_PT
                ]) {
                    if (map.getLayer(lyr)) map.removeLayer(lyr);
                }
                for (const src of [SRC_SAVED, SRC_PREVIEW, SRC_SELECTION]) {
                    if (map.getSource(src)) map.removeSource(src);
                }
                // Drop SDF icon images so a remount can re-register them
                // cleanly after a style change wipes the image registry.
                for (const k of ICON_ASSETS) {
                    if (map.hasImage(k)) { try { map.removeImage(k); } catch (e) { } }
                }
                map.__ovcinaOverlayIconsLoaded = null;
            } catch (e) { /* best-effort */ }
            try {
                map.getCanvas().style.cursor = '';
                if (map.doubleClickZoom && map.doubleClickZoom.enable) map.doubleClickZoom.enable();
            } catch (e) { /* best-effort */ }
        }
        // Unlock markers + clear the global edit-mode flag — same belt-and-
        // suspenders as exitEditMode in case the page was disposed mid-edit.
        setLocationMarkersDraggable(true);
        window.__ovcinaOverlayEditMode = false;
        delete instances[mapId];
    }

    // ---- Selection / shape mutation API (called from Blazor) -------------

    function selectShape(mapId, shape) {
        const map = getMap(mapId);
        if (!map) return;
        // `shape` is the deserialized DTO (camelCase JSON). Null clears the
        // highlight without changing tool / handler state.
        highlightSelection(map, shape);
    }

    function clearSelection(mapId) {
        const map = getMap(mapId);
        if (!map) return;
        highlightSelection(map, null);
    }

    function setVisibility(mapId, visible) {
        const inst = ensureInstance(mapId);
        inst.visible = visible !== false;
        const map = getMap(mapId);
        if (map) setSavedOverlayVisibility(map, inst);
    }

    function getIconAssets() {
        return ICON_ASSETS.slice();
    }

    function focusTextPopup() {
        window.requestAnimationFrame(function () {
            const input = document.getElementById('oh-map-text-popup-input')
                || document.querySelector('.oh-map-text-popup-input input');
            if (!input) return;
            input.focus();
            if (typeof input.select === 'function') input.select();
        });
    }

    window.ovcinaOverlay = {
        render: render,
        enterEditMode: enterEditMode,
        switchTool: switchTool,
        exitEditMode: exitEditMode,
        dispose: dispose,
        setStyle: setStyle,
        selectShape: selectShape,
        clearSelection: clearSelection,
        setVisibility: setVisibility,
        getIconAssets: getIconAssets,
        focusTextPopup: focusTextPopup,
        // Exposed for unit-testability / future tools; not called by Blazor.
        _circleToPolygonRing: circleToPolygonRing,
        _normalizeShape: normalizeShape
    };
})();
