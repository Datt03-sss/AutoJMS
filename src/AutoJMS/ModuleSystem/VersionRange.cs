using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoJMS.ModuleSystem
{
    public class VersionRange
    {
        private readonly string _raw;
        private readonly string _name;
        private readonly string _op;
        private readonly Version _version;

        private VersionRange(string raw, string name, string op, Version version)
        {
            _raw = raw;
            _name = name;
            _op = op;
            _version = version;
        }

        public static (string Name, VersionRange Range)? Parse(string constraint)
        {
            if (string.IsNullOrWhiteSpace(constraint)) return null;

            var parts = constraint.Split(new[] { '>', '<', '=', '!' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            var name = parts[0].Trim();

            var opBuilder = "";
            var rest = constraint.Substring(name.Length).Trim();
            foreach (var ch in rest)
            {
                if (ch == '>' || ch == '<' || ch == '=' || ch == '!')
                    opBuilder += ch;
                else
                    break;
            }
            var verStr = rest.Substring(opBuilder.Length).Trim();

            if (string.IsNullOrWhiteSpace(opBuilder) || string.IsNullOrWhiteSpace(verStr))
                return null;

            if (!Version.TryParse(verStr, out var version))
                return null;

            return (name, new VersionRange(constraint, name, opBuilder, version));
        }

        public static List<(string Name, VersionRange Range)> ParseMany(List<string> constraints)
        {
            return constraints
                .Select(Parse)
                .Where(r => r.HasValue)
                .Select(r => r.Value)
                .ToList();
        }

        public bool IsSatisfiedBy(string versionStr)
        {
            if (!Version.TryParse(versionStr, out var version))
                return false;

            return _op switch
            {
                ">=" => version >= _version,
                ">" => version > _version,
                "<=" => version <= _version,
                "<" => version < _version,
                "==" => version == _version,
                "=" => version == _version,
                "!=" => version != _version,
                _ => false
            };
        }

        public override string ToString() => _raw;
    }
}
