// LocalCache plugin - item detail page integration
// Injects Cache/Evict buttons on movie and episode detail pages.
(function () {
    'use strict';

    var currentItemId = null;
    var injectedButton = null;

    function getApiHeaders() {
        var client = window.ApiClient;
        if (!client) return null;
        var token = client.accessToken();
        if (!token) return null;
        return { 'Authorization': 'MediaBrowser Token="' + token + '"' };
    }

    function fetchCacheState(itemId, callback) {
        var client = window.ApiClient;
        if (!client) return;
        client.fetch({
            url: client.getUrl('LocalCache/Progress/' + itemId),
            type: 'GET'
        }).then(function (result) {
            callback(result.State, result);
        }, function () {
            callback(null, null);
        });
    }

    function cacheItem(itemId, btn) {
        var client = window.ApiClient;
        if (!client) return;
        btn.disabled = true;
        btn.querySelector('.detailButton-content').textContent = 'Caching...';
        client.fetch({
            url: client.getUrl('LocalCache/Cache/' + itemId),
            type: 'POST'
        }).then(function () {
            pollProgress(itemId, btn);
        }, function (err) {
            btn.disabled = false;
            btn.querySelector('.detailButton-content').textContent = 'Cache Failed';
            setTimeout(function () { updateButton(btn, itemId, null); }, 3000);
        });
    }

    function evictItem(itemId, btn) {
        var client = window.ApiClient;
        if (!client) return;
        btn.disabled = true;
        btn.querySelector('.detailButton-content').textContent = 'Removing...';
        client.fetch({
            url: client.getUrl('LocalCache/Cache/' + itemId),
            type: 'DELETE'
        }).then(function () {
            updateButton(btn, itemId, null);
        }, function () {
            btn.disabled = false;
            btn.querySelector('.detailButton-content').textContent = 'Evict Failed';
            setTimeout(function () { updateButton(btn, itemId, 'Cached'); }, 3000);
        });
    }

    function pollProgress(itemId, btn) {
        var interval = setInterval(function () {
            fetchCacheState(itemId, function (state, info) {
                if (state === 'Copying' && info) {
                    btn.disabled = true;
                    btn.querySelector('.detailButton-content').textContent =
                        'Caching ' + info.ProgressPercent.toFixed(0) + '%';
                } else {
                    clearInterval(interval);
                    updateButton(btn, itemId, state);
                }
            });
        }, 2000);
    }

    function updateButton(btn, itemId, state) {
        btn.disabled = false;
        var content = btn.querySelector('.detailButton-content');
        var icon = btn.querySelector('.detailButton-icon');

        if (state === 'Cached') {
            content.textContent = 'Evict Cache';
            if (icon) icon.textContent = 'delete';
            btn.title = 'Remove from local cache';
            btn.onclick = function () { evictItem(itemId, btn); };
        } else if (state === 'Copying') {
            content.textContent = 'Caching...';
            if (icon) icon.textContent = 'downloading';
            btn.disabled = true;
            btn.title = 'Caching in progress';
            pollProgress(itemId, btn);
        } else {
            content.textContent = 'Cache Locally';
            if (icon) icon.textContent = 'download';
            btn.title = 'Copy to local SSD for faster playback';
            btn.onclick = function () { cacheItem(itemId, btn); };
        }
    }

    function createButton() {
        var btn = document.createElement('button');
        btn.className = 'detailButton emby-button localcache-btn';
        btn.type = 'button';
        btn.style.cssText = 'display:inline-flex;flex-direction:column;align-items:center;';
        btn.innerHTML =
            '<span class="detailButton-icon material-icons download" aria-hidden="true"></span>' +
            '<span class="detailButton-content">Cache Locally</span>';
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

        // Don't re-inject for the same item
        if (itemId === currentItemId && injectedButton && document.contains(injectedButton)) {
            return;
        }

        var container = document.querySelector('.mainDetailButtons');
        if (!container) return;

        removeButton();
        currentItemId = itemId;

        var btn = createButton();
        container.appendChild(btn);
        injectedButton = btn;

        // Check current cache state
        fetchCacheState(itemId, function (state) {
            updateButton(btn, itemId, state);
        });
    }

    function removeButton() {
        if (injectedButton && injectedButton.parentNode) {
            injectedButton.parentNode.removeChild(injectedButton);
        }
        injectedButton = null;
    }

    // Monitor page changes
    var observer = new MutationObserver(function () {
        tryInject();
    });

    function startObserving() {
        var root = document.getElementById('reactRoot') || document.body;
        observer.observe(root, { childList: true, subtree: true });
    }

    // Also listen for hash changes (SPA navigation)
    window.addEventListener('hashchange', function () {
        currentItemId = null;
        setTimeout(tryInject, 500);
    });

    // Initial setup
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
