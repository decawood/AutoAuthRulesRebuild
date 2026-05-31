using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoAuth.Api.Models;

namespace AutoAuth.Api.Services;

public sealed class ObjectiveGuidelineService
{
    private const decimal SampleConfidence = 97m;
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private readonly string _guidelineDirectory;
    private readonly string _performanceWorkbookPath;

    public ObjectiveGuidelineService(IWebHostEnvironment environment)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../.."));
        _guidelineDirectory = Path.Combine(projectRoot, "Guideline XMLs");
        _performanceWorkbookPath = Path.Combine(projectRoot, "Precision Recall Data", "indication_guideline_performance.xlsx");
    }

    public IReadOnlyList<ObjectiveGuidelineSummary> Summaries()
    {
        var performanceRows = LoadPerformanceRows();

        return GetGuidelineFiles()
            .Select(file => BuildGuideline(file, performanceRows).Summary)
            .OrderBy(summary => summary.Title)
            .ThenBy(summary => summary.Code)
            .ToList();
    }

    public ObjectiveGuidelineDetail Detail(string hsim)
    {
        var performanceRows = LoadPerformanceRows();
        var guideline = GetGuidelineFiles()
            .Select(file => BuildGuideline(file, performanceRows))
            .FirstOrDefault(detail => detail.Summary.Hsim.Equals(hsim, StringComparison.OrdinalIgnoreCase));

        return guideline ?? throw new InvalidOperationException($"Guideline '{hsim}' was not found.");
    }

    private IEnumerable<string> GetGuidelineFiles()
    {
        if (!Directory.Exists(_guidelineDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_guidelineDirectory, "*.xml", SearchOption.TopDirectoryOnly);
    }

    private ObjectiveGuidelineDetail BuildGuideline(string path, IReadOnlyDictionary<string, PerformanceRow> performanceRows)
    {
        var document = XDocument.Load(path);
        var guideline = document.Descendants().FirstOrDefault(element => IsNamed(element, "Guideline"))
            ?? throw new InvalidOperationException($"Guideline metadata was not found in '{Path.GetFileName(path)}'.");
        var sections = FindAutoAuthorizationSections(document).ToList();
        var baseNodes = BuildSectionNodes(sections);
        var nodeIds = FlattenNodes(baseNodes).Select(node => node.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var matchedIds = nodeIds.Where(performanceRows.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usesSampleMetrics = nodeIds.Count > 0 && matchedIds.Count == 0;
        var nodes = baseNodes.Select(node => AttachMetrics(node, performanceRows, usesSampleMetrics)).ToList();
        var metrics = usesSampleMetrics
            ? AggregateSampleMetrics(nodes)
            : AggregatePerformanceMetrics(matchedIds.Select(id => performanceRows[id]));
        var rawTitle = AttributeValue(guideline, "Title");
        var hsim = AttributeValue(guideline, "HSIM");
        var summary = new ObjectiveGuidelineSummary(
            Id: hsim,
            Hsim: hsim,
            Code: AttributeValue(guideline, "GCode"),
            Title: CleanTitle(rawTitle),
            RawTitle: rawTitle,
            ProductCode: AttributeValue(guideline, "ProductCode"),
            GuidelineType: AttributeValue(guideline, "GuidelineType"),
            Version: AttributeValue(guideline, "VersionNumber"),
            Glos: EmptyToNull(AttributeValue(guideline, "GLOS")),
            FileName: Path.GetFileName(path),
            AutoAuthorizationSectionCount: sections.Count,
            IndicationCount: nodeIds.Count,
            MatchedIndicationCount: matchedIds.Count,
            HasPerformanceData: matchedIds.Count > 0,
            UsesSampleMetrics: usesSampleMetrics);

        return new ObjectiveGuidelineDetail(summary, metrics, nodes);
    }

    private static IReadOnlyList<XElement> FindAutoAuthorizationSections(XDocument document)
    {
        return document
            .Descendants()
            .Where(element => IsNamed(element, "Section"))
            .Select(section => section.Elements().FirstOrDefault(IsGuidelineSection))
            .Where(section => section is not null && IsTrue(AttributeValue(section, "isautoauthorization")))
            .Cast<XElement>()
            .OrderBy(section => ParseInt(AttributeValue(section, "displayorder")))
            .ToList();
    }

    private static List<ObjectiveGuidelineNode> BuildSectionNodes(IReadOnlyList<XElement> sections)
    {
        var sectionNodes = sections.Select((section, sectionIndex) =>
        {
            var listItems = section
                .Elements()
                .Where(element => IsNamed(element, "itemizedlist"))
                .SelectMany(list => list.Elements().Where(element => IsNamed(element, "listitem")))
                .Select((item, index) => BuildNode(item, $"{AttributeValue(section, "id")}-{index}"))
                .ToList();

            if (sections.Count == 1)
            {
                return listItems;
            }

            var sectionId = AttributeValue(section, "id");
            var sectionTitle = DirectChildText(section, "heading");
            if (string.IsNullOrWhiteSpace(sectionTitle))
            {
                sectionTitle = AttributeValue(section, "role");
            }

            return
            [
                new ObjectiveGuidelineNode(
                    Id: string.IsNullOrWhiteSpace(sectionId) ? $"section-{sectionIndex}" : sectionId,
                    Type: "group",
                    Text: sectionTitle,
                    Requirement: null,
                    Metrics: null,
                    Items: listItems)
            ];
        });

        return sectionNodes.SelectMany(nodes => nodes).ToList();
    }

    private static ObjectiveGuidelineNode BuildNode(XElement item, string fallbackId)
    {
        var id = AttributeValue(item, "indication_id");
        if (string.IsNullOrWhiteSpace(id))
        {
            id = fallbackId;
        }

        var para = item.Elements().FirstOrDefault(element => IsNamed(element, "para"));
        var text = para is null ? string.Empty : CleanText(para);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = DirectChildText(item, "autoauthsectiontext");
        }

        var children = item
            .Elements()
            .Where(element => IsNamed(element, "itemizedlist"))
            .SelectMany(list => list.Elements().Where(element => IsNamed(element, "listitem")))
            .Select((child, index) => BuildNode(child, $"{id}-{index}"))
            .ToList();

        return new ObjectiveGuidelineNode(
            Id: id,
            Type: children.Count > 0 ? "group" : "indication",
            Text: string.IsNullOrWhiteSpace(text) ? "Untitled indication" : text,
            Requirement: ExtractRequirement(para),
            Metrics: null,
            Items: children);
    }

    private static ObjectiveGuidelineNode AttachMetrics(
        ObjectiveGuidelineNode node,
        IReadOnlyDictionary<string, PerformanceRow> performanceRows,
        bool usesSampleMetrics)
    {
        ObjectiveGuidelineMetricSet? metrics = null;
        if (performanceRows.TryGetValue(node.Id, out var row))
        {
            metrics = MetricFromPerformanceRow(row);
        }
        else if (usesSampleMetrics)
        {
            metrics = SampleMetric(node.Id);
        }

        return node with
        {
            Metrics = metrics,
            Items = node.Items.Select(child => AttachMetrics(child, performanceRows, usesSampleMetrics)).ToList()
        };
    }

    private static IEnumerable<ObjectiveGuidelineNode> FlattenNodes(IEnumerable<ObjectiveGuidelineNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in FlattenNodes(node.Items))
            {
                yield return child;
            }
        }
    }

    private static ObjectiveGuidelineMetricSet? AggregatePerformanceMetrics(IEnumerable<PerformanceRow> rows)
    {
        var materialized = rows.ToList();
        if (materialized.Count == 0)
        {
            return null;
        }

        var truePositive = materialized.Sum(row => row.TruePositive);
        var falsePositive = materialized.Sum(row => row.FalsePositive);
        var trueNegative = materialized.Sum(row => row.TrueNegative);
        var falseNegative = materialized.Sum(row => row.FalseNegative);
        var totalCases = materialized.Sum(row => row.TotalCases);
        var precisionDenominator = truePositive + falsePositive;
        var recallDenominator = truePositive + falseNegative;

        return new ObjectiveGuidelineMetricSet(
            MetAi: Percent(truePositive + falsePositive, totalCases),
            Confidence: SampleConfidence,
            AgreementAgree: Percent(truePositive, precisionDenominator),
            AgreementDisagree: 100m - (Percent(truePositive, precisionDenominator) ?? 0m),
            Recall: Percent(truePositive, recallDenominator),
            TruePositive: truePositive,
            FalsePositive: falsePositive,
            TrueNegative: trueNegative,
            FalseNegative: falseNegative,
            TotalCases: totalCases,
            IsSample: false);
    }

    private static ObjectiveGuidelineMetricSet? AggregateSampleMetrics(IEnumerable<ObjectiveGuidelineNode> nodes)
    {
        var metrics = FlattenNodes(nodes).Select(node => node.Metrics).Where(metric => metric is not null).Cast<ObjectiveGuidelineMetricSet>().ToList();
        if (metrics.Count == 0)
        {
            return null;
        }

        var precision = metrics.Average(metric => metric.AgreementAgree ?? 0m);

        return new ObjectiveGuidelineMetricSet(
            MetAi: Math.Round(metrics.Average(metric => metric.MetAi ?? 0m), 1),
            Confidence: Math.Round(metrics.Average(metric => metric.Confidence ?? SampleConfidence), 1),
            AgreementAgree: Math.Round(precision, 1),
            AgreementDisagree: Math.Round(100m - precision, 1),
            Recall: Math.Round(metrics.Average(metric => metric.Recall ?? 0m), 1),
            TruePositive: null,
            FalsePositive: null,
            TrueNegative: null,
            FalseNegative: null,
            TotalCases: null,
            IsSample: true);
    }

    private static ObjectiveGuidelineMetricSet MetricFromPerformanceRow(PerformanceRow row)
    {
        return new ObjectiveGuidelineMetricSet(
            MetAi: Percent(row.TruePositive + row.FalsePositive, row.TotalCases),
            Confidence: SampleConfidence,
            AgreementAgree: RoundPercent(row.Precision),
            AgreementDisagree: Math.Round(100m - RoundPercent(row.Precision), 1),
            Recall: RoundPercent(row.Recall),
            TruePositive: row.TruePositive,
            FalsePositive: row.FalsePositive,
            TrueNegative: row.TrueNegative,
            FalseNegative: row.FalseNegative,
            TotalCases: row.TotalCases,
            IsSample: false);
    }

    private static ObjectiveGuidelineMetricSet SampleMetric(string id)
    {
        var seed = StableSeed(id);
        var precision = 72m + seed % 23;

        return new ObjectiveGuidelineMetricSet(
            MetAi: 20m + seed % 55,
            Confidence: 92m + seed % 8,
            AgreementAgree: precision,
            AgreementDisagree: 100m - precision,
            Recall: 76m + seed % 21,
            TruePositive: null,
            FalsePositive: null,
            TrueNegative: null,
            FalseNegative: null,
            TotalCases: null,
            IsSample: true);
    }

    private IReadOnlyDictionary<string, PerformanceRow> LoadPerformanceRows()
    {
        var rows = new Dictionary<string, PerformanceRow>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_performanceWorkbookPath))
        {
            return rows;
        }

        using var archive = ZipFile.OpenRead(_performanceWorkbookPath);
        var sharedStrings = LoadSharedStrings(archive);

        foreach (var sheetName in new[] { "Indications vs Agreed Cases", "Indication Performance" })
        {
            var worksheetRows = LoadWorksheetRows(archive, sharedStrings, sheetName);
            var header = worksheetRows.FirstOrDefault();
            foreach (var row in worksheetRows.Skip(1))
            {
                if (!TryBuildPerformanceRow(row, header, out var performanceRow))
                {
                    continue;
                }

                rows.TryAdd(performanceRow.Uid, performanceRow);
            }
        }

        return rows;
    }

    private static bool TryBuildPerformanceRow(IReadOnlyList<string> row, IReadOnlyList<string>? header, out PerformanceRow performanceRow)
    {
        performanceRow = default!;
        if (header is null)
        {
            return false;
        }

        var uid = Cell(row, header, "uid");
        if (string.IsNullOrWhiteSpace(uid))
        {
            return false;
        }

        performanceRow = new PerformanceRow(
            Uid: uid,
            Precision: ParseDecimal(Cell(row, header, "Precision (PPV)")),
            Recall: ParseDecimal(Cell(row, header, "Recall (Sensitivity)")),
            TruePositive: ParseInt(Cell(row, header, "True_positive")),
            FalsePositive: ParseInt(Cell(row, header, "False_positive")),
            TrueNegative: ParseInt(Cell(row, header, "True_negative")),
            FalseNegative: ParseInt(Cell(row, header, "False_negative")),
            TotalCases: ParseInt(Cell(row, header, "Total_cases")));

        return performanceRow.TotalCases > 0;
    }

    private static List<List<string>> LoadWorksheetRows(ZipArchive archive, IReadOnlyList<string> sharedStrings, string sheetName)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidOperationException("Workbook metadata not found.");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidOperationException("Workbook relationships not found.");
        var workbook = XDocument.Load(workbookEntry.Open());
        var relationships = XDocument.Load(relsEntry.Open());
        var relationshipTargets = relationships
            .Descendants()
            .Where(element => element.Name.LocalName == "Relationship")
            .ToDictionary(element => AttributeValue(element, "Id"), element => AttributeValue(element, "Target"));
        var sheet = workbook
            .Descendants(SpreadsheetNamespace + "sheet")
            .FirstOrDefault(element => AttributeValue(element, "name").Equals(sheetName, StringComparison.OrdinalIgnoreCase));

        if (sheet is null)
        {
            return [];
        }

        var relationshipId = sheet.Attribute(RelationshipNamespace + "id")?.Value ?? string.Empty;
        if (!relationshipTargets.TryGetValue(relationshipId, out var target))
        {
            return [];
        }

        var entryPath = NormalizeWorkbookTarget(target);
        var sheetEntry = archive.GetEntry(entryPath);
        if (sheetEntry is null)
        {
            return [];
        }

        var worksheet = XDocument.Load(sheetEntry.Open());
        var rows = new List<List<string>>();
        foreach (var row in worksheet.Descendants(SpreadsheetNamespace + "row"))
        {
            var cells = new Dictionary<int, string>();
            foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
            {
                var cellReference = AttributeValue(cell, "r");
                var index = ColumnIndex(cellReference);
                cells[index] = CellValue(cell, sharedStrings);
            }

            if (cells.Count > 0)
            {
                rows.Add(Enumerable.Range(0, cells.Keys.Max() + 1).Select(index => cells.GetValueOrDefault(index, string.Empty)).ToList());
            }
        }

        return rows;
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        var document = XDocument.Load(entry.Open());
        return document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => CleanSpreadsheetText(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string CellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = AttributeValue(cell, "t");
        if (type == "s")
        {
            var sharedStringIndex = ParseInt(cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty);
            return sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count ? sharedStrings[sharedStringIndex] : string.Empty;
        }

        if (type == "inlineStr")
        {
            return CleanSpreadsheetText(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
        }

        return cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
    }

    private static string Cell(IReadOnlyList<string> row, IReadOnlyList<string> header, string column)
    {
        var index = header.ToList().FindIndex(value => value.Equals(column, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < row.Count ? row[index] : string.Empty;
    }

    private static string NormalizeWorkbookTarget(string target)
    {
        var normalized = target.TrimStart('/');
        return normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? normalized : $"xl/{normalized}";
    }

    private static decimal? Percent(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return null;
        }

        return Math.Round(numerator / (decimal)denominator * 100m, 1);
    }

    private static decimal RoundPercent(decimal ratio)
    {
        return Math.Round(ratio * 100m, 1);
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static int ParseInt(string value)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? (int)parsed : 0;
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            var seed = 17;
            foreach (var character in value)
            {
                seed = seed * 31 + character;
            }

            return seed & int.MaxValue;
        }
    }

    private static int ColumnIndex(string cellReference)
    {
        var index = 0;
        foreach (var character in cellReference.Where(char.IsLetter))
        {
            index = index * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return Math.Max(0, index - 1);
    }

    private static string CleanTitle(string title)
    {
        return NormalizeWhitespace(Regex.Replace(title, @"\bcopy\b", string.Empty, RegexOptions.IgnoreCase)).Trim(' ', '-');
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ExtractRequirement(XElement? para)
    {
        if (para is null)
        {
            return string.Empty;
        }

        var logic = para.Descendants().FirstOrDefault(element => IsNamed(element, "listlogic"));
        return logic is null ? string.Empty : CleanText(logic);
    }

    private static string CleanText(XElement element)
    {
        var builder = new StringBuilder();
        AppendText(element, builder);
        return NormalizeWhitespace(builder.ToString());
    }

    private static void AppendText(XNode node, StringBuilder builder)
    {
        switch (node)
        {
            case XText text:
                builder.Append(text.Value);
                builder.Append(' ');
                break;
            case XElement element when ShouldSkipTextElement(element):
                break;
            case XElement element:
                foreach (var child in element.Nodes())
                {
                    AppendText(child, builder);
                }

                break;
        }
    }

    private static bool ShouldSkipTextElement(XElement element)
    {
        return IsNamed(element, "citation") || IsNamed(element, "footnote") || IsNamed(element, "evidence-grade");
    }

    private static string DirectChildText(XElement element, string childName)
    {
        var child = element.Elements().FirstOrDefault(candidate => IsNamed(candidate, childName));
        return child is null ? string.Empty : CleanText(child);
    }

    private static string NormalizeWhitespace(string value)
    {
        return Regex.Replace(Regex.Replace(value, @"\s+([:;,.])", "$1"), @"\s+", " ").Trim();
    }

    private static string CleanSpreadsheetText(IEnumerable<string> text)
    {
        return NormalizeWhitespace(string.Join(string.Empty, text));
    }

    private static bool IsGuidelineSection(XElement element)
    {
        return element.Name.LocalName.StartsWith("guideline-sect", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNamed(XElement element, string name)
    {
        return element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrue(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string AttributeValue(XElement? element, string name)
    {
        return element?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
    }

    private sealed record PerformanceRow(
        string Uid,
        decimal Precision,
        decimal Recall,
        int TruePositive,
        int FalsePositive,
        int TrueNegative,
        int FalseNegative,
        int TotalCases);
}
