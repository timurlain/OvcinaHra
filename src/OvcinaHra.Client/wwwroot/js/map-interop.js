// MapLibre GL JS interop for Blazor
window.ovcinaMap = {
    _map: null,
    _markers: {},
    _dotnetRef: null,
    _elementId: null,
    _apiKey: null,

    // Tile source styles
    _styles: {
        raster: {
            version: 8,
            sources: {
                'mapy-cz': {
                    type: 'raster',
                    tiles: ['https://mapserver.mapy.cz/turist-m/{z}-{x}-{y}'],
                    tileSize: 256,
                    maxzoom: 19,
                    attribution: '&copy; <a href="https://www.mapy.cz">Mapy.cz</a> &copy; <a href="https://www.openstreetmap.org">OSM</a>'
                }
            },
            layers: [{
                id: 'mapy-cz-tiles',
                type: 'raster',
                source: 'mapy-cz',
                minzoom: 0,
                maxzoom: 19
            }]
        },
        vectorUrl: function (apiKey) {
            return 'https://api.mapy.cz/v1/maptiles/outdoor/tiles.json?apikey=' + apiKey;
        }
    },

    init: function (elementId, dotnetRef, centerLat, centerLon, zoom, mapyCzApiKey) {
        this._dotnetRef = dotnetRef;
        this._elementId = elementId;
        this._apiKey = mapyCzApiKey || null;

        var style = this._apiKey
            ? this._styles.vectorUrl(this._apiKey)
            : this._styles.raster;

        this._map = new maplibregl.Map({
            container: elementId,
            style: style,
            center: [centerLon, centerLat],
            zoom: zoom
        });

        this._map.on('click', (e) => {
            if (this._dotnetRef) {
                this._dotnetRef.invokeMethodAsync('OnMapClicked', e.lngLat.lat, e.lngLat.lng);
            }
        });

        this._map.addControl(new maplibregl.NavigationControl(), 'top-right');
        this._map.addControl(new maplibregl.ScaleControl(), 'bottom-left');
    },

    switchStyle: function (useVector) {
        if (!this._map) return;

        // Save current markers, center, zoom
        var center = this._map.getCenter();
        var zoom = this._map.getZoom();
        var savedMarkers = {};
        for (var id in this._markers) {
            var m = this._markers[id];
            savedMarkers[id] = {
                lngLat: m.getLngLat(),
                popup: m.getPopup() ? m.getPopup().getHTML() : null,
                color: m.getElement().style.backgroundColor
            };
        }

        // Switch style
        var style = (useVector && this._apiKey)
            ? this._styles.vectorUrl(this._apiKey)
            : this._styles.raster;

        this._map.setStyle(style);

        // Restore markers after style loads
        this._map.once('styledata', () => {
            this._map.setCenter(center);
            this._map.setZoom(zoom);
            for (var id in savedMarkers) {
                var s = savedMarkers[id];
                this.addMarker(parseInt(id), s.lngLat.lat, s.lngLat.lng,
                    '', '', s.color);
                if (s.popup) {
                    this._markers[id].getPopup().setHTML(s.popup);
                }
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

        var el = document.createElement('div');
        el.className = 'ovcina-marker';
        el.style.backgroundColor = color || '#e74c3c';
        el.style.width = '14px';
        el.style.height = '14px';
        el.style.borderRadius = '50%';
        el.style.border = '2px solid white';
        el.style.boxShadow = '0 1px 4px rgba(0,0,0,0.4)';
        el.style.cursor = 'pointer';

        var popup = new maplibregl.Popup({ offset: 10 })
            .setHTML('<strong>' + this._escapeHtml(name) + '</strong><br><small>' + this._escapeHtml(kind) + '</small>');

        var marker = new maplibregl.Marker({ element: el })
            .setLngLat([lon, lat])
            .setPopup(popup)
            .addTo(this._map);

        el.addEventListener('click', (e) => {
            e.stopPropagation();
            if (this._dotnetRef) {
                this._dotnetRef.invokeMethodAsync('OnMarkerClicked', id);
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

    _escapeHtml: function (text) {
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(text || ''));
        return div.innerHTML;
    }
};
