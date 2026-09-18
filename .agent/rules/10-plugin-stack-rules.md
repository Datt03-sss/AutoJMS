# Plugin Stack Rules — ponytail, agent-skills, graphify

Áp dụng cho mọi agent. Bổ sung cho [08-agent-tooling-rules.md](./08-agent-tooling-rules.md), không thay thế nó.

## Precedence — đọc trước

```
AGENTS.md  >  CLAUDE.md  >  .agent/rules/*  >  .agent/skills/*  >  plugin skill / MCP tool
```

Một plugin *có thể* làm gì đó không bao giờ là quyền được làm điều đó. `ponytail` sẽ ép bạn viết ít
code nhất, `agent-skills` sẽ đề xuất refactor hoặc test suite mới, `graphify` sẽ muốn dựng lại graph —
cả ba vẫn bị chặn bởi Minimal Edit Rule, Protected Files, Secret Policy, khoá single-writer trong
`.agent-lock.md`, và gate "không push khi Release build chưa pass".

---

## 1. Thực tế đang cài gì

| Tên | Kind | Khai báo ở | Ai có | Scope |
|---|---|---|---|---|
| `superpowers@claude-plugins-official` | Claude Code plugin | `.claude/settings.json` | Claude Code CLI | project |
| `ponytail@ponytail` | Claude Code plugin | `.claude/settings.json` | Claude Code CLI | project |
| `agent-skills@addy-agent-skills` | Claude Code plugin | `.claude/settings.json` | Claude Code CLI | project |
| `graphify` | **Skill trong repo**, không phải plugin | `.claude/skills/graphify/` | client nào đọc `.claude/skills/` | project |
| `desktop-commander` | MCP server | `.mcp.json` | client nào load `.mcp.json` | project |

Cả ba plugin đều **project scope** — khai báo nằm trong `.claude/settings.json` và file đó được commit,
nên mọi session Claude Code mở repo này đều nhận được. Cowork / Antigravity / ChatGPT **không** có
plugin; đừng viết hướng dẫn giả định mọi agent đều có `/ponytail` hay `/spec`.

Dựng lại trên một máy mới:

```bash
pip install graphifyy
```

Marketplace và plugin tự resolve từ `.claude/settings.json` khi mở repo. `graphifyy` phải cài thủ công
vì nó là CLI Python, không đi kèm git.

---

## 2. ponytail — mặc định BẬT, và đó là chủ ý

`ponytail` tự kích hoạt ở mode `full` qua hook `SessionStart` mỗi phiên. Nó buộc trả lời bốn câu hỏi
trước khi viết code: code này có cần tồn tại không (YAGNI) → có thứ tái dùng được không → stdlib hoặc
native feature có sẵn không → có viết được một dòng thay vì năm mươi không.

**Đây là lớp thực thi tự động cho Minimal Edit Rule của CLAUDE.md.** Trên codebase này — nơi
`Main.cs` đã quá lớn và mỗi abstraction thừa là một chỗ để tab này rò sang tab khác — mặc định `full`
là đúng. Không tắt nó chỉ vì thấy vướng.

| Lệnh | Dùng khi |
|---|---|
| `/ponytail full` | mặc định, không cần gõ |
| `/ponytail lite` | khi đang viết Designer code hoặc boilerplate WinForms bắt buộc dài dòng |
| `/ponytail ultra` | chỉ khi Owner yêu cầu rõ một fix tối giản nhất có thể |
| `/ponytail off` | chỉ khi Owner yêu cầu rõ |
| `/ponytail-review` | soát một diff trước khi commit |
| `/ponytail-audit`, `/ponytail-debt`, `/ponytail-gain` | báo cáo, **read-only** — không tự sửa theo kết quả |

### Ràng buộc

1. **`ponytail-audit` / `ponytail-debt` không phải work order.** Chúng sẽ liệt kê hàng loạt chỗ "thừa"
   trong `Main.cs`, `TierRuntimePolicy.cs`, `VelopackUpdateService.cs`. Phần lớn là Protected Files.
   Xuất báo cáo cho Owner, không tự dọn.
2. **"Đơn giản hơn" không thắng "đang chạy đúng".** `.agent/rules/07-do-not-break-existing-logic.md`
   vẫn cao hơn. Logic nghiệp vụ DKCH/tier trông thừa thãi thường là do một ca thật ngoài đời.
3. **Comment `ponytail:`** — khi cố ý cắt góc, ponytail yêu cầu ghi chú trần giới hạn và đường nâng
   cấp. Giữ nguyên format đó, đừng đổi sang `// TODO`.

---

## 3. agent-skills — 25 skill, dùng có chọn lọc

Gồm 25 skill vòng đời (spec → plan → build → verify → review → ship), 9 slash command
(`/spec`, `/planning`, `/build`, `/test`, `/review`, `/ship`, `/constraints`, `/code-simplify`,
`/webperf`) và 4 subagent (`code-reviewer`, `security-auditor`, `test-engineer`,
`web-performance-auditor`).

### Trùng lặp với superpowers — quy tắc gỡ

Nhiều skill trùng thẳng với `superpowers` (TDD, planning, code review, debugging). Đừng chạy cả hai
cho cùng một việc.

| Việc | Dùng |
|---|---|
| Brainstorm / làm rõ spec trước khi code | `superpowers:brainstorming` |
| Viết plan, thực thi plan | `superpowers:writing-plans` / `executing-plans` |
| Debug tìm root cause | `superpowers:systematic-debugging` |
| TDD trên class logic thuần | `superpowers:test-driven-development` |
| Soát bảo mật licensing/tier/DataHub | `agent-skills` → `security-auditor` |
| Soát API/endpoint design phía DataHub | `agent-skills` → `api-and-interface-design` |
| Checklist trước khi phát hành | `agent-skills` → `shipping-and-launch` |

Nguyên tắc: **superpowers cho quy trình, agent-skills cho checklist chuyên đề.** Khi cả hai cùng
áp được, superpowers đi trước vì nó đã ăn khớp với `.agent/rules/08`.

### Ràng buộc

1. **`/ship` không phải lệnh phát hành.** Quy trình release của repo này là
   `.agent/skills/autojms-release-build/SKILL.md` + `release/build-release.ps1`, và build/upload
   release chỉ khi Owner yêu cầu. Dùng `/ship` như checklist đọc, không như script chạy.
2. **`/test` không sinh test cho WinForms.** Giới hạn TDD trong `.agent/rules/08 §3` giữ nguyên:
   chỉ class logic thuần (`DkchJourneyAnalyzer`, `Tab2Config`, parser, `TierDefinitions`). Không dựng
   harness giả cho `Main.Designer.cs` hay WebView2.
3. **`/webperf` và `frontend-ui-engineering` chỉ áp cho `backend/.../dashboard/`.** AutoJMS client là
   WinForms — Lighthouse/Core Web Vitals không áp dụng.
4. **Subagent không được push.** Bất kỳ subagent nào cũng bị gate build/verify như agent chính.

---

## 4. graphify — skill thủ công, KHÔNG phải mặc định

`graphify` biến repo thành knowledge graph truy vấn được (`graphify query|path|explain`).

### Hook đã bị gỡ — đừng cài lại

`graphify install --project` tự đăng ký hook `PreToolUse` chặn mọi `Bash|Grep|Read|Glob` để gọi
`graphify hook-guard`. **Hook đó đã bị gỡ khỏi `.claude/settings.json` có chủ ý**, vì hai lý do:

1. `graphify` là script Python nằm ngoài PATH mặc định trên Windows
   (`…\Python\pythoncore-3.14-64\Scripts\`). Máy nào chưa `pip install graphifyy` sẽ lỗi
   `command not found` ở **mọi** tool call.
2. Repo này đã có `.codegraph/` làm đúng việc đó, và global rule của Owner là CodeGraph trước tiên.
   Hai hệ đồ thị cùng chặn `Read`/`Grep` là dư thừa.

Nếu chạy lại `graphify install`, nó sẽ ghi hook trở lại. Gỡ hook lần nữa trước khi commit.

### Dùng khi nào

| Tình huống | Dùng |
|---|---|
| Hỏi code C#/JS trong repo | `.codegraph/` (`codegraph explore "..."`) — mặc định |
| Gộp cả docs/PDF/ảnh vào cùng một graph với code | `graphify` |
| Cần `graph.html` trực quan để trình bày cho Owner | `graphify` |
| Truy vết quan hệ giữa hai symbol | CodeGraph trước; `graphify path` nếu CodeGraph không ra |

### Ràng buộc

1. **`graphify-out/` không bao giờ được commit.** Đã nằm trong `.gitignore`. Nó nhúng nguyên văn code
   vào `graph.json`/wiki, và repo này PUBLIC.
2. **Không chạy `graphify` trên thư mục chứa dữ liệu thật** — `docs/manual/samples/`,
   `docs/manual/*.xlsx` chứa tên/địa chỉ/SĐT khách hàng. Chỉ trỏ vào `src/`, `backend/`, `tests/`.
3. **Không bật `--strict`.** Flag đó chặn lần đọc file thô đầu tiên mỗi phiên cho tới khi chạy
   `graphify query` — xung đột trực tiếp với luồng CodeGraph.
4. Phần lớn máy sẽ phải gọi bằng đường dẫn đầy đủ tới `graphify.exe` nếu chưa thêm Scripts vào PATH.

---

## 5. Ràng buộc chung cho mọi plugin về sau

1. **Thêm marketplace hoặc plugin mới = phải có yêu cầu rõ của Owner.** `.claude/settings.json` được
   commit, nên mỗi dòng thêm vào là một nguồn code bên thứ ba chạy trên máy mọi người mở repo.
2. **Không bao giờ commit hook gọi binary không chắc có trên PATH.** Hook trong
   `.claude/settings.json` chạy trên mọi máy; một lệnh thiếu sẽ làm hỏng cả phiên. Hook đi kèm plugin
   dùng `${CLAUDE_PLUGIN_ROOT}` (như `ponytail`) thì an toàn vì tự chứa.
3. **Không plugin nào được vượt gate.** Build → verify → commit → push. `/ship`, `execute-plan`,
   `/ponytail-review` chạy xong đều không phải là giấy phép push.
4. **Không plugin nào được sửa Protected Files** trong CLAUDE.md, bất kể skill nào gợi ý.
5. **Tôn trọng khoá.** Đọc `.agent-lock.md` trước lần ghi đầu tiên. Plugin không miễn trừ điều này.
6. **Không cài plugin/skill ở user scope cho việc của repo này.** Dùng `--scope project` để mọi agent
   nhận cùng một cấu hình, thay vì lệ thuộc vào máy của một người.
