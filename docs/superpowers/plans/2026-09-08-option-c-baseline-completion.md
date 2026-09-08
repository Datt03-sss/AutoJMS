# Option C — Hoàn tất baseline G7/G8 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chạy lại đúng bốn bài đo cũ với đủ dụng cụ, thu về `transaction_p95` và `commit_p95` (hai metric §13 nêu thiếu), lấy phát biểu chặt nhất mà dụng cụ cho phép về `counter_lock_wait` p50 — **có thể chỉ là một chặn trên**, xem §0 — và đặt bộ số mới làm **bộ chuẩn** cho P1/P7. Không thiết kế phép đo mới; một file ký nhỏ phải viết mới, xem **OD-D**.

**Architecture:** Bốn lượt tải sinh ra ba nguồn bằng chứng độc lập trong cùng một cửa sổ thời gian: client-side latency từ `baseline_load.py`, lock-wait từ session `psql` chạy `lock_wait_sampler.sql` ở 5 Hz, và server-side statement duration từ log PostgreSQL bật `log_min_duration_statement = 0`. Log được kéo về một thư mục **ngoài** repo rồi đưa qua `pg_log_metrics.py`. Cấu hình log là thay đổi **duy nhất** ghi vào staging, bật bằng SIGHUP (`pg_reload_conf()`, không restart) và **bắt buộc** tắt lại.

**Tech Stack:** Python 3 standard library (`baseline_load.py`, `pg_log_metrics.py`, và `mint_bench_assertions.py` — bộ ký RS256 chép từ `smoke-test.sh` step 2), `psql` 16 (`\watch` có tham số tên), Docker Compose trên staging VPS, git.

---

## 🚨 TEARDOWN KHẨN CẤP — chạy lệnh này nếu plan hỏng ở bất kỳ đâu sau Task 4

`log_min_duration_statement = 0` ghi lại **mọi câu lệnh**. Bỏ quên nó sẽ làm đầy đĩa VPS và để lại
một bản sao toàn bộ dữ liệu chạy qua database nằm trong log Docker. `deadlock_timeout = 100ms` bỏ
quên thì bộ dò deadlock chạy dày vô thời hạn.

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c "ALTER SYSTEM RESET log_min_duration_statement; ALTER SYSTEM RESET log_lock_waits; ALTER SYSTEM RESET deadlock_timeout; ALTER SYSTEM RESET log_line_prefix; SELECT pg_reload_conf();"'
```

Expected: `pg_reload_conf` trả `t`. **Chạy được nhiều lần, vô hại nếu log vốn đã tắt.**

Tên user/db `datahub_staging` dùng nguyên văn ở mọi lệnh `psql` trong plan này — giống hệt
`2026-09-07-p0-remediation-plan.md`, đã nằm trong repo được track từ trước. Không lồng `"$POSTGRES_USER"`
vào chuỗi SSH: bốn lớp thoát dấu nháy là một nguồn lỗi thật, và hai tên này không phải bí mật.

---

## ⛔ ĐIỀU KIỆN TIÊN QUYẾT — Owner phải cấp trước khi Task 2 chạy được

`baseline_load.py --assertions-file` cần **10 license assertion đã ký**, một dòng một cái, đúng thứ
tự với `--sites`. Staging hiện đặt `DATAHUB_ALLOW_STAGING_TEST_ISSUER=false`, và `.env.staging`
**chỉ có** `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` / `_PUBLIC_KEY_PATH` — **không có nửa private**.
Đây là đúng thiết kế: nửa private cố ý nằm ngoài VPS.

Có **hai** quyết định cần Owner ở đây, độc lập nhau: **P-1/P-2** (lấy khoá ở đâu) ngay dưới, và
**OD-D** (cho phép commit một file ký mới hay không) ở đầu Task 2. **Thiếu một trong hai thì plan
dừng ở Task 1.**

Về khoá — chỉ có hai đường, Owner chọn một:

| | Đường | Việc Owner phải làm | Đánh giá |
|---|---|---|---|
| **P-1** | **Ký RS256 bằng nửa private thật** (khuyến nghị) | Đặt khoá private vào biến môi trường `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` **trên máy Owner**, hoặc vào một file ngoài repo rồi trỏ `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE` tới nó | Thuật toán ký đã có sẵn và đã dùng thật: `backend/datahub/scripts/smoke-test.sh` step 2 mint `v1rs256.<payload>.<signature>`, tự làm PKCS#1 padding bằng Python thuần. Task 2 **chép** khối đó (không gọi lại được — xem OD-D). Đo đúng đường xác thực mà production dùng |
| **P-2** | Bật tạm `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true` rồi ký HMAC bằng `issue-staging-assertion.ps1` | Sửa `.env.staging` trên VPS, restart `api`, và nhớ tắt lại | **Không khuyến nghị.** Trái với chỉ thị đang có hiệu lực ("không bật lại cờ này"), hạ chuẩn bảo mật staging trong suốt lượt đo, và đo một đường validator **khác** với đường production |

> **Không dán khoá private vào chat.** Nó sẽ nằm vĩnh viễn trong transcript. Đặt nó vào biến môi
> trường hoặc file ngoài repo, rồi chỉ báo "đã sẵn sàng".

Nếu Owner không cấp được khoá, plan này **dừng ở Task 1** và câu hỏi A/B/C phải mở lại — vì khi đó
cả C **và** nghĩa vụ bắt buộc của A (chạy lại `bulk-10`) đều không thực hiện được.

---

## Global Constraints

Sao từ `CLAUDE.md`, `AGENTS.md`, và báo cáo P0. Mọi task ngầm định bao gồm mục này.

- Repo `Datt03-sss/AutoJMS` là **PUBLIC**. Không một file được track nào chứa VPS IP, tên tài khoản, đường dẫn khoá SSH, container ID, hay ngưỡng firewall. Hostname `dev.jmsauto.online` được phép.
- **Không commit** `.env`, service account key, `*.pfx`, `*.pem`, hay bất kỳ file token/khoá nào. Mask token dạng `first4...last4`.
- **Không bao giờ `git add .`** — stage từng đường dẫn một.
- **Không force push. Không viết lại lịch sử.**
- **Protected Files** — cần Owner yêu cầu riêng, hiện **chưa có**: `backend/datahub/docker-compose.yml`, DataHub production config, database schema migrations, và danh sách trong `CLAUDE.md`. Plan này **không sửa file nào trong đó**.
- **Minimal Edit Rule.** `baseline_load.py`, `lock_wait_sampler.sql`, `pg_log_metrics.py` là dụng cụ đo — **không sửa** trong lúc đo. Sửa dụng cụ giữa chừng thì bộ số mất giá trị.
- **Thay đổi duy nhất ghi vào staging** là ba tham số log, bật/tắt bằng `ALTER SYSTEM` + `pg_reload_conf()`. **Không** restart, **không** migration, **không** đụng container, **không** sửa `.env.staging`.
- **Không bật `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true`** trừ khi Owner ký P-2 ở trên.
- File log bắt được phải nằm **ngoài** cây làm việc của repo. `check-secrets.ps1` **không** quét nó: part 2 bỏ qua vì `.log` không có trong `$sourceExtensions`, part 5 bỏ qua vì liệt kê bằng `git ls-files --others --exclude-standard`. `*.log` bị gitignore chỉ giữ nó khỏi `git add .` — không có pass nào đọc nội dung.
- Cổng build trước mọi push:
  ```
  dotnet build ./AutoJMS.slnx -c Release      → 0 Warning(s), 0 Error(s)
  powershell -ExecutionPolicy Bypass -File ./eng/harness/verify.ps1  → ALL GATES PASSED
  ```
- Mẫu truy cập VPS — biến môi trường **không** sống qua các lần gọi Bash, nên mọi lệnh VPS phải mang tiền tố này trong **cùng một lần gọi**, và không bao giờ in giá trị ra:
  ```bash
  cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && <command>
  ```

---

## §0. Quyết định A/B/C — chọn **C**

Owner đã uỷ quyền quyết định này ngày 08/09/2026. Ghi lại đây cùng lý do, để P1/P7 sau này biết
quyết định dựa trên cái gì.

### Vì sao không phải A

§13 ghi giá của A là **0**. Đọc hết phần chữ nhỏ thì không phải:

1. **A kèm một nghĩa vụ bắt buộc.** §13 nói rõ: chọn A thì phải **giải thích `bulk-10 v2 p99 = 2198,7 ms`** (ngoại lai 5,6×) **hoặc chạy lại riêng bài `bulk-10`**. Hôm nay không ai có lời giải. Mà chạy lại `bulk-10` cần **đúng bộ đồ nghề mà C cần** — 10 assertion đã ký, bench sites, cấu hình log. Nghĩa là A không hề tránh được chi phí dựng đồ nghề của C; nó chỉ chạy 1 lượt thay vì 4. Bốn lượt là ~2 phút mỗi lượt cộng `sleep 90` giữa các lượt. **Phần chênh lệch thật giữa A và C là khoảng 8 phút chạy máy.**

2. **Baseline của A không chỉ thiếu — một phần đã được chính repo tuyên bố là sai.** Docstring của `baseline_load.py` (dòng 32–53) ghi: bốn số đã công bố do **phiên bản cũ** của chính file này sinh ra, và hai khác biệt **làm dịch số**: (a) `duration_seconds` cao hơn tới trọn một interval, nên `sustained_rps` đã công bố **báo thiếu ~4 %** ở concurrency 50; (b) lease renewal đi thành cụm chứ không phải nền trải đều, nên mẫu latency bị nhiễu khác đi. Docstring kết: *"A re-run of this file will not reproduce the published figures, and that is intended — the instrument was corrected without re-measuring."* Chọn A là lấy làm mốc chuẩn cho P1/P7 một bộ số mà **dụng cụ hiện tại cố ý không tái tạo được**. Mọi so sánh hiệu năng về sau sẽ so với một mốc không ai dựng lại được.

3. **Điều đó cũng đúng với nửa lock-wait.** Header `lock_wait_sampler.sql` ghi: trong bốn lượt đã công bố, sampler này chỉ sinh ra số cho `bulk-10 v2` và `bulk-50 v2`; ba lượt còn lại đến từ các vòng lặp dính defect 2 và 3, và **"None of those five is reproducible by this corrected query."**

Cộng lại: A tiết kiệm ~8 phút chạy máy, đổi lấy việc thiếu vĩnh viễn ba metric **và** đóng dấu chuẩn lên một bộ số mà hai trong ba dụng cụ hiện tại tuyên bố không tái tạo được. **A bị C trội hoàn toàn** (dominated), không phải là đánh đổi tốc-độ-đổi-chất-lượng.

### Vì sao không phải B

B là "một chương trình đo mới". Không có bằng chứng nào cho thấy **thiết kế** phép đo sai — §7.2 chỉ nói lượt chạy lại không khớp bit-với-bit, đó là biến thiên bình thường, không phải lỗi thiết kế. Cả ba dụng cụ đã nằm trong repo và đã qua ba vòng sửa. B trả tiền để làm lại thứ đã có. B chỉ đúng nếu ta muốn đo **những đại lượng khác** — không phải tình huống này.

### Vì sao C

C dùng lại nguyên trạng thiết kế đo và chỉ bổ sung dụng cụ đã có sẵn trong repo. Nó thu **chắc chắn hai** trong ba metric thiếu — `transaction_p95` và `commit_p95`, từ `log_min_duration_statement = 0`, thứ không có sàn. Cộng thêm ba lợi ích không nằm trong §13:

- Bộ số mới đo trên **binary hiện tại** (`d4cad07`, image vừa dựng lại 08/09), còn §5 đo trên một image cũ hơn. Sửa ở `d4cad07` chỉ đụng health check — không nằm trên đường xử lý request — nên nó không làm dịch hiệu năng; nhưng bộ số mới vẫn khớp với thứ đang thực sự chạy, còn bộ cũ thì không.
- Nó giải quyết luôn nghĩa vụ ngoại lai `bulk-10 p99` mà A phải gánh: lượt `bulk-10` mới hoặc tái hiện ngoại lai (thì nó là thật, và đo được), hoặc không (thì nó là nhiễu một lần, và đã trả lời xong).
- Đây là lần chạy thật đầu tiên của `pg_log_metrics.py`. Chạy nó khi còn đang mở P0 thì rẻ hơn nhiều so với lần đầu chạy nó giữa một sự cố P1.

### Cái C **không** làm được — nói trước, đừng để phát hiện lúc chạy

Metric thứ ba, **`counter_lock_wait` p50, nhiều khả năng vẫn không lấy được** — và giới hạn nằm ở **vật lý của dụng cụ**, không phải ở công sức, nên thêm giờ chạy cũng không đổi:

- **Sampler không cho percentile.** Header `lock_wait_sampler.sql` (dòng 143–148) nói rõ: ở 5 Hz nó là **mẫu thiên lệch theo độ dài** — một lượt chờ được quan sát với xác suất tỉ lệ thuận với thời gian chờ, nên chờ ngắn bị thiếu hệ thống và chờ dưới 200 ms có thể mất hẳn. Nguyên văn: *một p50/p95 của thời gian chờ vẫn cần server log hoặc một dụng cụ tổng điều tra.* Cột `max_wait_ms` chỉ là **cận dưới** của max.
- **Log có sàn.** `log_lock_waits` chỉ ghi khi đã chờ quá `deadlock_timeout`. Plan này hạ sàn xuống **100 ms** trong đúng cửa sổ đo (Task 4 Step 1) để lấy được nhiều nhất có thể, nhưng nếu mọi lượt chờ đều dưới 100 ms thì log ghi 0 dòng.

Kết cục nhiều khả năng nhất là: *"không lượt chờ khoá nào vượt 100 ms trong cả bốn lượt."* Đó là một **chặn trên toàn phân phối** — mạnh hơn một p50 về mặt bảo đảm — nhưng **không** phải con số §13 hỏi, và Task 7 Step 3 buộc phải viết đúng như vậy.

**Điều này không đổi quyết định.** B dùng đúng bộ dụng cụ ấy nên gặp đúng bức tường, trừ khi B tự thiết kế một dụng cụ tổng điều tra — chi phí lớn hơn hẳn, cho một metric mà chặn trên ở trên đã trả lời phần lớn giá trị thực tế. A thì không có cả chặn trên.

### Chi phí C mà §13 chưa tính — xem OD-D ở Task 2

§13 định giá C là "không viết harness mới". Điều đó **không còn đúng**: khi `DATAHUB_ALLOW_STAGING_TEST_ISSUER` chuyển sang `false`, đường cấp assertion đổi hẳn. `issue-staging-assertion.ps1` in ra được nhưng ký **HMAC**, bị `RsaLicenseAssertionValidator` từ chối. `smoke-test.sh` ký RS256 đúng nhưng **che** output và **tự sinh** site code, nên không dùng để tạo file 10 dòng được. C cần đúng **một** file nhỏ mới. Chi phí này là **chung** cho C, cho B, và cho nghĩa vụ bắt buộc của A — nên nó không làm C đắt hơn A.

### Cái phải chấp nhận khi chọn C — quyết định kèm theo

§13 buộc phải chọn một bộ làm chuẩn. **Quyết định: bộ bốn lượt mới là bộ chuẩn.** §5 giữ nguyên trong báo cáo như hồ sơ lịch sử của P0, được dán nhãn rõ là **không phải mốc so sánh**. Lý do: bộ mới sinh ra từ dụng cụ đã sửa, đo trên binary đang chạy, và mỗi metric của nó đi kèm một phát biểu trung thực về việc nó đo được hay không (kể cả trường hợp `counter_lock_wait` chỉ có chặn trên). Không trộn hai bộ — lấy latency của bộ cũ ghép với metric database của bộ mới sẽ tạo ra một "baseline" mà các phần của nó chưa từng cùng tồn tại, đúng loại lỗi báo cáo này đã phải sửa nhiều lần.

### Không ký gì thêm

Quyết định này **chỉ** giải quyết mục G7/G8 của §13. **OD-2** và **OD-6** vẫn cần chữ ký của Owner trên báo cáo (§13 khuyến nghị OD-6 = Option B, `server observed time`). **OD-1** vẫn hoãn tới sau P4. **OD-8 (RPO/RTO)** vẫn chưa được thoả. **P1 vẫn 🔒 KHOÁ** cho tới khi Owner ký OD-2 + OD-6.

---

## Cấu trúc file

| File | Vai trò | Thao tác |
|---|---|---|
| `backend/datahub/tests/baseline_load.py` | Sinh tải, đo client latency | **Chỉ chạy, không sửa** |
| `backend/datahub/tests/lock_wait_sampler.sql` | Sampler `pg_locks` 5 Hz | **Chỉ chạy, không sửa** |
| `backend/datahub/tests/pg_log_metrics.py` | Đọc log → `commit_*`/`transaction_*` | **Chỉ chạy, không sửa** |
| `backend/datahub/scripts/smoke-test.sh` | Nguồn của bộ ký RS256 được **chép** sang Task 2 | **Chỉ đọc** |
| `backend/datahub/tests/mint_bench_assertions.py` | Ký hàng loạt 10 assertion, in ra stdout | **Tạo mới ở Task 2** — cần **OD-D** |
| `docs/review/p0-report-2026-09-07.md` | Nơi ghi bộ chuẩn mới | Sửa §5, §12, §13, §14 ở Task 7 |
| `.agent-lock.md` | Khoá single-writer | Cập nhật ở Task 1, trả ở Task 8 |
| `~/autojms-bench/` **trên máy Owner** | Log bắt được, output client/sampler | **Ngoài repo.** Không commit. Log xoá ở Task 8 |
| `~/bench-assertions.txt` **trên VPS** | 10 assertion | **Ngoài repo trên VPS.** Xoá ở Task 8 |

---

## Task 1: Pre-flight — khoá, cây sạch, và ghi lại trạng thái log hiện tại

**Files:** Modify `.agent-lock.md`

**Interfaces:** Consumes: nothing. Produces: giá trị log gốc mà Task 6 Step 4 phải khôi phục về.

- [ ] **Step 1: Cây sạch, đồng bộ với origin**

```bash
cd "D:/v1.2605.2(new-test)" && git switch main && git pull --ff-only origin main && git status --porcelain --untracked-files=no && git rev-list --left-right --count origin/main...HEAD
```
Expected: `git status` không in gì; đếm ra `0	0`. Nếu cây bẩn — dừng, báo cáo. Không đo trên cây bẩn.

- [ ] **Step 2: Nhận khoá single-writer**

Thay toàn bộ nội dung `.agent-lock.md` bằng:

```markdown
# Agent Lock
Current Writer: Claude Code
Mode: WRITE
Scope: docs/review/p0-report-2026-09-07.md, docs/superpowers/plans/2026-09-08-option-c-baseline-completion.md, .agent-lock.md
```

Scope cố ý **không** liệt kê ba file dụng cụ: plan này chỉ chạy chúng, không sửa. Khoá cũ trỏ tới một đợt việc đã xong và phải được thay, không phải nối thêm.

- [ ] **Step 3: Staging đang chạy và rảnh**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging ps --format "{{.Service}} {{.State}}" && cd ~/AutoJMS && git log -n 1 --oneline'
```
Expected: `api running`, `caddy running`, `postgres running`, và commit `d4cad07` hoặc mới hơn. `caddy` không bao giờ báo healthy — đó là R8, không phải lỗi.

- [ ] **Step 4: GHI LẠI trạng thái log gốc — Task 6 Step 4 phải khôi phục đúng về đây**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" "cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -At -F'|' -c \"SELECT name, setting, source FROM pg_settings WHERE name LIKE 'log_min_duration_statement' OR name LIKE 'log_lock_waits' OR name LIKE 'log_line_prefix' OR name LIKE 'deadlock_timeout' ORDER BY name\""
```
Expected: 4 dòng. Điển hình là `log_min_duration_statement|-1|default`, `log_lock_waits|off|default`, `deadlock_timeout|1s|default`. **Chép nguyên văn 4 dòng này vào report file của task** — đó là trạng thái Task 6 Step 4 phải trả về.

`LIKE` dùng thay cho `IN (...)` chỉ vì lý do thoát dấu nháy: `IN` cần nháy đơn quanh từng hằng chuỗi,
mà chuỗi đã nằm trong hai lớp nháy của SSH. Kết quả truy vấn không đổi.

- [ ] **Step 5: Commit khoá**

```bash
cd "D:/v1.2605.2(new-test)" && git add .agent-lock.md && git commit -m "chore(lock): take the single-writer lock for the option C baseline run"
```

---

## Task 2: Dụng cụ ký hàng loạt — `mint_bench_assertions.py` (cần **OD-D**)

**Files:**
- Create: `backend/datahub/tests/mint_bench_assertions.py`

**Interfaces:** Consumes: khoá private (P-1). Produces: `python3 mint_bench_assertions.py --sites BENCH01,...,BENCH10 --channel staging --issuer <iss> --audience <aud>` in ra **một assertion `v1rs256.` mỗi dòng**, đúng thứ tự `--sites`. Task 3 dùng output này.

> ### ⛔ OD-D — quyết định Owner phải ký trước khi task này chạy
>
> §13 định giá C là "không viết harness mới". **Điều đó không còn đúng**, và plan này phải nói ra
> thay vì lặng lẽ vi phạm luật:
>
> - `baseline_load.py --assertions-file` cần 10 assertion **in ra được**, một dòng một cái.
> - `issue-staging-assertion.ps1` in ra được, nhưng nó ký **HMAC** (`v1.`) — chỉ hợp lệ khi
>   `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true`. Staging nay đặt `false`, và
>   `RsaLicenseAssertionValidator.cs:53` từ chối mọi prefix khác `v1rs256.`. Đường này **đã chết**.
> - `smoke-test.sh` **có** bộ ký RS256 đúng, nhưng không dùng lại được cho việc này vì hai lý do độc
>   lập: nó **che** assertion khi in (`datahub::mask`, chỉ ra `first4...last4`), và nó **tự sinh**
>   site code ở step 1 thay vì nhận từ ngoài. Bộ ký nằm inline trong step 2, không phải một entry
>   point tách rời.
>
> Nên C cần **đúng một** file mới, nhỏ, được review như code thường — cùng dạng ngoại lệ mà Owner đã
> ký cho `baseline_load.py` (OD-C = C1 ngày 08/09). Đây là chi phí **§13 chưa tính**, vì §13 lập ra
> khi giả định khoảng trống chỉ nằm ở cột sampler và hai vị từ grep; nó không kiểm lại rằng đường cấp
> assertion đã đổi khi cờ staging issuer bị tắt.
>
> **Lưu ý cho quyết định:** OD-D **không** làm C đắt hơn A. Nghĩa vụ bắt buộc của A — chạy lại
> `bulk-10` — cần **đúng 10 assertion ấy**. B cũng vậy. OD-D là chi phí chung của mọi đường dẫn tới
> một baseline đầy đủ, trừ khi Owner giải thích được ngoại lai `bulk-10 p99` bằng phân tích thuần tuý.
>
> | | Lựa chọn OD-D | Hệ quả |
> |---|---|---|
> | **D1** *(khuyến nghị)* | Cho phép commit `mint_bench_assertions.py`, review như code thường | C chạy được. Một file ~120 dòng, stdlib, có self-test |
> | **D2** | Không cho | C, B, và nghĩa vụ của A đều **không** thực hiện được. Phải quay lại §13 và chọn A **kèm một lời giải phân tích** cho ngoại lai `bulk-10 p99` |
>
> **Nếu Owner chưa ký D1: dừng ở đây, không tạo file, báo cáo.**

- [ ] **Step 1: Xác nhận khoá đã sẵn sàng — không in giá trị**

```bash
cd "D:/v1.2605.2(new-test)" && if [ -n "${DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY-}" ]; then echo "key=env len=${#DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY}"; elif [ -n "${DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE-}" ] && [ -f "$DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE" ]; then echo "key=file bytes=$(wc -c < "$DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY_FILE")"; else echo "key=MISSING"; fi
```
Expected: `key=env len=<số>` hoặc `key=file bytes=<số>`. Nếu ra `key=MISSING` — **dừng**, báo Owner, không đi tiếp. Chỉ in độ dài, không bao giờ in nội dung.

- [ ] **Step 2: Viết test trước — nó phải fail**

Tạo `backend/datahub/tests/mint_bench_assertions.py` với **chỉ** phần self-test trước, chạy
`python3 backend/datahub/tests/mint_bench_assertions.py --self-test`, và xác nhận nó **FAIL** với
`NameError`/`AttributeError` vì hàm chưa tồn tại. Ghi lại output fail vào report file. Đây là pha đỏ;
không suy luận thay cho nó.

Ba test bắt buộc, mỗi cái kiểm một tính chất khác nhau:

1. `test_payload_is_byte_identical_to_smoke_test` — dựng payload cho một bộ tham số cố định và so
   với chuỗi JSON nguyên văn mà `smoke-test.sh` sinh ra cho cùng bộ ấy:
   `{"Channel":"staging","SiteCodes":["BENCH01"],"ExpiresAt":1788000000,"DataHubUrl":null,"Seats":1,"TokenVersion":1,"Issuer":"iss","Audience":"aud"}`.
   Key phải **PascalCase** và thứ tự phải đúng như trên: API deserialize bằng option **phân biệt hoa
   thường** mặc định, nên `camelCase` sẽ không bind. `separators=(',', ':')` — không khoảng trắng.
2. `test_signature_verifies_against_the_public_half` — ký một payload bằng một khoá RSA 2048 **sinh
   tại chỗ trong test**, rồi verify bằng nửa public, so khớp bit. Không dùng khoá thật trong test.
3. `test_rejects_key_under_2048_bits` — khoá 1024 bit phải thoát với thông điệp nêu
   `RsaLicenseAssertionValidator.MinimumKeySizeBits`, giống hệt `smoke-test.sh`.

- [ ] **Step 3: Viết bản cài đặt tối thiểu**

Sao **nguyên văn** ba hàm `b64u`, `rsa_numbers`, `sign_pkcs1_sha256` và khối `payload = {...}` từ
`backend/datahub/scripts/smoke-test.sh` step 2 (dòng ~238–355). Không viết lại, không "cải tiến" —
một bộ ký viết lại là một bộ ký chưa được review.

Đầu file phải có ghi chú trôi dạt, cùng dạng đã dùng cho `pct` trong `pg_log_metrics.py`:

```python
# DUPLICATED FROM backend/datahub/scripts/smoke-test.sh step 2, deliberately.
# The signer there is inline inside a ten-step bash flow, is not a callable entry
# point, and masks its output -- so it cannot be reused to emit a 10-line
# assertions file. This copy must stay byte-compatible with it: the API compares
# the payload with case-SENSITIVE deserialization, so any drift in key names,
# key order or separators produces a token that fails CHANNEL_MISMATCH or
# LICENSE_ASSERTION_MALFORMED rather than an obvious error.
# CHANGE BOTH TOGETHER. test_payload_is_byte_identical_to_smoke_test pins the shape.
```

CLI: `--sites` (bắt buộc, phân tách bởi dấu phẩy), `--channel`, `--issuer`, `--audience` (bắt buộc),
`--expires-in-hours` (mặc định 8), `--self-test`. Khoá **chỉ** đọc từ
`DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` hoặc `..._FILE` — **không bao giờ** từ tham số dòng lệnh
(`/proc` đọc được command line của mọi tiến trình).

- [ ] **Step 4: Test phải pass**

```bash
cd "D:/v1.2605.2(new-test)" && python3 backend/datahub/tests/mint_bench_assertions.py --self-test
```
Expected: `Ran 3 tests` … `OK`.

- [ ] **Step 5: Cổng bí mật + commit**

```bash
cd "D:/v1.2605.2(new-test)" && powershell -ExecutionPolicy Bypass -File ./eng/harness/verify.ps1 2>&1 | tail -12
```
Expected: `OVERALL: ✅ ALL GATES PASSED`.

```bash
cd "D:/v1.2605.2(new-test)" && git add backend/datahub/tests/mint_bench_assertions.py && git commit -m "test(datahub): add an RS256 batch assertion minter for the option C baseline runs"
```

---

## Task 3: Cấp 10 bench site + assertion lên VPS

**Files:** không sửa file repo. Sinh `~/bench-assertions.txt` **trên VPS**.

**Interfaces:** Consumes: `mint_bench_assertions.py`. Produces: `~/bench-assertions.txt` (10 dòng) khớp thứ tự `BENCH01..BENCH10`.

- [ ] **Step 1: Đọc issuer/audience/channel từ staging — chỉ tên, không giá trị bí mật**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'grep -E "^(DATAHUB_CHANNEL|DATAHUB_LICENSE_ASSERTION_ISSUER|DATAHUB_LICENSE_ASSERTION_AUDIENCE)=" ~/AutoJMS/backend/datahub/.env.staging'
```
Expected: 3 dòng. Đây **không** phải bí mật (issuer/audience là định danh công khai, channel là `staging`) — nhưng vẫn **không** chép chúng vào bất kỳ file được track nào; chỉ dùng trong lệnh ở Step 2.

- [ ] **Step 2: Mint 10 assertion trên máy Owner rồi đẩy sang VPS qua stdin**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && python3 backend/datahub/tests/mint_bench_assertions.py --sites "$(seq -f 'BENCH%02g' 1 10 | paste -sd,)" --channel staging --issuer "<iss từ Step 1>" --audience "<aud từ Step 1>" | ssh -i "$SSHK" "$SSHU@$VPS" 'cat > ~/bench-assertions.txt && chmod 600 ~/bench-assertions.txt && wc -l < ~/bench-assertions.txt'
```
Expected: `10`.

Ký **trên máy Owner**, đẩy qua **stdin của ssh**. Khoá private không bao giờ chạm VPS — đó là điểm
của quyết định tách nửa private ra ngoài VPS, và plan này không được phá nó. `chmod 600` vì file
chứa 10 token còn hiệu lực 8 giờ.

- [ ] **Step 3: Chứng minh assertion dùng được — enroll đúng một device**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'A=$(head -1 ~/bench-assertions.txt); curl -s -o /dev/null -w "%{http_code}\n" -X POST https://dev.jmsauto.online/api/v1/devices/enroll -H "Authorization: Bearer $A" -H "Content-Type: application/json" -d "{\"siteCode\":\"BENCH01\",\"deviceName\":\"bench-probe\",\"role\":\"operator\"}"'
```
Expected: `200` hoặc `201`. Nếu `401` — assertion sai hình dạng; quay lại Task 2 Step 3 và so payload
với `smoke-test.sh`. **Không đi tiếp khi chưa có 2xx**: bật log rồi mới phát hiện assertion hỏng nghĩa
là bật `log_min_duration_statement = 0` để không thu được gì.

Bài enroll này tiêu 1 trong 10 permit/phút của bucket enrollment. Chờ 60 s trước Task 5.

---

## Task 4: Bật log statement + lock trên staging (SIGHUP)

**Files:** không sửa file nào. Đây là thay đổi **duy nhất** ghi vào staging.

**Interfaces:** Consumes: giá trị gốc ghi ở Task 1 Step 4. Produces: cửa sổ log cho Task 6.

> ⚠️ Từ điểm này trở đi, **teardown ở Task 6 Step 4 là bắt buộc dù plan kết thúc thế nào.** Nếu bất
> cứ bước nào hỏng, chạy khối TEARDOWN KHẨN CẤP ở đầu file này **trước khi** báo cáo.

- [ ] **Step 1: Bật**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" "cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c \"ALTER SYSTEM SET log_min_duration_statement = 0; ALTER SYSTEM SET log_lock_waits = on; ALTER SYSTEM SET deadlock_timeout = '100ms'; ALTER SYSTEM SET log_line_prefix = '%m [%p] %a '; SELECT pg_reload_conf();\""
```
Expected: `pg_reload_conf` trả `t`.

`deadlock_timeout = '100ms'` là **điều kiện cần** để Task 6 Step 3 thu được gì đó: `log_lock_waits`
chỉ ghi khi một phiên đã chờ lâu hơn `deadlock_timeout`, mặc định **1 giây**. Ở tải này gần như chắc
chắn không lượt chờ nào chạm 1 s, nên để mặc định thì log ghi **0 dòng** và metric thứ ba mất trắng —
lần thứ hai, vì đúng lý do khác lần đầu. Hạ sàn làm bộ dò deadlock chạy dày hơn; chấp nhận được trên
staging trong ~18 phút, và **phải reset ở Task 6 Step 4**. Nó phải được đặt **trước** khi tải chạy;
đặt sau là vô nghĩa vì log đã ghi xong.

`log_line_prefix = '%m [%p] %a '` là **chính xác** định dạng mà `pg_log_metrics.py` phân tích. `%a`
(application_name) **có thể rỗng**, khi đó prefix rút thành hai khoảng trắng liền nhau — parser đã
xử lý trường hợp này và có test cho nó. Không đổi prefix.

- [ ] **Step 2: Chứng minh SIGHUP chứ không phải restart**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" "cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -At -F'|' -c \"SELECT name, setting, pending_restart FROM pg_settings WHERE name LIKE 'log_min_duration_statement' OR name LIKE 'log_lock_waits' OR name LIKE 'deadlock_timeout'\" -c 'SELECT pg_postmaster_start_time()'"
```
Expected: `log_min_duration_statement|0|f`, `log_lock_waits|on|f`, `deadlock_timeout|100|f`, và `pg_postmaster_start_time()`
**vẫn là mốc cũ** — chứng minh không restart, connection pool không mất.

---

## Task 5: Chạy bốn lượt tải, có sampler chạy chồng lấn

**Files:** không sửa file nào.

**Interfaces:** Consumes: `~/bench-assertions.txt`. Produces: 4 khối JSON client + 4 file output sampler, tất cả nằm trên VPS ngoài repo.

> **DEFECT 1 của `lock_wait_sampler.sql` là cái bẫy chính của task này.** Harness tốn **~72 giây
> enroll device trước khi phát request đầu tiên**. Ở lần đo P0 đầu, sampler đã thoát trước khi tải bắt
> đầu; chồng lấn bằng **0 giây**, và các số 0 của nó bị đọc thành "không có tranh chấp". Cách chống:
> khởi động sampler **trong cùng một lệnh** với tải, và **tính** chồng lấn sau đó chứ đừng giả định.

- [ ] **Step 1: Chạy cả bốn lượt**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && mkdir -p ~/bench-out && SITES=$(seq -f "BENCH%02g" 1 10 | paste -sd,) && for m in interactive bulk; do for c in 10 50; do n="$m-$c"; echo "== $n =="; ( sleep 60; docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -At -F"|" -f - < tests/lock_wait_sampler.sql > ~/bench-out/lock-$n.txt 2>&1 ) & SP=$!; python3 tests/baseline_load.py --base https://dev.jmsauto.online --sites "$SITES" --assertions-file ~/bench-assertions.txt --concurrency $c --duration-seconds 120 --target-rps 8 --mode $m | tee ~/bench-out/client-$n.json; wait $SP; sleep 90; done; done'
```

Expected: bốn khối JSON, mỗi khối `ok` bằng `sent` và `http_429 = 0`, `http_409 = 0`,
`other_errors = 0`. Tổng thời gian ~18 phút.

`sleep 60` cho sampler: harness tốn ~72 s enroll, nên sampler (700 mẫu × 0,2 s = 140 s) bắt đầu ở
giây 60 và chạy tới giây 200, phủ trọn cửa sổ tải giây ~72–192. `sleep 90` giữa các lượt để cửa sổ
rate-limit 1 phút xả hết.

- [ ] **Step 2: TÍNH chồng lấn — đừng giả định (DEFECT 1)**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'for f in ~/bench-out/lock-*.txt; do echo "== $f  lines=$(wc -l < $f)"; head -1 "$f"; tail -1 "$f"; done'
```
Expected: mỗi file ~700 dòng; mốc `clock_timestamp()` đầu và cuối phải **bao trùm** cửa sổ tải của
lượt tương ứng. Nếu một file có ít hơn ~600 dòng hoặc mốc cuối trước khi tải kết thúc — **lượt đó
không có bằng chứng lock hợp lệ**; ghi lại là thiếu, đừng báo cáo các số 0 của nó là "không tranh chấp".

- [ ] **Step 3: Kéo output client + sampler về, ngoài repo**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && mkdir -p "$HOME/autojms-bench" && scp -i "$SSHK" "$SSHU@$VPS:~/bench-out/*" "$HOME/autojms-bench/" && ls -la "$HOME/autojms-bench/"
```
Expected: 8 file. `$HOME/autojms-bench/` nằm **ngoài** cây làm việc repo.

---

## Task 6: Trích log, tính metric, và **TEARDOWN BẮT BUỘC**

**Files:** không sửa file repo.

**Interfaces:** Consumes: cửa sổ log từ Task 4. Produces: `transaction_p95` + `commit_p95`, và phát biểu chặt nhất có thể về `counter_lock_wait` (xem Step 3b).

- [ ] **Step 1: Kéo log postgres về, ngoài repo**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs postgres --no-log-prefix --since 30m 2>/dev/null' > "$HOME/autojms-bench/pg-bench.log" && wc -l "$HOME/autojms-bench/pg-bench.log" && grep -c "duration:" "$HOME/autojms-bench/pg-bench.log"
```
Expected: hàng chục nghìn dòng, và số `duration:` **> 0**. Nếu là `0` — log không bắt được gì; chạy
teardown ở Step 4 rồi báo cáo, đừng chạy lại tải.

**File này chứa mọi câu lệnh API đã chạy.** Nó phải ở ngoài repo và phải bị xoá ở Task 8 Step 3.
**Không** trông vào cổng bí mật để bắt nó: không pass nào của `check-secrets.ps1` đọc `.log`.

- [ ] **Step 2: Tính `commit_*` và `transaction_*`**

```bash
cd "D:/v1.2605.2(new-test)" && python3 backend/datahub/tests/pg_log_metrics.py --log "$HOME/autojms-bench/pg-bench.log"
```
Expected: object JSON với 13 khoá. **Đây là lần đầu công cụ này chạy trên log thật.** Đọc trung thực:
- `commit_samples` ≤ 100 ⇒ các percentile là `null`, và đó là **thiếu dữ liệu**, không phải thành công một phần.
- `unpaired_commits + unclosed_transactions` chiếm tỉ lệ lớn trong `transaction_samples` ⇒ cửa sổ bắt bị cắt; nói rõ, đừng trình bày percentile như thể phủ trọn lượt chạy.
- Ra 0 mẫu ⇒ **đọc cảnh báo stderr trước khi kết luận** — nhiều khả năng là `log_line_prefix` chứ không phải "không có commit nào".

- [ ] **Step 3: `counter_lock_wait` — lấy từ **log**, không phải từ sampler**

> **Sửa một sai lầm mà chính plan này suýt mắc.** Bản nháp đầu tiên tính p50 từ cột `max_wait_ms`
> của sampler. **Làm thế là sai**, và header của `lock_wait_sampler.sql` (dòng 143–148) đã nói trước:
> ở 5 Hz nó là **mẫu có thiên lệch theo độ dài** — một lượt chờ được quan sát với xác suất tỉ lệ
> thuận với thời gian chờ, nên chờ ngắn bị thiếu hệ thống và chờ dưới 200 ms có thể mất hẳn.
> Nguyên văn kết luận của nó: *một p50/p95 của thời gian chờ **vẫn cần** server log hoặc một dụng cụ
> tổng điều tra.* Sampler cho ta `waiting` (có/không tranh chấp) và một **cận dưới** của max. Nó
> **không** cho percentile. Đừng công bố một con số p50 từ nó.

Nên `counter_lock_wait` p50 phải ra từ `log_lock_waits`. Nhưng dòng đó chỉ ghi khi một phiên đã chờ
lâu hơn `deadlock_timeout` — **mặc định 1 giây**. Ở tải này gần như chắc chắn không lượt chờ nào chạm
1 s, nên log mặc định sẽ ghi **0 dòng** và p50 vẫn không có.

Sàn ấy đã được hạ xuống 100 ms ở **Task 4 Step 1** — tức là **trước** khi tải chạy. Đặt nó ở đây thì
vô nghĩa: log của bốn lượt đã ghi xong rồi. Ở bước này chỉ còn đếm:

```bash
cd "D:/v1.2605.2(new-test)" && grep -c "still waiting for" "$HOME/autojms-bench/pg-bench.log"; grep -oE "after [0-9.]+ ms" "$HOME/autojms-bench/pg-bench.log" | grep -oE "[0-9.]+" | sort -n | awk '{a[NR]=$1} END {if (NR==0) {print "n=0 no lock wait exceeded the floor"} else {i=int((NR+1)/2); j=int(NR*0.95); if (j<1) j=1; printf "n=%d p50=%.1f p95=%.1f max=%.1f\n", NR, a[i], a[j], a[NR]}}'
```

- [ ] **Step 3b: Đọc kết quả cho trung thực — kể cả khi nó là "không đo được"**

Ba kết cục có thể, và **cả ba đều là kết quả hợp lệ** để ghi vào báo cáo:

1. `n` đủ lớn (≥ 100) ⇒ có p50 thật cho `counter_lock_wait`. Metric thứ ba **được phục hồi**.
2. `n` nhỏ (1–99) ⇒ ghi `n`, max, và **không** công bố p50. Nói: *"p50 nằm dưới sàn 100 ms của log."*
3. `n = 0` ⇒ ghi thẳng: **không lượt chờ khoá nào vượt 100 ms trong cả bốn lượt.** Đây là một phát
   biểu mạnh hơn nhiều so với một p50 — nó chặn trên toàn bộ phân phối — nhưng nó **không phải** là
   con số §13 hỏi, và báo cáo phải nói đúng như thế.

> **Điều chỉnh lời hứa của C — nêu ra vì §13 chưa lường:** C phục hồi chắc chắn **hai** metric
> (`transaction_p95`, `commit_p95` — chúng đến từ `log_min_duration_statement = 0`, **không** có sàn).
> Metric thứ ba, `counter_lock_wait` p50, bị chặn bởi **vật lý của dụng cụ**, không phải bởi công sức:
> sampler không cho percentile, log có sàn. Kết cục 3 là khả năng cao nhất. **Điều này không đổi
> quyết định** — B dùng đúng bộ dụng cụ ấy nên gặp đúng bức tường, trừ khi B tự thiết kế một dụng cụ
> tổng điều tra (chi phí lớn hơn hẳn, cho một metric mà kết cục 3 đã chặn trên). Nhưng nó **có** đổi
> câu chữ được phép dùng ở Task 7: không viết "C phục hồi cả ba metric".

- [ ] **Step 4: TEARDOWN — bắt buộc, vô điều kiện**

> Chạy bước này **dù Task 5 hay Task 6 có hỏng hay không**. `log_min_duration_statement = 0` ghi
> **mọi** câu lệnh; để quên qua đêm là đầy đĩa và staging chết. `deadlock_timeout = 100ms` để quên
> thì bộ dò deadlock chạy dày mãi mãi.

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" "cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -c \"ALTER SYSTEM RESET log_min_duration_statement; ALTER SYSTEM RESET log_lock_waits; ALTER SYSTEM RESET deadlock_timeout; ALTER SYSTEM RESET log_line_prefix; SELECT pg_reload_conf();\""
```

- [ ] **Step 5: Chứng minh teardown đã ăn — đừng tin `pg_reload_conf` một mình**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" "cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging exec -T postgres psql -U datahub_staging -d datahub_staging -At -F'|' -c \"SELECT name, setting, source FROM pg_settings WHERE name LIKE 'log_min_duration_statement' OR name LIKE 'log_lock_waits' OR name LIKE 'deadlock_timeout'\""
```
Expected: cả ba dòng có `source` = `default`. Nếu còn `configuration file` — `ALTER SYSTEM RESET`
chưa ăn; chạy lại Step 4 và **không** kết thúc phiên khi chưa thấy `default`. Đây chính là phép đối
chiếu `source = default` mà §11 của báo cáo P0 đã dùng.

- [ ] **Step 6: Xác nhận đĩa không phình**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'df -h / | tail -1'
```
Expected: mức dùng còn dưới 80 %. Docker json-file log driver có xoay vòng, nhưng ~18 phút ghi mọi
câu lệnh vẫn đáng kiểm.

---

## Task 7: Ghi baseline chuẩn mới vào báo cáo P0

**Files:**
- Modify: `docs/review/p0-report-2026-09-07.md` (§5, §12, §13, §14)

**Interfaces:** Consumes: số từ Task 5 và Task 6. Produces: §13 đóng, quyết định C có chữ ký.

> **Quyết định đính kèm mà §13 buộc phải chọn:** bộ bốn lượt mới là **bộ chuẩn**. §5 giữ nguyên làm
> **hồ sơ lịch sử P0**, dán nhãn rõ *không phải mốc so sánh*. **Không trộn hai bộ.** Lý do bắt buộc
> phải chọn: `baseline_load.py` (docstring dòng 32–53) tự khai rằng bản trước tính `duration_seconds`
> cao hơn tới trọn một interval, nên `sustained_rps` đã công bố **thấp hơn thực tế ~4 % ở concurrency
> 50**, và lease renewal phát theo cụm thay vì rải đều nên mẫu latency bị nhiễu khác đi. Hai bộ số
> không so sánh trực tiếp được, và điều đó là **cố ý**.

- [ ] **Step 1: Thêm §5.0 — nhãn cho bộ số cũ**

Chèn ngay trước bảng §5 hiện có, không xoá gì:

```markdown
> **§5 là hồ sơ lịch sử của đợt đo P0, KHÔNG phải mốc so sánh cho P1/P7.** Mốc chuẩn nay là §5B.
> Bốn lượt dưới đây do các bản `baseline_load.py` cũ sinh ra; docstring của file ấy (dòng 32–53) ghi
> rõ hai khác biệt LÀM DỊCH SỐ: `duration_seconds` cao hơn tới trọn một interval, nên `sustained_rps`
> ở đây thấp hơn thực tế khoảng 4 % tại concurrency 50; và lease renewal phát theo cụm thay vì rải
> đều, nên mẫu latency nhiễu khác. Chạy lại file hiện tại sẽ KHÔNG tái lập các số này — dụng cụ đã
> được sửa mà không đo lại. Ngoài ra, cột từng công bố dưới tên `max_wait_ms` thực ra là
> `max_stmt_age_ms` (tuổi câu lệnh, một cận trên), theo header `lock_wait_sampler.sql` dòng 121–128.
> Giữ §5 lại vì nó là bằng chứng của những gì P0 đã thấy; đừng đọc nó như một mốc.
```

- [ ] **Step 2: Thêm §5B — bảng chuẩn mới**

Cùng hình dạng cột với §5 để đối chiếu được từng trường, thêm ba cột mới. Điền từ
`~/autojms-bench/client-*.json` (Task 5) và output Task 6:

```markdown
### §5B. Baseline chuẩn (đợt Option C, <ngày chạy>)

| Lượt | conc | sent | ok | sustained_rps | p50 | p95 | p99 | transaction_p95 | commit_p95 | lock waiters | max_wait_ms |
|---|---|---|---|---|---|---|---|---|---|---|---|
| interactive-10 | 10 | | | | | | | | | | |
| interactive-50 | 50 | | | | | | | | | | |
| bulk-10 | 10 | | | | | | | | | | |
| bulk-50 | 50 | | | | | | | | | | |

Dụng cụ: `baseline_load.py` (bản hiện tại), `lock_wait_sampler.sql` (truy vấn đã sửa, cột
`max_wait_ms` thật), `pg_log_metrics.py` trên log postgres thu trong đúng cửa sổ. Chồng lấn
sampler/tải đã được TÍNH ở Task 5 Step 2, không giả định.
```

- [ ] **Step 3: Ghi trung thực ba metric — kể cả cái không lấy được**

Viết đúng kết cục đã quan sát, không làm tròn lên thành công:

```markdown
**Ba metric §13 nêu thiếu — kết cục thật:**
- `transaction_p95`, `commit_p95`: PHỤC HỒI. Từ `log_min_duration_statement = 0`, không có sàn.
  Nếu `commit_samples` ≤ 100 thì percentile là null và đó là THIẾU DỮ LIỆU, không phải thành công
  một phần — nói thẳng con số mẫu.
- `counter_lock_wait` p50: <một trong ba kết cục ở Task 6 Step 3b>. Nếu là kết cục 3, viết:
  "Không lượt chờ khoá nào vượt 100 ms trong cả bốn lượt. Đây là một chặn trên toàn phân phối,
  MẠNH hơn một p50, nhưng KHÔNG phải con số §13 hỏi." Không viết "phục hồi cả ba metric".
```

- [ ] **Step 4: Đóng §13 — ghi quyết định và cơ sở**

Thêm vào cuối §13, giữ nguyên bảng A/B/C ở trên:

```markdown
### §13.1 Quyết định: **C**, do Claude Code quyết theo uỷ quyền của Owner ngày 08/09/2026

Owner uỷ quyền nguyên văn: "thay anh đưa ra quyết định". Ba cơ sở, và cả ba đều là chỗ bảng A/B/C
ở trên định giá chưa đúng:

1. **"Giá 0" của A là sai.** A kèm nghĩa vụ BẮT BUỘC giải thích ngoại lai `bulk-10 v2 p99 =
   2198,7 ms` (gấp 5,6 lần) — hoặc chạy lại `bulk-10`. Mà chạy lại `bulk-10` cần ĐÚNG bộ dựng của C:
   10 assertion ký, bench site, cấu hình log. Chênh lệch thật giữa A và C là ~8 phút máy chạy.
2. **Chính repo đã tuyên bố một phần baseline của A là sai.** Xem §5.0. A khiến P1/P7 so với một mốc
   không ai dựng lại được.
3. **C đo đúng binary đang chạy** (Antigravity đã build lại image ngày 08/09), phục hồi hai metric,
   và trả xong nghĩa vụ của A như một hệ quả phụ.

**B** không mua thêm gì so với C mà đắt hơn nhiều: không có dấu hiệu nào cho thấy THIẾT KẾ phép đo
sai — chỉ có dụng cụ thiếu cột và cửa sổ log chưa bật.

**Chi phí C mà bảng trên chưa tính:** một file mới `backend/datahub/tests/mint_bench_assertions.py`
(OD-D). Đường cấp assertion đã đổi khi `DATAHUB_ALLOW_STAGING_TEST_ISSUER` chuyển sang `false`:
`issue-staging-assertion.ps1` ký HMAC nên bị `RsaLicenseAssertionValidator` từ chối, còn
`smoke-test.sh` che output và tự sinh site code. Chi phí này là CHUNG cho C, B, và cho nghĩa vụ bắt
buộc của A.

**Giới hạn C không vượt được:** `counter_lock_wait` p50 bị chặn bởi vật lý dụng cụ, không phải công
sức — sampler là mẫu thiên lệch theo độ dài nên không cho percentile (header dòng 143–148), log có
sàn `deadlock_timeout`. Xem §5B.
```

- [ ] **Step 5: Cập nhật §12 và §14**

§12: thay câu nói hai metric "phải grep trên log mới sinh ra trong bốn lượt chạy lại của C" bằng
trạng thái đã thực hiện, và ghi rằng `pg_log_metrics.py` **nay đã** chạy trên log thật lần đầu, kèm
kết quả. §14: đánh dấu mục §13 là ĐÓNG, và ghi rõ **P1 vẫn KHOÁ** — OD-2 và OD-6 còn chờ chữ ký
Owner, OD-1 hoãn sang P6, OD-8 chưa thoả. Quyết định này chỉ đóng đúng một mục.

- [ ] **Step 6: Commit**

```bash
cd "D:/v1.2605.2(new-test)" && git add docs/review/p0-report-2026-09-07.md && git commit -m "docs(review): record the option C baseline and close the section 13 decision"
```

---

## Task 8: Dọn dẹp, cổng, đẩy

**Files:**
- Modify: `.agent-lock.md`
- Add: 10 file `docs/` chưa track đã tồn tại từ trước đợt này

- [ ] **Step 1: Xoá dấu vết trên VPS**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'shred -u ~/bench-assertions.txt 2>/dev/null || rm -f ~/bench-assertions.txt; rm -rf ~/bench-out; ls ~/bench-assertions.txt ~/bench-out 2>&1 | head -2'
```
Expected: `No such file or directory` cho cả hai. `~/bench-assertions.txt` chứa 10 token còn hiệu
lực; `~/bench-out` chứa output sampler. Cả hai nằm ngoài repo nên **không** cổng bí mật nào bắt được.

- [ ] **Step 2: Xác nhận log thu về không lọt vào cây repo**

```bash
cd "D:/v1.2605.2(new-test)" && git status --porcelain | grep -iE "\.log$|bench" || echo "clean: no bench artefact inside the repo tree"
```
Expected: `clean: ...`. **Đây là kiểm tra thủ công có chủ đích**, vì `check-secrets.ps1` không đọc
`.log`: `$sourceExtensions` không liệt kê `.log`, và part 5 liệt kê bằng
`git ls-files --others --exclude-standard` nên bỏ qua đường dẫn bị ignore. Một file `.log` nằm trong
cây sẽ **không pass nào quét tới**. Giữ `$HOME/autojms-bench/` ở ngoài repo là biện pháp duy nhất.

- [ ] **Step 3: Xoá bản sao log trên máy Owner**

```bash
cd "D:/v1.2605.2(new-test)" && rm -f "$HOME/autojms-bench/pg-bench.log" && ls -la "$HOME/autojms-bench/"
```
Giữ lại `client-*.json` và `lock-*.txt` (không chứa bí mật, là bằng chứng của §5B). Chỉ xoá log —
nó chứa **mọi câu lệnh** đã chạy trong cửa sổ, kèm tham số.

- [ ] **Step 4: Trả khoá `.agent-lock.md`**

File này đang đứng tên `Claude Code` / `Mode: WRITE` với phạm vi của một đợt cũ (firebase rules,
harness, render server, `backend/datahub/docker-compose.yml`, IntegrationTests) — không còn đúng.
Đặt `Current Writer: none`, `Mode: IDLE`, xoá danh sách phạm vi cũ, ghi ngày. Antigravity đã nhắc
đúng điểm này.

- [ ] **Step 5: Cổng đầy đủ**

```bash
cd "D:/v1.2605.2(new-test)" && dotnet build ./AutoJMS.slnx -c Release 2>&1 | tail -5 && powershell -ExecutionPolicy Bypass -File ./eng/harness/verify.ps1 2>&1 | tail -15
```
Expected: build `0 Error(s)`; `OVERALL: ✅ ALL GATES PASSED`. **Không đẩy nếu build hỏng.**

- [ ] **Step 6: Quét rò rỉ hạ tầng trên đúng phần thêm vào**

```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && git diff --cached -U0 | grep '^+' | grep -F "$VPS_IP" && echo "LEAK: ip" || echo "ok: no ip"; git diff --cached -U0 | grep '^+' | grep -F "$VPS_KEY" && echo "LEAK: key path" || echo "ok: no key path"
```
Expected: `ok: no ip`, `ok: no key path`. Repo là **PUBLIC**; chỉ `dev.jmsauto.online` được phép.
**Không** dùng `git grep "$VPS_USER"` làm phép dò: `VPS_USER` = `datahub`, trùng tên thư mục
`backend/datahub/` của chính dự án, nên nó khớp hàng trăm chỗ hợp lệ và vô dụng làm bộ dò rò rỉ.

- [ ] **Step 7: Stage từng đường dẫn một, commit, đẩy**

```bash
cd "D:/v1.2605.2(new-test)" && git add .agent-lock.md docs/agent/CHANG_B_VERIFICATION_REPORT.vi.md docs/review/p0-execution-runbook.vi.md docs/review/p1-prompt-proposal.vi.md docs/review/streaming-v4.2-review.vi.md docs/review/streaming-v4.3-plan.vi.md docs/review/streaming-v4.4-contract.vi.md docs/review/streaming-v4.5-contract.vi.md docs/review/streaming-v4.6-contract.vi.md docs/review/walkthrough-v4.5.md docs/review/walkthrough-v4.6.md && git status --short && git commit -m "docs(plans): add the option C baseline completion plan and release the writer lock" && git push origin main && git log --oneline -1 && git status -sb
```
**Không bao giờ** `git add .`, không bao giờ `git add` một thư mục. Expected: `## main...origin/main`
không kèm `ahead`/`behind`.

Chính file plan này **đã được commit và đẩy từ trước** khi Task 1 chạy — nó là bản trình Owner duyệt
P-1 và OD-D, nên nó không có trong danh sách stage ở trên.

---

## Không làm — nêu rõ để không ai suy diễn ngược

- **Không** bật lại `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true`. Owner đã cấm rõ, và làm thế sẽ đo
  **một validator khác** với validator đang chạy thật — số đo sẽ vô nghĩa cho đúng mục đích của C.
- **Không** sửa `backend/datahub/docker-compose.yml`. Nó là Protected File, và toàn bộ thay đổi
  postgres trong plan này đi qua `ALTER SYSTEM` + SIGHUP: không restart, không chạm compose.
- **Không** sửa `baseline_load.py`, `lock_wait_sampler.sql`, `pg_log_metrics.py`. Chúng vừa được chữa
  xong; sửa tiếp giữa lúc đo là làm hỏng chính phép đo.
- **Không** xoá §5 của báo cáo P0. Dán nhãn, giữ lại. Luật dự án cấm xoá file, và §5 là bằng chứng.
- **Không** viết lại lịch sử để sửa thông điệp commit `6ce9217` (nó mang cùng lỗi "con đường duy nhất"
  đã sửa trong nội dung §12). Sửa nội dung, để lịch sử yên.
- **Không** mở P1. Quyết định này đóng đúng một mục của §13. OD-2 và OD-6 còn chờ chữ ký Owner;
  OD-1 hoãn sang P6; OD-8 chưa thoả.
- **Không** dán khoá private vào chat. Nó sẽ nằm lại trong transcript vĩnh viễn. Dùng biến môi trường
  hoặc `..._FILE` trên máy Owner.

---

## Self-Review (writing-plans)

**1. Spec coverage** — §13 hỏi ba thứ: chọn A/B/C (Task 7 Step 4), lấy lại ba metric thiếu (Task 5,
Task 6, ghi ở Task 7 Step 3 — **hai** phục hồi, cái thứ ba có chặn trên, giới hạn đã nêu rõ), và chọn
bộ nào làm chuẩn (Task 7 Step 1–2). Nghĩa vụ bắt buộc của A — ngoại lai `bulk-10 v2 p99` — được trả
bằng lượt `bulk-10` chạy lại ở Task 5. Teardown bắt buộc: Task 6 Step 4–6. Không mục nào của §13 còn hở.

**2. Placeholder scan** — Còn ba chỗ **cố ý** để trống, cả ba là dữ liệu chỉ tồn tại sau khi chạy chứ
không phải mô tả mơ hồ: giá trị issuer/audience ở Task 3 Step 2 (đọc ở Task 3 Step 1, không được chép
vào file được track), các ô của bảng §5B, và `<một trong ba kết cục>` ở Task 7 Step 3 — ba kết cục ấy
đã liệt kê đầy đủ kèm câu chữ bắt buộc cho mỗi cái. Không có "TBD", không có "xử lý lỗi phù hợp",
không có "tương tự Task N".

**3. Type consistency** — Tên site `BENCH01..BENCH10` thống nhất giữa Task 3, Task 5 và
`seq -f 'BENCH%02g'`. Cờ CLI của `mint_bench_assertions.py` (`--sites --channel --issuer --audience
--expires-in-hours --self-test`) khớp giữa Task 2 Step 3 và Task 3 Step 2. Cờ của `baseline_load.py`
ở Task 5 khớp CLI thật của file. Cột sampler: plan dùng `max_wait_ms` = cột **4**, và nói rõ cột 3
(`max_stmt_age_ms`) là thứ P0 đã công bố nhầm dưới tên `max_wait_ms` — hai tên này không được lẫn ở
Task 7. Biến VPS (`VPS`/`SSHK`/`SSHU`) đặt giống nhau ở mọi lệnh ssh.

**Ba khiếm khuyết tự bắt được khi soát, đã sửa tại chỗ:**
- Bản nháp tính p50 `counter_lock_wait` từ sampler — header của chính dụng cụ cấm điều đó (mẫu thiên
  lệch theo độ dài). Đã chuyển sang log, và hạ giới hạn lời hứa của C cho khớp sự thật.
- `deadlock_timeout = 100ms` ban đầu đặt ở Task 6, tức **sau** khi tải đã chạy xong — vô nghĩa. Đã
  chuyển lên Task 4 Step 1.
- Task 2 ban đầu giả định `smoke-test.sh` mint được 10 assertion. Nó **không**: nó che output và tự
  sinh site code. Đã thành OD-D.