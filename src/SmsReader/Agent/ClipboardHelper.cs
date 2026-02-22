using Spectre.Console;

namespace SmsReader.Agent;

public static class ClipboardHelper
{
    public static void CopyToClipboard(string text)
    {
        try
        {
            TextCopy.ClipboardService.SetText(text);
            AnsiConsole.MarkupLine("  [green]>> Copied to clipboard![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [grey]>> Clipboard unavailable: {Markup.Escape(ex.Message)}[/]");
        }
    }
}
