using System.Reflection;

namespace AutoJMS;

public static class AppVersion
{
    private static string _cached;

    public static string Current
    {
        get
        {
            if (_cached != null) return _cached;
            var info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                _cached = NormalizeDisplayVersion(info);
                return _cached;
            }

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            _cached = v != null
                ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
                : "0.00.00.0";
            return _cached;
        }
    }

    public static string NormalizeDisplayVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";

        string normalized = version.Trim();
        int buildMetadataIndex = normalized.IndexOf('+');

        if (buildMetadataIndex >= 0)
            normalized = normalized[..buildMetadataIndex];

        return normalized.Trim();
    }
}
