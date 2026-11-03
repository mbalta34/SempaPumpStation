using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// Simple file-backed recipe persistence — the modern equivalent of the Winlog text-file recipes
    /// under <c>C:\WinlogTools\*_recete\</c>. Each recipe is a UTF-8 <c>key=value</c> file under
    /// <c>%LOCALAPPDATA%\SempaPumpStation\Recipes\&lt;category&gt;\</c>.
    /// </summary>
    public static class RecipeStore
    {
        private static string Root =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "SempaPumpStation", "Recipes");

        private static string Dir(string category)
        {
            var d = Path.Combine(Root, category);
            Directory.CreateDirectory(d);
            return d;
        }

        public static List<string> List(string category)
        {
            try
            {
                return Directory.GetFiles(Dir(category), "*.txt")
                                .Select(Path.GetFileNameWithoutExtension)
                                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                                .ToList();
            }
            catch { return new List<string>(); }
        }

        public static Dictionary<string, string> Load(string category, string name)
        {
            var result = new Dictionary<string, string>();
            var file = Path.Combine(Dir(category), name + ".txt");
            if (!File.Exists(file)) return result;
            foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
            {
                int i = line.IndexOf('=');
                if (i > 0) result[line.Substring(0, i)] = line.Substring(i + 1);
            }
            return result;
        }

        public static void Save(string category, string name, Dictionary<string, string> data)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var file = Path.Combine(Dir(category), name.Trim() + ".txt");
            File.WriteAllLines(file, data.Select(kv => kv.Key + "=" + kv.Value), Encoding.UTF8);
        }

        public static void Delete(string category, string name)
        {
            var file = Path.Combine(Dir(category), name + ".txt");
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
