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

1. Thêm endpoint trong `admin-routes.js`.
2. Trong `buildRow()`, thêm vào `el("div", { class: "cell-actions" }, [...])` một
   nút nữa:

   ```js
   el("button", {
       class: "btn btn--mini",
       type: "button",
       "data-action": "<action>",
       "data-key": license.key,
       text: "<Nhãn>"
   })
   ```

   Thêm `class: "btn btn--mini btn--mini--danger"` nếu thao tác gây hậu quả nặng.
3. Trong `runAction()` thêm nhánh `else if (action === "<action>")` gọi `api(...)`
   rồi `toast(...)`.

`runAction()` tự `reload()` sau khi thành công — trạng thái trên màn hình luôn là
thứ server vừa xác nhận, không phải thứ frontend đoán.

Thao tác không thể hoàn tác thì thêm một `window.confirm` trong `onRowClick()`
trước khi gọi `runAction()` — `toggle` (Khoá key) và `unbind` (Reset HWID) đang
làm đúng như vậy. Nút nằm sẵn trên dòng nên rất dễ bấm nhầm; hộp thoại đó là lớp
chặn duy nhất.

---

## 7. Cách trang này hiểu lỗi

`describeError()` trong `app.js` tách ba trường hợp không phải "thử lại":

| Tình huống | Hiển thị |
|---|---|
| `503 ADMIN_API_DISABLED` | "Admin API chưa được bật" + hướng dẫn đặt env — **không** phải "sai token" |
| `401` | "Sai Admin Token", quay về màn khoá, xoá token khỏi `sessionStorage` |
| `429 ADMIN_AUTH_THROTTLED` | "Bị tạm khoá" — đã nhập sai quá 10 lần trong 15 phút, chờ hết cửa sổ |

Phân biệt 503 với 401 là có chủ đích: nếu gộp chung, Owner sẽ đi tìm lại token
trong khi vấn đề thật là server chưa hề được cấu hình.

---

## 8. Ánh xạ trạng thái

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
