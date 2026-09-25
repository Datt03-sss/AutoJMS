/**
 * aj-icons.js — điểm vào icon của AutoJMS Dashboard (WebView2).
 *
 * Nối hai thư viện bên thứ ba lại cho template dc-runtime dùng được:
 *   - Dữ liệu icon  : lucide@1.48.0 (ISC) — chép nguyên `d` từ `dist/esm/icons/*.mjs`
 *   - Chuyển động   : morphicons@1.7.1 (MIT) — xem ./morphicons/README.md
 *
 * Là ES module, KHÔNG phải classic script: dashboard chạy ở origin https thật
 * (`https://autojms.local`, map bằng SetVirtualHostNameToFolderMapping trong
 * Forms/FullStackOperation.Dashboard.cs), nên `import` cùng origin hợp lệ với
 * CSP `script-src 'self'`. Module script chạy sau khi parse xong document nhưng
 * TRƯỚC `DOMContentLoaded` — mà support.js lại boot dc-runtime trong
 * `DOMContentLoaded` — nên `<aj-morph-icon>` và `window.AjIcons` luôn sẵn sàng
 * trước khi template render lần đầu. Đừng đổi sang `async`.
 *
 * Quy chuẩn sử dụng: .agent/rules/11-icon-and-animation-rules.md
 */
import { MorphIconElement } from "./morphicons/element.js";
import { canonicalD } from "./morphicons/dom.js";

/**
 * Spring chuẩn của AutoJMS (rule 11 §4). Không nằm trong 3 preset dựng sẵn của
 * morphicons (smooth 170/26, snappy 420/30, bouncy 300/14), và thuộc tính HTML
 * `spring=` chỉ nhận được tên preset dạng chuỗi — nên phải gán bằng property,
 * và đó là lý do `<aj-morph-icon>` tồn tại thay vì dùng thẳng `<morph-icon>`.
 * ζ = 20 / (2·√200) ≈ 0.71: nảy nhẹ một nhịp rồi đứng, không rung.
 */
const AJ_SPRING = Object.freeze({ stiffness: 200, damping: 20 });

/**
 * Dữ liệu Lucide dạng IconNode `[tag, attrs][]`, chép nguyên văn từ
 * lucide@1.48.0. Chỉ giữ đúng các icon có trong bảng "Cặp Icon Chuyển Trạng
 * thái Chuẩn" của rule 11 §4 — thêm icon mới thì chép từ gói `lucide`, đừng vẽ tay.
 *
 * Lưu ý: bản Lucide hiện tại đặt tên `triangle-alert` (tên cũ `alert-triangle`).
 */
const LUCIDE = {
  copy: [
    ["rect", { width: "14", height: "14", x: "8", y: "8", rx: "2", ry: "2" }],
    ["path", { d: "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" }]
  ],
  check: [["path", { d: "M20 6 9 17l-5-5" }]],
  "refresh-cw": [
    ["path", { d: "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" }],
    ["path", { d: "M21 3v5h-5" }],
    ["path", { d: "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" }],
    ["path", { d: "M8 16H3v5" }]
  ],
  "triangle-alert": [
    ["path", { d: "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" }],
    ["path", { d: "M12 9v4" }],
    ["path", { d: "M12 17h.01" }]
  ],
  search: [
    ["path", { d: "m21 21-4.34-4.34" }],
    ["circle", { cx: "11", cy: "11", r: "8" }]
  ],
  x: [
    ["path", { d: "M18 6 6 18" }],
    ["path", { d: "m6 6 12 12" }]
  ],
  eye: [
    ["path", { d: "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0" }],
    ["circle", { cx: "12", cy: "12", r: "3" }]
  ],
  "eye-off": [
    ["path", { d: "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49" }],
    ["path", { d: "M14.084 14.158a3 3 0 0 1-4.242-4.242" }],
    ["path", { d: "M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143" }],
    ["path", { d: "m2 2 20 20" }]
  ],
  "chevron-down": [["path", { d: "m6 9 6 6 6-6" }]],
  "chevron-up": [["path", { d: "m18 15-6-6-6 6" }]],
  menu: [
    ["path", { d: "M4 5h16" }],
    ["path", { d: "M4 12h16" }],
    ["path", { d: "M4 19h16" }]
  ],
  play: [["path", { d: "M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z" }]],
  pause: [
    ["rect", { x: "14", y: "3", width: "5", height: "18", rx: "1" }],
    ["rect", { x: "5", y: "3", width: "5", height: "18", rx: "1" }]
  ]
};

/**
 * Tên icon → một chuỗi `d` duy nhất. `<aj-morph-icon icon="...">` là thuộc tính
 * HTML nên chỉ tải được chuỗi; `canonicalD` gộp mọi hình (rect/circle/path…)
 * của một IconNode về đúng một `d` mà morphicons vẫn morph được.
 */
export const AjIcons = Object.freeze(
  Object.fromEntries(Object.entries(LUCIDE).map(([name, node]) => [name, canonicalD(node)]))
);

/**
 * `<morph-icon>` nhưng mặc định mang spring chuẩn AutoJMS. Gán trong constructor
 * là hợp lệ: setter chỉ ghi vào props nội bộ, và `#watch()` thoát sớm khi chưa có
 * controller — không đụng DOM, đúng ràng buộc của custom element constructor.
 */
class AjMorphIcon extends MorphIconElement {
  constructor() {
    super();
    this.spring = AJ_SPRING;
  }
}

// Hai lần canh `typeof` là để module này nạp được cả ngoài trình duyệt —
// eng/harness/check-dashboard-icons.mjs import thẳng file này bằng node để kiểm
// bảng `d`, và morphicons cũng tự thiết kế không-DOM theo cùng cách (xem
// `const Base = typeof HTMLElement === "undefined" ? class {} : HTMLElement`).
if (typeof customElements !== "undefined" && !customElements.get("aj-morph-icon")) {
  customElements.define("aj-morph-icon", AjMorphIcon);
}

// Logic của dashboard nằm trong khối `data-dc-script` của index.html và được
// dc-runtime biên dịch bằng `new Function` — khối đó không `import` được, nên
// bảng `d` phải đi qua đối tượng toàn cục.
globalThis.AjIcons = AjIcons;
