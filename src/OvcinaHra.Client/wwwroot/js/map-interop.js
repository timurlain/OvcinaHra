// MapLibre GL JS interop for Blazor
window.ovcinaMap = {
    _map: null,
    _markers: {},
    _dotnetRef: null,
    _elementId: null,
    _apiKey: null,
    _moveShortcutActive: false,

    // Glyph URL — required by MapLibre for any layer with `text-field` in
    // its layout (e.g. the overlay editor's `oh-overlay-preview-text` text
    // primitive, #96). Without `glyphs`, MapLibre logs:
    //   "layers.X.layout.text-field: use of 'text-field' requires a style 'glyphs' property"
    // and the text never renders. The actual URL comes from
    // `MapLibre:GlyphsUrl` in appsettings (prod points at our Azure Blob,
    // dev at demotiles). The constant below is the safety fallback if the
    // setting is missing — never relied on by prod (#166).
    _defaultGlyphsUrl: 'https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf',
    _glyphsUrl: 'https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf',

    _buildRasterStyle: function () {
        return {
            version: 8,
            glyphs: this._glyphsUrl,
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
        };
    },

    _mapyCzRasterStyle: function (apiKey) {
        return this._makeRasterStyle('outdoor', apiKey);
    },

    // Diagnostic logging gate — opt in via the browser console with
    // `localStorage.setItem('oh-map-pin-diag', '1');` then reload. Default
    // OFF so production users don't see lat/lon flooding the console.
    _pinDiag: function () {
        try { return typeof localStorage !== 'undefined' && localStorage.getItem('oh-map-pin-diag') === '1'; }
        catch (e) { return false; }
    },

    _mapDiag: function (event, data) {
        try {
            console.log('[map-diag] map.' + event + ' ' + JSON.stringify(data || {}));
        } catch (e) { }
    },

    _eventTargetName: function (target) {
        try {
            if (!target) return null;
            var tag = target.tagName ? String(target.tagName).toLowerCase() : 'unknown';
            var cls = typeof target.className === 'string'
                ? target.className
                : (target.className && target.className.baseVal ? target.className.baseVal : '');
            var suffix = cls ? '.' + String(cls).split(/\s+/).filter(Boolean).slice(0, 3).join('.') : '';
            return tag + suffix;
        } catch (e) {
            return null;
        }
    },

    _clickDiagPayload: function (e, target, lngLat, extra) {
        var orig = e && (e.originalEvent || e);
        var payload = {
            ctrl: !!(orig && (orig.ctrlKey || orig.metaKey)),
            shift: !!(orig && orig.shiftKey),
            alt: !!(orig && orig.altKey),
            meta: !!(orig && orig.metaKey),
            target: target || this._eventTargetName(orig && orig.target),
            lngLat: lngLat ? { lat: lngLat.lat, lng: lngLat.lng } : null
        };
        return Object.assign(payload, extra || {});
    },

    init: function (elementId, dotnetRef, centerLat, centerLon, zoom, mapyCzApiKey, glyphsUrl) {
        if (this._pinDiag()) {
            console.log('[pin-diag] init() — elementId=' + elementId + ', existingMap=' + (this._map ? 'YES' : 'no') + ', center=[' + centerLon + ',' + centerLat + '], zoom=' + zoom);
        }
        this._dotnetRef = dotnetRef;
        this._elementId = elementId;
        this._apiKey = (mapyCzApiKey && mapyCzApiKey.length > 5) ? mapyCzApiKey : null;
        this._moveShortcutActive = false;
        // Resolved at init so every style construction below picks up the
        // environment-specific URL (#166). Empty/missing/whitespace-only →
        // fall back to the demotiles dev URL so local smoke testing still
        // works AND a stray space in appsettings.json doesn't ship a broken
        // URL to MapLibre.
        var trimmedGlyphsUrl = (typeof glyphsUrl === 'string') ? glyphsUrl.trim() : '';
        this._glyphsUrl = trimmedGlyphsUrl.length > 0 ? trimmedGlyphsUrl : this._defaultGlyphsUrl;

        // Start with Mapy.cz raster (tourist), fall back to OSM
        var initialStyle = this._apiKey ? this._mapyCzRasterStyle(this._apiKey) : this._buildRasterStyle();
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
            var orig = e.originalEvent;
            var ctrl = !!(orig && (orig.ctrlKey || orig.metaKey));
            var shift = !!(orig && orig.shiftKey);
            this._mapDiag('click.enter', this._clickDiagPayload(e, 'map-canvas', e.lngLat, {
                elementId: this._elementId,
                hasDotNetRef: !!this._dotnetRef
            }));
            var features = [];
            try { features = this._map.queryRenderedFeatures(e.point) || []; }
            catch (err) { features = []; }
            this._mapDiag('click.hit-test', {
                path: features.length > 0 ? 'map-feature' : 'map-background',
                featureCount: features.length,
                layerIds: features.slice(0, 5).map(function (f) { return f && f.layer && f.layer.id; }).filter(Boolean)
            });
            this._mapDiag('click.modifier_branch', {
                branch: ctrl && shift ? 'ctrl_shift_relocate' : (ctrl ? 'default' : 'none'),
                ctrl: ctrl,
                shift: shift,
                target: 'map-canvas'
            });
            this._mapDiag('click', {
                elementId: this._elementId,
                lat: e.lngLat.lat,
                lng: e.lngLat.lng,
                ctrl: ctrl,
                shift: shift,
                hasDotNetRef: !!this._dotnetRef
            });
            if (this._dotnetRef) {
                // Forward Ctrl/Meta + Shift state — /map uses
                //   Ctrl+Click       → place ungeocoded location
                //   Ctrl+Shift+Click → move existing location (any)
                this._mapDiag('click.invoke-dotnet', { ctrl: ctrl, shift: shift });
                this._dotnetRef.invokeMethodAsync('OnMapClicked', e.lngLat.lat, e.lngLat.lng, ctrl, shift);
            }
        });

        // Issue #273 — per-LocationKind label visibility. Each pin advertises
        // its kind via `data-kind` and a numeric `data-minzoom`, set in
        // addLocationPin from KIND_MIN_ZOOM. This handler iterates the
        // currently painted pins and toggles `oh-pin-label-on` based on the
        // current zoom — so Towns appear from zoom 0, Villages from 6,
        // smaller features only at 9-11. Replaces the binary
        // `oh-map-zoomed-in` toggle (#258) with a graduated reveal.
        //
        // Stash pins reuse the same gate keyed off their host location's
        // kind so a Stash inside Esgaroth shows its label as soon as
        // Esgaroth's would. Stash kind is captured via `data-kind` in
        // addStashPin (defaults to "village" when host kind isn't known).
        var applyZoomLabels = () => {
            if (!this._map) return;
            var z = this._map.getZoom();
            var indicatorEl = document.querySelector('.oh-map-zoom-indicator');
            if (indicatorEl) indicatorEl.textContent = 'Z ' + z.toFixed(1);
            var apply = (pin) => {
                var mz = parseFloat(pin.getAttribute('data-minzoom'));
                if (isNaN(mz)) return;
                if (z >= mz) pin.classList.add('oh-pin-label-on');
                else pin.classList.remove('oh-pin-label-on');
            };
            for (var lid in this._mapPagePins.loc) {
                var lel = this._mapPagePins.loc[lid].getElement();
                if (lel) {
                    var lpin = lel.querySelector('.oh-map-pin');
                    if (lpin) apply(lpin);
                }
            }
            for (var sid in this._mapPagePins.stash) {
                var sel = this._mapPagePins.stash[sid].getElement();
                if (sel) {
                    var spin = sel.querySelector('.oh-map-pin');
                    if (spin) apply(spin);
                }
            }
        };
        this._applyZoomLabels = applyZoomLabels;
        this._map.on('zoomend', applyZoomLabels);
        this._map.on('zoom', applyZoomLabels); // continuous update for the indicator

        // Crosshair cursor while Ctrl/Meta is held — visual hint that
        // Ctrl+Click will place an ungeocoded location. Pure CSS via a
        // class toggle so MapLibre's own cursor logic still works the
        // rest of the time. Listeners cleared in dispose().
        var mapContainer = this._map.getContainer();
        var keyDown = function (e) {
            if (e.key === 'Escape' && window.ovcinaMap._moveShortcutActive) {
                e.preventDefault();
                window.ovcinaMap._mapDiag('location.relocate.cancel', { source: 'escape' });
                if (window.ovcinaMap._dotnetRef) {
                    window.ovcinaMap._dotnetRef.invokeMethodAsync('OnLocationMoveShortcutCanceled');
                }
                return;
            }
            if (e.key === 'Control' || e.key === 'Meta') {
                mapContainer.classList.add('oh-map-ctrl-held');
                window.ovcinaMap._mapDiag('modifier.down', { key: e.key, ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey });
            } else if (e.key === 'Shift') {
                window.ovcinaMap._mapDiag('modifier.down', { key: e.key, ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey });
            }
        };
        var keyUp = function (e) {
            if (e.key === 'Control' || e.key === 'Meta') {
                mapContainer.classList.remove('oh-map-ctrl-held');
                window.ovcinaMap._mapDiag('modifier.up', { key: e.key, ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey });
            } else if (e.key === 'Shift') {
                window.ovcinaMap._mapDiag('modifier.up', { key: e.key, ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey });
            }
        };
        var blur = function () {
            mapContainer.classList.remove('oh-map-ctrl-held');
            window.ovcinaMap._mapDiag('modifier.blur', {});
        };
        document.addEventListener('keydown', keyDown);
        document.addEventListener('keyup', keyUp);
        window.addEventListener('blur', blur);
        this._ctrlListeners = { keyDown: keyDown, keyUp: keyUp, blur: blur };

        // Fire OnStyleLoadedCallback once the initial basemap style is fully
        // loaded so MapPage / TreasurePlanning can paint pins. Without this,
        // the callback only fires after a style switch — Map page never gets
        // its first paint trigger and ends up with 0 lokací (regression from
        // #212 which only wired the switchStyle path).
        this._map.once('load', () => {
            if (this._pinDiag()) {
                console.log('[pin-diag] map load fired — bounds=' + JSON.stringify(this._map.getBounds()));
            }
            if (this._dotnetRef) {
                this._dotnetRef.invokeMethodAsync('OnStyleLoadedCallback');
            }
        });

        // Sample marker positions on every zoomend so we can correlate
        // pin-screen-position with map-zoom-level. Gated to avoid console
        // spam in normal use — flip the localStorage flag to enable.
        this._map.on('zoomend', () => {
            if (!this._pinDiag()) return;
            var ids = Object.keys(this._mapPagePins.loc);
            if (ids.length === 0) return;
            var sample = ids.slice(0, 3).map((id) => {
                var m = this._mapPagePins.loc[id];
                var ll = m.getLngLat();
                return id + '@[' + ll.lng.toFixed(6) + ',' + ll.lat.toFixed(6) + ']';
            }).join(', ');
            console.log('[pin-diag] zoomend zoom=' + this._map.getZoom().toFixed(2) + ' pinCount=' + ids.length + ' sample=' + sample);
        });

        this._map.addControl(new maplibregl.NavigationControl(), 'top-right');
        this._map.addControl(new maplibregl.ScaleControl(), 'bottom-left');

        // Issue #273 — zoom-level indicator next to the Scale control. Bare-
        // bones IControl: a styled DIV that the zoom listener above updates
        // via .textContent. Position 'bottom-left' stacks it under the scale
        // bar in the same MapLibre control container so dimensions stay
        // consistent across themes.
        var ZoomIndicatorControl = function () { };
        ZoomIndicatorControl.prototype.onAdd = function (map) {
            var c = document.createElement('div');
            c.className = 'maplibregl-ctrl maplibregl-ctrl-group oh-map-zoom-indicator';
            c.title = 'Zoom (úroveň přiblížení)';
            c.textContent = 'Z ' + map.getZoom().toFixed(1);
            this._container = c;
            this._map = map;
            return c;
        };
        ZoomIndicatorControl.prototype.onRemove = function () {
            if (this._container && this._container.parentNode) {
                this._container.parentNode.removeChild(this._container);
            }
            this._map = undefined;
        };
        this._map.addControl(new ZoomIndicatorControl(), 'bottom-left');
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
            style = this._buildRasterStyle(); // OSM fallback
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

    setMoveShortcutMode: function (active) {
        this._moveShortcutActive = !!active;
        if (this._map) {
            var container = this._map.getContainer();
            if (container) {
                container.classList.toggle('oh-map-move-shortcut-active', this._moveShortcutActive);
            }
        }
        this._mapDiag('location.relocate.mode', { active: this._moveShortcutActive });
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

        // Honour overlay edit-mode (#159 sub-fix 5): if the user toggles
        // overlay edit-mode and then a stage filter / search re-adds markers,
        // the freshly-created markers should also come up locked.
        var initiallyDraggable = !window.__ovcinaOverlayEditMode;
        var marker = new maplibregl.Marker({ element: wrapper, anchor: 'top', draggable: initiallyDraggable })
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

    /// <summary>
    /// Toggle the draggable property on every location marker — called by the
    /// overlay editor on enter/exit edit-mode (#159 sub-fix 5) so the user
    /// can't accidentally relocate a pin while drawing on top of it.
    /// Idempotent; safe to call when no markers are present.
    /// </summary>
    setLocationMarkersDraggable: function (enabled) {
        var on = !!enabled;
        for (var id in this._markers) {
            try { this._markers[id].setDraggable(on); }
            catch (e) { /* MapLibre version differences — best-effort */ }
        }
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
        this._moveShortcutActive = false;
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
            // See _glyphsUrl above — required for the overlay editor text layer.
            glyphs: this._glyphsUrl,
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
// Count-badge treasure markers are tracked separately from ovcinaMap._markers
// so the /map and /treasures pages never collide.
// ----------------------------------------------------------------------
window.ovcinaMap._pieMarkers = {};
window.ovcinaMap._zeroMarkers = {};
window.ovcinaMap._activePieStages = null; // Set<string> | null (null = all active)

window.ovcinaMap._filteredTreasureCount = function (counts) {
    var c0 = counts[0] | 0, c1 = counts[1] | 0, c2 = counts[2] | 0, c3 = counts[3] | 0;
    var stages = ['start', 'early', 'mid', 'late'];
    var cs = [c0, c1, c2, c3];
    if (!this._activePieStages) {
        return c0 + c1 + c2 + c3;
    }
    var total = 0;
    for (var i = 0; i < stages.length; i++) {
        if (this._activePieStages.has(stages[i])) total += cs[i];
    }
    return total;
};

window.ovcinaMap._buildPieSvg = function (counts) {
    var total = this._filteredTreasureCount(counts);
    var label = total > 999 ? '999+' : String(total);
    var labelClass = label.length > 2 ? ' oh-tp-pin-num-wide' : '';
    var svg = '<svg width="48" height="48" viewBox="-24 -24 48 48" class="oh-tp-pin-svg">';
    svg += '<circle class="oh-tp-pin-backing" r="21" cx="0" cy="0" />';
    svg += '<circle class="oh-tp-pin-ring" r="22" cx="0" cy="0" />';
    svg += '<circle class="oh-tp-pin-inner" r="15" cx="0" cy="0" />';
    svg += '<text class="oh-tp-pin-num' + labelClass + '" x="0" y="4" text-anchor="middle">' + label + '</text>';
    svg += '</svg>';
    return svg;
};

window.ovcinaMap._wireDropTarget = function (element, locationId) {
    var self = this;
    var lastDragOverLog = 0;
    element.addEventListener('dragover', function (e) {
        e.preventDefault();
        if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
        element.classList.add('oh-tp-pin-dragtarget');
        var now = Date.now();
        if (now - lastDragOverLog > 750) {
            lastDragOverLog = now;
            console.log('[treasure-alloc] ' + JSON.stringify({
                event: 'drag-over',
                target: 'map-pin',
                locationId: locationId
            }));
        }
    });
    element.addEventListener('dragleave', function () {
        element.classList.remove('oh-tp-pin-dragtarget');
    });
    element.addEventListener('drop', function (e) {
        e.preventDefault();
        element.classList.remove('oh-tp-pin-dragtarget');
        var payload = e.dataTransfer && e.dataTransfer.getData('application/x-oh-pool-item');
        if (self._dotnetRef && payload) {
            console.log('[treasure-alloc] ' + JSON.stringify({
                event: 'drop',
                target: 'map-pin',
                locationId: locationId,
                payload: window.ovcinaDnd ? window.ovcinaDnd.parsePayload(payload) : payload
            }));
            self._dotnetRef.invokeMethodAsync('OnPieMarkerDropped', locationId, payload);
        }
    });
};

window.ovcinaMap.addPieMarker = function (id, lat, lon, counts) {
    if (!this._map) return;
    this.removePieMarker(id);
    var wrap = document.createElement('div');
    wrap.className = 'oh-tp-pin oh-tp-pin-pie';
    wrap.setAttribute('data-pie-id', id);

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

window.ovcinaMap.updatePieMarkerCounts = function (id, counts) {
    var rec = this._pieMarkers[id];
    if (!rec) return;
    rec.counts = counts.slice();
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
    var id = el.getAttribute('data-pie-id');
    var rec = id ? this._pieMarkers[id] : null;
    if (rec) {
        var total = this._filteredTreasureCount(rec.counts);
        rec.element.innerHTML = this._buildPieSvg(rec.counts);
        rec.element.classList.toggle('is-filter-zero', total === 0);
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
    _dropTargets: new WeakMap(),
    _keyboardButtons: new WeakSet(),

    parsePayload: function (payload) {
        try {
            return JSON.parse(payload);
        } catch (e) {
            return payload;
        }
    },

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
            console.log('[treasure-alloc] ' + JSON.stringify({ event: 'drag-start', payload: self.parsePayload(p) }));
            element.classList.add('oh-tp-pool-tile-dragging');
        });
        element.addEventListener('dragend', function () {
            element.classList.remove('oh-tp-pool-tile-dragging');
        });
    },

    setKeyboardButton: function (element) {
        if (!element || this._keyboardButtons.has(element)) return;
        this._keyboardButtons.add(element);
        element.addEventListener('keydown', function (e) {
            if (e.target !== element || (e.key !== ' ' && e.key !== 'Enter')) return;
            e.preventDefault();
            element.click();
        });
    },

    clear: function (element) {
        if (!element) return;
        this._wired.delete(element);
        element.removeAttribute('draggable');
    },

    setDropTarget: function (element, locationId, dotnetRef) {
        if (!element) return;
        var existing = this._dropTargets.get(element);
        if (existing) {
            existing.locationId = locationId;
            existing.dotnetRef = dotnetRef;
            return;
        }

        var record = {
            locationId: locationId,
            dotnetRef: dotnetRef,
            lastDragOverLog: 0
        };
        this._dropTargets.set(element, record);

        var self = this;
        element.addEventListener('dragover', function (e) {
            e.preventDefault();
            if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
            element.classList.add('oh-tp-card-dragtarget');

            var now = Date.now();
            if (now - record.lastDragOverLog > 750) {
                record.lastDragOverLog = now;
                console.log('[treasure-alloc] ' + JSON.stringify({
                    event: 'drag-over',
                    target: 'location-card',
                    locationId: record.locationId
                }));
            }
        });
        element.addEventListener('dragleave', function () {
            element.classList.remove('oh-tp-card-dragtarget');
        });
        element.addEventListener('drop', function (e) {
            e.preventDefault();
            element.classList.remove('oh-tp-card-dragtarget');
            var payload = e.dataTransfer && e.dataTransfer.getData('application/x-oh-pool-item');
            if (!payload) return;
            console.log('[treasure-alloc] ' + JSON.stringify({
                event: 'drop',
                target: 'location-card',
                locationId: record.locationId,
                payload: self.parsePayload(payload)
            }));
            if (record.dotnetRef) {
                record.dotnetRef.invokeMethodAsync('OnTreasurePoolDropped', record.locationId, payload);
            }
        });
    },

    clearDropTarget: function (element) {
        if (!element) return;
        this._dropTargets.delete(element);
        element.classList.remove('oh-tp-card-dragtarget');
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
    _dotSourceId: 'oh-bbox-location-dots',
    _dotLayerId: 'oh-bbox-location-dots-layer',

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

    _locationDotsFeatureCollection: function (dots) {
        var features = (dots || [])
            .filter(function (dot) {
                return dot && Number.isFinite(dot.lat) && Number.isFinite(dot.lon);
            })
            .map(function (dot) {
                return {
                    type: 'Feature',
                    geometry: { type: 'Point', coordinates: [dot.lon, dot.lat] },
                    properties: {
                        kind: dot.kind || 'wilderness',
                        color: dot.color || '#2D5016',
                        strokeColor: dot.strokeColor || '#FFFFFF'
                    }
                };
            });
        return { type: 'FeatureCollection', features: features };
    },

    _addOrUpdateLocationDots: function (map, dots) {
        var self = window.ovcinaBboxMap;
        var featureCollection = self._locationDotsFeatureCollection(dots);
        var src = map.getSource(self._dotSourceId);
        if (src) {
            src.setData(featureCollection);
            return;
        }

        map.addSource(self._dotSourceId, { type: 'geojson', data: featureCollection });
        map.addLayer({
            id: self._dotLayerId,
            type: 'circle',
            source: self._dotSourceId,
            paint: {
                'circle-radius': 3,
                'circle-color': ['get', 'color'],
                'circle-stroke-color': ['get', 'strokeColor'],
                'circle-stroke-width': 1
            }
        }, map.getLayer(self._fillLayerId) ? self._fillLayerId : undefined);
    },

    _whenStyleReady: function (inst, apply) {
        var map = inst.map;
        if (inst.styleReady || map.isStyleLoaded()) {
            inst.styleReady = true;
            apply();
            return;
        }

        var applied = false;
        var once = function () {
            if (applied) return;
            applied = true;
            inst.styleReady = true;
            apply();
        };
        map.once('load', once);
        map.once('styledata', function () {
            var style = map.getStyle && map.getStyle();
            if (style && Array.isArray(style.layers)) once();
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
        var inst = { map: map, hasOverlay: false, styleReady: false };
        map.on('load', function () { inst.styleReady = true; });
        map.on('styledata', function () {
            var style = map.getStyle && map.getStyle();
            if (style && Array.isArray(style.layers)) inst.styleReady = true;
        });
        this._instances[elementId] = inst;
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
        window.ovcinaBboxMap._whenStyleReady(inst, apply);
        inst.hasOverlay = true;
    },

    clearBboxRectangle: function (elementId) {
        var inst = this._instances[elementId];
        if (!inst) return;
        window.ovcinaBboxMap._removeOverlay(inst.map);
        inst.hasOverlay = false;
    },

    setLocationDots: function (elementId, dots) {
        var inst = this._instances[elementId];
        if (!inst) {
            console.warn('[bbox-author] passthrough ok=false');
            return;
        }

        var map = inst.map;
        var apply = function () {
            window.ovcinaBboxMap._addOrUpdateLocationDots(map, dots || []);
            console.log('[bbox-author] passthrough ok=true');
        };
        window.ovcinaBboxMap._whenStyleReady(inst, apply);
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

// ======================================================================
// Issue #207 — Map page (cartographer's workshop) pin layer.
// DOM markers (extending the existing ovcinaMap pattern) for the two
// active layers — Lokace and Skrýše & Poklady. Click events bubble
// through `_dotnetRef.invokeMethodAsync('OnMapPinClicked', kind, id)`
// so MapPage.razor can open the right-peek panel for the location.
// ======================================================================
window.ovcinaMap._mapPagePins = { loc: {}, stash: {} };

// Issue #273 — per-LocationKind minimum zoom thresholds. A pin's text label
// becomes visible once the map's zoom level reaches the value below for its
// kind. Town stays at 0 (always visible — kingdom seats are the most
// important wayfinding anchors). Other kinds graduate so smaller features
// don't clutter at low zoom. Tweak this single block to retune cadence.
window.ovcinaMap._kindMinZoom = {
    town: 0,
    village: 6,
    magical: 8,
    hobbit: 8,
    wilderness: 9,
    dungeon: 10,
    pointofinterest: 11
};
window.ovcinaMap._kindMinZoomFor = function (kindRaw) {
    var k = (kindRaw || 'wilderness').toLowerCase();
    var mz = this._kindMinZoom[k];
    return (typeof mz === 'number') ? mz : 11; // unknown kinds → most-conservative
};

// Issue #257 — per-LocationKind Bootstrap-Icon glyph for the tear-drop pin.
// The class names map directly to bi-* CSS — no font swap, no SVG sprite.
// Town gets a shield (kingdom seat anchor), Hobbit gets a filled tree
// (settled), Wilderness gets an outlined tree (uninhabited), Dungeon gets
// bricks. Stay aligned with the issue's spec or update the issue when
// adding a new LocationKind.
window.ovcinaMap._kindIcon = {
    town: 'bi-shield-fill',
    village: 'bi-house-fill',
    magical: 'bi-stars',
    hobbit: 'bi-tree-fill',
    wilderness: 'bi-tree',
    dungeon: 'bi-bricks',
    pointofinterest: 'bi-flag'
};
window.ovcinaMap._kindIconFor = function (kindRaw) {
    var k = (kindRaw || 'wilderness').toLowerCase();
    return this._kindIcon[k] || 'bi-geo-alt-fill';
};

window.ovcinaMap.addLocationPin = function (id, lat, lon, name, kind) {
    if (!this._map) {
        if (this._pinDiag()) console.warn('[pin-diag] addLocationPin called but no map — id=' + id);
        return;
    }
    if (this._pinDiag()) {
        console.log('[pin-diag] addLocationPin id=' + id + ' lat=' + lat + ' lon=' + lon + ' kind=' + kind + ' name=' + (name || '?'));
    }
    this.removeLocationPin(id);
    // Wrapper / inner pin pattern (matches the still-working addMarker for
    // LocationDetail). Passing the .oh-map-pin element directly to MapLibre
    // — which has `position: relative` for the stash badge — interacts
    // badly with MapLibre's transform-based positioning: `wrapper.style
    // .transform` ends up empty and the marker stays at the map container
    // origin while tiles slide underneath, looking like the pin "drifts"
    // on zoom. The clean wrapper has no positioning of its own so MapLibre
    // can apply transform: translate(...) freely.
    // display:flex sizes the wrapper to the inner pin (18×18) instead of
    // letting it stretch to the map container's full width. Without it,
    // MapLibre's drag hit-testing happens on a giant invisible box and
    // mousedown on the visible pin doesn't register as "on the marker"
    // — drag never starts. Matches the still-working addMarker pattern
    // for LocationDetail's mini-map.
    var wrapper = document.createElement('div');
    wrapper.style.display = 'flex';
    wrapper.style.alignItems = 'center';
    wrapper.style.justifyContent = 'center';
    wrapper.style.cursor = 'pointer';
    var pin = document.createElement('div');
    pin.className = 'oh-map-pin oh-map-pin-loc';
    var kindKey = (kind || 'wilderness').toLowerCase();
    pin.setAttribute('data-kind', kindKey);
    // Issue #273 — graduated label-visibility-by-zoom. Each pin advertises
    // its minzoom; the zoomend handler in init() flips `oh-pin-label-on`
    // when map.getZoom() >= this value. Initial state set inline below
    // so a pin added between zoomend events shows / hides correctly.
    var minZoom = this._kindMinZoomFor(kindKey);
    pin.setAttribute('data-minzoom', String(minZoom));
    if (this._map.getZoom() >= minZoom) pin.classList.add('oh-pin-label-on');
    wrapper.title = name || '';
    // Issue #257 — tear-drop pin chrome. The bubble (oh-map-pin-bg) carries
    // the per-kind background and the downward triangle tail (::after); the
    // glyph (Bootstrap-Icons bi-*) sits inside. Anchor moves to 'bottom' so
    // the tail tip lands on the GPS coord.
    var bg = document.createElement('div');
    bg.className = 'oh-map-pin-bg';
    var glyph = document.createElement(kindKey === 'hobbit' ? 'span' : 'i');
    if (kindKey === 'hobbit') {
        glyph.className = 'oh-map-pin-hobbit-icon';
    } else {
        glyph.className = 'oh-map-pin-glyph bi ' + this._kindIconFor(kindKey);
    }
    glyph.setAttribute('aria-hidden', 'true');
    bg.appendChild(glyph);
    pin.appendChild(bg);
    if (name) {
        var label = document.createElement('div');
        label.className = 'oh-map-pin-label';
        label.textContent = name;
        pin.appendChild(label);
    }
    wrapper.appendChild(pin);
    var self = this;
    wrapper.addEventListener('click', function (e) {
        var modifier = {
            ctrl: !!(e && (e.ctrlKey || e.metaKey)),
            shift: !!(e && e.shiftKey),
            meta: !!(e && e.metaKey)
        };
        self._mapDiag('location.pin.click.enter', {
            locationId: id,
            modifier: modifier,
            target: self._eventTargetName(e && e.target),
            currentTarget: self._eventTargetName(e && e.currentTarget),
            moveShortcutActive: !!self._moveShortcutActive,
            hasDotNetRef: !!self._dotnetRef,
            defaultPrevented: !!(e && e.defaultPrevented)
        });
        var payload = self._clickDiagPayload(e, 'location-pin', { lat: lat, lng: lon }, { locationId: id });
        self._mapDiag('feature.click.enter', payload);
        self._mapDiag('location.click.enter', payload);
        self._mapDiag('click.hit-test', {
            path: 'location-marker-dom',
            target: 'location',
            locationId: id
        });
        self._mapDiag('click.modifier_branch', {
            branch: payload.ctrl && payload.shift ? 'ctrl_shift_relocate' : 'default',
            ctrl: payload.ctrl,
            shift: payload.shift,
            target: 'location',
            locationId: id
        });
        if (self._moveShortcutActive) {
            self._mapDiag('location.pin.click.move_target_branch_taken', { locationId: id });
            e.preventDefault();
            e.stopPropagation();
            self._mapDiag('location.relocate.target', { source: 'location-pin', locationId: id, lng: lon, lat: lat });
            if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnMapClicked', lat, lon, false, false);
            return;
        }
        if (payload.ctrl && payload.shift) {
            self._mapDiag('location.pin.click.ctrlshift_branch_taken', { locationId: id });
            e.preventDefault();
            e.stopPropagation();
            self._mapDiag('location.relocate.start', { locationId: id, from: { lng: lon, lat: lat } });
            self._mapDiag('location.move_mode.dispatch.attempt', { locationId: id, hasDotNetRef: !!self._dotnetRef });
            if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnLocationMoveShortcutStarted', id, lat, lon);
            return;
        }
        self._mapDiag('location.pin.click.default_branch_taken', { locationId: id });
        e.stopPropagation();
        if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnMapPinClicked', 'location', id);
    });
    // Drag-to-relocate. Enabled when ANY fine pointer is available
    // (mouse, trackpad). The earlier `(pointer: coarse)` gate broke on
    // Windows touchscreen laptops where the primary pointer reports as
    // coarse even with a mouse plugged in. `(any-pointer: fine)` matches
    // if a fine input exists at all, which is the right gate. Pure-touch
    // tablets with no mouse fall back to Ctrl+Shift+Click via the click
    // picker.
    var draggable = true;
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
        draggable = window.matchMedia('(any-pointer: fine)').matches;
    } else if (typeof navigator !== 'undefined' && typeof navigator.maxTouchPoints === 'number') {
        draggable = navigator.maxTouchPoints === 0;
    }
    // Issue #257 — anchor:'bottom' lands the tear-drop tip exactly on the
    // GPS coord; the previous 'center' put the bubble's centroid there
    // which felt wrong for a balloon-shaped pin. Stash pins (circular)
    // keep anchor:'center' below.
    var marker = new maplibregl.Marker({ element: wrapper, anchor: 'bottom', draggable: draggable })
        .setLngLat([lon, lat])
        .addTo(this._map);
    if (draggable) {
        marker.on('dragend', function () {
            var newPos = marker.getLngLat();
            if (self._dotnetRef) {
                self._mapDiag('location.relocate.dispatch', {
                    source: 'dragend',
                    locationId: id,
                    lng: newPos.lng,
                    lat: newPos.lat
                });
                self._dotnetRef.invokeMethodAsync('OnLocationPinDragged', id, newPos.lat, newPos.lng);
            }
        });
    }
    this._mapPagePins.loc[id] = marker;
};

window.ovcinaMap.removeLocationPin = function (id) {
    var m = this._mapPagePins.loc[id];
    if (m) { try { m.remove(); } catch (e) { /* ignore */ } delete this._mapPagePins.loc[id]; }
};

window.ovcinaMap.addStashPin = function (id, locationId, lat, lon, name, count, stage) {
    if (!this._map) return;
    this.removeStashPin(id);
    // Same wrapper/inner-pin pattern as addLocationPin so MapLibre can
    // apply its positioning transform cleanly. The pin keeps its own
    // `position: relative` for the count badge (`.oh-map-pin-count` is
    // absolutely positioned relative to the pin, not the wrapper).
    // display:flex sizes the wrapper to the inner pin (18×18) instead of
    // letting it stretch to the map container's full width. Without it,
    // MapLibre's drag hit-testing happens on a giant invisible box and
    // mousedown on the visible pin doesn't register as "on the marker"
    // — drag never starts. Matches the still-working addMarker pattern
    // for LocationDetail's mini-map.
    var wrapper = document.createElement('div');
    wrapper.style.display = 'flex';
    wrapper.style.alignItems = 'center';
    wrapper.style.justifyContent = 'center';
    wrapper.style.cursor = 'pointer';
    var pin = document.createElement('div');
    pin.className = 'oh-map-pin oh-map-pin-stash';
    if (stage) pin.setAttribute('data-stage', stage);
    wrapper.title = (name || '') + (count ? ' · ' + count + ' pokladů' : '');
    if (count > 0) {
        var badge = document.createElement('span');
        badge.className = 'oh-map-pin-count';
        badge.textContent = String(count);
        pin.appendChild(badge);
    }
    wrapper.appendChild(pin);
    var self = this;
    wrapper.addEventListener('click', function (e) {
        var payload = self._clickDiagPayload(e, 'stash-pin', { lat: lat, lng: lon }, { stashId: id, locationId: locationId });
        self._mapDiag('feature.click.enter', payload);
        self._mapDiag('location.click.enter', payload);
        self._mapDiag('click.hit-test', {
            path: 'stash-marker-dom',
            target: 'location',
            stashId: id,
            locationId: locationId
        });
        self._mapDiag('click.modifier_branch', {
            branch: payload.ctrl && payload.shift ? 'ctrl_shift_relocate' : 'default',
            ctrl: payload.ctrl,
            shift: payload.shift,
            target: 'stash-location',
            locationId: locationId
        });
        if (self._moveShortcutActive) {
            e.preventDefault();
            e.stopPropagation();
            self._mapDiag('location.relocate.target', { source: 'stash-pin', locationId: locationId, stashId: id, lng: lon, lat: lat });
            if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnMapClicked', lat, lon, false, false);
            return;
        }
        if (payload.ctrl && payload.shift) {
            e.preventDefault();
            e.stopPropagation();
            self._mapDiag('location.relocate.start', { source: 'stash-pin', locationId: locationId, stashId: id, from: { lng: lon, lat: lat } });
            if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnLocationMoveShortcutStarted', locationId, lat, lon);
            return;
        }
        e.stopPropagation();
        // Stash pins open the host location's peek per the brief —
        // "stash IS at the location" — keeps one peek surface.
        if (self._dotnetRef) self._dotnetRef.invokeMethodAsync('OnMapPinClicked', 'location', locationId);
    });
    var marker = new maplibregl.Marker({ element: wrapper, anchor: 'center' })
        .setLngLat([lon, lat])
        .addTo(this._map);
    this._mapPagePins.stash[id] = marker;
};

window.ovcinaMap.removeStashPin = function (id) {
    var m = this._mapPagePins.stash[id];
    if (m) { try { m.remove(); } catch (e) { /* ignore */ } delete this._mapPagePins.stash[id]; }
};

window.ovcinaMap.clearMapPagePins = function () {
    for (var lid in this._mapPagePins.loc) {
        try { this._mapPagePins.loc[lid].remove(); } catch (e) { /* ignore */ }
    }
    for (var sid in this._mapPagePins.stash) {
        try { this._mapPagePins.stash[sid].remove(); } catch (e) { /* ignore */ }
    }
    this._mapPagePins = { loc: {}, stash: {} };
};

window.ovcinaMap.setMapPageLayerVisibility = function (layer, visible) {
    var bag = layer === 'stash' ? this._mapPagePins.stash : this._mapPagePins.loc;
    for (var k in bag) {
        var el = bag[k].getElement();
        if (el) el.style.display = visible ? '' : 'none';
    }
};

// Hook into dispose so map-page pins clear when the user navigates away —
// pre-fixup these markers + their DOM elements stuck around after the
// MapLibre instance was removed (Copilot finding).
(function () {
    if (window.ovcinaMap._mapPageDisposeHooked) return;
    window.ovcinaMap._mapPageDisposeHooked = true;
    var originalDispose = window.ovcinaMap.dispose;
    if (typeof originalDispose === 'function') {
        window.ovcinaMap.dispose = function () {
            try { this.clearMapPagePins(); } catch (e) { /* ignore */ }
            return originalDispose.apply(this, arguments);
        };
    }
})();

// Issue #259 — print-mode body class toggle. The print stylesheet is
// gated behind body.oh-map-print so the regular /map page never picks
// up @media print's chrome-hide rules accidentally. The labeled variant
// also force-shows location names regardless of zoom.
window.ovcinaMapPrint = {
    applyBodyMode: function (mode) {
        var b = document.body;
        if (!b) return;
        b.classList.remove('oh-map-print', 'oh-map-print-labeled', 'oh-map-print-blind');
        if (mode === 'labeled') {
            b.classList.add('oh-map-print', 'oh-map-print-labeled');
        } else if (mode === 'blind') {
            b.classList.add('oh-map-print', 'oh-map-print-blind');
        }
    }
};
