using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// File-backed recipe persistence — the modern equivalent of the Winlog text-file recipes
    /// under <c>C:\WinlogTools\*_recete\</c>. Each recipe is an XML file under
    /// <c>%LOCALAPPDATA%\SempaPumpStation\Recipes\&lt;category&gt;\&lt;name&gt;.xml</c>:
    /// <code>
    /// &lt;Recipe Category="Motor" Name="Volt 45 kW" Saved="2026-11-19T14:05:00"&gt;
    ///   &lt;Field Key="MOTOR_GUCU" Label="Gücü (kW)"&gt;45&lt;/Field&gt;
    /// &lt;/Recipe&gt;
    /// </code>
    /// Numbers are stored with the invariant culture. Recipes saved before 1.8 (<c>key=value</c> .txt files) are
    /// still listed and loaded; saving one writes it as XML and removes the .txt.
    /// </summary>
    public static class RecipeStore
    {
        private const string Extension = ".xml", LegacyExtension = ".txt";

        /// <summary>The recipes folder; tests point it elsewhere.</summary>
        public static string Root { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SempaPumpStation", "Recipes");

        private static string Dir(string category)
        {
            var d = Path.Combine(Root, category);
            Directory.CreateDirectory(d);
            return d;
        }

        private static string FileOf(string category, string name, string extension) =>
            Path.Combine(Dir(category), FileName(name) + extension);

        /// <summary>The recipe name as a file name: characters Windows does not allow become '_'.</summary>
        private static string FileName(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string(name.Trim().Select(ch => bad.Contains(ch) ? '_' : ch).ToArray());
        }

        public static List<string> List(string category)
        {
            try
            {
                var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                foreach (var f in Directory.GetFiles(Dir(category), "*" + Extension)) names.Add(NameOf(f));
                foreach (var f in Directory.GetFiles(Dir(category), "*" + LegacyExtension)) names.Add(Path.GetFileNameWithoutExtension(f));
                return names.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }

        // The name inside the file, which keeps characters the file name could not
        private static string NameOf(string file)
        {
            try
            {
                var name = (string)XDocument.Load(file).Root?.Attribute("Name");
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { }
            return Path.GetFileNameWithoutExtension(file);
        }

        public static Dictionary<string, string> Load(string category, string name)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(name)) return result;

            var file = FileOf(category, name, Extension);
            if (File.Exists(file))
            {
                foreach (var f in XDocument.Load(file).Root.Elements("Field"))
                {
                    var key = (string)f.Attribute("Key");
                    if (!string.IsNullOrEmpty(key)) result[key] = f.Value;
                }
                return result;
            }

            var legacy = FileOf(category, name, LegacyExtension);
            if (File.Exists(legacy))
                foreach (var line in File.ReadAllLines(legacy, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) result[line.Substring(0, i)] = line.Substring(i + 1);
                }
            return result;
        }

        /// <param name="labels">Optional field captions, written next to the keys so the file reads on its own.</param>
        public static void Save(string category, string name, IEnumerable<KeyValuePair<string, string>> data,
                                IReadOnlyDictionary<string, string> labels = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();

            var root = new XElement("Recipe",
                new XAttribute("Category", category),
                new XAttribute("Name", name),
                new XAttribute("Saved", DateTime.Now.ToString("s")));
            foreach (var kv in data)
            {
                var field = new XElement("Field", new XAttribute("Key", kv.Key));
                if (labels != null && labels.TryGetValue(kv.Key, out var label)) field.Add(new XAttribute("Label", label));
                field.Value = kv.Value ?? "";
                root.Add(field);
            }

            // Written next to the target first, so a crash never leaves a half-written recipe
            var file = FileOf(category, name, Extension);
            var temp = file + ".tmp";
            new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(temp);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);

            var legacy = FileOf(category, name, LegacyExtension);
            if (File.Exists(legacy)) File.Delete(legacy);
        }

        public static void Delete(string category, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            foreach (var ext in new[] { Extension, LegacyExtension })
            {
                var file = FileOf(category, name, ext);
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
