# AutoJMS Development Roadmap

Roadmap này định hướng phát triển AutoJMS theo workflow an toàn cho vibe coding. Phase 1.5 chỉ tạo tài liệu, không sửa logic production code.

## Phase 0 — Audit & Context

Status: done/partial.

- Codebase audit đã có tại `docs/audit/CODEBASE_AUDIT.md`.
- `.agent/` structure đã có context, rules, prompts, skills, workflows, checklists.
- `docs/` structure đã có architecture, API, release, troubleshooting, manual.
- Manual operations doc đã có tại `docs/manual/MANUAL_OPERATIONS.md`.
- Một số nội dung cũ vẫn cần verify khi đụng task cụ thể: Supabase RPC schema, hash manifest shape, module signature enforcement.

## Phase 1 — Stabilize Build

Mục tiêu:

- Fix build fail do missing `modules/*.json`.
- Không sửa runtime logic.
- Không xóa `ModuleSystem`.
- Ưu tiên conditional `Content Include` hoặc tạo default JSON tối thiểu.
- Sau khi fix mới được chạy build.

Current recorded state:

- Build blocker `modules/*.json` đã được xử lý bằng `Condition="Exists('...')"` trong `src/AutoJMS/AutoJMS.csproj`.
- Latest recorded command `dotnet build src/AutoJMS/AutoJMS.csproj -c Debug` pass với warnings.
- Nếu build fail lại, dùng `.agent/tasks/01-fix-build-blockers.md`.

## Phase 2 — Stabilize AuthToken / JMS 401

Mục tiêu:

- Chỉ chấp nhận JMS authToken hợp lệ.
- Không nhầm license JWT với JMS token.
- Không coi HTTP 200 `code=1` là expired nếu response business logic chưa chứng minh token hết hạn.
- WebView2 refresh phải chạy UI thread.
- Không clear token trên first 401.
- Retry đúng 1 lần sau refresh.
- Mask full token logs trước production.

## Phase 3 — Tier Runtime Policy

Mục tiêu:

- BASE không auto inventory sync.
- BASE không auto database tracking.
- BASE không auto FullStack.
- BASE manual tracking/print vẫn hoạt động.
- ULTRA mới chạy background jobs sau authToken.
- `ABOUT` luôn là tab cuối.
- `TierRuntimePolicy` là source of truth cho tier behavior.

## Phase 4 — Release Pipeline Stabilization

Mục tiêu:

- GitHub Releases chứa Velopack binary lớn.
- Supabase chỉ chứa manifest/config nhỏ.
- Major update manual qua tab About.
- Không mở GitHub page.
- Stable/beta rõ ràng.
- Version SemVer dùng cho Velopack; `displayVersion` có thể phục vụ hiển thị.

## Phase 5 — FullStackOperation Stabilization

Mục tiêu:

- `FullStackOperation` là form riêng.
- Launch sau `MainForm.Shown`.
- UI ready trước, fetch sau.
- Không update grid khi grid null/disposed.
- Nếu form close thì cancel background task.
- Chỉ fetch API sau khi có JMS authToken hợp lệ.

## Phase 6 — WebView2 Automation Hardening

Mục tiêu:

- `ExecuteScriptAsync` trên UI thread.
- DOM wait thay vì delay cứng.
- Selector đưa về runtime config.
- Không sửa selector nếu không hiểu flow.
- Vue/Element UI input setter phải dùng đúng native setter + input/change events.

## Phase 7 — Operation Center Enhancement

Mục tiêu:

- Dashboard operation center.
- SLA engine.
- Risk engine.
- Grid performance.
- Realtime operation workflow.
- Chỉ làm sau khi build, auth, tier, release và FullStack lifecycle ổn định.


