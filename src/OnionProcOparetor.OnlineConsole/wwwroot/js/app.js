// ============================================================================================
// App shell behaviour: the nav drawer and the light/dark theme.
//
// WHY THIS IS JAVASCRIPT AND NOT BLAZOR - this is the important part, please read before "fixing"
// it back into C#:
//
// This app uses per-page interactivity (Program.cs registers Razor Components with
// AddInteractiveServerComponents, and each page opts in with @rendermode InteractiveServer).
// Under that model the LAYOUT is rendered statically - Blazor cannot make a layout interactive,
// because it can't serialize the @Body RenderFragment as a root component parameter, and this is
// documented behaviour rather than a bug to work around.
//
// The consequence was a real, shipped defect: MainLayout's hamburger button was wired to
// @onclick="Sidebar.Toggle" (a scoped SidebarState service) and that handler could never fire,
// because the component it lives in has no circuit. Below the 900px breakpoint the sidebar is
// off-canvas and the hamburger is the ONLY way to reach it - so on a phone, the exact device this
// whole app exists for, the navigation was unreachable. It looked completely correct in the
// source; it just quietly did nothing in the browser.
//
// Handling it here fixes that and is better regardless: opening a menu and switching a theme are
// pure client-side view state. Routing them through a SignalR round-trip to a server that then
// re-renders and diffs the DOM makes them laggy on a phone over Tailscale, and makes them fail
// outright while the circuit is reconnecting - which is precisely when a user is most likely to be
// jabbing at the menu.
//
// Everything is bound with delegated listeners on `document`, not on the elements themselves, so
// it keeps working after Blazor's enhanced navigation swaps the page content out from under us.
// ============================================================================================
(function () {
    'use strict';

    // ---- Nav drawer --------------------------------------------------------------------------
    const shell = () => document.querySelector('.app-shell');

    function setDrawer(open) {
        const el = shell();
        if (!el) return;
        el.classList.toggle('sidebar-open', open);
        // Locking the body while the drawer is open stops the page behind it from scrolling under
        // the overlay - the single most common "this feels like a cheap web page" tell on mobile.
        document.body.style.overflow = open ? 'hidden' : '';

        const toggle = document.querySelector('[data-drawer-toggle]');
        if (toggle) toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    }

    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-drawer-toggle]')) {
            setDrawer(!shell()?.classList.contains('sidebar-open'));
            return;
        }
        // Backdrop, explicit close buttons, and any nav link inside the drawer all dismiss it. The
        // nav-link case matters: without it you tap "Logs", the page changes behind the drawer, and
        // the drawer just sits there looking stuck.
        if (e.target.closest('[data-drawer-close]') || e.target.closest('.sidebar-nav a')) {
            setDrawer(false);
        }
    });

    // Escape closes the drawer. Free to add, and it is the first thing a keyboard user tries.
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') setDrawer(false);
    });

    // ---- Theme -------------------------------------------------------------------------------
    // Three stored states exist - light / dark / system - because "follow the OS" is a real preference
    // a two-value setting can't express, and it stays the default until someone chooses otherwise.
    // But the HEADER BUTTON is a plain two-way flip between light and dark (see its click handler);
    // only Settings exposes all three. The initial application happens in an inline script in
    // App.razor, before first paint, so there is no flash of the wrong theme; this module only handles
    // later changes.
    const KEY = 'onion.theme';

    function read() {
        try { return localStorage.getItem(KEY) || 'system'; } catch (e) { return 'system'; }
    }

    function resolved(mode) {
        if (mode === 'light' || mode === 'dark') return mode;
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    function apply(mode) {
        const root = document.documentElement;
        if (mode === 'light' || mode === 'dark') root.setAttribute('data-theme', mode);
        else root.removeAttribute('data-theme');

        // Keep the browser/OS chrome (Android status bar, iOS standalone header) in step with the
        // page, instead of leaving a pale bar sitting on top of a dark app.
        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute('content', resolved(mode) === 'dark' ? '#0F0D0C' : '#241E1A');

        // The tooltip/label says what PRESSING will do, not what the current state is - the icon already
        // shows the current state, and a control should describe its own action.
        const next = resolved(mode) === 'dark' ? 'สว่าง' : 'มืด';
        document.querySelectorAll('[data-theme-toggle]').forEach(function (btn) {
            btn.setAttribute('title', 'เปลี่ยนเป็นธีม' + next);
            btn.setAttribute('aria-label', 'เปลี่ยนเป็นธีม' + next);
        });
    }

    function set(mode) {
        try {
            if (mode === 'system') localStorage.removeItem(KEY);
            else localStorage.setItem(KEY, mode);
        } catch (e) {
            // Private mode / site data blocked: the theme still changes for this session, it just
            // won't be remembered. Failing silently is correct here - a toast about localStorage is
            // noise to a teacher trying to look at a lab.
        }
        apply(mode);
        return mode;
    }

    document.addEventListener('click', function (e) {
        if (!e.target.closest('[data-theme-toggle]')) return;
        // Straight two-way flip against what is CURRENTLY ON SCREEN, not a three-state cycle.
        // The cycle was wrong for a one-button control: from 'system' the button had two different
        // "next" states depending on hidden state the user can't see, and one of the three stops
        // (system -> whichever the OS says) often looked like the tap did nothing at all, because the
        // OS preference already matched the theme being shown. resolved() is what makes this correct
        // from 'system' too: the first tap always gives you the opposite of what you are looking at.
        // "ตามระบบ" is still reachable - it lives in Settings, where a three-way choice can be shown
        // with all its options visible instead of guessed at through repeated taps.
        set(resolved(read()) === 'dark' ? 'light' : 'dark');
    });

    // The OS preference can change while the page is open (sunset, or a scheduled night mode). When
    // we're in 'system' mode we have to repaint the meta theme-color to match - the CSS follows on
    // its own, but that meta tag doesn't.
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
        if (read() === 'system') apply('system');
    });

    apply(read());

    // ---- Navigation feel ---------------------------------------------------------------------
    // In Blazor Server, tapping a nav link does nothing visible until a full round-trip completes:
    // the server re-renders the nav with its new `active` class and ships back a diff. On a phone over
    // Tailscale that is a beat of total silence after every tap, which is what makes the app feel
    // "stiff" - the user cannot tell whether the tap registered at all.
    //
    // Two cheap client-side fixes, neither of which touches application state:
    //   1. Move the active highlight IMMEDIATELY on tap. The server's own re-render arrives moments
    //      later and agrees with it, so this is pure optimism about something that is about to be true.
    //   2. Show a thin progress bar for as long as the navigation is in flight, so the wait is visibly
    //      the app working rather than the app ignoring you.
    let progressTimer = null;

    function startProgress() {
        document.documentElement.classList.add('nav-busy');
        clearTimeout(progressTimer);
        // Safety net: if the "finished" signal never arrives (dropped circuit, failed fetch), the bar
        // must not sit there spinning forever - a stuck progress bar is worse than none.
        progressTimer = setTimeout(stopProgress, 8000);
    }

    function stopProgress() {
        clearTimeout(progressTimer);
        document.documentElement.classList.remove('nav-busy');
    }

    document.addEventListener('click', function (e) {
        const link = e.target.closest('.tab-item[href], .sidebar-link[href]');
        if (!link || link.classList.contains('active')) return;

        document.querySelectorAll('.tab-item.active, .sidebar-link.active')
            .forEach(function (el) { el.classList.remove('active'); });
        link.classList.add('active');
        startProgress();
    });

    // Blazor's enhanced navigation finishes with this event. blazor.web.js may not have defined the
    // global by the time this file runs, so poll briefly rather than assuming it is there.
    (function hookBlazor(triesLeft) {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', stopProgress);
            return;
        }
        if (triesLeft > 0) setTimeout(function () { hookBlazor(triesLeft - 1); }, 100);
    })(50);

    // ---- Fullscreen screen view --------------------------------------------------------------
    // A remote 1920x1080 desktop inside a 360px-wide portrait phone is unreadable. Fullscreen +
    // landscape turns the whole phone into the screen, which is the only way looking at a student's
    // desktop from a phone is actually useful.
    //
    // Three levels of support, deliberately, because this is the least uniform API on mobile:
    //   - Android Chrome: real element fullscreen AND orientation lock. Both work.
    //   - iPadOS / desktop: element fullscreen works, orientation lock does not (throws) - caught.
    //   - iPhone Safari: element fullscreen does not exist at all (only <video> can go fullscreen), so
    //     fall back to a "faux fullscreen" - a fixed, full-viewport overlay. Visually identical for
    //     this purpose; the browser chrome just stays put.
    function stage() { return document.getElementById('screen-stage'); }

    function inFullscreen() {
        const el = stage();
        return !!document.fullscreenElement || !!(el && el.classList.contains('faux-fullscreen'));
    }

    async function enterFullscreen() {
        const el = stage();
        if (!el) return false;

        try {
            if (el.requestFullscreen) await el.requestFullscreen({ navigationUI: 'hide' });
            else if (el.webkitRequestFullscreen) el.webkitRequestFullscreen();
            else el.classList.add('faux-fullscreen');
        } catch (e) {
            el.classList.add('faux-fullscreen');
        }

        try {
            if (screen.orientation && screen.orientation.lock) await screen.orientation.lock('landscape');
        } catch (e) {
            // iOS and every desktop browser reject this. Not an error worth showing - the user can
            // simply turn the phone, and the page tells them so.
        }

        // noVNC re-fits its canvas on window resize (scaleViewport). The viewport really did change
        // size, but the event doesn't always fire on the faux path, so nudge it.
        window.dispatchEvent(new Event('resize'));
        return true;
    }

    async function exitFullscreen() {
        const el = stage();
        try { if (document.fullscreenElement) await document.exitFullscreen(); } catch (e) { /* already out */ }
        try { if (screen.orientation && screen.orientation.unlock) screen.orientation.unlock(); } catch (e) { /* not supported */ }
        if (el) el.classList.remove('faux-fullscreen');
        window.dispatchEvent(new Event('resize'));
    }

    // Leaving native fullscreen with the system gesture or Escape has to clean up the orientation lock
    // too, otherwise the phone stays stuck in landscape on the normal page.
    document.addEventListener('fullscreenchange', function () {
        if (!document.fullscreenElement) {
            try { if (screen.orientation && screen.orientation.unlock) screen.orientation.unlock(); } catch (e) { /* ignore */ }
            window.dispatchEvent(new Event('resize'));
        }
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && inFullscreen()) exitFullscreen();
    });

    window.onionScreen = {
        enter: enterFullscreen,
        exit: exitFullscreen,
        toggle: function () { return inFullscreen() ? exitFullscreen() : enterFullscreen(); },
        isFullscreen: inFullscreen
    };

    // Exposed for the Settings page, which IS an interactive component and can therefore offer an
    // explicit three-way choice through IJSRuntime rather than making users guess the cycle order.
    window.onionTheme = { get: read, set: set, resolved: function () { return resolved(read()); } };
})();
