using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RemoteConfigPractice
{
    public enum ConfigSource
    {
        NotLoaded,
        Remote,    // скачан с сервера (или сервер ответил 304: локальная копия совпадает с серверной)
        LocalCopy, // сервер недоступен или отдал битый конфиг: последняя валидная копия из persistentDataPath
        Defaults   // нет ни сервера, ни локальной копии: значения, прописанные в коде (WeaponDefaults)
    }

    // Чем закончился HTTP-запрос. Создаёт RemoteConfigLoader, а в тестах - сам тест (без сети).
    public sealed class DownloadResult
    {
        public bool Ok { get; private set; }
        public bool NotModified { get; private set; }
        public string Text { get; private set; }
        public long ResponseCode { get; private set; }
        public string ETag { get; private set; }
        public string LastModified { get; private set; }
        public string Error { get; private set; }

        public static DownloadResult Success(string text, long responseCode = 200, string etag = null, string lastModified = null) =>
            new DownloadResult { Ok = true, Text = text, ResponseCode = responseCode, ETag = etag, LastModified = lastModified };

        public static DownloadResult Unchanged() => new DownloadResult { Ok = true, NotModified = true, ResponseCode = 304 };

        public static DownloadResult Failure(string error, long responseCode = 0) =>
            new DownloadResult { Error = error, ResponseCode = responseCode };
    }

    public sealed class LoadOutcome
    {
        public ConfigSource Source;
        public ConfigFormat Format;
        public List<WeaponConfig> Weapons;
        public string Summary;
    }

    // Цепочка «сервер → локальная копия → дефолты». Без MonoBehaviour и сети, поэтому вся логика проверяется тестами.
    public static class ConfigResolver
    {
        public static LoadOutcome Resolve(string url, ConfigFormat format, DownloadResult download, CachedConfig cached,
            string cacheProblem, ConfigCache cache, IReadOnlyList<WeaponConfig> defaults, Action<LogType, string> log)
        {
            // 1. Ответ сервера.
            string remoteProblem;
            if (download.Ok && download.NotModified)
            {
                var parsed = cached != null ? ParseCached(cached) : null;
                if (parsed != null && parsed.IsValid)
                {
                    log(LogType.Log, $"Server: not modified (HTTP 304), local copy from {cached.savedAtUtc} is current");
                    return Outcome(ConfigSource.Remote, parsed,
                        $"not modified on the server (HTTP 304): {Count(parsed)} from the local copy of {cached.savedAtUtc}");
                }
                remoteProblem = "server answered 304 Not Modified, but there is no valid local copy";
            }
            else if (download.Ok)
            {
                var parsed = WeaponConfigParser.Parse(download.Text, format);
                if (parsed.IsValid)
                {
                    var copy = new CachedConfig
                    {
                        sourceUrl = url,
                        format = parsed.Format.ToString(),
                        etag = download.ETag,
                        lastModified = download.LastModified,
                        savedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
                        content = download.Text
                    };
                    if (cache.Save(copy, out string saveError)) log(LogType.Log, $"Local copy saved: {cache.FilePath}");
                    else log(LogType.Warning, $"Local copy not saved: {saveError}");
                    log(LogType.Log, $"Remote config OK: {Count(parsed)}, {parsed.Format}, HTTP {download.ResponseCode}");
                    return Outcome(ConfigSource.Remote, parsed,
                        $"downloaded from the server: {Count(parsed)} ({parsed.Format}, HTTP {download.ResponseCode})");
                }
                remoteProblem = $"remote config is invalid: {parsed.ErrorSummary}";
            }
            else
            {
                remoteProblem = $"download failed: {download.Error}";
            }

            // 2. Локальная копия.
            if (cached != null)
            {
                var parsed = ParseCached(cached);
                if (parsed.IsValid)
                {
                    log(LogType.Warning, $"Remote config not used ({remoteProblem}). Using the local copy saved {cached.savedAtUtc}");
                    return Outcome(ConfigSource.LocalCopy, parsed,
                        $"local copy saved {cached.savedAtUtc}: {Count(parsed)}. Server: {remoteProblem}");
                }
                cacheProblem = $"local copy is invalid: {parsed.ErrorSummary}";
            }

            // 3. Дефолты из кода.
            log(LogType.Error, $"Remote config not used ({remoteProblem}) and {cacheProblem ?? "no local copy"}. " +
                               "Using built-in default values");
            return new LoadOutcome
            {
                Source = ConfigSource.Defaults,
                Format = ConfigFormat.Auto,
                Weapons = defaults.Select(weapon => weapon.Clone()).ToList(),
                Summary = $"built-in defaults: {defaults.Count} weapons. Server: {remoteProblem}; {cacheProblem ?? "no local copy"}"
            };
        }

        public static WeaponConfig Find(IEnumerable<WeaponConfig> weapons, string id) =>
            weapons.FirstOrDefault(weapon => string.Equals(weapon.id?.Trim(), id?.Trim(), StringComparison.OrdinalIgnoreCase));

        static ParsedConfig ParseCached(CachedConfig cached)
        {
            var format = Enum.TryParse(cached.format, out ConfigFormat stored) ? stored : ConfigFormat.Auto;
            return WeaponConfigParser.Parse(cached.content, format);
        }

        static LoadOutcome Outcome(ConfigSource source, ParsedConfig parsed, string summary) =>
            new LoadOutcome { Source = source, Format = parsed.Format, Weapons = parsed.Weapons, Summary = summary };

        static string Count(ParsedConfig parsed) => parsed.Weapons.Count == 1 ? "1 weapon" : $"{parsed.Weapons.Count} weapons";
    }
}
