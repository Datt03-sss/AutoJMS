# Cross-Agent Collaboration Rules

## Overview

Antigravity và Claude Code hợp tác qua mô hình **Prompt Relay**:
- Antigravity tạo prompt đề xuất → Owner review/approve → Owner copy-paste cho Claude Code.
- Antigravity KHÔNG trực tiếp gửi lệnh cho Claude. Owner là relay trung gian.

## Claude Prompt Proposal Format

Khi Antigravity cần Claude viết/sửa code, tạo prompt theo format:

```
## 📋 Claude Prompt Proposal

### Context
[Mô tả bối cảnh: tại sao cần thay đổi, bug gì, feature gì]

### Current State
[Trạng thái hiện tại: file nào liên quan, logic hiện tại ra sao]
[Đính kèm code snippets quan trọng nếu cần]

### Required Changes
[Mô tả chi tiết từng thay đổi cần làm]
1. File: `path/to/file.cs`
   - Thêm/sửa gì, ở đâu, tại sao
2. File: `path/to/another-file.cs`
   - ...

### Technical Constraints
- [Ràng buộc kỹ thuật: không đụng file X, phải dùng pattern Y, ...]
- [Tham chiếu rule cụ thể nếu có]

### Verification Steps
1. `dotnet build .\AutoJMS.slnx -c Release` — phải pass
2. [Kiểm tra cụ thể: endpoint trả đúng response, migration chạy đúng, ...]

### After Push — VPS Steps (Antigravity sẽ thực hiện)
[Mô tả những gì Antigravity sẽ làm trên VPS sau khi code được push]
```

## Rules

1. **Prompt phải self-contained**: Claude phải có đủ context để làm mà không cần hỏi thêm.
2. **Prompt không override rules**: Mọi rule trong `AGENTS.md`/`CLAUDE.md` vẫn áp dụng.
3. **Protected Files phải được ghi rõ**: Nếu prompt yêu cầu sửa Protected Files, phải note rõ và Owner phải xác nhận trong chat với Claude.
4. **Verification trước VPS deploy**: Claude push xong, Antigravity mới pull và deploy trên VPS.
5. **Rollback path**: Prompt nên mô tả cách rollback nếu thay đổi gây lỗi.

## Báo cáo hiện trạng VPS — quy ước hai file

Antigravity là nguồn sự thật về hạ tầng VPS. Claude Code cần thông tin đó (migration nào đã áp,
image nào đang chạy, SQL đã đo trên PostgreSQL thật chưa) nhưng **repo `Datt03-sss/AutoJMS` là
PUBLIC**, nên báo cáo được tách làm hai:

| File | Trạng thái git | Nội dung |
|---|---|---|
| `backend/vps/VPS_STATUS_REPORT.private.md` | **ngoài git** (`.gitignore`: `*.private.md`) | Bản đầy đủ: hostname, IP công khai, tài khoản vận hành, `sudo NOPASSWD`, container ID, đường dẫn file secrets, danh sách cổng mở, ngưỡng fail2ban |
| `backend/vps/VPS_STATUS_REPORT.md` | **tracked, đã che** | Migration đã áp, số bảng/index, kết quả `EXPLAIN (ANALYZE, BUFFERS)`, smoke test, diễn tập restore, tag image, trạng thái hardening ở mức "khớp `bootstrap-vps.sh`" |

**Trách nhiệm Antigravity** — sau mỗi task VPS, cập nhật **cả hai** file: chi tiết vào bản
`.private.md`, rồi phản chiếu phần an toàn sang bản đã che. Không đưa giá trị định danh hạ tầng
vào bản tracked.

**Trách nhiệm Claude Code** — đọc bản đã che. Cần giá trị định danh cụ thể thì **hỏi Owner**;
không suy đoán và không copy giá trị từ bản private vào bất kỳ file tracked nào.

Lý do tách: từng mục riêng lẻ nghe vô hại, nhưng **tổ hợp** IP + tên tài khoản có `sudo NOPASSWD`
+ danh sách cổng mở + ngưỡng ban của fail2ban là bản đồ trinh sát hoàn chỉnh (bất kỳ RCE dưới
tài khoản đó = root, và ngưỡng ban cho biết cần rải chậm bao nhiêu để không bị chặn). Luật gốc:
[DEPLOY_EXECUTION_CHECKLIST.vi.md §6](../../backend/datahub/deploy/DEPLOY_EXECUTION_CHECKLIST.vi.md).

**Gate tự động** — `eng/harness/check-secrets.ps1` phần 4 quét file tracked theo một denylist
literal (IP, hostname) đọc từ `eng/harness/forbidden-values.local.txt` (ngoài git) hoặc biến môi
trường `AUTOJMS_FORBIDDEN_VALUES` trên CI. Trước khi có phần 4, một báo cáo tracked ghi đủ IP +
tài khoản + `sudo NOPASSWD` vẫn **pass im lặng** với dòng `Tracked files: OK`. Gate báo lỗi kèm
nhãn và `file:dòng`, **không in ra giá trị** — nếu in thì chính log CI lại rò rỉ thứ nó bảo vệ.
Denylist nằm ngoài git vì một danh sách định danh hạ tầng được commit sẽ công bố đúng những giá
trị nó tồn tại để chặn. Cách thêm entry và các giá trị cố ý **không** đưa vào: xem header của
`forbidden-values.local.txt`.

## Workflow Sequence

```
Antigravity phát hiện gap/bug trên VPS
    ↓
Antigravity tạo Claude Prompt Proposal (trong chat với Owner)
    ↓
Owner review prompt → approve / reject / modify
    ↓
Owner copy-paste prompt đã duyệt vào chat với Claude Code
    ↓
Claude Code thực thi: code → build → test → commit → push
    ↓
Claude Code output Final Report cho Owner
    ↓
Owner thông báo Antigravity: "code đã push"
    ↓
Antigravity SSH vào VPS: git pull → docker compose up → migrations → smoke test
    ↓
Antigravity output Deploy Report cho Owner
```
