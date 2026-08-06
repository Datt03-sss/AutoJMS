# Báo cáo nâng cấp tabDKCH — 2026-07-29

## 1. Summary

Bảy việc chính:

1. **Đổi luật chặn** — trước đây chặn MỌI "Quét phát hàng" chưa có "Quét kiện vấn đề" (kể cả ca
   phát đầu tiên) nên rất nhiều đơn hợp lệ bị bỏ oan. Nay **chỉ chặn đơn đã có "Giao lại hàng"**:
   sau đó có "Quét phát hàng" mà chưa có "Quét kiện vấn đề" phía sau → không bấm Lưu. Còn lại →
   đăng ký chuyển hoàn, để JMS tự quyết số ca phát.
2. **Đọc kết quả theo `succ`** — `succ=true` là chuẩn ưu tiên để kết luận thành công;
   `succ=false`/`fail=true` là thất bại xác định, và là **trạng thái DUY NHẤT** được phép lặp
   (đổi mode DKCH1↔DKCH2 đúng 1 lần). Timeout / lỗi UI / lỗi lạ đều không bấm Lưu lại.
3. **Thêm ô `tabDKCH_result`** nằm giữa `tabDKCH_inputNewBill` và `tabDKCH_nowTracking`, 4 dòng:
   thao tác cuối cùng / thông điệp kết quả / đề xuất bước tiếp (theo chế độ) / số lần ĐKCH + phát lại.
   Toàn bộ nội dung cấu hình trong `modules/tab2config.json` — thêm case mới không cần build lại app.
4. **Ô lịch sử hành trình chỉ còn lịch sử** — bỏ phán quyết "HỢP LỆ/CHẶN" (analyzer không biết
   ngưỡng ca phát riêng của từng đơn nên nhãn đó gây hiểu nhầm) và bỏ 4 dòng header trùng lặp với
   ô kết quả.
5. **Bỏ mọi bước hiển thị trung gian** — không còn "đang chờ xử lý", "đổi mode", hay bản xem trước
   thao tác cuối cùng. Ô kết quả chỉ đổi ở mốc cuối cùng của mỗi mã, nên luôn khớp thao tác thật.
6. **Ưu tiên tốc độ + không tự bỏ qua mã nào** (vòng 2) — chỉ 1 lần tracking/mã, bỏ chờ 1200 ms sau
   Lưu, mã nhập lại vẫn được đăng ký tiếp. Xem mục 5c.
7. **Chế độ Newbie / Normal** ở mục DATA — Newbie hiện thêm dòng đề xuất khi nhập lẻ 1 mã.

## 2. Files Changed

| File | Nội dung |
|---|---|
| `src/AutoJMS/Automation/DkchJourneyAnalyzer.cs` | Luật chặn mới + `SelfDispatchViolation`; thêm `DeliveryAttemptCount`, `RedeliverCount`, `ProblemScanTime/Reason`, `LastEventNetwork`, `LastEventNote`, `ShortHeadline`, `LastActionLine`; `ParseTime` dùng `InvariantCulture` |
| `src/AutoJMS/Automation/WebViewAutomation.cs` | `DkchSaveOutcome.Failed` + `DkchSaveResult`; `DkchResultLevel` + `DkchResultInfo`; `StripBom`; `LooksLikeActionEnvelope`; `ExtractMessage`/`ExtractCode`/`ExtractRawMessage`/`ExtractErrorCode`/`ExtractRatio`; `IsKnownBusinessRejection` + `TranslateJmsMessage`; `ClassifySaveFailure`; lọc response theo URI; `AddPriorityWaybill` trả `bool`; `MarkProcessed`; `PublishFromCatalog`/`TryPublishPreSave`; `PublishJourney` bỏ header; tracking lại 1 lần sau Lưu |
| `src/AutoJMS/Forms/Main.Designer.cs` | Thêm `tabDKCH_result` (UIRichTextBox) làm hàng 1 của `uiTableLayoutPanel33`; `RowCount` 2→3; `tabDKCH_nowTracking` xuống hàng 2 |
| `src/AutoJMS/Forms/Main.cs` | Nối `OnResultChanged`; `FormatDkchResult` (3 dòng), `DkchResultAccent`, `ShowDkchResultPlaceholder` |
| `src/AutoJMS/UI/AppTheme.cs` | Thêm `tabDKCH_result` vào danh sách UIRichTextBox được theme |
| `tests/AutoJMS.Tests/DkchJourneyAnalyzerTests.cs` | Cập nhật kỳ vọng theo luật mới + 7 test mới |
| `tests/AutoJMS.Tests/DkchSaveResponseTests.cs` | **Mới** — parse `succ`/`fail`/`msg`/`code`/`ratio`/lọc response |
| `src/AutoJMS/modules/tab2config.json` | **Mới** — 17 case: `{case}` → `{result}` + `{actRecommend}` + `statsLine` |
| `src/AutoJMS/Automation/Tab2Config.cs` | **Mới** — DTO + loader (cache, có `Reload()`) + bộ chọn case và thay placeholder |
| `src/AutoJMS/AutoJMS.csproj` | Thêm 1 dòng `<Content Include="modules\tab2config.json" …>` |
| `tests/AutoJMS.Tests/Tab2ConfigTests.cs` | **Mới** — test thứ tự case, `enabled=false`, `problemScanAfter`, placeholder, `statsLine`, fallback |

## 3. Luật chặn — chi tiết

CHẶN khi và chỉ khi cả 3 điều kiện đúng:

- (a) có `重派` / "Giao lại hàng" trong lịch sử, VÀ
- (b) có `出仓扫描` / "Quét phát hàng" **sau** nó, VÀ
- (c) **sau** lần quét phát đó chưa có `问题件扫描` / "Quét kiện vấn đề".

Khi chặn: **không bấm Lưu**, và thông báo có **2 biến thể** tuỳ chu kỳ phát lại đã từng "Quét kiện
vấn đề" hay chưa (`DkchJourneyDecision.SelfDispatchViolation`):

| Chu kỳ phát lại | Thông báo |
|---|---|
| Chưa kiện lần nào | Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn. |
| Đã kiện rồi vẫn "Quét phát hàng" thêm ca | Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn. |

Cài đặt dùng `redeliverBeforeDispatch` (lần "Giao lại hàng" gần nhất **đứng trước** lần "Quét phát
hàng" cuối cùng) thay vì `lastRedeliver`, nhờ đó chặn đúng cả khi có nhiều ca phát liên tiếp.

Ví dụ:

| Lịch sử | Kết quả |
|---|---|
| Xuống kiện → Quét phát hàng | ✅ Đăng ký (ca phát đầu, chưa từng giao lại) |
| Quét phát hàng → Quét kiện vấn đề | ✅ Đăng ký |
| Giao lại hàng → Quét phát hàng | ⛔ Chặn (chưa kiện lần nào) |
| Giao lại hàng → Quét phát hàng → Giao lại hàng | ⛔ Chặn (chưa kiện lần nào) |
| Giao lại hàng → Quét phát hàng → Quét kiện vấn đề → **Quét phát hàng** | ⛔ Chặn — **vi phạm tự ý chuyển hoàn** |
| Giao lại hàng → Quét phát hàng → Quét kiện vấn đề | ✅ Đăng ký |
| Quét phát hàng → Giao lại hàng | ✅ Đăng ký |
| … → Quét kiện vấn đề → Đăng ký chuyển hoàn | ✅ Vẫn thử đăng ký — JMS từ chối → app đổi sang lần 2 |

### KHÔNG chặn theo số ca phát

Mỗi đơn có ngưỡng ca phát (出仓次数) riêng và **chỉ JMS biết** ngưỡng đó. Vì vậy đơn đã đủ điều kiện
theo luật trên vẫn được bấm Lưu **đúng 1 lần**; nếu chưa đủ ca phát, server trả:

```
999010051:此单的出仓次数不满足登记条件，出仓次数：1/2
```

JMS dùng **hai** loại điều kiện, app tách thành 2 câu khác nhau:

| Chuỗi JMS | Nghĩa | Hiển thị |
|---|---|---|
| `问题件次数：1/3` | đơn mới, thiếu ca kiện vấn đề | Đơn mới chưa đủ ca (1/3). |
| `出仓次数：1/2` | đơn đã giao lại, thiếu ca phát | Đơn phát lại chưa đủ ca (1/2). |

Cả hai đều là `DkchSaveOutcome.Failed` (thất bại xác định): **không đổi mode, không Lưu lại**.
Ô kết quả hiện 1 dòng lỗi + 1 dòng "→ Phát lại ngày hôm sau."; mã lỗi và nguyên văn tiếng Trung
chỉ vào log.

Chốt an toàn quan trọng: `IsKnownBusinessRejection` được kiểm **trước** mọi heuristic đổi mode, và
`ExtractErrorCode` bóc phần số của mã kể cả khi JMS nhồi cả chuỗi `"999010051:此单…"` vào trường
`code`. Nếu không có hai chốt này, heuristic cũ (`code` chứa dấu `:` → cần DKCH2) sẽ hiểu sai lỗi
"chưa đủ ca phát" thành "sai mode" và phát sinh thêm một lần bấm Lưu.
`DeliveryAttemptCount` của analyzer chỉ dùng để hiển thị/ghi log, không dùng để chặn.

## 4. Chống lặp / chống spam

| Lớp | Cơ chế |
|---|---|
| Nghiệp vụ | Luật chặn (2 trạng thái `IsBlocked`) — quyết định TRƯỚC khi chạm form |
| Response | Chỉ lặp khi `succ=false`/`fail=true` **và** JMS yêu cầu mode KHÁC mode đang chạy; đổi mode tối đa 1 lần |
| Lọc response | `LooksLikeActionEnvelope` chỉ nhận phong bì <4 KB có `succ`/`msg`, loại `podTracking`/`keywordList` — không nhặt nhầm response request khác |
| Google Sheet | `_processedInSession` — chỉ áp dụng cho nguồn sheet (đọc lại mỗi 15 s) |
| Hàng đợi | `AddPriorityWaybill` từ chối mã đang CHỜ trong hàng đợi (không chặn mã đã Lưu xong) |
| UI | `_dkchManualInputGate` chặn Enter đồng thời |

**Mã người dùng nhập tay KHÔNG bị chặn bởi lớp nào ngoài luật nghiệp vụ** — nhập lại là làm lại.

Timeout, banner lỗi UI, exception lạ: **không bao giờ** bấm Lưu lại.

## 5. Ô kết quả `tabDKCH_result`

Vị trí: hàng 1 của `uiTableLayoutPanel33` (Absolute 84px), giữa `splitContainer1` và
`tabDKCH_nowTracking`. Hàng 0 và 2 dùng Percent (52/48) để cột trái co giãn được ở MinimumSize.

Layout cuối cùng — xem mục **5c** cho bảng đầy đủ 4 dòng.

```
dòng 1:  {thao tác cuối cùng}            ← app tự lấy, không cấu hình
dòng 2:  {result}                        ← in đậm, tô màu theo mức độ
dòng 3:  {actionPrefix}{actRecommend}    ← chỉ khi Newbie + nhập lẻ 1 mã
dòng 4:  {statsLine}                     ← số lần ĐKCH + số lần phát lại, CÙNG 1 dòng, màu xám
```

`statsLine` chỉ vẽ khi đã đọc được lịch sử hành trình (`HasJourney`), tránh hiện "0 · 0" gây hiểu nhầm.

### Chỉ cập nhật ở mốc CUỐI CÙNG — không có bước trung gian

Đã bỏ hẳn các publish trung gian từng làm ô kết quả trễ hơn thao tác đang chạy:

| Bỏ | Lý do |
|---|---|
| "⏳ Đang chờ xử lý" (`ReportQueued` + case `pending`) | không phải kết quả, chỉ là tiếng ồn |
| "↻ Đổi sang Chuyển hoàn lần 2" (case `need-dkch1/2`) | bước trung gian, kết quả lượt 2 publish ngay sau đó |
| Bản xem trước "{thao tác cuối cùng}" của mọi đơn hợp lệ | bị kết quả Lưu ghi đè ngay → nguồn gốc cảm giác "chậm hơn" |

Ô kết quả nay chỉ đổi tại 2 mốc: (1) đơn **không** được đăng ký (chặn/bỏ qua/không có dữ liệu) —
đó chính là kết quả cuối; (2) **sau** khi bấm Lưu. Muốn theo dõi từng bước thì xem log.

Riêng trạng thái trước-Lưu là **opt-in theo config**: chỉ vẽ khi có case `beforeSave` +
`outcomes: ["readyToRegister"]` đang bật. Mặc định `signed-cpn` và `dispatch-pending-problem-scan`
còn bật (theo yêu cầu case 4/5), `before-save-default` đã **tắt**. Không case nào khớp → không vẽ gì.

| Tình huống | Dòng 1 | Dòng 2 | Màu |
|---|---|---|---|
| Chưa Lưu, thao tác cuối = Ký nhận CPN | Đã ký nhận. | Thực hiện in đơn hoàn 1 phần (nếu có thể). | xanh dương |
| Chưa Lưu, thao tác cuối = Quét phát hàng | Chưa kiện vấn đề. | — | xanh dương |
| Chưa Lưu, hợp lệ khác | *(không vẽ gì — chờ kết quả Lưu)* | — | — |
| Chưa Lưu, chặn — chưa kiện lần nào | Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn. | — | đỏ |
| Chưa Lưu, chặn — đã kiện rồi tự ý phát thêm ca | Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn. | — | đỏ |
| Chưa Lưu, đã ĐKCH (mốc ĐKCH mới nhất) | {thao tác cuối cùng} | — | **xanh lá** |
| Sau Lưu OK, lần đầu | {thao tác mới nhất} | Kiểm tồn kho. | xanh lá |
| Sau Lưu OK, lần 2 | {thao tác mới nhất} | Kiểm tra trạng thái duyệt chuyển hoàn. | xanh lá |
| JMS: 问题件次数 1/3 | Đơn mới chưa đủ ca (1/3). | Phát lại ngày hôm sau. | đỏ |
| JMS: 出仓次数 1/2 | Đơn phát lại chưa đủ ca (1/2). | Phát lại ngày hôm sau. | đỏ |
| Không đọc được phản hồi | Không xác minh được kết quả Lưu. | Kiểm tra tay trên JMS trước khi thử lại. | cam |
| Lỗi JMS chưa map | {msg nguyên văn} | — | đỏ |

Chi tiết đầy đủ (mã lỗi, nguyên văn tiếng Trung, mốc thời gian) chỉ vào log, không vào ô này.

## 5c. Vòng 2 — tốc độ + chế độ Newbie/Normal (cập nhật)

### Luồng đăng ký mới

```
nhập mã → tracking hành trình (ĐÚNG 1 lần) → nhận diện case → bấm Lưu (1 lần) → trả kết quả
```

| | Trước | Sau |
|---|---|---|
| Gọi API hành trình / mã | 2–3 lần | **1 lần** |
| `Task.Delay` chờ sau Lưu | 1200 ms | **0** |
| Delay trong `PrepareFormAsync` | 100 + 100 ms | **40 + 40 ms** |
| Nhịp nhặt mã khỏi hàng đợi | 500 ms | **200 ms** |
| Bấm Lưu / mã | tối đa 2 | tối đa 2 (chỉ khi JMS báo SAI mode và mode đó thật sự khác mode đang chạy) |
| Recheck hành trình trước lượt 2 | có | **bỏ** |

Tiết kiệm ước tính **~1.5–2 giây mỗi mã**, chủ yếu từ việc bỏ lần tracking thứ hai và nhịp chờ 1200 ms.

### Không còn tự động bỏ qua

`DkchJourneyDecision.IsBlocked` là chốt DUY NHẤT khiến app không bấm Lưu — chỉ 2 trạng thái:
`BlockedPendingProblemScan` (luật quét kiện) và `BlockedNoData` (không đọc được hành trình, không
được đăng ký mù).

Đã bỏ:

- `_savedInSession` chặn mã đã Lưu thành công trong phiên → **nhập lại là làm lại**
- `SkipAlreadyRegistered` chặn đơn đã có mốc ĐKCH → nay vẫn thử Lưu, JMS tự từ chối rồi app đổi
  sang "Chuyển hoàn lần 2" (chính xác hơn app tự đoán)

**Vẫn giữ** chống trùng cho nguồn Google Sheet (`_processedInSession`): sheet được đọc lại mỗi 15 s,
không chặn thì cùng một dòng sẽ đăng ký lặp vô hạn. Mã nhập tay không đi qua nhánh này.

### Ô kết quả 4 dòng

```
1  {thao tác cuối cùng}                       ← từ lần tracking duy nhất, TRƯỚC lúc Lưu
2  {msg}                                      ← in đậm, tô màu theo mức độ
3  → {recommend}                              ← CHỈ khi Newbie + nhập lẻ 1 mã
4  Số lần đã ĐKCH: N   ·   Số lần phát lại: M ← màu xám
```

Vì không còn tracking lại sau khi Lưu, dòng 1 là trạng thái **trước** thao tác đăng ký — đúng theo
yêu cầu ưu tiên tốc độ.

### Chế độ Newbie / Normal

Dropdown `tabDKCH_guideMode` ở mục **DATA** (hàng 5 của `uiTableLayoutPanel8`).

| Chế độ | Nhập lẻ 1 mã | Nhập danh sách > 1 mã |
|---|---|---|
| Normal | không có dòng 3 | không có dòng 3 |
| Newbie | **có dòng 3** | không có dòng 3 |

Cờ được **chốt theo từng mã ngay lúc xếp hàng** (`_showRecommendFor`), không đọc lúc publish — nếu
không, một mã lẻ đang xử lý sẽ bị đổi kết quả khi người dùng vừa dán thêm danh sách, và mã lấy từ
Google Sheet sẽ thừa hưởng oan.

Chưa lưu lựa chọn vào `AutoJMS.json` (mặc định Normal mỗi lần mở app) — `CloneSettingsSnapshot`
trong `Main.cs` chỉ copy một tập field cố định nên thêm setting mới dễ bị ghi đè; để sau nếu cần.

### Case bị TẮT vì không còn được kích hoạt

`signed-cpn`, `dispatch-pending-problem-scan`, `before-save-default`, `saved-in-session`,
`already-registered` — tất cả đều là `phase: beforeSave` cho đơn KHÔNG bị chặn, mà app đã bỏ bước vẽ
trước khi Lưu. Thông tin của chúng ("Ký nhận CPN", "Quét phát hàng") vẫn thấy ở **dòng 1**.
Giữ trong file để tham chiếu cấu hình, `enabled: false`.

## 5d. Song ngữ Việt / Trung (cập nhật)

Trang JMS chạy được ở **cả hai ngôn ngữ** và đã từng đổi cả tên nhãn (DKCH1: "Chuyển hoàn" →
"Từ chối" / 退回). Mọi chuỗi phụ thuộc ngôn ngữ đã chuyển hết ra `modules/tab2config.json`.

| Khoá config | Việt | Trung |
|---|---|---|
| `dropdownOptions.DKCH1` | Từ chối · Chuyển hoàn | 退回 |
| `dropdownOptions.DKCH2` | Chuyển hoàn lần 2 | 二次退件 |
| `saveButtonTitles` | Lưu và thêm mới | 保存并新增 |
| `collapseHeaders` | Thông tin người gửi · hóa đơn gốc · đơn hàng mới | 原单收寄件人信息 · 新单收寄件人信息 |
| `jmsMessages.noData` | không có dữ liệu · Vận đơn không tồn tại | 没有数据 · 无数据 · 运单不存在 · 单号不存在 |

Nguyên tắc chung: mỗi khoá là một **danh sách**, app thử lần lượt và dùng cái nào đang thật sự có
trên trang; cấu hình được **gộp** với mặc định trong code nên thêm chuỗi mới không làm mất chuỗi cũ.
Không khớp được thì log/exception in ra **danh sách những gì JMS đang có** để biết chuỗi mới mà điền —
không cần build lại app.

### Ba lỗi chặn được phát hiện khi soát lại

1. **Không start được ở tiếng Trung** — `Main.cs` probe yêu cầu BẮT BUỘC `hasDkchText` chứa chuỗi
   tiếng Việt, nên ở tiếng Trung probe mãi trả `Loading` → `EnsureDkchPageReadyAsync` timeout →
   `DkchManager` không bao giờ được gọi. Nay `hasDkchText` chỉ là dấu hiệu PHỤ (route + 3 control
   của form là đủ) và danh sách marker có cả hai ngôn ngữ.
2. **Đổi mode sai hướng** — tôi từng thêm `已登记`/`二次退件` vào `needDkch1` mà **không có capture
   thật** của thông điệp tiếng Trung. Thông điệp "đã đăng ký, hãy dùng 二次退件" sẽ khớp `needDkch1`
   → đổi sang DKCH1 trong khi đang chạy DKCH1 → dừng. Đã **bỏ hết chuỗi đoán**, và đổi thứ tự:
   **mã lỗi số xét TRƯỚC** (999006328 → DKCH1; 137043004/999006082 → DKCH2) vì mã không phụ thuộc
   ngôn ngữ; từ khoá văn bản chỉ là lớp phụ.
3. **Test đỏ** — `Tab2ConfigTests` kỳ vọng text cũ của `Tab2Config.Default()`.

### Sửa thêm

| Vấn đề | Sửa |
|---|---|
| `.Trim('"')` không giải mã `\uXXXX` → chữ Trung trong thông báo lỗi thành rác | `UnwrapJs` dùng `JsonSerializer.Deserialize<string>`, `Trim` chỉ là fallback |
| Script async trả về `"{}"` (WebView2 không await Promise) làm chết mọi mã | nhánh `default` chỉ ghi log rồi đi tiếp, không ném lỗi |
| `dropdownOptions` **thay thế** mặc định trong khi doc nói **gộp** | dùng chung `Merge` như 3 khoá còn lại |
| STJ thay instance Dictionary → mất `OrdinalIgnoreCase`, người dùng viết `dkch1` bị bỏ qua âm thầm | `Normalize()` dựng lại dictionary sau khi deserialize |
| `IsKnownBusinessRejection` chỉ nhận mã 999010051 | nhận cả 999010052 (问题件次数) |
| Overload `CheckAndSelectDropdownAsync(string)` thành code chết | xoá |

### Còn hardcode (có lý do)

`DkchJourneyAnalyzer.Classify` giữ nhãn hành trình trong code nhưng **đã song ngữ sẵn**
(`重派`/`Giao lại hàng`, `出仓扫描`/`Quét phát hàng`, `问题件扫描`/`Quét kiện vấn đề`…) nên bước đọc
lịch sử chạy đúng ở cả hai ngôn ngữ. `出仓次数`/`问题件次数` trong `IsKnownBusinessRejection` cũng
để trong code vì đó là **mã nghiệp vụ trong response**, không phải nhãn giao diện.

### Cần smoke test

1. Đổi JMS sang tiếng Trung → mở tab CHUYỂN HOÀN → bấm DKCH1: phải start được (trước đây treo ở
   "Không mở được form DKCH").
2. Ở tiếng Trung, nhập 1 mã hợp lệ: dropdown phải chọn 退回, nút 保存并新增 phải được bấm.
3. Ở tiếng Trung, gặp đơn bị JMS từ chối: đọc dòng log `❌ JMS từ chối — … | <nguyên văn>` rồi thêm
   nguyên văn vào `jmsMessages.needDkch1` hoặc `needDkch2` cho đúng hướng đổi mode.
4. Nếu log có dòng `dropdown trả về không nhận diện được: {}` → WebView2 không await Promise của
   script async; cần đổi script dropdown sang dạng đồng bộ.

## 5b. Ô lịch sử `tabDKCH_nowTracking`

Đã bỏ toàn bộ 4 dòng header cũ (quét kiện vấn đề / nguyên nhân / số lần ĐKCH / số lần phát lại +
đường kẻ) vì **trùng hoàn toàn** với ô kết quả ngay phía trên. Hai con số đã chuyển lên ô kết quả,
nằm gọn trên 1 dòng. Ô này nay thuần lịch sử hành trình.

### `modules/tab2config.json`

File cấu hình mới, **sửa không cần build lại app**. App đọc theo thứ tự:

1. `{InstallRoot}\AppData\modules\tab2config.json` — bản người dùng sửa được
2. `{InstallDir}\modules\tab2config.json` — bản ship kèm app

Fallback (2) là **bắt buộc**: `AppPaths.MigrateBundledDataIfNeeded()` chỉ copy thư mục `modules`
khi `modules-cache.json` chưa tồn tại, nên máy đã chạy app một lần sẽ không bao giờ được copy file
mới xuống `AppData`.

Cấu trúc: mảng `cases`, duyệt **từ trên xuống**, case đầu tiên có `enabled: true` và khớp toàn bộ
`match` sẽ thắng; không case nào khớp thì dùng `fallback`. Vì vậy case càng cụ thể phải đặt càng lên
trên. Khoá `match` (mọi khoá tuỳ chọn):

| Khoá | Ý nghĩa |
|---|---|
| `phase` | `beforeSave` / `afterSave` / `any` |
| `outcomes` | `readyToRegister`, `blocked`, `blockedViolation`, `skipped`, `skippedInSession`, `success`, `failed`, `unverified`, `noData`, `modeSwitchFailed`, `error` |
| `modes` | `DKCH1` / `DKCH2` |
| `jmsCodes` | mảng mã lỗi số, vd `["999010051"]` |
| `msgContains` | mảng chuỗi con của msg JMS (kể cả tiếng Trung) |
| `registered` / `redelivered` | `yes` / `no` / `any` |
| `lastActionContains` | mảng chuỗi con của thao tác gần nhất |
| `problemScanAfter` | `"HH:mm"` — chỉ khớp khi "Quét kiện vấn đề" muộn hơn giờ này |

Placeholder trong `result`/`actRecommend`/`statsLine`: `{waybill} {ratio} {message} {rawMessage} {code} {mode}
{lastAction} {lastActionType} {lastActionTime} {registerCount} {redeliverCount} {dispatchCount}
{problemScanTime} {problemScanReason} {errorMessage}`.

**Hai case đang `enabled: false`, cần bạn điền rồi bật:**

| Case | Cần điền |
|---|---|
| `complaint` (Đơn dính khiếu nại) | `jmsCodes` và/hoặc `msgContains` — lấy nguyên văn từ dòng log `❌ JMS từ chối — … \| <nguyên văn>` khi gặp đơn khiếu nại thật |
| `late-problem-scan` (Đơn kiện muộn sau giờ hành chính) | `problemScanAfter` (đang tạm `17:30`) và 2 chỗ trống trong `actRecommend`: `{không liên lạc/hẹn}` và `( ... giờ)` |

## 6. Build/Verify Result

**CHƯA BUILD** — môi trường sandbox không có .NET SDK (`dotnet: command not found`), không build
được WinForms/Windows. Đã verify bằng cách khác:

- Cân bằng `{}`/`()`/`[]` (bỏ qua comment, string, verbatim, char literal): **OK** cả 7 file.
- Rà toàn bộ call site của các API đã đổi signature (`ClickSaveAndVerifyAsync`,
  `WaitForApiResponseAsync`, `AddPriorityWaybill`): đã cập nhật hết.
- Hand-simulate toàn bộ 29 test của 2 file test theo cài đặt mới: khớp kỳ vọng.
- Đối chiếu mọi property Sunny.UI dùng cho `tabDKCH_result` với tiền lệ có sẵn trong repo
  (`ReadOnly` → `Forms/TermsDialog.cs:88`; `RectColor` → `UI/AppTheme.cs:430`; còn lại →
  `tabDKCH_nowTracking`).

Chủ sở hữu cần chạy:

```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
dotnet test .\tests\AutoJMS.Tests\AutoJMS.Tests.csproj
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

## 7. Commit / Push

**Chưa commit, chưa push.** CLAUDE.md: không push khi chưa build pass. Working tree vốn đã dirty
sẵn từ trước (FullStack, SettingsManager, csproj…) nên **không dùng `git add .`** — chỉ add đúng
7 file ở mục 2.

## 8. Behavior Changed

- Đơn ở ca phát đầu tiên (chưa từng "Giao lại hàng") nay **được** đăng ký thay vì bị chặn.
- Thành công/thất bại kết luận theo `succ`; response thiếu `succ` mới dùng chuỗi "Thao tác thành công" + `code=1`.
- Lỗi nghiệp vụ ngoài nhóm đổi mode nay hiện **nguyên văn** `msg` của JMS thay vì "Lỗi thao tác" chung chung.
- Sau mỗi lần Lưu luôn tracking thêm đúng 1 lần → `tabDKCH_nowTracking` và ô kết quả luôn phản ánh trạng thái mới nhất.
- Có ô kết quả mới; `tabDKCH_nowTracking` thấp hơn 88px so với trước.
- `AddPriorityWaybill` trả `bool`.
- 6 helper parse response chuyển từ `internal` sang `public` để test được (không có `InternalsVisibleTo` trong repo).

## 9. Behavior Intentionally Unchanged

- Luật "1 lần/chu kỳ" (chủ sở hữu chọn giữ).
- Bản đồ mã lỗi → đổi mode DKCH1/DKCH2 (`999006328`, `137043004`, `999006082`, `code` chứa `:`).
- Mọi selector DOM và endpoint JMS (theo `webview2-devtools-inspector-skill`: không đoán selector).
- `CheckAndThrowIfError` giữ nguyên cho bước Tìm kiếm; bước Lưu dùng `ClassifySaveFailure` riêng.
- Luồng TRACKING / PRINT / HOME / ABOUT, `Program.cs`, licensing, Velopack, Supabase: không chạm.
- `BuildDkchHistoryText`, `CleanType`, `FormatNowTracking`.

## 10. Owner Manual Test Checklist

1. Tab CHUYỂN HOÀN hiển thị đúng 3 khối: nhập mã / **ô kết quả mới** / lịch sử hành trình.
2. Đổi theme Light ↔ Dark ↔ Red — ô kết quả có viền và nền theo theme, chữ đọc được.
3. Thu nhỏ cửa sổ về `MinimumSize` (1024×700) và thử DPI 125% — ô lịch sử hành trình **không** bị bóp về 0.
4. Chưa bật DKCH1/DKCH2 mà Enter → cảnh báo cũ, ô kết quả không đổi.
5. Nhập 1 mã ở ca phát đầu (Xuống kiện → Quét phát hàng, chưa Quét kiện vấn đề) → phải **Đăng ký**.
6. Nhập 1 mã đã "Giao lại hàng" → "Quét phát hàng", chưa "Quét kiện vấn đề" → phải **Chặn**, ô đỏ "Chưa quét kiện vấn đề, chặn đăng ký chuyển hoàn", **không** có request Lưu nào.
6b. Nhập 1 mã `Giao lại hàng → Quét phát → Quét kiện vấn đề → Quét phát hàng` → **Chặn**, ô đỏ "Chưa quét kiện vấn đề, vi phạm tự ý chuyển hoàn, chặn đăng ký chuyển hoàn", **tuyệt đối không** có request Lưu.
6c. Nhập 1 mã `Giao lại hàng → Quét phát → Quét kiện vấn đề` mà JMS trả `999010051 … 出仓次数：1/2` → ô đỏ "Đơn phát lại chưa đủ ca (1/2)", log chỉ có **1** lần bấm Lưu, **không** thấy dòng "đổi mode, thử lại".
6d. Nhập 1 mã ca phát đầu (`Xuống kiện → Quét phát hàng`, chưa kiện) → phải **bấm Lưu 1 lần**; nếu JMS trả `问题件次数：1/3` thì ô đỏ "Đơn mới chưa đủ ca (1/3)" + "→ Phát lại ngày hôm sau."
7. Nhập 1 mã có mốc "Đăng ký chuyển hoàn" mới nhất → **Bỏ qua**, ô **xanh lá** hiện thao tác cuối cùng.
8. Đăng ký 1 mã hợp lệ → ô xanh lá, `OK: n` tăng 1, ô lịch sử có mốc "Đăng ký chuyển hoàn" mới.
9. Nhập lại đúng mã vừa thành công → bị từ chối ngay, **không** hiện "Đang chờ xử lý".
10. Đơn sai mode → log ghi "đổi mode, thử lại 1 lần" và chỉ Lưu thêm **đúng 1 lần**.
11. Chạy nguồn Google Sheet với ≥10 mã → đối chiếu log, mỗi mã tối đa 1 lần Lưu.
12. Tab ABOUT vẫn là tab cuối.

## 11. Risks

| Mức | Rủi ro |
|---|---|
| Trung bình | Không build được ở sandbox → còn khả năng lỗi biên dịch mà rà tay bỏ sót. Bắt buộc build Release trước khi push. |
| Trung bình | `LooksLikeActionEnvelope` lọc theo *loại trừ* (`podTracking`/`keywordList`) + kích thước <4 KB, chưa lọc theo đúng endpoint Lưu. Cửa sổ chờ 8s: một response nhỏ khác có `succ` bay qua vẫn có thể bị đọc thành kết quả Lưu. Muốn siết cần bundle capture thật (`Ctrl+Shift+F12`) để lấy URL API Lưu rồi thêm allowlist. |
| Thấp | Nới luật chặn → số lượt đăng ký tăng; nếu JMS có rate-limit riêng thì cần theo dõi log phiên đầu. |
| Thấp | Đơn "chưa đủ ca phát" vẫn tốn 1 request Lưu mỗi lần thử (theo yêu cầu: không đoán ngưỡng ca phát). Nếu muốn tiết kiệm, có thể ghi ngưỡng học được từ lỗi `出仓次数：x/y` rồi tự chặn từ lần sau. |
| Thấp | Bảng dịch lỗi JMS hiện chỉ có `999010051`. Mã lỗi mới sẽ hiện nguyên văn tiếng Trung (không mất thông tin, nhưng khó đọc) — bổ sung vào `TranslateJmsMessage` khi gặp. |
| Thấp | Layout: 200px + 88px cố định trong `uiTitlePanel2`; `UiLayoutHelper.ConfigureDkchTab` ép `tabHome_pnlLeft.Width = 350` lúc runtime nên phải xem mắt (mục 3 checklist). |
| Thấp | `FormatDkchResult` set `RectColor` theo mức độ, sẽ bị `AppTheme.Apply` ghi lại khi đổi theme (viền accent mất tới lần cập nhật kế tiếp). |
| Thấp | `DeliveryAttemptCount` đếm "Quét phát hàng" sau lần đăng ký gần nhất — nếu nghiệp vụ định nghĩa "ca phát 2/3" theo mốc khác thì con số trong nhãn cần chỉnh. |
