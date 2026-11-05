using NUnit.Framework;

namespace RemoteConfigPractice.Tests
{
    public class WeaponConfigParserTests
    {
        [Test]
        public void Csv_ReadsAllRows()
        {
            var config = WeaponConfigParser.Parse("id,damage,cooldown\npistol,12,0.4\nrifle,22,0.15\n");

            Assert.That(config.Errors, Is.Empty);
            Assert.That(config.Format, Is.EqualTo(ConfigFormat.Csv));
            Assert.That(config.Weapons, Has.Count.EqualTo(2));
            Assert.That(config.Weapons[0].id, Is.EqualTo("pistol"));
            Assert.That(config.Weapons[0].damage, Is.EqualTo(12));
            Assert.That(config.Weapons[0].cooldown, Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(config.Weapons[1].cooldown, Is.EqualTo(0.15f).Within(1e-6f));
        }

        [Test]
        public void Csv_HandlesBomCrlfQuotesColumnOrderAndExtraColumns()
        {
            // Как выгружает таблица: BOM, \r\n, колонки в другом порядке, лишняя колонка, поле с запятой и кавычками,
            // дробное число через запятую (русская локаль) и пустые строки.
            string csv = "﻿Cooldown , ID,Damage,note\r\n" +
                         "\"0,5\",pistol,10,\"fast, \"\"light\"\"\"\r\n" +
                         "\r\n" +
                         "1.2,shotgun,40,\r\n";

            var config = WeaponConfigParser.Parse(csv);

            Assert.That(config.Errors, Is.Empty);
            Assert.That(config.Weapons, Has.Count.EqualTo(2));
            Assert.That(config.Weapons[0].id, Is.EqualTo("pistol"));
            Assert.That(config.Weapons[0].cooldown, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(config.Weapons[1].damage, Is.EqualTo(40));
            Assert.That(config.Weapons[1].cooldown, Is.EqualTo(1.2f).Within(1e-6f));
        }

        [Test]
        public void Csv_ValidationErrorsNameTheLine()
        {
            string csv = "id,damage,cooldown\n" +
                         "pistol,-1,0.5\n" +
                         "rifle,10,0\n" +
                         "shotgun,abc,1\n" +
                         ",5,1\n" +
                         "sniper,90,\n";

            var config = WeaponConfigParser.Parse(csv);

            Assert.That(config.IsValid, Is.False);
            Assert.That(config.Errors, Has.Count.EqualTo(5));
            Assert.That(config.Errors[0], Does.StartWith("line 2 (pistol)").And.Contain("damage must be >= 0"));
            Assert.That(config.Errors[1], Does.StartWith("line 3 (rifle)").And.Contain("cooldown must be > 0"));
            Assert.That(config.Errors[2], Does.StartWith("line 4 (shotgun)").And.Contain("not a whole number"));
            Assert.That(config.Errors[3], Does.StartWith("line 5").And.Contain("id is empty"));
            Assert.That(config.Errors[4], Does.StartWith("line 6 (sniper)").And.Contain("cooldown is empty"));
        }

        [Test]
        public void Csv_ZeroDamageIsAllowed_NegativeOrNonFiniteCooldownIsNot()
        {
            Assert.That(WeaponConfigParser.Parse("id,damage,cooldown\nfeather,0,0.1").IsValid, Is.True);
            Assert.That(WeaponConfigParser.Parse("id,damage,cooldown\nbroken,5,-0.5").IsValid, Is.False);
            Assert.That(WeaponConfigParser.Parse("id,damage,cooldown\nbroken,5,Infinity").IsValid, Is.False);
            Assert.That(WeaponConfigParser.Parse("id,damage,cooldown\nbroken,5,NaN").IsValid, Is.False);
        }

        [Test]
        public void Csv_MissingColumnIsAnError()
        {
            var config = WeaponConfigParser.Parse("id,damage\npistol,10\n");

            Assert.That(config.IsValid, Is.False);
            Assert.That(config.Errors[0], Does.Contain("no column cooldown"));
        }

        [Test]
        public void Csv_UnclosedQuoteIsAnError()
        {
            var config = WeaponConfigParser.Parse("id,damage,cooldown\n\"pistol,10,0.5\n");

            Assert.That(config.IsValid, Is.False);
            Assert.That(config.Errors[0], Does.Contain("line 2: quote is not closed"));
        }

        [Test]
        public void DuplicateIdIsAnError()
        {
            var config = WeaponConfigParser.Parse("id,damage,cooldown\npistol,10,0.5\nPistol,12,0.4\n");

            Assert.That(config.IsValid, Is.False);
            Assert.That(config.Errors[0], Does.Contain("already used at line 2"));
        }

        [Test]
        public void Json_ReadsSchemaVersion1()
        {
            string json = "{ \"schema_version\": 1, \"weapons\": [" +
                          "{ \"id\": \"pistol\", \"damage\": 10, \"cooldown\": 0.5 }," +
                          "{ \"id\": \"rifle\", \"damage\": 25, \"cooldown\": 0.2 } ] }";

            var config = WeaponConfigParser.Parse(json);

            Assert.That(config.Errors, Is.Empty);
            Assert.That(config.Format, Is.EqualTo(ConfigFormat.Json));
            Assert.That(config.Weapons, Has.Count.EqualTo(2));
            Assert.That(config.Weapons[1].id, Is.EqualTo("rifle"));
            Assert.That(config.Weapons[1].damage, Is.EqualTo(25));
            Assert.That(config.Weapons[1].cooldown, Is.EqualTo(0.2f).Within(1e-6f));
        }

        [Test]
        public void Json_MissingFieldsAreErrors_NotZeros()
        {
            string json = "{ \"schema_version\": 1, \"weapons\": [" +
                          "{ \"id\": \"pistol\", \"cooldown\": 0.5 }," +
                          "{ \"id\": \"rifle\", \"damage\": 25 } ] }";

            var config = WeaponConfigParser.Parse(json);

            Assert.That(config.Errors, Has.Count.EqualTo(2));
            Assert.That(config.Errors[0], Is.EqualTo("weapons[0] (pistol): damage is missing"));
            Assert.That(config.Errors[1], Is.EqualTo("weapons[1] (rifle): cooldown is missing"));
        }

        [Test]
        public void Json_UnsupportedOrMissingSchemaVersionIsRejected()
        {
            string weapons = "\"weapons\": [{ \"id\": \"pistol\", \"damage\": 10, \"cooldown\": 0.5 }]";

            var newer = WeaponConfigParser.Parse("{ \"schema_version\": 2, " + weapons + " }");
            var missing = WeaponConfigParser.Parse("{ " + weapons + " }");

            Assert.That(newer.Errors[0], Does.Contain("schema_version 2 is not supported"));
            Assert.That(missing.Errors[0], Is.EqualTo("schema_version is missing"));
        }

        [Test]
        public void Json_BrokenJsonIsAnError()
        {
            var config = WeaponConfigParser.Parse("{ \"schema_version\": 1, \"weapons\": [ { \"id\": ");

            Assert.That(config.IsValid, Is.False);
            Assert.That(config.Errors[0], Does.StartWith("invalid JSON"));
        }

        [Test]
        public void HtmlAndEmptyResponsesAreRejected()
        {
            Assert.That(WeaponConfigParser.Parse("<!DOCTYPE html><html><body>Sign in</body></html>").Errors[0],
                Does.Contain("HTML page"));
            Assert.That(WeaponConfigParser.Parse("  \n").Errors[0], Is.EqualTo("config is empty"));
            Assert.That(WeaponConfigParser.Parse("id,damage,cooldown\n").Errors[0], Is.EqualTo("config has no weapons"));
        }
    }
}
