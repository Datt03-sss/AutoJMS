# AutoJMS Vibe Coding Guide

This project supports vibe coding, but with guardrails.

## Why guardrails are needed

AutoJMS has:
- WinForms Designer risk
- WebView2 automation timing risk
- JMS authToken risk
- Firebase/Supabase/GitHub/Render integration
- Inno/Velopack release complexity
- Tier BASE/ULTRA behavior
- FullStackOperation lifecycle issues

## How to use AI safely

Use small tasks.
Never ask for broad rewrites.
Always provide:
- problem
- expected behavior
- forbidden files
- acceptance criteria

## Good prompt format

```txt
Đọc AGENTS.md, .agent/INDEX.md, .agent/context, .agent/rules trước.
Làm theo .agent/tasks/<task>.md.
Không sửa logic cũ nếu chưa được yêu cầu.
Chỉ sửa các file cần thiết.
Báo cáo files changed, tests, rollback.
```

## Bad prompt examples

- "Refactor toàn bộ MainForm"
- "Tối ưu hết app"
- "Sửa mọi lỗi"
- "Làm lại architecture"

