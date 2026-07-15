# Kế hoạch kiểm soát đơn tồn tại bưu cục (dựa trên hành trình & thao tác)

Mục tiêu: kiểm soát **toàn bộ đơn đang tồn tại bưu cục**, đối chiếu với **thao tác của bưu cục
trong dữ liệu hành trình**, để phát hiện đơn bị bỏ quên, đơn có vấn đề, và đo hiệu suất xử lý.

Nền dữ liệu: xem `docs/project/WORK_SUMMARY_2026-07.vi.md` (mục C — hai trụ dữ liệu).

## 1. Nguyên tắc

1. **Tồn kho = working set**: mỗi đơn trong danh sách "Giám sát tồn kho (Big Data)" là một đơn cần kiểm soát cho tới khi rời kho + đạt trạng thái cuối.
2. **Hành trình = bằng chứng thao tác**: mỗi đơn phải có thao tác bưu cục phù hợp mỗi ngày (tối thiểu là "Kiểm tra hàng tồn kho"); thiếu thao tác = rủi ro bỏ quên.
3. **Không xóa dữ liệu cũ** (đã có), chỉ cập nhật; dừng theo trạng thái cuối; retention 30 ngày (đã triển khai).

## 2. Kiến trúc đồng bộ 2 tầng

```
[JMS Big Data]  --(1) list tồn--> pull ~4225 mã (export/paginate 100)
      │
      ▼
[AutoJMS client]  --(2) upsert lô--> merge_waybill_rows_v2()  → public.waybills
      │
      ├─(3) với mỗi mã active: pull hành trình  podTracking/queryCssWork
      │        └─ trích "thao tác bưu cục mới nhất" + loại + thời gian + nhân viên
      │
      ├─(4) upsert timeline → bảng mới waybill_scans (append-only, xem DB plan)
      │
      └─(5) tính chỉ số kiểm soát (SLA, risk, ngày tồn, thiếu thao tác) → cập nhật waybills
```

### Tầng 1 — Đồng bộ danh sách tồn (nhanh)
- Chạy mỗi **5–10 phút**: lấy vài trang đầu (sắp theo thời gian) để bắt đơn mới + thay đổi.
- Chạy **full-refresh 1–2 lần/ngày**: export toàn bộ để đối chiếu, gọi `mark_left_inventory` cho đơn đã rời kho.

### Tầng 2 — Đồng bộ hành trình (theo lịch, ưu tiên)
- Không kéo hành trình mọi đơn mỗi lần (tốn kém). Ưu tiên theo `next_track_at`:
  - Đơn mới / đơn có kiện vấn đề: interval ngắn (15–30 phút).
  - Đơn tồn lâu / rủi ro cao: interval trung bình (1–2 giờ).
  - Đơn ổn định: interval dài (6–12 giờ).
- Dùng index `idx_waybills_site_due_active` để lấy đúng tập đến hạn.

## 3. Tín hiệu kiểm soát trích từ hành trình

Từ timeline `queryCssWork`, phân loại `Loại thao tác` thành nhóm tín hiệu:

| Nhóm | Loại thao tác | Ý nghĩa kiểm soát |
|---|---|---|
| Nhập kho | Xuống hàng kiện đến, Gửi hàng | Đơn đã về bưu cục, bắt đầu tính tồn. |
| Kiểm kho | **Kiểm tra hàng tồn kho** | Bằng chứng nhân viên còn quản lý đơn. Thiếu >24h ⇒ cảnh báo "bỏ quên". |
| Phát | Quét phát hàng, Lịch sử cuộc gọi-phát | Đang xử lý giao. Nhiều lần phát/gọi thất bại ⇒ rủi ro. |
| Vấn đề | **Quét kiện vấn đề** (+ nguyên nhân) | Đơn kẹt: khóa máy, hẹn lại, không liên lạc... ⇒ cần điều phối. |
| Rời kho | (không còn trong list tồn) | Đã đi tiếp/giao — đối chiếu trạng thái cuối. |
| Kết thúc | Ký nhận CPN, Kết thúc | Dừng kiểm soát (theo policy đã có). |

### Chỉ số suy ra (ghi vào `public.waybills`)
- `days_in_inventory`, `age_hours` — thời gian tồn.
- `last_action`, `last_action_time`, `last_site_code`, `employee_code/name` — thao tác gần nhất.
- `risk_score`, `risk_level`, `risk_reasons` — VD: +điểm nếu thiếu kiểm kho >24h, +điểm nếu có kiện vấn đề chưa giải quyết, +điểm nếu >N ngày tồn.
- `sla_status`, `sla_deadline` — so với cam kết thời hiệu (xem tabThoiHieu).

## 4. Quy tắc cảnh báo (alert)

| Cảnh báo | Điều kiện | Hành động đề xuất |
|---|---|---|
| **Bỏ quên** | active + không có thao tác "Kiểm tra hàng tồn kho"/"Quét phát hàng" trong 24h | tạo `dispatch_tasks` (CHECK_PHYSICAL_STOCK) |
| **Tồn quá hạn** | `days_in_inventory` > ngưỡng (VD 3 ngày) | nâng `risk_level`, hiện đỏ trên tabDash |
| **Kiện vấn đề lặp** | ≥2 lần "Quét kiện vấn đề" cùng nguyên nhân | giao điều phối/CSKH |
| **Phát thất bại nhiều** | ≥3 "Quét phát hàng" không thành công | ưu tiên xử lý |
| **Lệch tồn thực tế** | trong list tồn nhưng đã có thao tác rời kho | đối chiếu, sửa trạng thái |

## 5. Lộ trình triển khai

- **Phase 1 — Thu thập**: hoàn tất sync tồn (đã có) + thêm bảng `waybill_scans` và job kéo `queryCssWork` cho đơn active.
- **Phase 2 — Chỉ số & cảnh báo**: viết hàm tính `risk_score`/`sla_status` từ scans; sinh `dispatch_tasks` tự động.
- **Phase 3 — Giao diện**: tabDash hiển thị đơn tồn + tín hiệu; tabThoiHieu hiển thị SLA; ô lọc theo bưu cục/nhân viên/loại vấn đề.
- **Phase 4 — Tự động hóa**: pg_cron/edge function chạy tính chỉ số định kỳ; realtime đẩy cảnh báo xuống client.

## 6. Rủi ro & lưu ý
- Kéo hành trình toàn bộ đơn rất nặng ⇒ bắt buộc lịch ưu tiên theo `next_track_at`.
- Endpoint `jmsgw` yêu cầu phiên đăng nhập hợp lệ ⇒ cần cơ chế giữ session/token phía client AutoJMS (không lưu token vào repo).
- Dữ liệu cá nhân (SĐT người nhận) phải **mask** trước khi lưu (đã có cột `receiver_phone_masked`).
