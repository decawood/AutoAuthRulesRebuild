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

    public IReadOnlyList<ObjectiveGuidelineSummary> Summaries(string? metricMode = null)
    {
        var performanceRows = LoadPerformanceRows();
        var resolvedMode = ResolveMetricMode(metricMode);

        return GetGuidelineFiles()
            .Select(file => BuildGuideline(file, performanceRows, resolvedMode).Summary)
            .OrderBy(summary => summary.Title)
            .ThenBy(summary => summary.Code)
            .ToList();
    }

    public ObjectiveGuidelineDetail Detail(string hsim, string? metricMode = null)
    {
        var performanceRows = LoadPerformanceRows();
        var resolvedMode = ResolveMetricMode(metricMode);
        var guideline = GetGuidelineFiles()
            .Select(file => BuildGuideline(file, performanceRows, resolvedMode))
            .FirstOrDefault(detail => detail.Summary.Hsim.Equals(hsim, StringComparison.OrdinalIgnoreCase));

        return guideline ?? throw new InvalidOperationException($"Guideline '{hsim}' was not found.");
    }

    public ObjectiveGuidelinePreview PrecisionPreview(
        decimal precisionThreshold,
        bool useConfidenceThreshold,
        decimal confidenceThreshold,
        string? metricMode = null)
    {
        var performanceRows = LoadPerformanceRows();
        var resolvedMode = ResolveMetricMode(metricMode);
        var groups = GetGuidelineFiles()
            .Select(file => BuildGuideline(file, performanceRows, resolvedMode))
            .Select(detail => BuildPreviewGroup(detail, precisionThreshold, useConfidenceThreshold, confidenceThreshold))
            .Where(group => group.PathwayCount > 0)
            .OrderBy(group => group.Title)
            .ThenBy(group => group.Code)
            .ToList();

        return new ObjectiveGuidelinePreview(
            PrecisionThreshold: precisionThreshold,
            UseConfidenceThreshold: useConfidenceThreshold,
            ConfidenceThreshold: confidenceThreshold,
            MetricMode: MetricModeName(resolvedMode),
            GuidelineCount: groups.Count,
            PathwayCount: groups.Sum(group => group.PathwayCount),
            PrecisionQualifiedCount: groups.Sum(group => group.PrecisionQualifiedCount),
            ConfidenceQualifiedCount: groups.Sum(group => group.ConfidenceQualifiedCount),
            Guidelines: groups);
    }

    public IReadOnlyList<AuthorizationRequest> DemoRequests(string? metricMode = null)
    {
        var performanceRows = LoadPerformanceRows();
        var resolvedMode = ResolveMetricMode(metricMode);
        var now = DateTimeOffset.UtcNow;
        var segments = new[] { "Medicare", "Commercial", "Medicaid" };

        return GetGuidelineFiles()
            .Select(file => BuildGuideline(file, performanceRows, resolvedMode))
            .OrderBy(detail => detail.Summary.Title)
            .Select((detail, index) =>
            {
                var indications = FlattenNodes(detail.Nodes)
                    .Where(node => node.Items.Count == 0 && node.Metrics?.AgreementAgree is not null)
                    .OrderByDescending(node => node.Metrics!.AgreementAgree)
                    .ThenBy(node => node.Text)
                    .GroupBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Take(8)
                    .Select(node => new SynapseIndicationResult(
                        IndicationId: node.Id,
                        IndicationName: node.Text,
                        Category: node.LogicText ?? "Objective AutoAuth criterion",
                        IsObjective: true,
                        Precision: node.Metrics!.AgreementAgree!.Value,
                        Confidence: node.Metrics.Confidence ?? ProjectedConfidence(detail.Summary.Hsim, node.Id),
                        PathwayMet: node.Metrics.AgreementAgree >= 84m,
                        EvidenceSnippet: node.LogicText is null
                            ? "Guideline-derived demo criterion."
                            : $"Guideline logic: {node.LogicText}.",
                        SourceDocument: $"{detail.Summary.Code} - {detail.Summary.Title}"))
                    .ToList();
                var synapseIndicationIds = indications
                    .Select(indication => indication.IndicationId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var providerOnlyEvidence = FlattenNodes(detail.Nodes)
                    .Where(node => node.Items.Count == 0
                        && !IsExampleLogic(node)
                        && !synapseIndicationIds.Contains(node.Id))
                    .OrderBy(node => StableSeed($"{detail.Summary.Hsim}:{node.Id}:provider-attestation"))
                    .Take(3)
                    .Select(node => new ProviderAttestationEvidence(
                        IndicationId: node.Id,
                        IndicationName: node.Text,
                        Category: string.IsNullOrWhiteSpace(node.LogicText) ? "Provider-attested criterion" : node.LogicText,
                        Attested: true,
                        SourceDocument: $"{detail.Summary.Code} - {detail.Summary.Title}"))
                    .ToList();
                var providerAttestations = indications
                    .ToDictionary(
                        indication => indication.IndicationId,
                        indication => indication.Precision >= 88m,
                        StringComparer.OrdinalIgnoreCase);

                foreach (var evidence in providerOnlyEvidence)
                {
                    providerAttestations[evidence.IndicationId] = evidence.Attested;
                }

                return new AuthorizationRequest(
                    Id: $"AUTH-{index + 1001}",
                    MemberSegment: segments[index % segments.Length],
                    ServiceLine: detail.Summary.ProductCode.Equals("AC", StringComparison.OrdinalIgnoreCase)
                        ? "Elective procedure"
                        : "Inpatient admission",
                    CaseType: detail.Summary.GuidelineType.Equals("auth", StringComparison.OrdinalIgnoreCase)
                        ? "Elective"
                        : "Emergent",
                    GuidelineId: detail.Summary.Hsim,
                    GuidelineCode: detail.Summary.Code,
                    GuidelineName: detail.Summary.Title,
                    ReceivedAt: now.AddMinutes(-15 * (index + 1)),
                    ProviderAttestations: providerAttestations,
                    ProviderAttestationEvidence: providerOnlyEvidence,
                    SynapseResults: indications);
            })
            .Where(request => request.SynapseResults.Count > 0)
            .ToList();
    }

    private IEnumerable<string> GetGuidelineFiles()
    {
        if (!Directory.Exists(_guidelineDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_guidelineDirectory, "*.xml", SearchOption.TopDirectoryOnly);
    }

    private ObjectiveGuidelineDetail BuildGuideline(
        string path,
        IReadOnlyDictionary<string, PerformanceRow> performanceRows,
        ObjectiveMetricMode metricMode)
    {
        var document = XDocument.Load(path);
        var guideline = document.Descendants().FirstOrDefault(element => IsNamed(element, "Guideline"))
            ?? throw new InvalidOperationException($"Guideline metadata was not found in '{Path.GetFileName(path)}'.");
        var sections = FindAutoAuthorizationSections(document).ToList();
        var baseNodes = BuildSectionNodes(sections);
        var nodeIds = FlattenNodes(baseNodes).Select(node => node.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var matchedIds = nodeIds.Where(performanceRows.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usesSampleMetrics = metricMode == ObjectiveMetricMode.Sample;
        var rawTitle = FirstNonEmpty(
            AttributeValue(guideline, "Title"),
            DirectChildText(guideline, "title"),
            Path.GetFileNameWithoutExtension(path));
        var hsim = FirstNonEmpty(AttributeValue(guideline, "HSIM"), Path.GetFileNameWithoutExtension(path));
        var nodes = baseNodes.Select(node => AttachMetrics(node, hsim, performanceRows, metricMode)).ToList();
        var topLevelUsageMetrics = FirstTopLevelMetric(nodes);
        var performanceMetrics = FirstTopLevelPerformanceMetric(nodes) ?? (usesSampleMetrics
            ? AggregateSampleMetrics(nodes)
            : AggregatePerformanceMetrics(matchedIds.Select(id => performanceRows[id])));
        var metrics = MergeReviewerUsage(performanceMetrics, topLevelUsageMetrics);
        var code = FirstNonEmpty(
            AttributeValue(guideline, "GCode"),
            AttributeValue(guideline, "mcg"),
            DirectChildText(guideline, "mcg"));
        var productCode = FirstNonEmpty(
            AttributeValue(guideline, "ProductCode"),
            AttributeValue(guideline, "product"));
        var guidelineType = FirstNonEmpty(
            AttributeValue(guideline, "GuidelineType"),
            AttributeValue(guideline, "type"),
            AttributeValue(guideline, "mcgtype"));
        var version = FirstNonEmpty(
            AttributeValue(guideline, "VersionNumber"),
            AttributeValue(guideline, "version"),
            ExtractRevisionVersion(rawTitle));
        var summary = new ObjectiveGuidelineSummary(
            Id: hsim,
            Hsim: hsim,
            Code: code,
            Title: CleanTitle(rawTitle),
            RawTitle: rawTitle,
            ProductCode: productCode,
            GuidelineType: guidelineType,
            Version: version,
            Glos: EmptyToNull(AttributeValue(guideline, "GLOS")),
            FileName: Path.GetFileName(path),
            AutoAuthorizationSectionCount: sections.Count,
            IndicationCount: nodeIds.Count,
            MatchedIndicationCount: matchedIds.Count,
            HasPerformanceData: usesSampleMetrics || matchedIds.Count > 0,
            UsesSampleMetrics: usesSampleMetrics);

        return new ObjectiveGuidelineDetail(summary, metrics, nodes);
    }

    private static IReadOnlyList<XElement> FindAutoAuthorizationSections(XDocument document)
    {
        return document
            .Descendants()
            .Where(element => IsGuidelineSection(element) && IsTrue(AttributeValue(element, "isautoauthorization")))
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
                    LogicType: null,
                    LogicText: null,
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
            LogicType: ExtractLogicType(para),
            LogicText: ExtractRequirement(para),
            Metrics: null,
            Items: children);
    }

    private static ObjectiveGuidelineNode AttachMetrics(
        ObjectiveGuidelineNode node,
        string hsim,
        IReadOnlyDictionary<string, PerformanceRow> performanceRows,
        ObjectiveMetricMode metricMode,
        int depth = 0)
    {
        ObjectiveGuidelineMetricSet? performanceMetrics = null;
        if (metricMode == ObjectiveMetricMode.Sample)
        {
            performanceMetrics = SampleMetric(hsim, node.Id);
        }
        else if (performanceRows.TryGetValue(node.Id, out var row))
        {
            performanceMetrics = MetricFromPerformanceRow(row);
        }

        var metrics = WithProjectedReviewerUsage(performanceMetrics, hsim, node.Id, depth);

        return node with
        {
            Metrics = metrics,
            Items = node.Items.Select(child => AttachMetrics(child, hsim, performanceRows, metricMode, depth + 1)).ToList()
        };
    }

    private static ObjectiveGuidelineMetricSet? FirstTopLevelMetric(IEnumerable<ObjectiveGuidelineNode> nodes)
    {
        return nodes.Select(node => node.Metrics).FirstOrDefault(metric => metric is not null);
    }

    private static ObjectiveGuidelineMetricSet? FirstTopLevelPerformanceMetric(IEnumerable<ObjectiveGuidelineNode> nodes)
    {
        return nodes
            .Select(node => node.Metrics)
            .FirstOrDefault(metric => metric is not null && HasPerformanceMetric(metric));
    }

    private static ObjectiveGuidelineMetricSet? MergeReviewerUsage(
        ObjectiveGuidelineMetricSet? metrics,
        ObjectiveGuidelineMetricSet? usageMetrics)
    {
        if (metrics is null)
        {
            return usageMetrics;
        }

        if (usageMetrics is null)
        {
            return metrics;
        }

        return metrics with
        {
            ProviderSelectionRate = usageMetrics.ProviderSelectionRate,
            PayerSelectionRate = usageMetrics.PayerSelectionRate,
            ProviderAndPayerSelectionRate = usageMetrics.ProviderAndPayerSelectionRate,
            UsageIsProjected = metrics.UsageIsProjected || usageMetrics.UsageIsProjected
        };
    }

    private static bool HasPerformanceMetric(ObjectiveGuidelineMetricSet metric)
    {
        return metric.MetAi is not null
            || metric.Confidence is not null
            || metric.AgreementAgree is not null
            || metric.AgreementDisagree is not null
            || metric.Recall is not null
            || metric.TruePositive is not null
            || metric.FalsePositive is not null
            || metric.TrueNegative is not null
            || metric.FalseNegative is not null
            || metric.TotalCases is not null;
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

    private static ObjectiveGuidelinePreviewGroup BuildPreviewGroup(
        ObjectiveGuidelineDetail detail,
        decimal precisionThreshold,
        bool useConfidenceThreshold,
        decimal confidenceThreshold)
    {
        var evaluations = detail.Nodes
            .Select(node => EvaluatePreviewNode(node, precisionThreshold, useConfidenceThreshold, confidenceThreshold))
            .ToList();
        var nodes = evaluations
            .Select(evaluation => evaluation.Node)
            .Where(node => node is not null)
            .Cast<ObjectiveGuidelinePreviewNode>()
            .ToList();

        return new ObjectiveGuidelinePreviewGroup(
            Hsim: detail.Summary.Hsim,
            Code: detail.Summary.Code,
            Title: detail.Summary.Title,
            PathwayCount: evaluations.Sum(evaluation => evaluation.PathwayCount),
            PrecisionQualifiedCount: evaluations.Sum(evaluation => evaluation.PrecisionQualifiedCount),
            ConfidenceQualifiedCount: evaluations.Sum(evaluation => evaluation.ConfidenceQualifiedCount),
            Nodes: nodes);
    }

    private static PreviewEvaluation EvaluatePreviewNode(
        ObjectiveGuidelineNode node,
        decimal precisionThreshold,
        bool useConfidenceThreshold,
        decimal confidenceThreshold,
        bool forceInclude = false)
    {
        var precision = node.Metrics?.AgreementAgree;
        var confidence = node.Metrics?.Confidence;
        var precisionQualified = precision is not null && precision >= precisionThreshold;
        var confidenceQualified = !useConfidenceThreshold || (confidence is not null && confidence >= confidenceThreshold);
        var isExample = IsExampleLogic(node);

        if (node.Items.Count == 0)
        {
            var triggerable = !isExample && precisionQualified && confidenceQualified;
            var included = forceInclude || triggerable || precisionQualified;
            return new PreviewEvaluation(
                Node: included
                    ? ToPreviewNode(node, precisionQualified, confidenceQualified, isExample, triggerable, triggerable ? 1 : 0, [])
                    : null,
                GatePassed: triggerable,
                PathwayCount: triggerable ? 1 : 0,
                PrecisionQualifiedCount: precisionQualified ? 1 : 0,
                ConfidenceQualifiedCount: precisionQualified && confidenceQualified ? 1 : 0);
        }

        var childEvaluations = node.Items
            .Select(child => EvaluatePreviewNode(child, precisionThreshold, useConfidenceThreshold, confidenceThreshold))
            .ToList();
        var anyChildRelevant = childEvaluations.Any(evaluation => evaluation.Node is not null || evaluation.GatePassed);

        if (IsAllLogic(node) && anyChildRelevant)
        {
            childEvaluations = node.Items
                .Select(child => EvaluatePreviewNode(child, precisionThreshold, useConfidenceThreshold, confidenceThreshold, forceInclude: !IsExampleLogic(child)))
                .ToList();
        }

        var requiredChildEvaluations = node.Items
            .Zip(childEvaluations)
            .Where(pair => !IsExampleLogic(pair.First))
            .Select(pair => pair.Second)
            .ToList();
        var childNodes = childEvaluations
            .Select(evaluation => evaluation.Node)
            .Where(previewNode => previewNode is not null)
            .Cast<ObjectiveGuidelinePreviewNode>()
            .ToList();
        var nodePrecisionQualified = precisionQualified || childEvaluations.Any(evaluation => evaluation.PrecisionQualifiedCount > 0);
        var nodeConfidenceQualified = !useConfidenceThreshold || childEvaluations.Any(evaluation => evaluation.ConfidenceQualifiedCount > 0);
        var gatePassed = false;
        var pathwayCount = 0;

        if (!isExample && IsAllLogic(node))
        {
            gatePassed = requiredChildEvaluations.Count > 0 && requiredChildEvaluations.All(evaluation => evaluation.GatePassed);
            pathwayCount = gatePassed ? 1 : 0;
        }
        else if (!isExample && IsOneOrMoreLogic(node))
        {
            pathwayCount = childEvaluations.Sum(evaluation => evaluation.PathwayCount);
            gatePassed = pathwayCount > 0;
        }
        else if (!isExample)
        {
            pathwayCount = childEvaluations.Sum(evaluation => evaluation.PathwayCount);
            gatePassed = pathwayCount > 0;
        }

        var includeNode = forceInclude || childNodes.Count > 0 || gatePassed || precisionQualified;
        return new PreviewEvaluation(
            Node: includeNode
                ? ToPreviewNode(node, nodePrecisionQualified, nodeConfidenceQualified, isExample, gatePassed, pathwayCount, childNodes)
                : null,
            GatePassed: gatePassed,
            PathwayCount: pathwayCount,
            PrecisionQualifiedCount: (precisionQualified ? 1 : 0) + childEvaluations.Sum(evaluation => evaluation.PrecisionQualifiedCount),
            ConfidenceQualifiedCount: (precisionQualified && confidenceQualified ? 1 : 0) + childEvaluations.Sum(evaluation => evaluation.ConfidenceQualifiedCount));
    }

    private static ObjectiveGuidelinePreviewNode ToPreviewNode(
        ObjectiveGuidelineNode node,
        bool precisionQualified,
        bool confidenceQualified,
        bool isExample,
        bool triggerable,
        int pathwayCount,
        List<ObjectiveGuidelinePreviewNode> items)
    {
        return new ObjectiveGuidelinePreviewNode(
            Id: node.Id,
            Type: node.Type,
            Text: node.Text,
            LogicType: node.LogicType,
            LogicText: node.LogicText,
            Precision: node.Metrics?.AgreementAgree,
            Confidence: node.Metrics?.Confidence,
            IsExample: isExample,
            IsTriggerable: triggerable,
            IsPrecisionQualified: precisionQualified,
            IsConfidenceQualified: confidenceQualified,
            PathwayCount: pathwayCount,
            Items: items);
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
            ProviderSelectionRate: null,
            PayerSelectionRate: null,
            ProviderAndPayerSelectionRate: null,
            UsageIsProjected: false,
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
            ProviderSelectionRate: Math.Round(metrics.Average(metric => metric.ProviderSelectionRate ?? 0m), 1),
            PayerSelectionRate: Math.Round(metrics.Average(metric => metric.PayerSelectionRate ?? 0m), 1),
            ProviderAndPayerSelectionRate: Math.Round(metrics.Average(metric => metric.ProviderAndPayerSelectionRate ?? 0m), 1),
            UsageIsProjected: metrics.Any(metric => metric.UsageIsProjected),
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
            ProviderSelectionRate: null,
            PayerSelectionRate: null,
            ProviderAndPayerSelectionRate: null,
            UsageIsProjected: false,
            IsSample: false);
    }

    private static ObjectiveGuidelineMetricSet SampleMetric(string hsim, string id)
    {
        var seed = StableSeed($"{hsim}:{id}:precision");
        var bucket = seed % 100;
        var tenths = (seed / 100) % 100;
        var precision = bucket switch
        {
            < 24 => 80m + Math.Round(tenths / 10m, 1),
            < 58 => 90m + Math.Round(tenths % 50 / 10m, 1),
            _ => 95m + Math.Round(tenths % 43 / 10m, 1)
        };

        return new ObjectiveGuidelineMetricSet(
            MetAi: 18m + seed % 62,
            Confidence: ProjectedConfidence(hsim, id),
            AgreementAgree: precision,
            AgreementDisagree: 100m - precision,
            Recall: 78m + seed % 20,
            TruePositive: null,
            FalsePositive: null,
            TrueNegative: null,
            FalseNegative: null,
            TotalCases: null,
            ProviderSelectionRate: null,
            PayerSelectionRate: null,
            ProviderAndPayerSelectionRate: null,
            UsageIsProjected: false,
            IsSample: true);
    }

    private static ObjectiveGuidelineMetricSet WithProjectedReviewerUsage(
        ObjectiveGuidelineMetricSet? metrics,
        string hsim,
        string id,
        int depth)
    {
        var usage = ProjectedReviewerUsage(hsim, id, depth);
        return (metrics ?? EmptyMetric()) with
        {
            ProviderSelectionRate = usage.ProviderSelectionRate,
            PayerSelectionRate = usage.PayerSelectionRate,
            ProviderAndPayerSelectionRate = usage.ProviderAndPayerSelectionRate,
            UsageIsProjected = true
        };
    }

    private static ObjectiveGuidelineMetricSet EmptyMetric()
    {
        return new ObjectiveGuidelineMetricSet(
            MetAi: null,
            Confidence: null,
            AgreementAgree: null,
            AgreementDisagree: null,
            Recall: null,
            TruePositive: null,
            FalsePositive: null,
            TrueNegative: null,
            FalseNegative: null,
            TotalCases: null,
            ProviderSelectionRate: null,
            PayerSelectionRate: null,
            ProviderAndPayerSelectionRate: null,
            UsageIsProjected: false,
            IsSample: false);
    }

    private static ReviewerUsageRates ProjectedReviewerUsage(string hsim, string id, int depth)
    {
        var safeDepth = Math.Max(0, depth);
        var depthFactor = (decimal)Math.Pow(0.66d, Math.Pow(safeDepth, 1.22d));
        var providerSeed = StableSeed($"{hsim}:{id}:provider-usage");
        var payerSeed = StableSeed($"{hsim}:{id}:payer-usage");
        var overlapSeed = StableSeed($"{hsim}:{id}:provider-payer-overlap");
        var providerJitter = providerSeed % 190 / 10m - 8.5m;
        var payerJitter = payerSeed % 170 / 10m - 7m;
        var providerSelectionRate = ClampPercent(Math.Round(4m + 68m * depthFactor + providerJitter, 1), 1m, 92m);
        var payerSelectionRate = ClampPercent(Math.Round(3m + 58m * depthFactor + payerJitter, 1), 1m, 90m);
        var overlapShare = 0.36m + overlapSeed % 29 / 100m;
        var providerAndPayerSelectionRate = ClampPercent(
            Math.Round(Math.Min(providerSelectionRate, payerSelectionRate) * overlapShare - safeDepth * 0.6m, 1),
            0m,
            Math.Min(providerSelectionRate, payerSelectionRate));

        return new ReviewerUsageRates(
            ProviderSelectionRate: providerSelectionRate,
            PayerSelectionRate: payerSelectionRate,
            ProviderAndPayerSelectionRate: providerAndPayerSelectionRate);
    }

    private static decimal ClampPercent(decimal value, decimal min, decimal max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static decimal ProjectedConfidence(string hsim, string id)
    {
        var seed = StableSeed($"{hsim}:{id}:confidence");
        var bucket = seed % 100;
        var tenths = (seed / 100) % 100;

        return bucket switch
        {
            < 20 => 78m + Math.Round(tenths % 90 / 10m, 1),
            < 55 => 87m + Math.Round(tenths % 80 / 10m, 1),
            _ => 94m + Math.Round(tenths % 52 / 10m, 1)
        };
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

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string ExtractRevisionVersion(string title)
    {
        var match = Regex.Match(title, @"\bRevision\s+([0-9.]+)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
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

    private static string? ExtractLogicType(XElement? para)
    {
        if (para is null)
        {
            return null;
        }

        var logic = para.Descendants().FirstOrDefault(element => IsNamed(element, "listlogic"));
        return EmptyToNull(AttributeValue(logic, "type"));
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

    private static bool IsOneOrMoreLogic(ObjectiveGuidelineNode node)
    {
        return node.LogicType == "1" || (node.LogicText?.Contains("1 or more", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsAllLogic(ObjectiveGuidelineNode node)
    {
        return node.LogicType?.Equals("A", StringComparison.OrdinalIgnoreCase) == true
            || (node.LogicText?.Contains("ALL", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsExampleLogic(ObjectiveGuidelineNode node)
    {
        return node.LogicType?.Equals("E", StringComparison.OrdinalIgnoreCase) == true
            || (node.LogicText?.Contains("examples include", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static ObjectiveMetricMode ResolveMetricMode(string? metricMode)
    {
        return metricMode?.Equals("real", StringComparison.OrdinalIgnoreCase) == true
            ? ObjectiveMetricMode.Real
            : ObjectiveMetricMode.Sample;
    }

    private static string MetricModeName(ObjectiveMetricMode metricMode)
    {
        return metricMode == ObjectiveMetricMode.Real ? "real" : "sample";
    }

    private static string AttributeValue(XElement? element, string name)
    {
        return element?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
    }

    private enum ObjectiveMetricMode
    {
        Sample,
        Real
    }

    private sealed record PreviewEvaluation(
        ObjectiveGuidelinePreviewNode? Node,
        bool GatePassed,
        int PathwayCount,
        int PrecisionQualifiedCount,
        int ConfidenceQualifiedCount);

    private sealed record PerformanceRow(
        string Uid,
        decimal Precision,
        decimal Recall,
        int TruePositive,
        int FalsePositive,
        int TrueNegative,
        int FalseNegative,
        int TotalCases);

    private sealed record ReviewerUsageRates(
        decimal ProviderSelectionRate,
        decimal PayerSelectionRate,
        decimal ProviderAndPayerSelectionRate);
}
