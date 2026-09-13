"use strict";

// ==========================================================================
// AutoJMS License Dashboard — frontend logic
// ==========================================================================
// Vanilla, no framework, no build step: open the file and it runs. That is
// partly convenience and mostly policy — the server sends helmet()'s default
// Content-Security-Policy, whose script-src is 'self', so a Vue or Tailwind CDN
// tag would be blocked outright. Widening the policy to admit one would mean
// running third-party script on the page that holds the admin token, which is a
// bad trade for a table and a modal.
//
// Two consequences worth keeping in mind when editing:
//
//   1. Every handler is wired with addEventListener. script-src-attr is 'none',
//      so an onclick= attribute in index.html silently does nothing.
//   2. Everything that comes back from the API is written with textContent, not
//      innerHTML. The notes field is free text the owner types, and it is the
//      one value on this page that could carry markup.
// ==========================================================================

(() => {
    // ==========================================
    // CONSTANTS
    // ==========================================

    /**
     * The API lives beside this page, not at a hard-coded /api/admin: the two are
     * mounted from the same server.js, so if a reverse proxy ever puts the whole
     * app under a prefix, both move together and this keeps working.
     *
     *   /admin, /admin/, /admin/index.html  ->  /api/admin
     *   /tools/admin/index.html             ->  /tools/api/admin
     */
    const API_BASE = `${window.location.pathname.replace(/\/admin(?:\/.*)?$/, "")}/api/admin`;

    const TOKEN_STORAGE_KEY = "autojms.admin.token";
    const THEME_STORAGE_KEY = "autojms.admin.theme";

    /** Days below which a still-valid licence is shown in orange. */
    const EXPIRING_SOON_DAYS = 7;

    const TIERS = ["BASE", "ULTRA"];

    const STATUS_LABELS = {
        active: "Đang chạy",
        expiring: "Sắp hết hạn",
        grace: "Gia hạn tạm",
        expired: "Hết hạn",
        revoked: "Đã khoá",
        perpetual: "Vĩnh viễn"
    };

    // ==========================================
    // STATE
    // ==========================================

    const state = {
        token: "",
        licenses: [],
        search: "",
        tier: "all",
        status: "all",

        // Create-modal state. ULTRA is the default because it is what almost
        // every sale is; BASE is the deliberate downgrade, so it should cost a
        // click rather than be the thing you land on.
        createTier: "ULTRA",
        // The key shown in the preview box and sent to the server on submit.
        // The server re-validates it and refuses a taken one, so this is a
        // convenience, not a claim.
        candidateKey: "",

        // Edit-modal state.
        editKey: "",
        editTier: "ULTRA",
        // The record as the DETAIL route just returned it — not the table row.
        // Re-fetching on open is what stops a tab left open since this morning
        // from saving yesterday's values back over a key someone already fixed.
        editSnapshot: null,
        // Fleet defaults from the same response, so a blank override field can
        // say which number it inherits instead of just "mặc định".
        editDefaults: { graceDays: null, offlineGraceHours: null, seats: null, tokenVersion: 1, anchorDay: 16 }
    };

    const $ = id => document.getElementById(id);

    const dom = {
        lockScreen: $("lock-screen"),
        lockForm: $("lock-form"),
        lockError: $("lock-error"),
        lockSubmit: $("lock-submit"),
        tokenInput: $("token-input"),

        app: $("app"),
        themeToggle: $("theme-toggle"),
        themeIcon: $("theme-icon"),
        refreshButton: $("refresh-button"),
        lockButton: $("lock-button"),
        createButton: $("create-button"),

        statTotal: $("stat-total"),
        statActive: $("stat-active"),
        statExpiring: $("stat-expiring"),
        statExpired: $("stat-expired"),

        searchInput: $("search-input"),
        filterTier: $("filter-tier"),
        filterStatus: $("filter-status"),
        resultCount: $("result-count"),

        rows: $("license-rows"),
        emptyState: $("empty-state"),
        loadingState: $("loading-state"),

        createModal: $("create-modal"),
        createBackdrop: $("create-backdrop"),
        createForm: $("create-form"),
        createClose: $("create-close"),
        createCancel: $("create-cancel"),
        createSubmit: $("create-submit"),
        createError: $("create-error"),
        createMiddleCode: $("create-middle-code"),
        createTerms: $("create-terms"),
        createNotes: $("create-notes"),
        createSheetId: $("create-sheet-id"),
        createSkipHash: $("create-skip-hash"),
        createAutoUpdate: $("create-auto-update"),
        createSilentUpdate: $("create-silent-update"),
        createApplyStartup: $("create-apply-startup"),

        toggleBase: $("toggle-base"),
        toggleUltra: $("toggle-ultra"),
        keyDisplay: $("key-display"),
        btnGeneralKey: $("btn-general-key"),

        editModal: $("edit-modal"),
        editBackdrop: $("edit-backdrop"),
        editForm: $("edit-form"),
        editClose: $("edit-close"),
        editCancel: $("edit-cancel"),
        editSubmit: $("edit-submit"),
        editError: $("edit-error"),
        editToggleBase: $("edit-toggle-base"),
        editToggleUltra: $("edit-toggle-ultra"),
        editKeyDisplay: $("edit-key-display"),
        editCreatedAt: $("edit-created-at"),
        editCopyKey: $("edit-copy-key"),
        editMiddleCode: $("edit-middle-code"),
        editStatus: $("edit-status"),
        editExpiryCurrent: $("edit-expiry-current"),
        editExpiryMode: $("edit-expiry-mode"),
        editExpiryAnchorRow: $("edit-expiry-anchor-row"),
        editExpiryStart: $("edit-expiry-start"),
        editExpiryTerms: $("edit-expiry-terms"),
        editExpiryDateRow: $("edit-expiry-date-row"),
        editExpiryDate: $("edit-expiry-date"),
        editExpiryPreview: $("edit-expiry-preview"),
        editHwid: $("edit-hwid"),
        editUpdateChannel: $("edit-update-channel"),
        editSheetId: $("edit-sheet-id"),
        editNotes: $("edit-notes"),
        editSkipHash: $("edit-skip-hash"),
        editAutoUpdate: $("edit-auto-update"),
        editSilentUpdate: $("edit-silent-update"),
        editApplyStartup: $("edit-apply-startup"),
        editSeats: $("edit-seats"),
        editTokenVersion: $("edit-token-version"),
        editGraceDays: $("edit-grace-days"),
        editOfflineHours: $("edit-offline-hours"),
        editSiteCodes: $("edit-site-codes"),
        editSiteCode: $("edit-site-code"),
        editSiteId: $("edit-site-id"),

        confirmModal: $("confirm-modal"),
        confirmBackdrop: $("confirm-backdrop"),
        confirmClose: $("confirm-close"),
        confirmTitle: $("confirm-title"),
        confirmMessage: $("confirm-message"),
        confirmCancel: $("confirm-cancel"),
        confirmOk: $("confirm-ok"),

        toasts: $("toasts")
    };

    // ==========================================
    // DOM HELPERS
    // ==========================================

    function el(tag, props = {}, children = []) {
        const node = document.createElement(tag);

        for (const [name, value] of Object.entries(props)) {
            if (value === null || value === undefined || value === false) continue;
            if (name === "class") node.className = value;
            else if (name === "text") node.textContent = value;
            else node.setAttribute(name, value === true ? "" : String(value));
        }

        for (const child of [].concat(children)) {
            if (child === null || child === undefined || child === false) continue;
            node.append(child);
        }

        return node;
    }

    // Built node by node rather than through innerHTML: helmet's CSP is not what
    // stops innerHTML here (it does not), but every other builder on this page
    // is injection-proof by construction and this one has no reason not to be.
    // el() cannot do the job — SVG children need createElementNS or the browser
    // parses them as unknown HTML elements that render nothing.
    function createSvg(pathD, viewBox = "0 0 20 20", fill = "currentColor") {
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", viewBox);
        svg.setAttribute("width", "15");
        svg.setAttribute("height", "15");
        svg.setAttribute("fill", fill);
        // The button carries the name via title/aria-label; the drawing itself
        // must stay out of the accessibility tree or it gets announced twice.
        svg.setAttribute("aria-hidden", "true");
        const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
        path.setAttribute("d", pathD);
        // evenodd, not the browser default of nonzero. Several icons below cut a
        // shape out of a solid body — the tick inside the circle, the front sheet
        // inside the copy glyph — by drawing an inner subpath. Under nonzero an
        // inner subpath wound the same way as its outer one *adds* instead of
        // subtracting, so the tick silently disappears and the check button ships
        // as a plain green disc. Measured, not guessed: nonzero fills 50.3% of the
        // active icon's box (a full disc), evenodd 45.2% (disc minus tick).
        path.setAttribute("fill-rule", "evenodd");
        svg.appendChild(path);
        return svg;
    }

    function toast(kind, title, detail) {
        const node = el("div", { class: `toast toast--${kind}` }, [
            el("strong", { text: title }),
            detail ? el("span", { text: detail }) : null
        ]);

        dom.toasts.append(node);
        window.setTimeout(() => node.remove(), kind === "error" ? 7000 : 4000);
    }

    /**
     * Clipboard, with a fallback.
     *
     * navigator.clipboard only exists in a secure context — https, or localhost.
     * Someone reaching this console over a LAN address on plain http still needs
     * the copy button to work, because the whole point of minting a key is
     * pasting it into a chat window.
     */
    async function copyText(text) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch {
            // Fall through: a permission refusal is not a reason to give up.
        }

        try {
            const scratch = document.createElement("textarea");
            scratch.value = text;
            scratch.setAttribute("readonly", "");
            scratch.style.position = "fixed";
            scratch.style.opacity = "0";
            document.body.append(scratch);
            scratch.select();
            const copied = document.execCommand("copy");
            scratch.remove();
            return copied;
        } catch {
            return false;
        }
    }

    // ==========================================
    // THEME
    // ==========================================

    const THEME_ICONS = { auto: "◐", light: "☀", dark: "☾" };

    function applyTheme(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        dom.themeIcon.textContent = THEME_ICONS[theme] || THEME_ICONS.auto;
        dom.themeToggle.title = `Giao diện: ${theme === "auto" ? "theo hệ thống" : theme === "light" ? "sáng" : "tối"}`;
    }

    function initTheme() {
        let stored = "auto";
        try {
            stored = window.localStorage.getItem(THEME_STORAGE_KEY) || "auto";
        } catch {
            // Private browsing can refuse localStorage; the default is fine.
        }
        applyTheme(["auto", "light", "dark"].includes(stored) ? stored : "auto");
    }

    function cycleTheme() {
        const order = ["auto", "light", "dark"];
        const current = document.documentElement.getAttribute("data-theme") || "auto";
        const next = order[(order.indexOf(current) + 1) % order.length];

        applyTheme(next);
        try {
            window.localStorage.setItem(THEME_STORAGE_KEY, next);
        } catch {
            // Not worth telling the owner about; the choice just will not persist.
        }
    }

    // ==========================================
    // API
    // ==========================================

    async function api(method, path, body) {
        const response = await fetch(API_BASE + path, {
            method,
            headers: {
                "x-admin-token": state.token,
                ...(body === undefined ? {} : { "content-type": "application/json" })
            },
            body: body === undefined ? undefined : JSON.stringify(body)
        });

        let payload = null;
        try {
            payload = await response.json();
        } catch {
            payload = null;
        }

        if (response.ok) return payload;

        const error = new Error(payload?.message || `Máy chủ trả về HTTP ${response.status}.`);
        error.status = response.status;
        error.code = payload?.error || "";
        throw error;
    }

    /**
     * Turns an API failure into the one thing the owner should do next.
     *
     * The three cases that are not "try again" are worth separating: a 401 means
     * the token in this tab is wrong and the lock screen has to come back; a 503
     * ADMIN_API_DISABLED means the server has no ADMIN_SECRET_TOKEN configured, so
     * no token would work and saying "sai token" would send the owner hunting for
     * the wrong thing; a 429 means the guess limiter tripped and the only cure is
     * waiting.
     */
    function describeError(error) {
        if (error.code === "ADMIN_API_DISABLED") {
            return {
                fatal: true,
                title: "Admin API chưa được bật",
                detail: error.message
            };
        }

        if (error.status === 401) {
            return { fatal: true, title: "Sai Admin Token", detail: "Token trong tab này không đúng." };
        }

        if (error.status === 429) {
            return {
                fatal: false,
                title: "Bị tạm khoá",
                detail: error.message || "Quá nhiều yêu cầu, thử lại sau ít phút."
            };
        }

        return { fatal: false, title: "Không thực hiện được", detail: error.message };
    }

    // ==========================================
    // AUTH
    // ==========================================

    function readStoredToken() {
        try {
            return window.sessionStorage.getItem(TOKEN_STORAGE_KEY) || "";
        } catch {
            return "";
        }
    }

    function storeToken(token) {
        try {
            // sessionStorage, not localStorage: the token dies with the tab. An
            // admin token that survives a closed browser on a shared machine is a
            // credential left on the desk.
            window.sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
        } catch {
            // Storage refused; the token still works for this page view.
        }
    }

    function forgetToken() {
        try {
            window.sessionStorage.removeItem(TOKEN_STORAGE_KEY);
        } catch {
            // Nothing to clear.
        }
    }

    function showLock(message) {
        state.token = "";
        state.licenses = [];
        // Dropping the state is not enough on its own: the rows rendered from it
        // would stay in the DOM, so a locked tab would still be carrying the
        // whole customer list for anyone who opens devtools on it.
        dom.rows.replaceChildren();
        dom.app.hidden = true;
        dom.lockScreen.hidden = false;
        dom.tokenInput.value = "";

        if (message) {
            dom.lockError.textContent = message;
            dom.lockError.hidden = false;
        } else {
            dom.lockError.hidden = true;
        }

        dom.tokenInput.focus();
    }

    function showApp() {
        dom.lockScreen.hidden = true;
        dom.app.hidden = false;
    }

    async function unlock(token) {
        dom.lockSubmit.disabled = true;
        dom.lockSubmit.textContent = "Đang kiểm tra…";
        dom.lockError.hidden = true;

        state.token = token;

        try {
            // The list call IS the credential check — there is no separate login
            // endpoint, and adding one would only be a second thing to keep in
            // sync with requireAdminToken.
            const payload = await api("GET", "/licenses");
            storeToken(token);
            showApp();
            receive(payload);
        } catch (error) {
            const described = describeError(error);
            state.token = "";
            forgetToken();
            dom.lockError.textContent = `${described.title}. ${described.detail}`;
            dom.lockError.hidden = false;
        } finally {
            dom.lockSubmit.disabled = false;
            dom.lockSubmit.textContent = "Mở khoá";
        }
    }

    // ==========================================
    // LOADING
    // ==========================================

    function receive(payload) {
        state.licenses = Array.isArray(payload?.licenses) ? payload.licenses : [];
        dom.loadingState.hidden = true;
        render();
    }

    async function reload() {
        dom.refreshButton.disabled = true;

        try {
            receive(await api("GET", "/licenses"));
        } catch (error) {
            const described = describeError(error);
            if (described.fatal) {
                forgetToken();
                showLock(`${described.title}. ${described.detail}`);
                return;
            }
            toast("error", described.title, described.detail);
        } finally {
            dom.refreshButton.disabled = false;
        }
    }

    // ==========================================
    // DERIVED VIEW
    // ==========================================

    /** The bucket a record falls in, which drives both the badge and the filter. */
    function bucketOf(license) {
        const effective = String(license.effectiveStatus || "");

        if (effective === "expired") return "expired";
        if (effective === "grace") return "grace";

        if (effective === "active") {
            if (license.daysRemaining === null || license.daysRemaining === undefined) return "perpetual";
            if (license.daysRemaining <= EXPIRING_SOON_DAYS) return "expiring";
            return "active";
        }

        // Anything else is a status verify-license already refuses — revoked,
        // suspended, a typo someone left in Firebase. They are all "closed".
        return "revoked";
    }

    function matchesSearch(license, needle) {
        if (!needle) return true;
        return [license.key, license.middleCode, license.notes, license.hwid]
            .map(value => String(value || "").toLowerCase())
            .some(value => value.includes(needle));
    }

    function matchesStatus(bucket, wanted) {
        if (wanted === "all") return true;
        // "Đang chạy" is the umbrella the stat card counts: a key expiring in
        // three days is still running, and clicking the Active card should not
        // make it disappear.
        if (wanted === "active") return bucket === "active" || bucket === "expiring" || bucket === "perpetual";
        return bucket === wanted;
    }

    function visibleLicenses() {
        const needle = state.search.trim().toLowerCase();

        return state.licenses.filter(license => {
            if (state.tier !== "all" && String(license.tier || "").toUpperCase() !== state.tier) return false;
            if (!matchesStatus(bucketOf(license), state.status)) return false;
            return matchesSearch(license, needle);
        });
    }

    function renderStats() {
        const buckets = state.licenses.map(bucketOf);
        const count = predicate => buckets.filter(predicate).length;

        dom.statTotal.textContent = String(state.licenses.length);
        dom.statActive.textContent = String(
            count(bucket => bucket === "active" || bucket === "expiring" || bucket === "perpetual")
        );
        // Grace is past the expiry date but still running, so it belongs under
        // "needs attention now" rather than under "dead".
        dom.statExpiring.textContent = String(count(bucket => bucket === "expiring" || bucket === "grace"));
        dom.statExpired.textContent = String(count(bucket => bucket === "expired"));

        for (const card of document.querySelectorAll("[data-filter-status]")) {
            card.setAttribute("aria-pressed", String(card.dataset.filterStatus === state.status));
        }
    }

    // ==========================================
    // FORMATTING
    // ==========================================

    /**
     * "2026-10-16T00:00:00+07:00" -> "16/10/2026".
     *
     * Read off the string rather than through Date, on purpose: the stored value
     * already carries +07:00, and parsing it into a Date would re-render it in
     * the browser's timezone — an owner in VN would still see the 16th, but a
     * laptop left on UTC would show the 15th and the anchor rule would look broken.
     */
    function formatDate(iso) {
        const matched = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso || ""));
        return matched ? `${matched[3]}/${matched[2]}/${matched[1]}` : "";
    }

    function daysCell(license, bucket) {
        if (license.daysRemaining === null || license.daysRemaining === undefined) {
            return el("span", { class: "cell-days cell-days--none", text: bucket === "revoked" ? "—" : "∞" });
        }

        const days = Number(license.daysRemaining);
        if (days < 0) {
            return el("span", { class: "cell-days cell-days--danger", text: `quá ${Math.abs(days)} ngày` });
        }

        const tone = days <= EXPIRING_SOON_DAYS ? " cell-days--warn" : "";
        return el("span", { class: `cell-days${tone}`, text: `${days} ngày` });
    }

    // ==========================================
    // TABLE
    // ==========================================

    // Solid single-path shapes on a 20x20 grid. Vectors rather than emoji so the
    // row looks the same on Windows, macOS and Android instead of inheriting
    // whatever each OS decided a padlock looks like this year.
    const ICON = Object.freeze({
        // Two subpaths that do not overlap — the eraser cap and the body — so
        // evenodd fills both rather than cutting one out of the other.
        edit: "M13.586 3.586a2 2 0 112.828 2.828l-.793.793-2.828-2.828.793-.793zM11.379 5.793L3 14.172V17h2.828l8.38-8.379-2.83-2.828z",
        copy: "M7 2a2 2 0 00-2 2v1H4a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-1h1a2 2 0 002-2V7a2 2 0 00-2-2h-1V4a2 2 0 00-2-2H7zm0 2h6v2H7V4zm-3 5h10v8H4V9zm12 0v5h1V7h-5v2h4z",
        reset: "M4 2a1 1 0 011 1v2.101a7.002 7.002 0 0111.601 2.566 1 1 0 11-1.885.666A5.002 5.002 0 005.999 7H9a1 1 0 010 2H4a1 1 0 01-1-1V3a1 1 0 011-1zm.008 9.047a1 1 0 011.885-.666A5.002 5.002 0 0014.001 13H11a1 1 0 110-2h5a1 1 0 011 1v5a1 1 0 11-2 0v-2.101a7.002 7.002 0 01-11.601-2.566 1 1 0 01.608-1.286z",
        tierUp: "M10 3a1 1 0 01.707.293l5 5a1 1 0 01-1.414 1.414L11 6.414V16a1 1 0 11-2 0V6.414L5.707 9.707a1 1 0 01-1.414-1.414l5-5A1 1 0 0110 3z",
        tierDown: "M10 17a1 1 0 01-.707-.293l-5-5a1 1 0 011.414-1.414L9 13.586V4a1 1 0 112 0v9.586l3.293-3.293a1 1 0 011.414 1.414l-5 5A1 1 0 0110 17z",
        ban: "M10 18a8 8 0 100-16 8 8 0 000 16zM5.47 13.12A5.5 5.5 0 0113.12 5.47zM14.53 6.88A5.5 5.5 0 016.88 14.53z",
        active: "M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z"
    });

    function buildRow(license) {
        const bucket = bucketOf(license);
        const tier = String(license.tier || "BASE").toUpperCase();
        const otherTier = TIERS.find(candidate => candidate !== tier) || "BASE";
        const isClosed = bucket === "revoked";

        // The dot answers "is this key alive?" without reading the status badge
        // three columns away. It is decorative in the strict sense — the badge
        // already says the same thing in words — so it only carries a tooltip.
        const keyCell = el("div", { class: "cell-key" }, [
            el("span", {
                class: isClosed ? "status-dot status-dot--banned" : "status-dot status-dot--active",
                title: isClosed ? "Đã bị khoá (Banned)" : "Đang hoạt động (Active)"
            }),
            el("span", { class: "cell-key__text", text: license.key })
        ]);

        const notes = String(license.notes || "").trim();

        return el("tr", { "data-row-key": license.key }, [
            el("td", { "data-label": "Mã bưu cục" }, [
                el("span", { class: "cell-code", text: license.middleCode || "—" })
            ]),

            el("td", { "data-label": "Tier" }, [
                el("span", { class: `badge badge--${tier.toLowerCase()}`, text: tier })
            ]),

            el("td", { "data-label": "License key" }, [
                keyCell,
                notes ? el("span", { class: "cell-note", text: notes }) : null
            ]),

            el("td", { "data-label": "Hạn dùng" }, [
                license.expiresAt
                    ? el("span", { class: "cell-date", text: formatDate(license.expiresAt) })
                    : el("span", { class: "cell-date cell-date--none", text: "Vĩnh viễn" })
            ]),

            el("td", { "data-label": "Còn lại" }, [daysCell(license, bucket)]),

            el("td", { "data-label": "Trạng thái" }, [
                el("span", {
                    class: `badge badge--${bucket === "expiring" ? "grace" : bucket}`,
                    text: STATUS_LABELS[bucket] || bucket
                })
            ]),

            // Every action sits on the row itself: one tap each, no dropdown to
            // open first. Only the renewal keeps a word label; the rest are SVG
            // glyphs, so each carries both title (tooltip on hover) and
            // aria-label (the button's real name — the drawing is aria-hidden,
            // so without it a screen reader reads an unnamed button).
            el("td", { "data-label": "Thao tác" }, [
                el("div", { class: "cell-actions" }, [
                    // First in the row because it is the only button here that
                    // opens something instead of doing something: the other five
                    // commit a change on one click, this one asks first.
                    el("button", {
                        class: "btn--icon-action",
                        type: "button",
                        "data-action": "edit",
                        "data-key": license.key,
                        title: "Sửa license (toàn bộ thuộc tính)",
                        "aria-label": "Sửa license (toàn bộ thuộc tính)"
                    }, [createSvg(ICON.edit)]),
                    el("button", {
                        class: "btn btn--mini",
                        type: "button",
                        "data-action": "extend-1",
                        "data-key": license.key,
                        title: "Gia hạn thêm 1 tháng, neo ngày 16",
                        text: "+1 Tháng"
                    }),
                    el("button", {
                        class: "btn--icon-action",
                        type: "button",
                        "data-action": "copy",
                        "data-key": license.key,
                        title: "Copy license key",
                        "aria-label": "Copy license key"
                    }, [createSvg(ICON.copy)]),
                    el("button", {
                        class: "btn--icon-action",
                        type: "button",
                        "data-action": "unbind",
                        "data-key": license.key,
                        title: "Reset HWID (đổi máy)",
                        "aria-label": "Reset HWID (đổi máy)"
                    }, [createSvg(ICON.reset)]),
                    // Arrow points the way the change goes, so the direction is
                    // readable before the tooltip appears.
                    tier === "BASE"
                        ? el("button", {
                            class: "btn--icon-action btn--icon-action--upgrade",
                            type: "button",
                            "data-action": "tier",
                            "data-key": license.key,
                            "data-tier": otherTier,
                            title: "Nâng cấp lên ULTRA",
                            "aria-label": "Nâng cấp lên ULTRA"
                        }, [createSvg(ICON.tierUp)])
                        : el("button", {
                            class: "btn--icon-action btn--icon-action--downgrade",
                            type: "button",
                            "data-action": "tier",
                            "data-key": license.key,
                            "data-tier": otherTier,
                            title: "Hạ cấp xuống BASE",
                            "aria-label": "Hạ cấp xuống BASE"
                        }, [createSvg(ICON.tierDown)]),
                    // data-closed is what onRowClick reads to pick the confirm
                    // wording. It has to be an attribute, not the label: there
                    // is no text on this button to read the direction from.
                    isClosed
                        ? el("button", {
                            class: "btn--icon-action btn--icon-action--unban",
                            type: "button",
                            "data-action": "toggle",
                            "data-key": license.key,
                            "data-closed": "true",
                            title: "Mở khóa license (Active)",
                            "aria-label": "Mở khóa license (Active)"
                        }, [createSvg(ICON.active)])
                        : el("button", {
                            class: "btn--icon-action btn--icon-action--ban",
                            type: "button",
                            "data-action": "toggle",
                            "data-key": license.key,
                            "data-closed": "false",
                            title: "Khóa license (Ban)",
                            "aria-label": "Khóa license (Ban)"
                        }, [createSvg(ICON.ban)])
                ])
            ])
        ]);
    }

    function render() {
        renderStats();

        const visible = visibleLicenses();

        dom.rows.replaceChildren(...visible.map(buildRow));

        dom.resultCount.textContent = state.licenses.length
            ? `Hiển thị ${visible.length}/${state.licenses.length} key`
            : "";

        if (state.licenses.length === 0) {
            dom.emptyState.textContent = 'Chưa có license nào. Bấm "Cấp License Mới" để tạo key đầu tiên.';
            dom.emptyState.hidden = false;
        } else if (visible.length === 0) {
            dom.emptyState.textContent = "Không có key nào khớp bộ lọc hiện tại.";
            dom.emptyState.hidden = false;
        } else {
            dom.emptyState.hidden = true;
        }
    }

    // ==========================================
    // ROW ACTIONS
    // ==========================================

    async function runAction(action, key, dataset) {
        const row = dom.rows.querySelector(`tr[data-row-key="${CSS.escape(key)}"]`);
        if (row) row.classList.add("is-busy");

        try {
            if (action === "extend-1") {
                const terms = 1;
                const result = await api("POST", `/licenses/${encodeURIComponent(key)}/extend`, { terms });
                toast(
                    "ok",
                    "Đã gia hạn 1 tháng",
                    `${key} → ${formatDate(result.expiresAt)}`
                );
            } else if (action === "toggle") {
                const result = await api("POST", `/licenses/${encodeURIComponent(key)}/toggle-status`);
                toast(
                    result.status === "active" ? "ok" : "info",
                    result.status === "active" ? "Đã mở khoá" : "Đã khoá key",
                    key
                );
            } else if (action === "unbind") {
                await api("POST", `/licenses/${encodeURIComponent(key)}/unbind-hwid`);
                toast("ok", "Đã reset HWID", `${key} có thể kích hoạt trên máy mới.`);
            } else if (action === "tier") {
                const result = await api("POST", `/licenses/${encodeURIComponent(key)}/tier`, {
                    tier: dataset.tier
                });
                toast("ok", `Đã đổi sang ${result.tier}`, key);
            }

            await reload();
        } catch (error) {
            const described = describeError(error);
            if (described.fatal) {
                forgetToken();
                showLock(`${described.title}. ${described.detail}`);
                return;
            }
            toast("error", described.title, described.detail);
            if (row) row.classList.remove("is-busy");
        }
    }

    /**
     * The in-app replacement for window.confirm().
     *
     * Same contract — resolves true only when the owner actively agreed — but the
     * page keeps running while it is open, and the box follows the theme instead
     * of looking like a different piece of software on every browser.
     *
     * Esc, the ✕ and the backdrop all cancel, and focus returns to whatever
     * opened the dialog. None of that is polish: window.confirm did all three for
     * free, so a replacement without them is a downgrade.
     *
     * @param {{title: string, message: string, okText?: string, cancelText?: string, danger?: boolean}} options
     * @returns {Promise<boolean>} true only on an explicit confirm.
     */
    function confirmAction({ title, message, okText = "Xác nhận", cancelText = "Huỷ", danger = false }) {
        // A second dialog on top of the first would overwrite the text and hang a
        // second listener on the same OK button — one click would resolve both
        // promises and fire the API twice. Refuse rather than stack.
        if (!dom.confirmModal.hidden) return Promise.resolve(false);

        return new Promise(resolve => {
            const opener = document.activeElement;

            dom.confirmTitle.textContent = title;
            dom.confirmMessage.textContent = message;
            dom.confirmOk.textContent = okText;
            dom.confirmOk.className = danger ? "btn btn--danger" : "btn btn--primary";
            dom.confirmCancel.textContent = cancelText;

            const cleanup = confirmed => {
                dom.confirmModal.hidden = true;
                dom.confirmOk.removeEventListener("click", onOk);
                dom.confirmCancel.removeEventListener("click", onCancel);
                dom.confirmClose.removeEventListener("click", onCancel);
                dom.confirmBackdrop.removeEventListener("click", onCancel);
                document.removeEventListener("keydown", onKey);
                if (opener && typeof opener.focus === "function") opener.focus();
                resolve(confirmed);
            };
            const onOk = () => cleanup(true);
            const onCancel = () => cleanup(false);
            const onKey = event => {
                if (event.key === "Escape") cleanup(false);
            };

            dom.confirmOk.addEventListener("click", onOk);
            dom.confirmCancel.addEventListener("click", onCancel);
            dom.confirmClose.addEventListener("click", onCancel);
            dom.confirmBackdrop.addEventListener("click", onCancel);
            document.addEventListener("keydown", onKey);

            dom.confirmModal.hidden = false;
            // The destructive dialog focuses Huỷ. With focus on OK, one reflex
            // Enter — the owner was typing in the search box a second ago — bans a
            // paying customer's key, which is the exact accident this dialog is
            // here to prevent.
            (danger ? dom.confirmCancel : dom.confirmOk).focus();
        });
    }

    async function onRowClick(event) {
        const trigger = event.target.closest("[data-action]");
        if (!trigger) return;

        const { action, key } = trigger.dataset;
        if (!action || !key) return;

        if (action === "copy") {
            const copied = await copyText(key);
            toast(copied ? "ok" : "error", copied ? "Đã copy key" : "Không copy được", key);
            return;
        }

        // Opens a form; nothing is written until the owner submits it, so it
        // skips both the confirm chain and runAction entirely.
        if (action === "edit") {
            await openEdit(key);
            return;
        }

        // Everything past this point changes who can run the software, and every
        // button in the column is a 30px square with no text on it — there is
        // nothing to read on the way to clicking one. Only +1 Tháng goes straight
        // through: it is additive, so the worst case is a free month.
        if (action === "unbind") {
            const ok = await confirmAction({
                title: "Xác nhận Reset HWID",
                message: `Xác nhận reset HWID cho key ${key}?\nKey sẽ được giải phóng để liên kết với máy trạm kích hoạt tiếp theo.`,
                okText: "Reset HWID"
            });
            if (!ok) return;
        }

        if (action === "tier") {
            const nextTier = trigger.dataset.tier;
            const ok = await confirmAction({
                title: `Xác nhận Đổi sang ${nextTier}`,
                message: `Xác nhận chuyển license ${key} sang gói ${nextTier}?`,
                okText: `Đổi sang ${nextTier}`
            });
            if (!ok) return;
        }

        if (action === "toggle") {
            const isUnlocking = trigger.dataset.closed === "true";
            const ok = isUnlocking
                ? await confirmAction({
                    title: "Xác nhận Mở Khóa (UNBAN)",
                    message: `Xác nhận MỞ KHÓA (Active) license ${key}?\nMáy trạm sẽ có thể kích hoạt và hoạt động bình thường.`,
                    okText: "Mở Khóa"
                })
                : await confirmAction({
                    title: "Xác nhận Khóa License (BAN)",
                    message: `Xác nhận KHÓA (Ban) license ${key}?\nMáy trạm đang dùng key này sẽ bị ngắt quyền truy cập ngay lập tức.`,
                    okText: "Khóa License",
                    danger: true
                });
            if (!ok) return;
        }

        await runAction(action, key, trigger.dataset);
    }

    // ==========================================
    // CREATE
    // ==========================================

    /** Mirrors admin-routes.js's KEY_ALPHABET: every character is Firebase-safe. */
    const KEY_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    const KEY_GROUP_LENGTH = 4;

    /** Shown in the preview before the owner has typed a post-office code. */
    const MIDDLE_CODE_PLACEHOLDER = "0000";

    /**
     * Four characters of KEY_ALPHABET, drawn without modulo bias.
     *
     * 256 is not a multiple of 36, so `byte % 36` would favour A-T over U-9 by
     * about 14%. Bytes at or above 252 (the largest multiple of 36 that fits)
     * are redrawn instead. This only produces a candidate — the server mints
     * the real thing and refuses a collision — but a key generator that leans
     * on some letters is not worth shipping when the fix is one comparison.
     */
    function randomKeyGroup() {
        let group = "";
        while (group.length < KEY_GROUP_LENGTH) {
            const bytes = new Uint8Array(KEY_GROUP_LENGTH);
            window.crypto.getRandomValues(bytes);
            for (const byte of bytes) {
                if (group.length === KEY_GROUP_LENGTH) break;
                if (byte >= 252) continue;
                group += KEY_ALPHABET[byte % KEY_ALPHABET.length];
            }
        }
        return group;
    }

    function currentMiddleCode() {
        return dom.createMiddleCode.value.trim().toUpperCase() || MIDDLE_CODE_PLACEHOLDER;
    }

    function paintCandidateKey() {
        dom.keyDisplay.textContent = state.candidateKey;
    }

    /** New random outer groups, current middle code, straight onto the screen. */
    function generateRandomKey() {
        state.candidateKey = `${randomKeyGroup()}-${currentMiddleCode()}-${randomKeyGroup()}`;
        paintCandidateKey();
    }

    /**
     * Re-middles the candidate without re-rolling it.
     *
     * Typing "214A03" is six input events. Regenerating on each one would make
     * the two random groups flicker through six values while the owner is
     * reading the code they just typed, so only the middle segment moves.
     */
    function syncCandidateMiddle() {
        if (!state.candidateKey) {
            generateRandomKey();
            return;
        }

        const groups = state.candidateKey.split("-");
        state.candidateKey = `${groups[0]}-${currentMiddleCode()}-${groups[groups.length - 1]}`;
        paintCandidateKey();
    }

    /**
     * Flips the modal between the two packages.
     *
     * Everything tier-dependent is set here rather than in the click handlers,
     * so opening the modal and clicking a pill go through exactly one code
     * path and cannot drift apart.
     */
    function switchCreateTier(tier) {
        const isUltra = tier === "ULTRA";
        state.createTier = isUltra ? "ULTRA" : "BASE";

        dom.toggleUltra.className = isUltra ? "toggle-btn active-ultra" : "toggle-btn";
        dom.toggleBase.className = isUltra ? "toggle-btn" : "toggle-btn active-base";
        dom.toggleUltra.setAttribute("aria-pressed", String(isUltra));
        dom.toggleBase.setAttribute("aria-pressed", String(!isUltra));

        dom.createSubmit.className = isUltra
            ? "btn btn--primary btn-submit btn-ultra"
            : "btn btn--primary btn-submit";
        dom.createSubmit.textContent = isUltra ? "Tạo License ULTRA" : "Tạo License BASE";
    }

    function openCreate() {
        dom.createError.hidden = true;
        // reset() restores every checkbox to the `checked` in index.html, which
        // is all four of them — the fleet's default policy.
        dom.createForm.reset();
        switchCreateTier("ULTRA");
        generateRandomKey();
        dom.createModal.hidden = false;
        dom.createMiddleCode.focus();
    }

    function closeCreate() {
        dom.createModal.hidden = true;
    }

    async function submitCreate(event) {
        event.preventDefault();

        const middleCode = dom.createMiddleCode.value.trim().toUpperCase();
        const tier = state.createTier;
        const terms = Number(dom.createTerms.value);

        // The middle code may have changed after the last General Key press, so
        // the key is re-middled one final time rather than sent as displayed.
        syncCandidateMiddle();

        dom.createError.hidden = true;
        dom.createSubmit.disabled = true;
        const submitLabel = dom.createSubmit.textContent;
        dom.createSubmit.textContent = "Đang tạo…";

        try {
            const result = await api("POST", "/licenses/create", {
                key: state.candidateKey,
                middleCode,
                tier,
                terms,
                notes: dom.createNotes.value,
                // Sent for both gói. Google Sheet is not an ULTRA feature — the
                // token broker checks status and expiry only, and BASE loses
                // exactly four background capabilities, none of them Sheets.
                dataSpreadsheetId: dom.createSheetId.value.trim(),
                skipHashCheck: dom.createSkipHash.checked,
                modulePolicy: {
                    autoUpdate: dom.createAutoUpdate.checked,
                    silentUpdate: dom.createSilentUpdate.checked,
                    applyOnNextStartup: dom.createApplyStartup.checked
                }
            });

            closeCreate();

            const copied = await copyText(result.key);
            toast(
                "ok",
                copied ? "Đã tạo key và copy vào clipboard" : "Đã tạo key",
                `${result.key}${result.license.expiresAt ? ` · hạn ${formatDate(result.license.expiresAt)}` : " · vĩnh viễn"}`
            );

            await reload();
        } catch (error) {
            const described = describeError(error);
            if (described.fatal) {
                closeCreate();
                forgetToken();
                showLock(`${described.title}. ${described.detail}`);
                return;
            }
            dom.createError.textContent = described.detail;
            dom.createError.hidden = false;
            // A refused key is the one error the owner can clear without
            // reading anything: roll another and the Tạo button works.
            if (error.code === "LICENSE_KEY_TAKEN") generateRandomKey();
        } finally {
            dom.createSubmit.disabled = false;
            // Restored, not hard-coded: the label is "Tạo License ULTRA" or
            // "Tạo License BASE" depending on the pill, and writing either one
            // here would silently override the toggle after a failed attempt.
            dom.createSubmit.textContent = submitLabel;
        }
    }

    // ==========================================
    // EDIT
    // ==========================================
    //
    // The pencil in each row opens this. Unlike the five single-purpose buttons
    // beside it, it can move every field the licence server reads — including
    // the DataHub group (siteCodes / siteCode / siteId / seats / tokenVersion)
    // that the create modal never exposed, so until now the only way to fix one
    // was the Firebase console.
    //
    // Three rules the form obeys, all of them from the route it posts to:
    //
    //   1. The key is read-only. It is the Firebase node id, so renaming it
    //      means a new node plus a delete, stranding every station holding the
    //      old string. Issue a new key and revoke the old one instead.
    //   2. A blank override means "inherit the fleet default", which is NOT the
    //      same as typing today's default number — the typed one stops tracking
    //      when the fleet default moves. Hence the placeholders.
    //   3. Hạn dùng defaults to "giữ nguyên". The form posts every field on
    //      every save, so any other default would let a one-word notes fix
    //      quietly shift a paying customer's renewal date.

    /** Asia/Ho_Chi_Minh is a fixed +07:00 — same constant as license-expiry.js. */
    const VN_OFFSET_MS = 420 * 60_000;
    const MS_PER_DAY = 86_400_000;

    function vnPartsOf(ms) {
        const shifted = new Date(ms + VN_OFFSET_MS);
        return { year: shifted.getUTCFullYear(), month: shifted.getUTCMonth(), day: shifted.getUTCDate() };
    }

    /** `month` may overflow (12 -> January of year+1), which is what makes "next anchor" one line. */
    function vnMidnightMs(year, month, day) {
        return Date.UTC(year, month, day, 0, 0, 0, 0) - VN_OFFSET_MS;
    }

    /** "YYYY-MM-DD" (what <input type="date"> gives) -> VN midnight in epoch ms. */
    function vnDateToMs(value) {
        const matched = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value || ""));
        if (!matched) return null;
        return vnMidnightMs(Number(matched[1]), Number(matched[2]) - 1, Number(matched[3]));
    }

    /** epoch ms -> "YYYY-MM-DD", the value shape <input type="date"> wants back. */
    function msToVnDate(ms) {
        const p = vnPartsOf(ms);
        const pad = n => String(n).padStart(2, "0");
        return `${p.year}-${pad(p.month + 1)}-${pad(p.day)}`;
    }

    /**
     * Mirrors computeExpiry() from license-expiry.js, for the preview line only.
     *
     * The server recomputes this on save and its answer is the one that gets
     * stored — this exists so the owner can see what "3 tháng from the 20th"
     * lands on before committing, rather than saving and reading the toast.
     * Kept deliberately identical, including the `< floorMs` comparison: a
     * preview that disagrees with the server by one day is worse than no
     * preview, because it would look like the save went wrong.
     */
    function previewAnchorMs(startMs, terms) {
        const anchorDay = Number(state.editDefaults.anchorDay) || 16;
        // terms 0 is "tới ngày 16 gần nhất": one anchor step, no 30-day floor.
        const minTermDays = terms === 0 ? 0 : 30;
        const rounds = terms === 0 ? 1 : terms;

        const start = vnPartsOf(startMs);
        const floorMs = vnMidnightMs(start.year, start.month, start.day) + minTermDays * MS_PER_DAY;

        const floor = vnPartsOf(floorMs);
        let expiresAtMs = vnMidnightMs(floor.year, floor.month, anchorDay);
        if (expiresAtMs < floorMs) {
            expiresAtMs = vnMidnightMs(floor.year, floor.month + 1, anchorDay);
        }

        for (let round = 1; round < rounds; round += 1) {
            const at = vnPartsOf(expiresAtMs);
            expiresAtMs = vnMidnightMs(at.year, at.month + 1, anchorDay);
        }

        return expiresAtMs;
    }

    function switchEditTier(tier) {
        const isUltra = String(tier || "").toUpperCase() === "ULTRA";
        state.editTier = isUltra ? "ULTRA" : "BASE";

        dom.editToggleUltra.className = isUltra ? "toggle-btn active-ultra" : "toggle-btn";
        dom.editToggleBase.className = isUltra ? "toggle-btn" : "toggle-btn active-base";
        dom.editToggleUltra.setAttribute("aria-pressed", String(isUltra));
        dom.editToggleBase.setAttribute("aria-pressed", String(!isUltra));
    }

    /** Shows the row the chosen mode needs, and the date that mode will produce. */
    function syncExpiryRows() {
        const mode = dom.editExpiryMode.value;

        dom.editExpiryAnchorRow.hidden = mode !== "anchor";
        dom.editExpiryDateRow.hidden = mode !== "date";

        let preview = "";

        if (mode === "anchor") {
            const startMs = dom.editExpiryStart.value ? vnDateToMs(dom.editExpiryStart.value) : Date.now();
            if (startMs !== null) {
                const expiry = previewAnchorMs(startMs, Number(dom.editExpiryTerms.value));
                preview = `→ Hết hạn 00:00 ngày ${formatDate(msToVnDate(expiry))} (máy chủ tính lại khi lưu).`;
            }
        } else if (mode === "date") {
            const ms = vnDateToMs(dom.editExpiryDate.value);
            if (ms !== null) preview = `→ Hết hạn 00:00 ngày ${formatDate(msToVnDate(ms))}.`;
        } else if (mode === "perpetual") {
            preview = "→ Xoá hạn: key chạy vĩnh viễn cho tới khi bị khoá.";
        }

        dom.editExpiryPreview.textContent = preview;
        dom.editExpiryPreview.hidden = !preview;
    }

    /**
     * A status neither active nor revoked — an older "suspended", or a typo
     * someone left in the console — gets a throwaway option so the select opens
     * on what is really stored. Without it the box would open reading "active",
     * and saving would put a deliberately-stopped key back to work. The payload
     * builder drops the field entirely while that option is the one selected.
     */
    function paintStatusOptions(status) {
        const stored = String(status || "").trim().toLowerCase();
        const foreign = dom.editStatus.querySelector("option[data-foreign]");
        if (foreign) foreign.remove();

        if (stored !== "active" && stored !== "revoked") {
            dom.editStatus.append(
                el("option", { value: stored, "data-foreign": "true", text: `${status} (giữ nguyên)` })
            );
        }

        dom.editStatus.value = stored;
    }

    function fillEditForm(license, defaults) {
        const numberOrBlank = value => (value === null || value === undefined ? "" : String(value));
        const placeholder = value => (value === null || value === undefined ? "mặc định" : `mặc định (${value})`);

        dom.editKeyDisplay.textContent = license.key;
        dom.editCreatedAt.textContent = license.createdAt ? `Ngày tạo: ${license.createdAt}` : "";
        dom.editCreatedAt.hidden = !license.createdAt;

        switchEditTier(license.tier);
        dom.editMiddleCode.value = license.middleCode;
        paintStatusOptions(license.status);

        dom.editExpiryCurrent.textContent = license.expiresAt
            ? `Hiện tại: hết hạn ${formatDate(license.expiresAt)}` +
              (license.daysRemaining === null ? "" : ` · còn ${license.daysRemaining} ngày`)
            : "Hiện tại: vĩnh viễn, không có ngày đáo hạn.";

        dom.editExpiryMode.value = "keep";
        dom.editExpiryStart.value = msToVnDate(Date.now());
        dom.editExpiryTerms.value = "1";
        // Opens on the date already stored, so "dời thêm hai ngày" is two
        // keystrokes rather than typing the whole date from scratch.
        dom.editExpiryDate.value = license.expiresAt ? String(license.expiresAt).slice(0, 10) : msToVnDate(Date.now());
        syncExpiryRows();

        dom.editHwid.value = license.hwid;
        dom.editUpdateChannel.value = license.updateChannel || "";
        dom.editSheetId.value = license.dataSpreadsheetId;
        dom.editNotes.value = license.notes;

        dom.editSkipHash.checked = license.skipHashCheck === true;
        dom.editAutoUpdate.checked = license.modulePolicy.autoUpdate === true;
        dom.editSilentUpdate.checked = license.modulePolicy.silentUpdate === true;
        dom.editApplyStartup.checked = license.modulePolicy.applyOnNextStartup === true;

        dom.editSeats.value = numberOrBlank(license.seats);
        dom.editTokenVersion.value = numberOrBlank(license.tokenVersion);
        dom.editGraceDays.value = numberOrBlank(license.graceDays);
        dom.editOfflineHours.value = numberOrBlank(license.offlineGraceHours);

        dom.editSeats.placeholder = placeholder(defaults.seats);
        dom.editTokenVersion.placeholder = placeholder(defaults.tokenVersion);
        dom.editGraceDays.placeholder = placeholder(defaults.graceDays);
        dom.editOfflineHours.placeholder = placeholder(defaults.offlineGraceHours);

        dom.editSiteCodes.value = (license.siteCodes || []).join("\n");
        dom.editSiteCodes.placeholder = `Bỏ trống = dùng mã bưu cục (${license.middleCode || "—"})`;
        dom.editSiteCode.value = license.siteCode;
        dom.editSiteId.value = license.siteId;
    }

    async function openEdit(key) {
        if (!dom.editModal.hidden) return;

        const row = dom.rows.querySelector(`tr[data-row-key="${CSS.escape(key)}"]`);
        if (row) row.classList.add("is-busy");

        try {
            // Fetched, never read off the cached row: a tab left open since this
            // morning would otherwise post stale values back over a record
            // someone already corrected, and every field here is one this route
            // is allowed to write.
            const payload = await api("GET", `/licenses/${encodeURIComponent(key)}`);

            state.editKey = payload.license.key;
            state.editSnapshot = payload.license;
            if (payload.defaults && typeof payload.defaults === "object") state.editDefaults = payload.defaults;

            dom.editError.hidden = true;
            fillEditForm(payload.license, state.editDefaults);

            dom.editModal.hidden = false;
            dom.editMiddleCode.focus();
        } catch (error) {
            const described = describeError(error);
            if (described.fatal) {
                forgetToken();
                showLock(`${described.title}. ${described.detail}`);
                return;
            }
            toast("error", described.title, described.detail);
        } finally {
            if (row) row.classList.remove("is-busy");
        }
    }

    function closeEdit() {
        dom.editModal.hidden = true;
        state.editKey = "";
        state.editSnapshot = null;
    }

    /**
     * The form as the update route wants it.
     *
     * Every field is sent on every save — the route diffs against what is stored
     * and writes only what actually moved, so posting the whole form costs one
     * comparison per field and keeps this builder free of its own change
     * tracking. The one field deliberately omitted is a foreign status, which
     * the route would refuse outright; omitting it is how PATCH spells
     * "leave it alone".
     */
    function collectEditPayload() {
        const stored = String(state.editSnapshot?.status || "").trim().toLowerCase();
        const chosen = dom.editStatus.value;
        const statusIsForeign = chosen === stored && chosen !== "active" && chosen !== "revoked";

        const expiryMode = dom.editExpiryMode.value;
        const expiry =
            expiryMode === "anchor"
                ? { mode: "anchor", startAt: dom.editExpiryStart.value, terms: Number(dom.editExpiryTerms.value) }
                : expiryMode === "date"
                    ? { mode: "date", date: dom.editExpiryDate.value }
                    : { mode: expiryMode };

        return {
            middleCode: dom.editMiddleCode.value.trim().toUpperCase(),
            tier: state.editTier,
            ...(statusIsForeign ? {} : { status: chosen }),
            hwid: dom.editHwid.value.trim(),
            notes: dom.editNotes.value,
            dataSpreadsheetId: dom.editSheetId.value.trim(),
            updateChannel: dom.editUpdateChannel.value,
            skipHashCheck: dom.editSkipHash.checked,
            modulePolicy: {
                autoUpdate: dom.editAutoUpdate.checked,
                silentUpdate: dom.editSilentUpdate.checked,
                applyOnNextStartup: dom.editApplyStartup.checked
            },
            // "" is the wire spelling of "bỏ trống": the route reads it as
            // inherit-the-default and deletes the child.
            seats: dom.editSeats.value.trim(),
            tokenVersion: dom.editTokenVersion.value.trim(),
            graceDays: dom.editGraceDays.value.trim(),
            offlineGraceHours: dom.editOfflineHours.value.trim(),
            siteCodes: dom.editSiteCodes.value,
            siteCode: dom.editSiteCode.value.trim().toUpperCase(),
            siteId: dom.editSiteId.value.trim(),
            expiry
        };
    }

    async function submitEdit(event) {
        event.preventDefault();

        const key = state.editKey;
        const before = state.editSnapshot;
        if (!key || !before) return;

        // Two changes cost a customer their working software, and neither is
        // obvious from a form the owner came to for something else. The rest of
        // the fields go straight through — this dialog is a brake, not a habit.
        if (dom.editStatus.value === "revoked" && before.status !== "revoked") {
            const ok = await confirmAction({
                title: "Xác nhận Khóa License (BAN)",
                message: `Xác nhận KHÓA (Ban) license ${key}?\nMáy trạm đang dùng key này sẽ bị ngắt quyền truy cập ngay lập tức.`,
                okText: "Khóa License",
                danger: true
            });
            if (!ok) return;
        }

        const middleCode = dom.editMiddleCode.value.trim().toUpperCase();
        if (middleCode && before.middleCode && middleCode !== before.middleCode) {
            const ok = await confirmAction({
                title: "Xác nhận đổi mã bưu cục",
                message:
                    `Đổi mã bưu cục của ${key} từ ${before.middleCode} sang ${middleCode}?\n` +
                    "Đây cũng là site code của DataHub, nên máy trạm sẽ chuyển sang tenant khác ở lần enroll tiếp theo.",
                okText: "Đổi mã bưu cục",
                danger: true
            });
            if (!ok) return;
        }

        dom.editError.hidden = true;
        dom.editSubmit.disabled = true;
        const submitLabel = dom.editSubmit.textContent;
        dom.editSubmit.textContent = "Đang lưu…";

        try {
            const result = await api("POST", `/licenses/${encodeURIComponent(key)}/update`, collectEditPayload());
            const changed = Array.isArray(result.changed) ? result.changed : [];

            closeEdit();

            // "Đã lưu" on a save that wrote nothing is how a dashboard teaches
            // someone to stop reading its toasts, so the two cases say different
            // things — and the changed list names what actually moved.
            if (changed.length === 0) {
                toast("info", "Không có gì thay đổi", `${key} giữ nguyên toàn bộ thuộc tính.`);
            } else {
                toast("ok", `Đã lưu ${changed.length} thay đổi`, `${key} · ${changed.join(", ")}`);
            }

            await reload();
        } catch (error) {
            const described = describeError(error);
            if (described.fatal) {
                closeEdit();
                forgetToken();
                showLock(`${described.title}. ${described.detail}`);
                return;
            }
            dom.editError.textContent = described.detail;
            dom.editError.hidden = false;
        } finally {
            dom.editSubmit.disabled = false;
            dom.editSubmit.textContent = submitLabel;
        }
    }

    // ==========================================
    // WIRING
    // ==========================================

    function init() {
        initTheme();

        dom.lockForm.addEventListener("submit", event => {
            event.preventDefault();
            const token = dom.tokenInput.value.trim();
            if (token) unlock(token);
        });

        dom.themeToggle.addEventListener("click", cycleTheme);
        dom.refreshButton.addEventListener("click", reload);

        dom.lockButton.addEventListener("click", () => {
            forgetToken();
            showLock("Đã xoá token khỏi tab này.");
        });

        dom.createButton.addEventListener("click", openCreate);
        dom.createClose.addEventListener("click", closeCreate);
        dom.createCancel.addEventListener("click", closeCreate);
        dom.createBackdrop.addEventListener("click", closeCreate);
        dom.createForm.addEventListener("submit", submitCreate);

        // type="button" on all three, so none of them submits the form. The
        // pills sit inside <form id="create-form"> and a bare <button> there
        // would default to submit and mint a key on the first click.
        dom.toggleBase.addEventListener("click", () => switchCreateTier("BASE"));
        dom.toggleUltra.addEventListener("click", () => switchCreateTier("ULTRA"));
        dom.btnGeneralKey.addEventListener("click", generateRandomKey);
        dom.createMiddleCode.addEventListener("input", syncCandidateMiddle);

        dom.editClose.addEventListener("click", closeEdit);
        dom.editCancel.addEventListener("click", closeEdit);
        dom.editBackdrop.addEventListener("click", closeEdit);
        dom.editForm.addEventListener("submit", submitEdit);

        dom.editToggleBase.addEventListener("click", () => switchEditTier("BASE"));
        dom.editToggleUltra.addEventListener("click", () => switchEditTier("ULTRA"));

        dom.editCopyKey.addEventListener("click", async () => {
            const copied = await copyText(state.editKey);
            toast(copied ? "ok" : "error", copied ? "Đã copy key" : "Không copy được", state.editKey);
        });

        // All three feed the same preview line, so the computed expiry follows
        // the form instead of appearing only after a save.
        dom.editExpiryMode.addEventListener("change", syncExpiryRows);
        dom.editExpiryStart.addEventListener("input", syncExpiryRows);
        dom.editExpiryTerms.addEventListener("change", syncExpiryRows);
        dom.editExpiryDate.addEventListener("input", syncExpiryRows);

        // Keeps the siteCodes placeholder honest while the code is being typed:
        // a blank list falls back to whatever sits in this box, not to the value
        // that was there when the modal opened.
        dom.editMiddleCode.addEventListener("input", () => {
            const code = dom.editMiddleCode.value.trim().toUpperCase();
            dom.editSiteCodes.placeholder = `Bỏ trống = dùng mã bưu cục (${code || "—"})`;
        });

        dom.searchInput.addEventListener("input", event => {
            state.search = event.target.value;
            render();
        });

        dom.filterTier.addEventListener("change", event => {
            state.tier = event.target.value;
            render();
        });

        dom.filterStatus.addEventListener("change", event => {
            state.status = event.target.value;
            render();
        });

        for (const card of document.querySelectorAll("[data-filter-status]")) {
            card.addEventListener("click", () => {
                const wanted = card.dataset.filterStatus;
                // Clicking the card that is already applied clears it, so the
                // cards work as a toggle rather than a one-way trip.
                state.status = state.status === wanted ? "all" : wanted;
                dom.filterStatus.value = ["all", "active", "expiring", "grace", "expired", "revoked", "perpetual"]
                    .includes(state.status)
                    ? state.status
                    : "all";
                render();
            });
        }

        dom.rows.addEventListener("click", onRowClick);

        document.addEventListener("keydown", event => {
            if (event.key !== "Escape") return;
            // confirmAction() runs its own Escape listener and its box sits on
            // top of these two. Without this guard, one Escape would cancel the
            // ban confirm AND close the edit form underneath it, throwing away
            // everything typed on the way there.
            if (!dom.confirmModal.hidden) return;
            if (!dom.editModal.hidden) closeEdit();
            else if (!dom.createModal.hidden) closeCreate();
        });

        const stored = readStoredToken();
        if (stored) {
            // A token already in this tab means a reload, not a first visit: go
            // straight to the table and only fall back to the lock screen if the
            // server refuses it.
            state.token = stored;
            showApp();
            reload();
        } else {
            dom.loadingState.hidden = true;
            showLock("");
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
