using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace RemoteConfigPractice
{
    // Загружает конфиг оружия по URL и применяет его к Weapon в сцене:
    //   1. UnityWebRequest с таймаутом и повторами (экспоненциальная задержка), If-None-Match по ETag локальной копии;
    //   2. разбор CSV или JSON (формат по содержимому или явно) и валидация: damage >= 0, cooldown > 0;
    //   3. валидный конфиг сохраняется локальной копией в Application.persistentDataPath/RemoteConfig;
    //   4. ошибка загрузки или битый конфиг: берётся локальная копия, а если её нет - дефолты из кода
    //      (WeaponDefaults) и ошибка в лог.
    [DefaultExecutionOrder(-100)]
    public class RemoteConfigLoader : MonoBehaviour
    {
        // Домен .invalid зарезервирован (RFC 2606) и никогда не резолвится: настоящая ошибка сети, а не заглушка.
        public const string OfflineUrl = "https://offline.invalid/config/weapons.csv";

        [Header("Источник")]
        [Tooltip("CSV (Google Sheets: Файл → Поделиться → Опубликовать в интернете → CSV) или JSON на статическом хостинге")]
        public string url = "https://ocavit.github.io/unity-remote-config/config/weapons.csv";
        public ConfigFormat format = ConfigFormat.Auto;

        [Header("Сеть")]
        [Min(1)] public int timeoutSeconds = 10;
        [Tooltip("Сколько раз повторить запрос при ошибке сети или 5xx")]
        [Min(0)] public int retries = 2;
        [Tooltip("Задержка перед первым повтором, дальше удваивается")]
        [Min(0f)] public float retryDelay = 1f;
        [Tooltip("Добавлять к URL ?nocache=<время>: CDN GitHub Pages кэширует файлы до 10 минут")]
        public bool bypassCdnCache = true;
        [Tooltip("Отправлять If-None-Match с ETag локальной копии: если конфиг не менялся, сервер ответит 304 без тела")]
        public bool useETag = true;

        [Header("Локальная копия")]
        public string cacheFileName = ConfigCache.DefaultFileName;

        [Header("Оружие")]
        [Tooltip("Пусто - все Weapon в сцене")]
        public List<Weapon> weapons = new List<Weapon>();
        public bool loadOnStart = true;

        [Header("Отладка")]
        [Tooltip("Имитировать отсутствие сети: запрос уходит на несуществующий домен .invalid")]
        public bool simulateOffline;

        public struct Entry
        {
            public DateTime Time;
            public LogType Type;
            public string Message;
        }

        public ConfigSource Source { get; private set; } = ConfigSource.NotLoaded;
        public bool IsLoading { get; private set; }
        public string Summary { get; private set; } = "not loaded yet";
        public string RequestedUrl { get; private set; }
        public IReadOnlyList<WeaponConfig> Weapons => applied;
        public IReadOnlyList<Entry> History => history;
        public ConfigCache Cache => cache ??= ConfigCache.InPersistentData(cacheFileName);
        public event Action<RemoteConfigLoader> Loaded;

        readonly List<WeaponConfig> applied = new List<WeaponConfig>();
        readonly List<Entry> history = new List<Entry>();
        ConfigCache cache;
        Coroutine running;

        void Start()
        {
            if (loadOnStart) Reload();
        }

        public void Reload()
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Load());
        }

        public bool DeleteLocalCopy()
        {
            bool deleted = Cache.Delete();
            Write(LogType.Log, deleted ? $"Local copy deleted: {Cache.FilePath}" : "There is no local copy to delete");
            return deleted;
        }

        IEnumerator Load()
        {
            IsLoading = true;
            string source = simulateOffline ? OfflineUrl : url;
            CachedConfig cached = Cache.Load(out string cacheProblem);

            DownloadResult download = null;
            yield return Download(source, cached, result => download = result);

            var outcome = ConfigResolver.Resolve(source, format, download, cached, cacheProblem, Cache,
                WeaponDefaults.All, Write);
            Apply(outcome);
            IsLoading = false;
            running = null;
            Loaded?.Invoke(this);
        }

        IEnumerator Download(string source, CachedConfig cached, Action<DownloadResult> done)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                done(DownloadResult.Failure("URL is empty"));
                yield break;
            }

            // ETag отправляем, только если копия скачана с этого же адреса: иначе 304 подтвердил бы чужой файл.
            string etag = useETag && cached != null && cached.sourceUrl == source ? cached.etag : null;
            for (int attempt = 1; ; attempt++)
            {
                RequestedUrl = bypassCdnCache ? WithNoCache(source) : source;
                Write(LogType.Log, attempt == 1 ? $"GET {RequestedUrl}" : $"GET {RequestedUrl} (attempt {attempt})");
                float started = Time.realtimeSinceStartup;
                using (var request = UnityWebRequest.Get(RequestedUrl))
                {
                    request.timeout = timeoutSeconds;
                    if (!string.IsNullOrEmpty(etag)) request.SetRequestHeader("If-None-Match", etag);
                    yield return request.SendWebRequest();

                    long code = request.responseCode;
                    string took = FormattableString.Invariant($"{(Time.realtimeSinceStartup - started) * 1000f:0} ms");
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        if (code == 304)
                        {
                            Write(LogType.Log, $"HTTP 304 Not Modified, {took}");
                            done(DownloadResult.Unchanged());
                        }
                        else
                        {
                            string text = request.downloadHandler.text;
                            Write(LogType.Log, $"HTTP {code}, {text.Length} chars, {took}");
                            done(DownloadResult.Success(text, code, request.GetResponseHeader("ETag"),
                                request.GetResponseHeader("Last-Modified")));
                        }
                        yield break;
                    }

                    // Для ответа сервера error уже содержит код ("HTTP/1.1 404 Not Found"), для сбоя сети - причину.
                    string error = code > 0 ? request.error : $"{request.result}: {request.error}";
                    // Повторяем только то, что может пройти само: сбой сети, таймаут, 5xx, 429. 404 повтором не лечится.
                    bool transient = request.result == UnityWebRequest.Result.ConnectionError || code >= 500 ||
                                     code == 429 || code == 408;
                    if (!transient || attempt > retries)
                    {
                        done(DownloadResult.Failure(error, code));
                        yield break;
                    }
                    float delay = retryDelay * Mathf.Pow(2f, attempt - 1);
                    Write(LogType.Warning, FormattableString.Invariant($"{error}; retry in {delay:0.#} s"));
                    yield return new WaitForSecondsRealtime(delay);
                }
            }
        }

        void Apply(LoadOutcome outcome)
        {
            Source = outcome.Source;
            Summary = outcome.Summary;
            applied.Clear();
            applied.AddRange(outcome.Weapons);

            var targets = weapons.Count > 0 ? weapons : FindObjectsByType<Weapon>(FindObjectsSortMode.None).ToList();
            foreach (var weapon in targets.Where(weapon => weapon != null).OrderBy(weapon => weapon.id))
            {
                var config = ConfigResolver.Find(outcome.Weapons, weapon.id);
                var from = outcome.Source;
                if (config == null)
                {
                    config = WeaponDefaults.Find(weapon.id);
                    from = ConfigSource.Defaults;
                    if (config != null && outcome.Source != ConfigSource.Defaults)
                        Write(LogType.Warning, $"\"{weapon.id}\" is not in the config: using the default value");
                }
                if (config == null)
                {
                    Write(LogType.Error, $"\"{weapon.id}\" has neither a config entry nor a default value: not changed");
                    continue;
                }
                weapon.Apply(config, from);
            }

            foreach (var config in outcome.Weapons.Where(config => targets.All(weapon => weapon == null ||
                         !string.Equals(weapon.id?.Trim(), config.id, StringComparison.OrdinalIgnoreCase))))
                Write(LogType.Log, $"Config entry \"{config.id}\" is not used by any Weapon in the scene");
        }

        static string WithNoCache(string source) =>
            source + (source.Contains("?") ? "&" : "?") + "nocache=" +
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        void Write(LogType type, string message)
        {
            history.Add(new Entry { Time = DateTime.Now, Type = type, Message = message });
            if (history.Count > 50) history.RemoveAt(0);
            Debug.unityLogger.Log(type, "[RemoteConfig] " + message);
        }
    }
}
