# AutoAuth Rules Engine Prototype - requirements

## Vision

The AutoAuth Rules Engine Prototype is a local demo app for exploring how MCG Path auto-authorization rules could be configured, simulated, explained, and evaluated before any production rules-engine rebuild.

The prototype has two related jobs:

1. Show how customer-configurable AutoAuth rules can turn Synapse indication outputs into an authorization decision.
2. Show how objective indication criteria from guideline XML can be rendered with performance metrics so product, clinical, and customer-facing teams can discuss trust, precision, recall, and guideline coverage using the same screen.

The current app is a scaffold and simulator, not a production authorization engine. It uses local demo requests, local guideline XML exports, and a local precision/recall workbook.

## Users

- Daniel and MCG product stakeholders evaluating AutoAuth rule behavior.
- Clinical, data science, and implementation partners reviewing whether objective indication criteria and model-performance metrics are understandable enough for demos and planning.
- Engineers using the prototype to understand product intent before building production services.

## Core Workflows

### 1. Configure AutoAuth rules

The app must let a prototype user view and edit local rule definitions. A rule defines when a demo authorization request should auto-approve or remain in manual review.

Rules support:

- A human-readable name and description.
- Enabled or disabled state.
- Priority order.
- Decision action: auto-approve or pend for review.
- Member segment scope.
- Service line scope.
- Optional guideline scope.
- Precision / user agreement staging filter.
- Optional Synapse confidence staging filter as a secondary narrowing signal.
- Optional Synapse versus existing utilization-rate staging filter, with one selected reviewer reference rate.
- Rule-specific Medical Necessity Bucket membership.
- Mode-specific settings such as minimum pathways.

Rule changes are local only. They update in-memory state in the local API and are not persisted after the backend process stops.

Threshold controls are filters for finding candidate pathways. They do not place anything into the rule by themselves. A pathway becomes part of a rule only after the user deliberately adds it to that rule's Medical Necessity Bucket and saves the rule.

The optional **Synapse versus existing utilization rate** filter compares `# Met (AI)` to exactly one reference rate: payer selected (default), provider selected, or payer-provider overlap. It is off by default. Its signed whole-percentage-point slider ranges from `-100` to `+100` and passes a pathway when:

`AI met rate - selected utilization rate <= configured delta`

For example, `+5` allows Synapse to find an indication up to 5 percentage points more often than the selected reference; `-5` requires it to find the indication at least 5 points less often. If either metric is missing, a pathway does not match while the filter is enabled. Like confidence, this is staging context only: changing it never removes a saved bucket item or changes how that item evaluates.

### 2. Simulate authorization decisions

The app must include demo authorization requests that can be evaluated against the active rule set. Each request contains:

- Request metadata such as member segment, service line, case type, guideline ID, and guideline name.
- Provider attestations by indication.
- Synapse indication results with indication name, category, objective flag, confidence, pathway-met status, evidence snippet, and source document.

When the user runs an evaluation, the app must:

- Evaluate every rule in priority order.
- Record all condition checks for every rule, even if the rule does not fire.
- Return a single decision: auto-approved or pended for review.
- Show which rule or rules caused the decision.
- Show which saved Medical Necessity Bucket pathways were met for the request.
- Save the evaluation in the local audit trail for the current session.

### 3. Stage and manage Medical Necessity Bucket pathways

The app must let a prototype user use precision, optional confidence, and optional Synapse-versus-utilization filters to find candidate guideline pathways, then deliberately add selected candidates to a rule-specific Medical Necessity Bucket.

Bucket behavior:

- The bucket is scoped to one rule, not global.
- A unique bucket item is identified by `HSIM + pathway node ID`.
- Each bucket item stores HSIM, GCode/title display fields, pathway node ID, pathway text, list logic text/type, precision/confidence snapshot, and added timestamp.
- `ALL` bucket items may also store child evidence rows with the selected evidence source, metric snapshots, threshold snapshots, and added timestamp.
- Matching triggerable pathways are checked by default in the staging drawer.
- Adding selected pathways skips duplicates.
- Removing a pathway updates the local draft immediately.
- Saving the rule persists the current draft bucket in the local API.
- If a saved bucket item falls below the current filters or metric mode, keep it in the bucket and show a drift warning instead of removing it automatically.
- A saved below-threshold Synapse exception is shown as an intentional exception, not as an accidental drift warning. If the metric later clears the threshold, show it as a previous exception that now meets threshold.

The bucket is the committed rule input. Slider settings remain useful as last-used staging filters and demo context, but they are not the rule itself.

### 4. Explain rule execution

A demo user must be able to understand why a request did or did not auto-approve. For each rule, the app must show:

- Whether the rule was enabled.
- Whether the request matched the rule's member segment, service line, and guideline scope.
- Whether saved Medical Necessity Bucket pathways matched and were met.
- For mixed-evidence `ALL` pathways, which child evidence sources passed or failed.
- The last precision, optional confidence, and optional Synapse-versus-utilization staging filters as context.
- Whether the rule fired.
- Which action the rule took if it fired.

Rules that are disabled should still appear in the execution trace, but disabled rules must not fire.

### 5. Render objective indication criteria from guideline XML

The app must render objective AutoAuth indication criteria from real guideline XML exports in `Guideline XMLs/`.

Guideline display rules:

- A guideline XML file is eligible if it can be parsed as a guideline export.
- Display titles should remove the standalone word "copy" when that appears in exported filenames or guideline titles.
- Sections should be selected by `isautoauthorization="true"` on guideline section elements, not by title matching.
- AutoAuth indication sections should render as nested guideline criteria, preserving parent/child structure.
- Group rows should be expandable.
- Leaf rows should display individual indication criteria.

The rule configuration and simulator use guideline HSIM values as backend identifiers and display GCodes with titles in the UI.

### 6. Merge guideline criteria with precision and recall data

The objective indications screen must use the local performance workbook in `Precision Recall Data/` when metrics are available.

Performance matching rules:

- The default demo mode uses deterministic projected sample metrics for all parsed XML indication rows.
- A dev-only metric mode can overlay real precision / recall values from the workbook for comparison.
- Match XML indication IDs to workbook `uid` values.
- Prefer rows from `Indications vs Agreed Cases`.
- Use `Indication Performance` as a fallback source.
- For matched indications, populate the available metrics from the workbook.
- For unmatched indications in a guideline that has some matched rows, show no data rather than inventing row-level metrics.
- If an entire guideline has no matched performance rows, populate deterministic sample metrics so the screen remains demoable.
- When sample metrics are used, show a single guideline-level "Sample metrics" badge, not a badge on every row.

Guideline-level metric cards should match the first top-level parent row metric when that parent row has metrics. This prevents confusion during demos where the top cards and the first visible parent criteria row appear to be summarizing the same thing. Aggregate guideline metrics may be used only as a fallback when there is no top-level parent metric.

### 7. Define objective performance terms

The app must label user agreement and precision as the same concept for this demo.

Definitions:

- **Precision / User Agreement:** Of what Synapse selected, how much the human also selected.
- **Recall:** Of what the human selected, how much Synapse also selected.

The definitions should be available near the corresponding metric labels, especially column headers and top metric cards.

### 8. Show projected reviewer usage

The objective indications screen must show projected reviewer selection rates for each parsed indication row.

Usage metric definitions:

- **Provider selected:** percentage of reviewed cases where the provider selected the indication.
- **Payer selected:** percentage of reviewed cases where the payer selected the indication.
- **Provider + payer:** percentage of reviewed cases where both provider and payer selected the indication.

Usage projection rules:

- Usage values are deterministic projected demo values until real provider/payer selection data is available.
- Projection should generally decrease as an indication appears deeper in the XML tree.
- The decrease should be non-linear and should include stable row-level variation, so lower indications may occasionally have higher usage than nearby rows.
- Provider + payer must never exceed either the provider-selected rate or the payer-selected rate.
- Usage values are context metrics, not quality metrics, so lower usage should not be styled as a failure.

### 9. Highlight metric quality consistently

Metric tone should be consistent across guideline-level cards and row-level metrics.

Current threshold rules:

- Green / positive: 95% or higher.
- Yellow / warning: 80% through 94.9%.
- Red / negative: below 80%.
- Neutral: metric is missing.

`# Met (AI)` is descriptive volume context and may use an informational tone rather than quality thresholds.

Synapse confidence is currently projected sample data. It remains in the UI as a secondary signal, but precision / user agreement is the primary automation metric for this prototype.

### 10. Preserve local-only data behavior

The prototype must remain local-only for now.

- Demo rule definitions live in memory.
- Demo authorization requests live in memory.
- Evaluation history lives in memory.
- Guideline XML files are read from the local repository folder.
- Precision/recall data is read from the local repository workbook.
- No production backend, customer database, live Synapse call, or PHI persistence is in scope for this prototype phase.

## Rule Behavior

### Precision threshold mode

A precision-threshold rule fires when at least one saved Medical Necessity Bucket pathway:

- Is in scope for the rule.
- Has pathway-met status.

For a mixed-evidence `ALL` bucket item, pathway-met status means every saved child evidence row passed.

The configured precision threshold, optional Synapse confidence threshold, and optional Synapse-versus-utilization filter are staging filters for finding candidates. They are stored with the rule as last-used filter settings, but they do not cause auto-approval unless the pathway has been added to the bucket.

### Mixed-evidence ALL pathways

An incomplete `ALL of the following` pathway may be added to the Medical Necessity Bucket only when every required non-example child has an explicit saved evidence source:

- **Synapse:** the Synapse-produced pathway result must be met.
- **Synapse exception:** the Synapse-produced pathway result must be met, and the saved exception remains auditable even when precision is below the current threshold.
- **Provider attestation:** provider attestation for that child must be true, and Synapse pathway-met status is not required for that child.

Provider attestation and below-threshold Synapse exceptions are explicit per child. They are not broad rule-level toggles.

### Data point combination mode

A data-point-combination rule fires when at least one saved Medical Necessity Bucket pathway has both:

- Provider attestation.
- Pathway-met status.

This mode represents agreement between provider-supplied data and Synapse extraction.

### Pathway threshold mode

A pathway-threshold rule fires when the number of met Medical Necessity Bucket pathways meets or exceeds the configured minimum.

This mode supports customer rules that require more than the base guideline threshold before auto-approval.

### Decision precedence

Manual-review guardrails must win over auto-approval. If any fired rule takes the pend-for-review action, the final decision is pended for review.

If no active auto-approval rule fires, the final decision is pended for review.

## Current Non-Goals

- No production authorization workflow.
- No live rules persistence.
- No user authentication or customer tenancy model.
- No live Synapse service call.
- No backend import of arbitrary new workbook schemas beyond the current scaffold.
- No direct reuse of Vue MUCL or DTR renderer components in React. The React app should port the recursive rendering pattern and visual language.
- No production-grade bucket publishing, approval workflow, versioning, or rollback yet.

## Success Criteria

The prototype is successful when a demo user can:

- Configure rule behavior without code changes.
- Run a demo request and understand why the decision happened.
- Review an audit trail of local evaluations.
- Search guideline XML exports and inspect objective AutoAuth criteria.
- See precision/user-agreement and recall metrics beside the guideline criteria when workbook data exists.
- Understand when metrics are sample data versus matched workbook data.
- Discuss objective indication trust using the guideline-like screen without needing to inspect XML or Excel directly.
