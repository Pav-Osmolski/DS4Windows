using System.Globalization;
using System.Resources;
using DS4WinWPF.DS4Forms.Themes;
using System.Xml.Linq;

namespace DS4WindowsTests;

// Source-layout tests still assert the actual wording after labels move to resx.
internal static class LocalizedSource
{
    internal static XDocument Load(string path)
    {
        var document = XDocument.Load(path);
        foreach (var attribute in document.Descendants().Attributes())
            if (attribute.Value.StartsWith("{lex:Loc Strings:UI_")) attribute.Value = Resolve(attribute.Value);
        return document;
    }

    // Isolated source-layout tests create disposable STA threads. Resolve text
    // before parsing so a global localization provider never captures a dead
    // test dispatcher. The compiled-control tests exercise the actual provider.
    internal static string Markup(XElement element)
    {
        foreach (var attribute in element.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attribute.Name.NamespaceName == "http://wpflocalizeextension.codeplex.com") attribute.Remove();
            else if (attribute.Value.StartsWith("{lex:Loc ")) attribute.Value = Resolve(attribute.Value);
        }
        return element.ToString();
    }

    internal static string Resolve(string value)
    {
        if (value?.StartsWith("{lex:Loc ") != true) return value;
        string key = value[9..^1];
        string family = "Strings";
        bool explicitFamily = key.Contains(':');
        if (explicitFamily) { var parts = key.Split(':', 2); family = parts[0]; key = parts[1]; }
        var assembly = typeof(AppThemeCatalog).Assembly;
        var resources = new ResourceManager(family == "Resources" ? "DS4WinWPF.Properties.Resources" : "DS4WinWPF.Translations.Strings", assembly);
        string resolved = resources.GetString(key, CultureInfo.InvariantCulture);
        if (resolved == null && !explicitFamily)
            resolved = new ResourceManager("DS4WinWPF.Properties.Resources", assembly).GetString(key, CultureInfo.InvariantCulture);
        Assert.IsNotNull(resolved, "Missing neutral resource " + family + ":" + key);
        return resolved;
    }
}
