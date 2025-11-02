using System;
using System.IO;
using System.Xml.Serialization;
using Idf2Kompas.Models;

namespace Idf2Kompas.Services
{
    public static class SettingsService
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Idf2Kompas");
        private static string FilePath => Path.Combine(Dir, "settings.xml");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                using (var fs = File.OpenRead(FilePath))
                {
                    var xs = new XmlSerializer(typeof(AppSettings));
                    return (AppSettings)xs.Deserialize(fs) ?? new AppSettings();
                }
            }
            catch { return new AppSettings(); }
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                using (var fs = File.Create(FilePath))
                {
                    var xs = new XmlSerializer(typeof(AppSettings));
                    xs.Serialize(fs, s);
                }
            }
            catch { }
        }
    }
}
