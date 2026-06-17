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
                _cached = info.Trim();
                return _cached;
            }

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            _cached = v != null
                ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
                : "0.00.00.0";
            return _cached;
        }
    }
}
