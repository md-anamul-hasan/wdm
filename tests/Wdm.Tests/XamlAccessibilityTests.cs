using System.Xml.Linq;
using Xunit;

namespace Wdm.Tests;

/// <summary>
/// Screen readers get a button's name from AutomationProperties.Name, a Content text, or a text child. A button that has none of them
/// is announced as just "button": the toolbar's Add URL, Resume, Pause and Delete were exactly that until 2.0.1.
/// </summary>
public class XamlAccessibilityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Wdm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Wdm.slnx not found above the test output folder");
    }

    private static bool HasName(XElement button)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        if (button.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.Name")) return true;
        if (button.Attribute("Content") != null) return true;
        return button.Descendants(x + "TextBlock").Any(t => t.Attribute("Text") != null);
    }

    [Theory]
    [InlineData("MainWindow.xaml")]
    public void EveryButton_HasAnAccessibleName(string file)
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "Wdm.Ui", file));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var unnamed = doc.Descendants(x + "Button").Where(b => !HasName(b)).Select(b => b.Attribute("Click")?.Value ?? b.ToString()[..Math.Min(80, b.ToString().Length)]).ToList();
        Assert.True(unnamed.Count == 0, "Buttons without a name: " + string.Join(", ", unnamed));
    }

    [Fact]
    public void EveryInputControl_HasAnAutomationName()
    {
        // A label next to a text box is not read with it: the control itself needs the name (a slider or grid likewise).
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "Wdm.Ui", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var inputs = new[] { "TextBox", "PasswordBox", "ComboBox", "Slider", "DataGrid" };
        var unnamed = doc.Descendants().Where(e => e.Name.Namespace == x && inputs.Contains(e.Name.LocalName))
            .Where(e => !e.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.Name"))
            .Select(e => e.Name.LocalName + " " + (e.Attribute("Text")?.Value ?? e.Attribute("Value")?.Value ?? e.Attribute("ItemsSource")?.Value ?? "")).ToList();
        Assert.True(unnamed.Count == 0, "Input controls without an AutomationProperties.Name: " + string.Join("; ", unnamed));
    }

    [Fact]
    public void ToolbarButtons_CarryExplicitAutomationNames()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Wdm.Ui", "MainWindow.xaml"));
        foreach (var name in new[] { "Add URL", "Resume", "Pause", "Delete" })
            Assert.Contains($"AutomationProperties.Name=\"{name}\"", xaml);
    }
}
