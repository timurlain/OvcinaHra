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

    _rasterStyle: function (apiKey) {
        if (apiKey && apiKey.length > 5) {
            return {
                version: 8,
                sources: {
                    'mapy-cz': {
                        type: 'raster',
                        tiles: ['https://api.mapy.cz/v1/maptiles/outdoor/256/{z}/{x}/{y}?apikey=' + apiKey],
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

    init: function (elementId, lat, lon, zoom, kindColor, mapyCzApiKey) {
        var el = document.getElementById(elementId);
        if (!el || typeof maplibregl === 'undefined') return;
        // If already initialized with same coords, no-op.
        var existing = this._instances[elementId];
        if (existing) {
            this.update(elementId, lat, lon);
            return;
        }

        var map = new maplibregl.Map({
            container: el,
            style: this._rasterStyle(mapyCzApiKey),
            center: [lon, lat],
            zoom: zoom || 12,
            interactive: false,       // read-only mini preview
            attributionControl: false
        });

        var el2 = document.createElement('div');
        el2.style.cssText = 'width:16px;height:16px;border-radius:50%;background:' +
            (kindColor || '#2D5016') + ';border:2px solid #fff;box-shadow:0 1px 3px rgba(0,0,0,.35);';
        var marker = new maplibregl.Marker({ element: el2 }).setLngLat([lon, lat]).addTo(map);

        this._instances[elementId] = { map: map, marker: marker };
    },

    update: function (elementId, lat, lon) {
        var inst = this._instances[elementId];
        if (!inst) return;
        inst.marker.setLngLat([lon, lat]);
        inst.map.jumpTo({ center: [lon, lat] });
    },

    dispose: function (elementId) {
        var inst = this._instances[elementId];
        if (!inst) return;
        try { inst.map.remove(); } catch (e) { /* ignore */ }
        delete this._instances[elementId];
    }
};
