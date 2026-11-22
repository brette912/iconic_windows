using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace Iconic
{
    public static class AppPaths
    {
        public static string DataDir =>
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Iconic");
        public static string LibraryFile => IOPath.Combine(DataDir, "library.json");
        public static string IconsDir => IOPath.Combine(DataDir, "icons");

        /// <summary>Where desktop items are kept after Iconic tidies them away.</summary>
        public static string StorageDir =>
            IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Iconic Library");

        /// <summary>Iconic's own copies of shortcuts from the shared (all users) desktop.</summary>
        public static string SharedDir => IOPath.Combine(StorageDir, "Shared Desktop");

        public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        public static string PublicDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    public class LibraryStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public Library Library { get; private set; }

        public void Load()
        {
            try
            {
                if (File.Exists(AppPaths.LibraryFile))
                    Library = JsonSerializer.Deserialize<Library>(File.ReadAllText(AppPaths.LibraryFile), Options);
            }
            catch
            {
                // Keep a copy of an unreadable file rather than silently losing it.
                try { File.Copy(AppPaths.LibraryFile, AppPaths.LibraryFile + ".broken", true); } catch { }
                Library = null;
            }

            Library ??= new Library();
            Library.Groups ??= new System.Collections.ObjectModel.ObservableCollection<AppGroup>();
            Library.IgnoredDesktopItems ??= new List<string>();
            if (Library.Groups.Count == 0)
                Library.Groups.Add(new AppGroup { Name = "Desktop", Accent = "#0A84FF" });
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var tmp = AppPaths.LibraryFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Library, Options));
                File.Move(tmp, AppPaths.LibraryFile, true);
            }
            catch { /* never crash the UI over a save */ }
        }
    }

    public static class DesktopService
    {
        private static readonly string[] SkipNames = { "desktop.ini", "thumbs.db", "Iconic.lnk" };

        /// <summary>Items on the user's desktop that Iconic can move in.</summary>
        public static List<string> FindCandidates() => Scan(AppPaths.Desktop);

        /// <summary>Items on the shared (all users) desktop. These need admin rights to move, so they're only referenced.</summary>
        public static List<string> FindPublicItems() => Scan(AppPaths.PublicDesktop);

        private static List<string> Scan(string dir)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;

            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); }
            catch { return list; }

            foreach (var entry in entries)
            {
                var name = IOPath.GetFileName(entry);
                if (SkipNames.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    var attr = File.GetAttributes(entry);
                    if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (attr.HasFlag(FileAttributes.Directory) && IsIconicSource(entry)) continue;
                }
                catch { continue; }
                list.Add(entry);
            }
            return list;
        }

        // Never move Iconic's own source folder off the desktop.
        private static bool IsIconicSource(string dir) =>
            File.Exists(IOPath.Combine(dir, "src", "Iconic.csproj")) ||
            File.Exists(IOPath.Combine(dir, "Iconic.csproj"));

        public static string DisplayName(string path)
        {
            var ext = IOPath.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk" || ext == ".url") return IOPath.GetFileNameWithoutExtension(path);
            return IOPath.GetFileName(path);
        }

        public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        /// <summary>Moves a file or folder into destDir, picking "name (2)" if the name is taken.</summary>
        public static string MoveTo(string source, string destDir, string name)
        {
            Directory.CreateDirectory(destDir);
            var dest = UniquePath(destDir, name);
            if (Directory.Exists(source)) Directory.Move(source, dest);
            else File.Move(source, dest);
            return dest;
        }

        public static string CopyTo(string source, string destDir, string name)
        {
            Directory.CreateDirectory(destDir);
            var dest = UniquePath(destDir, name);
            File.Copy(source, dest);
            return dest;
        }

        public static bool IsUnder(string path, string dir)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(dir)) return false;
            return path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Looks for a shortcut with the same name on the desktops or in the Start Menu.</summary>
        public static string FindReplacement(string missingPath, string displayName)
        {
            var ext = IOPath.GetExtension(missingPath ?? "").ToLowerInvariant();
            var names = new[] { IOPath.GetFileNameWithoutExtension(missingPath ?? ""), displayName }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0) return null;

            bool isShortcut = ext == ".lnk" || ext == ".url";
            var places = new List<(string Dir, bool Deep)>
            {
                (AppPaths.Desktop, false),
                (AppPaths.PublicDesktop, false)
            };
            if (isShortcut)
            {
                places.Add((Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), true));
                places.Add((Environment.GetFolderPath(Environment.SpecialFolder.Programs), true));
            }

            foreach (var (dir, deep) in places)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = deep, IgnoreInaccessible = true };
                    foreach (var file in Directory.EnumerateFiles(dir, isShortcut ? "*" + ext : "*", options))
                    {
                        if (!string.Equals(IOPath.GetExtension(file), ext, StringComparison.OrdinalIgnoreCase)) continue;
                        var stem = IOPath.GetFileNameWithoutExtension(file);
                        if (names.Any(n => n.Equals(stem, StringComparison.OrdinalIgnoreCase))) return file;
                    }
                }
                catch { }
            }
            return null;
        }

        private static string UniquePath(string dir, string name)
        {
            var path = IOPath.Combine(dir, name);
            if (!Exists(path)) return path;
            var stem = IOPath.GetFileNameWithoutExtension(name);
            var ext = IOPath.GetExtension(name);
            for (int i = 2; ; i++)
            {
                path = IOPath.Combine(dir, $"{stem} ({i}){ext}");
                if (!Exists(path)) return path;
            }
        }
    }

    /// <summary>A saved layout: groups, labels, order and custom icons in one file.</summary>
    public class BackupFile
    {
        public int Version { get; set; } = 1;
        public DateTime Created { get; set; }
        public Library Library { get; set; }
        public Dictionary<string, string> Icons { get; set; } = new Dictionary<string, string>();
    }

    public static class BackupService
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string BackupDir => IOPath.Combine(AppPaths.StorageDir, "Backups");

        public static void Export(Library library, string file)
        {
            var backup = new BackupFile { Created = DateTime.Now, Library = library };
            foreach (var item in library.Groups.SelectMany(g => g.Items))
            {
                if (!string.IsNullOrEmpty(item.CustomIcon) && File.Exists(item.CustomIcon))
                    backup.Icons[item.Id] = Convert.ToBase64String(File.ReadAllBytes(item.CustomIcon));
            }
            Directory.CreateDirectory(IOPath.GetDirectoryName(file));
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(backup, Options));
            File.Move(tmp, file, true);
        }

        public static BackupFile Read(string file) =>
            JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(file), Options);

        /// <summary>One automatic backup per day; keeps the latest 14.</summary>
        public static void AutoBackup(Library library)
        {
            try
            {
                Export(library, IOPath.Combine(BackupDir, $"Iconic backup {DateTime.Now:yyyy-MM-dd}.iconic"));
                var old = new DirectoryInfo(BackupDir).GetFiles("Iconic backup *.iconic")
                    .OrderByDescending(f => f.Name).Skip(14);
                foreach (var file in old) file.Delete();
            }
            catch { }
        }
    }

    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Iconic";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(ValueName) != null;
                }
                catch { return false; }
            }
        }

        public static void Set(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue(ValueName, false);
        }
    }
}
