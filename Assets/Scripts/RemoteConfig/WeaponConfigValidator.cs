using System.Collections.Generic;
using System.Globalization;

namespace RemoteConfigPractice
{
    // Проверка значений одного оружия: id не пустой, damage >= 0, cooldown > 0 (и это конечное число).
    public static class WeaponConfigValidator
    {
        public static List<string> Problems(WeaponConfig weapon)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(weapon.id))
                problems.Add("id is empty");
            if (weapon.damage < 0)
                problems.Add($"damage must be >= 0 (got {weapon.damage})");
            if (!float.IsFinite(weapon.cooldown) || weapon.cooldown <= 0f)
                problems.Add($"cooldown must be > 0 (got {weapon.cooldown.ToString(CultureInfo.InvariantCulture)})");
            return problems;
        }

        public static bool IsValid(WeaponConfig weapon) => Problems(weapon).Count == 0;
    }
}
