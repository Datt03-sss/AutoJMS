# Render CLI (repo-local, pinned)

CLI chính thức của Render (`render-oss/cli`, Apache-2.0, homepage <https://render.com/docs/cli>),
cài **cục bộ trong repo** ở phiên bản **ghim cứng 2.24.0**.

Dùng cho một việc duy nhất: **license server** — Web Service `autojms-api` trên Render, được định
nghĩa bởi [`render.yaml`](../../render.yaml) ở gốc repo (`rootDir: backend/render-license-server`).

---

## 1. Cài / cập nhật

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\render-cli\install-render-cli.ps1
```

Chạy lại nhiều lần vô hại: đúng phiên bản đã có thì script in `Already installed` rồi thoát 0. Muốn
cài lại: thêm `-Force`. Muốn đổi phiên bản: `-Version 2.25.0` (và sửa mặc định trong script nếu định
ghim mức mới cho cả dự án).

Đưa vào PATH **chỉ trong shell hiện tại**:

```powershell
$env:PATH = "$PWD\tools\render-cli\bin;$env:PATH"
```

Vì sao cài trong repo thay vì cài vào máy:

- Không có package winget / choco / scoop cho Render CLI (kiểm tra 2026-08-26), nên phương án còn
  lại là tải tay không ghim phiên bản — không ai tái lập được.
- Phiên bản ghim trong script ⇒ mọi máy và mọi agent dùng đúng một CLI, và việc nâng cấp là một
  diff đọc được chứ không phải "bản mới nhất hôm đó".
- `bin/` bị git-ignore. File nhị phân 8.7 MB theo nền tảng không thuộc về một repo **PUBLIC**.

**Kiểm tra toàn vẹn, không phải kiểm tra chữ ký.** Script so SHA-256 của file zip với
`cli_<ver>_SHA256SUMS` phát hành cùng release. Việc này chặn tải hỏng / tải bị sửa giữa đường, nhưng
release còn có `SHA256SUMS.sig` — xác thực nó cần `cosign` và public key của Render, cả hai đều chưa
được ghim ở đây. Đừng ghi trong tài liệu nào rằng bản này "đã verify signature".

## 2. Đăng nhập là việc của Owner

```powershell
render login          # mở browser — CHỈ Owner chạy
render workspace set  # bắt buộc: thiếu workspace thì gần như mọi lệnh fail
render whoami         # exit 1 + "run `render login`" nếu chưa đăng nhập
```

Agent (Claude Code, Antigravity) **không** chạy `render login` và **không** nhận/nhập `RENDER_API_KEY`
— đó là credential. Dùng không tương tác (CI hoặc script) thì đặt biến môi trường `RENDER_API_KEY`,
theo cùng nguyên tắc đã áp dụng cho `AUTOJMS_FORBIDDEN_VALUES` trong
[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml): bơm qua `env:`, không nội suy `${{ }}`
vào thân script, không ghi ra file tạm.

Chưa `render workspace set` thì kể cả `render services -o json` cũng chỉ trả về
`Error: no workspace set`, không phải danh sách rỗng.

## 3. CLI **không** làm được A1-a…A1-g

Bảy thao tác A1-a…A1-g trong
[`docs/agent/BACKEND_BUILD_AND_VPS_DEPLOY_PLAN.vi.md`](../../docs/agent/BACKEND_BUILD_AND_VPS_DEPLOY_PLAN.vi.md)
(mục 3.4) vẫn đang chặn go-live của license server, và **CLI không thay thế được thao tác nào**:

| Bước | Việc | CLI làm được? |
|---|---|---|
| A1-a | Trỏ service `autojms-api` từ repo `AutoJMS-API` sang `Datt03-sss/AutoJMS` | ❌ dashboard |
| A1-b | Apply blueprint + điền 6 biến `sync: false` | ❌ `blueprints` chỉ có `validate`, không có `apply` |
| A1-c | Upload lại `googleSheetsServiceAccount.json` vào `/etc/secrets/` | ❌ dashboard |
| A1-d | Xác nhận `Instances = 1` | ✅ `render services instances <srv-id>` (chỉ *đọc*) |
| A1-e | Thu hồi Supabase anon key | ❌ ngoài Render |
| A1-f | Ghim một phiên bản `express` / `firebase-admin` | ❌ việc trong repo |
| A1-g | Giữ `autoDeploy: false` tới Chặng F | ❌ dashboard (nhưng xem §4: CLI là cách bấm deploy tay) |

Nói cách khác: **vai của CLI ở đây là kiểm chứng và bấm deploy tay, không phải cutover.** Cho tới khi
A1-a thực sự được bấm, Render production vẫn đang chạy repo `AutoJMS-API`, và `render.yaml` trong repo
này chỉ là dự định — CLI cũng sẽ báo cáo đúng cái đang chạy, không phải cái đã chốt trên giấy.

## 4. Lệnh đã kiểm chứng trên v2.24.0

```powershell
render services -o json                      # liệt kê service, lấy srv-... id
render services instances srv-xxxx           # A1-d: đếm instance đang chạy
render deploys list srv-xxxx -o json         # lịch sử deploy
render deploys create srv-xxxx --wait        # autoDeploy=false ⇒ đây là cách bấm deploy tay
render deploys create srv-xxxx --commit <sha> --wait
render logs -r srv-xxxx --tail               # -r BẮT BUỘC khi không có TTY
render logs -r srv-xxxx --status-code 500 --limit 100
render restart srv-xxxx
render psql / render ssh                     # phiên tương tác, Owner dùng
```

Ghi chú thực dụng:

- `-o json|yaml|text` tự chuyển sang `text` khi không có TTY. Muốn parse thì **ghi rõ `-o json`**,
  đừng dựa vào mặc định.
- `--wait` làm `deploys create` trả exit code khác 0 khi deploy fail — cần dùng nếu sau này gọi từ
  script/CI, vì không có nó lệnh trả 0 dù build đỏ.
- `--confirm` bỏ qua mọi prompt xác nhận. **Không bao giờ** ghép `--confirm` với
  `services delete` / `restart` / `deploys cancel` trong script.

## 5. Hai cái bẫy đã gặp, đã xác minh (v2.24.0)

- **`render blueprints validate ./render.yaml` KHÔNG phải linter offline.** Không có workspace nó
  thoát 1 với `no workspace specified and no default workspace set`. Nó gọi lên Render, nên không
  dùng được trong CI như một bước kiểm cú pháp `render.yaml`, và không phải là cách kiểm tra
  `render.yaml` trước khi push.
- **`render skills list` panic** (`nil pointer dereference` trong `pkg/tui`, exit 2), cả ở chế độ mặc
  định lẫn `-o json`. Nhóm lệnh `render skills` (cài "Render agent skills" vào Claude Code / Cursor)
  vì thế coi như không dùng được ở bản này. Kể cả khi nó chạy, `skills install` ghi vào cấu hình của
  AI tool — thay đổi cấu hình lâu dài, phải do Owner quyết, không phải agent tự chạy.

## 6. Ranh giới

- Đây là tooling, không phải app source. Không có mã sản phẩm nào trong `tools/`.
- CLI chỉ chạm tới **license server trên Render**. VPS DataHub (`docker`, `psql` qua SSH) là địa hạt
  của Antigravity theo [`.agent/rules/09-cross-agent-collaboration.md`](../../.agent/rules/09-cross-agent-collaboration.md);
  hai đường không trộn.
- Mọi giá trị định danh hạ tầng (IP, hostname, service id) **không** được chép vào file trong repo —
  repo này PUBLIC và cổng `eng/harness/check-secrets.ps1` phần 4 chặn đúng loại đó.
