using System.Globalization;
using System.Xml.Serialization;
using System.Resources;
using System.Collections;
using System.Text.RegularExpressions;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;
using DS4WinWPF.DS4Forms.Themes;

namespace DS4WindowsTests;

[TestClass]
public class AppThemeAndLanguageTests
{
    [TestMethod]
    public void BuiltApplicationIdentifiesTheCurrentForkAndRetainsOriginalCredits()
    {
        var assembly = typeof(AppThemeCatalog).Assembly;
        var details = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
        Assert.AreEqual("Pav-Osmolski", details.CompanyName);
        Assert.AreEqual("DS4Windows", details.ProductName);
        StringAssert.Contains(details.Comments, "Pav-Osmolski fork");
        StringAssert.Contains(details.LegalCopyright, "Ryochan7");
        Assert.IsFalse(details.LegalCopyright.Contains("\u00c2\u00a9"));
    }

    [TestMethod]
    public void EveryCompiledLanguageContainsBothCompleteResourceFamilies()
    {
        string[] cultures = "ar cs de el es fi fr he hu-HU id it ja ms nl pl pt pt-BR ru sv tr uk-UA vi zh-Hans zh-Hant".Split(' ');
        foreach (string family in new[] { "DS4WinWPF.Translations.Strings", "DS4WinWPF.Properties.Resources" })
        {
            var manager = new ResourceManager(family, typeof(AppThemeCatalog).Assembly);
            var neutral = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
            foreach (string culture in cultures)
            {
                var translated = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false);
                Assert.IsNotNull(translated, family + ":" + culture + " must be packaged without falling back.");
                foreach (DictionaryEntry entry in neutral)
                {
                    if (entry.Value is not string source) continue;
                    string value = translated.GetString((string)entry.Key);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(value), family + ":" + culture + ":" + entry.Key);
                    var slots = Regex.Matches(source, @"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})").Cast<Match>().ToArray();
                    if (slots.Length == 0) continue;
                    int maximum = slots.Max(slot => int.Parse(slot.Groups[1].Value));
                    var arguments = Enumerable.Repeat<object>(new AnyFormat(), maximum + 1).ToArray();
                    try { _ = string.Format(CultureInfo.GetCultureInfo(culture), value, arguments); }
                    catch (FormatException error) { Assert.Fail(family + ":" + culture + ":" + entry.Key + " " + error.Message); }
                }
            }
        }
    }

    private sealed class AnyFormat : IFormattable
    {
        public string ToString(string format, IFormatProvider provider) => "1";
    }

    [TestMethod]
    public void ExistingThemeIdsRemainCompatibleAndSystemContrastTakesPriority()
    {
        Assert.AreEqual(0u, (uint)AppThemeChoice.Default);
        Assert.AreEqual(1u, (uint)AppThemeChoice.Light);
        Assert.AreEqual(2u, (uint)AppThemeChoice.Dark);
        Assert.AreEqual(AppThemeChoice.Default, AppThemeCatalog.Normalize((AppThemeChoice)999));
        Assert.AreEqual(AppThemeChoice.Light, AppThemeCatalog.Resolve(AppThemeChoice.Default, false, false));
        Assert.AreEqual(AppThemeChoice.Dark, AppThemeCatalog.Resolve(AppThemeChoice.Default, true, false));
        foreach (var choice in Enum.GetValues<AppThemeChoice>())
            Assert.AreEqual(AppThemeChoice.HighContrast, AppThemeCatalog.Resolve(choice, false, true));
    }

    [TestMethod]
    public void ThemeAccentAndLegacyLanguageRoundTripThroughSettingsXml()
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        foreach (var theme in Enum.GetValues<AppThemeChoice>())
        foreach (string accent in new[] { "Theme", "Windows", "#abcdef" })
        {
            var original = new AppSettingsDTO { AppTheme = theme, AppAccent = accent, UseLang = "se" };
            using var writer = new StringWriter();
            serializer.Serialize(writer, original);
            using var reader = new StringReader(writer.ToString());
            var saved = (AppSettingsDTO)serializer.Deserialize(reader);
            Assert.AreEqual(theme, saved.AppTheme);
            Assert.AreEqual(accent.StartsWith('#') ? accent.ToUpperInvariant() : accent, saved.AppAccent);
            Assert.AreEqual("sv", saved.UseLang);
        }
        var dto = new AppSettingsDTO { UseLang = "idn", AppThemeString = "999", AppAccent = "invalid" };
        Assert.AreEqual("id", dto.UseLang);
        Assert.AreEqual(AppThemeChoice.Default, dto.AppTheme);
        Assert.AreEqual("Theme", dto.AppAccent);
    }

    [TestMethod]
    public void LanguageNamesUseAnInitialCapitalWithoutChangingNativeSpelling()
    {
        Assert.AreEqual("Polski", LanguagePackPolicy.DisplayName(CultureInfo.GetCultureInfo("pl")));
        Assert.AreEqual("Bahasa Indonesia", LanguagePackPolicy.DisplayName(CultureInfo.GetCultureInfo("id")));
        Assert.AreEqual("sv", LanguagePackPolicy.AvailableCulture("se", new[] { "sv", "id" }));
        Assert.AreEqual("id", LanguagePackPolicy.AvailableCulture("idn", new[] { "sv", "id" }));
        Assert.AreEqual("pl", LanguagePackPolicy.AvailableCulture("pl-PL", new[] { "pl" }));
        Assert.AreEqual("", LanguagePackPolicy.AvailableCulture("not-a-language", new[] { "pl" }));
    }
}
