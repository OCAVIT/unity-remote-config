using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RemoteConfigPractice
{
    public enum ConfigFormat
    {
        Auto, // по содержимому: начинается с { или [ - JSON, иначе CSV
        Csv,
        Json
    }

    // Результат разбора. Конфиг годен, только если в нём нет ни одной ошибки: частично битый конфиг не применяется
    // и не сохраняется, чтобы не затереть последнюю рабочую локальную копию.
    public sealed class ParsedConfig
    {
        public ConfigFormat Format;
        public readonly List<WeaponConfig> Weapons = new List<WeaponConfig>();
        public readonly List<string> Errors = new List<string>();

        public bool IsValid => Errors.Count == 0;
        public string ErrorSummary => string.Join("; ", Errors);
    }

    // Разбор конфига оружия из CSV (Google Sheets → «Опубликовать в интернете» → CSV) или из JSON
    // ({"schema_version": 1, "weapons": [...]}) и валидация каждой записи.
    public static class WeaponConfigParser
    {
        public const int SchemaVersion = 1;
        static readonly string[] Columns = { "id", "damage", "cooldown" };

        public static ConfigFormat Detect(string text)
        {
            foreach (char c in text ?? "")
            {
                if (char.IsWhiteSpace(c) || c == '﻿') continue;
                return c == '{' || c == '[' ? ConfigFormat.Json : ConfigFormat.Csv;
            }
            return ConfigFormat.Csv;
        }

        public static ParsedConfig Parse(string text, ConfigFormat format = ConfigFormat.Auto)
        {
            var result = new ParsedConfig();
            text = (text ?? "").TrimStart('﻿');
            result.Format = format == ConfigFormat.Auto ? Detect(text) : format;
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Errors.Add("config is empty");
                return result;
            }
            if (text.TrimStart().StartsWith("<"))
            {
                // Так выглядит, например, страница входа Google, если таблица не опубликована, или страница ошибки хостинга.
                result.Errors.Add("got an HTML page instead of CSV/JSON (is the file published at this URL?)");
                return result;
            }

            if (result.Format == ConfigFormat.Json) ReadJson(text, result);
            else ReadCsv(text, result);

            if (result.Errors.Count == 0 && result.Weapons.Count == 0)
                result.Errors.Add("config has no weapons");
            return result;
        }

        // ---------- CSV ----------

        static void ReadCsv(string text, ParsedConfig result)
        {
            var rows = CsvRows(text, out string syntaxError);
            if (syntaxError != null)
            {
                result.Errors.Add(syntaxError);
                return;
            }

            int headerIndex = rows.FindIndex(row => !row.IsBlank);
            if (headerIndex < 0)
            {
                result.Errors.Add("CSV has no header row");
                return;
            }
            var header = rows[headerIndex];
            var names = header.Cells.Select(cell => cell.Trim().ToLowerInvariant()).ToList();
            var missing = Columns.Where(column => !names.Contains(column)).ToList();
            if (missing.Count > 0)
            {
                result.Errors.Add($"line {header.Line}: header has no column {string.Join(", ", missing)} " +
                                  $"(expected {string.Join(",", Columns)}, got \"{string.Join(",", header.Cells)}\")");
                return;
            }
            int idColumn = names.IndexOf("id"), damageColumn = names.IndexOf("damage"), cooldownColumn = names.IndexOf("cooldown");

            var places = new List<string>();
            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.IsBlank) continue;
                string id = row.Cell(idColumn).Trim();
                string place = id.Length > 0 ? $"line {row.Line} ({id})" : $"line {row.Line}";
                var problems = new List<string>();

                string damageText = row.Cell(damageColumn).Trim();
                if (!TryParseInt(damageText, out int damage))
                    problems.Add(damageText.Length == 0 ? "damage is empty" : $"damage \"{damageText}\" is not a whole number");
                string cooldownText = row.Cell(cooldownColumn).Trim();
                if (!TryParseFloat(cooldownText, out float cooldown))
                    problems.Add(cooldownText.Length == 0 ? "cooldown is empty" : $"cooldown \"{cooldownText}\" is not a number");

                var weapon = new WeaponConfig(id, damage, cooldown);
                if (problems.Count == 0) problems.AddRange(WeaponConfigValidator.Problems(weapon));
                Add(result, weapon, place, problems, places);
            }
        }

        internal sealed class CsvRow
        {
            public readonly int Line;
            public readonly List<string> Cells;

            public CsvRow(int line, List<string> cells)
            {
                Line = line;
                Cells = cells;
            }

            public bool IsBlank => Cells.All(string.IsNullOrWhiteSpace);
            public string Cell(int index) => index < Cells.Count ? Cells[index] : "";
        }

        // Строки CSV по RFC 4180: поля в кавычках могут содержать запятые, переводы строк и "" (кавычку).
        // Переводы строк: \n, \r\n и \r. Line - номер строки файла, с которой начинается запись (для сообщений об ошибках).
        internal static List<CsvRow> CsvRows(string text, out string error)
        {
            var rows = new List<CsvRow>();
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            int line = 1, rowLine = 1, quoteLine = 0;
            error = null;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else quoted = false;
                    }
                    else
                    {
                        if (c == '\n') line++;
                        cell.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        quoted = true;
                        quoteLine = line;
                        break;
                    case ',':
                        cells.Add(cell.ToString());
                        cell.Clear();
                        break;
                    case '\r':
                    case '\n':
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                        cells.Add(cell.ToString());
                        cell.Clear();
                        rows.Add(new CsvRow(rowLine, cells));
                        cells = new List<string>();
                        line++;
                        rowLine = line;
                        break;
                    default:
                        cell.Append(c);
                        break;
                }
            }

            if (quoted)
            {
                error = $"line {quoteLine}: quote is not closed";
                return rows;
            }
            if (cell.Length > 0 || cells.Count > 0)
            {
                cells.Add(cell.ToString());
                rows.Add(new CsvRow(rowLine, cells));
            }
            return rows;
        }

        static bool TryParseInt(string text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        static bool TryParseFloat(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            // Таблица с русской локалью выгружает дробные числа через запятую: "0,5" (в CSV такое поле стоит в кавычках).
            return text.IndexOf(',') >= 0 && text.IndexOf('.') < 0 &&
                   float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // ---------- JSON ----------

        static void ReadJson(string text, ParsedConfig result)
        {
            WeaponConfigList list;
            try
            {
                list = JsonUtility.FromJson<WeaponConfigList>(text);
            }
            catch (Exception e)
            {
                result.Errors.Add($"invalid JSON: {e.Message}");
                return;
            }
            if (list == null)
            {
                result.Errors.Add("invalid JSON");
                return;
            }
            if (list.schema_version == WeaponConfigList.NoVersion)
            {
                result.Errors.Add("schema_version is missing");
                return;
            }
            if (list.schema_version != SchemaVersion)
            {
                // Конфиг для другой версии клиента: значения могут значить не то же самое, поэтому не применяем.
                result.Errors.Add($"schema_version {list.schema_version} is not supported (this build reads {SchemaVersion})");
                return;
            }
            if (list.weapons == null)
            {
                result.Errors.Add("no \"weapons\" array");
                return;
            }

            var places = new List<string>();
            for (int i = 0; i < list.weapons.Length; i++)
            {
                var weapon = list.weapons[i] ?? new WeaponConfig();
                string place = string.IsNullOrWhiteSpace(weapon.id) ? $"weapons[{i}]" : $"weapons[{i}] ({weapon.id})";
                var problems = new List<string>();
                if (weapon.damage == WeaponConfig.NoDamage) problems.Add("damage is missing");
                if (float.IsNaN(weapon.cooldown)) problems.Add("cooldown is missing");
                if (problems.Count == 0) problems.AddRange(WeaponConfigValidator.Problems(weapon));
                else if (string.IsNullOrWhiteSpace(weapon.id)) problems.Insert(0, "id is empty");
                weapon.id = weapon.id?.Trim();
                Add(result, weapon, place, problems, places);
            }
        }

        // ---------- общее ----------

        static void Add(ParsedConfig result, WeaponConfig weapon, string place, List<string> problems, List<string> places)
        {
            if (problems.Count == 0)
            {
                int same = result.Weapons.FindIndex(w => string.Equals(w.id, weapon.id, StringComparison.OrdinalIgnoreCase));
                if (same >= 0) problems.Add($"id \"{weapon.id}\" is already used at {places[same]}");
            }
            foreach (var problem in problems)
                result.Errors.Add($"{place}: {problem}");
            if (problems.Count > 0) return;
            result.Weapons.Add(weapon);
            places.Add(place);
        }
    }
}
