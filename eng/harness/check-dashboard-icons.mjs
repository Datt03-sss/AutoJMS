/**
 * Gate icon của WebView2 Dashboard.
 *
 * Vì sao cần: `src/AutoJMS/Web/` không có bước build. Thư viện bên thứ ba được
 * chép nguyên vào repo và nạp bằng `<script type="module">` thẳng từ đĩa, nên
 * một `import` trỏ sai tên file băm, hay một binding `{{ ...IconD }}` không có
 * ai sinh ra, đều KHÔNG hỏng lúc `dotnet build` — chỉ hỏng lúc mở tab Dashboard
 * trên máy nhân viên bưu cục. Gate này là chỗ duy nhất bắt được.
 *
 * Không có nhánh nào "skip": thiếu file là FAIL, đúng tinh thần test-node.ps1.
 *
 * Chạy: node eng/harness/check-dashboard-icons.mjs
 */
import { readFileSync, existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const WEB = join(ROOT, "src", "AutoJMS", "Web");
const ENTRY = join(WEB, "aj-icons.js");
const INDEX = join(WEB, "index.html");

const failures = [];
const fail = (msg) => failures.push(msg);
const ok = (msg) => console.log(`  ok   ${msg}`);

// ── 1. Đồ thị import phân giải được ────────────────────────────────────────
// Tên file băm của morphicons (controller-CXZuwJ_M.js…) đổi theo mỗi lần
// upstream build lại. Nâng cấp mà chép thiếu một chunk thì lỗi hiện ra ở đây.
const seen = new Set();
function walkImports(file) {
  if (seen.has(file)) return;
  seen.add(file);
  if (!existsSync(file)) {
    fail(`import trỏ tới file không tồn tại: ${file.slice(ROOT.length + 1)}`);
    return;
  }
  const src = readFileSync(file, "utf8");
  for (const m of src.matchAll(/\bfrom\s*"(\.[^"]+)"/g)) {
    walkImports(resolve(dirname(file), m[1]));
  }
}
walkImports(ENTRY);
if (!failures.length) ok(`đồ thị import: ${seen.size} file, phân giải hết`);

// ── 2. Bảng `d` dựng được thật ─────────────────────────────────────────────
// Nạp đúng module mà trình duyệt nạp, không phải bản chép lại trong test.
const { AjIcons } = await import(pathToFileURL(ENTRY).href);
const EXPECTED = [
  "copy", "check", "refresh-cw", "triangle-alert", "search", "x",
  "eye", "eye-off", "chevron-down", "chevron-up", "menu", "play", "pause"
];
for (const name of EXPECTED) {
  const d = AjIcons[name];
  if (typeof d !== "string" || d.length < 8) {
    fail(`AjIcons['${name}'] không phải chuỗi d hợp lệ (nhận: ${JSON.stringify(d)})`);
  } else if (!/^[Mm]/.test(d)) {
    fail(`AjIcons['${name}'] không bắt đầu bằng lệnh moveto: ${d.slice(0, 24)}`);
  }
}
if (!failures.length) ok(`canonicalD: ${EXPECTED.length}/${EXPECTED.length} icon ra chuỗi d hợp lệ`);

// ── 3. index.html và bảng icon khớp nhau ───────────────────────────────────
const html = readFileSync(INDEX, "utf8");

if (!html.includes('<script type="module" src="./aj-icons.js"></script>')) {
  fail("index.html không nạp ./aj-icons.js bằng <script type=\"module\">");
}
for (const retired of ["lucide-icons.js", "morphicons.min.js"]) {
  if (html.includes(retired)) fail(`index.html còn tham chiếu file đã bỏ: ${retired}`);
}

// Mọi `<aj-morph-icon icon="{{ x }}">` phải có một `x:` trong renderVals.
const bound = [...html.matchAll(/<aj-morph-icon[^>]*\bicon="\{\{\s*([A-Za-z0-9_]+)\s*\}\}"/g)].map((m) => m[1]);
if (!bound.length) fail("không còn <aj-morph-icon> nào trong index.html — morph đã bị gỡ?");
for (const name of new Set(bound)) {
  if (!new RegExp(`\\b${name}\\s*:`).test(html)) {
    fail(`<aj-morph-icon icon="{{ ${name} }}"> nhưng renderVals không sinh ra '${name}'`);
  }
}

// Mọi tên icon truyền cho AJ_ICON(…) phải có trong bảng. Đối số thường là một
// biểu thức ba ngôi (`cond ? 'check' : 'copy'`), nên lấy MỌI chuỗi trong đó.
const used = [...html.matchAll(/AJ_ICON\(([^)]*)\)/g)]
  .flatMap((m) => [...m[1].matchAll(/'([^']+)'/g)].map((q) => q[1]));
if (!used.length) fail("không có lời gọi AJ_ICON('…') nào trong index.html");
for (const name of new Set(used)) {
  if (!AjIcons[name]) fail(`index.html gọi AJ_ICON('${name}') nhưng aj-icons.js không có icon đó`);
}
if (!failures.length) {
  ok(`index.html: ${bound.length} <aj-morph-icon>, ${new Set(used).size} icon, khớp hết`);
}

// ── Kết quả ────────────────────────────────────────────────────────────────
if (failures.length) {
  console.error("\nDashboard icon gate FAILED:");
  for (const f of failures) console.error(`  ✗ ${f}`);
  process.exit(1);
}
console.log("Dashboard icon gate passed.");
