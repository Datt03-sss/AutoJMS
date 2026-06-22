import os

filepath = r"d:\v1.2605.2(new-test)\src\AutoJMS\Forms\FullStackOperation.cs"

with open(filepath, "rb") as f:
    raw_content = f.read()

# Decode to string
content = raw_content.decode("utf-8")

# Normalize newlines to \n
content_norm = content.replace("\r\r\n", "\n").replace("\r\n", "\n").replace("\r", "\n")

target_marker = """        private void UpdateDashGridDataSource(List<WaybillDbModel> data)
        {
            _lastFilteredDashRows = data?.ToList() ?? new List<WaybillDbModel>();
            PostStateToWebView2();
        }"""

marker_idx = content_norm.find(target_marker)
if marker_idx == -1:
    print("ERROR: Target marker not found!")
    exit(1)

# Keep up to target_marker plus the target_marker itself
truncated_content = content_norm[:marker_idx + len(target_marker)]

# Append closing braces for class and namespace
truncated_content += "\n    }\n}\n"

# Write back with CRLF line endings
final_content = truncated_content.replace("\n", "\r\n")

with open(filepath, "wb") as f:
    f.write(final_content.encode("utf-8"))

print("SUCCESSFULLY TRUNCATED FullStackOperation.cs!")
