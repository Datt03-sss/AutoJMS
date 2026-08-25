# Hướng dẫn triển khai DataHub lên VPS (từng bước)

> **Đối tượng:** người vận hành có quyền `sudo` trên VPS và quyền push image lên registry.
>
> **Thiết kế backend:** [docs/architecture/datahub-backend-design.vi.md](../../../docs/architecture/datahub-backend-design.vi.md)
>
> **Đang triển khai thật?** Mở [DEPLOY_EXECUTION_CHECKLIST.vi.md](./DEPLOY_EXECUTION_CHECKLIST.vi.md)
> song song — nó có thứ tự pha, cổng chặn giữa các pha, và sổ ghi digest/commit/siteId.
>

> **Nguyên tắc xuyên suốt:**
> 1. **Staging trước, production sau.** Không bao giờ deploy production khi staging chưa chạy trọn bộ smoke test.
> 2. **Không bao giờ commit secret.** `.env.staging` / `.env.production` chỉ tồn tại trên VPS, chmod `600`.
> 3. **Image luôn pin theo `@sha256`.** Tag di động (`:latest`, `:v1`) bị script từ chối.
> 4. **PostgreSQL không bao giờ publish port ra host.** Nếu bạn thấy `5432` trong `ss -ltnp`, dừng lại.

**Thời gian dự kiến:** ~60–90 phút cho staging lần đầu; ~20 phút cho production sau đó.

---

## Mục lục

| Bước | Nội dung |
|---|---|
| [0](#bước-0--chuẩn-bị-trước-khi-ssh) | Chuẩn bị trước khi SSH |
| [1](#bước-1--provision-vps) | Provision VPS |
| [2](#bước-2--hardening-hệ-điều-hành) | Hardening hệ điều hành |
| [3](#bước-3--cài-docker-engine--powershell-7) | Cài Docker Engine + PowerShell 7 |
| [4](#bước-4--dns-và-tls) | DNS và TLS |
| [5](#bước-5--build-và-push-api-image-trên-máy-dev) | Build và push API image (máy dev, hoặc §5.0 trên VPS) |
| [6](#bước-6--lấy-code-lên-vps) | Lấy code lên VPS |
| [7](#bước-7--sinh-secret-và-tạo-file-env) | Sinh secret và tạo file env |
| [8](#bước-8--khởi-động-stack) | Khởi động stack |
| [9](#bước-9--áp-migration) | Áp migration |
| [10](#bước-10--provision-site) | Provision site |
| [11](#bước-11--smoke-test) | Smoke test |
| [12](#bước-12--backup-và-diễn-tập-restore) | Backup và diễn tập restore |
| [13](#bước-13--rollback) | Rollback |
| [14](#bước-14--vận-hành-thường-ngày) | Vận hành thường ngày |
| [15](#bước-15--cutover-staging--production) | Cutover staging → production |
| [16](#bước-16--sự-cố-thường-gặp) | Sự cố thường gặp |

---

## Bước 0 — Chuẩn bị trước khi SSH

Kiểm tra bạn đã có đủ:

- [ ] VPS chưa dùng cho việc khác (staging và production nên là **hai VPS riêng**; nếu buộc dùng chung host thì phải tách bằng hai thư mục + hai project Compose khác `name:` — xem §15.3).
- [ ] Quyền tạo A record trên DNS cho hostname sẽ dùng.
- [ ] Tài khoản container registry (ví dụ `ghcr.io`) và Personal Access Token có scope `write:packages`.
- [ ] Trên máy dev: Docker Desktop, `git`, PowerShell 7 (`pwsh`), .NET 10 SDK.
- [ ] Đã chốt hostname:

| Môi trường | Hostname mẫu | `DATAHUB_CHANNEL` | `ASPNETCORE_ENVIRONMENT` |
|---|---|---|---|
| Staging | `datahub-dev.example.com` | `staging` | `Staging` |
| Production | `datahub.example.com` | `production` | `Production` |

> Hostname là **DNS name ổn định**, không phải IP. License assertion mang `datahub_url`; đổi VPS
> chỉ cần trỏ lại A record, không phải phát lại license.

---

## Bước 1 — Provision VPS

### 1.1 Cấu hình tối thiểu

| Hạng mục | Staging | Production |
|---|---|---|
| vCPU | 2 | 4 |
| RAM | 4 GB | 8 GB |
| Disk | 40 GB SSD | 80 GB SSD |
| OS | Ubuntu Server 24.04 LTS | Ubuntu Server 24.04 LTS |

Cơ sở tính RAM: `postgres` giới hạn 2 GB + `api` 768 MB + `caddy` 256 MB + OS/overhead.
4 GB là mức chạy được, 8 GB là mức có biên an toàn cho backup và pg_dump đồng thời.

### 1.2 Tạo user không phải root

Đăng nhập bằng root lần đầu, rồi:

```bash
adduser --gecos "" datahub && usermod -aG sudo datahub && mkdir -p /home/datahub/.ssh && cp /root/.ssh/authorized_keys /home/datahub/.ssh/authorized_keys && chown -R datahub:datahub /home/datahub/.ssh && chmod 700 /home/datahub/.ssh && chmod 600 /home/datahub/.ssh/authorized_keys
```

Mở **một session SSH mới** bằng user `datahub` và xác nhận `sudo -v` chạy được **trước khi**
đóng session root. Nếu không vào được, bạn còn session root để sửa.

### 1.3 Đặt timezone và hostname

```bash
sudo timedatectl set-timezone Asia/Ho_Chi_Minh && sudo hostnamectl set-hostname datahub-staging && timedatectl
```

> Timezone của host **không** ảnh hưởng tính đúng: API luôn quy đổi `scanTime` naive về
> `Asia/Ho_Chi_Minh` bằng code và lưu trữ theo UTC. Đặt timezone chỉ để log dễ đọc.
> Bắt buộc phải có: `systemd-timesyncd` đang chạy (`timedatectl` báo `System clock synchronized: yes`)
> — lease fencing dùng `clock_timestamp()` của PostgreSQL, đồng hồ lệch nhiều sẽ gây fence sai.

---

## Bước 2 — Hardening hệ điều hành

### 2.1 Cập nhật và bật vá tự động

```bash
sudo apt-get update && sudo apt-get -y upgrade && sudo apt-get install -y unattended-upgrades fail2ban ufw curl ca-certificates gnupg && sudo dpkg-reconfigure -f noninteractive unattended-upgrades
```

### 2.2 Khoá SSH về key-only

Sửa `/etc/ssh/sshd_config.d/99-datahub.conf`:

```bash
sudo tee /etc/ssh/sshd_config.d/99-datahub.conf > /dev/null <<'EOF'
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
MaxAuthTries 3
ClientAliveInterval 300
ClientAliveCountMax 2
EOF
```

Kiểm cú pháp rồi mới reload — sai cú pháp mà restart là mất đường vào máy:

```bash
sudo sshd -t && sudo systemctl reload ssh
```

**Giữ session hiện tại mở**, mở session thứ hai để xác nhận vẫn login được.

### 2.3 Firewall

```bash
sudo ufw default deny incoming && sudo ufw default allow outgoing && sudo ufw allow 22/tcp comment 'SSH' && sudo ufw allow 80/tcp comment 'ACME HTTP-01' && sudo ufw allow 443/tcp comment 'DataHub HTTPS' && sudo ufw --force enable && sudo ufw status verbose
```

Chỉ ba port. **Không** mở `5432` — nếu có ai đề nghị mở, câu trả lời là không: máy trạm dùng REST,
không dùng DB trực tiếp.

> Docker ghi trực tiếp vào `iptables` và có thể **đi vòng** qua `ufw` với các port đã publish.
> Trong Compose này chỉ `caddy` publish 80/443 (đã mở), `api` dùng `expose`, `postgres` không
> publish gì — nên rủi ro này không xuất hiện. Sau bước 8 hãy xác nhận lại bằng §2.5.

### 2.4 fail2ban cho SSH

```bash
sudo tee /etc/fail2ban/jail.d/sshd.local > /dev/null <<'EOF'
[sshd]
enabled = true
maxretry = 5
findtime = 10m
bantime = 1h
EOF
sudo systemctl enable --now fail2ban && sudo fail2ban-client status sshd
```

### 2.5 Kiểm chứng bề mặt tấn công

Chạy sau bước 8 và **mỗi lần** sửa Compose:

```bash
sudo ss -ltnp | grep -vE '127\.0\.0\.1|\[::1\]'
```

Kết quả mong đợi: chỉ `:22`, `:80`, `:443`. Nếu thấy `:5432` hoặc `:8080` ⇒ Compose bị sửa sai,
dừng và khôi phục lại `docker-compose.yml` từ git.

---

## Bước 3 — Cài Docker Engine + PowerShell 7

### 3.1 Docker Engine (repo chính thức)

```bash
sudo install -m 0755 -d /etc/apt/keyrings && sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc && sudo chmod a+r /etc/apt/keyrings/docker.asc && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null && sudo apt-get update && sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

Cho user `datahub` dùng docker không cần sudo, rồi **đăng xuất và đăng nhập lại**:

```bash
sudo usermod -aG docker datahub
```

Xác nhận:

```bash
docker version && docker compose version
```

> Thuộc nhóm `docker` tương đương quyền root trên host. Chỉ thêm user vận hành thực sự.

### 3.2 PowerShell 7

Các script vận hành trong `backend/datahub/scripts/` là PowerShell. Cài `pwsh` trên VPS:

```bash
sudo apt-get install -y wget apt-transport-https software-properties-common && wget -q "https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb && sudo dpkg -i /tmp/packages-microsoft-prod.deb && rm /tmp/packages-microsoft-prod.deb && sudo apt-get update && sudo apt-get install -y powershell && pwsh --version
```

> **Không muốn cài `pwsh`?** Mỗi bước dùng script bên dưới đều kèm mục
> *"Không có pwsh"* với lệnh `docker compose` tương đương. Nhưng script có thêm kiểm tra an toàn
> (chặn tag di động, xác minh digest, kiểm marker migration) mà lệnh thủ công không có —
> cài `pwsh` là lựa chọn được khuyến nghị.

---

## Bước 4 — DNS và TLS

### 4.1 Tạo A record

| Type | Name | Value | TTL |
|---|---|---|---|
| `A` | `datahub-dev` | `<IP công khai của VPS>` | 300 |

Chờ DNS lan truyền rồi kiểm **từ máy dev**:

```bash
dig +short datahub-dev.example.com
```

Phải trả về đúng IP VPS. **Đừng sang bước 8 trước khi DNS đúng** — Caddy sẽ xin chứng chỉ ACME
thất bại và có thể đụng rate limit của Let's Encrypt.

### 4.2 TLS

Không cần làm gì thủ công. Caddy tự xin và tự gia hạn chứng chỉ qua ACME HTTP-01, dùng
`DATAHUB_PUBLIC_HOST` và `TLS_CONTACT_EMAIL` từ file env. Điều kiện: port 80 mở (bước 2.3) và
DNS trỏ đúng (4.1).

Caddy admin API đã tắt (`admin off` trong [Caddyfile](../Caddyfile)) — không có endpoint quản trị
để lộ ra ngoài.

---

## Bước 5 — Build và push API image (trên máy dev)

Compose trên VPS **cố tình không có `build:`** — VPS chỉ tiêu thụ image đã build sẵn. Build ở nơi
có SDK, rồi pin theo digest.

### 5.0 Nếu máy dev không có Docker — build ngay trên VPS

[start-stack.ps1](../scripts/start-stack.ps1) chỉ nhận digest từ registry (nó `pull` rồi đối chiếu
`RepoDigests`), nên **bắt buộc phải push lên registry**; nhưng nơi *build* không nhất thiết là máy
dev. Nếu máy dev không có Docker Engine/Desktop, làm **bước 6 trước** (clone repo lên VPS) rồi
build tại chỗ:

```bash
cd ~/AutoJMS && docker build -f backend/datahub/Dockerfile -t ghcr.io/<owner>/autojms-datahub-api:$(date +%Y-%m-%d)-1 .
```

Sau đó `docker login` / `docker push` / lấy digest y như §5.2–§5.3 — chỉ là chạy trên VPS.

> Build context là gốc repo, nhưng [Dockerfile](../Dockerfile) chỉ `COPY src/AutoJMS.DataHub.Api/`,
> nên image không chứa code desktop.
>
> Đánh đổi: VPS phải tải image `mcr.microsoft.com/dotnet/sdk:10.0` (~1 GB) và giữ build cache.
> Cấu hình staging ở §1.1 (2 vCPU / 4 GB / 40 GB) đủ chạy; chạy `docker builder prune` sau khi
> push nếu cần lấy lại disk.
>
> **Không** thêm `build:` vào `docker-compose.yml` để "build luôn khi up" —
> [deployment-static-smoke.ps1](../tests/deployment-static-smoke.ps1) sẽ fail với
> `The VPS Compose file must consume a prebuilt image, not rebuild source.`

### 5.1 Build

Build context là **thư mục gốc repo** (Dockerfile tham chiếu nhiều project). Từ gốc repo:

```powershell
docker build -f backend/datahub/Dockerfile -t ghcr.io/<owner>/autojms-datahub-api:2026-08-22-1 .
```

### 5.2 Push

```powershell
docker login ghcr.io -u <github-user>
```

```powershell
docker push ghcr.io/<owner>/autojms-datahub-api:2026-08-22-1
```

### 5.3 Lấy digest — đây là giá trị bạn sẽ dùng

```powershell
docker image inspect ghcr.io/<owner>/autojms-datahub-api:2026-08-22-1 --format '{{index .RepoDigests 0}}'
```

Kết quả có dạng `ghcr.io/<owner>/autojms-datahub-api@sha256:<64 ký tự hex>`.
**Copy nguyên chuỗi này** — nó là giá trị của `DATAHUB_API_IMAGE` ở bước 7.

> Tag chỉ để cho người đọc. Digest là thứ định danh bytes thật.
> [start-stack.ps1](../scripts/start-stack.ps1) từ chối mọi giá trị không khớp
> `^.+@sha256:[0-9a-fA-F]{64}$`, và sau khi `pull` còn đối chiếu `RepoDigests` để chắc chắn
> image kéo về đúng digest yêu cầu.

### 5.4 Nếu registry là private

Trên VPS phải `docker login` cùng registry trước bước 8:

```bash
docker login ghcr.io -u <github-user>
```

---

## Bước 6 — Lấy code lên VPS

VPS cần `backend/datahub/` (Compose, Caddyfile, migrations, scripts). Cách gọn nhất là clone repo:

```bash
cd ~ && git clone https://github.com/Datt03-sss/AutoJMS.git && cd AutoJMS && git switch main && git log --oneline -1
```

Ghi lại commit hash — bạn sẽ cần nó khi rollback (bước 13).

> Repo private thì dùng deploy key **read-only**, không dùng token cá nhân có quyền ghi.

---

## Bước 7 — Sinh secret và tạo file env

### 7.1 Sinh secret

Sinh **trên VPS** và **riêng cho từng môi trường**. Không bao giờ copy khoá từ staging sang
production — mục đích của việc tách là để token staging bị lộ cũng không chạm được production.

```bash
for name in POSTGRES_PASSWORD DATAHUB_DEVICE_TOKEN_SIGNING_KEY DATAHUB_ENROLLMENT_PEPPER DATAHUB_STAGING_TEST_SIGNING_KEY; do printf '%s=%s\n' "$name" "$(openssl rand -hex 32)"; done
```

> Dùng hex (64 ký tự) là chủ ý: thoả yêu cầu "≥ 32 byte" của khoá ký và "≥ 32 ký tự" của pepper,
> đồng thời không chứa ký tự nào có thể làm hỏng connection string (`;`) hay khó escape trong shell.
> Lưu bản sao vào password manager **ngay lúc này** — mất khoá ký device token nghĩa là mọi thiết
> bị phải enroll lại.

### 7.2 Tạo `.env.staging`

```bash
cd ~/AutoJMS/backend/datahub && cp env.staging.template .env.staging && chmod 600 .env.staging && nano .env.staging
```

Điền toàn bộ chỗ `REPLACE_WITH_...`:

| Biến | Điền gì |
|---|---|
| `DATAHUB_PUBLIC_HOST` | `datahub-dev.example.com` |
| `TLS_CONTACT_EMAIL` | email thật để nhận cảnh báo chứng chỉ |
| `DATAHUB_API_IMAGE` | chuỗi `...@sha256:...` từ bước 5.3 |
| `POSTGRES_PASSWORD` | giá trị hex từ 7.1 |
| `DATAHUB_DEVICE_TOKEN_SIGNING_KEY` | giá trị hex từ 7.1 |
| `DATAHUB_ENROLLMENT_PEPPER` | giá trị hex từ 7.1 |
| `DATAHUB_STAGING_TEST_SIGNING_KEY` | giá trị hex từ 7.1 |
| `DATAHUB_ALLOW_STAGING_TEST_ISSUER` | để `true` (chỉ staging) |
| `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` | để **trống** ở staging (staging dùng HMAC test issuer) |
| `DATAHUB_DEVICE_TOKEN_LIFETIME_SECONDS` | để `86400`, hoặc `900` nếu muốn ép re-enroll để test |

Giữ nguyên `ASPNETCORE_ENVIRONMENT=Staging`, `DATAHUB_CHANNEL=staging`,
`POSTGRES_DB=datahub_staging`, `POSTGRES_USER=datahub_staging`.

### 7.3 Xác nhận không rò secret

```bash
cd ~/AutoJMS && git status --short && git check-ignore -v backend/datahub/.env.staging
```

`git status` **không được** hiển thị `.env.staging`; `git check-ignore` phải in ra dòng luật khớp.
Nếu file lọt vào `git status`, dừng lại và sửa `.gitignore` trước khi làm gì khác.

### 7.4 Khác biệt cho production

| Biến | Staging | Production |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| `DATAHUB_CHANNEL` | `staging` | `production` |
| `DATAHUB_ALLOW_STAGING_TEST_ISSUER` | `true` | `false` (hoặc bỏ hẳn) |
| `DATAHUB_STAGING_TEST_SIGNING_KEY` | có | **trống** |
| `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` (hoặc `_PATH`) | trống | **PEM public key của license server** |
| `DATAHUB_LICENSE_ASSERTION_ISSUER` | `autojms-license-staging` | `autojms-license-production` |
| `DATAHUB_LICENSE_ASSERTION_AUDIENCE` | `autojms-datahub-enroll-staging` | `autojms-datahub-enroll-production` |

> ⚠️ **Điều kiện go-live production:** production nạp `RsaLicenseAssertionValidator` **chỉ khi**
> có key material (`DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` hoặc `_PATH` —
> xem `AddDataHubIdentity` trong `Auth/IdentityServiceCollectionExtensions.cs:20`).
> Không có key ⇒ nạp `UnavailableLicenseAssertionValidator` và
> `POST /api/v1/devices/enroll` trả **`503 LICENSE_ASSERTION_UNAVAILABLE`**. Đây là
> *fail-closed có chủ ý*, không phải bug. Mọi bước khác (stack, migration, provision,
> health, lease, ingest bằng token phát tay) vẫn chạy được.
>
> Chỉ nạp **nửa public**. Nửa private nằm trên license server dưới tên
> `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY`; nếu VPS giữ nửa private thì một VPS bị chiếm
> có thể tự phát license.
>
> `_ISSUER`/`_AUDIENCE` phải **khớp từng ký tự** với hai biến cùng tên trên license server.
> Lệch một ký tự thì assertion ký đúng vẫn bị từ chối là `LICENSE_ASSERTION_INVALID`,
> và triệu chứng ở máy trạm chỉ là "enroll thất bại" — không nói vì sao.

---

## Bước 8 — Khởi động stack

### 8.1 Dùng script (khuyến nghị)

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/start-stack.ps1 -ComposeEnvFile .env.staging
```

Script làm tuần tự: kiểm `DATAHUB_API_IMAGE` phải là digest → `docker compose config --quiet` →
`pull api` → đối chiếu `RepoDigests` với digest yêu cầu → `up -d --no-build`.
Thành công thì in `DataHub stack started from pinned image digest <sha>`.

### 8.2 Không có pwsh

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging -f docker-compose.yml config --quiet && docker compose --env-file .env.staging -f docker-compose.yml pull api && docker compose --env-file .env.staging -f docker-compose.yml up -d --no-build
```

Bạn tự chịu trách nhiệm kiểm digest bằng tay.

### 8.3 Xác nhận trạng thái

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging ps
```

Mong đợi: `postgres` **healthy**, `api` **healthy**, `caddy` **running**.
Do `depends_on: condition: service_healthy`, thứ tự start là postgres → api → caddy; `caddy`
chưa lên có nghĩa `api` chưa healthy.

Nếu `api` không healthy:

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs --tail 80 api
```

`/health/ready` sẽ unhealthy nếu thiếu biến bắt buộc hoặc khoá ký ngắn hơn 32 byte — log sẽ chỉ rõ.

### 8.4 Chạy lại kiểm tra bề mặt tấn công (§2.5)

```bash
sudo ss -ltnp | grep -vE '127\.0\.0\.1|\[::1\]'
```

Chỉ được thấy `:22`, `:80`, `:443`.

---

## Bước 9 — Áp migration

Migration chạy **bên trong** service `postgres` qua `docker compose exec` — không cần mở port DB
và không cần cài `psql` trên host.

### 9.1 Dùng script (khuyến nghị)

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/apply-migrations.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging
```

Hành vi: tạo bảng `schema_migrations` nếu chưa có → áp mọi file khớp `^\d+_.*\.sql$` theo thứ tự
số tăng dần, **mỗi file trong `--single-transaction`** → bỏ qua version đã ghi nhận → sau mỗi file
xác minh marker version đã thực sự được ghi.

Chạy lại lệnh này là **an toàn và idempotent** — đây là cách áp migration mới sau khi update code.

### 9.2 Không có pwsh

Áp lần lượt theo đúng thứ tự (không đảo thứ tự):

```bash
cd ~/AutoJMS/backend/datahub && for f in migrations/001_core.sql migrations/002_seed_policies.sql migrations/003_seed_retention.sql migrations/004_projection_slot_payloads.sql migrations/005_change_retention_floor.sql; do echo "== $f"; docker compose --env-file .env.staging exec -T postgres sh -ec 'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 --single-transaction' < "$f"; done
```

### 9.3 Xác nhận

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres sh -ec 'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -c "SELECT version FROM schema_migrations ORDER BY version;"'
```

Phải liệt kê đủ 5 version: `001_core` … `005_change_retention_floor`.

Kiểm sâu hơn bằng bộ assert catalog:

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres sh -ec 'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1' < tests/001_core_catalog_assertions.sql
```

---

## Bước 10 — Provision site

Site **phải** tồn tại trước khi enroll thiết bị — enrollment không tự tạo site.

Sinh một UUID (giữ lại, đây là `siteId` dùng trong mọi URL API):

```bash
cat /proc/sys/kernel/random/uuid
```

### 10.1 Dùng script (khuyến nghị)

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/provision-site.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging -SiteId '<uuid vừa sinh>' -SiteCode 'HCM01'
```

Script chuẩn hoá `SiteCode` về **CHỮ IN HOA** và gọi
`create_datahub_site(site_id, site_code)` trong một transaction — hàm này seed nguyên tử cả ba
thứ: hàng `sites`, hàng `site_fetch_leases`, hàng `site_change_counters`.

> Tuyệt đối **không** tạo site bằng `INSERT INTO sites` thủ công. Thiếu `site_change_counters`
> thì mọi ingest của site đó sẽ lỗi.

### 10.2 Xác nhận

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres sh -ec 'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -c "SELECT s.site_id, s.site_code, s.seats, l.leader_term, c.change_seq, c.pruned_through_seq FROM sites s JOIN site_fetch_leases l USING (site_id) JOIN site_change_counters c USING (site_id);"'
```

Phải thấy đúng 1 hàng cho mỗi site, `change_seq = 0`, `pruned_through_seq = 0`.

---

## Bước 11 — Smoke test

Chạy **từ máy dev** (qua internet, đi đúng đường Caddy → api) để kiểm cả TLS lẫn proxy.
Đặt biến cho gọn:

```bash
HOST=https://datahub-dev.example.com; SITE=<uuid site>
```

### 11.1 Health + TLS

```bash
curl -sS -i "$HOST/health/live"
```

```bash
curl -sS "$HOST/health/ready" | tee /dev/stderr | grep -q '"channel":"staging"' && echo OK-CHANNEL
```

Mong đợi: `200`, `status: "Healthy"`, `checks` gồm `runtime-configuration` và `postgres` đều
`Healthy`, `channel` = `staging`. Chứng chỉ hợp lệ (curl không báo lỗi TLS).

### 11.2 Auth phải chặn đúng

```bash
curl -sS -o /dev/null -w '%{http_code}\n' "$HOST/api/v1/sites/$SITE/projections/snapshot"
```

Mong đợi `401`. Nếu ra `200` ⇒ **dừng deploy ngay**, auth không hoạt động.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -H 'Authorization: Bearer v1.rac.rac' "$HOST/api/v1/sites/$SITE/projections/snapshot"
```

Mong đợi `401`.

### 11.3 Phát assertion staging và enroll

Chỉ chạy được ở staging (`DATAHUB_ALLOW_STAGING_TEST_ISSUER=true` **và** `DATAHUB_CHANNEL=staging`).
Trên VPS, lấy các giá trị từ `.env.staging`:

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/issue-staging-assertion.ps1 -SigningKey '<DATAHUB_STAGING_TEST_SIGNING_KEY>' -Issuer 'autojms-license-staging' -Audience 'autojms-datahub-enroll-staging' -SiteCode 'HCM01' -Seats 2 -DataHubUrl 'https://datahub-dev.example.com'
```

`Issuer`/`Audience` **phải trùng khít** `DATAHUB_LICENSE_ASSERTION_ISSUER` /
`DATAHUB_LICENSE_ASSERTION_AUDIENCE` trong file env, nếu không sẽ ra `401`.
Assertion mặc định sống 8 giờ.

Enroll (assertion đi ở chính header `Authorization`):

```bash
ASSERTION='<chuỗi assertion vừa in ra>'
```

```bash
curl -sS -X POST "$HOST/api/v1/devices/enroll" -H "Authorization: Bearer $ASSERTION" -H 'Content-Type: application/json' -d '{"siteCode":"HCM01","deviceName":"smoke-test-1","role":"operator"}'
```

Mong đợi `201` với `deviceId`, `siteId`, `channel: "staging"`, `deviceToken`, `expiresAt`.
Lưu token:

```bash
TOKEN='<deviceToken>'
```

> **Cách đọc mã lỗi ở bước này:** `503` = validator không khả dụng (kiểm
> `DATAHUB_ALLOW_STAGING_TEST_ISSUER` và `DATAHUB_CHANNEL`); `401` = sai khoá/issuer/audience hoặc
> assertion hết hạn; `403 SITE_NOT_LICENSED` = `siteCode` không nằm trong assertion;
> `404` = site chưa provision (quay lại bước 10); `409` = hết seat hoặc trùng thiết bị.

### 11.4 Kiểm cách ly channel

Token staging **không được** dùng trên production và ngược lại. Sau khi có cả hai môi trường,
gọi endpoint production bằng token staging:

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer $TOKEN" "https://datahub.example.com/api/v1/sites/$SITE/projections/snapshot"
```

Mong đợi `403` (`CHANNEL_MISMATCH`). Nếu ra `200` ⇒ hai môi trường đang dùng chung khoá ký,
**sai nghiêm trọng** — sinh lại secret riêng cho từng môi trường (bước 7.1).

### 11.5 Snapshot và changes

```bash
curl -sS -H "Authorization: Bearer $TOKEN" "$HOST/api/v1/sites/$SITE/projections/snapshot"
```

Mong đợi `200`, `itemCount: 0`, `items: []`, có `snapshot_seq` (đúng snake_case — đây là field
duy nhất trên API không dùng camelCase).

```bash
curl -sS -H "Authorization: Bearer $TOKEN" "$HOST/api/v1/sites/$SITE/changes?after=0&limit=10"
```

Mong đợi `200`, `items: []`, `hasMore: false`, `nextAfter: 0`.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer $TOKEN" "$HOST/api/v1/sites/$SITE/changes?after=999999"
```

Mong đợi `409` (`RESYNC_REQUIRED`) — con trỏ vượt `change_seq` hiện tại.

### 11.6 Lease và fencing

```bash
curl -sS -X POST -H "Authorization: Bearer $TOKEN" "$HOST/api/v1/sites/$SITE/lease/acquire"
```

Mong đợi `200` với `leaderTerm` (ghi lại, gọi là `T`).

```bash
TERM=<T>
```

```bash
curl -sS -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "{\"leaderTerm\":$TERM}" "$HOST/api/v1/sites/$SITE/lease/renew"
```

Mong đợi `200`, `leaderTerm` **không đổi** (renew không tăng term).

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "{\"leaderTerm\":$((TERM-1))}" "$HOST/api/v1/sites/$SITE/lease/renew"
```

Mong đợi `409` (`LEADER_FENCED`) — term cũ bị chặn.

### 11.7 Ingest có fence + idempotency

```bash
KEY="smoke-$(date +%s)-0001"
```

```bash
BODY='{"items":[{"waybillNo":"SMOKE000000001","scanTime":"2026-08-22 09:15:00","code":110,"status":"ARRIVED","scanTypeName":"Arrival scan","scanByCode":"smoke","payload":{"uploadTime":"2026-08-22 09:20:00"}}]}'
```

```bash
curl -sS -X POST "$HOST/api/v1/sites/$SITE/jms/ingest" -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: $KEY" -H "X-Leader-Term: $TERM" -H 'Content-Type: application/json' -d "$BODY"
```

Mong đợi `200`: `acceptedItems: 1`, `duplicateItems: 0`, `changedProjections: 1`,
`replayed: false`, có `firstChangeSeq`/`lastChangeSeq`.

> Chỉ được dùng đúng các field có trong `JmsObservation` (`waybillNo`, `scanTime`, `code`,
> `status`, `scanTypeName`, `scanNetworkCode`, `scanByCode`, `packageNumber`, `taskCode`,
> `remark1`…`remark9`, `payload`). API cấu hình `JsonUnmappedMemberHandling.Disallow`, nên bất kỳ
> field lạ nào — kể cả gõ sai tên — đều bị **`400`** ngay ở tầng model binding, chưa vào nghiệp vụ.
> `code: 110` là `state_transition` theo seed `002_seed_policies.sql`, `98` là `inventory`, mã
> không seed mặc định là `activity`.

Gửi **lại y nguyên** (cùng key, cùng body):

```bash
curl -sS -X POST "$HOST/api/v1/sites/$SITE/jms/ingest" -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: $KEY" -H "X-Leader-Term: $TERM" -H 'Content-Type: application/json' -d "$BODY"
```

Mong đợi `replayed: true` và **`change_seq` không tăng thêm**.

Cùng key nhưng body khác:

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$HOST/api/v1/sites/$SITE/jms/ingest" -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: $KEY" -H "X-Leader-Term: $TERM" -H 'Content-Type: application/json' -d '{"items":[]}'
```

Mong đợi `409` (`IDEMPOTENCY_KEY_REUSED`).

Bulk ingest **thiếu** `X-Leader-Term`:

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$HOST/api/v1/sites/$SITE/jms/ingest" -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: smoke-nofence-0001" -H 'Content-Type: application/json' -d "$BODY"
```

Mong đợi `409` (`LEADER_FENCED`).

Sau ingest, delta phải xuất hiện:

```bash
curl -sS -H "Authorization: Bearer $TOKEN" "$HOST/api/v1/sites/$SITE/changes?after=0&limit=10"
```

Mong đợi ít nhất 1 item với `entityType: "waybill_projection"`, `operation: "upsert"`.

Nhả lease:

```bash
curl -sS -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "{\"leaderTerm\":$TERM}" "$HOST/api/v1/sites/$SITE/lease/release"
```

`leaderTerm` trả về phải **lớn hơn** `T` (release tăng term để chặn leader zombie).

### 11.8 Kiểm ràng buộc đầu vào

Field lạ ngoài schema (`JsonUnmappedMemberHandling.Disallow`):

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$HOST/api/v1/sites/$SITE/jms/observations" -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: smoke-unmapped-0001" -H 'Content-Type: application/json' -d '{"items":[],"khongTonTai":1}'
```

Mong đợi `400`.

Body vượt 1 MiB:

```bash
python3 -c "print('{\"items\":[],\"pad\":\"' + 'a'*1200000 + '\"}')" > /tmp/big.json && curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$HOST/api/v1/sites/$SITE/jms/observations" -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: smoke-big-0001" -H 'Content-Type: application/json' --data-binary @/tmp/big.json
```

Mong đợi `413`.

### 11.9 SignalR (doorbell)

Chưa có client SignalR trong desktop app (lỗ hổng #2), nên kiểm ở mức negotiate là đủ để xác nhận
Caddy proxy đúng:

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST -H "Authorization: Bearer $TOKEN" "$HOST/hubs/site/negotiate?negotiateVersion=1"
```

Mong đợi `200` với `connectionId` + danh sách `availableTransports` gồm `WebSockets`.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$HOST/hubs/site/negotiate?negotiateVersion=1"
```

Mong đợi `401` (không có token thì hub từ chối).

> Mất doorbell **không** mất dữ liệu — client vẫn lấy đủ bằng `GET /changes?after=`.
> SignalR là tối ưu độ trễ, không phải điều kiện đúng đắn.

### 11.10 Bảng tổng kết smoke test

| # | Kiểm | Mong đợi | ✔ |
|---|---|---|---|
| 1 | `/health/live` | `200` Healthy | ☐ |
| 2 | `/health/ready` | `200`, cả 2 check Healthy, `channel` đúng | ☐ |
| 3 | Snapshot không token | `401` | ☐ |
| 4 | Token rác | `401` | ☐ |
| 5 | Enroll bằng assertion staging | `201` + deviceToken | ☐ |
| 6 | Token staging gọi production | `403 CHANNEL_MISMATCH` | ☐ |
| 7 | Snapshot rỗng | `200`, `count: 0` | ☐ |
| 8 | `changes?after=999999` | `409 RESYNC_REQUIRED` | ☐ |
| 9 | Lease acquire | `200` + `leaderTerm` | ☐ |
| 10 | Renew term cũ | `409 LEADER_FENCED` | ☐ |
| 11 | Ingest có fence | `200`, `changedProjections: 1` | ☐ |
| 12 | Ingest lặp cùng key+body | `replayed: true`, seq không tăng | ☐ |
| 13 | Cùng key khác body | `409 IDEMPOTENCY_KEY_REUSED` | ☐ |
| 14 | Bulk thiếu `X-Leader-Term` | `409 LEADER_FENCED` | ☐ |
| 15 | Changes sau ingest | có item `waybill_projection` | ☐ |
| 16 | Release | term tăng | ☐ |
| 17 | Field lạ | `400` | ☐ |
| 18 | Body > 1 MiB | `413` | ☐ |
| 19 | Hub negotiate có token | `200` + WebSockets | ☐ |
| 20 | Hub negotiate không token | `401` | ☐ |
| 21 | `ss -ltnp` | chỉ 22/80/443 | ☐ |

---

## Bước 12 — Backup và diễn tập restore

### 12.1 Backup thủ công

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/backup-postgres.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging -OutputDirectory /home/datahub/backups
```

Tạo file `datahub-<UTC timestamp>.dump` ở định dạng custom, compress mức 6.

Không có pwsh:

```bash
cd ~/AutoJMS/backend/datahub && mkdir -p /home/datahub/backups && docker compose --env-file .env.staging exec -T postgres sh -ec 'exec pg_dump --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --format=custom --compress=6' > "/home/datahub/backups/datahub-$(date -u +%Y%m%dT%H%M%SZ).dump"
```

### 12.2 Backup theo lịch

```bash
sudo chmod 700 /home/datahub/backups && ( crontab -l 2>/dev/null; echo '17 2 * * * cd /home/datahub/AutoJMS/backend/datahub && /usr/bin/pwsh -File scripts/backup-postgres.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging -OutputDirectory /home/datahub/backups >> /home/datahub/backups/backup.log 2>&1' ) | crontab - && crontab -l
```

Dọn bản cũ (giữ 14 ngày):

```bash
( crontab -l 2>/dev/null; echo '40 3 * * * find /home/datahub/backups -name "datahub-*.dump" -mtime +14 -delete' ) | crontab - && crontab -l
```

> **Bản dump là dữ liệu khách hàng.** Nếu đẩy ra ngoài VPS (khuyến nghị), phải mã hoá và dùng
> **bucket riêng cho từng môi trường** — không trộn dump staging và production. Không bao giờ
> commit dump vào git.

### 12.3 Diễn tập restore — bắt buộc, không phải tuỳ chọn

Backup chưa từng restore thành công thì không phải backup. Diễn tập trên **staging**:

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres sh -ec 'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -c "SELECT count(*) AS events FROM waybill_scan_events; SELECT count(*) AS projections FROM waybill_projections;"'
```

Ghi lại số liệu, rồi restore đè:

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/restore-postgres.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging -DumpFile /home/datahub/backups/<file>.dump -AllowExistingData
```

Restore dùng `--single-transaction --exit-on-error --no-owner --no-privileges`; `--clean --if-exists`
**chỉ** được thêm khi có `-AllowExistingData`. Cờ này là hàng rào chủ ý: xoá dữ liệu hiện có phải
là hành động rõ ràng, không phải mặc định.

So lại số liệu, rồi restart api để reset connection pool:

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging restart api && sleep 25 && curl -sS https://datahub-dev.example.com/health/ready
```

Ghi lại thời gian thực tế từ lúc bắt đầu tới lúc `/health/ready` xanh.

> Mục tiêu "< 30 phút" là **mục tiêu diễn tập, không phải SLA**. Con số bạn đo được chính là
> thời gian phục hồi thật.

---

## Bước 13 — Rollback

### 13.1 Rollback image (nhanh nhất, không đụng dữ liệu)

Dùng khi bản mới lỗi nhưng schema không đổi:

```bash
cd ~/AutoJMS/backend/datahub && sed -i.bak "s|^DATAHUB_API_IMAGE=.*|DATAHUB_API_IMAGE=ghcr.io/<owner>/autojms-datahub-api@sha256:<digest CŨ>|" .env.staging && pwsh -File scripts/start-stack.ps1 -ComposeEnvFile .env.staging
```

Lưu `.env.*.bak` với chmod `600`, hoặc xoá sau khi xác nhận ổn.

> **Vì vậy phải ghi lại digest của mọi bản đã deploy.** Không có digest cũ thì không có rollback
> nhanh. Ghi vào `release/notes/` hoặc password manager cùng thời điểm deploy.

### 13.2 Rollback code vận hành (Compose/Caddyfile/migrations)

```bash
cd ~/AutoJMS && git log --oneline -5 && git switch --detach <commit tốt trước đó> && cd backend/datahub && pwsh -File scripts/start-stack.ps1 -ComposeEnvFile .env.staging
```

### 13.3 Rollback có đổi schema

Migration **chỉ tiến, không lùi** — không có script down. Quy trình:

1. Rollback image về digest cũ (13.1).
2. Nếu bản cũ không chạy được với schema mới ⇒ restore dump **trước khi migrate** (bước 12.3).
3. Chấp nhận mất dữ liệu ghi trong khoảng thời gian đó, hoặc trích xuất thủ công từ dump mới hơn.

Vì thế: **luôn backup ngay trước khi áp migration mới** ở production.

---

## Bước 14 — Vận hành thường ngày

### 14.1 Xem log

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs --tail 200 -f api
```

Log đã rotate sẵn (api 20 MB × 5, caddy 10 MB × 3, postgres 20 MB × 5) — không cần logrotate thêm.

### 14.2 Kiểm retention đang chạy

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs api | grep -i "retention" | tail -20
```

Retention chỉ log khi **thực sự xoá** cái gì đó. Không có dòng nào là bình thường với hệ mới.
Chính sách hiện tại (`retention_policies`): `waybill_scan_events` 60 ngày, `dashboard_changes`
14 ngày, `audit_logs` 90 ngày. Đổi chính sách bằng cách sửa dữ liệu bảng — nhưng **chỉ 4 đồng hồ
nằm trong allow-list của code** mới có hiệu lực, thêm hàng cho bảng khác sẽ bị bỏ qua.

Xem chính sách hiện hành:

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres sh -ec 'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -c "SELECT site_id, table_name, delete_after FROM retention_policies ORDER BY table_name, site_id NULLS FIRST;"'
```

### 14.3 Cập nhật lên bản mới

```bash
cd ~/AutoJMS && git pull --ff-only origin main
```

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/backup-postgres.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging -OutputDirectory /home/datahub/backups
```

Cập nhật `DATAHUB_API_IMAGE` sang digest mới, rồi:

```bash
cd ~/AutoJMS/backend/datahub && pwsh -File scripts/start-stack.ps1 -ComposeEnvFile .env.staging && pwsh -File scripts/apply-migrations.ps1 -ComposeFile docker-compose.yml -ComposeEnvFile .env.staging
```

Chạy lại smoke test (bước 11).

### 14.4 Xoay khoá

| Khoá | Hệ quả khi xoay | Cách làm |
|---|---|---|
| `DATAHUB_DEVICE_TOKEN_SIGNING_KEY` | **mọi device token hiện có mất hiệu lực** | đổi env → `up -d` → tất cả thiết bị enroll lại |
| `DATAHUB_ENROLLMENT_PEPPER` | `credential_hash` cũ không còn đối chiếu được | tránh xoay; nếu buộc phải, coi như enroll lại toàn bộ |
| `POSTGRES_PASSWORD` | phải đổi ở cả `postgres` và connection string | đổi trong DB trước, rồi đổi env, rồi `up -d` |
| `DATAHUB_STAGING_TEST_SIGNING_KEY` | assertion staging cũ hết hiệu lực | an toàn, chỉ ảnh hưởng staging |

Xoay khoá là hành động có downtime cho client — lên lịch, đừng làm giữa giờ làm việc.

### 14.5 Kiểm tra định kỳ (hàng tuần)

```bash
df -h / && free -m && docker compose --env-file ~/AutoJMS/backend/datahub/.env.staging -f ~/AutoJMS/backend/datahub/docker-compose.yml ps && ls -lh /home/datahub/backups | tail -5
```

| Dấu hiệu | Việc cần làm |
|---|---|
| Disk > 80% | kiểm volume `postgres_data`, dọn dump cũ, xem retention có chạy |
| `api` restart nhiều lần | xem log; có thể OOM ở mức `mem_limit: 768m` |
| Không có dump mới trong 24h | kiểm cron + `backup.log` |
| Chứng chỉ sắp hết hạn | kiểm log caddy; ACME cần port 80 mở |

---

## Bước 15 — Cutover staging → production

### 15.1 Điều kiện tiên quyết

- [ ] Toàn bộ 21 mục smoke test ở staging **xanh**.
- [ ] Đã diễn tập restore thành công trên staging, có ghi thời gian thực tế.
- [ ] Digest image đã deploy được ghi lại ở nơi tra cứu được.
- [ ] Adapter xác minh assertion bất đối xứng (JWS/JWKS) **đã có** — nếu chưa,
      production sẽ không enroll được thiết bị (`503`). Xem cảnh báo ở bước 7.4.
- [ ] Có cửa sổ bảo trì và người trực.

### 15.2 Trình tự

Lặp lại bước 1 → 11 với các thay đổi:

| Hạng mục | Giá trị production |
|---|---|
| Hostname | `datahub.example.com` |
| File env | `.env.production` (từ `env.production.template`) |
| `DATAHUB_CHANNEL` | `production` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| Secret | **sinh mới toàn bộ**, không copy từ staging |
| `DATAHUB_ALLOW_STAGING_TEST_ISSUER` | `false` |
| `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` (hoặc `_PATH`) | PEM public key của license server |
| Thư mục backup | riêng biệt, không trộn với staging |
| Image digest | **đúng digest đã kiểm ở staging** |

Bước 11.3 (issue-staging-assertion) **không áp dụng** cho production — production dùng assertion
thật từ mặt phẳng giấy phép.

Bắt buộc chạy 11.4 sau khi có cả hai môi trường: token staging gọi production phải ra `403`.

### 15.3 Nếu buộc phải dùng chung một VPS

Không khuyến nghị, nhưng nếu bắt buộc:

- Hai thư mục clone riêng biệt.
- Sửa `name:` trong Compose (`autojms-datahub`) thành hai giá trị khác nhau ⇒ volume và network
  không dùng chung.
- Chỉ **một** stack được publish 80/443. Stack còn lại phải đứng sau cùng Caddy đó với hostname
  khác — nghĩa là bạn phải sửa Caddyfile, và mất tính "cách ly hoàn toàn".
- Chấp nhận: một sự cố tài nguyên (OOM, đầy disk) ảnh hưởng **cả hai** môi trường.

### 15.4 Canary trước khi mở rộng

Triển khai **một** site và **một** máy trạm thật, để chạy ít nhất một ngày làm việc. Ghi lại:
số ingest, số duplicate, số `LEADER_FENCED`, số `RESYNC_REQUIRED`, độ trễ p95.
Chỉ mở rộng sau khi canary sạch.

---

## Bước 16 — Sự cố thường gặp

### Caddy không xin được chứng chỉ

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs --tail 60 caddy
```

| Nguyên nhân | Kiểm tra |
|---|---|
| DNS chưa trỏ đúng | `dig +short <host>` từ máy ngoài |
| Port 80 bị chặn | `sudo ufw status`; ACME HTTP-01 **cần** port 80 |
| Đụng rate limit Let's Encrypt | log caddy; chờ, và đừng restart liên tục |
| `TLS_CONTACT_EMAIL` sai | kiểm file env |

### `api` unhealthy, `/health/ready` trả 503

```bash
cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs --tail 100 api
```

| Nguyên nhân | Sửa |
|---|---|
| Thiếu biến bắt buộc | so lại với template (bước 7.2) |
| Khoá ký < 32 byte | sinh lại bằng `openssl rand -hex 32` |
| Pepper < 32 ký tự | sinh lại |
| `postgres` chưa healthy | `docker compose ps`; xem log postgres |
| Password DB không khớp | so `POSTGRES_PASSWORD` với password thật trong DB |

### `start-stack.ps1` báo "must be an immutable registry reference"

`DATAHUB_API_IMAGE` đang là tag di động. Lấy digest ở bước 5.3 và điền dạng
`repo@sha256:<64 hex>`. Đây là hàng rào chủ ý, không phải bug — tag di động khiến rollback
và điều tra sự cố không còn tin cậy.

### `start-stack.ps1` báo "Pulled API image does not match the requested digest"

Digest bạn điền không tồn tại trên registry, hoặc bạn chưa `docker login` với registry private
(bước 5.4).

### Enroll trả 503 ở staging

Kiểm cả hai điều kiện — thiếu một là fail-closed:

```bash
cd ~/AutoJMS/backend/datahub && grep -E '^(DATAHUB_CHANNEL|DATAHUB_ALLOW_STAGING_TEST_ISSUER)=' .env.staging
```

Phải là `DATAHUB_CHANNEL=staging` **và** `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true`.
Ở production, `503` là hành vi đúng hiện tại (lỗ hổng #1).

### Enroll trả 401 ở staging

`Issuer`/`Audience`/`SigningKey` truyền cho `issue-staging-assertion.ps1` không trùng với
`DATAHUB_LICENSE_ASSERTION_ISSUER` / `_AUDIENCE` / `DATAHUB_STAGING_TEST_SIGNING_KEY` trong env,
hoặc assertion đã hết hạn (mặc định 8 giờ).

### Ingest liên tục `409 LEADER_FENCED`

| Nguyên nhân | Sửa |
|---|---|
| Không renew trong 120 giây | renew mỗi 30 giây |
| Dùng term cũ sau khi acquire lại | luôn dùng term mới nhất từ response |
| Đồng hồ VPS lệch | `timedatectl` phải báo `System clock synchronized: yes` |
| Hai máy cùng tưởng mình là leader | acquire lại; term tăng sẽ tự loại máy cũ |

### Client liên tục nhận `409 RESYNC_REQUIRED`

Con trỏ nằm ngoài cửa sổ khả dụng. Nếu xảy ra thường xuyên:

- Client offline lâu hơn retention của `dashboard_changes` (14 ngày) ⇒ tăng `delete_after`
  cho `dashboard_changes`, hoặc chấp nhận client phải snapshot lại.
- Client lưu con trỏ sai (ví dụ dùng `snapshot_seq` của lần khác) ⇒ lỗi phía client.

### Đầy disk

```bash
df -h / && docker system df && docker compose --env-file ~/AutoJMS/backend/datahub/.env.staging -f ~/AutoJMS/backend/datahub/docker-compose.yml exec -T postgres sh -ec 'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -c "SELECT pg_size_pretty(pg_database_size(current_database()));"'
```

Theo thứ tự: dọn dump cũ → `docker image prune` (an toàn, image đang dùng được pin) →
kiểm retention có chạy → cân nhắc siết `delete_after`.

**Không bao giờ** `docker volume prune` — sẽ xoá `postgres_data`.

### Cần mở port 5432 để debug

Không. Dùng `docker compose exec postgres psql ...` như mọi lệnh trong tài liệu này.
Nếu cần từ máy dev, dùng SSH tunnel tạm thời và đóng ngay sau khi xong:

```bash
ssh -N -L 15432:127.0.0.1:5432 datahub@<ip-vps>
```

> Lưu ý: tunnel này **không** hoạt động với Compose hiện tại vì `postgres` nằm trong network
> `internal` và không bind lên host. Đó là thiết kế đúng. Muốn debug thì `exec` vào container.

---

## Tham chiếu nhanh

| File | Vai trò |
|---|---|
| [docker-compose.yml](../docker-compose.yml) | topology 3 service |
| [Caddyfile](../Caddyfile) | TLS + proxy + no-buffer cho SignalR |
| [Dockerfile](../Dockerfile) | build API image (context = gốc repo) |
| [env.staging.template](../env.staging.template) / [env.production.template](../env.production.template) | mẫu biến môi trường |
| [scripts/start-stack.ps1](../scripts/start-stack.ps1) | start có kiểm digest |
| [scripts/apply-migrations.ps1](../scripts/apply-migrations.ps1) | migration idempotent |
| [scripts/provision-site.ps1](../scripts/provision-site.ps1) | tạo site nguyên tử |
| [scripts/issue-staging-assertion.ps1](../scripts/issue-staging-assertion.ps1) | phát assertion test (chỉ staging) |
| [scripts/backup-postgres.ps1](../scripts/backup-postgres.ps1) / [restore-postgres.ps1](../scripts/restore-postgres.ps1) | backup / restore |
| [tests/deployment-static-smoke.ps1](../tests/deployment-static-smoke.ps1) | kiểm bất biến deployment (chạy ở CI/máy dev) |
| [openapi/datahub-v1.yaml](../openapi/datahub-v1.yaml) | hợp đồng API chuẩn |
| [README.md](../README.md) | ghi chú vận hành ngắn (EN) |
