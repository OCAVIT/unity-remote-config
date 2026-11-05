using System;
using UnityEngine;

namespace RemoteConfigPractice
{
    // Простое оружие, к которому RemoteConfigLoader применяет параметры из конфига. Поля открыты: в Play Mode их видно
    // и можно менять в инспекторе, смотреть в отладчике (точка останова в Apply) и вывести в консоль (меню компонента).
    // Чтобы значения было видно в деле, оружие «стреляет» раз в cooldown секунд: вспышка и счётчики shots/totalDamage.
    public class Weapon : MonoBehaviour
    {
        [Tooltip("Ключ строки в конфиге (колонка id)")]
        public string id = "pistol";

        [Header("Параметры из конфига")]
        public int damage;
        public float cooldown;
        [Tooltip("Откуда взяты значения: сервер, локальная копия или дефолты")]
        public ConfigSource source;

        [Header("Статистика стрельбы")]
        public int shots;
        public long totalDamage;

        public float DamagePerSecond => cooldown > 0f ? damage / cooldown : 0f;

        float timer, flash;
        Vector3 baseScale;
        Renderer body;
        Color baseColor = Color.gray;
        MaterialPropertyBlock block;

        void Awake()
        {
            baseScale = transform.localScale;
            body = GetComponent<Renderer>();
            if (body != null && body.sharedMaterial != null) baseColor = body.sharedMaterial.color;
            block = new MaterialPropertyBlock();

            // Пока загрузчик не ответил, у оружия уже есть рабочие значения - дефолты по id.
            var defaults = WeaponDefaults.Find(id);
            if (defaults != null)
            {
                damage = defaults.damage;
                cooldown = defaults.cooldown;
                source = ConfigSource.NotLoaded;
            }
        }

        public void Apply(WeaponConfig config, ConfigSource from)
        {
            damage = config.damage;
            cooldown = config.cooldown;
            source = from;
            timer = Mathf.Min(timer, cooldown);
            Debug.Log($"[Weapon] {this}", this);
        }

        void Update()
        {
            if (cooldown > 0f)
            {
                timer += Time.deltaTime;
                if (timer >= cooldown)
                {
                    // При очень маленьком cooldown за кадр бывает несколько выстрелов: счётчики честные, вспышка одна.
                    int fired = Mathf.FloorToInt(timer / cooldown);
                    timer -= fired * cooldown;
                    shots += fired;
                    totalDamage += (long)fired * damage;
                    flash = 1f;
                }
            }

            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 5f);
            transform.localScale = baseScale * (1f + 0.18f * flash);
            transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
            if (body != null)
            {
                block.SetColor("_Color", Color.Lerp(baseColor, Color.white, 0.65f * flash));
                body.SetPropertyBlock(block);
            }
        }

        [ContextMenu("Print values")]
        void Print() => Debug.Log($"[Weapon] {this}; shots {shots}, total damage {totalDamage}", this);

        public override string ToString() =>
            FormattableString.Invariant($"{id}: damage {damage}, cooldown {cooldown:0.###} s, DPS {DamagePerSecond:0.#} ({source})");
    }
}
