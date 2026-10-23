using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Maintainer extraction tool. Uses syntax context rather than replacing every
// string: serialised values, wire identifiers, paths, formats and log records
// are not UI translations. The generated inventory is reviewed before use.
if (args.Length != 3 || args[0] is not ("scan" or "extract" or "catalog"))
    throw new ArgumentException("Usage: LocalizationTools scan|extract|catalog <repository> <inventory.json>");
string root = Path.GetFullPath(args[1]);
var inventory = new List<Occurrence>();
foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "DS4Windows"), "*.cs", SearchOption.AllDirectories))
{
    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
    if (relative.Contains("/obj/") || relative.Contains("/bin/") || relative.EndsWith(".Designer.cs")) continue;
    bool uiFile = relative.StartsWith("DS4Windows/DS4Forms/") && !relative.Contains("/Themes/") &&
        !relative.EndsWith("Artwork.cs") && !relative.EndsWith("ArtworkCatalog.cs");
    bool policyFile = Path.GetFileName(file) is "ControllerRuntimeStatus.cs" or "ControllerMicrophoneUiPolicy.cs" or
        "ControllerAudioUiPolicy.cs" or "BatteryAlertService.cs" or "BatteryAlertAudio.cs" or "BatteryAlertSettings.cs";
    string source = File.ReadAllText(file, Encoding.UTF8);
    var tree = CSharpSyntaxTree.ParseText(source);
    if (args[0] == "catalog")
    {
        foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string name = call.Expression.ToString();
            if (!(name.EndsWith("UiText.Get") || name.EndsWith("UiText.Format") || name.EndsWith("UiText.FileFilter"))) continue;
            if (call.ArgumentList.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax literal) continue;
            string text = literal.Token.ValueText;
            foreach (string phrase in name.EndsWith("FileFilter") ? text.Split('|').Where((_, index) => index % 2 == 0) : new[] { text })
                inventory.Add(new Occurrence(relative, literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1, phrase, "UI_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phrase)))[..16]));
        }
        continue;
    }
    var rewriter = new UiRewriter(relative, uiFile || policyFile, inventory);
    string changed = rewriter.Visit(tree.GetRoot())!.ToFullString();
    if (args[0] == "extract" && changed != source) File.WriteAllText(file, changed, new UTF8Encoding(false));
}
File.WriteAllText(args[2], JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
Console.WriteLine($"Reviewed {inventory.Count} UI source occurrences; {inventory.Select(x => x.Source).Distinct().Count()} phrases.");

record Occurrence(string File, int Line, string Source, string ResourceKey);

sealed class UiRewriter(string path, bool uiFile, List<Occurrence> inventory) : CSharpSyntaxRewriter
{
    private static readonly Regex Words = new("[A-Za-z]{3}", RegexOptions.CultureInvariant);
    private static readonly Regex UiName = new("Text|Title|Description|Label|Caption|Message|Hint|Tooltip|ToolTip|Summary|Status|DisplayName|FriendlyName|Warning|Help", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ShortWords = new(StringComparer.Ordinal)
    {
        "Apply", "Cancel", "Save", "Delete", "Remove", "Add", "Edit", "Duplicate", "Rename", "Refresh", "Reset", "Retry", "Browse", "Start", "Stop", "Close", "Default", "None", "Unknown", "Enabled", "Disabled", "Connected", "Disconnected", "Ready", "Stopped", "Warning", "Error", "Information", "Yes", "No", "All", "Overview", "Settings", "Profiles", "Controllers", "General", "Appearance", "Advanced", "Integrations", "Output", "Audio", "Battery", "Actions", "Custom", "Preview", "Finished", "Cancelled", "Automatic", "Normal", "Light", "Dark", "Support", "Help", "License", "Credits", "Extras", "Hotkeys", "Keyboard", "Mouse", "Touchpad", "Gyro", "Volume", "Strength", "Language", "Low", "Medium", "High", "Off", "On"
    };
    private static readonly HashSet<string> InvariantNames = new(StringComparer.Ordinal)
    { "Xbox 360", "Xbox One / Series", "DualShock 4", "DualSense", "DualSense Edge", "Switch 2 Pro", "DS4Windows", "Xbox One", "Windows 10", "Windows 11", "Switch Pro", "Joy-Con", "Joy-Con 2", "Microsoft .NET", "SDL Gamepad Tool", "VIIPER", "HidHide", "Bluetooth", "FakerInput", "XInput", "DirectInput", "Windows", "Steam", "HidGuardian" };
    private static bool Human(string text) => !InvariantNames.Contains(text) && Words.IsMatch(text) && !text.Contains("http", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains('\\') && !text.Contains(".exe", StringComparison.OrdinalIgnoreCase) && !text.Contains(".dll", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains(".png", StringComparison.OrdinalIgnoreCase) && !text.Contains(".ico", StringComparison.OrdinalIgnoreCase) &&
        !text.StartsWith('<') && !text.Contains("{Binding") && (text.Any(char.IsWhiteSpace) || ShortWords.Contains(text) || Regex.IsMatch(text, "^[A-Z][a-z]+$"));

    private static bool ExistingLocalization(SyntaxNode node) => node.Ancestors().OfType<InvocationExpressionSyntax>().Any(call =>
        call.Expression.ToString().Contains("UiText.") || call.Expression.ToString().Contains("ResourceManager.GetString") ||
        call.Expression.ToString().Contains("GetLocalizedString"));

    private bool UiContext(ExpressionSyntax node)
    {
        if (ExistingLocalization(node)) return false;
        // Constants and language-neutral comparisons cannot call a runtime lookup.
        if (node.Ancestors().Any(parent => parent is AttributeSyntax or CaseSwitchLabelSyntax or ConstantPatternSyntax)) return false;
        if (node.Ancestors().OfType<FieldDeclarationSyntax>().Any(field => field.Modifiers.Any(SyntaxKind.ConstKeyword))) return false;
        if (node.Ancestors().OfType<LocalDeclarationStatementSyntax>().Any(local => local.Modifiers.Any(SyntaxKind.ConstKeyword))) return false;
        if (node.Ancestors().OfType<BinaryExpressionSyntax>().Any(binary => binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression))) return false;
        var call = node.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        string called = call?.Expression.ToString() ?? "";
        if (Regex.IsMatch(called, "Log|Debug|Trace|Regex|Parse|Enum|nameof|GetString|Contains|StartsWith|EndsWith|Equals|Replace|Append|Path|FindName|SelectSingleNode|GetResource|Process.Start|SetBinding|OnPropertyChanged|PropertyChanged")) return false;
        if (Regex.IsMatch(called, "MessageBox.*Show|ShowMessage|ShowError|ShowWarning|ShowInformation")) return true;
        if (!uiFile) return false;
        var assignment = node.Ancestors().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
        if (assignment != null && UiName.IsMatch(assignment.Left.ToString())) return true;
        var variable = node.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        if (variable != null && UiName.IsMatch(variable.Identifier.ValueText)) return true;
        var property = node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (property != null && UiName.IsMatch(property.Identifier.ValueText)) return true;
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (node.Ancestors().Any(parent => parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax) &&
            method != null && !Regex.IsMatch(method.Identifier.ValueText, "Normalize|Parse|Serializ|Format.*Path|Get.*Path|Get.*Id|Get.*Key")) return true;
        if (Regex.IsMatch(called, "Set.*(Text|Status|Hint)|Update.*(Text|Status|Progress)|ReportProgress")) return true;
        var created = node.Ancestors().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
        string type = created?.Type.ToString() ?? "";
        if (node.Ancestors().OfType<ImplicitObjectCreationExpressionSyntax>().Any()) return true;
        return Regex.IsMatch(type, "MenuItem|Button|Label|TextBlock|.*Choice$|.*Option$|.*UiState$|.*Presentation$|ControllerStartupStatus|ControllerMicrophoneUiState") ||
            created != null && type.EndsWith("Exception", StringComparison.Ordinal);
    }

    private string Record(ExpressionSyntax node, string source)
    {
        string key = "UI_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
        inventory.Add(new(path, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, source, key));
        return SyntaxFactory.Literal(source).ToString();
    }

    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.StringLiteralExpression) || node.Parent is InterpolatedStringExpressionSyntax) return base.VisitLiteralExpression(node);
        string text = node.Token.ValueText;
        if (!Human(text) || !UiContext(node)) return base.VisitLiteralExpression(node);
        string literal = Record(node, text);
        return SyntaxFactory.ParseExpression("global::DS4Windows.UiText.Get(" + literal + ")").WithTriviaFrom(node);
    }

    public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        string words = string.Concat(node.Contents.OfType<InterpolatedStringTextSyntax>().Select(part => part.TextToken.ValueText));
        if (!Human(words) || !UiContext(node)) return base.VisitInterpolatedStringExpression(node);
        var format = new StringBuilder();
        var values = new List<string>();
        foreach (var part in node.Contents)
        {
            if (part is InterpolatedStringTextSyntax text) format.Append(text.TextToken.ValueText);
            else if (part is InterpolationSyntax value)
            {
                format.Append('{').Append(values.Count);
                if (value.AlignmentClause != null) format.Append(',').Append(value.AlignmentClause.Value.ToString());
                if (value.FormatClause != null) format.Append(':').Append(value.FormatClause.FormatStringToken.ValueText);
                format.Append('}');
                values.Add(value.Expression.ToString());
            }
        }
        string literal = Record(node, format.ToString());
        return SyntaxFactory.ParseExpression("global::DS4Windows.UiText.Format(" + literal + (values.Count > 0 ? ", " + string.Join(", ", values) : "") + ")").WithTriviaFrom(node);
    }
}
