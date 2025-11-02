using System.IO;

namespace Idf2Kompas.Services
{
    public static class LibraryHelper
    {
        public static bool BodyExists(string libDir, string modelName)
        {
            return !string.IsNullOrWhiteSpace(FindModelPath(libDir, modelName));
        }

        public static string FindModelPath(string libDir, string modelName)
        {
            if (string.IsNullOrWhiteSpace(libDir) || string.IsNullOrWhiteSpace(modelName)) return null;
            var stem = Path.GetFileNameWithoutExtension(modelName);
            foreach (var ext in new[] { ".m3d", ".a3d", ".x_t", ".step", ".stp" })
            {
                var p = Path.Combine(libDir, stem + ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}
