# Phân tích & so sánh 2 nguồn dữ liệu tồn kho + ý tưởng chống sót đơn

> Trạng thái khảo sát: **Nguồn 1 (Big Data)** đã khảo sát trực tiếp qua Claude in Chrome (endpoint,
> cột, quy mô đều xác nhận). **Nguồn 2 (Thống kê kiểm kho)** mô tả dựa trên yêu cầu của chủ dự án +
> logic nghiệp vụ JMS — **cần xác minh lại cột/endpoint** khi truy cập lại (đánh dấu ⚠️).
> Phạm vi: chỉ dùng 2 nguồn cho trước, không dùng nguồn hành trình (`queryCssWork`) làm input ở bước này.

## 1. Hai nguồn

### Nguồn 1 — Giám sát tồn kho (Big Data), tab Chi tiết
- Đường dẫn: Chỉ số vận hành → Khâu phát → Giám sát tồn kho (Big Data) → Chi tiết.
- Bộ lọc: Phạm vi **"Theo TTTC quét gửi kiện"**, thời gian **today-30 → today**.
- Endpoint: `POST /businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2` (Apache Doris).
- Neo dữ liệu: theo **thời điểm quét GỬI kiện** (send-scan) trong 30 ngày.
- Xuất file: lấy **toàn bộ mã, không cần page**.
- Bản chất: "hệ thống TIN rằng đơn đang tồn tại bưu cục" — suy từ logic quét, **bao gồm cả tồn ảo**
  (mis-scan "Xuống hàng kiện đến", nhảy mã, nhập tay sai) dù không có hàng thực.

### Nguồn 2 — Thống kê kiểm kho ⚠️
- Đường dẫn: Chỉ số vận hành → Khâu phát → Thống kê kiểm kho.
- Bộ lọc: **Thời gian tồn 1 ngày (today-today)** → Số đơn tồn.
- Neo dữ liệu: theo **thao tác KIỂM KHO** (kiểm tra hàng tồn kho) — chỉ đơn được **quét kiểm kho vật lý** trong ngày.
- Xuất file: lấy **toàn bộ mã, không cần page**.
- Bản chất: "đơn được XÁC NHẬN có mặt vật lý hôm nay" — nhân viên thực sự cầm/quét kiện.

## 2. So sánh

| Tiêu chí | Nguồn 1 (Big Data 30d) | Nguồn 2 (Kiểm kho today) |
|---|---|---|
| Neo thời gian | Quét gửi kiện, cửa sổ **30 ngày** | Kiểm kho, **1 ngày** |
| Điều kiện xuất hiện | Hệ thống tính là tồn (theo scan) | Có bản ghi kiểm kho vật lý hôm nay |
| Bắt **tồn ảo** (mis-scan A2) | **CÓ** — đây là nơi lộ ra tồn ảo | **KHÔNG** (không có hàng thật thì không kiểm kho được) |
| Bắt **đơn tồn > 30 ngày** | **KHÔNG** (rơi khỏi cửa sổ) | **CÓ** (còn hàng thì vẫn kiểm kho, vẫn hiện) |
| Bắt đơn **chưa kiểm kho hôm nay** | CÓ (nếu trong 30d) | KHÔNG (chưa được kiểm hôm nay) |
| Độ "thật" của tồn | Trung bình (lẫn tồn ảo) | Cao (đã xác nhận vật lý) |
| Độ phủ theo thời gian | Rộng nhưng giới hạn 30 ngày | Chỉ 1 ngày, phải cộng dồn nhiều ngày |
| Quy mô/đợt | ~4.225 đơn/tháng/BC | Bằng số đơn kiểm kho/ngày |

### Quan hệ tập hợp (theo cùng 1 bưu cục)
```
        Nguồn 1 (Big Data 30d)                 Nguồn 2 (Kiểm kho today)
   ┌───────────────────────────┐        ┌───────────────────────────┐
   │  tồn recent (<30d)         │        │  đã kiểm kho hôm nay       │
   │  + TỒN ẢO (mis-scan)  ⚠️   │        │  (xác nhận vật lý)         │
   └───────────────────────────┘        └───────────────────────────┘
              └──────────────┬───────────────────┘
     S1 ∩ S2  = tồn thật, đã kiểm hôm nay  (khỏe)
     S1 \ S2  = trong Big Data nhưng chưa kiểm hôm nay
                → hoặc TỒN ẢO (mis-scan)  → cần điều tra/đánh dấu
                → hoặc hàng thật chưa kiểm → cần đi kiểm kho
     S2 \ S1  = kiểm kho có, Big Data KHÔNG (send-scan > 30 ngày)
                → đơn tồn LÂU vẫn còn hàng → S1 một mình sẽ SÓT ⚠️
```

## 3. Nhận định chọn nguồn

- **Không nguồn nào đủ một mình.**
  - Chỉ dùng S1 → **sót đơn tồn > 30 ngày** (long-tail vẫn còn hàng thực).
  - Chỉ dùng S2 → **sót tồn ảo** (mis-scan A2) và sót đơn chưa kịp kiểm kho hôm nay.
- **Kết hợp (hợp nhất) hai nguồn** là cách duy nhất phủ hết:
  - S1 cho **bề rộng + lộ tồn ảo** (đúng ca A2 mà chủ dự án lo).
  - S2 cho **đuôi dài quá 30 ngày + xác nhận vật lý**.

## 4. Ý tưởng theo dõi — giảm tối đa sót đơn (chỉ dùng 2 nguồn)

### 4.1 Nạp dữ liệu
- Mỗi ngày **export full cả 2 nguồn** (không page).
- **S1 = tập nền (master)**: upsert vào `public.waybills` (đã có, newest-wins, không xóa).
  - Đánh dấu `source_bd = true`, cập nhật `last_seen_bd_at`.
- **S2 = tín hiệu xác nhận vật lý**: với mỗi mã trong S2, cập nhật cờ:
  - `physically_checked = true`, `last_stockcheck_at = today`, `source_kk = true`.
  - **Nếu mã trong S2 mà CHƯA có trong DB** (tức S2\S1, đơn >30 ngày) → **INSERT mới** để không sót.

### 4.2 Đối soát hằng ngày (reconciliation)
| Nhóm | Định nghĩa | Xử lý |
|---|---|---|
| **Khỏe** | có ở S1 **và** S2 | tồn thật, đã kiểm hôm nay — theo dõi bình thường |
| **Nghi tồn ảo** | có ở S1, **không** ở S2 nhiều ngày liên tiếp | cờ `suspect_phantom=true`; nếu thao tác gần nhất chỉ là "Xuống hàng kiện đến" tại BC mình mà không có kiểm kho/phát → đưa vào danh sách **cần xử lý** |
| **Đuôi dài** | có ở S2, **không** ở S1 (send-scan >30d) | INSERT/giữ, đánh dấu `long_tail=true` — **đây là đơn S1 sẽ sót** |
| **Rời kho** | trước có, nay **không** ở cả S1 lẫn S2 | `is_in_current_inventory=false`, giữ theo dõi tới khi rõ trạng thái cuối |

### 4.3 Quy tắc giữ/loại (theo yêu cầu chủ dự án)
- Đơn có **bất kỳ thao tác tại bưu cục mình** (kể cả mis-scan "Xuống hàng kiện đến") → **lưu và giữ** trong DB để theo dõi/xử lý.
- **Chỉ loại khỏi DB khi**: (a) có **hành trình mới hơn** cho thấy đơn đã đi tiếp/không còn thuộc bưu cục mình, **hoặc** (b) được **đánh dấu "Đã xử lý"** (`is_handled`, đã có sẵn).
- Kết hợp policy đã triển khai: "Ký nhận CPN" xóa ngay; "Kết thúc" giữ cho trọng tài; retention 30 ngày cho phần còn lại — **ngoại trừ** đơn đang `suspect_phantom`/`long_tail` chưa xử lý thì **không** để retention tự xóa (giữ tới khi (a)/(b)).

### 4.4 Cộng dồn chống sót (union theo thời gian)
- Vì S1 là cửa sổ trượt 30 ngày và S2 chỉ 1 ngày, **không lấy S mỗi ngày rồi ghi đè** — mà **hợp nhất tích lũy** vào `waybills`:
  - Mỗi ngày chỉ **thêm mã mới + cập nhật cờ**, không xóa mã cũ ngoài quy tắc 4.3.
  - Nhờ vậy: đơn hôm nay rơi khỏi cửa sổ 30 ngày của S1 **vẫn còn trong DB** và tiếp tục được S2 xác nhận nếu còn hàng → **không sót**.
- Chạy S2 **mỗi ngày liên tục** là chốt chặn quan trọng nhất cho đuôi dài.

### 4.5 Chỉ số giám sát đề xuất (ghi vào `waybills`)
- `source_bd`, `source_kk`, `last_seen_bd_at`, `last_stockcheck_at`.
- `days_since_last_stockcheck` — kiểm kho gần nhất cách bao lâu (thiếu lâu ⇒ rủi ro).
- `suspect_phantom`, `long_tail` — 2 cờ chống sót.
- `needs_action` = `suspect_phantom OR (đang tồn AND days_since_last_stockcheck > N)`.

## 5. Kết luận
- **Chọn hợp nhất S1 ∪ S2**, không chọn một. S1 lo bề rộng + tồn ảo; S2 lo đuôi dài + xác nhận thật.
- Cơ chế **upsert tích lũy, không xóa** + đối soát chéo mỗi ngày + 2 cờ `suspect_phantom`/`long_tail`
  cho phép **giảm tối đa sót đơn**, đồng thời vẫn giữ đúng luật "chỉ loại khi có hành trình mới hơn hoặc đã xử lý".

## 6. Việc cần xác minh lại (khi Chrome truy cập được)
- ⚠️ Cột & endpoint chính xác của **Thống kê kiểm kho** (tên trường, có cột "mã vận đơn", "bưu cục", "thời gian kiểm kho", "nhân viên kiểm" không).
- ⚠️ Xác nhận S2 có thực sự loại tồn ảo (mis-scan) hay không, và có cột phân biệt kiểm kho vật lý vs kiểm kho hệ thống.
- ⚠️ Định dạng file export của cả 2 (cột trùng khớp để map upsert).
