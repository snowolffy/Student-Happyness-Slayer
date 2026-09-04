// noVNC is VENDORED into wwwroot/lib/novnc (v1.4.0, MPL-2.0, LICENSE.txt kept alongside it), not
// pulled from a CDN. Two reasons, both learned the hard way:
//
//  1. It was broken. The old code imported ".../novnc@1.4.0/lib/rfb.js" from jsdelivr, and every
//     Screen View died with "exports is not defined". noVNC's npm package ships TWO builds: `core/`
//     is the real ES-module source, `lib/` is a CommonJS transpile for Node - and a browser `import()`
//     of a CommonJS file hits its `exports.` assignments with no `exports` object in scope. The right
//     entry point is core/rfb.js, which is what this now loads.
//  2. Even fixed, a CDN is the wrong dependency for this app. OnlineConsole is reachable ONLY over the
//     tailnet; a phone using a Tailscale exit node, or a school network that blocks CDNs, can reach the
//     console perfectly and still fail to load the one file that draws the screen. Everything else this
//     app needs is served from the same origin - the VNC decoder should be too.
//
// ~50 small ES modules (core/ plus the vendored pako zlib it imports), all served from this origin, so
// the browser resolves every relative import against us and nothing leaves the tailnet.
let rfbCtorPromise = null;

function loadRfbCtor() {
    if (!rfbCtorPromise) {
        rfbCtorPromise = import("/lib/novnc/core/rfb.js").then(mod => mod.default);
        // Don't cache a rejected promise: without this, one failed load (a flaky first request while
        // the circuit was still coming up) would make every later retry fail instantly with the same
        // stale error, and the "ลองใหม่" button would look broken.
        rfbCtorPromise.catch(() => { rfbCtorPromise = null; });
    }
    return rfbCtorPromise;
}

// One noVNC RFB instance per open Screen View / Live View tile, keyed by a caller-chosen string
// (the client GUID is enough since there's at most one view of a given machine open at a time).
const sessions = new Map();

/**
 * Connects to /ws/vnc/{clientGuid}?port=... (the transparent WebSocket<->TCP relay in
 * VncBridgeEndpoint.cs) and renders into the given container element. The VNC password is passed here
 * directly (not through the WebSocket) because noVNC always drives the full RFB handshake itself,
 * including authentication - see the design-note comment at the top of VncBridgeEndpoint.cs for why a
 * transparent relay was chosen over having the server pre-authenticate.
 *
 * One view at a time by design: scaleViewport only scales the canvas after a full-resolution framebuffer
 * has already arrived and been decoded, so tiling several of these costs N full desktop streams. That is
 * why the multi-tile Live View page was removed.
 */
export async function connect(containerId, clientGuid, port, password, sessionKey) {
    const container = document.getElementById(containerId);
    if (!container) return false;

    disconnect(sessionKey); // replace any stale session for this key first

    const RFB = await loadRfbCtor();
    const proto = location.protocol === "https:" ? "wss:" : "ws:";
    const url = `${proto}//${location.host}/ws/vnc/${encodeURIComponent(clientGuid)}?port=${encodeURIComponent(port)}`;

    const rfb = new RFB(container, url, { credentials: { password } });
    rfb.scaleViewport = true;
    rfb.resizeSession = false;
    rfb.showDotCursor = true;

    // Tuned for this deployment's link, not left at noVNC's LAN-oriented defaults. Both only affect
    // Tight/JPEG encoding, so they are harmless if the Agent's VNC server negotiates something else.
    //
    //   qualityLevel   0-9, default 6. JPEG quality for photographic areas. 5 is visibly fine for
    //                  "what is this student doing" and meaningfully cheaper than 6 on a phone link.
    //   compressionLevel 0-9, default 2. Higher = fewer bytes, more CPU ON THE AGENT (the VNC server
    //                  side does the compressing). 6 is the useful middle: noVNC's own guidance is that
    //                  level 9 costs roughly twice the CPU for only 10-20% better compression, which is
    //                  a bad trade on a classroom PC that also has a student working on it.
    rfb.qualityLevel = 5;
    rfb.compressionLevel = 6;

    sessions.set(sessionKey, rfb);
    return true;
}

export function disconnect(sessionKey) {
    const rfb = sessions.get(sessionKey);
    if (rfb) {
        try { rfb.disconnect(); } catch { /* already gone */ }
        sessions.delete(sessionKey);
    }
}
