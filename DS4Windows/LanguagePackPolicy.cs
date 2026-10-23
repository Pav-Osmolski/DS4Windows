using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace DS4Windows;

public static class LanguagePackPolicy
{
    public static string Normalize(string language)
    {
        language = (language ?? "").Trim();
        if (language.Equals("se", StringComparison.OrdinalIgnoreCase)) return "sv";
        if (language.Equals("idn", StringComparison.OrdinalIgnoreCase)) return "id";
        try { return CultureInfo.GetCultureInfo(language).Name; }
        catch (CultureNotFoundException) { return ""; }
    }

    public static string DisplayName(CultureInfo culture)
    {
        if (culture.Name == "id") return "Bahasa Indonesia";
        string name = culture.NativeName;
        if (name.Length == 0) return name;
        string first = StringInfo.GetNextTextElement(name);
        return culture.TextInfo.ToUpper(first) + name[first.Length..];
    }

    public static string AvailableCulture(string requested, IEnumerable<string> available)
    {
        var names = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var culture = CultureInfo.GetCultureInfo(Normalize(requested)); culture.Name.Length > 0; culture = culture.Parent)
            if (names.Contains(culture.Name)) return culture.Name;
        return "";
    }
}
