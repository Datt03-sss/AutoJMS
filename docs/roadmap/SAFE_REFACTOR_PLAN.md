# Safe Refactor Plan

Refactor chỉ được bắt đầu khi build và behavior hiện tại đã đủ rõ. Phase 1.5 không refactor code.

## Principles

- Không refactor khi build đang fail.
- Không refactor `MainForm` lớn trong một lần.
- Không tách service nếu chưa có test/acceptance criteria.
- Refactor theo vertical slice nhỏ.
- Mỗi patch chỉ giải quyết một vấn đề.
- Không đổi public behavior nếu không cần.
- Mỗi bugfix phải có rollback note.
- WinForms Designer cần cực kỳ cẩn thận.
- Không di chuyển vào `src/` cho đến khi release pipeline ổn.

## Safe Slice Pattern

1. Read context/rules.
2. Reproduce or locate exact issue.
3. Define current behavior.
4. Define acceptance criteria.
5. Patch smallest area.
6. Build/test.
7. Document rollback.

## Unsafe Refactor Examples

- Moving all root `.cs` files into `src/` before release scripts are verified.
- Splitting `Main.cs` without tests for tabs, WebView2 token capture, tier policy and update flow.
- Editing WinForms Designer-generated files manually without a specific UI task.
- Replacing service contracts while old code paths still depend on static state.

## Migration Guardrails

- Keep HOME/DKCH/TRACKING/PRINT/ABOUT behavior unchanged unless a task explicitly targets that feature.
- Keep `FullStackOperation` as a standalone ULTRA form.
- Keep `ModuleSystem` until there is a dedicated migration plan.
- Keep release/installer scripts unchanged until a release pipeline task explicitly allows script edits.

