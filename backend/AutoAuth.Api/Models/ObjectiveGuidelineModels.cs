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
    string? LogicType,
    string? LogicText,
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
    decimal? ProviderSelectionRate,
    decimal? PayerSelectionRate,
    decimal? ProviderAndPayerSelectionRate,
    bool UsageIsProjected,
    bool IsSample);

public sealed record ObjectiveGuidelinePreview(
    decimal PrecisionThreshold,
    bool UseConfidenceThreshold,
    decimal ConfidenceThreshold,
    bool UseSynapseUtilizationRateFilter,
    string UtilizationReferenceSource,
    decimal SynapseUtilizationDelta,
    string MetricMode,
    int GuidelineCount,
    int PathwayCount,
    int PrecisionQualifiedCount,
    int ConfidenceQualifiedCount,
    int UtilizationQualifiedCount,
    List<ObjectiveGuidelinePreviewGroup> Guidelines);

public sealed record ObjectiveGuidelinePreviewGroup(
    string Hsim,
    string Code,
    string Title,
    int PathwayCount,
    int PrecisionQualifiedCount,
    int ConfidenceQualifiedCount,
    int UtilizationQualifiedCount,
    List<ObjectiveGuidelinePreviewNode> Nodes);

public sealed record ObjectiveGuidelinePreviewNode(
    string Id,
    string Type,
    string Text,
    string? LogicType,
    string? LogicText,
    decimal? Precision,
    decimal? Confidence,
    decimal? MetAi,
    decimal? UtilizationReferenceRate,
    decimal? SynapseUtilizationDifference,
    bool IsExample,
    bool IsTriggerable,
    bool IsPrecisionQualified,
    bool IsConfidenceQualified,
    bool IsUtilizationQualified,
    int PathwayCount,
    List<ObjectiveGuidelinePreviewNode> Items);
