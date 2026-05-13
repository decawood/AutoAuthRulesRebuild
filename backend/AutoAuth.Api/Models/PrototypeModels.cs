namespace AutoAuth.Api.Models;

public enum RuleMode
{
    ConfidenceThreshold,
    DataPointCombination,
    PathwayThreshold
}

public enum RuleAction
{
    AutoApprove,
    PendForReview
}

public enum AuthorizationDecision
{
    AutoApproved,
    PendedForReview
}

public sealed record SynapseIndicationResult(
    string IndicationId,
    string IndicationName,
    string Category,
    bool IsObjective,
    decimal Confidence,
    bool PathwayMet,
    string EvidenceSnippet,
    string SourceDocument);

public sealed record AuthorizationRequest(
    string Id,
    string MemberSegment,
    string ServiceLine,
    string CaseType,
    string GuidelineId,
    string GuidelineName,
    DateTimeOffset ReceivedAt,
    Dictionary<string, bool> ProviderAttestations,
    List<SynapseIndicationResult> SynapseResults);

public sealed class RuleDefinition
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public RuleMode Mode { get; set; }
    public RuleAction Action { get; set; } = RuleAction.AutoApprove;
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string> MemberSegments { get; set; } = [];
    public List<string> ServiceLines { get; set; } = [];
    public List<string> GuidelineIds { get; set; } = [];
    public List<string> EligibleIndicationIds { get; set; } = [];
    public decimal ConfidenceThreshold { get; set; } = 90;
    public bool RequireProviderAttestation { get; set; }
    public int MinimumPathways { get; set; } = 1;
    public string UpdatedBy { get; set; } = "Prototype admin";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record RuleUpdateRequest(
    string Name,
    string Description,
    RuleMode Mode,
    RuleAction Action,
    int Priority,
    bool Enabled,
    List<string> MemberSegments,
    List<string> ServiceLines,
    List<string> GuidelineIds,
    List<string> EligibleIndicationIds,
    decimal ConfidenceThreshold,
    bool RequireProviderAttestation,
    int MinimumPathways,
    string UpdatedBy);

public sealed record EvaluationRequest(string RequestId);

public sealed record ShutdownRequest(bool Confirm = false);

public sealed record ConditionResult(
    string Label,
    string Expected,
    string Actual,
    bool Passed,
    string Detail);

public sealed record RuleExecutionEntry(
    string RuleId,
    string RuleName,
    RuleMode Mode,
    int Priority,
    bool Fired,
    RuleAction? ActionTaken,
    List<ConditionResult> Conditions);

public sealed record EvaluationResult(
    string Id,
    DateTimeOffset EvaluatedAt,
    AuthorizationDecision Decision,
    string DecisionSummary,
    AuthorizationRequest Request,
    List<RuleExecutionEntry> RuleExecutions,
    List<string> MedicallyNecessaryBucket,
    string PhiRetentionStatement);

public sealed record PrototypeDashboard(
    int ActiveRules,
    int DemoRequests,
    int EvaluationsRun,
    decimal LatestAutoApprovalRate,
    string TargetAutoApprovalRate,
    string DeploymentModel,
    string DataRetention);

public sealed record PrototypeSnapshot(
    PrototypeDashboard Dashboard,
    List<RuleDefinition> Rules,
    List<AuthorizationRequest> Requests,
    List<EvaluationResult> Evaluations);
