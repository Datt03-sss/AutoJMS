# Kế hoạch xây dựng database cho FullStackForm

Thiết kế database (Supabase `autojms_database` = `jrqxnviixmagiriqysov`) phục vụ các tab của
FullStackOperation: **tabDash**, **tabThoiHieu**, và các tab mở rộng trong tương lai.

Nguyên tắc chung (đã áp dụng): tenancy theo `site_code` + RLS; ghi qua RPC SECURITY DEFINER
(không cho ghi trực tiếp bảng); newest-wins; realtime publication; retention 30 ngày; **không xóa** ngoài policy.

## 1. Hiện trạng (đã có)

| Bảng | Dùng cho |
|---|---|
| `waybills` | Nguồn chính tabDash & tabThoiHieu (đơn tồn + chỉ số) |
| `order_notes` | Ghi chú thao tác (append-only) |
| `order_checks` | Đánh dấu đã kiểm (newest-wins) |
| `dispatch_tasks` | Nhiệm vụ điều phối/xử lý |
| `inventory_sync_leases` | Chống trùng sync giữa nhiều máy |
| `app_manifest/modules/configs` | Manifest & cấu hình |

RPC đã có: `merge_waybill_rows_v2`, `pull_waybill_delta`, `finalize_waybills`, `mark_left_inventory`,
`purge_signed_receipts`, `mark_waybill_handled`, `run_retention_cleanup`, các lease/notes/checks/tasks RPC.

## 2. Bảng mới cần bổ sung

### 2.1 `waybill_scans` — timeline thao tác bưu cục (Phase 1)
Nguồn: `podTracking/queryCssWork`. Append-only, idempotent theo `(site_code, waybill_no, scan_seq)` hoặc hash.

```
waybill_scans(
  id uuid pk,
  site_code text,
  waybill_no text,
  scan_seq integer,               -- STT trong timeline
  scan_time timestamptz,          -- Thời gian thao tác
  upload_time timestamptz,        -- Thời gian tải lên
  op_type text,                   -- Loại thao tác (Kiểm tra hàng tồn kho, Quét phát hàng...)
  op_group text,                  -- nhóm suy ra: INBOUND/STOCKCHECK/DELIVERY/PROBLEM/OUTBOUND/FINAL
  description text,               -- Mô tả lịch sử hành trình
  source text,                    -- Nguồn (Idata/JMS-PC/AI/Thủ công)
  op_site_code text, op_site_name text,
  employee_code text, employee_name text,
  problem_reason text,            -- nguyên nhân kiện vấn đề (nếu có)
  weight double precision, volumetric_weight double precision,
  client_id text,                 -- idempotent push
  created_at timestamptz,
  unique(site_code, waybill_no, scan_seq)
)
index (site_code, waybill_no, scan_time desc)
index (site_code, op_group, scan_time desc)   -- lọc theo nhóm tín hiệu
```
RPC: `merge_waybill_scans(site, jsonb[])` (upsert idempotent), `pull_scans_delta(site, since, limit)`.

### 2.2 `waybill_problems` — kiện vấn đề (Phase 2, tùy chọn tách riêng)
Nguồn: `abnormalPieceScanList/pageList`. Có thể suy từ `waybill_scans` (op_group=PROBLEM); tách bảng nếu cần trạng thái xử lý riêng.

```
waybill_problems(site_code, waybill_no, reason, first_seen_at, last_seen_at,
                 occurrence_count, resolved boolean, resolved_at, updated_at,
                 primary key(site_code, waybill_no, reason))
```

## 3. Ánh xạ dữ liệu theo tab

### tabDash — Dashboard đơn tồn realtime
- Nguồn: `waybills` (đơn `is_active AND is_in_current_inventory`) + tín hiệu mới nhất từ `waybill_scans`.
- Cột hiển thị (khớp UI hiện tại): STT, Mã vận đơn, NV xử lý cuối, Trạng thái hiện tại, Thao tác cuối, Thời gian thao tác, NV/nguyên nhân kiện vấn đề, Số lần nhắc, Cập nhật lúc.
- Đọc nhanh: `pull_waybill_delta` + realtime; index `idx_waybills_dash_hot`.
- View đề xuất: `v_dashboard_active` = join `waybills` với thao tác cuối (lateral lấy scan mới nhất).

### tabThoiHieu — Giám sát SLA/thời hiệu
- Nguồn: `waybills.sla_status`, `sla_deadline`, `days_in_inventory`, `age_hours`.
- Cần hàm tính SLA từ mốc "TTTC quét gửi kiện"/thời gian tồn so với cam kết → cập nhật khi sync.
- Index `idx_waybills_site_sla` (đã có). View `v_sla_breaching` = đơn quá/sắp quá hạn.

### Tab tương lai (đề xuất khung sẵn)
| Tab dự kiến | Bảng/nguồn | Ghi chú |
|---|---|---|
| Kiện vấn đề | `waybill_problems` / scans PROBLEM | điều phối xử lý, CSKH |
| Điều phối | `dispatch_tasks` | giao việc kiểm kho/xử lý |
| Kiểm kho | `order_checks` + scans STOCKCHECK | đối chiếu kiểm kho thực tế |
| Trọng tài | `waybills` (is_handled) + notes | đơn "Kết thúc" chờ xử lý khiếu nại |
| Chat/Zalo | (đang dùng ZaloChatService) | có thể thêm bảng `chat_messages` nếu cần lưu |

## 4. Chuẩn hoá & quy ước

- **Mọi bảng**: có `site_code`, `updated_at`; bật RLS scoped `site_code`; chỉ ghi qua RPC.
- **Idempotent**: bảng append-only dùng `client_id`/khóa tự nhiên + `on conflict do nothing/update`.
- **Newest-wins** cho bảng trạng thái (`where excluded.updated_at >= t.updated_at`).
- **Realtime**: thêm bảng mới vào publication `supabase_realtime`.
- **Retention**: bảng scans/problems theo cùng vòng đời `waybills` — khi đơn bị purge, xóa scans liên quan (thêm FK `on delete cascade` hoặc bước trong `run_retention_cleanup`).
- **search_path** pin cho mọi function; grant execute chỉ cho RPC site-scoped.

## 5. Lộ trình migration đề xuất

1. `2026071x_waybill_scans.sql` — bảng scans + RPC merge/pull + realtime + cascade retention.
2. `2026071x_sla_compute.sql` — hàm tính SLA/risk từ scans, cập nhật `waybills`.
3. `2026071x_waybill_problems.sql` — (nếu cần) bảng kiện vấn đề + RPC.
4. `2026071x_dashboard_views.sql` — `v_dashboard_active`, `v_sla_breaching`.

Mỗi migration: idempotent, chạy `get_advisors` sau khi áp, build/verify + commit theo `CLAUDE.md`.

## 6. Sơ đồ quan hệ (rút gọn)

```
waybills (waybill_no PK, site_code)
   ├─1..n─ waybill_scans      (timeline thao tác)
   ├─1..n─ order_notes        (ghi chú)
   ├─0..1─ order_checks       (kiểm kho)
   ├─0..n─ dispatch_tasks     (điều phối)
   └─0..n─ waybill_problems   (kiện vấn đề)
```

> Lưu ý: các thay đổi schema/bảng mới thuộc vùng "Database schema migrations" (protected trong `CLAUDE.md`)
> — chỉ triển khai khi chủ dự án yêu cầu cụ thể cho từng migration.
