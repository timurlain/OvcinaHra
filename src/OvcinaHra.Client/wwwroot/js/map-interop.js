// MapLibre GL JS interop for Blazor
window.ovcinaMap = {
    _map: null,
    _markers: {},
    _dotnetRef: null,
    _elementId: null,
    _apiKey: null,

    _rasterStyle: {
        version: 8,
        sources: {
            'osm': {
                type: 'raster',
                tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                tileSize: 256,
                maxzoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org">OpenStreetMap</a>'
            }
        },
        layers: [{
            id: 'osm-tiles',
            type: 'raster',
            source: 'osm',
            minzoom: 0,
            maxzoom: 19
        }]
    },

    _mapyCzRasterStyle: function (apiKey) {
        return this._makeRasterStyle('outdoor', apiKey);
    },

    init: function (elementId, dotnetRef, centerLat, centerLon, zoom, mapyCzApiKey) {
        this._dotnetRef = dotnetRef;
        this._elementId = elementId;
        this._apiKey = (mapyCzApiKey && mapyCzApiKey.length > 5) ? mapyCzApiKey : null;

        // Start with Mapy.cz raster (tourist), fall back to OSM
        var initialStyle = this._apiKey ? this._mapyCzRasterStyle(this._apiKey) : this._rasterStyle;
        this._map = new maplibregl.Map({
            container: elementId,
            style: initialStyle,
            center: [centerLon, centerLat],
            zoom: zoom
        });

        this._map.on('error', function (e) {
            console.warn('Map tile error:', e.error?.message || e);
        });

        this._map.on('click', (e) => {
            if (this._dotnetRef) {
                this._dotnetRef.invokeMethodAsync('OnMapClicked', e.lngLat.lat, e.lngLat.lng);
            }
        });

        this._map.addControl(new maplibregl.NavigationControl(), 'top-right');
        this._map.addControl(new maplibregl.ScaleControl(), 'bottom-left');
    },

    switchStyle: function (styleKey) {
        if (!this._map) return;

        var center = this._map.getCenter();
        var zoom = this._map.getZoom();
        var style;

        if (styleKey === 'vector' && this._apiKey) {
            style = 'https://api.mapy.cz/v1/maptiles/outdoor/tiles.json?apikey=' + this._apiKey;
        } else if (styleKey === 'tourist' && this._apiKey) {
            style = this._mapyCzRasterStyle(this._apiKey);
        } else if (styleKey === 'aerial' && this._apiKey) {
            style = this._makeRasterStyle('aerial', this._apiKey);
        } else if (styleKey === 'basic' && this._apiKey) {
            style = this._makeRasterStyle('basic', this._apiKey);
        } else {
            style = this._rasterStyle; // OSM fallback
        }

        // Clear markers before style change
        this.clearMarkers();

        this._map.setStyle(style);
        this._map.once('styledata', () => {
            this._map.setCenter(center);
            this._map.setZoom(zoom);
            // Blazor will re-add markers via LoadAndDisplayLocationsAsync
            if (this._dotnetRef) {
                this._dotnetRef.invokeMethodAsync('OnStyleLoadedCallback');
            }
        });
    },

    setCenter: function (lat, lon, zoom) {
        if (this._map) {
            this._map.flyTo({ center: [lon, lat], zoom: zoom || this._map.getZoom() });
        }
    },

    fitBounds: function (swLat, swLon, neLat, neLon) {
        if (this._map) {
            this._map.fitBounds([[swLon, swLat], [neLon, neLat]], { padding: 40 });
        }
    },

    addMarker: function (id, lat, lon, name, kind, color) {
        if (!this._map) return;
        this.removeMarker(id);

        var wrapper = document.createElement('div');
        wrapper.className = 'ovcina-marker-wrapper';
        wrapper.style.display = 'flex';
        wrapper.style.flexDirection = 'column';
        wrapper.style.alignItems = 'center';
        wrapper.style.cursor = 'pointer';

        var pin = document.createElement('div');
        pin.className = 'ovcina-marker';
        pin.style.backgroundColor = color || '#e74c3c';
        pin.style.width = '14px';
        pin.style.height = '14px';
        pin.style.borderRadius = '50%';
        pin.style.border = '2px solid white';
        pin.style.boxShadow = '0 1px 4px rgba(0,0,0,0.4)';
        wrapper.appendChild(pin);

        var label = document.createElement('div');
        label.className = 'ovcina-marker-label';
        label.textContent = name;
        label.style.fontSize = '11px';
        label.style.fontWeight = '600';
        label.style.color = '#1a1a2e';
        label.style.textShadow = '0 0 3px white, 0 0 3px white, 0 0 3px white';
        label.style.whiteSpace = 'nowrap';
        label.style.marginTop = '2px';
        label.style.pointerEvents = 'none';
        wrapper.appendChild(label);

        var popup = new maplibregl.Popup({ offset: 10 })
            .setHTML('<strong>' + this._escapeHtml(name) + '</strong><br><small>' + this._escapeHtml(kind) + '</small>');

        var marker = new maplibregl.Marker({ element: wrapper, anchor: 'top', draggable: true })
            .setLngLat([lon, lat])
            .setPopup(popup)
            .addTo(this._map);

        wrapper.addEventListener('click', (e) => {
            e.stopPropagation();
            if (this._dotnetRef) {
                this._dotnetRef.invokeMethodAsync('OnMarkerClicked', id);
            }
        });

        marker.on('dragend', () => {
            var newPos = marker.getLngLat();
            if (this._dotnetRef) {
                this._dotnetRef.invokeMethodAsync('OnMarkerDragged', id, newPos.lat, newPos.lng, lat, lon);
            }
        });

        this._markers[id] = marker;
    },

    removeMarker: function (id) {
        if (this._markers[id]) {
            this._markers[id].remove();
            delete this._markers[id];
        }
    },

    clearMarkers: function () {
        for (var id in this._markers) {
            this._markers[id].remove();
        }
        this._markers = {};
    },

    updateMarkerPosition: function (id, lat, lon) {
        if (this._markers[id]) {
            this._markers[id].setLngLat([lon, lat]);
        }
    },

    dispose: function () {
        this.clearMarkers();
        if (this._map) {
            this._map.remove();
            this._map = null;
        }
        this._dotnetRef = null;
    },

    _makeRasterStyle: function (styleName, apiKey) {
        return {
            version: 8,
            sources: {
                'mapy-cz': {
                    type: 'raster',
                    tiles: ['https://api.mapy.cz/v1/maptiles/' + styleName + '/256/{z}/{x}/{y}?apikey=' + apiKey],
                    tileSize: 256,
                    maxzoom: 19,
                    attribution: '&copy; <a href="https://www.mapy.cz">Mapy.cz</a>'
                }
            },
            layers: [{
                id: 'mapy-cz-tiles',
                type: 'raster',
                source: 'mapy-cz',
                minzoom: 0,
                maxzoom: 19
            }]
        };
    },

    _escapeHtml: function (text) {
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(text || ''));
        return div.innerHTML;
    }
};

// ----------------------------------------------------------------------
// Mini-map helper: single-pin read-only mini maps, keyed by element id.
// Used by LocationDetail › Upravit to preview a location's GPS position
// alongside the editable coords. Multiple instances can coexist on the
// page without conflicting with the main ovcinaMap used on /map.
// ----------------------------------------------------------------------
window.ovcinaMiniMap = {
    _instances: {},

    // styleKey:
    //   'aerial'  → Mapy.cz aerial (photo) — default for the LocationDetail orientation map (issue #74)
    //   'outdoor' → Mapy.cz tourist raster (previous default)
    //   'basic'   → Mapy.cz basic raster
    //   anything else (or no apiKey) → OSM raster fallback
    _styleFor: function (styleKey, apiKey) {
        if (apiKey && apiKey.length > 5 && (styleKey === 'aerial' || styleKey === 'outdoor' || styleKey === 'basic')) {
            return {
                version: 8,
                sources: {
                    'mapy-cz': {
                        type: 'raster',
                        tiles: ['https://api.mapy.cz/v1/maptiles/' + styleKey + '/256/{z}/{x}/{y}?apikey=' + apiKey],
                        tileSize: 256,
                        maxzoom: 19,
                        attribution: '&copy; Mapy.cz'
                    }
                },
                layers: [{ id: 'mapy-cz-tiles', type: 'raster', source: 'mapy-cz', minzoom: 0, maxzoom: 19 }]
            };
        }
        return {
            version: 8,
            sources: {
                'osm': {
                    type: 'raster',
                    tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                    tileSize: 256,
                    maxzoom: 19,
                    attribution: '&copy; OpenStreetMap'
                }
            },
            layers: [{ id: 'osm-tiles', type: 'raster', source: 'osm', minzoom: 0, maxzoom: 19 }]
        };
    },

    // styleKey is optional — defaults to 'aerial' (photo map) for the LocationDetail
    // orientation map (issue #74). Bumped default zoom from 12 to 16 so the user lands
    // on a town-scale view, not a continent-scale one.
    init: function (elementId, lat, lon, zoom, kindColor, mapyCzApiKey, styleKey) {
        var el = document.getElementById(elementId);
        if (!el || typeof maplibregl === 'undefined') return;
        var existing = this._instances[elementId];
        if (existing) {
            this.update(elementId, lat, lon);
            return;
        }

        var map = new maplibregl.Map({
            container: el,
            style: this._styleFor(styleKey || 'aerial', mapyCzApiKey),
            center: [lon, lat],
            zoom: zoom || 16,
            interactive: false,
            attributionControl: false
        });

        // Issue #110: the focused (current) location stands out vs. surrounding
        // context labels via a halo ring + larger pin. Two-layer element:
        // outer translucent ring (kindColor, 30px), inner solid pin (22px).
        var wrap = document.createElement('div');
        wrap.style.cssText = 'position:relative;width:30px;height:30px;display:flex;align-items:center;justify-content:center;';
        var halo = document.createElement('div');
        halo.style.cssText = 'position:absolute;inset:0;border-radius:50%;background:' +
            (kindColor || '#2D5016') + ';opacity:.22;';
        var pin = document.createElement('div');
        pin.style.cssText = 'width:22px;height:22px;border-radius:50%;background:' +
            (kindColor || '#2D5016') + ';border:2.5px solid #fff;box-shadow:0 2px 6px rgba(0,0,0,.55);position:relative;';
        wrap.appendChild(halo);
        wrap.appendChild(pin);
        var marker = new maplibregl.Marker({ element: wrap }).setLngLat([lon, lat]).addTo(map);

        this._instances[elementId] = { map: map, marker: marker, contextMarkers: [] };
    },

    update: function (elementId, lat, lon) {
        var inst = this._instances[elementId];
        if (!inst) return;
        inst.marker.setLngLat([lon, lat]);
        inst.map.jumpTo({ center: [lon, lat] });
    },

    // Paint a set of small dots around the current location for orientation (issue #74).
    // Each item: { lat, lon, name, color, showLabel }.
    // Issue #110: each dot now optionally carries a name label next to it
    // (white halo-shadow for readability on any tile style). Label text is
    // Czech-safe (no normalization) — diacritics render natively.
    // Replaces the previous context markers (so caller can rebuild on game switch).
    setContextMarkers: function (elementId, markers) {
        var inst = this._instances[elementId];
        if (!inst) return;
        // Clear previous context markers
        if (inst.contextMarkers) {
            for (var i = 0; i < inst.contextMarkers.length; i++) {
                try { inst.contextMarkers[i].remove(); } catch (e) { /* ignore */ }
            }
        }
        inst.contextMarkers = [];
        if (!markers || !markers.length) return;
        for (var j = 0; j < markers.length; j++) {
            var m = markers[j];
            if (m.lat == null || m.lon == null) continue;

            var wrap = document.createElement('div');
            wrap.style.cssText = 'display:flex;align-items:center;gap:3px;pointer-events:none;';
            wrap.title = m.name || '';

            var dot = document.createElement('div');
            dot.style.cssText = 'width:10px;height:10px;border-radius:50%;background:' +
                (m.color || '#666') +
                ';border:1.5px solid rgba(255,255,255,.85);box-shadow:0 1px 2px rgba(0,0,0,.4);opacity:.85;flex:0 0 auto;';
            wrap.appendChild(dot);

            if (m.name && m.showLabel !== false) {
                var label = document.createElement('span');
                label.textContent = m.name;
                label.style.cssText = 'font:600 11px -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;' +
                    'color:#2a2a2a;white-space:nowrap;' +
                    'text-shadow:0 0 2px #fff,0 0 2px #fff,0 0 2px #fff,0 0 2px #fff;' +
                    'letter-spacing:.01em;';
                wrap.appendChild(label);
            }

            // anchor:'left' + offset:[-5,0] centers the 10px dot on the
            // coordinate (dot is first child; offset pulls the wrap 5px left
            // so the dot's centre, not its left edge, sits on the lat/lng).
            // Label still flows to the right of the dot via the flex layout.
            var ctx = new maplibregl.Marker({ element: wrap, anchor: 'left', offset: [-5, 0] })
                .setLngLat([m.lon, m.lat]).addTo(inst.map);
            inst.contextMarkers.push(ctx);
        }
    },

    dispose: function (elementId) {
        var inst = this._instances[elementId];
        if (!inst) return;
        if (inst.contextMarkers) {
            for (var i = 0; i < inst.contextMarkers.length; i++) {
                try { inst.contextMarkers[i].remove(); } catch (e) { /* ignore */ }
            }
        }
        try { inst.map.remove(); } catch (e) { /* ignore */ }
        delete this._instances[elementId];
    }
};

// ----------------------------------------------------------------------
// Pie-wedge markers for /treasures planning.
// Fixed-quadrant layout — Start=NE, Early=SE, Mid=SW, Late=NW — with
// per-stage wedge radius scaled by count / maxCount. Unfilled portion
// shows a muted parchment disc beneath the coloured wedges. Pie markers
// are tracked separately from ovcinaMap._markers so the /map and
// /treasures pages never collide.
// ----------------------------------------------------------------------
window.ovcinaMap._pieMarkers = {};
window.ovcinaMap._zeroMarkers = {};
window.ovcinaMap._activePieStages = null; // Set<string> | null (null = all active)

window.ovcinaMap._buildPieSvg = function (counts, maxCount) {
    var c0 = counts[0] | 0, c1 = counts[1] | 0, c2 = counts[2] | 0, c3 = counts[3] | 0;
    var total = c0 + c1 + c2 + c3;
    var cap = Math.max(1, maxCount || total);
    var maxR = 28;
    var stages = ['start', 'early', 'mid', 'late'];
    // Each wedge occupies a fixed 90° quadrant. Path uses a small arc.
    // Paths below are parameterised by `r` (scaled radius per stage).
    var paths = [
        function (r) { return 'M0,0 L0,' + (-r) + ' A' + r + ',' + r + ' 0 0 1 ' + r + ',0 Z'; }, // NE / Start
        function (r) { return 'M0,0 L' + r + ',0 A' + r + ',' + r + ' 0 0 1 0,' + r + ' Z'; }, // SE / Early
        function (r) { return 'M0,0 L0,' + r + ' A' + r + ',' + r + ' 0 0 1 ' + (-r) + ',0 Z'; }, // SW / Mid
        function (r) { return 'M0,0 L' + (-r) + ',0 A' + r + ',' + r + ' 0 0 1 0,' + (-r) + ' Z'; } // NW / Late
    ];
    var svg = '<svg width="64" height="64" viewBox="-32 -32 64 64" class="oh-tp-pin-svg">';
    // Parchment backing disc (shows through in unfilled portions).
    svg += '<circle class="oh-tp-pin-backing" r="' + maxR + '" cx="0" cy="0" />';
    // Coloured wedges at scaled radius (skip zero-count stages).
    var cs = [c0, c1, c2, c3];
    for (var i = 0; i < 4; i++) {
        if (cs[i] <= 0) continue;
        var r = Math.max(6, maxR * Math.min(1, cs[i] / cap));
        svg += '<path class="oh-tp-wedge oh-tp-wedge-' + stages[i] + '" data-stage="' + stages[i] + '" d="' + paths[i](r.toFixed(2)) + '"/>';
    }
    // Outer ring + inner numeral disc.
    svg += '<circle class="oh-tp-pin-ring" r="' + (maxR + 1) + '" cx="0" cy="0" />';
    svg += '<circle class="oh-tp-pin-inner" r="10" cx="0" cy="0" />';
    svg += '<text class="oh-tp-pin-num" x="0" y="4" text-anchor="middle">' + total + '</text>';
    svg += '</svg>';
    return svg;
};

window.ovcinaMap._wireDropTarget = function (element, locationId) {
    var self = this;
    element.addEventListener('dragover', function (e) {
        e.preventDefault();
        if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
        element.classList.add('oh-tp-pin-dragtarget');
    });
    element.addEventListener('dragleave', function () {
        element.classList.remove('oh-tp-pin-dragtarget');
    });
    element.addEventListener('drop', function (e) {
        e.preventDefault();
        element.classList.remove('oh-tp-pin-dragtarget');
        var payload = e.dataTransfer && e.dataTransfer.getData('application/x-oh-pool-item');
        if (self._dotnetRef && payload) {
            self._dotnetRef.invokeMethodAsync('OnPieMarkerDropped', locationId, payload);
        }
    });
};

window.ovcinaMap.addPieMarker = function (id, lat, lon, counts, maxCount) {
    if (!this._map) return;
    this.removePieMarker(id);
    var wrap = document.createElement('div');
    wrap.className = 'oh-tp-pin oh-tp-pin-pie';
    wrap.setAttribute('data-pie-id', id);
    wrap.innerHTML = this._buildPieSvg(counts, maxCount);

    var self = this;
    wrap.addEventListener('click', function (e) {
        e.stopPropagation();
        if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnPieMarkerClicked', id);
    });
    this._wireDropTarget(wrap, id);

    var marker = new maplibregl.Marker({ element: wrap, anchor: 'center' })
        .setLngLat([lon, lat])
        .addTo(this._map);

    this._pieMarkers[id] = { marker: marker, element: wrap, counts: counts.slice() };
    this._applyStageFilterToElement(wrap);
};

window.ovcinaMap.updatePieMarkerCounts = function (id, counts, maxCount) {
    var rec = this._pieMarkers[id];
    if (!rec) return;
    rec.counts = counts.slice();
    rec.element.innerHTML = this._buildPieSvg(counts, maxCount);
    this._applyStageFilterToElement(rec.element);
};

window.ovcinaMap.removePieMarker = function (id) {
    var rec = this._pieMarkers[id];
    if (rec) {
        try { rec.marker.remove(); } catch (e) { /* ignore */ }
        delete this._pieMarkers[id];
    }
};

window.ovcinaMap.addZeroMarker = function (id, lat, lon, name, kind) {
    if (!this._map) return;
    this.removeZeroMarker(id);
    var dot = document.createElement('div');
    dot.className = 'oh-tp-pin oh-tp-pin-zero';
    dot.setAttribute('data-kind', (kind || 'wilderness').toLowerCase());
    dot.title = name || '';
    var self = this;
    dot.addEventListener('click', function (e) {
        e.stopPropagation();
        if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnPieMarkerClicked', id);
    });
    this._wireDropTarget(dot, id);
    var marker = new maplibregl.Marker({ element: dot, anchor: 'center' })
        .setLngLat([lon, lat])
        .addTo(this._map);
    this._zeroMarkers[id] = { marker: marker, element: dot };
};

window.ovcinaMap.removeZeroMarker = function (id) {
    var rec = this._zeroMarkers[id];
    if (rec) {
        try { rec.marker.remove(); } catch (e) { /* ignore */ }
        delete this._zeroMarkers[id];
    }
};

window.ovcinaMap.clearPieMarkers = function () {
    for (var id in this._pieMarkers) {
        try { this._pieMarkers[id].marker.remove(); } catch (e) { /* ignore */ }
    }
    this._pieMarkers = {};
    for (var zid in this._zeroMarkers) {
        try { this._zeroMarkers[zid].marker.remove(); } catch (e) { /* ignore */ }
    }
    this._zeroMarkers = {};
    this._activePieStages = null;
};

// Wrap switchStyle + dispose so style changes and teardown also wipe pie/zero
// markers (the original methods only touched ._markers). Prevents DOM leaks
// when the /treasures page switches map style or the user navigates away.
(function () {
    var originalSwitchStyle = window.ovcinaMap.switchStyle;
    if (typeof originalSwitchStyle === 'function') {
        window.ovcinaMap.switchStyle = function () {
            try { this.clearPieMarkers(); } catch (e) { /* ignore */ }
            return originalSwitchStyle.apply(this, arguments);
        };
    }
    var originalDispose = window.ovcinaMap.dispose;
    if (typeof originalDispose === 'function') {
        window.ovcinaMap.dispose = function () {
            try { this.clearPieMarkers(); } catch (e) { /* ignore */ }
            return originalDispose.apply(this, arguments);
        };
    }
})();

window.ovcinaMap._applyStageFilterToElement = function (el) {
    var active = this._activePieStages;
    var wedges = el.querySelectorAll('[data-stage]');
    for (var i = 0; i < wedges.length; i++) {
        var s = wedges[i].getAttribute('data-stage');
        wedges[i].style.opacity = (!active || active.has(s)) ? '1' : '0.15';
    }
};

window.ovcinaMap.setStageFilter = function (stages) {
    this._activePieStages = (stages && stages.length > 0) ? new Set(stages) : null;
    for (var id in this._pieMarkers) {
        this._applyStageFilterToElement(this._pieMarkers[id].element);
    }
};

// ----------------------------------------------------------------------
// Tiny HTML5-DnD helper — lets Blazor pool tiles set a payload without
// round-tripping DataTransfer through C#. Registers a native dragstart
// on the element; on drop, the pie-marker side reads
// 'application/x-oh-pool-item'. setDraggable is idempotent.
// ----------------------------------------------------------------------
window.ovcinaDnd = {
    _wired: new WeakMap(),

    setDraggable: function (element, payload) {
        if (!element) return;
        if (this._wired.has(element)) {
            this._wired.set(element, payload);
            return;
        }
        this._wired.set(element, payload);
        element.setAttribute('draggable', 'true');
        var self = this;
        element.addEventListener('dragstart', function (e) {
            var p = self._wired.get(element);
            if (!p) return;
            if (e.dataTransfer) {
                e.dataTransfer.setData('application/x-oh-pool-item', p);
                e.dataTransfer.effectAllowed = 'move';
            }
            element.classList.add('oh-tp-pool-tile-dragging');
        });
        element.addEventListener('dragend', function () {
            element.classList.remove('oh-tp-pool-tile-dragging');
        });
    },

    clear: function (element) {
        if (!element) return;
        this._wired.delete(element);
        element.removeAttribute('draggable');
    },

    // Generic DOM helper used by pages that want a smooth scroll to a known
    // anchor id without a full JS interop round-trip per handler.
    scrollIntoViewById: function (elementId) {
        var el = document.getElementById(elementId);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
};

// ----------------------------------------------------------------------
// Bounding-box picker map: interactive editor for a game's world bbox.
// Multi-instance (keyed by elementId) so the picker component can coexist
// with the main /map view or live in a popup without colliding.
// Source of truth is the Blazor form model — this helper exposes getBounds
// (viewport → [sw, ne]) and drawBboxRectangle (rectangle overlay) so the
// parent component can sync the two. (issue #2)
// ----------------------------------------------------------------------
window.ovcinaBboxMap = {
    _instances: {},
    _sourceId: 'oh-bbox-overlay',
    _fillLayerId: 'oh-bbox-overlay-fill',
    _lineLayerId: 'oh-bbox-overlay-line',

    _styleFor: function (apiKey) {
        if (apiKey && apiKey.length > 5) {
            return {
                version: 8,
                sources: {
                    'mapy-cz': {
                        type: 'raster',
                        tiles: ['https://api.mapy.cz/v1/maptiles/outdoor/256/{z}/{x}/{y}?apikey=' + apiKey],
                        tileSize: 256,
                        maxzoom: 19,
                        attribution: '&copy; <a href="https://www.mapy.cz">Mapy.cz</a>'
                    }
                },
                layers: [{ id: 'mapy-cz-tiles', type: 'raster', source: 'mapy-cz', minzoom: 0, maxzoom: 19 }]
            };
        }
        return {
            version: 8,
            sources: {
                'osm': {
                    type: 'raster',
                    tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                    tileSize: 256,
                    maxzoom: 19,
                    attribution: '&copy; OpenStreetMap'
                }
            },
            layers: [{ id: 'osm-tiles', type: 'raster', source: 'osm', minzoom: 0, maxzoom: 19 }]
        };
    },

    _rectangleFeature: function (swLat, swLng, neLat, neLng) {
        // GeoJSON expects [lng, lat]. Rectangle polygon: SW → SE → NE → NW → SW.
        return {
            type: 'Feature',
            geometry: {
                type: 'Polygon',
                coordinates: [[
                    [swLng, swLat],
                    [neLng, swLat],
                    [neLng, neLat],
                    [swLng, neLat],
                    [swLng, swLat]
                ]]
            },
            properties: {}
        };
    },

    _addOrUpdateOverlay: function (map, swLat, swLng, neLat, neLng) {
        var self = window.ovcinaBboxMap;
        var feature = self._rectangleFeature(swLat, swLng, neLat, neLng);
        var src = map.getSource(self._sourceId);
        if (src) {
            src.setData(feature);
            return;
        }
        map.addSource(self._sourceId, { type: 'geojson', data: feature });
        map.addLayer({
            id: self._fillLayerId,
            type: 'fill',
            source: self._sourceId,
            paint: { 'fill-color': '#4a7c34', 'fill-opacity': 0.18 }
        });
        map.addLayer({
            id: self._lineLayerId,
            type: 'line',
            source: self._sourceId,
            paint: { 'line-color': '#2d5016', 'line-width': 2, 'line-dasharray': [2, 2] }
        });
    },

    _removeOverlay: function (map) {
        var self = window.ovcinaBboxMap;
        if (map.getLayer(self._lineLayerId)) map.removeLayer(self._lineLayerId);
        if (map.getLayer(self._fillLayerId)) map.removeLayer(self._fillLayerId);
        if (map.getSource(self._sourceId)) map.removeSource(self._sourceId);
    },

    init: function (elementId, apiKey, centerLat, centerLon, zoom) {
        var el = document.getElementById(elementId);
        if (!el || typeof maplibregl === 'undefined') return;
        var existing = this._instances[elementId];
        if (existing) { return; }
        var map = new maplibregl.Map({
            container: el,
            style: this._styleFor(apiKey),
            center: [centerLon, centerLat],
            zoom: zoom || 7
        });
        map.addControl(new maplibregl.NavigationControl(), 'top-right');
        map.addControl(new maplibregl.ScaleControl(), 'bottom-left');
        map.on('error', function (e) { console.warn('Bbox map tile error:', e.error && e.error.message || e); });
        this._instances[elementId] = { map: map, hasOverlay: false };
    },

    getBounds: function (elementId) {
        var inst = this._instances[elementId];
        if (!inst) return null;
        var b = inst.map.getBounds();
        return {
            swLat: b.getSouth(),
            swLng: b.getWest(),
            neLat: b.getNorth(),
            neLng: b.getEast()
        };
    },

    drawBboxRectangle: function (elementId, swLat, swLng, neLat, neLng) {
        var inst = this._instances[elementId];
        if (!inst) return;
        var map = inst.map;
        var apply = function () { window.ovcinaBboxMap._addOrUpdateOverlay(map, swLat, swLng, neLat, neLng); };
        if (map.isStyleLoaded()) apply(); else map.once('styledata', apply);
        inst.hasOverlay = true;
    },

    clearBboxRectangle: function (elementId) {
        var inst = this._instances[elementId];
        if (!inst) return;
        window.ovcinaBboxMap._removeOverlay(inst.map);
        inst.hasOverlay = false;
    },

    fitToBounds: function (elementId, swLat, swLng, neLat, neLng, paddingPx) {
        var inst = this._instances[elementId];
        if (!inst) return;
        var padding = (paddingPx && paddingPx > 0) ? paddingPx : 40;
        inst.map.fitBounds([[swLng, swLat], [neLng, neLat]], { padding: padding });
    },

    dispose: function (elementId) {
        var inst = this._instances[elementId];
        if (!inst) return;
        try { inst.map.remove(); } catch (e) { /* ignore */ }
        delete this._instances[elementId];
    }
};
