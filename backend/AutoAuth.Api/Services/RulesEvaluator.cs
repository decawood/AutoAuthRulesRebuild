using AutoAuth.Api.Models;

namespace AutoAuth.Api.Services;

public sealed class RulesEvaluator(PrototypeStore store)
{
    public EvaluationResult Evaluate(string requestId)
    {
        var request = store.GetRequest(requestId) ?? throw new InvalidOperationException($"Authorization request '{requestId}' was not found.");
        var executions = new List<RuleExecutionEntry>();

        foreach (var rule in store.Rules.OrderBy(rule => rule.Priority))
        {
            executions.Add(EvaluateRule(rule, request));
        }

        var firedActions = executions
            .Where(execution => execution.Fired)
            .Select(execution => execution.ActionTaken)
            .ToList();

        var decision = firedActions.Contains(RuleAction.PendForReview)
            ? AuthorizationDecision.PendedForReview
            : firedActions.Contains(RuleAction.AutoApprove)
                ? AuthorizationDecision.AutoApproved
                : AuthorizationDecision.PendedForReview;

        var firedRuleNames = executions
            .Where(execution => execution.Fired)
            .Select(execution => execution.RuleName)
            .ToList();

        var summary = decision == AuthorizationDecision.AutoApproved
            ? $"Auto-approved because {string.Join(", ", firedRuleNames)} fired."
            : firedRuleNames.Count == 0
                ? "Pended because no active auto-approval rule fired."
                : $"Pended because {string.Join(", ", firedRuleNames)} fired a manual-review guardrail.";

        var medicallyNecessaryBucket = request.SynapseResults
            .Where(result => result.Confidence >= 90 && result.PathwayMet)
            .OrderByDescending(result => result.Confidence)
            .Select(result => $"{result.IndicationName} ({result.Confidence:0}% confidence)")
            .ToList();

        return store.SaveEvaluation(new EvaluationResult(
            Id: $"EVAL-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            EvaluatedAt: DateTimeOffset.UtcNow,
            Decision: decision,
            DecisionSummary: summary,
            Request: request,
            RuleExecutions: executions,
            MedicallyNecessaryBucket: medicallyNecessaryBucket,
            PhiRetentionStatement: "Prototype evaluation is stateless: request details stay in memory only and are cleared when the API stops."));
    }

    private static RuleExecutionEntry EvaluateRule(RuleDefinition rule, AuthorizationRequest request)
    {
        var conditions = new List<ConditionResult>
        {
            new(
                Label: "Rule status",
                Expected: "Enabled",
                Actual: rule.Enabled ? "Enabled" : "Disabled",
                Passed: rule.Enabled,
                Detail: rule.Enabled ? "Rule is available for evaluation." : "Disabled rules are logged but cannot fire."),
            ListCondition("Member segment", rule.MemberSegments, request.MemberSegment),
            ListCondition("Service line", rule.ServiceLines, request.ServiceLine),
            ListCondition("Guideline", rule.GuidelineIds, request.GuidelineId, emptyMeansAny: true)
        };

        conditions.AddRange(rule.Mode switch
        {
            RuleMode.ConfidenceThreshold => EvaluateConfidenceThreshold(rule, request),
            RuleMode.DataPointCombination => EvaluateDataPointCombination(rule, request),
            RuleMode.PathwayThreshold => EvaluatePathwayThreshold(rule, request),
            _ => []
        });

        var fired = conditions.All(condition => condition.Passed);

        return new RuleExecutionEntry(
            RuleId: rule.Id,
            RuleName: rule.Name,
            Mode: rule.Mode,
            Priority: rule.Priority,
            Fired: fired,
            ActionTaken: fired ? rule.Action : null,
            Conditions: conditions);
    }

    private static ConditionResult ListCondition(string label, IReadOnlyCollection<string> allowedValues, string actualValue, bool emptyMeansAny = false)
    {
        if (emptyMeansAny && allowedValues.Count == 0)
        {
            return new ConditionResult(label, "Any", actualValue, true, "Rule does not limit this field.");
        }

        var passed = allowedValues.Contains(actualValue, StringComparer.OrdinalIgnoreCase);
        return new ConditionResult(
            Label: label,
            Expected: string.Join(", ", allowedValues),
            Actual: actualValue,
            Passed: passed,
            Detail: passed ? $"{actualValue} is in scope for this rule." : $"{actualValue} is outside this rule's configured scope.");
    }

    private static List<ConditionResult> EvaluateConfidenceThreshold(RuleDefinition rule, AuthorizationRequest request)
    {
        var eligibleResults = FilterEligibleIndications(rule, request).ToList();
        var qualifying = eligibleResults
            .Where(result => result.Confidence >= rule.ConfidenceThreshold && result.PathwayMet)
            .OrderByDescending(result => result.Confidence)
            .ToList();

        return
        [
            new(
                Label: "Eligible indications",
                Expected: rule.EligibleIndicationIds.Count == 0 ? "Any indication" : string.Join(", ", rule.EligibleIndicationIds),
                Actual: eligibleResults.Count == 0 ? "None" : string.Join(", ", eligibleResults.Select(result => result.IndicationName)),
                Passed: eligibleResults.Count > 0,
                Detail: "Eligible indications define the pool that can enter the medically necessary bucket."),
            new(
                Label: "Synapse confidence bucket",
                Expected: $">= {rule.ConfidenceThreshold:0}% and pathway met",
                Actual: qualifying.Count == 0 ? "No qualifying indications" : string.Join(", ", qualifying.Select(result => $"{result.IndicationName} {result.Confidence:0}%")),
                Passed: qualifying.Count > 0,
                Detail: qualifying.Count > 0 ? "At least one indication met the confidence threshold." : "No indication met both confidence and pathway requirements.")
        ];
    }

    private static List<ConditionResult> EvaluateDataPointCombination(RuleDefinition rule, AuthorizationRequest request)
    {
        var eligibleResults = FilterEligibleIndications(rule, request).ToList();
        var attestedAndSupported = eligibleResults
            .Where(result => request.ProviderAttestations.TryGetValue(result.IndicationId, out var attested)
                && attested
                && result.Confidence >= rule.ConfidenceThreshold
                && result.PathwayMet)
            .OrderByDescending(result => result.Confidence)
            .ToList();

        return
        [
            new(
                Label: "Provider attestation required",
                Expected: rule.RequireProviderAttestation ? "Yes" : "No",
                Actual: rule.RequireProviderAttestation ? "Yes" : "No",
                Passed: true,
                Detail: "This prototype mode combines provider attestation and Synapse support for the same indication."),
            new(
                Label: "Attestation plus Synapse agreement",
                Expected: $"Provider attested and Synapse >= {rule.ConfidenceThreshold:0}%",
                Actual: attestedAndSupported.Count == 0 ? "No matching indication" : string.Join(", ", attestedAndSupported.Select(result => $"{result.IndicationName} {result.Confidence:0}%")),
                Passed: attestedAndSupported.Count > 0,
                Detail: attestedAndSupported.Count > 0 ? "Provider and Synapse agree on at least one eligible indication." : "No eligible indication had both provider attestation and Synapse support.")
        ];
    }

    private static List<ConditionResult> EvaluatePathwayThreshold(RuleDefinition rule, AuthorizationRequest request)
    {
        var qualifying = FilterEligibleIndications(rule, request)
            .Where(result => result.PathwayMet && result.Confidence >= rule.ConfidenceThreshold)
            .OrderByDescending(result => result.Confidence)
            .ToList();

        return
        [
            new(
                Label: "Pathways met",
                Expected: $">= {rule.MinimumPathways} pathways at {rule.ConfidenceThreshold:0}% confidence",
                Actual: $"{qualifying.Count} pathways met",
                Passed: qualifying.Count >= rule.MinimumPathways,
                Detail: qualifying.Count == 0 ? "No pathway met this rule's threshold." : string.Join(", ", qualifying.Select(result => result.IndicationName)))
        ];
    }

    private static IEnumerable<SynapseIndicationResult> FilterEligibleIndications(RuleDefinition rule, AuthorizationRequest request)
    {
        if (rule.EligibleIndicationIds.Count == 0)
        {
            return request.SynapseResults;
        }

        return request.SynapseResults.Where(result => rule.EligibleIndicationIds.Contains(result.IndicationId, StringComparer.OrdinalIgnoreCase));
    }
}
