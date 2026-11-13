using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfList = System.Windows.Documents.List;

namespace Kniip.Update;

/// <summary>Zet markdown (releasenotes) om naar een WPF <see cref="FlowDocument"/>.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseTaskLists()
        .Build();

    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");
    private static Brush CodeBackground => Resource("CodeBrush", Brushes.WhiteSmoke);
    private static Brush QuoteBorder => Resource("QuoteBorderBrush", Brushes.LightGray);

    private static Brush Accent => Resource("AccentBrush", Brushes.SteelBlue);

    private static Brush Subtle => Resource("SubtleTextBrush", Brushes.Gray);

    public static FlowDocument CreateDocument() => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 13,
        PagePadding = new Thickness(14, 10, 14, 10),
        TextAlignment = TextAlignment.Left,
        Background = Resource("CardBrush", Brushes.White),
    };

    /// <summary>Voegt de blokken van <paramref name="markdown"/> toe; relatieve links wijzen naar <paramref name="baseUri"/>.</summary>
    public static void Append(FlowDocument document, string markdown, Uri? baseUri = null)
    {
        MarkdownDocument parsed = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        foreach (MdBlock block in parsed)
        {
            if (RenderBlock(block, baseUri) is { } rendered)
            {
                document.Blocks.Add(rendered);
            }
        }
    }

    public static Paragraph Heading(string text, double fontSize, Thickness margin) => new(new Run(text))
    {
        FontSize = fontSize,
        FontWeight = FontWeights.SemiBold,
        Margin = margin,
    };

    private static WpfBlock? RenderBlock(MdBlock block, Uri? baseUri)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var paragraph = new Paragraph
                {
                    FontSize = heading.Level switch { 1 => 19, 2 => 16.5, 3 => 14.5, _ => 13 },
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, heading.Level <= 2 ? 12 : 8, 0, 4),
                };
                AddInlines(paragraph.Inlines, heading.Inline, baseUri);
                return paragraph;
            }

            case ParagraphBlock paragraphBlock:
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                AddInlines(paragraph.Inlines, paragraphBlock.Inline, baseUri);
                return paragraph;
            }

            case ListBlock listBlock:
            {
                var list = new WpfList
                {
                    MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(20, 0, 0, 0),
                };
                if (listBlock.IsOrdered && int.TryParse(listBlock.OrderedStart, out int start) && start > 0)
                {
                    list.StartIndex = start;
                }

                foreach (ListItemBlock itemBlock in listBlock.OfType<ListItemBlock>())
                {
                    var item = new ListItem();
                    foreach (MdBlock child in itemBlock)
                    {
                        if (RenderBlock(child, baseUri) is { } rendered)
                        {
                            rendered.Margin = new Thickness(0, 0, 0, 2);
                            item.Blocks.Add(rendered);
                        }
                    }

                    list.ListItems.Add(item);
                }

                return list;
            }

            case QuoteBlock quote:
            {
                var section = new Section
                {
                    BorderBrush = QuoteBorder,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 0, 0, 0),
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = Subtle,
                };
                foreach (MdBlock child in quote)
                {
                    if (RenderBlock(child, baseUri) is { } rendered)
                    {
                        section.Blocks.Add(rendered);
                    }
                }

                return section;
            }

            case CodeBlock code:
            {
                var paragraph = new Paragraph
                {
                    FontFamily = CodeFont,
                    FontSize = 12,
                    Background = CodeBackground,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 8),
                };
                string[] lines = code.Lines.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        paragraph.Inlines.Add(new LineBreak());
                    }

                    paragraph.Inlines.Add(new Run(lines[i]));
                }

                return paragraph;
            }

            case ThematicBreakBlock:
                return new Paragraph
                {
                    BorderBrush = QuoteBorder,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    FontSize = 1,
                    Margin = new Thickness(0, 4, 0, 10),
                };

            case HtmlBlock:
                return null;

            case ContainerBlock container:
            {
                var section = new Section();
                foreach (MdBlock child in container)
                {
                    if (RenderBlock(child, baseUri) is { } rendered)
                    {
                        section.Blocks.Add(rendered);
                    }
                }

                return section.Blocks.Count > 0 ? section : null;
            }

            case LeafBlock leaf when leaf.Inline != null:
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                AddInlines(paragraph.Inlines, leaf.Inline, baseUri);
                return paragraph;
            }

            default:
                return null;
        }
    }

    private static void AddInlines(InlineCollection target, ContainerInline? container, Uri? baseUri)
    {
        if (container == null)
        {
            return;
        }

        foreach (MdInline inline in container)
        {
            if (RenderInline(inline, baseUri) is { } rendered)
            {
                target.Add(rendered);
            }
        }
    }

    private static WpfInline? RenderInline(MdInline inline, Uri? baseUri)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new Run(literal.Content.ToString());

            case CodeInline code:
                return new Run(code.Content)
                {
                    FontFamily = CodeFont,
                    FontSize = 12,
                    Background = CodeBackground,
                };

            case LineBreakInline lineBreak:
                return lineBreak.IsHard ? new LineBreak() : new Run(" ");

            case TaskList task:
                return new Run(task.Checked ? "\u2611 " : "\u2610 ");

            case AutolinkInline autolink:
                return CreateLink(new Span(new Run(autolink.Url)), autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, baseUri);

            case LinkInline link:
            {
                var span = new Span();
                AddInlines(span.Inlines, link, baseUri);
                if (span.Inlines.Count == 0)
                {
                    span.Inlines.Add(new Run(link.Url ?? string.Empty));
                }

                return CreateLink(span, link.Url, baseUri);
            }

            case EmphasisInline emphasis:
            {
                var span = new Span();
                AddInlines(span.Inlines, emphasis, baseUri);
                if (emphasis.DelimiterChar == '~')
                {
                    span.TextDecorations = TextDecorations.Strikethrough;
                }
                else if (emphasis.DelimiterCount >= 2)
                {
                    span.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    span.FontStyle = FontStyles.Italic;
                }

                return span;
            }

            case HtmlInline html when html.Tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase):
                return new LineBreak();

            case HtmlEntityInline entity:
                return new Run(entity.Transcoded.ToString());

            case HtmlInline:
                return null;

            case ContainerInline container:
            {
                var span = new Span();
                AddInlines(span.Inlines, container, baseUri);
                return span;
            }

            default:
                return null;
        }
    }

    private static WpfInline CreateLink(Span content, string? url, Uri? baseUri)
    {
        Uri? target = ResolveUrl(url, baseUri);
        if (target == null)
        {
            return content;
        }

        var link = new Hyperlink(content)
        {
            Foreground = Accent,
            ToolTip = target.ToString(),
            NavigateUri = target,
        };
        link.RequestNavigate += (_, e) =>
        {
            OpenUrl(e.Uri);
            e.Handled = true;
        };
        return link;
    }

    private static Uri? ResolveUrl(string? url, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        Uri? uri = Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute) ? absolute
            : baseUri != null && Uri.TryCreate(baseUri, url, out Uri? relative) ? relative
            : null;
        return uri != null && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto)
            ? uri
            : null;
    }

    public static void OpenUrl(Uri uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Zonder standaardbrowser gebeurt er niets; de link staat in de tooltip.
        }
    }

    private static Brush Resource(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
