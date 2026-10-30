using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Daycord.Desktop.Views;

/// <summary>
/// Small, offline, non-HTML Markdown preview built from native
/// Avalonia controls. Does not fetch external content or run scripts.
/// </summary>
internal static class NativeMarkdownPreview
{
    private static readonly Regex Heading = new(
        @"^ {0,3}(#{1,6})[ \t]+(.+?)\s*#*\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ListItem = new(
        @"^(?<indent>[ \t]*)(?<marker>[-+*]|\d+[.)])[ \t]+(?<text>.+)$",
        RegexOptions.CultureInvariant);

    private static readonly FontFamily CodeFont = new(
        "Menlo, Consolas, Courier New");

    public static void Render(StackPanel host, string? markdown)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Children.Clear();

        if (string.IsNullOrWhiteSpace(markdown))
            return;

        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var paragraph = new StringBuilder();
        var code = new StringBuilder();

        string? activeFence = null;

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
                return;

            var block = NewTextBlock(12);

            AddInlineText(
                block.Inlines!,
                paragraph.ToString().TrimEnd());

            host.Children.Add(block);
            paragraph.Clear();
        }

        void FlushCode()
        {
            var block = new TextBlock
            {
                FontSize = 12,
                FontFamily = CodeFont,
                TextWrapping = TextWrapping.Wrap,
                Text = code.ToString().TrimEnd('\n')
            };

            host.Children.Add(new Border
            {
                Background = new SolidColorBrush(
                    Color.FromArgb(24, 128, 128, 128)),

                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Child = block
            });

            code.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            var fence = trimmed.StartsWith(
                "```",
                StringComparison.Ordinal)
                    ? "```"
                    : trimmed.StartsWith(
                        "~~~",
                        StringComparison.Ordinal)
                            ? "~~~"
                            : null;

            if (activeFence is not null)
            {
                if (trimmed.StartsWith(
                    activeFence,
                    StringComparison.Ordinal))
                {
                    FlushCode();
                    activeFence = null;
                }
                else
                {
                    code.AppendLine(line);
                }

                continue;
            }

            if (fence is not null)
            {
                FlushParagraph();
                activeFence = fence;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            // ATX headings: # through ######
            var heading = Heading.Match(line);

            if (heading.Success)
            {
                FlushParagraph();

                var level = heading.Groups[1].Length;

                var block = NewTextBlock(level switch
                {
                    1 => 24,
                    2 => 21,
                    3 => 18,
                    4 => 16,
                    5 => 14,
                    _ => 12
                });

                block.FontWeight = FontWeight.SemiBold;

                AddInlineText(
                    block.Inlines!,
                    heading.Groups[2].Value);

                host.Children.Add(block);
                continue;
            }

            // Ordered and unordered list items.
            var listItem = ListItem.Match(line);

            if (listItem.Success)
            {
                FlushParagraph();

                var marker = listItem.Groups["marker"].Value;

                var prefix = char.IsDigit(marker[0])
                    ? marker[..^1] + ".  "
                    : "•  ";

                var indent = listItem.Groups["indent"].Value
                    .Replace(
                        "\t",
                        "    ",
                        StringComparison.Ordinal)
                    .Length;

                var block = NewTextBlock(12);

                block.Margin = new Thickness(
                    10 + Math.Min(indent, 32) * 7,
                    0,
                    0,
                    0);

                block.Inlines!.Add(new Run(prefix));

                AddInlineText(
                    block.Inlines,
                    listItem.Groups["text"].Value);

                host.Children.Add(block);
                continue;
            }

            if (paragraph.Length != 0)
                paragraph.Append('\n');

            paragraph.Append(line);
        }

        FlushParagraph();

        if (activeFence is not null)
            FlushCode();
    }

    private static TextBlock NewTextBlock(double fontSize) => new()
    {
        FontSize = fontSize,
        TextWrapping = TextWrapping.Wrap,

        // Avoid relying on an implicitly created collection.
        Inlines = new InlineCollection()
    };

    private static void AddInlineText(
        InlineCollection target,
        string value,
        int depth = 0)
    {
        // Bound recursion for malformed or deeply nested input.
        if (depth >= 12)
        {
            target.Add(new Run(value));
            return;
        }

        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length == 0)
                return;

            target.Add(new Run(plain.ToString()));
            plain.Clear();
        }

        for (var index = 0; index < value.Length;)
        {
            // Escaped Markdown punctuation.
            if (value[index] == '\\' &&
                index + 1 < value.Length &&
                "\\`*_[]()".Contains(value[index + 1]))
            {
                plain.Append(value[index + 1]);
                index += 2;
                continue;
            }

            // Inline code.
            if (value[index] == '`')
            {
                var end = value.IndexOf('`', index + 1);

                if (end > index + 1)
                {
                    FlushPlain();

                    target.Add(new Run(
                        value[(index + 1)..end])
                    {
                        FontFamily = CodeFont
                    });

                    index = end + 1;
                    continue;
                }
            }

            // Display Markdown link labels as plain text.
            // Never navigate automatically or fetch the URL.
            if (value[index] == '[' &&
                (index == 0 || value[index - 1] != '!'))
            {
                var labelEnd = value.IndexOf(']', index + 1);

                if (labelEnd > index + 1 &&
                    labelEnd + 1 < value.Length &&
                    value[labelEnd + 1] == '(')
                {
                    var urlEnd = value.IndexOf(
                        ')',
                        labelEnd + 2);

                    if (urlEnd > labelEnd + 2)
                    {
                        FlushPlain();

                        AddInlineText(
                            target,
                            value[(index + 1)..labelEnd],
                            depth + 1);

                        index = urlEnd + 1;
                        continue;
                    }
                }
            }

            // Bold and italic markup.
            string? marker = null;

            if (value.AsSpan(index).StartsWith("**") ||
                value.AsSpan(index).StartsWith("__"))
            {
                marker = value.Substring(index, 2);
            }
            else if (value[index] is '*' or '_')
            {
                marker = value.Substring(index, 1);
            }

            if (marker is not null)
            {
                var end = value.IndexOf(
                    marker,
                    index + marker.Length,
                    StringComparison.Ordinal);

                if (end > index + marker.Length)
                {
                    FlushPlain();

                    var span = new Span
                    {
                        Inlines = new InlineCollection()
                    };

                    if (marker.Length == 2)
                    {
                        span.FontWeight = FontWeight.Bold;
                    }
                    else
                    {
                        span.FontStyle = FontStyle.Italic;
                    }

                    AddInlineText(
                        span.Inlines,
                        value.Substring(
                            index + marker.Length,
                            end - index - marker.Length),
                        depth + 1);

                    target.Add(span);

                    index = end + marker.Length;
                    continue;
                }
            }

            plain.Append(value[index]);
            index++;
        }

        FlushPlain();
    }
}