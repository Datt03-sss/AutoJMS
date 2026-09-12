# AutoJMS License Dashboard

Giao diện quản trị license, phục vụ tĩnh tại `/admin` bởi chính
`backend/render-license-server/server.js`. Không có bước build: ba file trong thư
mục này **là** thứ chạy trên trình duyệt. Sửa file, push, Render deploy — backend
và giao diện đi cùng một lần.

| File | Vai trò |
|---|---|
| `index.html` | Khung trang: màn khoá, 4 thẻ thống kê, thanh lọc, bảng, modal cấp key |
| `styles.css` | Toàn bộ design token (màu, bo góc, đổ bóng, font) + layout + responsive |
| `app.js` | Logic: xác thực, gọi API, lọc tức thì, thao tác từng dòng, toast |

---

## 1. Bật Admin API trước khi dùng

Trang này vô dụng nếu server chưa có admin token. Đặt biến môi trường trên Render
(hoặc trong `.env` khi chạy local) rồi khởi động lại:

```
ADMIN_SECRET_TOKEN=<chuỗi ngẫu nhiên tối thiểu 16 ký tự>
```

Sinh một chuỗi bằng Node:

```bash
node -e "console.log(require('crypto').randomBytes(32).toString('base64url'))"
```

**Không có giá trị mặc định, và đó là cố ý.** Repo này PUBLIC. Một mật khẩu mặc
định nằm trong code không phải mật khẩu — nó là mật khẩu đã công bố, trên đúng
những endpoint cấp license ULTRA và thu hồi key của khách đang trả tiền. Vì vậy:

- Chưa đặt biến → mọi route `/api/admin/*` trả **503 `ADMIN_API_DISABLED`** và
  **không ghi gì** vào Firebase. Không phải "mở", mà là "tắt".
- Đặt dưới 16 ký tự → cũng coi như chưa đặt (một token 4 ký tự là cùng một lỗ
  hổng, chỉ khác là nó trông như đã bảo vệ).
- Lúc boot server ghi một dòng `admin.disabled` kèm **lý do**, không kèm giá trị.

Token gõ ở màn khoá chỉ nằm trong `sessionStorage` của tab đó và mất khi đóng tab.
Nút **Khoá** ở góc phải xoá ngay lập tức.

---

## 2. Chạy thử local

```bash
cd backend/render-license-server
npm install
node server.js
```

Mở `http://localhost:3000/admin`.

Server cần đủ biến môi trường Firebase/JWT như thường lệ (xem `env.template`);
riêng dashboard chỉ cần thêm `ADMIN_SECRET_TOKEN`.

---

## 3. Quy tắc bắt buộc khi sửa giao diện

Server gửi `Content-Security-Policy` mặc định của `helmet()`. Ba hệ quả không thể
lách:

1. **`script-src 'self'` — không CDN.** Thẻ `<script src="https://cdn…">` (Tailwind,
   Vue, Alpine, jQuery) sẽ bị chặn thẳng, không chạy, không báo gì ngoài console.
   Muốn dùng thư viện thì tải file về đặt cạnh `app.js` và trỏ đường dẫn tương đối.
   Đừng nới CSP: trang này giữ admin token, cho script bên thứ ba chạy trên nó là
   đánh đổi tồi cho một cái bảng và một cái modal.
2. **Không `<script>` inline.** Mọi JS phải nằm trong `app.js`.
3. **`script-src-attr 'none'` — không `onclick=`.** Thuộc tính sự kiện trong HTML
   sẽ im lặng không chạy. Gắn handler bằng `addEventListener` trong `app.js`
   (bảng dùng event delegation qua `data-action` / `data-key`).

Ngoài ra: mọi giá trị lấy từ API được ghi bằng `textContent`, **không** bằng
`innerHTML`. Trường `notes` là free-text Owner tự gõ — đó là chỗ duy nhất trên
trang có thể mang theo markup.

---

## 4. Đổi màu / font / bo góc

Toàn bộ nằm ở đầu `styles.css`, trong ba khối biến:

- `:root` — theme sáng
- `@media (prefers-color-scheme: dark) :root` — theme tối cho người chưa chọn
- `html[data-theme="light"]` / `html[data-theme="dark"]` — khi Owner bấm nút đổi

Đổi `--brand` là đổi màu chủ đạo của cả nút, badge ULTRA, viền focus và nền màn
khoá. Ba biến trạng thái `--ok` / `--warn` / `--danger` điều khiển thẻ thống kê,
badge và cột "Còn lại".

Nút đổi giao diện xoay vòng `auto → light → dark`, lưu ở `localStorage`
(`autojms.admin.theme`).

---

## 5. Thêm một cột vào bảng

Sửa hai chỗ, cùng thứ tự:

1. `index.html` — thêm một `<th scope="col">` vào `<thead>`.
2. `app.js` — trong `buildRow()`, thêm một `el("td", { "data-label": "<Tên cột>" }, [...])`
   đúng vị trí tương ứng.

`data-label` là bắt buộc: dưới 900px bảng chuyển thành thẻ dọc và nhãn cột được
lấy từ chính thuộc tính đó (`.table td::before { content: attr(data-label) }`).
Quên `data-label` thì trên điện thoại ô đó sẽ không có tiêu đề.

Nếu cột hiển thị dữ liệu mới, `GET /api/admin/licenses` phải trả thêm trường đó —
xem `admin-routes.js`.

---

## 6. Thêm một thao tác vào cột Thao tác

Cột Thao tác không có menu `⋯`: mọi nút nằm thẳng trên dòng để bấm một chạm.
Chỉ `+1 Tháng` giữ chữ (`btn btn--mini`), còn lại là nút icon vuông 30px
(`btn--icon-action`) để 5 nút không làm bảng rối.

Icon là **SVG một path, tô đặc**, không phải emoji. Emoji do font hệ điều hành
vẽ: cùng một ký tự ra ba hình khác nhau trên Windows / macOS / Android, và ổ
khoá của Windows thì gần như không phân biệt được đóng với mở ở cỡ 15px. SVG
thì mọi máy nhìn thấy đúng một hình.

1. Thêm endpoint trong `admin-routes.js`.
2. Thêm path mới vào hằng `ICON` trong `app.js` (lưới `0 0 20 20`, một `d` duy
   nhất, tô đặc — đừng dùng path viền vì `createSvg()` không set `stroke`).
3. Trong `buildRow()`, thêm vào `el("div", { class: "cell-actions" }, [...])` một
   nút nữa:

   ```js
   el("button", {
       class: "btn--icon-action",
       type: "button",
       "data-action": "<action>",
       "data-key": license.key,
       title: "<Nhãn>",
       "aria-label": "<Nhãn>"
   }, [createSvg(ICON.<tên>)])
   ```

   `title` **và** `aria-label` đều bắt buộc với nút icon: `title` là tooltip khi
   di chuột, `aria-label` là tên thật của nút. Bản thân `<svg>` mang
   `aria-hidden="true"` — thiếu `aria-label` thì trình đọc màn hình chỉ đọc được
   "button", không có gì khác.

   Màu lấy theo `fill="currentColor"`, nên đổi màu icon là đổi `color` của nút:
   `btn--icon-action--ban` (đỏ), `--unban` (xanh), `--upgrade` (màu chủ đạo),
   `--downgrade` (xám, cố ý nhạt — không có gì đáng để mắt dừng lại).
4. Trong `runAction()` thêm nhánh `else if (action === "<action>")` gọi `api(...)`
   rồi `toast(...)`.

**Không dùng `innerHTML` để dựng SVG.** `createSvg()` tạo node bằng
`createElementNS` vì `document.createElement("svg")` ra một phần tử HTML lạ,
trình duyệt không vẽ gì cả — và vì mọi hàm dựng DOM khác trên trang này đều
an toàn theo cấu trúc, hàm này không có lý do gì để là ngoại lệ.

`runAction()` tự `reload()` sau khi thành công — trạng thái trên màn hình luôn là
thứ server vừa xác nhận, không phải thứ frontend đoán.

Thao tác nặng thì thêm một `window.confirm` trong `onRowClick()` trước khi gọi
`runAction()` — `toggle` (cả Khoá lẫn Mở khoá) và `unbind` (Reset HWID) đang làm
đúng như vậy. Nút nằm sẵn trên dòng và giờ chỉ còn là một ô vuông 30px không
chữ nên rất dễ bấm nhầm; hộp thoại đó là lớp chặn duy nhất.

Đừng phân biệt chiều của thao tác bằng `textContent` — nhãn giờ là icon, không
có chữ để đọc. `toggle` dùng `data-closed="true|false"` và `onRowClick()` đọc
`trigger.dataset.closed` để chọn câu hỏi xác nhận.

---

## 7. Modal "Cấp License Mới"

Một thẻ duy nhất, hai mặt cấu hình. Không có bước wizard, không có tab.

### Cặp pill BASE / ULTRA

Không phải hai `<input type="radio">`. Chọn gói là thao tác đầu tiên và nó đổi cả
phần thân bên dưới, nên nó phải nhìn như công tắc chứ không như một trường nhập
nữa. Mặc định là **ULTRA** — BASE là lựa chọn hạ cấp có chủ đích, đáng một cú
bấm, không đáng là thứ mở ra đã thấy.

`switchCreateTier(tier)` trong `app.js` là **nơi duy nhất** được phép đổi những
thứ phụ thuộc gói. Thêm bất cứ thứ gì đổi theo tier thì thêm vào đúng hàm đó:

| Đổi cái gì | Thành |
|---|---|
| `state.createTier` | `"ULTRA"` / `"BASE"` |
| class hai pill | `active-ultra` (gradient tím-hồng) / `active-base` (nền trắng) |
| `aria-pressed` hai pill | `true` / `false` — pill là nút, không phải radio, nên trạng thái chọn phải nói ra bằng thuộc tính này |
| `#panel-ultra` / `#panel-base` | `hidden` đổi chiều |
| `#create-submit` | chữ `Tạo License ULTRA` ↔ `Tạo License BASE`, class `btn-ultra` bật/tắt |

`#panel-base` là ô Google Sheet ID **bị khoá** kèm placeholder "Không hỗ trợ trên
gói BASE", không phải ô bị ẩn. Ẩn hẳn thì modal nhảy chiều cao mỗi lần đổi gói và
Owner không bao giờ biết vì sao BASE không có chỗ điền Sheet.

### Khung "Mã License Dự Kiến"

Mã sinh ở client **trước** khi tạo, để Owner xem rồi mới bấm:

- `generateRandomKey()` — quay cả hai nhóm 4 ký tự. Dùng `crypto.getRandomValues`
  và **loại bỏ byte ≥ 252** trước khi `% 36`; thiếu bước đó thì 4 chữ cái đầu
  bảng chữ ra thường hơn ~14% so với phần còn lại.
- `syncCandidateMiddle()` — gõ vào ô Mã bưu cục thì **chỉ** đoạn giữa đổi, hai
  nhóm ngẫu nhiên giữ nguyên. Quay lại cả mã trên mỗi phím gõ thì Owner thấy mã
  nhấp nháy qua sáu giá trị trong lúc gõ một mã bưu cục sáu ký tự.
- `submitCreate()` gọi `syncCandidateMiddle()` một lần nữa ngay trước khi POST,
  phòng trường hợp ô mã bưu cục được sửa bằng cách không sinh ra sự kiện `input`.

Mã đó được gửi lên trong trường `key`. **Đó là tiện lợi, không phải uỷ quyền**:
`resolveLicenseKey()` trong `admin-routes.js` kiểm lại hình dạng, bắt buộc đoạn
giữa đúng bằng mã bưu cục vừa validate, và bắt buộc node còn trống. Sai hình dạng
→ **400 `INVALID_LICENSE_KEY`**; đã có người dùng → **409 `LICENSE_KEY_TAKEN`**.
Server **từ chối chứ không tự sửa**, vì mã dashboard đã hiện lên là mã Owner có
thể đã dán cho khách rồi. Gặp 409 thì `app.js` tự quay mã mới để bấm lại là được.

Không gửi `key` (hoặc gửi rỗng) thì server tự sinh như trước.

### Khối công tắc

Bốn checkbox: `skipHashCheck`, `autoUpdate`, `silentUpdate`, `applyOnNextStartup`.

Mặc định bật nằm ở thuộc tính `checked` trong `index.html`, **không** nằm trong
`app.js` — `openCreate()` gọi `dom.createForm.reset()`, và `reset()` trả checkbox
về đúng cái `checked` trong HTML. Muốn đổi mặc định thì sửa HTML.

Ở server, ba công tắc module đọc bằng `!== false` chứ không bằng `Boolean()`: một
body hoàn toàn không có `modulePolicy` phải để cả ba **bật**, vì đó là hình dạng
mọi bản ghi đang có trong hệ thống. Chỉ `false` tường minh mới tắt.

### Thêm một trường vào modal

Bốn chỗ, đúng thứ tự:

1. `index.html` — thêm `<label class="field">` (trong `.form-grid` nếu muốn nằm
   hai cột, hoặc trong `#panel-ultra` nếu chỉ ULTRA mới có).
2. `app.js` — thêm một dòng vào bản đồ `dom`.
3. `app.js` — thêm vào body của `api("POST", "/licenses/create", { … })`.
4. `admin-routes.js` — thêm hàm làm sạch (xem `sanitizeNotes` /
   `sanitizeSpreadsheetId`) rồi thêm trường vào `record`.

Ba nút `#toggle-base`, `#toggle-ultra`, `#btn-general-key` đều **bắt buộc** có
`type="button"`. Chúng nằm trong `<form id="create-form">`, mà `<button>` không
ghi `type` thì mặc định là `submit` — bấm pill để xem gói kia sẽ cấp luôn một key.

---

## 8. Cách trang này hiểu lỗi

`describeError()` trong `app.js` tách ba trường hợp không phải "thử lại":

| Tình huống | Hiển thị |
|---|---|
| `503 ADMIN_API_DISABLED` | "Admin API chưa được bật" + hướng dẫn đặt env — **không** phải "sai token" |
| `401` | "Sai Admin Token", quay về màn khoá, xoá token khỏi `sessionStorage` |
| `429 ADMIN_AUTH_THROTTLED` | "Bị tạm khoá" — đã nhập sai quá 10 lần trong 15 phút, chờ hết cửa sổ |

Phân biệt 503 với 401 là có chủ đích: nếu gộp chung, Owner sẽ đi tìm lại token
trong khi vấn đề thật là server chưa hề được cấu hình.

---

## 9. Ánh xạ trạng thái

`GET /api/admin/licenses` trả `effectiveStatus` do `evaluateLicense()` tính —
cùng hàm mà `/api/verify-license` dùng cho máy trạm, nên bảng này không bao giờ
nói khác với thứ khách đang gặp. `app.js` gom thêm một bậc để hiển thị:

| Bucket | Điều kiện | Badge |
|---|---|---|
| `active` | đang chạy, còn > 7 ngày | xanh "Đang chạy" |
| `expiring` | đang chạy, còn ≤ 7 ngày | cam "Sắp hết hạn" |
| `grace` | đã quá hạn, còn trong 7 ngày ân hạn | cam "Gia hạn tạm" |
| `expired` | quá cả ân hạn | đỏ "Hết hạn" |
| `perpetual` | active và không có `expiresAt` | xanh "Vĩnh viễn" |
| `revoked` | mọi `status` khác `active` | xám "Đã khoá" |

Thẻ "Sắp hết hạn" đếm cả `expiring` lẫn `grace`: cả hai đều cần xử lý ngay và
chưa cái nào chết hẳn. Đổi ngưỡng 7 ngày ở hằng số `EXPIRING_SOON_DAYS` trong
`app.js`.

Chấm tròn trước license key (`.status-dot`) chỉ tách **hai** trạng thái: đỏ khi
bucket là `revoked`, xanh với mọi bucket còn lại. Nó trả lời "key này còn bật
không", không phải "còn mấy ngày" — hết hạn vẫn là chấm xanh, vì hết hạn là việc
của cột Hạn dùng và badge trạng thái. Chấm chỉ mang `title`, không mang chữ:
badge cách đó ba cột đã nói đúng điều ấy bằng lời rồi, lặp lại thì trình đọc màn
hình phải đọc hai lần.
