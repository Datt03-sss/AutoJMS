# morphicons — vendored, verbatim

Mã nguồn bên thứ ba. **Không sửa các file `.js` trong thư mục này.**

| | |
|---|---|
| Gói | [`morphicons`](https://www.npmjs.com/package/morphicons) `1.7.1` |
| Trang chủ | https://www.morphicons.com |
| Repo | https://github.com/guillermolg00/morphicons |
| Giấy phép | MIT — xem [LICENSE](./LICENSE) |
| Lấy về bằng | `npm pack morphicons@1.7.1` → `package/dist/` |

## Vì sao chỉ có 6 file

Gói npm có 30 file (React / Vue / Svelte / Astro / React Native adapter). Dashboard
này không dùng framework nào trong số đó, chỉ dùng custom element `<morph-icon>`,
nên chỉ chép đúng nhánh phụ thuộc của `dist/element.js`:

```
element.js
  └── controller-CXZuwJ_M.js
        ├── dom.js ──────────────┐
        ├── normalize-CYnN3Npw.js│
        └── spring-CFHloqPP.js ──┘
```

Tên file băm (`-CXZuwJ_M`, `-CYnN3Npw`, `-CFHloqPP`) là tên upstream sinh ra khi build;
giữ nguyên để các lệnh `import` bên trong tự phân giải mà không phải sửa một dòng nào.

## Vì sao là ESM, không phải UMD

Dashboard được phục vụ qua `SetVirtualHostNameToFolderMapping` tại origin
`https://autojms.local` (xem `Forms/FullStackOperation.Dashboard.cs`), tức là một origin
https thật chứ không phải `file://`. `<script type="module">` chạy bình thường và CSP
`script-src 'self'` cho phép import cùng origin. Không cần bundler, không cần UMD.

## Nâng cấp

1. `npm pack morphicons@<version>` ở ngoài repo.
2. Đối chiếu lại đồ thị import ở trên (tên file băm **sẽ đổi**) rồi chép đúng nhánh đó.
3. Cập nhật số version trong bảng trên và trong `../aj-icons.js`.
4. Chạy `node eng/harness/check-dashboard-icons.mjs` — gate này bắt mọi import gãy.

Điểm vào của AutoJMS là [`../aj-icons.js`](../aj-icons.js); quy chuẩn sử dụng nằm ở
[.agent/rules/11-icon-and-animation-rules.md](../../../../.agent/rules/11-icon-and-animation-rules.md).
