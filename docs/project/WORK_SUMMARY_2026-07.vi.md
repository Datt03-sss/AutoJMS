# Tổng hợp công việc & khảo sát — AutoJMS (tháng 7/2026)

Tài liệu tổng hợp toàn bộ thao tác đã thực hiện và dữ liệu khảo sát được từ hệ thống JMS,
làm nền cho các kế hoạch kiểm soát tồn kho và mở rộng database FullStackForm.

## A. Những gì đã thao tác (đã hoàn thành & push `origin/main`)

### 1. Dọn & tái cấu trúc repo
- Giảm dung lượng repo từ **3.4 GB → ~370 MB** (xóa build outputs, archive output cũ, node_modules, bin/obj).
- Gộp `.agents/` vào `.agent/`, chuyển 25 workspace phiên cũ vào `archive/agent-sessions/`, dọn root còn README/CLAUDE/AGENTS, sắp xếp lại `docs/` và `eng/`.
- Viết lại `README.md` thành bản đồ cấu trúc repo phục vụ vibe-coding.

### 2. Rule "Skills First"
- Thêm quy tắc: mỗi task ưu tiên dùng skill trong `.agent/skills/` và `.agents/skills/`, nếu không có thì `find-skills`.
- Cài `supabase/agent-skills` (2 skill: `supabase`, `supabase-postgres-best-practices`); bắt buộc dùng cho mọi việc Supabase/Postgres.

### 3. Dựng database Supabase cho FullStackOperation (project `autojms_database` = `jrqxnviixmagiriqysov`)
- Áp 3 migration nền: bootstrap → tighten_privileges → hybrid_sync.
- Bảng: `waybills` (tenancy `site_code` + cột dashboard), `order_notes`, `order_checks`, `dispatch_tasks`, `inventory_sync_leases`, `app_manifest/modules/configs`.
- RPC newest-wins + delta-pull; RLS theo `site_code`; realtime publication; lease chống trùng sync.
- Migration tối ưu: index partial hot-path, RLS cho `app_*`, pin `search_path`; sửa bug `jwt_site_code()` crash khi claims rỗng.

### 4. Đồng bộ non-destructive + dừng theo trạng thái cuối
- `merge_waybill_rows_v2` (upsert theo lô, newest-wins), `finalize_waybills`, `mark_left_inventory`, `is_final_status`.
- Trigger `waybills_stop_on_final` tự dừng tracking khi status = "Ký nhận CPN"/"Kết thúc".

### 5. Chính sách lưu trữ 30 ngày (pg_cron)
- "Ký nhận CPN" → xóa ngay (`purge_signed_receipts`); "Kết thúc" → giữ vô thời hạn cho trọng tài tới khi `mark_waybill_handled`.
- `run_retention_cleanup(30)` chạy hằng ngày qua pg_cron job `autojms-retention-daily` (18:00 UTC).

## B. Khảo sát JMS (qua Claude in Chrome)

Tài khoản `(LCI)Kim Tân`, mã BC `214A02`, chi nhánh Thái Nguyên `208001`. API gateway: `https://jmsgw.jtexpress.vn`.

### B1. Nguồn "danh sách đơn tồn kho" — Giám sát tồn kho (Big Data)
- Đường dẫn: Chỉ số vận hành → Khâu phát → Giám sát tồn kho (Big Data) → tab **Chi tiết**.
- Endpoint: `POST /businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2` (backend Apache Doris).
- Bộ lọc dùng: Phạm vi "Theo TTTC quét gửi kiện", thời gian `today-30 → today`.
- Quy mô: **~4.225 đơn / 1 tháng** cho 1 bưu cục; tối đa **100 dòng/trang**; có **export server-side**; filter Mã vận đơn tra **500 mã/lần**.
- Cột chính: Mã vận đơn, Mã khách hàng, Đích đến, COD, Bưu cục/Mã BC thao tác, PIC, Loại tồn kho, Kiện đang tồn kho, Thời gian tồn kho, Thời gian bắt đầu/kết thúc tồn kho.
- **Quan trọng**: đây là danh sách *đang tồn* — đơn đạt trạng thái cuối sẽ **rời khỏi danh sách**.

### B2. Nguồn "hành trình & thao tác bưu cục" — Tra hành trình
- Đường dẫn: Nền tảng điều hành → Tra hành trình → Tra hành trình (tra tối đa 1000 đơn/lần).
- Endpoints (per-waybill):
  - `POST /operatingplatform/podTracking/queryCssWork` — **timeline thao tác** (nguồn chính).
  - `POST /operatingplatform/podTracking/queryMergerOrderInfo` — thông tin đơn.
  - `POST /operatingplatform/abnormalPieceScanList/pageList` — kiện vấn đề.
  - `GET /operatingplatform/order/getOrderCallHistoryByWaybillNo` — lịch sử cuộc gọi.
  - `GET /operatingplatform/order/getOrderImgByWaybillNo` — ảnh (kiểm kho/POD).
  - `GET /operatingplatform/order/queryShortMsgRecord` — lịch sử SMS.
- Schema timeline (`queryCssWork`): `STT, Thời gian thao tác, Thời gian tải lên, Loại thao tác, Mô tả lịch sử hành trình, Nguồn, Trọng lượng, Trọng lượng quy đổi`.
- **Các loại thao tác bưu cục quan sát được** (rất giá trị cho kiểm soát):
  - `Xuống hàng kiện đến`, `Đóng bao`, `Gửi hàng` — luân chuyển.
  - `Kiểm tra hàng tồn kho` — kiểm kho (lặp mỗi ngày, kèm "Xem hình ảnh").
  - `Quét phát hàng` — nhân viên đang phát.
  - `Quét kiện vấn đề` (đỏ) — kèm nguyên nhân (VD: "Không liên lạc được Khách hàng/khóa máy", "Người mua hẹn lại ngày nhận").
  - `Lịch sử cuộc gọi-phát` — số điện thoại (masked), thời lượng cuộc gọi.
- Mỗi thao tác gắn: **bưu cục** thao tác, **nhân viên** quét mã, **tem xe**, **mã bao**, **nguồn** (Idata/JMS-PC/AI/Thủ công).

### B3. Các màn kiểm soát khác (đã thấy trong menu Nền tảng điều hành, chưa đào sâu)
- GS dữ liệu: Giám sát 2in1, Giám sát hàng phát/đến/gửi (New), Giám sát kiện vấn đề, Hàng giá trị cao, Đơn dừng hành trình, Giám sát hành trình (RT), Giám sát đóng bao.
- Chặn/Hoàn/Tiếp, QL chặn kiện/chuyển hoàn/chuyển tiếp.
- QL ký nhận: Tra cứu đơn ký nhận (Big Data).
- Báo biểu: Đơn hoàn chưa trả người gửi, Giám sát kiện trung chuyển, Thống kê chuyển đơn...

## C. Hai trụ dữ liệu cho kiểm soát hàng

| Trụ | Nguồn | Vai trò |
|---|---|---|
| **Tồn kho hiện tại** | `take_ret_mon_detail_doris2` | Biết đơn nào đang nằm tại bưu cục (working set). |
| **Hành trình/thao tác** | `podTracking/queryCssWork` + phụ trợ | Biết mỗi đơn đã/đang được bưu cục thao tác gì, khi nào, ai làm, có vấn đề gì. |

Kết hợp 2 trụ này là nền cho toàn bộ kế hoạch kiểm soát (xem `docs/roadmap/inventory-control-plan.vi.md`) và mở rộng database FullStackForm (xem `docs/architecture/fullstack-database-plan.vi.md`).
