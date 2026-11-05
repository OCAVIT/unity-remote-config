using System.Collections.Generic;

namespace RemoteConfigPractice
{
    // Заранее прописанные значения. Применяются, если нет ни сервера, ни локальной копии, а также для оружия,
    // которого нет в загруженном конфиге. Специально отличаются от серверных, чтобы по экрану было видно источник.
    public static class WeaponDefaults
    {
        public static readonly IReadOnlyList<WeaponConfig> All = new[]
        {
            new WeaponConfig("pistol", 10, 0.5f),
            new WeaponConfig("rifle", 25, 0.2f),
            new WeaponConfig("shotgun", 30, 1f),
            new WeaponConfig("sniper", 90, 2f)
        };

        public static WeaponConfig Find(string id) => ConfigResolver.Find(All, id);
    }
}
