import re

filepath = r"d:\v1.2605.2(new-test)\src\AutoJMS\Forms\FullStackOperation.cs"

with open(filepath, "r", encoding="utf-8") as f:
    content = f.read()

# Regular expression to match standard C# method declarations
method_pattern = r"(public|private|protected)\s+(async\s+)?(void|Task|Task<[^>]+>|string|bool|int|List<[^>]+>|IEnumerable<[^>]+>|object|Control|DataGridViewTextBoxColumn)\s+(\w+)\s*\(([^)]*)\)"

matches = re.finditer(method_pattern, content)

print("Methods found in FullStackOperation.cs:")
for m in matches:
    start_char = m.start()
    # Find line number
    line_num = content[:start_char].count("\n") + 1
    decl = m.group(0)
    name = m.group(4)
    print(f"Line {line_num}: {decl}")
