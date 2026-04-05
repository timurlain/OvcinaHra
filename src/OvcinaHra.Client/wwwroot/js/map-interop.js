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
            layers: [{
                id: 'mapy-cz-tiles',
                type: 'raster',
                source: 'mapy-cz',
                minzoom: 0,
                maxzoom: 19
            }]
        };
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
