# Walkthrough v4.6 — Hướng dẫn đọc & thi hành

> Đồng hành với [streaming-v4.6-contract.vi.md](./streaming-v4.6-contract.vi.md).
> Ưu tiên: `brownfield safety > correctness > rollback > simplicity > performance`.

---

## 1. v4.6 khác v4.5 ở đâu

v4.5 đã đúng hướng, nhưng có **bốn phát biểu sai so với code thật**. v4.6 không mở rộng kiến trúc — nó **sửa bốn chỗ đó** và khoá nốt semantics còn hở.

| # | v4.5 nói | Sự thật (đã kiểm code) |
|---|---|---|
| 1 | `ingested_at` = "server observed receive time" | Là **DB `DEFAULT now()` = transaction start time**. API **không bao giờ** ghi cột này (`grep` = 0 kết quả). Và vì `now()` là hằng số trong transaction, **cả 200 item của một bulk request có giá trị giống hệt nhau** ⇒ **không dùng làm tie-break được**. |
| 2 | delete-change giữ 90 ngày ⇒ client có 90 ngày resync | **Sai.** Pruning chỉ cắt **prefix liên tục**; `pruned_through_seq` mới là thẩm quyền. Cursor ≤ mốc đó → `RESYNC_REQUIRED`, kể cả khi delete-change cũ còn nguyên. |
| 3 | Guard client: `incoming.version <= cached.version` | **Bất khả thi cho delete** — delete-change **không mang `version`** (body chỉ có `waybill_no`). Ordering authority phải là **`change_seq`** + **delete marker**. |
| 4 | "RAM chỉ giữ 50–100 object" | **Chưa chứng minh.** Client hiện bind full `List<WaybillDbModel>` vào `DataGridView`, **không** dùng VirtualMode; sort còn tạo thêm bản sao đầy đủ. |

---

## 2. Blocker quan trọng nhất: resurrect cache phía client

Kịch bản làm hỏng dữ liệu người dùng nhìn thấy:

```
1. cache có W ở version 10
2. delete W đến (change_seq 11)  → W bị xoá khỏi cache
3. batch cũ được replay: upsert W version 10
4. cache không còn W ⇒ không có version để so ⇒ W SỐNG LẠI
```

Guard theo `version` không cứu được, vì delete-change **không có** `version`. Và `ProjectChangeItems` hiện chỉ collapse **trong phạm vi một page**, không có guard xuyên page/xuyên replay.

**Cách khoá trong v4.6 (§10)**: dùng `change_seq` làm ordering authority, lưu `lastAppliedChangeSeq` **per-entity**, và khi xoá thì **để lại delete marker** thay vì xoá trắng. Upsert đến sau với `changeSeq` nhỏ hơn marker → ignore.

Và phải phân biệt rạch ròi: **server tombstone** (`waybill_tombstones`, chống resurrect ở server, sống ≥2 năm) **khác** **client delete marker** (cache cục bộ, TTL ngắn). Không được suy ra cái này từ cái kia.

---

## 3. Cursor không phải là view cache

v4.5 gộp hai thứ vào một câu "persist cache → persist cursor". v4.6 tách:

- **Replication state** (`changeCursor`) — bền vững, chỉ tiến khi change đã thực sự được xử lý.
- **View cache** (dashboard current page) — **disposable**, hỏng lúc nào cũng được.

Hệ quả thực tế: nếu refetch trang hiện tại thất bại, **cursor không được rollback**. Trang chỉ bị đánh dấu `stale` và lần đọc sau bắt buộc refetch. Rollback cursor vì lỗi hiển thị là biến sự cố cache thành sự cố dữ liệu — đúng điều §15 cấm: *disk cache failure MUST NOT become business-data failure*.

---

## 4. Owner cần ký gì (P0)

8 quyết định ở **§25**, đã trình bày dạng **mẫu ký**: mỗi mục có lựa chọn, hệ quả, và khuyến nghị kỹ thuật (khuyến nghị **không phải** quyết định). Ba mục chặn P1:

- **OD-1 — terminal scan code.** Chưa có mã nào được chứng minh. Chưa ký thì hệ thống **fail-closed**: P1 chỉ được thêm schema và guard capability, **cấm** implement mã đoán.
- **OD-2 — horizon.** `45d / 60d / 90d / ≥2 năm`, giữ bất biến `ingest < event_retention`.
- **OD-6 — mốc tính terminal retention.** A = `source_event_at` (phải chấp nhận late-terminal purge ngay, cần test) hoặc B = server observed time (an toàn vận hành hơn).

---

## 5. P0 — không đổi một dòng code

Bốn việc phải PASS trước khi được phép migrate:

1. **Backup** (`scripts/backup-postgres.ps1` — đã có)
2. **Restore** vào instance tạm (`scripts/restore-postgres.ps1` — đã có)
3. **Smoke test trên DB đã restore** (`scripts/smoke-test.sh` — đã có, 10 bước end-to-end)
4. **Network**: `Internet→DB:5432` BLOCKED · `API→DB` PASS · TLS VerifyFull PASS

Cộng: **preflight 7 đối tượng** (table/column/type/nullability/default/index/constraint) và **baseline hiệu năng tách riêng interactive vs bulk** (10 & 50 concurrent). Không có baseline → không vào P1.

> Fail bất kỳ bước nào → **STOP RELEASE**. Không migration trước khi backup được verify.

---

## 6. Hai thao tác một chiều

| Thao tác | Vì sao không rollback được bằng config |
|---|---|
| **Terminal projection purge (P6)** | Huỷ dữ liệu thật. Chỉ backup cứu được. Đó là lý do nó nằm sau P3/P4. |
| **Hard-cut LocalDb (P4)** | Một chiều. Rollback chỉ là "remote-only rollback" hoặc full restore — **không** giả định LocalDb quay lại bình thường. Gate là **5 điều kiện**, không phải chỉ `outbox = 0`. |

Và một đính chính về rollback P1: **"tắt terminal guard = full rollback" chỉ đúng khi chưa có terminal/tombstone state nào được tạo.** Khi đã có, rollback phải **preserve** state mới — cấm xoá/đảo schema, đặc biệt cấm xoá tombstone (xoá = mở lại khả năng resurrect).

---

## 7. Rủi ro vận hành vẫn nguyên đó

`DeleteProjectionsAsync` đang ngủ **chỉ vì thiếu một dòng `retention_policies`**, và predicate hiện tại là `updated_at < delete_after` — xoá theo **bất hoạt**, không theo terminal. Một câu `INSERT` là đủ kích hoạt xoá projection còn sống khi chưa có tombstone.

→ **Cấm seed policy đó cho tới khi hết P6.**

---

## 8. Thứ tự đọc

**Owner** → §0 (change log) → §25 (mẫu ký) → §24 (phase gates) → §22 (rollback P1).
**Antigravity** → §1 (baseline khoá) → §26 (đừng rewrite) → §28 (giả định bị cấm) → §29.
**Claude Code** → **§28 và §29 trước tiên** → §26 → §19 (migration) → mục của phase đang làm → §27 (test tương ứng).

§28 là mục đáng đọc nhất: **22 giả định bị cấm**, mỗi dòng kèm lý do đã kiểm bằng code. Phần lớn lỗi trong các bản trước đều bắt nguồn từ một trong số đó.

---

## 9. Trạng thái tài liệu

| File | Vai trò |
|---|---|
| `streaming-v4.6-contract.vi.md` | **Hợp đồng hiện hành** |
| `walkthrough-v4.6.md` | Tài liệu này |
| `streaming-v4.5-contract.vi.md` | Superseded — 4 phát biểu sai đã nêu ở §1 |
| `walkthrough-v4.5.md` | Superseded |
| `streaming-v4.4-contract.vi.md` | Superseded |
| `streaming-v4.3-plan.vi.md` | Superseded |
| `streaming-v4.2-review.vi.md` | Superseded (finding **B5** sai về code) |

---

## 10. Bước tiếp theo

**Chỉ đề xuất Owner duyệt P0.** Không bắt đầu P1 cho tới khi P0 gate PASS và OD-1/OD-2/OD-6 được ký.
