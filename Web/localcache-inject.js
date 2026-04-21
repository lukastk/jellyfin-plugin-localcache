// LocalCache plugin - item detail page integration
// Injects Cache/Evict buttons on movie and episode detail pages.
(function () {
    'use strict';

    var currentItemId = null;
    var injectedButton = null;
    var injectedInfo = null;

    function authHeaders() {
        var client = window.ApiClient;
        if (!client || !client.accessToken()) return {};
        return { 'Authorization': 'MediaBrowser Token="' + client.accessToken() + '", Client="Jellyfin Web", Device="Browser", DeviceId="localcache", Version="1.0"' };
    }

    function apiFetch(path) {
        var client = window.ApiClient;
        if (!client) return Promise.reject();
        return fetch(client.getUrl(path), { headers: authHeaders() }).then(function (r) {
            return r.ok ? r.json() : Promise.reject();
        });
    }

    function fetchCacheState(itemId, callback) {
        apiFetch('LocalCache/Progress/' + itemId).then(function (data) {
            callback(data.State);
        }).catch(function () { callback(null); });
    }

    function formatBytes(bytes) {
        if (bytes == null) return '?';
        if (bytes >= 1073741824) return (bytes / 1073741824).toFixed(1) + ' GB';
        if (bytes >= 1048576) return (bytes / 1048576).toFixed(0) + ' MB';
        return (bytes / 1024).toFixed(0) + ' KB';
    }

    function fetchAndShowInfo(itemId) {
        // Fetch item size and cache status in parallel
        var itemSize = null;
        var cacheStatus = null;
        var cacheState = null;
        var done = 0;

        function render() {
            if (done < 2) return;
            var parts = [];

            if (itemSize != null) {
                parts.push('File size: ' + formatBytes(itemSize));
            }

            if (cacheState === 'Cached') {
                parts.push('Cached locally');
            }

            if (cacheStatus) {
                var used = formatBytes(cacheStatus.UsedCacheSizeBytes);
                var max = cacheStatus.MaxCacheSizeBytes;
                if (max > 0) {
                    var free = max - cacheStatus.UsedCacheSizeBytes;
                    parts.push('Cache: ' + used + ' used, ' + formatBytes(free) + ' free of ' + formatBytes(max));
                } else {
                    parts.push('Cache: ' + used + ' used (no limit)');
                }
            }

            if (parts.length === 0) return;

            removeInfo();
            var el = document.createElement('div');
            el.className = 'localcache-info';
            el.style.cssText = 'color:#aaa;font-size:0.85em;padding:4px 0 0 0;margin-top:2px;';
            el.textContent = parts.join('  \u2022  ');

            // Insert after the detail ribbon
            var ribbon = document.querySelector('.detailRibbon');
            if (ribbon && ribbon.parentNode) {
                ribbon.parentNode.insertBefore(el, ribbon.nextSibling);
                injectedInfo = el;
            }
        }

        // Get item file size from Jellyfin API
        apiFetch('Items/' + itemId).then(function (item) {
            if (item && item.MediaSources && item.MediaSources.length > 0) {
                itemSize = item.MediaSources[0].Size;
            }
        }).catch(function () {}).finally(function () { done++; render(); });

        // Get cache status
        Promise.all([
            apiFetch('LocalCache/Status').catch(function () { return null; }),
            apiFetch('LocalCache/Progress/' + itemId).catch(function () { return null; })
        ]).then(function (results) {
            cacheStatus = results[0];
            if (results[1]) cacheState = results[1].State;
        }).catch(function () {}).finally(function () { done++; render(); });
    }

    function doPost(path, callback) {
        var client = window.ApiClient;
        if (!client) return;
        var h = authHeaders();
        h['Content-Length'] = '0';
        fetch(client.getUrl(path), { method: 'POST', headers: h }).then(function (resp) {
            callback(resp.ok);
        }).catch(function () { callback(false); });
    }

    function doDelete(path, callback) {
        var client = window.ApiClient;
        if (!client) return;
        fetch(client.getUrl(path), { method: 'DELETE', headers: authHeaders() }).then(function (resp) {
            callback(resp.ok);
        }).catch(function () { callback(false); });
    }

    function cacheItem(itemId, btn) {
        setButtonState(btn, 'caching');
        doPost('LocalCache/Cache/' + itemId, function (ok) {
            if (ok) {
                pollProgress(itemId, btn);
            } else {
                setButtonState(btn, 'error');
                setTimeout(function () { setButtonState(btn, 'none'); }, 3000);
            }
        });
    }

    function evictItem(itemId, btn) {
        setButtonState(btn, 'evicting');
        doDelete('LocalCache/Cache/' + itemId, function (ok) {
            setButtonState(btn, ok ? 'none' : 'cached');
        });
    }

    function pollProgress(itemId, btn) {
        var interval = setInterval(function () {
            fetchCacheState(itemId, function (state) {
                if (state === 'Copying') {
                    setButtonState(btn, 'caching');
                } else {
                    clearInterval(interval);
                    setButtonState(btn, state === 'Cached' ? 'cached' : 'none');
                }
            });
        }, 2000);
    }

    function setButtonState(btn, state) {
        var icon = btn.querySelector('.detailButton-icon');
        btn.disabled = false;
        btn.onclick = null;
        var itemId = btn.getAttribute('data-itemid');

        if (state === 'cached') {
            if (icon) { icon.className = 'material-icons detailButton-icon delete'; }
            btn.title = 'Remove from local cache';
            btn.onclick = function () { evictItem(itemId, btn); };
        } else if (state === 'caching') {
            if (icon) { icon.className = 'material-icons detailButton-icon downloading'; }
            btn.title = 'Caching in progress...';
            btn.disabled = true;
        } else if (state === 'evicting') {
            if (icon) { icon.className = 'material-icons detailButton-icon hourglass_empty'; }
            btn.title = 'Removing...';
            btn.disabled = true;
        } else if (state === 'error') {
            if (icon) { icon.className = 'material-icons detailButton-icon error_outline'; }
            btn.title = 'Cache failed';
        } else {
            if (icon) { icon.className = 'material-icons detailButton-icon download'; }
            btn.title = 'Cache locally for faster playback';
            btn.onclick = function () { cacheItem(itemId, btn); };
        }
    }

    function createButton(itemId) {
        var btn = document.createElement('button');
        btn.setAttribute('is', 'emby-button');
        btn.type = 'button';
        btn.className = 'button-flat detailButton localcache-btn';
        btn.setAttribute('data-itemid', itemId);
        btn.innerHTML =
            '<div class="detailButton-content">' +
            '<span class="material-icons detailButton-icon download" aria-hidden="true"></span>' +
            '</div>';
        btn.title = 'Cache locally';
        return btn;
    }

    function getItemIdFromUrl() {
        var hash = window.location.hash || '';
        var match = hash.match(/[?&]id=([a-f0-9-]+)/i);
        return match ? match[1] : null;
    }

    function tryInject() {
        var itemId = getItemIdFromUrl();
        if (!itemId) {
            removeButton();
            currentItemId = null;
            return;
        }

        if (itemId === currentItemId && injectedButton && document.contains(injectedButton)) {
            return;
        }

        var container = document.querySelector('.mainDetailButtons');
        if (!container) return;

        removeButton();
        currentItemId = itemId;

        var btn = createButton(itemId);
        container.appendChild(btn);
        injectedButton = btn;

        fetchCacheState(itemId, function (state) {
            setButtonState(btn, state === 'Cached' ? 'cached' : state === 'Copying' ? 'caching' : 'none');
            if (state === 'Copying') pollProgress(itemId, btn);
        });

        fetchAndShowInfo(itemId);
    }

    function removeButton() {
        if (injectedButton && injectedButton.parentNode) {
            injectedButton.parentNode.removeChild(injectedButton);
        }
        injectedButton = null;
        removeInfo();
    }

    function removeInfo() {
        if (injectedInfo && injectedInfo.parentNode) {
            injectedInfo.parentNode.removeChild(injectedInfo);
        }
        injectedInfo = null;
    }

    var observer = new MutationObserver(function () { tryInject(); });

    function startObserving() {
        var root = document.getElementById('reactRoot') || document.body;
        observer.observe(root, { childList: true, subtree: true });
    }

    window.addEventListener('hashchange', function () {
        currentItemId = null;
        setTimeout(tryInject, 500);
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            startObserving();
            tryInject();
        });
    } else {
        startObserving();
        tryInject();
    }
})();
