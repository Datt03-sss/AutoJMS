using System.IO;
using System.Linq;

namespace AutoJMS.FullStack.LocalDb
{
    public static class FullStackLocalDbPaths
    {
        public static string GetDatabasePath(string dbFileName)
        {
            string siteCode = SiteContextProvider.Get();
            string folder = string.IsNullOrWhiteSpace(siteCode) ? "default" : SanitizeFolderName(siteCode);
            return Path.Combine(AppPaths.UserDataDir, "FullStack", folder, dbFileName);
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            // Path.GetInvalidFileNameChars() khong co '.'; can Trim('.') de "." khoi ve
            // thu muc chia se va ".." khoi thoat khoi FullStack\.
            var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().Trim('.');
            return string.IsNullOrWhiteSpace(clean) ? "default" : clean;
        }
    }
}
