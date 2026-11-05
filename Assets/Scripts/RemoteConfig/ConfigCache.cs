using System;
using System.IO;
using UnityEngine;

namespace RemoteConfigPractice
{
    // Содержимое локальной копии: сам скачанный конфиг (как есть, CSV или JSON) и откуда/когда он получен.
    [Serializable]
    public class CachedConfig
    {
        public string sourceUrl;
        public string format;
        public string etag;
        public string lastModified;
        public string savedAtUtc;
        public string content;
    }

    // Локальная копия последнего валидного конфига: один JSON-файл в Application.persistentDataPath/RemoteConfig.
    // Пишется атомарно (сначала во временный файл, потом подменой), так что сбой посреди записи не портит рабочую копию.
    public sealed class ConfigCache
    {
        public const string DefaultFileName = "weapons.cache.json";

        public string FilePath { get; }
        public bool Exists => File.Exists(FilePath);

        public ConfigCache(string directory, string fileName = DefaultFileName)
        {
            FilePath = Path.Combine(directory, fileName);
        }

        public static ConfigCache InPersistentData(string fileName = DefaultFileName) =>
            new ConfigCache(Path.Combine(Application.persistentDataPath, "RemoteConfig"), fileName);

        public CachedConfig Load(out string error)
        {
            error = null;
            if (!File.Exists(FilePath))
            {
                error = "no local copy";
                return null;
            }
            try
            {
                var cached = JsonUtility.FromJson<CachedConfig>(File.ReadAllText(FilePath));
                if (cached == null || string.IsNullOrWhiteSpace(cached.content))
                {
                    error = "local copy is empty or damaged";
                    return null;
                }
                return cached;
            }
            catch (Exception e)
            {
                error = $"local copy can't be read: {e.Message}";
                return null;
            }
        }

        public bool Save(CachedConfig config, out string error)
        {
            string temp = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(temp, JsonUtility.ToJson(config, true));
                if (!File.Exists(FilePath))
                {
                    File.Move(temp, FilePath);
                }
                else
                {
                    try
                    {
                        File.Replace(temp, FilePath, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temp, FilePath, true);
                        File.Delete(temp);
                    }
                }
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { /* временный файл не важен */ }
                return false;
            }
        }

        public bool Delete()
        {
            if (!File.Exists(FilePath)) return false;
            File.Delete(FilePath);
            return true;
        }
    }
}
