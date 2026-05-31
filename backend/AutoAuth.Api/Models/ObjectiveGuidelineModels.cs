namespace AutoAuth.Api.Models;

public sealed record ObjectiveGuidelineSummary(
    string Id,
    string Hsim,
    string Code,
    string Title,
    string RawTitle,
    string ProductCode,
    string GuidelineType,
    string Version,
    string? Glos,
    string FileName,
    int AutoAuthorizationSectionCount,
    int IndicationCount,
    int MatchedIndicationCount,
    bool HasPerformanceData,
    bool UsesSampleMetrics);

public sealed record ObjectiveGuidelineDetail(
    ObjectiveGuidelineSummary Summary,
    ObjectiveGuidelineMetricSet? Metrics,
    List<ObjectiveGuidelineNode> Nodes);

public sealed record ObjectiveGuidelineNode(
    string Id,
    string Type,
    string Text,
    string? Requirement,
    ObjectiveGuidelineMetricSet? Metrics,
    List<ObjectiveGuidelineNode> Items);

public sealed record ObjectiveGuidelineMetricSet(
    decimal? MetAi,
    decimal? Confidence,
    decimal? AgreementAgree,
    decimal? AgreementDisagree,
    decimal? Recall,
    int? TruePositive,
    int? FalsePositive,
    int? TrueNegative,
    int? FalseNegative,
    int? TotalCases,
    bool IsSample);
