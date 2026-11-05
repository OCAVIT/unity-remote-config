using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RemoteConfigPractice
{
    // Экран демо-сцены: откуда пришёл конфиг, значения каждого Weapon, лог загрузчика и кнопки для проверки
    // сценариев ошибок (Reload, имитация отсутствия сети, удаление локальной копии).
    // Ключи командной строки билда: -configUrl <url>, -offline, -clearConfigCache,
    // -screenshot <файл> (кадр после загрузки конфига, затем выход).
    public class ConfigDebugPanel : MonoBehaviour
    {
        const float Height = 720f;
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public RemoteConfigLoader loader;

        Weapon[] weapons = Array.Empty<Weapon>();
        string screenshot, copyInfo = "none";
        GUIStyle panel, title, label, small, cell, head, badge, button;
        Camera view;

        void Awake()
        {
            if (loader == null) loader = FindAnyObjectByType<RemoteConfigLoader>();
            var args = Environment.GetCommandLineArgs();
            string Value(string key)
            {
                int index = Array.IndexOf(args, key);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }

            if (Value("-configUrl") is string url) loader.url = url;
            if (args.Contains("-offline")) loader.simulateOffline = true;
            if (args.Contains("-clearConfigCache")) loader.DeleteLocalCopy();
            screenshot = Value("-screenshot");
            loader.Loaded += _ => RefreshCopyInfo();
        }

        IEnumerator Start()
        {
            weapons = FindObjectsByType<Weapon>(FindObjectsSortMode.None).OrderBy(weapon => weapon.transform.position.x).ToArray();
            view = Camera.main;
            RefreshCopyInfo();
            if (string.IsNullOrEmpty(screenshot)) yield break;

            float deadline = Time.realtimeSinceStartup + 60f;
            while ((loader.IsLoading || loader.Source == ConfigSource.NotLoaded) && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return new WaitForSeconds(2.5f);
            ScreenCapture.CaptureScreenshot(screenshot);
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        void RefreshCopyInfo()
        {
            var cached = loader.Cache.Load(out _);
            copyInfo = cached == null
                ? "none"
                : $"{loader.Cache.FilePath.Replace('\\', '/')}  (saved {cached.savedAtUtc})";
        }

        void OnGUI()
        {
            Styles();
            float scale = Screen.height / Height;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;

            WeaponLabels(scale);

            var area = new Rect(16f, 262f, width - 32f, Height - 262f - 16f);
            GUILayout.BeginArea(area, panel);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Remote Config: weapons", title);
            GUILayout.FlexibleSpace();
            GUI.enabled = !loader.IsLoading;
            if (GUILayout.Button("Reload", button, GUILayout.Width(110f))) loader.Reload();
            if (GUILayout.Button(loader.simulateOffline ? "Offline: ON" : "Offline: OFF", button, GUILayout.Width(130f)))
            {
                loader.simulateOffline = !loader.simulateOffline;
                loader.Reload();
            }
            if (GUILayout.Button("Delete local copy", button, GUILayout.Width(170f)))
            {
                loader.DeleteLocalCopy();
                RefreshCopyInfo();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            string state = loader.IsLoading ? "LOADING…" : SourceName(loader.Source);
            GUILayout.Label($"Source: <b><color={SourceColor(loader.IsLoading ? ConfigSource.NotLoaded : loader.Source)}>{state}</color></b>" +
                            $"   {loader.Summary}", label);
            GUILayout.Label($"URL: {(loader.simulateOffline ? RemoteConfigLoader.OfflineUrl + "  (simulated offline)" : loader.url)}", small);
            GUILayout.Label($"Local copy: {copyInfo}", small);

            GUILayout.Space(8f);
            Row(head, "weapon", "damage", "cooldown, s", "DPS", "shots", "values from");
            foreach (var weapon in weapons)
            {
                if (weapon == null) continue;
                Row(cell, weapon.id, weapon.damage.ToString(Invariant), weapon.cooldown.ToString("0.00", Invariant),
                    weapon.DamagePerSecond.ToString("0.0", Invariant), weapon.shots.ToString(Invariant),
                    $"<color={SourceColor(weapon.source)}>{SourceName(weapon.source)}</color>");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Log", head);
            int visible = Mathf.Max(1, Mathf.FloorToInt((area.height - 290f) / 26f));
            foreach (var entry in loader.History.Skip(Math.Max(0, loader.History.Count - visible)))
            {
                string color = entry.Type == LogType.Log ? "#AEB7C4" : entry.Type == LogType.Warning ? "#F5C04A" : "#FF7A7A";
                GUILayout.Label($"<color=#6F7A8A>{entry.Time:HH:mm:ss}</color>  <color={color}>{entry.Message}</color>", small);
            }
            GUILayout.EndArea();
        }

        void WeaponLabels(float scale)
        {
            if (view == null) return;
            foreach (var weapon in weapons)
            {
                if (weapon == null) continue;
                Vector3 point = view.WorldToScreenPoint(weapon.transform.position + Vector3.up * 1.05f);
                if (point.z <= 0f) continue;
                var rect = new Rect(point.x / scale - 110f, (Screen.height - point.y) / scale - 52f, 220f, 50f);
                GUI.Label(rect, FormattableString.Invariant($"<b>{weapon.id}</b>\n{weapon.damage} dmg · {weapon.cooldown:0.00} s"), badge);
            }
        }

        void Row(GUIStyle style, string id, string damage, string cooldown, string dps, string shots, string from)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(id, style, GUILayout.Width(150f));
            GUILayout.Label(damage, style, GUILayout.Width(110f));
            GUILayout.Label(cooldown, style, GUILayout.Width(130f));
            GUILayout.Label(dps, style, GUILayout.Width(110f));
            GUILayout.Label(shots, style, GUILayout.Width(110f));
            GUILayout.Label(from, style);
            GUILayout.EndHorizontal();
        }

        static string SourceName(ConfigSource source) => source switch
        {
            ConfigSource.Remote => "REMOTE",
            ConfigSource.LocalCopy => "LOCAL COPY",
            ConfigSource.Defaults => "DEFAULTS",
            _ => "NOT LOADED"
        };

        static string SourceColor(ConfigSource source) => source switch
        {
            ConfigSource.Remote => "#5BD38A",
            ConfigSource.LocalCopy => "#F5C04A",
            ConfigSource.Defaults => "#FF7A7A",
            _ => "#9AA4B2"
        };

        void Styles()
        {
            if (panel != null) return;
            var background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.12f, 0.9f));
            background.Apply();
            var text = new Color(0.9f, 0.92f, 0.95f);
            panel = new GUIStyle { normal = { background = background }, padding = new RectOffset(18, 18, 14, 14) };
            title = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true, normal = { textColor = text } };
            small = new GUIStyle(label) { fontSize = 14, wordWrap = false, clipping = TextClipping.Clip };
            cell = new GUIStyle(label) { fontSize = 16, wordWrap = false };
            head = new GUIStyle(cell) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.6f, 0.65f, 0.72f) } };
            badge = new GUIStyle(label) { alignment = TextAnchor.LowerCenter, fontSize = 15, normal = { textColor = Color.white } };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 30f };
        }
    }
}
