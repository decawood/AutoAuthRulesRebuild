using AutoAuth.Api.Models;

namespace AutoAuth.Api.Services;

public sealed class PrototypeStore
{
    private readonly List<RuleDefinition> _rules = SeedRules();
    private readonly List<AuthorizationRequest> _requests;
    private readonly List<EvaluationResult> _evaluations = [];

    public PrototypeStore(ObjectiveGuidelineService guidelines)
    {
        _requests = guidelines.DemoRequests().ToList();
        SeedMedicalNecessityBuckets(_rules, _requests);
    }

    public IReadOnlyList<RuleDefinition> Rules => _rules;
    public IReadOnlyList<AuthorizationRequest> Requests => _requests;
    public IReadOnlyList<EvaluationResult> Evaluations => _evaluations;

    public PrototypeSnapshot Snapshot()
    {
        return new PrototypeSnapshot(Dashboard(), [.. _rules.OrderBy(rule => rule.Priority)], [.. _requests], [.. _evaluations.OrderByDescending(evaluation => evaluation.EvaluatedAt)]);
    }

    public PrototypeDashboard Dashboard()
    {
        var autoApproved = _evaluations.Count(evaluation => evaluation.Decision == AuthorizationDecision.AutoApproved);
        var latestRate = _evaluations.Count == 0 ? 0 : Math.Round((decimal)autoApproved / _evaluations.Count * 100, 1);

        return new PrototypeDashboard(
            ActiveRules: _rules.Count(rule => rule.Enabled),
            DemoRequests: _requests.Count,
            EvaluationsRun: _evaluations.Count,
            LatestAutoApprovalRate: latestRate,
            TargetAutoApprovalRate: "80%",
            DeploymentModel: "Standalone local API",
            DataRetention: "In-memory only");
    }

    public RuleDefinition? GetRule(string id)
    {
        return _rules.FirstOrDefault(rule => rule.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public AuthorizationRequest? GetRequest(string id)
    {
        return _requests.FirstOrDefault(request => request.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public RuleDefinition UpdateRule(string id, RuleUpdateRequest update)
    {
        var existing = GetRule(id);
        if (existing is null)
        {
            throw new InvalidOperationException($"Rule '{id}' was not found.");
        }

        existing.Name = update.Name;
        existing.Description = update.Description;
        existing.Mode = update.Mode;
        existing.Action = update.Action;
        existing.Priority = update.Priority;
        existing.Enabled = update.Enabled;
        existing.MemberSegments = update.MemberSegments;
        existing.ServiceLines = update.ServiceLines;
        existing.GuidelineIds = update.GuidelineIds;
        existing.EligibleIndicationIds = update.EligibleIndicationIds;
        existing.MedicalNecessityBucket = DeduplicateBucket(update.MedicalNecessityBucket);
        existing.PrecisionThreshold = update.PrecisionThreshold;
        existing.UseConfidenceThreshold = update.UseConfidenceThreshold;
        existing.ConfidenceThreshold = update.ConfidenceThreshold;
        existing.UseSynapseUtilizationRateFilter = update.UseSynapseUtilizationRateFilter;
        existing.UtilizationReferenceSource = UtilizationReferenceSources.Normalize(update.UtilizationReferenceSource);
        existing.SynapseUtilizationDelta = ClampSynapseUtilizationDelta(update.SynapseUtilizationDelta);
        existing.RequireProviderAttestation = update.RequireProviderAttestation;
        existing.MinimumPathways = update.MinimumPathways;
        existing.UpdatedBy = string.IsNullOrWhiteSpace(update.UpdatedBy) ? "Prototype admin" : update.UpdatedBy;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        return existing;
    }

    public EvaluationResult SaveEvaluation(EvaluationResult result)
    {
        _evaluations.Add(result);
        return result;
    }

    private static List<RuleDefinition> SeedRules()
    {
        return
        [
            new RuleDefinition
            {
                Id = "rule-precision-95",
                Name = "High-precision medical necessity",
                Description = "Auto-approve Medicare and Commercial requests when at least one saved bucket pathway is met.",
                Mode = RuleMode.ConfidenceThreshold,
                Action = RuleAction.AutoApprove,
                Priority = 10,
                MemberSegments = ["Medicare", "Commercial"],
                ServiceLines = ["Elective procedure", "Inpatient admission"],
                PrecisionThreshold = 95,
                ConfidenceThreshold = 90,
                UpdatedBy = "Daniel"
            },
            new RuleDefinition
            {
                Id = "rule-attestation-precision",
                Name = "Provider attestation plus high precision",
                Description = "Auto-approve when provider attestation lines up with a saved bucket pathway.",
                Mode = RuleMode.DataPointCombination,
                Action = RuleAction.AutoApprove,
                Priority = 20,
                MemberSegments = ["Medicare", "Commercial", "Medicaid"],
                ServiceLines = ["Elective procedure", "Inpatient admission"],
                PrecisionThreshold = 90,
                UseConfidenceThreshold = true,
                ConfidenceThreshold = 88,
                RequireProviderAttestation = true,
                UpdatedBy = "Daniel"
            },
            new RuleDefinition
            {
                Id = "rule-two-pathways",
                Name = "Two-pathway inpatient threshold",
                Description = "Auto-approve inpatient admission requests only when at least two saved bucket pathways are met.",
                Mode = RuleMode.PathwayThreshold,
                Action = RuleAction.AutoApprove,
                Priority = 30,
                MemberSegments = ["Medicare", "Medicaid"],
                ServiceLines = ["Inpatient admission"],
                MinimumPathways = 2,
                PrecisionThreshold = 88,
                ConfidenceThreshold = 85,
                UpdatedBy = "Daniel"
            },
            new RuleDefinition
            {
                Id = "rule-manual-review-example",
                Name = "Manual-review guardrail example",
                Description = "Disabled example guardrail for showing that manual-review rules still appear in execution traces without firing.",
                Mode = RuleMode.ConfidenceThreshold,
                Action = RuleAction.PendForReview,
                Priority = 40,
                Enabled = false,
                MemberSegments = ["Medicare", "Commercial", "Medicaid"],
                ServiceLines = ["Inpatient admission", "Elective procedure"],
                PrecisionThreshold = 92,
                ConfidenceThreshold = 92,
                UpdatedBy = "Daniel"
            }
        ];
    }

    private static void SeedMedicalNecessityBuckets(List<RuleDefinition> rules, IReadOnlyList<AuthorizationRequest> requests)
    {
        var firstRule = rules.FirstOrDefault(rule => rule.Id == "rule-precision-95");
        if (firstRule is not null)
        {
            firstRule.MedicalNecessityBucket = requests
                .SelectMany(request => request.SynapseResults
                    .Where(result => result.Precision >= 95m && result.PathwayMet)
                    .OrderByDescending(result => result.Precision)
                    .Take(2)
                    .Select(result => BucketItemFromRequest(request, result)))
                .Take(5)
                .ToList();
        }

        var attestationRule = rules.FirstOrDefault(rule => rule.Id == "rule-attestation-precision");
        if (attestationRule is not null)
        {
            attestationRule.MedicalNecessityBucket = requests
                .SelectMany(request => request.SynapseResults
                    .Where(result => result.Precision >= 90m && request.ProviderAttestations.GetValueOrDefault(result.IndicationId))
                    .OrderByDescending(result => result.Precision)
                    .Take(1)
                    .Select(result => BucketItemFromRequest(request, result)))
                .Take(4)
                .ToList();
        }
    }

    private static MedicalNecessityBucketItem BucketItemFromRequest(AuthorizationRequest request, SynapseIndicationResult result)
    {
        return new MedicalNecessityBucketItem(
            Hsim: request.GuidelineId,
            GuidelineCode: request.GuidelineCode,
            GuidelineTitle: request.GuidelineName,
            PathwayId: result.IndicationId,
            PathwayText: result.IndicationName,
            LogicType: null,
            LogicText: string.IsNullOrWhiteSpace(result.Category) ? null : result.Category,
            Precision: result.Precision,
            Confidence: result.Confidence,
            AddedAt: DateTimeOffset.UtcNow);
    }

    private static List<MedicalNecessityBucketItem> DeduplicateBucket(IEnumerable<MedicalNecessityBucketItem> items)
    {
        return items
            .GroupBy(item => $"{item.Hsim}::{item.PathwayId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static decimal ClampSynapseUtilizationDelta(decimal value)
    {
        return Math.Round(Math.Clamp(value, -100m, 100m), 0, MidpointRounding.AwayFromZero);
    }
}
