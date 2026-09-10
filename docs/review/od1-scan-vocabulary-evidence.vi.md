# OD-1 — Bằng chứng từ vựng scan type (trích JMS thật, 10/09/2026)

> Tài liệu này là **bằng chứng trình Owner ký OD-1**, không phải quyết định.
> Việc chọn tập terminal code là **của Owner** — §6 của [streaming-v4.6-contract.vi.md](./streaming-v4.6-contract.vi.md) vẫn fail-closed cho tới khi có chữ ký.

---

## 1. Nguồn dữ liệu

| Mục | Giá trị |
|---|---|
| Nguồn | JMS production tracking API (`operatingplatform/podTracking/inner/query/keywordList`) |
| Công cụ trích | [tools/harvest_jms_events.py](../../tools/harvest_jms_events.py) (stdlib-only, read-only với JMS) |
| Danh sách đầu vào | 239 mã vận đơn thật do Owner cung cấp (`docs/manual/waybilltest.xlsx`) |
| Ngày trích | 10/09/2026 |
| Kết quả | **239/239** đơn có hành trình · **4.753** sự kiện quét · **24** cặp `(code, name)` phân biệt |
| Khoảng thời gian sự kiện | `2026-08-17 04:38:35` → `2026-09-10 23:51:31` (giờ VN) |
| Sự kiện bị loại vì `scanTime` không parse được | **0** |

Đây chính là **"bản trích payload JMS thật"** mà §6 của contract yêu cầu, thay cho phương án
"chờ tới production sau P4". Bảng dưới đây **thay thế** kết luận của khảo sát staging 10/09
(*"staging chỉ có 2 mã 98/110 do bộ test sinh"*) — nhưng xem §4, bench residue vẫn còn trên staging.

> ⚠️ **Dữ liệu thô không nằm trong git.** Mỗi sự kiện mang tên/địa chỉ/số điện thoại khách hàng thật
> và 259 mã bưu cục; repo này **PUBLIC**. `docs/manual/samples/` và `docs/manual/waybilltest.xlsx`
> đã được thêm vào `.gitignore`. Ai cần dữ liệu thô phải **chạy lại harvest bằng token JMS của mình**.
> Bảng dưới đây là phần **duy nhất** không chứa PII nên là phần duy nhất được commit.

---

## 2. Bảng từ vựng đầy đủ (tương đương query 0.1)

`pct_final` = tỉ lệ đơn *có* mã này mà mã này là sự kiện **cuối cùng** của đơn.

| code | scan_type_name | events | waybills | times_final | pct_final |
|---:|---|---:|---:|---:|---:|
| 2 | Xuống hàng kiện đến | 1169 | 239 | 10 | 4,2% |
| 1 | Gửi hàng | 1116 | 239 | 3 | 1,3% |
| 30 | Đóng bao | 669 | 216 | 0 | 0,0% |
| 90 | Hàng đến TTTC | 363 | 195 | 0 | 0,0% |
| 219 | Lịch sử cuộc gọi-phát | 284 | 114 | 12 | 10,5% |
| 94 | Quét phát hàng | 270 | 233 | 1 | 0,4% |
| 10 | Nhận hàng | 236 | 235 | 0 | 0,0% |
| 31 | Gỡ bao | 220 | 209 | 1 | 0,5% |
| **100** | **Ký nhận CPN** | **197** | **197** | **193** | **98,0%** |
| 110 | Quét kiện vấn đề | 112 | 66 | 14 | 21,2% |
| 98 | Kiểm tra hàng tồn kho | 70 | 27 | 5 | 18,5% |
| 218 | Lịch sử cuộc gọi-nhận | 8 | 4 | 0 | 0,0% |
| 172 | In đơn chuyển hoàn | 7 | 7 | 0 | 0,0% |
| 176 | Đang chuyển hoàn | 7 | 7 | 0 | 0,0% |
| 170 | Đăng ký chuyển hoàn | 7 | 7 | 0 | 0,0% |
| 50 | Quét mã gửi hàng | 4 | 4 | 0 | 0,0% |
| 225 | Nhận hàng bằng bao | 3 | 3 | 0 | 0,0% |
| 402 | In bill chuyển tiếp | 2 | 2 | 0 | 0,0% |
| 400 | Đăng ký chuyển tiếp | 2 | 2 | 0 | 0,0% |
| 228 | Xác nhận chuyển đơn | 2 | 2 | 0 | 0,0% |
| 227 | Đăng ký chuyển đơn | 2 | 2 | 0 | 0,0% |
| 178 | Đăng ký CH lần 2 | 1 | 1 | 0 | 0,0% |
| 177 | Giao lại hàng | 1 | 1 | 0 | 0,0% |
| 226 | Quét mã tem giá trị cao | 1 | 1 | 0 | 0,0% |

**Không có mã nào ánh xạ ra 2 tên khác nhau** trong dữ liệu thật (query 0.5 → 0 dòng drift).
Ghi lại điều này vì nó **sẽ không đúng** nếu chạy trên staging hiện tại — xem §4.

---

## 3. Ứng viên terminal

### 3.1 `100 / Ký nhận CPN` — ứng viên terminal duy nhất có bằng chứng mạnh

- 197/239 đơn (82,4%) có mã 100.
- **193/197 = 98,0%** số đơn đó kết thúc bằng chính mã 100.
- 4 ngoại lệ đều kết thúc bằng `219 / Lịch sử cuộc gọi-phát` — bản ghi **nhật ký cuộc gọi**
  ghi sau khi đã ký nhận, không phải trạng thái vận chuyển tiếp diễn.

→ Nếu Owner chấp nhận, `100` là terminal; và `219` phải được xếp `event_kind = communication`
để nó **không** phá trạng thái terminal khi tới sau.

### 3.2 Họ chuyển hoàn `170 / 172 / 176 / 177 / 178` — chưa đủ bằng chứng

7 đơn đang trong luồng chuyển hoàn, **không đơn nào đã kết thúc** trong cửa sổ trích.
Về ngữ nghĩa `176 / Đang chuyển hoàn` là trạng thái *đang chạy*, không phải kết thúc;
mã kết thúc của luồng hoàn (hoàn thành trả về người gửi) **không xuất hiện** trong bộ 239 đơn này.
→ **Không đề xuất ký** phần này. Cần một bộ đơn có ca hoàn hoàn tất.

### 3.3 Các mã **không** phải terminal dù có `times_final > 0`

`110` (14 lần), `2` (10), `219` (12), `98` (5), `1` (3): đây là các đơn **còn đang chạy** tại thời điểm
trích, không phải đơn đã kết thúc. Đừng đọc `times_final` mà bỏ qua `pct_final`.

---

## 4. Ba cảnh báo phải xử lý trước khi chạy query chính thức

### 4.1 Cửa sổ settle 14 ngày làm query 0.2 mù trên bộ dữ liệu này

`od1_scan_vocabulary.sql` query 0.2 chỉ đếm đơn có sự kiện cuối **cũ hơn 14 ngày** (CTE `settled`).
Bộ 239 đơn này quá mới:

| Ngưỡng settle | Số đơn đủ điều kiện |
|---|---|
| ≥ 14 ngày (mặc định) | **1** / 239 |
| ≥ 7 ngày | 2 / 239 |
| ≥ 3 ngày | 3 / 239 |
| ≥ 1 ngày | 4 / 239 |

Chỉ **13/4.753** sự kiện (0,3%) cũ hơn 14 ngày. Chạy query 0.2 hôm nay trả về **đúng 1 dòng**, và
đơn đó kết thúc bằng `2 / Xuống hàng kiện đến` — **rõ ràng không phải terminal**. Tức là query sẽ
đưa ra một câu trả lời *sai mà trông có vẻ hợp lệ*.

**Lựa chọn cho Owner** (xếp theo chi phí):
1. **Chạy lại query 0.2 khoảng 25/09/2026** — dữ liệu đã nạp sẵn, không cần trích lại, giữ nguyên
   cửa sổ 14 ngày. *Khuyến nghị.*
2. Trích thêm một bộ mã vận đơn **cũ hơn 30 ngày** rồi chạy ngay.
3. Hạ cửa sổ settle và **ghi rõ** đã hạ — làm yếu bằng chứng, không khuyến nghị.

§3.1 ở trên (`pct_final` theo đơn *có* mã) **không** phụ thuộc cửa sổ settle, nên vẫn dùng được ngay.

### 4.2 Seed `002` gán sai nhãn cho code 110

[backend/datahub/migrations/002_seed_policies.sql](../../backend/datahub/migrations/002_seed_policies.sql) hiện seed:

```sql
(1,  98, 'inventory'),
(1, 110, 'state_transition')
```

Đối chiếu JMS thật: `98 = "Kiểm tra hàng tồn kho"` → khớp `inventory` ✅.
Nhưng `110 = "Quét kiện vấn đề"` (quét kiện có vấn đề) — **không phải** một `state_transition` chung chung.
Nhãn này được đặt khi chưa có dữ liệu thật; giờ đã có. Sửa nhãn thuộc P1, không thuộc P0.

### 4.3 Bench residue trên staging sẽ làm hỏng kết quả query

`baseline_load.py` hardcode `code: 110, scanTypeName: "state_transition"`, và staging đang có
**7.800 dòng `BENCH-%`**. Nếu nạp 4.753 dòng thật vào cùng bảng rồi chạy `od1_scan_vocabulary.sql`:

- **Query 0.5 sẽ báo drift giả**: `code 110 → 2 tên` (`state_transition` của bench vs `Quét kiện vấn đề` thật).
- **Query 0.1 sẽ bị nhấn chìm**: 7.802 dòng bench áp đảo 4.753 dòng thật.

→ Trước khi chạy bằng chứng chính thức, phải **loại `waybill_no LIKE 'BENCH-%'`** khỏi query,
hoặc xoá các dòng đó. **Xoá dữ liệu khỏi DB đang phục vụ là quyết định của Owner** — không tự làm.

---

## 5. Cấu trúc payload JMS thật (đầu vào thiết kế schema P1)

`details[]` có **34 khoá phân biệt**. Tần suất trên mẫu 110 sự kiện đầu:

| Nhóm | Khoá | Độ phủ |
|---|---|---|
| Luôn có | `billCode`, `waybillNo`, `scanTime`, `scanTypeName`, `scanByCode`, `scanByName`, `waybillTrackingContent`, `trackTemplate`, `code`, `scanNetworkCode`, `uploadTime` | 110/110 |
| Gần như luôn có | `scanNetworkName` (104), `remark6` (98) | ≥ 89% |
| Thường có | `packageNumber` (58), `imgType` (56), `nextStopName`/`nextNetworkCode` (51), `status`/`taskCode` (46), `remark2` (42) | 40–55% |
| Thưa | `remark1` (38), `remark4` (28), `remark5` (16), `staffCode`/`staffName`/`staffContact` (15), `weight` (8), `remark3` (7), `remark7`/`remark9`/`stationCode` (6), `length`/`width`/`high` (2) | < 35% |

Ba điểm ảnh hưởng thiết kế P1:

1. **`code` và `scanTypeName` phủ 100%** → an toàn để đặt `NOT NULL` cho `scan_type_code`/`scan_type_name`.
2. **`status` chỉ phủ 42%** → phải nullable; không được dùng làm khoá phân vùng hay điều kiện terminal.
3. **JMS phát giờ tường (`yyyy-MM-dd HH:mm:ss`) không kèm offset** — đúng nhánh Asia/Ho_Chi_Minh của
   [ScanTimeParser.cs:46](../../src/AutoJMS.DataHub.Api/Domain/ScanTimeParser.cs). Nối thêm `Z` sẽ
   **dịch mọi sự kiện đi 7 giờ**. Đây là bẫy phải ghi rõ trong bất kỳ ETL nào ở P1.

---

## 6. Ô ký của Owner

| Hạng mục | Đề xuất dựa trên bằng chứng | Owner ký |
|---|---|---|
| Terminal code chính | `100` (Ký nhận CPN) — 98,0% `pct_final` | ☐ |
| `219` = communication, không phá terminal | Có | ☐ |
| Họ chuyển hoàn `170/172/176/177/178` | **Chưa ký** — thiếu ca hoàn tất | ☐ |
| Cách biểu diễn | Theo `code`, không theo `name` (name có thể đổi theo locale) | ☐ |
| Xử lý bench residue trước khi chạy query | Loại trừ / xoá — **Owner chọn** | ☐ |
| Thời điểm chạy query 0.2 | ~25/09/2026 sau khi 239 đơn settle | ☐ |

> Nhắc lại điều kiện trước P6: **chạy lại query OD-1 trên production (read-only)** để xác nhận
> danh sách terminal đã ký vẫn đúng khi khối lượng lớn hơn.
