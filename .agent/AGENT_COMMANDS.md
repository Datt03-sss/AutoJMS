# Agent Commands

## Start every task

```txt
Đọc AGENTS.md, .agent/INDEX.md, .agent/context, .agent/rules và docs/audit/CODEBASE_AUDIT.md trước. Sau đó đọc .agent/task-board/NOW.md. Không sửa logic cũ nếu chưa được yêu cầu.
```

## Fix build blockers

```txt
Đọc context/rules trước. Làm theo .agent/tasks/01-fix-build-blockers.md. Không sửa runtime logic. Không refactor. Mục tiêu là build pass.
```

## Fix JMS authToken 401

```txt
Đọc context/rules trước. Làm theo .agent/tasks/02-stabilize-auth-token.md. Không nhầm license JWT với JMS authToken. Không clear token trên first 401. Không truy cập WebView2 ngoài UI thread.
```

## Disable BASE background jobs

```txt
Đọc context/rules trước. Làm theo .agent/tasks/03-disable-base-background-jobs.md. BASE manual TRACKING/PRINT vẫn hoạt động. BASE không auto inventory/database sync.
```

## Stabilize release pipeline

```txt
Đọc context/rules trước. Làm theo .agent/tasks/04-stabilize-release-pipeline.md. GitHub Releases chứa binary lớn. Supabase chỉ manifest/config. Không mở GitHub page.
```

## FullStack lifecycle fix

```txt
Đọc context/rules trước. Làm theo .agent/tasks/05-fullstack-operation-lifecycle.md. FullStack là form riêng. UI ready trước, fetch sau. Không update grid nếu disposed/null.
```

