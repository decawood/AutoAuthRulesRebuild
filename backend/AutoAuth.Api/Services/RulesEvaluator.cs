using AutoAuth.Api.Models;

namespace AutoAuth.Api.Services;

public sealed class RulesEvaluator(PrototypeStore store, ObjectiveGuidelineService guidelines)
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

        var medicallyNecessaryBucket = store.Rules
            .Where(rule => rule.Enabled && rule.Action == RuleAction.AutoApprove)
            .SelectMany(rule => BucketPathways(rule, request))
            .Where(pathway => pathway.PathwayMet)
            .GroupBy(pathway => $"{pathway.Item.Hsim}::{pathway.Item.PathwayId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(pathway => pathway.Precision ?? 0m)
            .Select(pathway => $"{pathway.DisplayName} ({FormatOptionalPercent(pathway.Precision)} precision)")
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

    private RuleExecutionEntry EvaluateRule(RuleDefinition rule, AuthorizationRequest request)
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
            RuleMode.ConfidenceThreshold => EvaluatePrecisionThreshold(rule, request),
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

    private List<ConditionResult> EvaluatePrecisionThreshold(RuleDefinition rule, AuthorizationRequest request)
    {
        var bucketPathways = BucketPathways(rule, request).ToList();
        var qualifying = bucketPathways
            .Where(pathway => pathway.PathwayMet)
            .OrderByDescending(pathway => pathway.Precision ?? 0m)
            .ToList();
        var conditions = new List<ConditionResult>
        {
            new(
                Label: "Medical necessity bucket",
                Expected: rule.MedicalNecessityBucket.Count == 0 ? "At least one selected pathway" : $"{rule.MedicalNecessityBucket.Count} selected pathway(s)",
                Actual: bucketPathways.Count == 0 ? "No selected pathways on this request" : string.Join(", ", bucketPathways.Select(pathway => pathway.DisplayName)),
                Passed: bucketPathways.Count > 0,
                Detail: "Only pathways deliberately added to this rule's bucket can drive auto-approval."),
            new(
                Label: "Bucket pathway status",
                Expected: "Selected pathway is met",
                Actual: qualifying.Count == 0 ? "No selected pathways are met" : string.Join(", ", qualifying.Select(pathway => $"{pathway.DisplayName} met")),
                Passed: qualifying.Count > 0,
                Detail: qualifying.Count > 0 ? "At least one selected bucket pathway is met." : "Selected pathways remain staged, but none are met for this request.")
        };

        conditions.AddRange(ChildEvidenceConditions(bucketPathways));
        conditions.AddRange(StagingFilterConditions(rule, bucketPathways));

        return conditions;
    }

    private List<ConditionResult> EvaluateDataPointCombination(RuleDefinition rule, AuthorizationRequest request)
    {
        var bucketPathways = BucketPathways(rule, request).ToList();
        var attestedAndSupported = bucketPathways
            .Where(pathway => HasProviderAttestationSupport(pathway, request)
                && pathway.PathwayMet)
            .OrderByDescending(pathway => pathway.Precision ?? 0m)
            .ToList();

        var conditions = new List<ConditionResult>
        {
            new(
                Label: "Provider attestation required",
                Expected: rule.RequireProviderAttestation ? "Yes" : "No",
                Actual: rule.RequireProviderAttestation ? "Yes" : "No",
                Passed: true,
                Detail: "This prototype mode combines provider attestation and Synapse support for the same indication."),
            new(
                Label: "Attestation plus Synapse agreement",
                Expected: "Provider attested and selected bucket pathway is met",
                Actual: attestedAndSupported.Count == 0 ? "No matching indication" : string.Join(", ", attestedAndSupported.Select(pathway => $"{pathway.DisplayName} {FormatOptionalPercent(pathway.Precision)}")),
                Passed: attestedAndSupported.Count > 0,
                Detail: attestedAndSupported.Count > 0 ? "Provider and Synapse agree on at least one selected bucket pathway." : "No selected bucket pathway had both provider attestation and pathway support.")
        };

        conditions.AddRange(ChildEvidenceConditions(bucketPathways));
        conditions.AddRange(StagingFilterConditions(rule, bucketPathways));
        return conditions;
    }

    private List<ConditionResult> EvaluatePathwayThreshold(RuleDefinition rule, AuthorizationRequest request)
    {
        var bucketPathways = BucketPathways(rule, request).ToList();
        var qualifying = bucketPathways
            .Where(pathway => pathway.PathwayMet)
            .OrderByDescending(pathway => pathway.Precision ?? 0m)
            .ToList();

        var conditions = new List<ConditionResult>
        {
            new(
                Label: "Pathways met",
                Expected: $">= {rule.MinimumPathways} selected bucket pathways",
                Actual: $"{qualifying.Count} pathways met",
                Passed: qualifying.Count >= rule.MinimumPathways,
                Detail: qualifying.Count == 0 ? "No saved bucket pathway was met." : string.Join(", ", qualifying.Select(pathway => pathway.DisplayName)))
        };

        conditions.AddRange(ChildEvidenceConditions(bucketPathways));
        conditions.AddRange(StagingFilterConditions(rule, bucketPathways));
        return conditions;
    }

    private static IEnumerable<ConditionResult> StagingFilterConditions(RuleDefinition rule, IReadOnlyList<BucketPathwayEvaluation> bucketPathways)
    {
        yield return new ConditionResult(
            Label: "Precision filter setting",
            Expected: $"Last staging filter >= {rule.PrecisionThreshold:0.#}% precision",
            Actual: bucketPathways.Count == 0 ? "No bucket pathways on this request" : string.Join(", ", bucketPathways.Select(pathway => $"{pathway.DisplayName} {FormatOptionalPercent(pathway.Precision)}")),
            Passed: true,
            Detail: "Precision is used to find and add candidates to the bucket. Saved bucket membership drives this evaluation.");

        if (rule.UseConfidenceThreshold)
        {
            var confidenceQualified = bucketPathways
                .Where(pathway => pathway.Confidence >= rule.ConfidenceThreshold)
                .OrderByDescending(pathway => pathway.Confidence ?? 0m)
                .ToList();

            yield return new ConditionResult(
                Label: "Synapse confidence filter",
                Expected: $">= {rule.ConfidenceThreshold:0.#}% confidence",
                Actual: confidenceQualified.Count == 0 ? "No confidence-qualified pathways" : string.Join(", ", confidenceQualified.Select(pathway => $"{pathway.DisplayName} {FormatOptionalPercent(pathway.Confidence)}")),
                Passed: true,
                Detail: "Confidence is a secondary staging filter. It does not remove saved bucket pathways during evaluation.");
        }

        if (!rule.UseSynapseUtilizationRateFilter)
        {
            yield break;
        }

        yield return new ConditionResult(
            Label: "Synapse vs existing utilization rate filter",
            Expected: $"# Met (AI) - {UtilizationReferenceLabel(rule.UtilizationReferenceSource)} <= {rule.SynapseUtilizationDelta:+0.#;-0.#;0} pp",
            Actual: "Configured for staging only",
            Passed: true,
            Detail: "This comparison was used to find candidates before they were saved to the bucket. It does not remove saved bucket pathways during evaluation.");
    }

    private static string UtilizationReferenceLabel(string source)
    {
        return UtilizationReferenceSources.Normalize(source) switch
        {
            UtilizationReferenceSources.Provider => "Provider selected",
            UtilizationReferenceSources.PayerProviderOverlap => "Payer-provider overlap",
            _ => "Payer selected"
        };
    }

    private IEnumerable<BucketPathwayEvaluation> BucketPathways(RuleDefinition rule, AuthorizationRequest request)
    {
        var resultsById = request.SynapseResults.ToDictionary(result => result.IndicationId, StringComparer.OrdinalIgnoreCase);

        foreach (var item in rule.MedicalNecessityBucket.Where(item => item.Hsim.Equals(request.GuidelineId, StringComparison.OrdinalIgnoreCase)))
        {
            if (item.ChildEvidence.Count > 0)
            {
                yield return EvaluateMixedEvidencePathway(item, request, resultsById);
                continue;
            }

            if (resultsById.TryGetValue(item.PathwayId, out var result))
            {
                yield return new BucketPathwayEvaluation(
                    Item: item,
                    DisplayName: result.IndicationName,
                    Precision: result.Precision,
                    Confidence: result.Confidence,
                    PathwayMet: result.PathwayMet,
                    RequiredLeafIds: [result.IndicationId],
                    ChildEvidence: []);
                continue;
            }

            if (TryEvaluateGuidelineGroup(item, request, resultsById, out var groupPathway))
            {
                yield return groupPathway;
            }
        }
    }

    private static BucketPathwayEvaluation EvaluateMixedEvidencePathway(
        MedicalNecessityBucketItem item,
        AuthorizationRequest request,
        IReadOnlyDictionary<string, SynapseIndicationResult> resultsById)
    {
        var childEvidence = item.ChildEvidence
            .Select(child =>
            {
                resultsById.TryGetValue(child.PathwayId, out var result);
                var attested = request.ProviderAttestations.GetValueOrDefault(child.PathwayId);
                var passed = child.EvidenceSource switch
                {
                    MedicalNecessityEvidenceSource.ProviderAttestation => attested,
                    MedicalNecessityEvidenceSource.Synapse => result?.PathwayMet == true,
                    MedicalNecessityEvidenceSource.SynapseException => result?.PathwayMet == true,
                    _ => false
                };

                var actual = child.EvidenceSource switch
                {
                    MedicalNecessityEvidenceSource.ProviderAttestation => attested ? "Provider attested" : "Provider did not attest",
                    MedicalNecessityEvidenceSource.Synapse => result?.PathwayMet == true ? "Synapse-produced pathway met" : "Synapse-produced pathway not met",
                    MedicalNecessityEvidenceSource.SynapseException => result?.PathwayMet == true ? "Saved exception pathway met" : "Saved exception pathway not met",
                    _ => "Unknown evidence source"
                };

                return new BucketChildEvidenceEvaluation(
                    Evidence: child,
                    Passed: passed,
                    Actual: actual);
            })
            .ToList();

        return new BucketPathwayEvaluation(
            Item: item,
            DisplayName: item.PathwayText,
            Precision: item.Precision,
            Confidence: item.Confidence,
            PathwayMet: childEvidence.Count > 0 && childEvidence.All(child => child.Passed),
            RequiredLeafIds: childEvidence.Select(child => child.Evidence.PathwayId).ToList(),
            ChildEvidence: childEvidence);
    }

    private bool TryEvaluateGuidelineGroup(
        MedicalNecessityBucketItem item,
        AuthorizationRequest request,
        IReadOnlyDictionary<string, SynapseIndicationResult> resultsById,
        out BucketPathwayEvaluation groupPathway)
    {
        groupPathway = default!;

        ObjectiveGuidelineNode? node;
        try
        {
            node = FindGuidelineNode(guidelines.Detail(request.GuidelineId).Nodes, item.PathwayId);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (node is null || node.Items.Count == 0)
        {
            return false;
        }

        var requiredLeafIds = RequiredLeafIds(node).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requiredLeafIds.Count == 0)
        {
            return false;
        }

        var pathwayMet = IsAllLogic(node)
            ? requiredLeafIds.All(id => resultsById.TryGetValue(id, out var result) && result.PathwayMet)
            : requiredLeafIds.Any(id => resultsById.TryGetValue(id, out var result) && result.PathwayMet);

        groupPathway = new BucketPathwayEvaluation(
            Item: item,
            DisplayName: item.PathwayText,
            Precision: item.Precision,
            Confidence: item.Confidence,
            PathwayMet: pathwayMet,
            RequiredLeafIds: requiredLeafIds,
            ChildEvidence: []);
        return true;
    }

    private static IEnumerable<ConditionResult> ChildEvidenceConditions(IEnumerable<BucketPathwayEvaluation> bucketPathways)
    {
        foreach (var pathway in bucketPathways.Where(pathway => pathway.ChildEvidence.Count > 0))
        {
            foreach (var child in pathway.ChildEvidence)
            {
                yield return new ConditionResult(
                    Label: $"ALL child evidence - {child.Evidence.PathwayText}",
                    Expected: EvidenceSourceLabel(child.Evidence.EvidenceSource),
                    Actual: child.Actual,
                    Passed: child.Passed,
                    Detail: ChildEvidenceDetail(child.Evidence));
            }
        }
    }

    private static bool HasProviderAttestationSupport(BucketPathwayEvaluation pathway, AuthorizationRequest request)
    {
        if (pathway.ChildEvidence.Any(child => child.Evidence.EvidenceSource == MedicalNecessityEvidenceSource.ProviderAttestation && child.Passed))
        {
            return true;
        }

        return pathway.RequiredLeafIds.Any(id => request.ProviderAttestations.GetValueOrDefault(id));
    }

    private static string ChildEvidenceDetail(MedicalNecessityChildEvidence evidence)
    {
        return evidence.EvidenceSource switch
        {
            MedicalNecessityEvidenceSource.ProviderAttestation => "This ALL child is explicitly allowed to be satisfied by provider attestation.",
            MedicalNecessityEvidenceSource.SynapseException => $"Saved Synapse exception. Precision was {FormatOptionalPercent(evidence.Precision)} against a {evidence.PrecisionThreshold:0.#}% staging threshold when saved.",
            MedicalNecessityEvidenceSource.Synapse => "This ALL child is satisfied by the saved Synapse-produced pathway result.",
            _ => "Unknown evidence source."
        };
    }

    private static string EvidenceSourceLabel(string evidenceSource)
    {
        return evidenceSource switch
        {
            MedicalNecessityEvidenceSource.ProviderAttestation => "Provider attestation",
            MedicalNecessityEvidenceSource.SynapseException => "Synapse exception",
            MedicalNecessityEvidenceSource.Synapse => "Synapse",
            _ => evidenceSource
        };
    }

    private static ObjectiveGuidelineNode? FindGuidelineNode(IEnumerable<ObjectiveGuidelineNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var child = FindGuidelineNode(node.Items, id);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static IEnumerable<string> RequiredLeafIds(ObjectiveGuidelineNode node)
    {
        foreach (var child in node.Items)
        {
            foreach (var id in RequiredLeafIdsIncludingNode(child))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<string> RequiredLeafIdsIncludingNode(ObjectiveGuidelineNode node)
    {
        if (IsExampleLogic(node))
        {
            yield break;
        }

        if (node.Items.Count == 0)
        {
            yield return node.Id;
            yield break;
        }

        foreach (var child in node.Items)
        {
            foreach (var id in RequiredLeafIdsIncludingNode(child))
            {
                yield return id;
            }
        }
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

    private static string FormatOptionalPercent(decimal? value)
    {
        return value is null ? "No metric" : $"{value:0.#}%";
    }

    private sealed record BucketPathwayEvaluation(
        MedicalNecessityBucketItem Item,
        string DisplayName,
        decimal? Precision,
        decimal? Confidence,
        bool PathwayMet,
        IReadOnlyList<string> RequiredLeafIds,
        IReadOnlyList<BucketChildEvidenceEvaluation> ChildEvidence);

    private sealed record BucketChildEvidenceEvaluation(
        MedicalNecessityChildEvidence Evidence,
        bool Passed,
        string Actual);
}
