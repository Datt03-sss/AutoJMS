# Bảng thực thi triển khai DataHub — staging trước

> **File này là lớp thực thi, không phải tài liệu tham chiếu.**
> Mọi lệnh chi tiết nằm ở [VPS_DEPLOY_GUIDE.vi.md](./VPS_DEPLOY_GUIDE.vi.md).
> Ở đây chỉ có: thứ tự làm, **cổng chặn** (không đạt thì không đi tiếp), việc **chỉ owner làm được**,
> và **sổ ghi** giá trị sinh ra trong lúc triển khai.
>
> Tick vào ô khi xong. File này được commit; **giá trị secret thì không** — sổ ghi ở §6 chỉ ghi
> những gì không phải secret (digest, commit hash, siteId).

---

## 1. Kết quả preflight (máy dev, 2026-08-22)

| Kiểm tra | Kết quả | Ảnh hưởng |
|---|---|---|
| `docker --version` / `docker compose version` | **không có** | §5 (build trên máy dev) không làm được như viết → dùng **§5.0 build trên VPS** |
| `.github/` | **không tồn tại** | không có CI build/push; build bằng tay |
| `pwsh -v` | **không có** trên máy dev | không ảnh hưởng: script chạy trên VPS, §3.2 cài PowerShell 7 |
| `powershell` (5.1) | `5.1.26100.9168` | đủ để chạy `issue-staging-assertion.ps1` tại chỗ nếu cần |
| `openssl version` | `OpenSSL 3.5.5` | có thể sinh secret trên máy dev, **nhưng vẫn nên sinh trên VPS** (§7.1) |
| Parse 11 script `.ps1` bằng PS 5.1 | 10 OK, `restore-postgres.ps1` **FAIL** | script này dùng cú pháp PS7 (ternary, dòng 26) → **chỉ chạy trên VPS** sau §3.2 |
| `openapi-lint.ps1` | PASS | contract không chặn deploy |
| `deployment-static-smoke.ps1` | PASS | asset deploy đúng invariant |

**Hai kết luận quyết định thứ tự triển khai:**

1. `start-stack.ps1` từ chối tag mutable và đối chiếu `RepoDigests` sau khi `pull`
   ⇒ **image bắt buộc phải nằm trên registry**. Build ở đâu cũng được, push thì bắt buộc.
2. `IdentityServiceCollectionExtensions` chỉ nạp validator HMAC khi
   *(staging test issuer bật)* **và** *(channel = `staging`)*; ngược lại nạp
   `UnavailableLicenseAssertionValidator` ⇒ `POST /devices/enroll` trả **`503`**.
   ⇒ **Production chưa enroll được thiết bị nào. Staging là môi trường duy nhất chạy đủ luồng.**

---

## 2. Giả định đang áp dụng

Nếu một giả định sai, sửa ở đây trước khi chạy tiếp — thứ tự bên dưới phụ thuộc vào chúng.

| # | Giả định | Nếu sai thì |
|---|---|---|
| A1 | Triển khai **staging trước**, production sau (§15) | không có đường nào khác: production enroll = 503 |
| A2 | Registry là **GHCR** (`ghcr.io/<owner>/autojms-datahub-api`) | Docker Hub / registry riêng cũng được, chỉ đổi tên image ở §5 và §7.2 |
| A3 | **Build trên VPS** (§5.0), vì máy dev không có Docker | cài Docker Desktop trên máy dev rồi làm §5.1–5.3 như gốc |
| A4 | Có **một tên miền thật** trỏ được A record về IP VPS | không có miền ⇒ Caddy không xin được cert ACME. Dùng dynamic-DNS có A record, hoặc `tls internal` (chỉ để test nội bộ, client phải tin CA nội bộ) |
| A5 | OS là **Ubuntu Server 24.04 LTS**, user không phải root tên `datahub` | distro khác thì §2–§3 phải đổi lệnh. Xem §2b nếu VPS đang là 20.04 |

### 2b. Nếu VPS đang chạy Ubuntu 20.04

20.04 **chạy được** nhưng kém hơn ở ba điểm đo được:

| Hạng mục | Ubuntu 20.04 (focal) | Ubuntu 24.04 (noble) |
|---|---|---|
| Hỗ trợ bảo mật OS | hết hỗ trợ tiêu chuẩn 05/2025 — cần Ubuntu Pro/ESM | tới 2029 |
| Docker CE mới nhất trong repo | **28.1.1** (Docker dừng build cho focal) | **29.7.x** |
| `libseccomp2` | 2.4.3 ở bản gốc, **2.5.1** qua `focal-updates` | 2.5.5 |

`libseccomp2` là điểm dễ vỡ nhất: glibc ≥ 2.34 (image .NET 10, alpine mới) gọi `clone3`;
libseccomp < 2.5 trả `EPERM` thay vì `ENOSYS` nên container chết với
`Operation not permitted`. Bản `focal-updates` đã đủ, nhưng phải `apt upgrade` trước —
[bootstrap-vps.sh](./bootstrap-vps.sh) kiểm điều này và **dừng** nếu chưa đạt.

**Khuyến nghị:** VPS còn trống thì cài lại bằng **Ubuntu 24.04 LTS** — mất ~10 phút và
xoá cả ba rủi ro trên. Nếu buộc phải giữ 20.04: bật Ubuntu Pro (miễn phí ≤ 5 máy) để có
ESM, và chấp nhận Docker Engine đứng ở 28.1.1.

## 3. Việc chỉ owner làm được

Tuyệt đối không giao cho agent — liên quan tới thông tin xác thực và tài nguyên trả phí:

- [ ] Tạo/chọn VPS, lấy IP, mở SSH bằng key (không mật khẩu) — §1
- [ ] `docker login ghcr.io -u <github-user>` (nhập PAT `write:packages` bằng tay) — §5.2
- [ ] Tạo A record tại nhà cung cấp DNS — §4.1
- [ ] Sinh secret bằng `openssl rand -hex 32` và **lưu vào password manager** — §7.1
- [ ] Điền `.env.staging` (file này không bao giờ được commit) — §7.2

---

## 4. Trình tự thực thi

Mỗi pha có **cổng chặn**: không đạt thì dừng, không đi pha sau.

### Pha 0 — Nền hệ thống

> **Tự động hoá:** [bootstrap-vps.sh](./bootstrap-vps.sh) làm trọn Pha 0 + Pha 1 trong một lệnh
> (idempotent, chạy lại được). Nó **từ chối** khoá SSH khi user chưa có `authorized_keys`, để
> không tự đẩy bạn ra khỏi máy. Chạy tay theo §1–§3 vẫn được nếu muốn kiểm từng bước.

- [ ] §1.1 VPS đạt tối thiểu 2 vCPU / 4 GB / 40 GB (staging)
- [ ] §1.2 user `datahub` có sudo, đã mở **session mới** xác nhận `sudo -v` trước khi rời root
- [ ] §1.3 timezone `Asia/Ho_Chi_Minh`, `timedatectl` báo `System clock synchronized: yes`
- [ ] §2.1–2.4 unattended-upgrades, SSH key-only, UFW (chỉ 22/80/443), fail2ban
- [ ] Đổi mật khẩu root nếu nó từng được gõ/dán ở bất kỳ đâu ngoài password manager

> **Cổng 0:** §2.5 — `ss -tulpn` **không** được thấy `5432` mở ra ngoài; đồng hồ đã sync
> (lease fencing dùng `clock_timestamp()`, lệch đồng hồ ⇒ fence sai).

### Pha 1 — Runtime + mã nguồn

- [ ] §3.1 Docker Engine từ repo chính thức, `docker run hello-world` chạy
- [ ] §3.2 PowerShell 7 (`pwsh -v`) — **bắt buộc**, `restore-postgres.ps1` không parse trên PS 5.1
- [ ] §6 `git clone` repo, `git switch main`, ghi commit hash vào §6 bên dưới

> **Cổng 1:** `pwsh -v` in ra `7.x` và `docker compose version` chạy được với user `datahub`
> (đã `newgrp docker` hoặc logout/login lại).

### Pha 2 — DNS + TLS (làm trước khi `up`)

- [ ] §4.1 A record `datahub-dev.<miền>` → IP VPS; `dig +short` từ máy dev trả đúng IP
- [ ] Port 80 và 443 mở (ACME HTTP-01 cần 80)

> **Cổng 2:** DNS đã propagate. Caddy khởi động trước khi DNS đúng sẽ ăn rate-limit của
> Let's Encrypt và phải chờ.

### Pha 3 — Image

- [ ] §5.0 `docker build -f backend/datahub/Dockerfile -t ghcr.io/<owner>/autojms-datahub-api:<ngày>-1 .` **trên VPS**, từ gốc repo
- [ ] §5.2 owner `docker login ghcr.io` rồi `docker push`
- [ ] §5.3 `docker image inspect ... --format '{{index .RepoDigests 0}}'` → ghi digest vào §6
- [ ] (nếu package private) giữ nguyên phiên `docker login` trên VPS — §5.4

> **Cổng 3:** digest khớp `^.+@sha256:[0-9a-fA-F]{64}$`. Không có digest thì
> `start-stack.ps1` throw ngay dòng đầu.

### Pha 4 — Cấu hình + khởi động

- [ ] §7.1 sinh 4 secret trên VPS, lưu password manager
- [ ] §7.2 `cp env.staging.template .env.staging && chmod 600 .env.staging`, điền hết `REPLACE_WITH_`
- [ ] §7.3 `git status --short` **không** thấy `.env.staging`; `git check-ignore -v` in ra luật khớp
- [ ] §8.1 `pwsh -File scripts/start-stack.ps1 -ComposeEnvFile .env.staging`
- [ ] §8.3 `docker compose ps`: `postgres` healthy, `api` healthy, `caddy` running
- [ ] §8.4 chạy lại §2.5 — bề mặt tấn công không đổi sau khi container lên

> **Cổng 4:** cả 3 container ở trạng thái mong đợi **và** `5432` vẫn không lộ ra host.
> `caddy` chưa lên nghĩa là `api` chưa healthy — đọc log `api` trước, đừng sửa Caddy.

### Pha 5 — Schema + site

- [ ] §9.1 `apply-migrations.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging`
- [ ] §9.3 `SELECT version FROM schema_migrations` liệt kê đủ **5** version (`001_core` … `005_change_retention_floor`)
- [ ] §9.3 chạy `tests/001_core_catalog_assertions.sql` không lỗi
- [ ] §10 sinh UUID (`cat /proc/sys/kernel/random/uuid`) → ghi vào §6
- [ ] §10.1 `provision-site.ps1 -SiteId <uuid> -SiteCode HCM01`
- [ ] §10.2 truy vấn join 3 bảng: đúng 1 hàng, `change_seq = 0`, `pruned_through_seq = 0`

> **Cổng 5:** site phải có đủ **cả ba** hàng (`sites`, `site_fetch_leases`,
> `site_change_counters`). Thiếu `site_change_counters` ⇒ mọi ingest của site đó lỗi.
> Chỉ tạo site bằng `create_datahub_site(...)`, không `INSERT INTO sites` tay.

### Pha 6 — Smoke test (chạy từ máy dev, qua internet)

- [ ] §11.1 `/health/live` 200; `/health/ready` `Healthy` + `channel: "staging"`; TLS không lỗi
- [ ] §11.2 không token → `401`; token rác → `401` (**ra `200` thì dừng deploy**)
- [ ] §11.3 phát assertion staging → enroll `201`, có `deviceToken`
- [ ] §11.5 snapshot `200` `itemCount: 0` có `snapshot_seq`; changes `hasMore: false`, `nextAfter: 0`
- [ ] §11.6 lease: acquire `200` → acquire lần 2 `409 LEASE_HELD` → renew giữ `leaderTerm`
- [ ] §11.7 ingest có `X-Leader-Term` + `Idempotency-Key`: `acceptedItems: 1`, `changedProjections: 1`; gửi lại đúng body → `replayed: true`; thiếu `X-Leader-Term` → `409 LEADER_FENCED`
- [ ] §11.8 field sai tên → `400`; `limit` ngoài khoảng → clamp; `after` quá cũ → `409 RESYNC_REQUIRED`
- [ ] §11.9 SignalR `/hubs/site` negotiate được, nhận doorbell sau ingest
- [ ] §11.10 đối chiếu bảng tổng kết — **mọi dòng phải khớp**

> **Cổng 6:** một dòng không khớp = một invariant bị vỡ. Ghi lại mã lỗi thực tế rồi tra §16
> trước khi sửa cấu hình.

### Pha 7 — Backup / restore / vận hành

- [ ] §12.1 `backup-postgres.ps1` tạo được dump, kiểm dung lượng > 0
- [ ] §12.2 cron backup theo lịch + kiểm quyền file dump (`chmod 600`)
- [ ] §12.3 **diễn tập restore** — bắt buộc, cần `pwsh` (PS 5.1 không parse được script này)
- [ ] §14.5 lịch kiểm tra hàng tuần đã đặt vào calendar

> **Cổng 7:** chưa diễn tập restore thành công thì **chưa được coi là đã triển khai**.
> Backup chưa từng restore là backup chưa tồn tại.

### Pha 8 — Production (chỉ khi §15.1 đủ điều kiện)

- [ ] Đã có adapter xác minh assertion bất đối xứng (JWS/JWKS) — **nếu chưa, dừng ở đây**
- [ ] §15.2 trình tự cutover, secret sinh mới hoàn toàn (không copy từ staging)
- [ ] §11.4 token staging gọi production → `403 CHANNEL_MISMATCH`

---

## 5. Chuỗi lệnh ngắn nhất từ Pha 0 → Pha 5

Thay `<owner>`, `<uuid>`, `<ip>` bằng giá trị thật.

**Pha 0 + 1 — chạy bằng root, một lệnh:**

```bash
curl -fsSL https://raw.githubusercontent.com/Datt03-sss/AutoJMS/main/backend/datahub/deploy/bootstrap-vps.sh -o /tmp/bootstrap-vps.sh && less /tmp/bootstrap-vps.sh
```

Đọc xong rồi mới chạy — đừng bao giờ `curl | bash` một script chưa đọc:

```bash
bash /tmp/bootstrap-vps.sh --hostname datahub-staging --user datahub
```

Cài khoá SSH từ **máy dev**, rồi mới khoá cửa:

```bash
ssh-copy-id datahub@<ip>
```

```bash
bash /tmp/bootstrap-vps.sh --hostname datahub-staging --user datahub --harden-ssh --yes
```

**Pha 1 tiếp — đăng nhập bằng `datahub` (không phải root) để có group docker:**

```bash
cd ~ && git clone https://github.com/Datt03-sss/AutoJMS.git && cd AutoJMS && git switch main && git log --oneline -1
```

```bash
cd ~/AutoJMS && docker build -f backend/datahub/Dockerfile -t ghcr.io/<owner>/autojms-datahub-api:$(date +%Y-%m-%d)-1 .
```

```bash
docker push ghcr.io/<owner>/autojms-datahub-api:$(date +%Y-%m-%d)-1 && docker image inspect ghcr.io/<owner>/autojms-datahub-api:$(date +%Y-%m-%d)-1 --format '{{index .RepoDigests 0}}'
```

```bash
cd ~/AutoJMS/backend/datahub && cp env.staging.template .env.staging && chmod 600 .env.staging && nano .env.staging
```

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/start-stack.ps1 -ComposeEnvFile .env.staging && pwsh -File scripts/apply-migrations.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging
```

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/provision-site.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging -SiteId '<uuid>' -SiteCode 'HCM01'
```

> `docker login ghcr.io` phải do owner chạy trước lệnh `docker push` — không đưa PAT vào script.

---

## 6. Sổ ghi triển khai

> ⚠️ **Repo `Datt03-sss/AutoJMS` là repo PUBLIC.** Không commit IP VPS, hostname, tên user,
> đường dẫn backup hay bất cứ thứ gì chỉ đường tới hạ tầng. Giữ sổ ghi ở **password manager
> hoặc ghi chú riêng tư**, không phải trong git. Bảng dưới là *mẫu để copy ra ngoài*, cố ý
> để trống trong repo.

| Hạng mục | Ghi ở đâu |
|---|---|
| Ngày triển khai | ghi chú riêng tư |
| IP VPS / hostname / user vận hành | ghi chú riêng tư — **không** commit |
| Commit hash đã deploy | ghi chú riêng tư (tra lại được bằng `git log` trên VPS) |
| `DATAHUB_API_IMAGE` (digest) | ghi chú riêng tư — cần cho rollback (§13.1) |
| `siteId` (UUID) / `siteCode` | ghi chú riêng tư |
| Ngày diễn tập restore gần nhất | ghi chú riêng tư |
| Ngày xoay khoá gần nhất | ghi chú riêng tư |
| Mật khẩu, khoá ký, pepper, deviceToken | **chỉ** password manager |

---

## 7. Tham chiếu

- Từng bước chi tiết: [VPS_DEPLOY_GUIDE.vi.md](./VPS_DEPLOY_GUIDE.vi.md)
- Thiết kế backend: [datahub-backend-design.vi.md](../../../docs/architecture/datahub-backend-design.vi.md)
- Sơ đồ: [datahub-backend-diagrams.md](../../../docs/architecture/datahub-backend-diagrams.md)
- Contract: [openapi/datahub-v1.yaml](../openapi/datahub-v1.yaml)
- Sự cố thường gặp: [VPS_DEPLOY_GUIDE.vi.md §16](./VPS_DEPLOY_GUIDE.vi.md#bước-16--sự-cố-thường-gặp)
