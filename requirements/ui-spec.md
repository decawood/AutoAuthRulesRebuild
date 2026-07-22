# UI specification

Companion to [requirements.md](requirements.md). This document defines what each screen looks like and how Daniel interacts with it. It is the frontend build guide.

**Scope:** This document covers layout, visual elements, component behavior, interaction flows, and screen states. Product rules, metric definitions, and decision behavior live in requirements.md and are referenced here instead of repeated.

## Overall Navigation

The app header displays **AutoAuth Rules Engine Prototype** with an **MCG Path** eyebrow. The header also shows local status badges and a **Shut down** button.

The main navigation has four top-level tabs:

1. **Rule configuration** - configure local AutoAuth rule definitions.
2. **Simulator** - select a demo authorization request and run an evaluation.
3. **Objective indications** - inspect AutoAuth indication criteria parsed from guideline XML files and paired with performance metrics.
4. **Audit trail** - review evaluations run during the current local session.

The active tab uses the MUCL-style tab button treatment. Tabs should remain compact and scannable.

## App Chrome

### Header

The header contains:

- Eyebrow: **MCG Path**.
- H1: **AutoAuth Rules Engine Prototype**.
- Deployment badge from the dashboard, currently **Standalone local API**.
- Data-retention badge from the dashboard, currently **In-memory only**.
- **Shut down** action.

Clicking **Shut down** asks for browser confirmation. If confirmed, the frontend calls the local shutdown endpoint, then replaces the app with a stopped-state panel.

### Metric Strip

The metric strip appears below the header and above the tab navigation. It displays:

- Active rules.
- Demo requests.
- Evaluations run.
- Auto-approval rate, with target rate helper text.

The strip is informational and should not be interactive.

### Status Line

The app displays a status line after saves, evaluations, loading failures, and other local API operations.

Status states:

- Busy: operation in progress.
- Success: operation completed.
- Error: local API returned or surfaced a failure.

## Tab 1: Rule Configuration

### Purpose

Daniel configures prototype rules that determine whether a demo authorization request auto-approves or pends for review. For rule behavior, see requirements.md §Rule Behavior.

### Layout

The tab uses a two-column workspace:

- Left side panel: **Configuration modes** filter.
- Main content: list of editable rule cards.

The side panel has segmented controls for:

- All modes.
- Confidence threshold.
- Data point combination.
- Pathway threshold.

Selecting a mode filters the visible rule cards only; it does not change rule behavior.

### Rule Card

Each rule card displays:

- Rule mode label.
- Editable rule name.
- Enabled/disabled switch.
- Editable description.
- Short mode explanation.
- Action select.
- Priority number input.
- Precision threshold slider.
- Optional **Apply Synapse confidence filter** checkbox.
- Conditional Synapse confidence threshold slider when the confidence filter is enabled.
- Optional **Apply Synapse versus existing utilization rate** checkbox.
- Conditional radio group and signed slider when the utilization filter is enabled.
- Matching pathway preview summary.
- Conditional **Minimum pathways** input for pathway-threshold rules.
- Member segment chips.
- Service line chips.
- Last-updated attribution.
- **Save rule** button.

Cards for disabled rules should visually mute, but remain editable.

### Synapse Versus Existing Utilization Rate Filter

The utilization filter is unchecked by default. When checked, it shows a single-select radio group under **Compare # Met (AI) with**:

- **Payer selected** - default.
- **Provider selected**.
- **Payer-provider overlap**.

It also shows a signed slider from `-100 pp` through `0 pp` to `+100 pp`, in whole-percentage-point increments, with the thumb initially centered at zero. Its label includes the same small information-tooltip pattern used for Objective Indications metrics; the tooltip explains in business terms that the setting controls how much more or less often Synapse may find an indication before it becomes a candidate, and that saved bucket pathways are unaffected. A live plain-language sentence explains the selected setting. For example, `+5 pp` says Synapse may find the indication at most 5 points more often than the selected rate; `-5 pp` says Synapse must find it at least 5 points less often.

### Matching Pathway Preview

Each rule card shows a compact preview summary below the threshold controls:

- Count of guidelines with at least one matching pathway.
- Count of matching pathways.
- Count of pathways already in the rule's Medical Necessity Bucket.
- **View pathways** button.

Clicking **View pathways** opens a MUCL-style drawer. The drawer groups rows by guideline using `GCode - title`, while HSIM remains the backend identifier. The drawer shows nested indication criteria, precision / user agreement, Synapse confidence, and list logic such as **1 or more of the following**, **ALL of the following**, and **examples include**. When the utilization filter is active, eligible leaf rows also show `# Met (AI)`, the selected reviewer rate, and their signed difference; rows that fail it are labeled **Utilization mismatch**.

The drawer is the staging surface for the rule:

- Left/main area: matching pathway tree with checkboxes.
- Right side panel: the current Medical Necessity Bucket for the rule.
- Matching triggerable pathways are checked by default.
- **Add selected to bucket** appends checked triggerable pathways and skips duplicates.
- Bucket items have a **Remove** action.
- Removing an item updates the local rule draft immediately.
- **Save rule** persists the current draft bucket to the local API.
- If a saved bucket item no longer matches the current filters or metric mode, keep it visible and show **Below current filter**.
- If a saved bucket item is outside the active utilization comparison, keep it visible and show **Outside current utilization filter**.
- Saved Synapse exceptions must be labeled deliberately as **Saved exception: below threshold** or **Previous exception: now meets threshold**.

Selectable rows:

- For `1 or more`, qualifying leaf pathways are selectable.
- For satisfied `ALL`, the satisfied `ALL` group is selectable as one bucket item.
- Incomplete `ALL` groups show required child evidence controls and become selectable only when every child has an explicit evidence source.
- `examples include` rows are contextual and not selectable.

Mixed-evidence `ALL` controls:

- Each required child offers `Synapse`, `Synapse exception`, and `Provider attestation` choices when applicable.
- `Synapse` is available when the Synapse-produced result clears the active staging filters.
- `Synapse exception` is available when a Synapse-produced result exists but precision is below the active precision filter.
- `Provider attestation` is available for any required child.
- The bucket side panel stores the whole `ALL` parent and displays the saved child evidence choices underneath it.

List logic behavior:

- `1 or more` passes when any child path passes.
- `ALL` passes only when every required child path passes.
- `examples include` is contextual and should not count as a required automation gate.
- Incomplete `ALL` groups should show which children passed and which did not.

### Save Behavior

Saving a rule sends the current card state to the local API. On success:

- The prototype snapshot reloads.
- The status line says the rule was saved locally.
- Medical Necessity Bucket additions and removals are persisted for the current backend session.

On failure:

- The card remains on screen.
- The status line displays the local API error.

## Tab 2: Simulator

### Purpose

Daniel selects a demo authorization request, runs the active rules against it, and inspects the resulting decision trace.

### Layout

The tab uses a two-column workspace:

- Left side panel: demo request list.
- Main content: selected request summary, indication table, and evaluation panel.

### Demo Request List

Each request button shows:

- Request ID.
- Member segment.
- Case type.

Selecting a request changes the main content without running a new evaluation.

### Request Summary

The selected request summary shows:

- Eyebrow: **Authorization request**.
- Guideline name.
- **Run evaluation** button.
- Fact grid with request ID, member segment, service line, and case type.

### Indication Table

The evidence table has columns:

- Evidence.
- Attested.
- Precision.
- Confidence.
- Pathway.

Each row shows:

- Evidence name.
- Evidence type, category, and source document.
- Provider attestation badge.
- Precision / user agreement progress track and percent when a Synapse-produced result exists.
- Synapse confidence progress track and percent when a Synapse-produced result exists.
- Provider-attestation-only rows with `N/A` precision/confidence.
- Pathway met / not-met badge.

### Evaluation Panel

After running an evaluation for the selected request, the panel shows:

- Decision banner: auto-approved or pended for review.
- Decision summary text.
- Medically necessary bucket chips from saved bucket pathways met by the request.
- Rule execution trace.

Each rule execution trace card shows:

- Priority.
- Rule name.
- Fired action or did-not-fire state.
- Condition rows with pass/fail dots, condition label, explanation, and actual value.
- Bucket membership and pathway-met status as the decision-driving checks.
- Mixed-evidence `ALL` child evidence source and pass/fail state.
- Precision, optional confidence, and optional utilization-comparison staging filters as trace context, not as decision gates.

If no evaluation has been run for the selected request, show an empty state.

## Tab 3: Objective Indications

### Purpose

Daniel inspects objective AutoAuth indication criteria from guideline XML exports and sees performance metrics next to those criteria. For data rules and metric definitions, see requirements.md §§5-9.

### Loading Behavior

On tab load, the frontend requests guideline summaries from the local API.

Default selection:

- If Pneumonia is available, select it first.
- Otherwise select the first guideline returned by the API.

Loading states:

- While summaries load: show a loading message.
- While a selected guideline detail loads: show a loading message if no detail is already displayed.
- On error: show an empty-state style error message.

### Guideline Selector

The selector appears at the top of the tab and contains:

- Search input with placeholder **Search by title, code, or product**.
- Selected guideline dropdown.
- Count summary showing filtered count of total guidelines.

Search filters by:

- Guideline title.
- Guideline code.
- Product code.
- Guideline type.

If the currently selected guideline does not match the search filter, it remains available in the dropdown so the selected state does not disappear unexpectedly.

### Guideline Hero

The hero section shows:

- Eyebrow: **Guideline indications**.
- Guideline display title and code.
- Helper text explaining that criteria are parsed from guideline XML, projected sample metrics are used by default, real spreadsheet precision can be shown from demo settings, and provider/payer usage is projected.
- Product code badge.
- Version badge.
- Optional GLOS badge.
- Match badge:
  - **Projected sample metrics** in default demo mode.
  - Matched indication count when real spreadsheet mode is enabled and workbook data exists.

The projected/real metric mode badge appears here only. It should not repeat on every row.

### Guideline Metric Cards

The metric grid shows:

- `# Met (AI)`.
- `Synapse confidence`.
- `Precision / user agreement`.
- `Recall`.
- `Provider selected`.
- `Payer selected`.
- `Provider + payer`.

Precision and recall cards include small information buttons with tooltip definitions.

Provider/payer usage cards include small information buttons with tooltip definitions. Their helper text indicates that they are projected selection rates.

Guideline-level metric cards should display the same values as the first top-level parent row when that parent row has metrics. This avoids visible mismatch between the summary cards and the root criteria row during demos.

`Synapse confidence` helper text should indicate that it is currently a projected secondary signal.

### Metric Information Tooltips

Information buttons are compact `i` buttons. They should:

- Open on hover.
- Open on keyboard focus.
- Close on mouse leave, blur, or Escape.
- Use `aria-label` and `aria-describedby`.
- Display the definition in a tooltip rather than a modal or pop-up dialog.

Precision definition appears on precision labels only. Recall definition appears on recall labels only.

### Guideline Indication Tree

The indication tree is displayed in a dense, horizontally scrollable table with columns:

- Indication.
- `# Met (AI)`.
- `Synapse Confidence`.
- `Precision / User Agreement`.
- `Recall`.
- `Provider selected`.
- `Payer selected`.
- `Both selected`.

The tree uses recursive rows:

- Group rows are expandable/collapsible.
- Leaf rows are static.
- Nested rows use indentation and branch-line styling.
- Root rows have a stronger group treatment.
- Rows with sample metrics may have subtle sample styling, but must not show repeated sample badges.

If no AutoAuth indication rows are found in the XML, show:

`No auto-authorization indication rows were found in this XML.`

### Row Metrics

Metric cells show badges or no-data states:

- Numeric values use percent formatting with up to one decimal.
- Missing metrics display `-`.
- Missing agreement metrics also show **No data**.
- Precision / user agreement shows agree and disagree values.
- Provider/payer usage metrics show projected selection-rate badges.
- Provider/payer usage metrics use neutral or informational badge styling, not positive/warning/negative quality styling.

Tone thresholds follow requirements.md §7:

- Positive at 95% or higher.
- Warning at 80% through 94.9%.
- Negative below 80%.
- Neutral when missing.

`# Met (AI)` uses an informational badge when present.

### Mobile and Dense Layout Behavior

The objective indications table is data-dense. On narrow screens:

- The selector stacks cleanly.
- The metric grid wraps.
- The indication table may scroll horizontally, especially with the additional provider/payer usage columns.
- Text must wrap within its row without overlapping metric columns.

Do not shrink metric columns so far that badges overlap or become unreadable.

## Tab 4: Audit Trail

### Purpose

Daniel reviews evaluations run during the current local session.

### Empty State

If there are no evaluations, show:

`No local evaluations have been run.`

### Audit Card

Each audit card shows:

- Evaluation timestamp.
- Request ID and decision.
- Decision summary.
- Member segment badge.
- Service line badge.
- PHI retention statement badge.

The audit trail is session-local. It disappears when the backend process stops.

## Visual Language

The app should keep the MCG/MUCL visual language:

- Quiet operational layout.
- Dense but readable panels.
- Compact badges.
- Segmented controls for mode filters.
- Clear button hierarchy.
- Restrained color use.
- No decorative hero marketing layout.

Cards are appropriate for repeated objects such as rules, trace entries, and audit entries. Page sections should remain direct working areas rather than marketing-style cards.

## Accessibility and Interaction Notes

- Buttons must have accessible labels when icon-like or terse.
- Tree rows should expose row/treegrid semantics.
- Tooltips must be keyboard reachable.
- Progress tracks should use progressbar semantics.
- Empty, loading, success, and error states should be visible without requiring browser developer tools.
- Confirmation is required before shutting down the local server.

## Deferred UI Work

- Wire objective indication selection into simulator rule inputs.
- Replace projected Synapse confidence with real confidence data.
- Add live upload or refresh controls for guideline XML and performance workbook files.
- Add richer metric provenance details if demos require explaining source sheet, row counts, or match confidence.
- Add production-grade persistence and authentication only after the prototype scope changes.
