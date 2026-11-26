using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Transflux.Core;

public sealed class XmlCsvConverter(string? recordPath = null, IReadOnlyList<string>? columns = null) : IFileConverter
{
    public ConversionOperation Operation => ConversionOperation.XmlToCsv;
    public IReadOnlyCollection<string> SourceExtensions => new[] { ".xml" };
    public string OutputExtension => ".csv";

    // Paths use slash-separated qualified names, with Clark notation for namespaces.
    // Automatic mode selects the first direct child of the document root.
    private static string Name(XName name) => name.ToString();
    private const int MaxDepth = 64, MaxColumns = 4096, MaxField = 1024 * 1024;
    private static XmlReader Open(string source) => XmlReader.Create(source, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
        IgnoreComments = true, IgnoreProcessingInstructions = true,
        MaxCharactersFromEntities = 1024, MaxCharactersInDocument = 512L * 1024 * 1024
    });

    private IEnumerable<Dictionary<string, string>> Rows(string source, CancellationToken token)
    {
        using var reader = Open(source);
        var path = new List<string>();
        string? selected = recordPath?.Trim('/');
        if (selected == "") throw new ArgumentException("Record path is empty.");
        reader.MoveToContent();
        while (!reader.EOF)
        {
            token.ThrowIfCancellationRequested();
            if (reader.Depth > MaxDepth) throw new InvalidDataException("XML nesting exceeds limit.");
            if (reader.NodeType == XmlNodeType.Element)
            {
                while (path.Count > reader.Depth) path.RemoveAt(path.Count - 1);
                path.Add(Name(XName.Get(reader.LocalName, reader.NamespaceURI)));
                var current = string.Join("/", path);
                if (selected is null && reader.Depth == 1) selected = current;
                if (current == selected)
                {
                    using (var subtree = reader.ReadSubtree())
                    {
                        var element = LoadBounded(subtree, token);
                        var row = new Dictionary<string, string>(StringComparer.Ordinal);
                        Flatten(element, "", row, token);
                        yield return row;
                    }
                    reader.Read();
                    continue;
                }
            }
            reader.Read();
        }
    }

    private static XElement LoadBounded(XmlReader reader, CancellationToken token)
    {
        // Validate depth and cumulative record size before constructing an element tree.
        var stack = new Stack<XElement>();
        XElement? root = null;
        long chars = 0;
        int nodes = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.Depth > MaxDepth || ++nodes > 100000)
                throw new InvalidDataException("XML record complexity exceeds limit.");
            chars += reader.Value.Length + reader.Name.Length + reader.NamespaceURI.Length;
            if (chars > 16L * 1024 * 1024) throw new InvalidDataException("XML record exceeds size limit.");
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var element = new XElement(XName.Get(reader.LocalName, reader.NamespaceURI));
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            chars += reader.Value.Length + reader.Name.Length + reader.NamespaceURI.Length;
                            if (++nodes > 100000 || chars > 16L * 1024 * 1024)
                                throw new InvalidDataException("XML record exceeds size limit.");
                            if (reader.NamespaceURI != "http://www.w3.org/2000/xmlns/" && reader.Name != "xmlns")
                                element.Add(new XAttribute(XName.Get(reader.LocalName, reader.NamespaceURI), reader.Value));
                        } while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                    if (stack.Count > 0) stack.Peek().Add(element); else root = element;
                    if (!reader.IsEmptyElement) stack.Push(element);
                    break;
                case XmlNodeType.EndElement: stack.Pop(); break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    if (stack.Count > 0) stack.Peek().Add(new XText(reader.Value));
                    break;
            }
        }
        return root ?? throw new InvalidDataException("Empty XML record.");
    }

    private static void Flatten(XElement element, string prefix, Dictionary<string, string> row, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            Add(row, prefix + "@" + Name(attribute.Name), attribute.Value);
        var children = element.Elements().ToList();
        if (children.Count == 0)
        {
            Add(row, prefix.Length == 0 ? "#text" : prefix.TrimEnd('.'), element.Value);
            return;
        }
        var directText = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value));
        if (!string.IsNullOrWhiteSpace(directText)) Add(row, prefix + "#text", directText);
        foreach (var group in children.GroupBy(c => c.Name))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var key = Name(group.Key) + (i > 0 ? "[" + (i + 1) + "]" : "");
                Flatten(list[i], prefix + key + ".", row, token);
            }
        }
    }
    private static void Add(Dictionary<string, string> row, string key, string value)
    {
        if (row.Count >= MaxColumns || key.Length > 4096 || value.Length > MaxField)
            throw new InvalidDataException("XML field or column limit exceeded.");
        if (!row.TryAdd(key, value)) throw new InvalidDataException("Ambiguous flattened column: " + key);
    }
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Line(IEnumerable<string> values) => string.Join(",", values.Select(Csv));

    public async Task ConvertAsync(string source, string temporaryOutput, CancellationToken cancellationToken)
    {
        var headers = columns?.ToList() ?? new List<string>();
        var known = new HashSet<string>(headers, StringComparer.Ordinal);
        if (known.Count != headers.Count) throw new ArgumentException("Duplicate selected columns.");
        long count = 0;
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows(source, cancellationToken))
        {
            count++;
            if (columns is not null) observed.UnionWith(row.Keys);
            if (columns is null)
                foreach (var key in row.Keys)
                    if (known.Add(key)) headers.Add(key);
            if (known.Count > MaxColumns || observed.Count > MaxColumns)
                throw new InvalidDataException("XML schema exceeds column limit.");
        }
        if (count == 0 || headers.Count == 0) throw new InvalidDataException("No matching XML records or columns.");
        if (columns is not null && headers.Any(h => !observed.Contains(h)))
            throw new InvalidDataException("One or more selected columns were not found.");
        await using var writer = new StreamWriter(new FileStream(temporaryOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan), new UTF8Encoding(false), 65536);
        await writer.WriteLineAsync(Line(headers).AsMemory(), cancellationToken);
        foreach (var row in Rows(source, cancellationToken))
            await writer.WriteLineAsync(Line(headers.Select(h => row.GetValueOrDefault(h, ""))).AsMemory(), cancellationToken);
    }
}
