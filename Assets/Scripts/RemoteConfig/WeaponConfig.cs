using System;
using System.Globalization;

namespace RemoteConfigPractice
{
    // Параметры одного оружия из удалённого конфига: строка CSV (id,damage,cooldown) или элемент массива weapons в JSON.
    [Serializable]
    public class WeaponConfig
    {
        // «Поле не задано». JsonUtility не трогает поля, которых нет в JSON, поэтому по этим значениям видно пропуск
        // (иначе отсутствующий damage молча стал бы нулём и прошёл бы проверку damage >= 0).
        public const int NoDamage = int.MinValue;

        public string id;
        public int damage = NoDamage;
        public float cooldown = float.NaN;

        public WeaponConfig() { }

        public WeaponConfig(string id, int damage, float cooldown)
        {
            this.id = id;
            this.damage = damage;
            this.cooldown = cooldown;
        }

        public WeaponConfig Clone() => new WeaponConfig(id, damage, cooldown);

        public override string ToString() =>
            $"{id}: damage {damage}, cooldown {cooldown.ToString("0.###", CultureInfo.InvariantCulture)} s";
    }

    // Корень JSON-конфига. JsonUtility не читает массив верхнего уровня, поэтому оружие лежит в поле weapons.
    [Serializable]
    public class WeaponConfigList
    {
        public const int NoVersion = -1;

        public int schema_version = NoVersion;
        public WeaponConfig[] weapons;
    }
}
