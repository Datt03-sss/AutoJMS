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
        status: "all"
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
        createSkipHash: $("create-skip-hash"),

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
        copy: "M7 2a2 2 0 00-2 2v1H4a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-1h1a2 2 0 002-2V7a2 2 0 00-2-2h-1V4a2 2 0 00-2-2H7zm0 2h6v2H7V4zm-3 5h10v8H4V9zm12 0v5h1V7h-5v2h4z",
        reset: "M4 2a1 1 0 011 1v2.101a7.002 7.002 0 0111.601 2.566 1 1 0 11-1.885.666A5.002 5.002 0 005.999 7H9a1 1 0 010 2H4a1 1 0 01-1-1V3a1 1 0 011-1zm.008 9.047a1 1 0 011.885-.666A5.002 5.002 0 0014.001 13H11a1 1 0 110-2h5a1 1 0 011 1v5a1 1 0 11-2 0v-2.101a7.002 7.002 0 01-11.601-2.566 1 1 0 01.608-1.286z",
        tierUp: "M10 3a1 1 0 01.707.293l5 5a1 1 0 01-1.414 1.414L11 6.414V16a1 1 0 11-2 0V6.414L5.707 9.707a1 1 0 01-1.414-1.414l5-5A1 1 0 0110 3z",
        tierDown: "M10 17a1 1 0 01-.707-.293l-5-5a1 1 0 011.414-1.414L9 13.586V4a1 1 0 112 0v9.586l3.293-3.293a1 1 0 011.414 1.414l-5 5A1 1 0 0110 17z",
        ban: "M10 18a8 8 0 100-16 8 8 0 000 16zM4.33 10a5.67 5.67 0 019.22-4.39l-7.83 7.83A5.64 5.64 0 014.33 10zm2.12 4.39l7.83-7.83A5.67 5.67 0 0110 15.67a5.64 5.64 0 01-3.55-1.28z",
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
            if (action === "extend-1" || action === "extend-12") {
                const terms = action === "extend-1" ? 1 : 12;
                const result = await api("POST", `/licenses/${encodeURIComponent(key)}/extend`, { terms });
                toast(
                    "ok",
                    terms === 1 ? "Đã gia hạn 1 tháng" : "Đã gia hạn 1 năm",
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

        // The ones that change who can run the software. Extension is additive
        // and a tier change is one click to undo, so neither asks. Both
        // directions of toggle ask: unlocking a key is as much a decision as
        // locking one, and the whole button is now a 30px square with no text
        // on it — there is nothing to read on the way to clicking it.
        if (action === "toggle") {
            const isUnlocking = trigger.dataset.closed === "true";
            const message = isUnlocking
                ? `Xác nhận MỞ KHOÁ (Active) license ${key}?\nMáy trạm sẽ có thể kích hoạt và hoạt động bình thường.`
                : `Xác nhận KHOÁ (Ban) license ${key}?\nMáy trạm đang dùng key này sẽ bị ngắt quyền truy cập ngay lập tức.`;
            if (!window.confirm(message)) return;
        }

        if (action === "unbind") {
            if (!window.confirm(`Reset HWID của ${key}?\nKey sẽ gắn vào máy nào kích hoạt tiếp theo.`)) return;
        }

        await runAction(action, key, trigger.dataset);
    }

    // ==========================================
    // CREATE
    // ==========================================

    function openCreate() {
        dom.createError.hidden = true;
        dom.createForm.reset();
        dom.createSkipHash.checked = true;
        dom.createModal.hidden = false;
        dom.createMiddleCode.focus();
    }

    function closeCreate() {
        dom.createModal.hidden = true;
    }

    async function submitCreate(event) {
        event.preventDefault();

        const middleCode = dom.createMiddleCode.value.trim().toUpperCase();
        const tier = dom.createForm.querySelector('input[name="tier"]:checked')?.value || "ULTRA";
        const terms = Number(dom.createTerms.value);

        dom.createError.hidden = true;
        dom.createSubmit.disabled = true;
        dom.createSubmit.textContent = "Đang tạo…";

        try {
            const result = await api("POST", "/licenses/create", {
                middleCode,
                tier,
                terms,
                notes: dom.createNotes.value,
                skipHashCheck: dom.createSkipHash.checked
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
        } finally {
            dom.createSubmit.disabled = false;
            dom.createSubmit.textContent = "Tạo key";
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
            if (!dom.createModal.hidden) closeCreate();
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
