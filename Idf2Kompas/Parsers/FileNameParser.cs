using System.IO;
using System.Text.RegularExpressions;

namespace Idf2Kompas.Parsers
{
    public sealed class FileNameInfo
    {
        public string DesignationItem { get; set; }
        public string Name { get; set; }
    }

    public static class FileNameParser
    {
        public static FileNameInfo Parse(string path)
        {
            var info = new FileNameInfo();
            if (string.IsNullOrWhiteSpace(path)) return info;
            var baseName = Path.GetFileNameWithoutExtension(path) ?? "";
            baseName = baseName.Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2012', '-')
                               .Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u2015', '-');
            baseName = Regex.Replace(baseName, @"(?:[СC][БB])", "СБ", RegexOptions.IgnoreCase);
            var m = Regex.Match(baseName,
                @"^(?:(?<desig>[A-Za-z\u0400-\u04FF0-9]+(?:\.[A-Za-z\u0400-\u04FF0-9]+)+(?:-[A-Za-z0-9]+)?)\s*)?(?<doc>СБ)?\s*(?<name>.+)?$",
                RegexOptions.CultureInvariant);
            if (m.Success)
            {
                info.DesignationItem = m.Groups["desig"].Success ? m.Groups["desig"].Value : null;
                info.Name = m.Groups["name"].Success ? m.Groups["name"].Value.Trim() : null;
            }
            return info;
        }
    }
}
