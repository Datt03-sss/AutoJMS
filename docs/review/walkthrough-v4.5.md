> ⛔ **SUPERSEDED bởi [streaming-v4.6-contract.vi.md](./streaming-v4.6-contract.vi.md)** (2026-09-05). v4.6 sửa 4 phát biểu sai của bản này: (1) `ingested_at` là transaction start time từ DB default, không phải ingress time, và giống hệt nhau cho mọi item trong một bulk; (2) delete-retention 90d KHÔNG cho client 90 ngày delta-resync; (3) version guard bất khả thi cho delete (delete-change không mang version); (4) tuyên bố RAM 50-100 object chưa được chứng minh.

# Walkthrough v4.5 — Hướng dẫn đọc & thi hành Pre-Implementation Contract

> Tài liệu đồng hành của [streaming-v4.5-contract.vi.md](./streaming-v4.5-contract.vi.md).
> Mục đích: giúp Owner duyệt nhanh, và giúp Antigravity/Claude Code thi hành **không suy diễn**.

---

## 1. v4.5 là gì (và không là gì)

**Là**: hợp đồng tiền-triển-khai cho một hệ thống **brownfield đang chạy**. Nó mô tả **delta tối thiểu** cần thêm, các **cổng an toàn** phải qua, và các **quyết định của Owner** còn treo.

**Không là**: bản thiết kế kiến trúc mới. v4.5 **không** thêm cơ chế mới nếu code hiện tại đã giải quyết đúng. Phần lớn giá trị của nó nằm ở việc **ngăn viết lại thứ đã đúng** và **ngăn suy diễn semantics**.

Thứ tự ưu tiên khi có xung đột:
```
brownfield safety  >  correctness  >  rollback  >  đơn giản  >  performance
```

---

## 2. Thay đổi lớn nhất so với v4.4

| Nhóm | Điều quan trọng nhất |
|---|---|
| **Sửa lỗi định danh** | `ReadWriteSiteData` **không tồn tại**. Enum thật chỉ có `ReadSiteData` và `WriteSiteData`. Và **admin không phải capability** — cả 3 role đều giữ cả hai capability, nên `reopen` phải đi qua admin/operator token. Đây là loại lỗi khiến Claude Code tự "phát minh" enum. |
| **Xác minh thay vì xây mới** | Yêu cầu tách interactive vs leader-fenced **đã có sẵn** trong `IngestEndpoints.cs`. v4.5 chuyển nó từ "việc phải làm" sang "việc phải test". |
| **Nâng lên Owner Decision** | Mốc tính terminal retention (`source_event_at` vs received time) trở thành **OD-6**, cấm implementation tự chọn. |
| **Hoãn có kỷ luật** | Historical Replay tách Contract / Implementation — **cấm** tạo endpoint khi chưa có archive source (OD-7). `IChangeSequenceAllocator` hoãn thành refactor sau test. |
| **Cổng an toàn mới** | Backup→restore→smoke test và Infrastructure gate trở thành **P0 bắt buộc**. Fail → STOP RELEASE. |
| **Migration nghiêm hơn** | Preflight 5 trạng thái thay cho kiểm nhị phân; partial state → STOP + REPORT, **cấm auto-skip**. |
| **Client đơn giản hơn** | Dashboard cache chỉ giữ **current page**, disposable, debounce 300–500ms. Thêm hành vi bắt buộc khi **disk full**. |
| **Rollback tường minh** | Mục riêng: điều kiện kích hoạt và hành động cho từng phase, kèm 2 điểm **không thể rollback bằng config**. |

---

## 3. Thứ tự đọc đề xuất

**Owner (duyệt)** → §0 (bảng thay đổi) → §21 (Owner Decisions) → §19 (Phase & Gates) → §20 (Rollback).
**Antigravity (soạn prompt)** → §1 (khoá) → §22 (đừng code lại) → §24 (ràng buộc) → phần kỹ thuật liên quan.
**Claude Code (thi hành)** → §24 trước tiên → §22 → §10 (migration) → mục của phase đang làm → §23 (test tương ứng).

---

## 4. Điều Owner cần ký trước khi bất kỳ dòng code nào được viết

Có **8 quyết định** đang treo (§21). Ba cái **chặn P1**:

- **OD-1 — danh sách terminal scan code.** Chưa có cơ sở: seed hiện chỉ phân loại `98` (inventory) và `110` (state_transition). Không có mã nào được chứng minh là terminal. Cho tới khi ký, hệ thống **fail-closed**: `is_terminal=false`, không tombstone, không purge.
- **OD-2 — các con số horizon.** Đề xuất `45d / 60d / 90d / ≥2 năm`.
- **OD-6 — mốc tính terminal retention.** A = `source_event_at` (phải chấp nhận hành vi late-terminal purge ngay) hoặc B = server received time.

Ba cái chặn phase sau: **OD-3** (reopen/rebuild), **OD-5** (fingerprint), **OD-7** (archive source cho replay).
Hai cái thuộc vận hành: **OD-4** (DPAPI theo mức PII), **OD-8** (tần suất backup → RPO; RTO phải **đo** ở P0, không ước lượng).

---

## 5. Cổng P0 — không thương lượng

Không có dòng code nào được đổi ở P0. Bốn việc phải PASS:

1. **Backup** DB hiện tại (`scripts/backup-postgres.ps1` — đã có).
2. **Verify restore** vào instance tạm (`scripts/restore-postgres.ps1` — đã có).
3. **Smoke test trên DB đã restore** (`scripts/smoke-test.sh` — đã có, 10 bước end-to-end).
4. **Infrastructure**: `Internet→DB:5432` BLOCKED · `API→DB` qua WireGuard ALLOWED · TLS VerifyFull PASS · hostname khớp.

Cộng thêm: **preflight matrix** schema và **baseline throughput** (10 & 50 concurrent) để P1 có mốc so sánh.

> Bất kỳ mục nào FAIL → **STOP RELEASE**. Tuyệt đối không "migration trước, backup sau".

---

## 6. Rủi ro vận hành nguy hiểm nhất (đọc kỹ)

`RetentionRepository.DeleteProjectionsAsync` **đã tồn tại và đang ngủ**. Vị từ hiện tại của nó là:

```
p.updated_at < now() - delete_after      -- theo BẤT HOẠT, không theo terminal
```

Nó ngủ chỉ vì **chưa có dòng `retention_policies` cho `waybill_projections`**. Nghĩa là: **một dòng SQL INSERT là đủ để kích hoạt việc xoá projection còn sống** — trong khi tombstone chưa tồn tại, nên đơn có thể bị **hồi sinh** bởi event đến muộn.

→ **CẤM seed policy đó cho tới khi hết P6.** Đây là rủi ro cụ thể, không phải lý thuyết.

---

## 7. Bản đồ phase (rút gọn)

```
P0  Freeze + Backup + Infra + OD + baseline      ── không đổi code
P1  Server Safety: terminal, tombstone, horizon  ── không Redis, không client
P2  Server Read: /waybills /detail /history+ETag  ── reuse /changes + snapshot
P3  Client: disk cache, multi-site cursor         ── site-scoped, idempotent merge
P4  Write cutover: interactive + bulk fenced      ── drain outbox = 0 rồi mới hard-cut
P5  SignalR doorbell (no payload)                 ── Safety Poll là backstop
P6  Retention: bật purge terminal                 ── thao tác HUỶ DỮ LIỆU
P7  Production validation: load + chaos           ── 50 concurrent, disk full, restart
R1  (Optional) Redis Detail Pointer               ── chỉ khi P7 chứng minh cần
```

Hai điểm **không rollback được bằng config**: **P6** (huỷ dữ liệu — chỉ backup cứu được) và **P4 hard-cut LocalDb** (một chiều). Đó là lý do cả hai nằm sau các phase kiểm chứng.

---

## 8. Ranh giới cho Claude Code

Được phép: thêm cột/bảng additive · thêm guard fail-closed · thêm test · đổi **vị từ** purge sang `is_terminal` · wrapper không đổi execution order.

**Không được phép**: implement mục `⛔ PENDING OD-x` · tự chọn business policy · gọi Redis là dependency · implement Dashboard Epoch · viết lại snapshot/idempotency/change-feed/retention · đổi allocation semantics của `change_seq` · dùng định danh không tồn tại · tạo endpoint replay khi chưa có archive source.

Khi contract mâu thuẫn với code thật → **dừng và báo cáo**, không tự hoà giải.

---

## 9. Trạng thái tài liệu

| File | Vai trò |
|---|---|
| `streaming-v4.5-contract.vi.md` | **Hợp đồng hiện hành** |
| `walkthrough-v4.5.md` | Tài liệu này — hướng dẫn đọc |
| `streaming-v4.4-contract.vi.md` | Superseded — tra lịch sử |
| `streaming-v4.3-plan.vi.md` | Superseded |
| `streaming-v4.2-review.vi.md` | Superseded (lưu ý: finding **B5** sai về code) |
