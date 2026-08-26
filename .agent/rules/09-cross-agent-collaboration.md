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
