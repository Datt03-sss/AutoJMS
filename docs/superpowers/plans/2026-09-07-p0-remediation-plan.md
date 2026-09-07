# Kế hoạch Khắc phục P0 — DataHub Staging

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Đưa các gate P0 G1/G3/G4/G5/G6/G7/G8 từ trạng thái "PASS trên giấy" về trạng thái có bằng chứng thật đo được, để Owner ký OD-1/OD-2/OD-6 trên dữ liệu thật thay vì trên 2 dòng smoke-test.

**Architecture:** Không đụng một dòng code ứng dụng. Ba nhánh công việc chạy song song được: (A) khôi phục HTTPS + domain cho staging rồi chạy trọn 3.3/3.4 và đường ký RS256; (B) reset staging sạch rồi chạy lại backup/restore/smoke bằng **đúng script có sẵn trong repo**, và đo baseline bằng **log của PostgreSQL** thay vì số ước lượng phía client; (C) ~~lấy từ vựng scan thật cho OD-1 từ `journey_history.db` trên máy trạm~~ — **nhánh C đã huỷ ngày 08/09** khi Owner ký OD-A = A3: client store đang bị thiết kế cho biến mất, và DataHub không thể chứa scan event trước P4, nên trong P0 **không tồn tại nguồn nào** để ký OD-1.

**Tech Stack:** PostgreSQL 16-alpine (Docker Compose), Caddy + ACME HTTP-01, .NET 10 minimal API, PowerShell 7 (`pwsh`) trên Ubuntu 24.04, SQLite/SQLCipher phía client, bash + python3 trên VPS.

---

## Global Constraints

Mọi task đều ngầm bao gồm các ràng buộc dưới đây. Vi phạm bất kỳ dòng nào ⇒ **DỪNG và báo cáo Owner**, không tự xử lý.

- **P1 vẫn 🔒 KHOÁ.** Kế hoạch này chỉ phục vụ nghiệm thu P0. Không tạo `waybill_tombstones`, không thêm 8 cột P1, không seed `jms_event_policies`, không seed `retention_policies('waybill_projections')`.
- **Không sửa code ứng dụng** (`src/**`). Không chạy migration ngoài `apply-migrations.ps1`/`apply-migrations.sh` của repo.
- **Không viết script ad-hoc trên VPS.** Nếu một script của repo không chạy được như mô tả → dừng, báo cáo sự không khớp. Ngoại lệ duy nhất là Task 6 và chỉ khi Owner ký **OD-C**, và khi đó file phải được **commit vào repo để review**, không phải tạo trên VPS rồi xoá.
- **Repo `Datt03-sss/AutoJMS` là PUBLIC.** Địa chỉ IP VPS, tên tài khoản SSH, đường dẫn khoá, ID container, ngưỡng fail2ban/UFW **không được xuất hiện trong bất kỳ file tracked nào**. Chúng chỉ nằm trong `backend/vps/VPS_STATUS_REPORT.private.md` (đã được `.gitignore` bắt qua pattern `*.private.md`). Hostname `dev.jmsauto.online` đã có sẵn trong 16 file tracked nên được phép viết ra.
- **Không commit** `.env*`, service account key, `*.pfx`, `*.pem`, hay bất kỳ file token/khoá nào. Token trong log mask dạng `first4...last4`.
- **Không `git add .`.** Luôn `git add` từng path cụ thể.
- **Không force push, không rewrite history.**
- Mọi thao tác chỉ chạy trên **staging**. Không chạy tải, không chạy `smoke-test.sh` (nó ghi row thật) lên bất kỳ môi trường nào khác.
- Trong toàn bộ tài liệu này, `$VPS` là biến shell đọc từ file private; không bao giờ viết giá trị thật ra file tracked.

---

## Ba phát hiện làm thay đổi đề xuất của Antigravity

Đề xuất khắc phục của Antigravity đúng về tinh thần (nhận đủ B1→B8) nhưng **cả ba việc đều dựa trên giả định sai**. Kiểm chứng lại từ code và DNS:

### PH-1 — Cả hai cách lấy bằng chứng OD-1 của Antigravity đều bất khả thi

Antigravity đề xuất *"Cách A: chạy `od1_scan_vocabulary.sql` trên Database Production"* hoặc *"Cách B: dump Production về staging"*. Không cách nào tồn tại:

| Bằng chứng | Kết quả |
|---|---|
| `nslookup datahub.jmsauto.online` | **NXDOMAIN** — chưa có DataHub production nào được deploy |
| Bộ nhớ dự án | Cả hai VPS đều greenfield, dựng mới |
| [`DataHubClient.cs:300-301`](src/AutoJMS/Data/DataHubClient.cs#L300) | `AppendWaybillEventsAsync(...) => AuxiliaryEntityNotSupported("events", events)` |
| `grep scan_type_code` trong `DataHubClient.cs` + `DataHubSyncService.cs` | **0 kết quả** |

Nghĩa là: client **không có đường dây nào** để đẩy scan event lên DataHub. Bảng `waybill_scan_events` ở **bất kỳ** instance DataHub nào cũng chỉ có thể chứa row do `smoke-test.sh` ghi, cho tới khi P4 (write cutover) hoàn thành. Đây không phải "staging còn trống" — đây là **giới hạn kiến trúc theo thiết kế**. Chạy `od1_scan_vocabulary.sql` trên production, nếu production có tồn tại, vẫn trả về đúng 0 dòng thật.

**Nguồn duy nhất đang thật sự chứa từ vựng scan** là máy trạm:

- [`JourneyHistoryDbConnectionFactory.cs:19`](src/AutoJMS/FullStack/LocalDb/JourneyHistoryDbConnectionFactory.cs#L19) — `%UserDataDir%\FullStack\journey_history.db`
- [`JourneyHistoryDbInitializer.cs:47-59`](src/AutoJMS/FullStack/LocalDb/JourneyHistoryDbInitializer.cs#L47) — bảng `journey_history` có `scan_type_name` và `raw_json`
- [`WaybillJourneyJsonParser.cs:186`](src/AutoJMS/FullStack/Services/WaybillJourneyJsonParser.cs#L186) — response JMS có trường `code`, được giữ nguyên trong `raw_json`

⇒ Task 7 thay thế hoàn toàn "Việc 1" của Antigravity. *(Cập nhật 08/09: Owner ký OD-A = A3 — client store cũng đang bị thiết kế cho biến mất, nên Task 7 bị huỷ luôn và OD-1 hoãn sang sau P4. Xem "Quyết định đã ký".)*

### PH-2 — Câu hỏi G6 đã có sẵn câu trả lời trong repo

Antigravity hỏi *"Anh có tên miền để gán TLS không, hay ký miễn trừ?"*. Không cần hỏi:

- `dev.jmsauto.online` phân giải về **đúng VPS staging đang dùng** (đã xác minh bằng `nslookup`).
- [`backend/vps/VPS_STATUS_REPORT.md`](backend/vps/VPS_STATUS_REPORT.md) cập nhật 26/08/2026 ghi rõ staging **đã từng chạy** Caddy + TLS ACME HTTP-01 cho chính hostname đó, `smoke-test.sh` **24/24 PASS qua HTTPS công khai**, và một đợt diễn tập restore **dùng đúng script thật của repo** đã hoàn tất.

⇒ Đợt dựng lại VPS ngày 07/09 đã **hạ cấp** một staging HTTPS đang chạy tốt xuống IP trần HTTP, rồi báo G6 PASS; và đã làm lại bằng script ad-hoc một bài diễn tập restore vốn đã đạt trước đó bằng script thật. Không cần domain mới, không cần waiver — chỉ cần **khôi phục lại cấu hình đã từng chạy**. Task 2.

> ⚠️ **Câu hỏi chưa có lời đáp:** DB staging ngày 26/08 có chứa dữ liệu gì bị mất trong đợt dựng lại không? Bằng chứng nghiêng về "không có dữ liệu JMS thật" (xem PH-1), nhưng **chưa xác nhận được**. Ghi nhận vào báo cáo, không kết luận.

### PH-3 — `pg_stat_statements` không đo được thứ mà gate yêu cầu

Antigravity đề xuất *"Bật `pg_stat_statements` để DB tự thống kê latency"*. Ba vấn đề:

1. **Không có percentile.** `pg_stat_statements` chỉ cho `min_exec_time`, `max_exec_time`, `mean_exec_time`, `stddev_exec_time`. Gate G7/G8 yêu cầu **p95**. Không suy ra được p95 từ mean+stddev nếu không giả định phân phối — mà latency đuôi dài thì giả định đó sai.
2. **Bắt buộc restart postmaster.** Nó cần `shared_preload_libraries`, không nạp nóng được.
3. **Buộc phải sửa `docker-compose.yml`**, cụ thể là khối tham số `-c` của service postgres — nơi có sẵn cảnh báo *"ONE setting in five lines… Change them together or not at all."*

**Công cụ đúng:** `log_min_duration_statement = 0` + `log_lock_waits = on`, đặt qua `ALTER SYSTEM` + `pg_reload_conf()` — **SIGHUP, không restart, không sửa compose file** — cho ra thời lượng thật của từng câu lệnh, tính p95 thật được. Lock wait lấy từ một session `psql` **giữ mở liên tục** lấy mẫu `pg_locks`/`pg_stat_activity`, không phải `docker exec` mỗi lần lấy mẫu (chi phí `docker exec` lớn hơn chính đại lượng cần đo). Task 5.

---

## Quyết định Owner cần chốt trước khi chạy

Ba quyết định này chặn Task 6 và Task 7. Task 1→5 chạy được ngay, không cần chờ.

> **ĐÃ KÝ 2026-09-08.** OD-A = **A3** · OD-B = **B1** · OD-C = **C1**. Chi tiết ngay dưới bảng.

| Mã | Nội dung | Lựa chọn | Khuyến nghị |
|---|---|---|---|
| **OD-A** | Nguồn bằng chứng OD-1 (xem PH-1) | **A1.** Đọc `journey_history.db` trên máy trạm bằng `sqlcipher` CLI + khoá DPAPI (khoá hiện ra trên màn hình operator một lần).<br>**A2.** Đổi tên DB cũ, chạy app với `AUTOJMS_DB_ENCRYPTION=0` để tạo cache plaintext mới, thu thập ≥14 ngày rồi đọc bằng `sqlite3`.<br>**A3.** Hoãn OD-1 sang sau P4, P1 chạy với `jms_event_policies` rỗng. | **A1 nếu DB hiện đang plaintext** (Task 7 Step 1 sẽ biết); nếu đã mã hoá thì **A2**, vì A1 buộc phải xử lý khoá mã hoá trần. A3 là lối thoát cuối — nó đẩy rủi ro xoá nhầm dữ liệu sang P6. |
| **OD-B** | Khôi phục HTTPS staging (xem PH-2) | **B1.** Trỏ lại `dev.jmsauto.online` + Caddy ACME như cấu hình 26/08.<br>**B2.** Ký waiver 3.3/3.4 cho staging. | **B1.** Hostname đã phân giải đúng VPS, cấu hình đã từng chạy — chi phí gần bằng 0. Waiver là trả tiền để mất bằng chứng. |
| **OD-C** | Bộ đo tải cho G7/G8 | **C1.** Cho phép commit một harness đo tải vào `backend/datahub/tests/baseline_load.py`, được review như code thường.<br>**C2.** Đánh dấu G7/G8 là **NOT MEASURED**, hoãn sang P7. | **C1.** Baseline đo **sau** khi P1 đổi schema thì vô giá trị — không còn mốc để so. Nhưng đây là ngoại lệ với luật "không script mới" nên phải do Owner ký, không tự quyết. Nếu Owner chọn C2 thì bỏ Task 6, ghi thẳng `NOT MEASURED` vào báo cáo — **tuyệt đối không** ghi lại số cũ. |

---

## Quyết định đã ký — 2026-09-08

**OD-A = A3 — hoãn OD-1 sang sau P4.** Owner không chọn A1 hay A2 mà bác bỏ tiền đề của cả hai:

> *"Mục tiêu là loại bỏ lưu trữ dữ liệu trên máy client nên sẽ không tồn tại `journey_history.db`."*

Hai bằng chứng độc lập cùng chỉ về A3:
1. **Kiểm chứng trên máy trạm (08/09):** `C:\AutoJMS\` là bản cài Velopack thật, `AppData\` tồn tại nhưng **không có thư mục `FullStack\`**; tìm khắp C: và D: không ra file `journey_history.db` nào. Nhánh A1 không có đối tượng để đọc.
2. **Định hướng kiến trúc của Owner:** file đó đang bị thiết kế cho biến mất, nên nhánh A2 (thu thập plaintext ≥14 ngày trên máy trạm) là đầu tư vào một kho dữ liệu sắp bị gỡ.

Hệ quả bắt buộc phải ghi vào báo cáo Task 8:
- **G1 = FAIL có chủ đích.** Không ai ký OD-1 trong P0. Không được ghi lại 2 dòng `98/110` do `smoke-test.sh` sinh ra như thể là từ vựng thật (đó chính là lỗi B1).
- **Bỏ Task 7.** Không tạo `backend/datahub/tests/od1_scan_vocabulary_client.sql` — nó là trình đọc cho một DB sẽ không tồn tại, commit vào repo PUBLIC là commit code chết.
- **P1 chạy với `jms_event_policies` rỗng.** Rủi ro dồn sang **P6**: không có danh sách terminal scan code đã ký thì không được phép bật purge projection. P6 phải coi đây là điều kiện chặn.
- `backend/datahub/tests/od1_scan_vocabulary.sql` (đã commit ở `3b11163`) giữ nguyên — nó là công cụ OD-1 **duy nhất còn giá trị**, và chỉ chạy được sau khi P4 mở đường ghi `waybill_scan_events`.

**OD-B = B1 — khôi phục HTTPS + domain.** Task 2 chạy. `--base https://dev.jmsauto.online` ở Task 4 và Task 6 giữ nguyên như đã viết.

**OD-C = C1 — cho phép commit harness đo tải.** Task 6 chạy. `backend/datahub/tests/baseline_load.py` được commit vào repo và review như code thường; **không** tạo trên VPS rồi xoá. Đây là ngoại lệ duy nhất với luật "không viết script mới", do Owner ký đè.

**Quyền thực thi:** Owner cho phép subagent tự trị chạy toàn bộ thao tác hạ tầng trên VPS, kể cả `docker compose down -v`, sửa `.env.staging` và restart stack.

---

## File Structure

| File | Trạng thái | Trách nhiệm |
|---|---|---|
| `backend/vps/VPS_STATUS_REPORT.private.md` | sửa (untracked, gitignored) | Nơi duy nhất chứa IP/định danh VPS |
| `backend/vps/VPS_STATUS_REPORT.md` | sửa (tracked, đã redact) | Ghi nhận việc hạ cấp HTTPS và khôi phục |
| `backend/datahub/tests/od1_scan_vocabulary_client.sql` | **KHÔNG tạo** — OD-A = A3 | Đã huỷ: `journey_history.db` đang bị thiết kế cho biến mất, trình đọc nó là code chết |
| `backend/datahub/tests/baseline_load.py` | tạo mới — OD-C = C1 **đã ký 08/09** | Harness đo tải duy trì, đa device, có pacing |
| `docs/review/p0-report-2026-09-07.md` | tạo mới | Báo cáo P0 cuối cùng, verdict cơ học từng gate |
| `backend/datahub/tests/od1_scan_vocabulary.sql` | **không đổi** | Đã commit ở `3b11163`; giữ nguyên cho lần chạy sau P4 |
| `backend/datahub/docker-compose.yml` | **không đổi** | PH-3 loại bỏ lý do phải sửa file này |

---

### Task 1: Đưa định danh VPS ra khỏi tầm với của git

**Files:**
- Modify: `backend/vps/VPS_STATUS_REPORT.private.md` (untracked)
- Verify: toàn bộ working tree

- [ ] **Step 1: Ghi định danh vào file private**

Thêm vào `backend/vps/VPS_STATUS_REPORT.private.md`, điền giá trị thật vào các chỗ `<…>` (file này gitignored nên được phép chứa giá trị thật; **kế hoạch này thì không**):

```markdown
## Staging VPS — 2026-09-07

- IP: <địa chỉ IP VPS staging>
- Hostname công khai: dev.jmsauto.online (A record đã trỏ đúng IP này — xác minh ở Task 2 Step 1)
- SSH: <user>@<IP>, khoá riêng tại <đường dẫn khoá SSH>
- Repo trên VPS: ~/AutoJMS
- Env file: backend/datahub/.env.staging

<!-- Bốn dòng dưới đây là nguồn cho các biến shell ở Step 3. Giữ nguyên định dạng. -->
VPS_IP=<địa chỉ IP VPS staging>
VPS_USER=<user SSH>
VPS_KEY=<đường dẫn khoá SSH>
```

- [ ] **Step 2: Xác nhận `.gitignore` thật sự chặn file này**

```bash
cd "D:/v1.2605.2(new-test)" && git check-ignore -v backend/vps/VPS_STATUS_REPORT.private.md
```

Expected: `.gitignore:114:*.private.md	backend/vps/VPS_STATUS_REPORT.private.md`

- [ ] **Step 3: Nạp biến môi trường dùng cho mọi task sau**

Chạy một lần mỗi phiên terminal. Biến được **đọc từ file private**, không gõ giá trị thật vào bất kỳ file nào khác:

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'echo ok'
```

Expected: `ok`

Mọi lệnh `ssh` ở các task sau đều viết dưới dạng `ssh -i "$SSHK" "$SSHU@$VPS"`. Nếu thấy một địa chỉ IP viết thẳng ở bất kỳ đâu trong repo ⇒ đó là lỗi, sửa trước khi commit.
- [ ] **Step 4: Xác nhận IP chưa từng lọt vào git**

```bash
cd "D:/v1.2605.2(new-test)" && git grep -n -F "$VPS" -- . ; echo "exit=$?"
```

Expected: không in ra dòng nào, `exit=1`. Nếu có bất kỳ dòng nào ⇒ **DỪNG**, báo Owner: IP đã nằm trong lịch sử repo public và cần xử lý riêng trước khi làm tiếp.

- [ ] **Step 5: Xác nhận IP cũng không nằm trong file untracked sắp được add**

```bash
cd "D:/v1.2605.2(new-test)" && git ls-files --others --exclude-standard | while IFS= read -r f; do grep -lF "$VPS" -- "$f" 2>/dev/null; done; echo "done"
```

Expected: chỉ `backend/vps/VPS_STATUS_REPORT.private.md` được liệt kê (file này đã bị `.gitignore` bắt nên không thể `add` nhầm), hoặc không có gì. Bất kỳ file nào khác ⇒ chuyển giá trị đó sang file private trước khi đi tiếp.

---

### Task 2: Khôi phục HTTPS + domain cho staging (G6 mục 3.3, 3.4)

**Files:**
- Modify trên VPS: `~/AutoJMS/backend/datahub/.env.staging` (không tracked, không copy về repo)
- Modify: `backend/vps/VPS_STATUS_REPORT.md`

**Interfaces:**
- Produces: `https://dev.jmsauto.online` phục vụ được API — Task 3, 4, 6 đều dùng base URL này.

> Task này chỉ chạy khi **OD-B = B1**. Nếu Owner chọn B2, bỏ qua Task 2, ghi `3.3/3.4 = WAIVED (Owner ký ngày …)` vào báo cáo Task 8, và mọi `--base` ở các task sau dùng `http://127.0.0.1` chạy từ trong VPS.

- [ ] **Step 1: Xác nhận A record vẫn trỏ đúng VPS**

```bash
nslookup dev.jmsauto.online
```

Expected: `Address:` khớp đúng `VPS_IP` trong file private. Nếu khác ⇒ DỪNG, báo Owner (DNS đã bị đổi kể từ 26/08).

- [ ] **Step 2: Xác nhận cổng 80 và 443 mở được cho ACME**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'sudo ss -lntp | grep -E ":(80|443)\s" ; sudo ufw status | head -20'
```

Expected: thấy 80/tcp và 443/tcp ở trạng thái ALLOW. Nếu UFW chặn 80 thì ACME HTTP-01 sẽ thất bại — mở trước khi đi tiếp.

- [ ] **Step 3: Đặt hostname công khai vào env file của stack**

Caddy lấy site address trực tiếp từ `{$DATAHUB_PUBLIC_HOST}` ([`Caddyfile:6`](backend/datahub/Caddyfile#L6)) và email ACME từ `{$TLS_CONTACT_EMAIL}` ([`Caddyfile:2`](backend/datahub/Caddyfile#L2)). Giá trị phải là **hostname trần, không có scheme** — đúng như `env.staging.template:14`. Đây chính là nguyên nhân gốc của B3: khi `DATAHUB_PUBLIC_HOST` là một địa chỉ IP, Caddy không thể xin chứng chỉ ACME cho IP nên phục vụ HTTP trần.

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && grep -n "^DATAHUB_PUBLIC_HOST=\|^TLS_CONTACT_EMAIL=" .env.staging'
```

Expected: thấy `DATAHUB_PUBLIC_HOST=<địa chỉ IP>` (hoặc một giá trị không phải hostname). Sửa hai dòng thành:

```
DATAHUB_PUBLIC_HOST=dev.jmsauto.online
TLS_CONTACT_EMAIL=<email của Owner>
```

Không thêm `https://`, không thêm dấu `/` cuối — `Caddyfile` dùng giá trị này làm site address nguyên văn.

- [ ] **Step 4: Khởi động lại stack và chờ ACME cấp chứng chỉ**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging up -d && sleep 45 && docker compose --env-file .env.staging logs caddy --tail 40'
```

Expected: log Caddy có `certificate obtained successfully` cho `dev.jmsauto.online`. Nếu thấy `too many failed authorizations` ⇒ đã chạm rate limit Let's Encrypt, chờ theo hướng dẫn trong log, không thử lại liên tục.

- [ ] **Step 5: 3.3 — chứng chỉ hợp lệ, chuỗi tin cậy đầy đủ**

```bash
curl -sS -o /dev/null -w '%{http_code} %{ssl_verify_result}\n' https://dev.jmsauto.online/health/ready
```

Expected: `200 0` — `ssl_verify_result=0` nghĩa là chuỗi chứng chỉ verify thành công, đây chính là bằng chứng 3.3.

- [ ] **Step 6: 3.4 — hostname trong chứng chỉ khớp**

```bash
echo | openssl s_client -connect dev.jmsauto.online:443 -servername dev.jmsauto.online 2>/dev/null | openssl x509 -noout -subject -issuer -dates -ext subjectAltName
```

Expected: `subject=CN=dev.jmsauto.online`, `issuer` là Let's Encrypt, `subjectAltName` chứa `DNS:dev.jmsauto.online`, `notAfter` còn hạn. Đây là bằng chứng 3.4.

- [ ] **Step 7: 3.1 — cổng 5432 vẫn bị chặn từ ngoài**

```bash
timeout 10 bash -c "cat < /dev/null > /dev/tcp/$VPS/5432" 2>&1; echo "exit=$?"
```

Expected: `exit=124` (timeout) hoặc `Connection refused`. Bất kỳ kết nối thành công nào ⇒ **DỪNG NGAY**, đây là lỗ hổng bảo mật.

- [ ] **Step 8: 3.6 — postgres không bind ra host**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging ps --format "{{.Service}}\t{{.Ports}}"'
```

Expected: dòng `postgres` chỉ có `5432/tcp`, **không có** `0.0.0.0:5432->5432/tcp`.

- [ ] **Step 9: Ghi nhận vào status report (đã redact)**

Trong `backend/vps/VPS_STATUS_REPORT.md`, thêm mục ghi rõ: staging bị dựng lại ngày 07/09/2026 làm mất cấu hình HTTPS của bản 26/08, đã khôi phục về `https://dev.jmsauto.online`, kèm ngày hết hạn chứng chỉ. **Không viết IP.**

- [ ] **Step 10: Commit**

```bash
cd "D:/v1.2605.2(new-test)" && git add backend/vps/VPS_STATUS_REPORT.md && git commit -m "docs(vps): record the staging HTTPS regression and its restoration"
```

---

### Task 3: Reset staging về trạng thái sạch (B8)

**Files:** không sửa file nào trong repo.

**Interfaces:**
- Produces: DB staging chỉ chứa schema + row do migration sinh ra; 0 row `BENCH*`. Task 4 và Task 6 đều cần điểm xuất phát này.

> Reset bằng cách huỷ volume rồi migrate lại, **không** viết câu `DELETE` thủ công nào. Staging là môi trường dùng-rồi-bỏ; một `DELETE ... LIKE 'BENCH%'` viết tay là đúng loại lệnh mà P0 cấm.

- [ ] **Step 1: Đếm rác trước khi xoá, để có số đối chứng**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c "SELECT (SELECT count(*) FROM sites) AS sites, (SELECT count(*) FROM sites WHERE site_code LIKE '"'"'BENCH%'"'"') AS bench_sites, (SELECT count(*) FROM waybill_scan_events) AS events;"'
```

Expected: ghi lại 3 con số. Theo báo cáo của Antigravity, `events` khoảng ~700 và `bench_sites` ≥ 1.

- [ ] **Step 2: Huỷ stack kèm volume**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging down -v'
```

Expected: các container bị removed, volume dữ liệu bị removed.

- [ ] **Step 3: Dựng lại stack**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging up -d && sleep 20 && docker compose --env-file .env.staging ps'
```

Expected: postgres và api ở trạng thái `running`/`healthy`.

- [ ] **Step 4: Chạy migration bằng script của repo**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'pwsh -File ~/AutoJMS/backend/datahub/scripts/apply-migrations.ps1 -ComposeFile ~/AutoJMS/backend/datahub/docker-compose.yml -ComposeEnvFile ~/AutoJMS/backend/datahub/.env.staging'
```

Expected: 6/6 migration applied, không lỗi.

- [ ] **Step 5: Chạy lại preflight và đòi 9/9 PASS**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -f /dev/stdin' < backend/datahub/tests/p0_preflight.sql
```

Expected: mục 1.9 in ra 9 dòng `G2.1` → `G2.9`, cột `verdict` **toàn bộ là `PASS`**. Bất kỳ dòng `*** STOP ***` nào ⇒ DỪNG, báo cáo, không "sửa tạm".

- [ ] **Step 6: Xác nhận DB đã sạch rác**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c "SELECT (SELECT count(*) FROM sites WHERE site_code LIKE '"'"'BENCH%'"'"') AS bench_sites, (SELECT count(*) FROM waybill_scan_events) AS events;"'
```

Expected: `bench_sites = 0`, `events = 0`.

---

### Task 4: Chạy lại G3/G4/G5 bằng đúng script của repo, và đường ký RS256 (B4, B5, B6)

**Files:** không sửa file nào trong repo.

**Interfaces:**
- Consumes: stack sạch từ Task 3; `https://dev.jmsauto.online` từ Task 2.
- Produces: một file dump có kích thước thật, một con số RTO có chú thích trung thực, và bằng chứng smoke chạy qua đường RS256.

- [ ] **Step 1: Tạo dữ liệu nền để dump không rỗng**

Chạy `smoke-test.sh` một lần trên stack sạch để có ít nhất một site + lease + event thật:

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && ./scripts/smoke-test.sh --env-file .env.staging --base https://dev.jmsauto.online'
```

Expected: `===== SMOKE RESULT: 24 passed, 0 failed =====`

- [ ] **Step 2: G3 — backup bằng `backup-postgres.ps1` (chế độ compose, không cần `pg_dump` trên PATH)**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'pwsh -File ~/AutoJMS/backend/datahub/scripts/backup-postgres.ps1 -OutputDirectory ~/datahub-backups -ComposeFile ~/AutoJMS/backend/datahub/docker-compose.yml -ComposeEnvFile ~/AutoJMS/backend/datahub/.env.staging && ls -lh ~/datahub-backups/'
```

Expected: một file `datahub-<yyyyMMddTHHmmssZ>.dump`. Ghi lại **kích thước thật** — đây là số phải đi kèm mọi phát biểu về RTO.

- [ ] **Step 3: G4 — restore bằng `restore-postgres.ps1`**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'DUMP=$(ls -t ~/datahub-backups/*.dump | head -1) && time pwsh -File ~/AutoJMS/backend/datahub/scripts/restore-postgres.ps1 -DumpFile "$DUMP" -ComposeFile ~/AutoJMS/backend/datahub/docker-compose.yml -ComposeEnvFile ~/AutoJMS/backend/datahub/.env.staging -AllowExistingData -Confirm:$false'
```

Expected: kết thúc không lỗi. `time` in ra `real` — đây là **thời gian restore thật**, tách bạch khỏi thời gian boot container.

- [ ] **Step 4: G5 — smoke lại trên DB đã restore**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && ./scripts/smoke-test.sh --env-file .env.staging --base https://dev.jmsauto.online'
```

Expected: `===== SMOKE RESULT: 24 passed, 0 failed =====`

- [ ] **Step 5: Ghi RTO kèm điều kiện biên, không ghi trần con số**

Trong ghi chép, viết đúng dạng:

> RTO đo được = `<real>` giây **trên dump `<kích thước>`, DB staging với `<N>` row**. Đây **không phải** RTO của production; con số này chỉ dùng để chứng minh quy trình restore chạy được, không dùng cho kế hoạch thảm hoạ.

- [ ] **Step 6: B6 — kiểm chứng đường ký RS256**

`smoke-test.sh` tự tài liệu hoá 3 chế độ assertion ở đầu file. Chế độ 2 là đường production. Cần stack chạy với `DATAHUB_ALLOW_STAGING_TEST_ISSUER=false` và khoá riêng RSA đưa vào qua **đường dẫn file**, không qua command line:

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && grep -c "DATAHUB_ALLOW_STAGING_TEST_ISSUER=false" .env.staging && grep -c "DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE" .env.staging'
```

Expected: cả hai đều `1`. Nếu là `0` ⇒ đặt hai biến này vào `.env.staging` (đường dẫn khoá nằm ngoài repo), `docker compose up -d` lại, rồi chạy tiếp. **Không** bật lại `true` để làm cho test xanh — chính file smoke cảnh báo điều đó.

- [ ] **Step 7: Chạy smoke qua đường RS256**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && ./scripts/smoke-test.sh --env-file .env.staging --base https://dev.jmsauto.online 2>&1 | tail -30'
```

Expected: `== step 0: choose assertion mode ==` in ra chế độ **rs256** (không phải `hmac`), và kết thúc `24 passed, 0 failed`. Đây là bằng chứng B6.

---

### Task 5: Bật đo lường phía server, không restart, không sửa compose (PH-3)

**Files:** không sửa file nào trong repo. Toàn bộ thay đổi nằm trong `postgresql.auto.conf` do `ALTER SYSTEM` ghi, và được hoàn tác ở Task 6 Step 9.

**Interfaces:**
- Produces: log PostgreSQL chứa thời lượng thật của từng câu lệnh — nguồn duy nhất để tính p95 thật ở Task 6.

- [ ] **Step 1: Ghi lại giá trị hiện tại để hoàn tác được**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c "SELECT name, setting FROM pg_settings WHERE name IN ('"'"'log_min_duration_statement'"'"','"'"'log_lock_waits'"'"','"'"'deadlock_timeout'"'"','"'"'log_line_prefix'"'"');"'
```

Expected: ghi lại 4 giá trị. Mặc định thường là `log_min_duration_statement = -1`, `log_lock_waits = off`.

- [ ] **Step 2: Bật ghi log thời lượng và lock wait — chỉ SIGHUP**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c "ALTER SYSTEM SET log_min_duration_statement = 0; ALTER SYSTEM SET log_lock_waits = on; ALTER SYSTEM SET log_line_prefix = '"'"'%m [%p] %a '"'"'; SELECT pg_reload_conf();"'
```

Expected: `ALTER SYSTEM` × 3, rồi `pg_reload_conf` trả `t`.

- [ ] **Step 3: Xác nhận đã có hiệu lực và postmaster **không** khởi động lại**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c "SELECT name, setting, pending_restart FROM pg_settings WHERE name IN ('"'"'log_min_duration_statement'"'"','"'"'log_lock_waits'"'"'); SELECT pg_postmaster_start_time();"'
```

Expected: `log_min_duration_statement = 0`, `log_lock_waits = on`, cả hai `pending_restart = f`, và `pg_postmaster_start_time()` vẫn là thời điểm cũ (chứng minh không restart, không mất connection pool).

- [ ] **Step 4: Kiểm chứng log thật sự có số thời lượng**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c "SELECT pg_sleep(0.25);" && docker compose --env-file .env.staging logs postgres --tail 5'
```

Expected: log có dòng dạng `duration: 25x.xxx ms  statement: SELECT pg_sleep(0.25);`. Nếu không thấy `duration:` ⇒ DỪNG, log đang bị nuốt bởi cấu hình logging collector; báo cáo, không đi tiếp bằng số ước lượng.

---

### Task 6: Đo baseline G7/G8 bằng tải duy trì và số liệu phía server

**Files:**
- Create: `backend/datahub/tests/baseline_load.py`

> **OD-C = C1, Owner ký 08/09 — task này CHẠY, và `baseline_load.py` được commit vào repo để review như code thường** (không tạo trên VPS rồi xoá).
>
> ⚠️ **Step 9 là bắt buộc dù task kết thúc thế nào.** Nó hoàn tác cấu hình log mà Task 5 bật; nếu bỏ, `log_min_duration_statement = 0` sẽ ghi log vô hạn và làm đầy đĩa VPS. Nếu task hỏng giữa chừng, vẫn phải chạy Step 9 bằng tay trước khi báo cáo.

**Interfaces:**
- Consumes: DB sạch (Task 3), HTTPS (Task 2), log duration (Task 5).
- Produces: p95 thật tính từ log postgres, throughput duy trì thật, lock wait thật.

**Ràng buộc bắt buộc của harness** — rút ra từ code, không phải phỏng đoán:

| Ràng buộc | Nguồn | Hệ quả cho harness |
|---|---|---|
| IP limiter: 600 permit / cửa sổ 1 phút | [`IngressRateLimitMiddleware.cs:11-14`](src/AutoJMS.DataHub.Api/Auth/IngressRateLimitMiddleware.cs#L11) | Tải duy trì phải **< 10 req/s** tính trên toàn bộ máy chạy tải, nếu không sẽ dính 429 giữa chừng |
| Device limiter: 240 permit / phút, tính **theo device** | [`IngressRateLimitMiddleware.cs:16-23`](src/AutoJMS.DataHub.Api/Auth/IngressRateLimitMiddleware.cs#L16) | Mỗi request device tiêu **2 permit** (một ở IP bucket, một ở device bucket qua `DeviceStatusMiddleware` + `.RequireRateLimiting("device")`) |
| Enrollment: 10 permit / phút / IP | `Program.cs` policy `"enrollment"` | Enroll device **một lần**, có nghỉ giữa các lần, rồi **tái sử dụng token**. Đây chính là lý do đợt đo trước dính 429 khi enroll 10 device |
| `/jms/ingest` đòi header `X-Leader-Term` | [`IngestEndpoints.cs:57-59`](src/AutoJMS.DataHub.Api/Endpoints/IngestEndpoints.cs#L57) | Nhóm B phải giữ lease và gửi kèm term, nếu không sẽ nhận 409 `LeaderFenced` |
| Cửa sổ cố định, không phải rate duy trì | `FixedWindowRateLimiter` | Một burst 50 request **không** chứng minh throughput. Bài đo phải kéo dài **≥ 120 giây** |

- [ ] **Step 1: Chuẩn bị site và assertion — mỗi site một leader riêng**

`lease/acquire` là **một leader trên một site**: device thứ hai acquire sẽ fence device thứ nhất và làm term nhảy. Nên chế độ `bulk` phải trải trên **nhiều site**, mỗi site đúng một device. Chế độ `interactive` không bị ràng buộc này vì `/jms/observations` không fenced, nhưng dùng chung cấu trúc cho gọn.

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && for i in $(seq 1 10); do pwsh -File ./scripts/provision-site.ps1 -SiteCode "BENCH$(printf %02d $i)" -ComposeFile ./docker-compose.yml -ComposeEnvFile .env.staging; sleep 2; done'
```

Đối chiếu tên tham số thật của `provision-site.ps1` trước khi chạy (`pwsh -File ./scripts/provision-site.ps1 -?`). Nếu chữ ký khác ⇒ **DỪNG**, báo cáo sự không khớp, không suy đoán.

Sinh assertion cho 10 site bằng script của repo, ghi ra file **ngoài repo**:

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && for i in $(seq 1 10); do pwsh -File ./scripts/issue-staging-assertion.ps1 -SiteCode "BENCH$(printf %02d $i)"; done > ~/bench-assertions.txt && wc -l ~/bench-assertions.txt'
```

Expected: 10 dòng. File này chứa assertion ký thật — **không** đưa vào repo, xoá sau khi đo xong.

- [ ] **Step 2: Viết harness**

Tạo `backend/datahub/tests/baseline_load.py` với đúng nội dung sau. Chỉ dùng thư viện chuẩn — VPS không cài thêm package.

```python
#!/usr/bin/env python3
"""baseline_load.py — paced, multi-device sustained load for the P0 G7/G8 baseline.

This measures CLIENT-SIDE latency and sustained throughput only. Transaction p95,
commit time and counter lock wait are NOT measurable from here and are deliberately
absent: they come from the PostgreSQL log and pg_locks (plan Task 6 Steps 6 and 8).
Reporting a client number under a server-side name is exactly the defect this
harness exists to avoid.

Rate limits this must stay under (IngressRateLimitMiddleware.cs):
  IP bucket     600 permits / 1 min fixed window  -> keep total under 10 req/s
  device bucket 240 permits / 1 min, per device   -> spread across devices
  enrollment     10 permits / 1 min, per IP       -> enroll once, paced, reuse
"""
import argparse, json, random, statistics, string, sys, threading, time, urllib.error, urllib.request

def call(base, method, path, token=None, body=None, extra=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base.rstrip("/") + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    for k, v in (extra or {}).items():
        req.add_header(k, v)
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            payload = resp.read().decode("utf-8", "replace")
            return resp.status, payload, (time.perf_counter() - started) * 1000
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), (time.perf_counter() - started) * 1000
    except Exception as e:                                   # noqa: BLE001
        return 0, repr(e), (time.perf_counter() - started) * 1000

def enroll(base, site_code, assertion, index):
    """Enrol one device. 429 here means the 10/min enrollment window is full:
    back off and retry rather than silently running with fewer devices."""
    for attempt in range(6):
        status, body, _ = call(base, "POST", "/api/v1/devices/enroll", token=assertion,
                               body={"siteCode": site_code, "deviceName": f"bench-{index}", "role": "operator"})
        if status == 201:
            parsed = json.loads(body)
            return parsed["deviceToken"], parsed["siteId"]
        if status == 429:
            wait = 15 * (attempt + 1)
            print(f"enroll {site_code}: 429, backing off {wait}s", file=sys.stderr)
            time.sleep(wait)
            continue
        raise SystemExit(f"enroll {site_code} failed: HTTP {status} {body}")
    raise SystemExit(f"enroll {site_code} still rate-limited after 6 attempts")

def acquire_lease(base, site_id, token):
    status, body, _ = call(base, "POST", f"/api/v1/sites/{site_id}/lease/acquire", token=token)
    if status != 200:
        raise SystemExit(f"lease acquire failed for {site_id}: HTTP {status} {body}")
    return json.loads(body)["leaderTerm"]

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--base", required=True)
    p.add_argument("--sites", required=True, help="comma-separated site codes")
    p.add_argument("--assertions-file", required=True, help="one assertion per line, same order as --sites")
    p.add_argument("--mode", choices=["interactive", "bulk"], required=True)
    p.add_argument("--concurrency", type=int, default=10)
    p.add_argument("--duration-seconds", type=int, default=120)
    p.add_argument("--target-rps", type=float, default=8.0)
    args = p.parse_args()

    site_codes = [s.strip() for s in args.sites.split(",") if s.strip()]
    assertions = [l.strip() for l in open(args.assertions_file, encoding="utf-8") if l.strip()]
    if len(assertions) != len(site_codes):
        raise SystemExit(f"{len(site_codes)} sites but {len(assertions)} assertions")

    devices = []
    for i, (code, assertion) in enumerate(zip(site_codes, assertions)):
        token, site_id = enroll(args.base, code, assertion, i)
        term = acquire_lease(args.base, site_id, token) if args.mode == "bulk" else None
        devices.append({"site_id": site_id, "token": token, "term": term})
        time.sleep(7)                       # stay inside the 10/min enrollment window
    print(f"prepared {len(devices)} devices", file=sys.stderr)

    endpoint = "jms/ingest" if args.mode == "bulk" else "jms/observations"
    scan_date = time.strftime("%Y-%m-%d", time.gmtime())
    interval = len(devices) / args.target_rps if args.target_rps > 0 else 0
    lock = threading.Lock()
    latencies, counts = [], {"sent": 0, "ok": 0, "http_429": 0, "http_409": 0, "other_errors": 0}
    deadline = time.time() + args.duration_seconds

    def worker(slot):
        alphabet = string.ascii_uppercase + string.digits
        while time.time() < deadline:
            cycle = time.perf_counter()
            dev = devices[slot % len(devices)]
            suffix = "".join(random.choices(alphabet, k=10))
            headers = {"Idempotency-Key": f"bench-{suffix}"}       # 16 chars: inside the 8..128 rule
            if dev["term"] is not None:
                headers["X-Leader-Term"] = str(dev["term"])
            body = {"items": [{"waybillNo": f"BENCH-{suffix}", "scanTime": f"{scan_date} 10:00:00",
                               "code": 110, "status": "Arrived", "scanTypeName": "state_transition",
                               "payload": {"src": "baseline"}}]}
            status, text, ms = call(args.base, "POST",
                                    f"/api/v1/sites/{dev['site_id']}/{endpoint}",
                                    token=dev["token"], body=body, extra=headers)
            with lock:
                counts["sent"] += 1
                if status == 200:
                    counts["ok"] += 1
                    latencies.append(ms)
                elif status == 429:
                    counts["http_429"] += 1
                elif status == 409:
                    counts["http_409"] += 1
                else:
                    counts["other_errors"] += 1
                    if counts["other_errors"] <= 3:
                        print(f"HTTP {status}: {text[:200]}", file=sys.stderr)
            # Pace so the FLEET hits --target-rps. Without this the run measures how
            # fast a burst drains, which is not a sustained throughput at all.
            slack = interval - (time.perf_counter() - cycle)
            if slack > 0:
                time.sleep(slack)

    started = time.time()
    threads = [threading.Thread(target=worker, args=(i,)) for i in range(args.concurrency)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    elapsed = time.time() - started

    def pct(values, q):
        return round(statistics.quantiles(values, n=100)[q - 1], 1) if len(values) > 100 else None

    print(json.dumps({
        "mode": args.mode, "concurrency": args.concurrency, "devices": len(devices),
        "duration_seconds": round(elapsed, 1), **counts,
        "sustained_rps": round(counts["sent"] / elapsed, 1),
        "client_latency_p50_ms": pct(latencies, 50),
        "client_latency_p95_ms": pct(latencies, 95),
        "client_latency_p99_ms": pct(latencies, 99),
        "note": "client-side latency only; server p95 comes from the postgres log",
    }, indent=2))

if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Xác nhận harness không phát minh ra metric phía server**

```bash
cd "D:/v1.2605.2(new-test)" && grep -nE "commit_p95|transaction_p95|counter_lock_wait|\* *0\.85" backend/datahub/tests/baseline_load.py; echo "exit=$?"
```

Expected: không có dòng nào, `exit=1`. Ba tên này là ba nhãn bị gán sai ở lỗi B2; chúng chỉ được phép xuất hiện trong kết quả của Step 5 và Step 6.

- [ ] **Step 4: Commit harness trước khi chạy, để nó được review**

```bash
cd "D:/v1.2605.2(new-test)" && git add backend/datahub/tests/baseline_load.py && git commit -m "test(datahub): add a paced, multi-device sustained load harness for the P0 baseline"
```

- [ ] **Step 5: Đồng bộ lên VPS bằng git, không scp file rời**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS && git pull --ff-only origin main && git log --oneline -1'
```

Expected: commit vừa tạo xuất hiện.

- [ ] **Step 6: Mở sampler lock wait trong **một** session giữ liên tục**

Mở một terminal riêng, giữ chạy suốt bài đo. Một session duy nhất, không `docker exec` mỗi lần lấy mẫu:

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -At -c "\timing off" -c "SELECT 1" -c "\watch 0.2" ' &
```

Rồi lấy mẫu lock wait thật bằng truy vấn dưới đây trong cùng session (`\watch 0.2`):

```sql
SELECT now(), count(*) FILTER (WHERE NOT granted) AS waiting,
       coalesce(max(EXTRACT(epoch FROM now() - a.query_start) * 1000)
                FILTER (WHERE NOT l.granted), 0) AS max_wait_ms
  FROM pg_locks l
  JOIN pg_stat_activity a USING (pid)
 WHERE l.relation = 'site_change_counters'::regclass;
```

Expected: khi tải chạy, `waiting` > 0 ở một số mẫu. Nếu **toàn bộ** mẫu đều `waiting = 0` thì kết luận đúng là *"không quan sát được tranh chấp lock ở mức tải này"* — **không phải** `p50 = 0ms` như một phép đo.

- [ ] **Step 7: Chạy 4 bài đo, mỗi bài 120 giây**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && SITES=$(seq -f "BENCH%02g" 1 10 | paste -sd,) && for m in interactive bulk; do for c in 10 50; do echo "== $m c=$c =="; python3 tests/baseline_load.py --base https://dev.jmsauto.online --sites "$SITES" --assertions-file ~/bench-assertions.txt --concurrency $c --duration-seconds 120 --target-rps 8 --mode $m; sleep 90; done; done'
```

Expected: mỗi bài in JSON với `http_429 = 0`, `http_409 = 0`, `sustained_rps` ≈ 8, và `client_latency_p95_ms` có giá trị (không phải `null` — `null` nghĩa là dưới 100 mẫu, chạy lại với `--duration-seconds` lớn hơn).

`sleep 90` giữa các bài là để cửa sổ rate limit hồi đầy — bỏ nó đi thì bài sau sẽ dính 429 vì cửa sổ trước. Nếu `http_409` > 0 ở chế độ `bulk` thì lease đã bị fence: một tiến trình khác đã acquire lease trên cùng site, dừng lại và tìm nguyên nhân thay vì chạy tiếp.

- [ ] **Step 8: Tính p95 **thật** từ log postgres**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs postgres --since 15m --no-log-prefix | grep -oP "duration: \K[0-9.]+" | python3 -c "import sys,statistics as s; v=sorted(float(x) for x in sys.stdin); print(f\"n={len(v)} p50={s.quantiles(v,n=100)[49]:.1f}ms p95={s.quantiles(v,n=100)[94]:.1f}ms p99={s.quantiles(v,n=100)[98]:.1f}ms max={v[-1]:.1f}ms\") if len(v)>100 else print(f\"CHƯA ĐỦ MẪU: n={len(v)}\")"'
```

Expected: `n` vài nghìn, kèm p50/p95/p99. Nếu in ra `CHƯA ĐỦ MẪU` ⇒ log đã bị xoay vòng mất; tăng `--since` hoặc chạy lại. **Không** suy p95 từ số phía client.

- [ ] **Step 9: Hoàn tác cấu hình log**

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c "ALTER SYSTEM RESET log_min_duration_statement; ALTER SYSTEM RESET log_lock_waits; ALTER SYSTEM RESET log_line_prefix; SELECT pg_reload_conf();"'
```

Expected: `pg_reload_conf` trả `t`. `log_min_duration_statement = 0` để lâu sẽ làm phình đĩa — bước này bắt buộc.

- [ ] **Step 10: Dọn rác do bài đo sinh ra**

Lặp lại Task 3 Step 2 → Step 6 để đưa DB về sạch. Sau Task 6, staging **phải** trở về trạng thái không có row `BENCH*`.

Xoá luôn file assertion, nó chứa chữ ký license thật:

```bash
ssh -i "$SSHK" "$SSHU@$VPS" 'rm -f ~/bench-assertions.txt && ls ~/bench-assertions.txt 2>&1'
```

Expected: `No such file or directory`.

---

### Task 7: Bằng chứng OD-1 thật, lấy từ nguồn duy nhất có nó (B1, PH-1)

**Files:**
- Create: `backend/datahub/tests/od1_scan_vocabulary_client.sql`

> ## ⛔ TASK NÀY ĐÃ BỊ HUỶ — OD-A = A3, ký 2026-09-08
>
> **Không thực thi bất kỳ step nào bên dưới. Không tạo `od1_scan_vocabulary_client.sql`.**
> Owner chốt rằng `journey_history.db` sẽ không tồn tại (mục tiêu bỏ lưu trữ dữ liệu ở máy client), và
> kiểm chứng ngày 08/09 xác nhận máy trạm hiện không có file đó. Toàn bộ nội dung dưới đây giữ lại
> làm hồ sơ vì sao P0 không ký được OD-1 — **đó là tài liệu, không phải việc cần làm.**
> Task 8 ghi `OD-1 = HOÃN SAU P4`, `G1 = FAIL có chủ đích`, và cảnh báo P6 không được bật purge.

**Interfaces:**
- Consumes: `%UserDataDir%\FullStack\journey_history.db` trên máy trạm.
- Produces: bảng từ vựng scan thật để Owner ký OD-1.

- [ ] **Step 1: Xác định DB đang mã hoá hay plaintext — bước này quyết định đi nhánh A1 hay A2**

```bash
sqlite3 "$LOCALAPPDATA/../AutoJMS/FullStack/journey_history.db" "SELECT count(*) FROM journey_history;" 2>&1
```

Đường dẫn chính xác lấy từ `AppPaths.UserDataDir`; nếu chưa chắc, tìm bằng:

```bash
find "$USERPROFILE" -name journey_history.db 2>/dev/null | head
```

Expected — một trong hai:
- Ra một con số ⇒ DB **plaintext**, đi tiếp Step 3 luôn, không cần xử lý khoá.
- `Error: file is not a database` ⇒ DB đã **SQLCipher hoá** ([`LocalDbEncryption.cs`](src/AutoJMS/FullStack/LocalDb/LocalDbEncryption.cs)), phải qua Step 2.

- [ ] **Step 2: (chỉ khi đã mã hoá) Mở khoá theo nhánh Owner đã ký**

Khoá là 32 byte ngẫu nhiên, bọc DPAPI ở scope `LocalMachine`, nằm tại `AppData\secure\fullstack-db.key` ([`LocalDbEncryption.cs:22-30`](src/AutoJMS/FullStack/LocalDb/LocalDbEncryption.cs#L22)). Vì là `LocalMachine`, **chỉ mở được trên đúng máy trạm đó** — copy file DB sang máy khác là vô ích.

**Nhánh A1** — cần `sqlcipher` CLI. Lấy khoá dạng hex bằng PowerShell trên máy trạm:

```powershell
Add-Type -AssemblyName System.Security; $b=[IO.File]::ReadAllBytes("$env:LOCALAPPDATA\..\AutoJMS\secure\fullstack-db.key"); $e=[Text.Encoding]::UTF8.GetBytes("AutoJMS.FullStack.LocalDb.v1"); [Convert]::ToHexString([Security.Cryptography.ProtectedData]::Unprotect($b,$e,'LocalMachine'))
```

Rồi mở DB — khoá phải là câu lệnh **đầu tiên** trên connection:

```
sqlcipher journey_history.db
sqlite> PRAGMA key = "x'<HEX>'";
sqlite> .read od1_scan_vocabulary_client.sql
```

⚠️ Khoá hiện nguyên văn trên màn hình. **Không** dán vào bất kỳ file nào trong repo, không dán vào chat, không đưa vào commit. Xoá lịch sử terminal sau khi xong.

**Nhánh A2** — không đụng vào khoá:

```powershell
Rename-Item "$env:LOCALAPPDATA\..\AutoJMS\FullStack\journey_history.db" "journey_history.encrypted.bak"
setx AUTOJMS_DB_ENCRYPTION 0
```

App sẽ dựng cache mới ở dạng plaintext. Vận hành bình thường **≥ 14 ngày** để đủ độ phủ như runbook yêu cầu, rồi đọc bằng `sqlite3` thường. File cũ được giữ nguyên, không mất dữ liệu.

- [ ] **Step 3: Viết query từ vựng phía client**

Tạo `backend/datahub/tests/od1_scan_vocabulary_client.sql` — bản sinh đôi SQLite của [`od1_scan_vocabulary.sql`](backend/datahub/tests/od1_scan_vocabulary.sql) đã commit ở `3b11163`, trả lời cùng bộ câu hỏi trên nguồn dữ liệu thật. Nội dung bắt buộc:

```sql
-- od1_scan_vocabulary_client.sql — CHỈ ĐỌC. Không có INSERT/UPDATE/DELETE/CREATE.
-- Nguồn: %UserDataDir%\FullStack\journey_history.db, bảng journey_history.
-- Bảng này có scan_type_name nhưng KHÔNG có cột scan_type_code; mã số nằm trong
-- raw_json dưới khoá "code" (WaybillJourneyJsonParser.cs:186), nên phải bóc ra
-- bằng json_extract.

.mode column
.headers on

-- 0.1 Độ phủ thời gian: OD-1 cần >= 14 ngày dữ liệu mới có nghĩa.
SELECT count(*)                                   AS rows_total,
       count(DISTINCT waybill_no)                 AS waybills,
       min(scan_time)                             AS earliest,
       max(scan_time)                             AS latest,
       CAST(julianday(max(scan_time)) - julianday(min(scan_time)) AS INT) AS span_days
  FROM journey_history;

-- 0.2 Toàn bộ từ vựng, kèm mã bóc từ raw_json.
SELECT json_extract(raw_json, '$.code')           AS scan_type_code,
       scan_type_name,
       count(*)                                   AS events,
       count(DISTINCT waybill_no)                 AS waybills,
       min(scan_time)                             AS first_seen,
       max(scan_time)                             AS last_seen
  FROM journey_history
 GROUP BY 1, 2
 ORDER BY events DESC;

-- 0.3 Ứng viên terminal: mã xuất hiện ở BƯỚC CUỐI của hành trình.
-- pct_final tính trên các waybill đã "nghỉ" (>7 ngày không có scan mới), vì
-- waybill đang chạy dở luôn có một bước cuối tạm thời và sẽ làm loãng tỷ lệ.
WITH last_event AS (
    SELECT waybill_no,
           json_extract(raw_json, '$.code') AS scan_type_code,
           scan_type_name,
           ROW_NUMBER() OVER (PARTITION BY waybill_no ORDER BY scan_time DESC, stt DESC) AS rn
      FROM journey_history
),
settled AS (
    SELECT waybill_no FROM journey_history
     GROUP BY waybill_no
    HAVING julianday('now') - julianday(max(scan_time)) > 7
)
SELECT l.scan_type_code,
       l.scan_type_name,
       count(*)                                          AS times_final,
       count(*) FILTER (WHERE s.waybill_no IS NOT NULL)  AS times_final_settled,
       ROUND(100.0 * count(*) FILTER (WHERE s.waybill_no IS NOT NULL)
             / NULLIF((SELECT count(*) FROM settled), 0), 1) AS pct_of_settled
  FROM last_event l
  LEFT JOIN settled s ON s.waybill_no = l.waybill_no
 WHERE l.rn = 1
 GROUP BY 1, 2
 ORDER BY times_final DESC;

-- 0.4 Đối chiếu với từ vựng mà client đang kỳ vọng.
SELECT 'Ký nhận CPN'         AS pattern, count(*) AS hits FROM journey_history WHERE scan_type_name LIKE '%Ký nhận CPN%'
UNION ALL SELECT 'Ký nhận chuyển hoàn', count(*) FROM journey_history WHERE scan_type_name LIKE '%Ký nhận chuyển hoàn%'
UNION ALL SELECT '快件签收',            count(*) FROM journey_history WHERE scan_type_name LIKE '%快件签收%'
UNION ALL SELECT 'ký nhận (lower)',     count(*) FROM journey_history WHERE lower(scan_type_name) LIKE '%ký nhận%';

-- 0.5 Chất lượng dữ liệu: mã rỗng/NULL sẽ thành lỗ hổng trong policy P1.
SELECT count(*) FILTER (WHERE json_extract(raw_json,'$.code') IS NULL) AS code_null,
       count(*) FILTER (WHERE trim(coalesce(scan_type_name,'')) = '')  AS name_blank,
       count(*) FILTER (WHERE raw_json IS NULL)                        AS raw_json_null
  FROM journey_history;

-- 0.6 Một mã có gắn với nhiều tên không? Nếu có, OD-1 phải khoá theo mã, không theo tên.
SELECT json_extract(raw_json,'$.code') AS scan_type_code,
       count(DISTINCT scan_type_name)  AS distinct_names,
       group_concat(DISTINCT scan_type_name) AS names
  FROM journey_history
 GROUP BY 1
HAVING count(DISTINCT scan_type_name) > 1
 ORDER BY distinct_names DESC;
```

- [ ] **Step 4: Kiểm tra file không chứa lệnh ghi**

```bash
cd "D:/v1.2605.2(new-test)" && grep -inE "^\s*(insert|update|delete|drop|create|alter|attach)" backend/datahub/tests/od1_scan_vocabulary_client.sql; echo "exit=$?"
```

Expected: không có dòng nào, `exit=1`. Bất kỳ kết quả nào ⇒ sửa trước khi chạy.

- [ ] **Step 5: Chạy trên máy trạm và lưu kết quả ra ngoài repo**

```bash
sqlite3 "<đường dẫn journey_history.db>" ".read backend/datahub/tests/od1_scan_vocabulary_client.sql" > "$USERPROFILE/od1-evidence-2026-09-07.txt" 2>&1 && head -40 "$USERPROFILE/od1-evidence-2026-09-07.txt"
```

Expected: mục 0.1 cho `span_days ≥ 14` và `rows_total` hàng nghìn. Nếu `span_days < 14` ⇒ **chưa đủ bằng chứng ký OD-1**; báo Owner và chờ thêm dữ liệu, không ký non.

- [ ] **Step 6: Commit query, **không** commit kết quả**

Kết quả chứa mã vận đơn thật của khách. Chỉ commit file query:

```bash
cd "D:/v1.2605.2(new-test)" && git add backend/datahub/tests/od1_scan_vocabulary_client.sql && git commit -m "test(datahub): read the OD-1 scan vocabulary from the client journey store, where it actually lives"
```

---

### Task 8: Tổng hợp báo cáo P0 với verdict cơ học

**Files:**
- Create: `docs/review/p0-report-2026-09-07.md`

**Interfaces:**
- Consumes: kết quả của Task 2→7.

- [ ] **Step 1: Viết báo cáo theo đúng khung dưới đây**

Mỗi gate ghi **verdict + bằng chứng + giới hạn của bằng chứng**. Gate nào chưa chạy thì ghi `NOT MEASURED`, tuyệt đối không ghi `PASS` kèm chú thích.

```markdown
# P0 REPORT — 2026-09-07 — môi trường: staging

| Gate | Verdict | Bằng chứng | Giới hạn của bằng chứng |
|---|---|---|---|
| G1 OD-1 evidence | **FAIL (có chủ đích)** | Không có. OD-A = A3 ký 08/09: client store đang bị thiết kế cho biến mất, DataHub không thể chứa scan event trước P4 | Không tồn tại nguồn nào có từ vựng scan thật ở thời điểm P0. **Không** dùng 2 dòng `98/110` của smoke-test làm bằng chứng |
| G2 Preflight | PASS | Task 3 Step 5, 9/9 check | DB greenfield: chứng minh schema khớp, không chứng minh migration chạy được trên dữ liệu có sẵn |
| G3 Backup | | Task 4 Step 2, tên + kích thước file | |
| G4 Restore | | Task 4 Step 3, `real` time | |
| G5 Smoke sau restore | | Task 4 Step 4, 24/24 | |
| G6 Infra 3.1–3.6 | | Task 2 Step 5–8 | |
| G7 Baseline A | | Task 6 Step 7–8, p95 từ log postgres | |
| G8 Baseline B | | Task 6 Step 7–8 | |
| G9 Owner decisions | | OD-A / OD-B / OD-C + OD-1/OD-2/OD-6 | |

## Điểm chưa xác nhận
- DB staging bản 26/08 có chứa dữ liệu gì bị mất trong đợt dựng lại 07/09 không (PH-2).

## Khuyến nghị ký
- **OD-2** (horizon retention) và **OD-6** (mốc tính terminal retention): ký được — cả hai là quyết định chính sách, không phụ thuộc bằng chứng còn thiếu. OD-6 khuyến nghị Option B (`server observed time`) để terminal đến muộn không bị purge tức thì.
- **OD-1**: **không ký được trong P0.** OD-A = A3. Hoãn tới sau P4, khi `waybill_scan_events` thật sự nhận được dữ liệu và `od1_scan_vocabulary.sql` chạy có nghĩa. Điều kiện ký khi đó vẫn là `span_days ≥ 14` và ứng viên terminal có `pct_of_settled` rõ rệt.
- **Chuyển cảnh báo sang P6:** không có OD-1 đã ký thì **không được bật purge projection**. P1 chạy với `jms_event_policies` rỗng là chấp nhận được; P6 xoá dữ liệu dựa trên bảng rỗng thì không.
- **P1 vẫn 🔒 KHOÁ** cho tới khi Owner ký OD-2 và OD-6 trên báo cáo này. OD-1 không còn là điều kiện mở P1 (nó chuyển thành điều kiện mở P6).
```

- [ ] **Step 2: Quét bí mật trước khi commit**

```bash
cd "D:/v1.2605.2(new-test)" && grep -nE "$VPS|BEGIN [A-Z ]*PRIVATE KEY|x'[0-9A-Fa-f]{64}'" docs/review/p0-report-2026-09-07.md; echo "exit=$?"
```

Expected: không có dòng nào, `exit=1`.

- [ ] **Step 3: Chạy harness kiểm tra bí mật của repo**

```bash
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

Expected: PASS. Lưu ý part 4 (infra denylist) sẽ in `INACTIVE` khi biến môi trường denylist không được nạp — đó là hành vi đúng ở máy local, không phải lỗi.

- [ ] **Step 4: Commit**

```bash
cd "D:/v1.2605.2(new-test)" && git add docs/review/p0-report-2026-09-07.md && git commit -m "docs(review): report the P0 gates against measured evidence, not asserted status" && git push origin main && git log --oneline -1
```

---

## Thứ tự chạy

Sau khi ký OD-A/OD-B/OD-C ngày 08/09, thứ tự là một chuỗi tuyến tính, không còn nhánh chờ:

**Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6 → Task 8.**

- Task 2 phải xong trước Task 4 và Task 6: cả hai gọi `--base https://dev.jmsauto.online`, URL đó chỉ tồn tại sau khi Task 2 dựng lại TLS.
- Task 5 (bật `log_min_duration_statement = 0`) phải nằm ngay trước Task 6, và **Step 9 của Task 6 tắt nó đi** — nếu Task 6 vì lý do gì đó không chạy tới Step 9, phải tắt bằng tay, nếu không log sẽ làm đầy đĩa.
- **Task 7: BỎ.** OD-A = A3.
