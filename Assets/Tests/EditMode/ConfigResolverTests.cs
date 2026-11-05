using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace RemoteConfigPractice.Tests
{
    // Цепочка «сервер → локальная копия → дефолты» без сети: ответ сервера подставляется готовым DownloadResult,
    // локальная копия пишется во временную папку.
    public class ConfigResolverTests
    {
        const string Url = "https://example.com/config/weapons.csv";
        const string Good = "id,damage,cooldown\npistol,12,0.4\nrifle,22,0.15\n";
        const string Older = "id,damage,cooldown\npistol,11,0.45\nrifle,20,0.18\n";

        string folder;
        ConfigCache cache;
        List<(LogType type, string message)> log;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "RemoteConfigTests-" + Guid.NewGuid().ToString("N"));
            cache = new ConfigCache(folder);
            log = new List<(LogType, string)>();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        LoadOutcome Resolve(DownloadResult download)
        {
            var cached = cache.Load(out string problem);
            return ConfigResolver.Resolve(Url, ConfigFormat.Auto, download, cached, problem, cache, WeaponDefaults.All,
                (type, message) => log.Add((type, message)));
        }

        void SaveCopy(string content) =>
            Assert.That(cache.Save(new CachedConfig { sourceUrl = Url, format = "Csv", etag = "\"v1\"", savedAtUtc = "earlier", content = content },
                out _), Is.True);

        [Test]
        public void ValidRemoteConfig_IsAppliedAndSavedAsLocalCopy()
        {
            var outcome = Resolve(DownloadResult.Success(Good, 200, "\"v2\"", "Thu, 05 Nov 2026 12:00:00 GMT"));

            Assert.That(outcome.Source, Is.EqualTo(ConfigSource.Remote));
            Assert.That(outcome.Weapons[0].damage, Is.EqualTo(12));
            var saved = cache.Load(out _);
            Assert.That(saved.content, Is.EqualTo(Good));
            Assert.That(saved.sourceUrl, Is.EqualTo(Url));
            Assert.That(saved.etag, Is.EqualTo("\"v2\""));
            Assert.That(log, Has.None.Matches<(LogType type, string message)>(entry => entry.type == LogType.Error));
        }

        [Test]
        public void DownloadFailure_UsesLocalCopy()
        {
            SaveCopy(Older);

            var outcome = Resolve(DownloadResult.Failure("ConnectionError: Cannot resolve destination host"));

            Assert.That(outcome.Source, Is.EqualTo(ConfigSource.LocalCopy));
            Assert.That(outcome.Weapons[0].damage, Is.EqualTo(11));
            Assert.That(log, Has.Some.Matches<(LogType type, string message)>(entry =>
                entry.type == LogType.Warning && entry.message.Contains("Cannot resolve destination host")));
        }

        [Test]
        public void InvalidRemoteConfig_UsesLocalCopy_AndDoesNotOverwriteIt()
        {
            SaveCopy(Older);

            var outcome = Resolve(DownloadResult.Success("id,damage,cooldown\npistol,-5,0\n"));

            Assert.That(outcome.Source, Is.EqualTo(ConfigSource.LocalCopy));
            Assert.That(outcome.Summary, Does.Contain("damage must be >= 0"));
            Assert.That(cache.Load(out _).content, Is.EqualTo(Older));
        }

        [Test]
        public void NoServerAndNoLocalCopy_UsesDefaults_AndLogsAnError()
        {
            var outcome = Resolve(DownloadResult.Failure("HTTP/1.1 404 Not Found", 404));

            Assert.That(outcome.Source, Is.EqualTo(ConfigSource.Defaults));
            Assert.That(outcome.Weapons, Has.Count.EqualTo(WeaponDefaults.All.Count));
            Assert.That(outcome.Weapons[0].damage, Is.EqualTo(WeaponDefaults.All[0].damage));
            Assert.That(log, Has.Some.Matches<(LogType type, string message)>(entry =>
                entry.type == LogType.Error && entry.message.Contains("404") && entry.message.Contains("no local copy")));
        }

        [Test]
        public void DamagedLocalCopy_FallsBackToDefaults()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(cache.FilePath, "{ this is not json");

            var outcome = Resolve(DownloadResult.Failure("Request timeout"));

            Assert.That(outcome.Source, Is.EqualTo(ConfigSource.Defaults));
            Assert.That(log, Has.Some.Matches<(LogType type, string message)>(entry =>
                entry.type == LogType.Error && entry.message.Contains("local copy")));
        }

        [Test]
        public void NotModified_UsesLocalCopyAsServerValues()
        {
            SaveCopy(Good);

            var outcome = Resolve(DownloadResult.Unchanged());

            Assert.That(outcome.Source, Is.EqualTo(ConfigSource.Remote));
            Assert.That(outcome.Summary, Does.Contain("304"));
            Assert.That(outcome.Weapons[1].id, Is.EqualTo("rifle"));
        }

        [Test]
        public void Defaults_AreValidAndDistinct()
        {
            var ids = new HashSet<string>();
            foreach (var weapon in WeaponDefaults.All)
            {
                Assert.That(WeaponConfigValidator.Problems(weapon), Is.Empty, weapon.ToString());
                Assert.That(ids.Add(weapon.id), Is.True, $"duplicate default id {weapon.id}");
            }
        }

        [Test]
        public void PublishedConfigFiles_AreValid()
        {
            // Те же файлы, что раздаёт GitHub Pages (docs/config): битый конфиг не дойдёт до публикации незамеченным.
            string docs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "config"));
            var csv = WeaponConfigParser.Parse(File.ReadAllText(Path.Combine(docs, "weapons.csv")));
            var json = WeaponConfigParser.Parse(File.ReadAllText(Path.Combine(docs, "weapons.json")));

            Assert.That(csv.Errors, Is.Empty);
            Assert.That(json.Errors, Is.Empty);
            Assert.That(json.Weapons, Has.Count.EqualTo(csv.Weapons.Count));
            for (int i = 0; i < csv.Weapons.Count; i++)
                Assert.That(json.Weapons[i].ToString(), Is.EqualTo(csv.Weapons[i].ToString()));
        }
    }
}
