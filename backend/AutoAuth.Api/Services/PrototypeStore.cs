using AutoAuth.Api.Models;

namespace AutoAuth.Api.Services;

public sealed class PrototypeStore
{
    private readonly List<RuleDefinition> _rules = SeedRules();
    private readonly List<AuthorizationRequest> _requests = SeedRequests();
    private readonly List<EvaluationResult> _evaluations = [];

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
        existing.ConfidenceThreshold = update.ConfidenceThreshold;
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
                Id = "rule-confidence-95",
                Name = "High-confidence medical necessity",
                Description = "Auto-approve Medicare and Commercial requests when Synapse places at least one eligible indication in the medically necessary bucket.",
                Mode = RuleMode.ConfidenceThreshold,
                Action = RuleAction.AutoApprove,
                Priority = 10,
                MemberSegments = ["Medicare", "Commercial"],
                ServiceLines = ["Elective procedure", "Inpatient admission"],
                ConfidenceThreshold = 95,
                UpdatedBy = "Daniel"
            },
            new RuleDefinition
            {
                Id = "rule-attestation-synapse",
                Name = "Provider attestation plus Synapse agreement",
                Description = "Auto-approve when the provider attests to an objective indication and Synapse agrees with high confidence.",
                Mode = RuleMode.DataPointCombination,
                Action = RuleAction.AutoApprove,
                Priority = 20,
                MemberSegments = ["Medicare", "Commercial", "Medicaid"],
                ServiceLines = ["Elective procedure", "Inpatient admission"],
                EligibleIndicationIds = ["hypoxemia", "failed-conservative-therapy", "progressive-neuro-deficit"],
                ConfidenceThreshold = 90,
                RequireProviderAttestation = true,
                UpdatedBy = "Daniel"
            },
            new RuleDefinition
            {
                Id = "rule-two-pathways",
                Name = "Two-pathway inpatient threshold",
                Description = "Auto-approve inpatient admission requests only when at least two qualifying pathways are met.",
                Mode = RuleMode.PathwayThreshold,
                Action = RuleAction.AutoApprove,
                Priority = 30,
                MemberSegments = ["Medicare", "Medicaid"],
                ServiceLines = ["Inpatient admission"],
                MinimumPathways = 2,
                ConfidenceThreshold = 82,
                UpdatedBy = "Daniel"
            },
            new RuleDefinition
            {
                Id = "rule-subjective-review",
                Name = "Subjective indication review guardrail",
                Description = "Keep cases with only subjective indications in manual review until customer risk tolerance is validated.",
                Mode = RuleMode.ConfidenceThreshold,
                Action = RuleAction.PendForReview,
                Priority = 40,
                MemberSegments = ["Medicare", "Commercial", "Medicaid"],
                ServiceLines = ["Inpatient admission", "Elective procedure"],
                EligibleIndicationIds = ["altered-mental-status", "pain-severe-persistent"],
                ConfidenceThreshold = 92,
                UpdatedBy = "Daniel"
            }
        ];
    }

    private static List<AuthorizationRequest> SeedRequests()
    {
        var now = DateTimeOffset.UtcNow;

        return
        [
            new AuthorizationRequest(
                Id: "AUTH-1001",
                MemberSegment: "Medicare",
                ServiceLine: "Inpatient admission",
                CaseType: "Emergent",
                GuidelineId: "M-160",
                GuidelineName: "Respiratory Failure Admission",
                ReceivedAt: now.AddMinutes(-42),
                ProviderAttestations: new Dictionary<string, bool>
                {
                    ["hypoxemia"] = true,
                    ["altered-mental-status"] = false,
                    ["tachypnea"] = true
                },
                SynapseResults:
                [
                    new SynapseIndicationResult("hypoxemia", "Hypoxemia", "Objective", true, 97, true, "Oxygen saturation documented at 86% on room air.", "ED triage note"),
                    new SynapseIndicationResult("tachypnea", "Tachypnea", "Objective", true, 91, true, "Respiratory rate recorded at 32 breaths per minute.", "Vitals flowsheet"),
                    new SynapseIndicationResult("altered-mental-status", "Altered mental status, severe or persistent", "Subjective", false, 64, false, "Nursing note mentions confusion but no persistence documented.", "Nursing assessment")
                ]),
            new AuthorizationRequest(
                Id: "AUTH-1002",
                MemberSegment: "Commercial",
                ServiceLine: "Elective procedure",
                CaseType: "Elective",
                GuidelineId: "S-430",
                GuidelineName: "Lumbar Spine Procedure",
                ReceivedAt: now.AddMinutes(-35),
                ProviderAttestations: new Dictionary<string, bool>
                {
                    ["failed-conservative-therapy"] = true,
                    ["progressive-neuro-deficit"] = false,
                    ["pain-severe-persistent"] = true
                },
                SynapseResults:
                [
                    new SynapseIndicationResult("failed-conservative-therapy", "Failed conservative therapy", "Objective", true, 94, true, "Six weeks of physical therapy and NSAID trial documented.", "Orthopedic clinic note"),
                    new SynapseIndicationResult("progressive-neuro-deficit", "Progressive neurologic deficit", "Objective", true, 72, false, "No worsening motor deficit found in latest exam.", "Neurology consult"),
                    new SynapseIndicationResult("pain-severe-persistent", "Severe persistent pain", "Subjective", false, 88, true, "Patient reports persistent 8/10 pain despite treatment.", "Pain assessment")
                ]),
            new AuthorizationRequest(
                Id: "AUTH-1003",
                MemberSegment: "Medicaid",
                ServiceLine: "Inpatient admission",
                CaseType: "Emergent",
                GuidelineId: "M-280",
                GuidelineName: "Neurologic Event Admission",
                ReceivedAt: now.AddMinutes(-28),
                ProviderAttestations: new Dictionary<string, bool>
                {
                    ["progressive-neuro-deficit"] = true,
                    ["altered-mental-status"] = true,
                    ["hypoxemia"] = false
                },
                SynapseResults:
                [
                    new SynapseIndicationResult("progressive-neuro-deficit", "Progressive neurologic deficit", "Objective", true, 87, true, "Exam documents worsening unilateral weakness.", "Neurology consult"),
                    new SynapseIndicationResult("altered-mental-status", "Altered mental status, severe or persistent", "Subjective", false, 93, true, "Confusion persisted across two documented assessments.", "Hospitalist H&P"),
                    new SynapseIndicationResult("hypoxemia", "Hypoxemia", "Objective", true, 31, false, "Oxygen saturation remained above 95%.", "Vitals flowsheet")
                ]),
            new AuthorizationRequest(
                Id: "AUTH-1004",
                MemberSegment: "Commercial",
                ServiceLine: "Elective procedure",
                CaseType: "Elective",
                GuidelineId: "S-880",
                GuidelineName: "Advanced Imaging",
                ReceivedAt: now.AddMinutes(-18),
                ProviderAttestations: new Dictionary<string, bool>
                {
                    ["red-flag-symptoms"] = false,
                    ["failed-conservative-therapy"] = false,
                    ["prior-imaging-inconclusive"] = true
                },
                SynapseResults:
                [
                    new SynapseIndicationResult("red-flag-symptoms", "Red flag symptoms", "Objective", true, 46, false, "No red flag symptoms found.", "Primary care note"),
                    new SynapseIndicationResult("failed-conservative-therapy", "Failed conservative therapy", "Objective", true, 58, false, "Only two weeks of conservative management documented.", "Primary care note"),
                    new SynapseIndicationResult("prior-imaging-inconclusive", "Prior imaging inconclusive", "Objective", true, 96, true, "Prior x-ray report recommends advanced imaging for clarification.", "Radiology report")
                ])
        ];
    }
}
