import { useEffect, useMemo, useState } from 'react';
import { evaluateRequest, getObjectiveGuideline, getObjectiveGuidelines, getPrecisionPreview, getPrototype, shutdownPrototype, updateRule } from './api.js';

const memberSegments = ['Medicare', 'Commercial', 'Medicaid'];
const serviceLines = ['Inpatient admission', 'Elective procedure'];

const modeLabels = {
  ConfidenceThreshold: 'Precision threshold',
  DataPointCombination: 'Data point combination',
  PathwayThreshold: 'Pathway threshold'
};

const actionLabels = {
  AutoApprove: 'Auto-approve',
  PendForReview: 'Pend for review'
};

const modeKeys = Object.keys(modeLabels);
const actionKeys = Object.keys(actionLabels);
const decisionKeys = ['AutoApproved', 'PendedForReview'];
const percentFormatter = new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 });
const evidenceSources = {
  synapse: 'synapse',
  synapseException: 'synapseException',
  providerAttestation: 'providerAttestation'
};

const evidenceSourceLabels = {
  [evidenceSources.synapse]: 'Synapse',
  [evidenceSources.synapseException]: 'Synapse exception',
  [evidenceSources.providerAttestation]: 'Provider attestation'
};

const viewLabels = {
  configure: 'Rule configuration',
  simulator: 'Simulator',
  objective: 'Objective indications',
  audit: 'Audit trail'
};

const metricDefinitions = {
  recall: {
    term: 'Recall',
    definition: 'Of what the human selected, how much Synapse also selected'
  },
  precision: {
    term: 'Precision',
    definition: 'Of what Synapse selected, how much the human also selected'
  },
  providerUsage: {
    term: 'Provider selected',
    definition: 'Projected percentage of reviewed cases where the provider selected this indication'
  },
  payerUsage: {
    term: 'Payer selected',
    definition: 'Projected percentage of reviewed cases where the payer selected this indication'
  },
  combinedUsage: {
    term: 'Provider + payer',
    definition: 'Projected percentage of reviewed cases where both provider and payer selected this indication'
  }
};

function Badge({ children, variant = 'neutral', className = '' }) {
  return (
    <span className={`m-badge m-badge--${variant} ${className}`.trim()}>
      {children}
    </span>
  );
}

function Button({ children, variant = 'outline', className = '', ...props }) {
  return (
    <button className={`m-button m-button--${variant} ${className}`.trim()} type="button" {...props}>
      {children}
    </button>
  );
}

function App() {
  const [snapshot, setSnapshot] = useState(null);
  const [activeView, setActiveView] = useState('configure');
  const [selectedRequestId, setSelectedRequestId] = useState('');
  const [currentEvaluation, setCurrentEvaluation] = useState(null);
  const [modeFilter, setModeFilter] = useState('All');
  const [status, setStatus] = useState({ type: 'idle', message: '' });
  const [appStopped, setAppStopped] = useState(false);
  const [metricMode, setMetricMode] = useState('sample');

  const requests = snapshot?.requests ?? [];
  const rules = snapshot?.rules ?? [];
  const evaluations = snapshot?.evaluations ?? [];
  const dashboard = snapshot?.dashboard;

  const selectedRequest = useMemo(() => {
    return requests.find((request) => request.id === selectedRequestId) ?? requests[0];
  }, [requests, selectedRequestId]);

  useEffect(() => {
    loadPrototype();
  }, []);

  useEffect(() => {
    if (!selectedRequestId && requests.length > 0) {
      setSelectedRequestId(requests[0].id);
    }
  }, [requests, selectedRequestId]);

  const loadPrototype = async () => {
    try {
      const data = await getPrototype();
      setSnapshot(data);
    } catch (error) {
      setStatus({ type: 'error', message: error.message });
    }
  };

  const handleSaveRule = async (rule) => {
    setStatus({ type: 'busy', message: 'Saving rule...' });
    try {
      await updateRule(rule.id, normalizeRuleForSave(rule));
      await loadPrototype();
      setStatus({ type: 'success', message: 'Rule saved locally.' });
    } catch (error) {
      setStatus({ type: 'error', message: error.message });
    }
  };

  const handleRunEvaluation = async () => {
    if (!selectedRequest) {
      return;
    }

    setStatus({ type: 'busy', message: 'Running evaluation...' });
    try {
      const evaluation = await evaluateRequest(selectedRequest.id);
      setCurrentEvaluation(evaluation);
      await loadPrototype();
      setActiveView('simulator');
      setStatus({ type: 'success', message: `${evaluation.request.id} ${formatDecision(evaluation.decision).toLowerCase()}.` });
    } catch (error) {
      setStatus({ type: 'error', message: error.message });
    }
  };

  const handleShutdown = async () => {
    const confirmed = window.confirm(
      'Shut down AutoAuth Rules Prototype?\n\nThis stops the local server. Relaunch from the Dock to start again.'
    );

    if (!confirmed) {
      return;
    }

    setAppStopped(true);

    try {
      await shutdownPrototype();
    } catch {
      // Expected: the server may stop before the browser receives the response.
    }
  };

  if (appStopped) {
    return (
      <main className="app-shell stopped-shell mcg-mucl">
        <section className="stopped-panel">
          <div className="power-mark" aria-hidden="true" />
          <h1>AutoAuth Rules Prototype has been shut down</h1>
          <p>Close this tab or click the Dock icon to rebuild and relaunch.</p>
        </section>
      </main>
    );
  }

  if (!snapshot) {
    return (
      <main className="app-shell loading-shell mcg-mucl">
        <div className="loading-panel">
          <p>Starting local prototype...</p>
          {status.message && <p className="status-line error">{status.message}</p>}
        </div>
      </main>
    );
  }

  return (
    <main className="app-shell mcg-mucl">
      <header className="app-header">
        <div>
          <p className="eyebrow">MCG Path</p>
          <h1>AutoAuth Rules Engine Prototype</h1>
        </div>
        <div className="header-badges" aria-label="Prototype status">
          <Badge variant="info">{dashboard.deploymentModel}</Badge>
          <Badge>{dashboard.dataRetention}</Badge>
          <details className="dev-settings">
            <summary>Demo settings</summary>
            <label>
              <span>Metric mode</span>
              <select value={metricMode} onChange={(event) => setMetricMode(event.target.value)}>
                <option value="sample">Projected sample metrics</option>
                <option value="real">Real spreadsheet metrics</option>
              </select>
            </label>
          </details>
          <Button variant="negative-text" className="shutdown-button" onClick={handleShutdown}>
            Shut down
          </Button>
        </div>
      </header>

      <section className="metric-strip" aria-label="Prototype metrics">
        <Metric label="Active rules" value={dashboard.activeRules} />
        <Metric label="Demo requests" value={dashboard.demoRequests} />
        <Metric label="Evaluations run" value={dashboard.evaluationsRun} />
        <Metric label="Auto-approval rate" value={`${dashboard.latestAutoApprovalRate}%`} helper={`Target ${dashboard.targetAutoApprovalRate}`} />
      </section>

      <nav className="view-tabs" aria-label="Prototype views">
        {Object.entries(viewLabels).map(([view, label]) => (
          <Button
            key={view}
            variant="tab"
            className={activeView === view ? 'is-active' : ''}
            onClick={() => setActiveView(view)}
          >
            {label}
          </Button>
        ))}
      </nav>

      {status.message && (
        <div className={`status-line ${status.type}`} role="status">
          {status.message}
        </div>
      )}

      {activeView === 'configure' && (
        <ConfigurationView
          rules={rules}
          modeFilter={modeFilter}
          setModeFilter={setModeFilter}
          onSaveRule={handleSaveRule}
          metricMode={metricMode}
        />
      )}

      {activeView === 'simulator' && (
        <SimulatorView
          requests={requests}
          selectedRequest={selectedRequest}
          setSelectedRequestId={setSelectedRequestId}
          currentEvaluation={currentEvaluation}
          onRunEvaluation={handleRunEvaluation}
        />
      )}

      {activeView === 'objective' && (
        <ObjectiveIndicationsView metricMode={metricMode} />
      )}

      {activeView === 'audit' && (
        <AuditView evaluations={evaluations} />
      )}
    </main>
  );
}

function Metric({ label, value, helper }) {
  return (
    <article className="metric">
      <span>{label}</span>
      <strong>{value}</strong>
      {helper && <small>{helper}</small>}
    </article>
  );
}

function ConfigurationView({ rules, modeFilter, setModeFilter, onSaveRule, metricMode }) {
  const modes = ['All', ...Object.keys(modeLabels)];
  const filteredRules = modeFilter === 'All' ? rules : rules.filter((rule) => getRuleMode(rule.mode) === modeFilter);

  return (
    <section className="workspace-grid">
      <aside className="side-panel">
        <h2>Configuration modes</h2>
        <div className="segmented vertical">
          {modes.map((mode) => (
            <Button
              key={mode}
              variant="segment"
              className={modeFilter === mode ? 'is-active' : ''}
              onClick={() => setModeFilter(mode)}
            >
              {mode === 'All' ? 'All modes' : modeLabels[mode]}
            </Button>
          ))}
        </div>
      </aside>

      <section className="rule-list" aria-label="Rules">
        {filteredRules.map((rule) => (
          <RuleCard
            key={rule.id}
            rule={rule}
            onSaveRule={onSaveRule}
            metricMode={metricMode}
          />
        ))}
      </section>
    </section>
  );
}

function RuleCard({ rule, onSaveRule, metricMode }) {
  const [draft, setDraft] = useState(rule);
  const [preview, setPreview] = useState(null);
  const [previewStatus, setPreviewStatus] = useState({ type: 'busy', message: 'Loading pathway preview...' });
  const [drawerOpen, setDrawerOpen] = useState(false);

  useEffect(() => {
    setDraft(rule);
  }, [rule]);

  const precisionThreshold = Number(draft.precisionThreshold ?? draft.confidenceThreshold ?? 90);
  const confidenceThreshold = Number(draft.confidenceThreshold ?? 90);
  const useConfidenceThreshold = Boolean(draft.useConfidenceThreshold);

  useEffect(() => {
    let cancelled = false;
    const timeout = window.setTimeout(async () => {
      setPreviewStatus({ type: 'busy', message: 'Loading pathway preview...' });
      try {
        const data = await getPrecisionPreview({
          precisionThreshold,
          useConfidenceThreshold,
          confidenceThreshold,
          metricMode
        });
        if (!cancelled) {
          setPreview(data);
          setPreviewStatus({ type: 'idle', message: '' });
        }
      } catch (error) {
        if (!cancelled) {
          setPreview(null);
          setPreviewStatus({ type: 'error', message: error.message });
        }
      }
    }, 250);

    return () => {
      cancelled = true;
      window.clearTimeout(timeout);
    };
  }, [precisionThreshold, useConfidenceThreshold, confidenceThreshold, metricMode]);

  const updateDraft = (field, value) => {
    setDraft((current) => ({ ...current, [field]: value }));
  };

  const toggleArrayValue = (field, value) => {
    setDraft((current) => {
      const values = new Set(current[field] ?? []);
      if (values.has(value)) {
        values.delete(value);
      } else {
        values.add(value);
      }

      return { ...current, [field]: [...values] };
    });
  };

  const modeDescription = {
    ConfidenceThreshold: 'Uses the saved Medical Necessity Bucket; sliders only stage candidate pathways.',
    DataPointCombination: 'Requires provider attestation and a saved bucket pathway for the same indication.',
    PathwayThreshold: 'Requires more saved bucket pathways than the base guideline threshold.'
  }[getRuleMode(draft.mode)];

  return (
    <>
      <article className={`rule-card ${draft.enabled ? '' : 'muted'}`}>
        <div className="rule-card-header">
          <div>
            <p className="mode-label">{modeLabels[getRuleMode(draft.mode)]}</p>
            <input
              className="rule-title"
              value={draft.name}
              onChange={(event) => updateDraft('name', event.target.value)}
              aria-label="Rule name"
            />
          </div>
          <label className="switch" aria-label="Rule enabled state">
            <input
              type="checkbox"
              checked={draft.enabled}
              onChange={(event) => updateDraft('enabled', event.target.checked)}
            />
            <Badge variant={draft.enabled ? 'positive' : 'neutral'}>{draft.enabled ? 'Enabled' : 'Disabled'}</Badge>
          </label>
        </div>

        <textarea
          className="rule-description"
          value={draft.description}
          onChange={(event) => updateDraft('description', event.target.value)}
          aria-label="Rule description"
        />

        <p className="rule-context">{modeDescription}</p>

        <div className="field-grid">
          <label>
            <span>Action</span>
            <select value={getRuleAction(draft.action)} onChange={(event) => updateDraft('action', event.target.value)}>
              {Object.entries(actionLabels).map(([value, label]) => (
                <option key={value} value={value}>{label}</option>
              ))}
            </select>
          </label>

          <label>
            <span>Priority</span>
            <input
              type="number"
              min="1"
              max="99"
              value={draft.priority}
              onChange={(event) => updateDraft('priority', Number(event.target.value))}
            />
          </label>

          <label className="wide">
            <span>Precision threshold: {formatPercent(precisionThreshold)}</span>
            <input
              type="range"
              min="50"
              max="100"
              value={precisionThreshold}
              onChange={(event) => updateDraft('precisionThreshold', Number(event.target.value))}
            />
          </label>

          {getRuleMode(draft.mode) === 'PathwayThreshold' && (
            <label>
              <span>Minimum pathways</span>
              <input
                type="number"
                min="1"
                max="5"
                value={draft.minimumPathways}
                onChange={(event) => updateDraft('minimumPathways', Number(event.target.value))}
              />
            </label>
          )}
        </div>

        <div className="confidence-filter">
          <label className="check-control">
            <input
              type="checkbox"
              checked={useConfidenceThreshold}
              onChange={(event) => updateDraft('useConfidenceThreshold', event.target.checked)}
            />
            <span>Apply Synapse confidence filter</span>
          </label>
          {useConfidenceThreshold && (
            <label className="confidence-slider">
              <span>Synapse confidence threshold: {formatPercent(confidenceThreshold)}</span>
              <input
                type="range"
                min="50"
                max="100"
                value={confidenceThreshold}
                onChange={(event) => updateDraft('confidenceThreshold', Number(event.target.value))}
              />
            </label>
          )}
        </div>

        <PathwayPreviewSummary
          preview={preview}
          status={previewStatus}
          bucketCount={draft.medicalNecessityBucket?.length ?? 0}
          onOpen={() => setDrawerOpen(true)}
        />

        <OptionGroup
          title="Member segments"
          options={memberSegments}
          selected={draft.memberSegments}
          onToggle={(value) => toggleArrayValue('memberSegments', value)}
        />

        <OptionGroup
          title="Service lines"
          options={serviceLines}
          selected={draft.serviceLines}
          onToggle={(value) => toggleArrayValue('serviceLines', value)}
        />

        <div className="card-actions">
          <span>Last updated by {draft.updatedBy}</span>
          <Button variant="filled" onClick={() => onSaveRule(draft)}>Save rule</Button>
        </div>
      </article>
      <PathwayDrawer
        open={drawerOpen}
        preview={preview}
        bucket={draft.medicalNecessityBucket ?? []}
        onAddToBucket={(items) => {
          setDraft((current) => ({
            ...current,
            medicalNecessityBucket: mergeBucketItems(current.medicalNecessityBucket ?? [], items)
          }));
        }}
        onRemoveFromBucket={(item) => {
          setDraft((current) => ({
            ...current,
            medicalNecessityBucket: (current.medicalNecessityBucket ?? []).filter((bucketItem) => bucketKey(bucketItem) !== bucketKey(item))
          }));
        }}
        onClose={() => setDrawerOpen(false)}
      />
    </>
  );
}

function PathwayPreviewSummary({ preview, status, bucketCount, onOpen }) {
  const hasPreview = preview && status.type !== 'error';

  return (
    <section className="pathway-preview-summary" aria-label="Matching pathway preview">
      <div>
        <p className="eyebrow">Matching pathway preview</p>
        {status.type === 'busy' && <p>Calculating guideline impact...</p>}
        {status.type === 'error' && <p>{status.message}</p>}
        {hasPreview && (
          <div className="preview-counts">
            <strong>{preview.guidelineCount}</strong>
            <span>guidelines</span>
            <strong>{preview.pathwayCount}</strong>
            <span>pathways</span>
            <strong>{bucketCount}</strong>
            <span>in bucket</span>
          </div>
        )}
      </div>
      <Button
        variant="outline"
        onClick={onOpen}
        disabled={!hasPreview || preview.guidelines.length === 0}
      >
        View pathways
      </Button>
    </section>
  );
}

function PathwayDrawer({ open, preview, bucket = [], onAddToBucket, onRemoveFromBucket, onClose }) {
  const selectableItems = useMemo(() => flattenPreviewBucketItems(preview), [preview]);
  const [evidenceSelections, setEvidenceSelections] = useState({});
  const completedMixedItems = useMemo(() => buildCompletedMixedAllBucketItems(preview, evidenceSelections), [preview, evidenceSelections]);
  const addableItems = useMemo(() => [...selectableItems, ...completedMixedItems], [selectableItems, completedMixedItems]);
  const selectableKeySet = useMemo(() => new Set(addableItems.map(bucketKey)), [addableItems]);
  const bucketGroups = useMemo(() => groupBucketByGuideline(bucket), [bucket]);
  const bucketKeySet = useMemo(() => new Set(bucket.map(bucketKey)), [bucket]);
  const previewNodeMap = useMemo(() => mapPreviewNodes(preview), [preview]);
  const [selectedKeys, setSelectedKeys] = useState(new Set());

  useEffect(() => {
    if (open) {
      setEvidenceSelections(buildInitialEvidenceSelections(preview));
      setSelectedKeys(new Set(selectableItems.map(bucketKey)));
    }
  }, [open, preview, selectableItems]);

  useEffect(() => {
    if (!open) {
      return;
    }

    setSelectedKeys((current) => {
      const next = new Set(current);
      completedMixedItems.forEach((item) => next.add(bucketKey(item)));
      return next;
    });
  }, [open, completedMixedItems]);

  if (!open) {
    return null;
  }

  const selectedItems = addableItems.filter((item) => selectedKeys.has(bucketKey(item)));
  const addableCount = selectedItems.filter((item) => !bucketKeySet.has(bucketKey(item))).length;
  const selectedCount = selectedItems.length;

  const toggleSelected = (item) => {
    const key = bucketKey(item);
    setSelectedKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });
  };
  const updateEvidenceSelection = (groupKey, childId, evidenceSource) => {
    setEvidenceSelections((current) => ({
      ...current,
      [groupKey]: {
        ...(current[groupKey] ?? {}),
        [childId]: evidenceSource
      }
    }));
  };

  return (
    <div className="drawer-layer" role="presentation">
      <button className="drawer-scrim" type="button" aria-label="Close pathway drawer" onClick={onClose} />
      <aside className="pathway-drawer" role="dialog" aria-modal="true" aria-label="Matching guideline pathways">
        <div className="drawer-header">
          <div>
            <p className="eyebrow">Matching pathways</p>
            <h2>{preview?.guidelineCount ?? 0} guidelines, {preview?.pathwayCount ?? 0} pathways</h2>
          </div>
          <Button variant="outline" onClick={onClose}>Close</Button>
        </div>
        <div className="drawer-workspace">
          <main className="drawer-main">
            <div className="drawer-action-bar">
              <div>
                <strong>{selectedCount} selected</strong>
                <span>{addableCount} ready to add</span>
              </div>
              <Button
                variant="filled"
                onClick={() => onAddToBucket(selectedItems)}
                disabled={selectedCount === 0}
              >
                Add selected to bucket
              </Button>
            </div>

            <div className="drawer-body">
              {preview?.guidelines?.length ? (
                preview.guidelines.map((guideline) => (
                  <section className="drawer-guideline" key={guideline.hsim}>
                    <div className="drawer-guideline__header">
                      <h3>{guideline.code} - {guideline.title}</h3>
                      <Badge variant="info-subtle">{guideline.pathwayCount} pathway{guideline.pathwayCount === 1 ? '' : 's'}</Badge>
                    </div>
                    <div className="drawer-tree">
                      <PreviewNodeList
                        nodes={guideline.nodes}
                        depth={0}
                        guideline={guideline}
                        preview={preview}
                        selectedKeys={selectedKeys}
                        evidenceSelections={evidenceSelections}
                        onToggleSelection={toggleSelected}
                        onEvidenceSelection={updateEvidenceSelection}
                      />
                    </div>
                  </section>
                ))
              ) : (
                <div className="empty-state">No pathways match the current filters.</div>
              )}
            </div>
          </main>

          <aside className="bucket-side-panel" aria-label="Medical necessity bucket">
            <div className="bucket-side-panel__header">
              <div>
                <p className="eyebrow">Medical necessity bucket</p>
                <h3>{bucket.length} selected pathway{bucket.length === 1 ? '' : 's'}</h3>
              </div>
            </div>

            {bucketGroups.length === 0 ? (
              <div className="bucket-empty">Nothing has been added to this rule yet.</div>
            ) : (
              <div className="bucket-group-list">
                {bucketGroups.map((group) => (
                  <section className="bucket-group" key={group.hsim}>
                    <h4>{group.code} - {group.title}</h4>
                    <div className="bucket-item-list">
                      {group.items.map((item) => {
                        const filterStatus = bucketFilterStatus(item, preview, selectableKeySet);

                        return (
                          <article className="bucket-item" key={bucketKey(item)}>
                            <div>
                              <strong>{item.pathwayText}</strong>
                              <span>{item.logicText || item.logicType || 'Pathway'}</span>
                            </div>
                            <div className="bucket-item__meta">
                              <Badge variant={badgeVariant(agreementTone(item.precision))}>{formatPercent(item.precision)}</Badge>
                              <Badge variant={badgeVariant(metricTone(item.confidence))}>{formatPercent(item.confidence)}</Badge>
                              {filterStatus && <Badge variant={filterStatus.variant}>{filterStatus.label}</Badge>}
                            </div>
                            {item.childEvidence?.length > 0 && (
                              <div className="bucket-item__children">
                                {item.childEvidence.map((child) => (
                                  <div className="bucket-child-evidence" key={`${bucketKey(item)}-${child.pathwayId}`}>
                                    <strong>{child.pathwayText}</strong>
                                    <div className="bucket-item__meta">
                                      <Badge variant="info-subtle">{evidenceSourceLabels[child.evidenceSource] ?? child.evidenceSource}</Badge>
                                      <Badge variant={badgeVariant(agreementTone(currentChildPrecision(child, item, previewNodeMap)))}>{formatPercent(currentChildPrecision(child, item, previewNodeMap))}</Badge>
                                      {savedExceptionStatus(child, item, previewNodeMap) && (
                                        <Badge variant={savedExceptionStatus(child, item, previewNodeMap).variant}>
                                          {savedExceptionStatus(child, item, previewNodeMap).label}
                                        </Badge>
                                      )}
                                    </div>
                                  </div>
                                ))}
                              </div>
                            )}
                            <Button variant="negative-text" onClick={() => onRemoveFromBucket(item)}>Remove</Button>
                          </article>
                        );
                      })}
                    </div>
                  </section>
                ))}
              </div>
            )}
          </aside>
        </div>
      </aside>
    </div>
  );
}

function PreviewNodeList({
  nodes,
  depth,
  guideline,
  preview,
  selectedKeys,
  evidenceSelections,
  onToggleSelection,
  onEvidenceSelection,
  ancestorSatisfiedAll = false
}) {
  return nodes.map((node, index) => (
    <PreviewNode
      key={`${node.id}-${depth}-${index}`}
      node={node}
      depth={depth}
      guideline={guideline}
      preview={preview}
      selectedKeys={selectedKeys}
      evidenceSelections={evidenceSelections}
      onToggleSelection={onToggleSelection}
      onEvidenceSelection={onEvidenceSelection}
      ancestorSatisfiedAll={ancestorSatisfiedAll}
    />
  ));
}

function PreviewNode({
  node,
  depth,
  guideline,
  preview,
  selectedKeys,
  evidenceSelections,
  onToggleSelection,
  onEvidenceSelection,
  ancestorSatisfiedAll
}) {
  const hasChildren = node.items?.length > 0;
  const normalSelectable = isPreviewNodeSelectable(node, ancestorSatisfiedAll);
  const mixedAllCandidate = isMixedAllCandidate(node, ancestorSatisfiedAll);
  const mixedChildren = mixedAllCandidate ? requiredEvidenceChildren(node) : [];
  const mixedGroupKey = bucketKey(bucketItemFromPreviewNode(guideline, node));
  const mixedAllComplete = mixedAllCandidate && isMixedAllComplete(mixedChildren, evidenceSelections[mixedGroupKey]);
  const selectable = normalSelectable || mixedAllComplete;
  const selectableItem = normalSelectable
    ? bucketItemFromPreviewNode(guideline, node)
    : mixedAllComplete
      ? mixedAllBucketItemFromPreviewNode(preview, guideline, node, mixedChildren, evidenceSelections[mixedGroupKey])
      : null;
  const selected = selectableItem ? selectedKeys.has(bucketKey(selectableItem)) : false;
  const satisfiedAll = isSatisfiedAllNode(node);
  const statusVariant = node.isExample
    ? 'neutral'
    : selectable
      ? 'positive-subtle'
      : mixedAllCandidate
        ? 'warning'
      : node.isTriggerable
        ? 'info-subtle'
      : node.isPrecisionQualified
        ? 'warning'
        : 'neutral';
  const statusText = node.isExample
    ? 'Context'
    : selectable
      ? 'Ready to add'
      : mixedAllCandidate
        ? 'Needs evidence'
      : node.isTriggerable
        ? 'Matches filter'
      : node.isPrecisionQualified
        ? 'Partial'
        : 'Not met';

  return (
    <div className="preview-node" style={{ '--tree-indent': `${depth * 18}px` }}>
      <div className="preview-node__row">
        <div className="preview-node__select">
          {selectableItem ? (
            <label>
              <input
                type="checkbox"
                checked={selected}
                onChange={() => onToggleSelection(selectableItem)}
              />
              <span className="sr-only">Select {node.text}</span>
            </label>
          ) : (
            <span aria-hidden="true" />
          )}
        </div>
        <div className="preview-node__text">
          <strong>{node.text}</strong>
          {node.logicText && <span>{node.logicText}</span>}
        </div>
        <div className="preview-node__metrics">
          <Badge variant={badgeVariant(agreementTone(node.precision))}>{formatPercent(node.precision)}</Badge>
          <Badge variant={badgeVariant(metricTone(node.confidence))}>{formatPercent(node.confidence)}</Badge>
          <Badge variant={statusVariant}>{statusText}</Badge>
        </div>
      </div>
      {mixedAllCandidate && (
        <AllEvidenceSelector
          groupKey={mixedGroupKey}
          children={mixedChildren}
          preview={preview}
          selections={evidenceSelections[mixedGroupKey] ?? {}}
          onChange={onEvidenceSelection}
        />
      )}
      {hasChildren && (
        <div className="preview-node__children">
          <PreviewNodeList
            nodes={node.items}
            depth={depth + 1}
            guideline={guideline}
            preview={preview}
            selectedKeys={selectedKeys}
            evidenceSelections={evidenceSelections}
            onToggleSelection={onToggleSelection}
            onEvidenceSelection={onEvidenceSelection}
            ancestorSatisfiedAll={ancestorSatisfiedAll || satisfiedAll}
          />
        </div>
      )}
    </div>
  );
}

function AllEvidenceSelector({ groupKey, children, preview, selections, onChange }) {
  return (
    <div className="preview-node__evidence">
      <strong>Complete this ALL pathway with explicit evidence</strong>
      {children.map((child) => {
        const selectedSource = selections[child.id] ?? '';
        const synapseAvailable = child.isTriggerable;
        const exceptionAvailable = canUseSynapseException(child, preview);

        return (
          <div className="evidence-row" key={`${groupKey}-${child.id}`}>
            <div className="evidence-row__summary">
              <strong>{child.text}</strong>
              <div className="bucket-item__meta">
                <Badge variant={badgeVariant(agreementTone(child.precision))}>{formatPercent(child.precision)}</Badge>
                <Badge variant={badgeVariant(metricTone(child.confidence))}>{formatPercent(child.confidence)}</Badge>
                {exceptionAvailable && <Badge variant="warning">Below current filter</Badge>}
              </div>
            </div>
            <div className="evidence-choice-row">
              <EvidenceChoice
                groupKey={groupKey}
                childId={child.id}
                source={evidenceSources.synapse}
                selectedSource={selectedSource}
                disabled={!synapseAvailable}
                onChange={onChange}
              />
              <EvidenceChoice
                groupKey={groupKey}
                childId={child.id}
                source={evidenceSources.synapseException}
                selectedSource={selectedSource}
                disabled={!exceptionAvailable}
                onChange={onChange}
              />
              <EvidenceChoice
                groupKey={groupKey}
                childId={child.id}
                source={evidenceSources.providerAttestation}
                selectedSource={selectedSource}
                onChange={onChange}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}

function EvidenceChoice({ groupKey, childId, source, selectedSource, disabled = false, onChange }) {
  return (
    <label className={disabled ? 'evidence-choice is-disabled' : 'evidence-choice'}>
      <input
        type="radio"
        name={`${groupKey}-${childId}`}
        checked={selectedSource === source}
        disabled={disabled}
        onChange={() => onChange(groupKey, childId, source)}
      />
      <span>{evidenceSourceLabels[source]}</span>
    </label>
  );
}

function OptionGroup({ title, options, selected, labels = {}, onToggle, allowEmptyLabel }) {
  return (
    <fieldset className="option-group">
      <legend>{title}</legend>
      {allowEmptyLabel && selected.length === 0 && <Badge variant="neutral" className="empty-pill">{allowEmptyLabel}</Badge>}
      <div className="chip-row">
        {options.map((option) => (
          <label key={option} className={selected.includes(option) ? 'chip selected' : 'chip'}>
            <input
              type="checkbox"
              checked={selected.includes(option)}
              onChange={() => onToggle(option)}
            />
            <span>{labels[option] ?? option}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function SimulatorView({ requests, selectedRequest, setSelectedRequestId, currentEvaluation, onRunEvaluation }) {
  return (
    <section className="workspace-grid simulator-grid">
      <aside className="side-panel">
        <h2>Demo requests</h2>
        <div className="request-list">
          {requests.map((request) => (
            <Button
              key={request.id}
              variant="request"
              className={selectedRequest?.id === request.id ? 'is-active request-button' : 'request-button'}
              onClick={() => setSelectedRequestId(request.id)}
            >
              <strong>{request.id}</strong>
              <span>{request.guidelineCode} - {request.memberSegment}</span>
            </Button>
          ))}
        </div>
      </aside>

      {selectedRequest && (
        <section className="simulator-main">
          <article className="request-summary">
            <div>
              <p className="eyebrow">Authorization request</p>
              <h2>{selectedRequest.guidelineCode} - {selectedRequest.guidelineName}</h2>
            </div>
            <Button variant="filled" className="primary-action" onClick={onRunEvaluation}>Run evaluation</Button>
          </article>

          <div className="summary-grid">
            <Fact label="Request ID" value={selectedRequest.id} />
            <Fact label="Guideline" value={selectedRequest.guidelineCode} />
            <Fact label="Member segment" value={selectedRequest.memberSegment} />
            <Fact label="Service line" value={selectedRequest.serviceLine} />
          </div>

          <IndicationTable request={selectedRequest} />

          {currentEvaluation && currentEvaluation.request.id === selectedRequest.id ? (
            <EvaluationPanel evaluation={currentEvaluation} />
          ) : (
            <div className="empty-state">No evaluation selected for this request.</div>
          )}
        </section>
      )}
    </section>
  );
}

function Fact({ label, value }) {
  return (
    <div className="fact">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function ObjectiveIndicationsView({ metricMode }) {
  const [guidelines, setGuidelines] = useState([]);
  const [selectedGuidelineId, setSelectedGuidelineId] = useState('');
  const [guideline, setGuideline] = useState(null);
  const [query, setQuery] = useState('');
  const [listStatus, setListStatus] = useState({ type: 'busy', message: 'Loading guideline XMLs...' });
  const [detailStatus, setDetailStatus] = useState({ type: 'idle', message: '' });

  useEffect(() => {
    let cancelled = false;

    const loadGuidelines = async () => {
      setListStatus({ type: 'busy', message: 'Loading guideline XMLs...' });

      try {
        const data = await getObjectiveGuidelines(metricMode);
        if (cancelled) {
          return;
        }

        setGuidelines(data);
        setListStatus({ type: 'idle', message: '' });
        setSelectedGuidelineId((current) => {
          if (current) {
            return current;
          }

          const defaultGuideline = data.find((candidate) => candidate.title.toLowerCase() === 'pneumonia') ?? data[0];
          return defaultGuideline?.id ?? '';
        });
      } catch (error) {
        if (!cancelled) {
          setListStatus({ type: 'error', message: error.message });
        }
      }
    };

    loadGuidelines();

    return () => {
      cancelled = true;
    };
  }, [metricMode]);

  useEffect(() => {
    if (!selectedGuidelineId) {
      setGuideline(null);
      return;
    }

    let cancelled = false;

    const loadGuideline = async () => {
      setDetailStatus({ type: 'busy', message: 'Loading guideline indications...' });

      try {
        const data = await getObjectiveGuideline(selectedGuidelineId, metricMode);
        if (!cancelled) {
          setGuideline(data);
          setDetailStatus({ type: 'idle', message: '' });
        }
      } catch (error) {
        if (!cancelled) {
          setGuideline(null);
          setDetailStatus({ type: 'error', message: error.message });
        }
      }
    };

    loadGuideline();

    return () => {
      cancelled = true;
    };
  }, [selectedGuidelineId, metricMode]);

  const filteredGuidelines = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    if (!normalizedQuery) {
      return guidelines;
    }

    return guidelines.filter((candidate) => (
      [candidate.title, candidate.rawTitle, candidate.code, candidate.productCode, candidate.guidelineType, candidate.hsim, candidate.fileName]
        .filter(Boolean)
        .some((value) => value.toLowerCase().includes(normalizedQuery))
    ));
  }, [guidelines, query]);
  const selectedSummary = guidelines.find((candidate) => candidate.id === selectedGuidelineId);
  const selectOptions = selectedSummary && !filteredGuidelines.some((candidate) => candidate.id === selectedSummary.id)
    ? [selectedSummary, ...filteredGuidelines]
    : filteredGuidelines;

  return (
    <section className="objective-view" aria-label="Objective indication viewer">
      <GuidelineSelector
        guidelines={selectOptions}
        selectedGuidelineId={selectedGuidelineId}
        setSelectedGuidelineId={setSelectedGuidelineId}
        query={query}
        setQuery={setQuery}
        totalCount={guidelines.length}
        filteredCount={filteredGuidelines.length}
        loading={listStatus.type === 'busy'}
      />

      {listStatus.type === 'error' && (
        <div className="empty-state">{listStatus.message}</div>
      )}

      {detailStatus.type === 'busy' && !guideline && (
        <div className="empty-state">{detailStatus.message}</div>
      )}

      {detailStatus.type === 'error' && (
        <div className="empty-state">{detailStatus.message}</div>
      )}

      {guideline && (
        <GuidelineDetail guideline={guideline} metricMode={metricMode} />
      )}
    </section>
  );
}

function GuidelineSelector({
  guidelines,
  selectedGuidelineId,
  setSelectedGuidelineId,
  query,
  setQuery,
  totalCount,
  filteredCount,
  loading
}) {
  return (
    <section className="guideline-selector" aria-label="Guideline search">
      <label>
        <span>Search guidelines</span>
        <input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Search by title, code, or product"
        />
      </label>
      <label>
        <span>Selected guideline</span>
        <select
          value={selectedGuidelineId}
          onChange={(event) => setSelectedGuidelineId(event.target.value)}
          disabled={loading || guidelines.length === 0}
        >
          {guidelines.length === 0 ? (
            <option value="">No guidelines found</option>
          ) : (
            guidelines.map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {guidelineLabel(candidate)}
              </option>
            ))
          )}
        </select>
      </label>
      <div className="guideline-selector__meta" aria-live="polite">
        <strong>{filteredCount}</strong>
        <span>of {totalCount} guidelines</span>
      </div>
    </section>
  );
}

function GuidelineDetail({ guideline, metricMode }) {
  const { summary, metrics } = guideline;
  const isSampleMode = metricMode !== 'real';
  const matchText = isSampleMode
    ? 'Projected sample metrics'
    : `${summary.matchedIndicationCount} of ${summary.indicationCount} indication IDs matched`;

  return (
    <>
      <article className="objective-hero">
        <div>
          <p className="eyebrow">Guideline indications</p>
          <h2>{guidelineLabel(summary)}</h2>
          <p>Showing auto-authorization indication criteria parsed from the guideline XML. Metrics use projected sample values by default, with real spreadsheet precision available in demo settings and projected provider/payer usage for each indication.</p>
        </div>
        <div className="objective-hero__badges">
          <Badge variant="info-subtle">{summary.productCode}</Badge>
          <Badge variant="highlight-subtle">v{summary.version}</Badge>
          {summary.glos && <Badge>{summary.glos}</Badge>}
          <Badge variant={isSampleMode ? 'warning' : 'positive-subtle'}>
            {matchText}
          </Badge>
        </div>
      </article>

      <section className="objective-metric-grid" aria-label="Guideline performance metrics">
        <ObjectiveMetric
          label="# Met (AI)"
          value={formatPercent(metrics?.metAi)}
          helper={metrics?.totalCases ? `${metrics.totalCases} cases` : undefined}
          tone={metrics?.metAi == null ? 'neutral' : 'info'}
        />
        <ObjectiveMetric
          label="Synapse confidence"
          value={formatPercent(metrics?.confidence)}
          helper="Projected signal"
          tone={metricTone(metrics?.confidence)}
        />
        <ObjectiveMetric
          label="Precision / user agreement"
          value={formatPercent(metrics?.agreementAgree)}
          helper={metrics?.agreementDisagree == null ? undefined : `${formatPercent(metrics.agreementDisagree)} disagree`}
          tone={agreementTone(metrics?.agreementAgree)}
          definition={metricDefinitions.precision}
          definitionsId="objective-precision-metric-definition"
        />
        <ObjectiveMetric
          label="Recall"
          value={formatPercent(metrics?.recall)}
          tone={metricTone(metrics?.recall)}
          definition={metricDefinitions.recall}
          definitionsId="objective-recall-metric-definition"
        />
        <ObjectiveMetric
          label="Provider selected"
          value={formatPercent(metrics?.providerSelectionRate)}
          helper={metrics?.usageIsProjected ? 'Projected selection rate' : undefined}
          tone={metrics?.providerSelectionRate == null ? 'neutral' : 'info'}
          definition={metricDefinitions.providerUsage}
          definitionsId="objective-provider-usage-metric-definition"
        />
        <ObjectiveMetric
          label="Payer selected"
          value={formatPercent(metrics?.payerSelectionRate)}
          helper={metrics?.usageIsProjected ? 'Projected selection rate' : undefined}
          tone={metrics?.payerSelectionRate == null ? 'neutral' : 'info'}
          definition={metricDefinitions.payerUsage}
          definitionsId="objective-payer-usage-metric-definition"
        />
        <ObjectiveMetric
          label="Provider + payer"
          value={formatPercent(metrics?.providerAndPayerSelectionRate)}
          helper={metrics?.usageIsProjected ? 'Projected overlap rate' : undefined}
          tone={metrics?.providerAndPayerSelectionRate == null ? 'neutral' : 'info'}
          definition={metricDefinitions.combinedUsage}
          definitionsId="objective-combined-usage-metric-definition"
        />
      </section>

      <section className="guideline-panel">
        <div className="guideline-panel__header">
          <div>
            <p className="eyebrow">Guideline indication tree</p>
            <h2>Specific indication elements</h2>
          </div>
          <div className="objective-hero__badges">
            <Badge variant="info-subtle">{summary.autoAuthorizationSectionCount} AutoAuth section{summary.autoAuthorizationSectionCount === 1 ? '' : 's'}</Badge>
          </div>
        </div>

        <div className="guideline-table-wrap">
          <div className="guideline-table" role="treegrid" aria-label={`${summary.title} ${summary.code} indication metrics`}>
            <div className="guideline-table__head" role="row">
              <span>Indication</span>
              <span># Met (AI)</span>
              <span>Synapse Confidence</span>
              <span className="guideline-table__head-cell guideline-table__head-cell--with-info">
                Precision / User Agreement
                <MetricDefinitionTooltip
                  id="objective-precision-header-definition"
                  definition={metricDefinitions.precision}
                  align="left"
                />
              </span>
              <span className="guideline-table__head-cell guideline-table__head-cell--with-info">
                Recall
                <MetricDefinitionTooltip
                  id="objective-recall-header-definition"
                  definition={metricDefinitions.recall}
                  align="left"
                />
              </span>
              <span className="guideline-table__head-cell guideline-table__head-cell--with-info">
                Provider selected
                <MetricDefinitionTooltip
                  id="objective-provider-usage-header-definition"
                  definition={metricDefinitions.providerUsage}
                  align="left"
                />
              </span>
              <span className="guideline-table__head-cell guideline-table__head-cell--with-info">
                Payer selected
                <MetricDefinitionTooltip
                  id="objective-payer-usage-header-definition"
                  definition={metricDefinitions.payerUsage}
                  align="left"
                />
              </span>
              <span className="guideline-table__head-cell guideline-table__head-cell--with-info">
                Both selected
                <MetricDefinitionTooltip
                  id="objective-combined-usage-header-definition"
                  definition={metricDefinitions.combinedUsage}
                  align="left"
                />
              </span>
            </div>
            {guideline.nodes.length === 0 ? (
              <div className="guideline-table__empty">No auto-authorization indication rows were found in this XML.</div>
            ) : (
              <GuidelineNodeList nodes={guideline.nodes} depth={0} />
            )}
          </div>
        </div>
      </section>
    </>
  );
}

function ObjectiveMetric({ label, value, helper, tone = 'neutral', definition, definitionsId }) {
  return (
    <article className={`objective-metric objective-metric--${tone}`}>
      <div className="objective-metric__label-row">
        <span className="objective-metric__label">{label}</span>
        {definition && definitionsId && (
          <MetricDefinitionTooltip id={definitionsId} definition={definition} />
        )}
      </div>
      <strong>{value}</strong>
      {helper && <small>{helper}</small>}
    </article>
  );
}

function MetricDefinitionTooltip({ id, definition, align = 'right' }) {
  const [open, setOpen] = useState(false);
  const ariaLabel = `${definition.term}: ${definition.definition}`;

  return (
    <span
      className={`metric-info metric-info--${align} ${open ? 'metric-info--open' : ''}`}
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
      onFocus={() => setOpen(true)}
      onBlur={() => setOpen(false)}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          setOpen(false);
        }
      }}
    >
      <button
        className="metric-info__button"
        type="button"
        aria-label={ariaLabel}
        aria-describedby={id}
      >
        i
      </button>
      <span className="metric-info__tooltip" id={id} role="tooltip">
        <strong>{definition.term}</strong>
        <span>{definition.definition}</span>
      </span>
    </span>
  );
}

function GuidelineNodeList({ nodes, depth }) {
  return nodes.map((node, index) => (
    <GuidelineNode key={`${node.id}-${depth}-${index}`} node={node} depth={depth} />
  ));
}

function GuidelineNode({ node, depth }) {
  const hasChildren = node.items?.length > 0;
  const [expanded, setExpanded] = useState(true);
  const rowClass = [
    'guideline-node__row',
    hasChildren ? 'guideline-node__row--group' : 'guideline-node__row--leaf',
    depth === 0 ? 'guideline-node__row--root' : '',
    node.metrics?.isSample ? 'guideline-node__row--sample' : ''
  ].filter(Boolean).join(' ');
  const commonProps = {
    className: rowClass,
    style: { '--tree-depth': depth, '--tree-indent': `${depth * 20}px` },
    role: 'row',
    'aria-level': depth + 1
  };

  if (hasChildren) {
    return (
      <div className="guideline-node" role="rowgroup">
        <button
          {...commonProps}
          type="button"
          aria-expanded={expanded}
          onClick={() => setExpanded((current) => !current)}
        >
          <GuidelineNodeLabel node={node} depth={depth} expanded={expanded} hasChildren />
          <GuidelineMetricCells metrics={node.metrics} />
        </button>
        {expanded && (
          <div className="guideline-node__children" role="rowgroup">
            <GuidelineNodeList nodes={node.items} depth={depth + 1} />
          </div>
        )}
      </div>
    );
  }

  return (
    <div {...commonProps}>
      <GuidelineNodeLabel node={node} depth={depth} />
      <GuidelineMetricCells metrics={node.metrics} />
    </div>
  );
}

function GuidelineNodeLabel({ node, expanded, hasChildren = false }) {
  return (
    <div className="guideline-node__label" role="gridcell">
      <span className={hasChildren ? 'guideline-node__toggle' : 'guideline-node__status'} aria-hidden="true">
        {hasChildren ? (expanded ? '-' : '+') : ''}
      </span>
      <div className="guideline-node__text">
        <strong>{node.text}</strong>
      </div>
    </div>
  );
}

function GuidelineMetricCells({ metrics }) {
  return (
    <>
      <MetricBadge value={metrics?.metAi} suffix="%" variant="info-subtle" label="# Met AI" />
      <MetricBadge value={metrics?.confidence} suffix="%" variant={badgeVariant(metricTone(metrics?.confidence))} label="Synapse confidence" />
      <AgreementCell metrics={metrics} />
      <MetricBadge value={metrics?.recall} suffix="%" variant={badgeVariant(metricTone(metrics?.recall))} label="Recall" />
      <MetricBadge value={metrics?.providerSelectionRate} suffix="%" variant="info-subtle" label="Provider selected" />
      <MetricBadge value={metrics?.payerSelectionRate} suffix="%" variant="info-subtle" label="Payer selected" />
      <MetricBadge value={metrics?.providerAndPayerSelectionRate} suffix="%" variant="info-subtle" label="Provider and payer selected" />
    </>
  );
}

function MetricBadge({ value, suffix = '', variant = 'neutral', label }) {
  const hasValue = value != null;
  const displayValue = hasValue ? `${formatNumber(value)}${suffix}` : '-';

  return (
    <div className="guideline-metric-cell" role="gridcell" aria-label={hasValue ? `${label}: ${displayValue}` : `${label}: no data`}>
      <Badge variant={hasValue ? variant : 'neutral'}>{displayValue}</Badge>
    </div>
  );
}

function AgreementCell({ metrics }) {
  if (metrics?.agreementAgree == null) {
    return (
      <div className="agreement-cell" role="gridcell" aria-label="Precision user agreement: no data">
        <Badge>-</Badge>
        <span>No data</span>
      </div>
    );
  }

  return (
    <div className="agreement-cell" role="gridcell" aria-label={`Precision user agreement: ${formatPercent(metrics.agreementAgree)} agree, ${formatPercent(metrics.agreementDisagree)} disagree`}>
      <Badge variant={badgeVariant(agreementTone(metrics.agreementAgree))}>{formatPercent(metrics.agreementAgree)} agree</Badge>
      <span>{formatPercent(metrics.agreementDisagree)} disagree</span>
    </div>
  );
}

function IndicationTable({ request }) {
  const synapseRows = request.synapseResults.map((result) => ({
    id: result.indicationId,
    name: result.indicationName,
    category: result.category,
    sourceDocument: result.sourceDocument,
    attested: Boolean(request.providerAttestations[result.indicationId]),
    precision: result.precision,
    confidence: result.confidence,
    pathwayMet: result.pathwayMet,
    evidenceType: 'Synapse-produced result'
  }));
  const synapseIds = new Set(synapseRows.map((row) => row.id));
  const providerOnlyRows = (request.providerAttestationEvidence ?? [])
    .filter((evidence) => !synapseIds.has(evidence.indicationId))
    .map((evidence) => ({
      id: evidence.indicationId,
      name: evidence.indicationName,
      category: evidence.category,
      sourceDocument: evidence.sourceDocument,
      attested: Boolean(evidence.attested),
      precision: null,
      confidence: null,
      pathwayMet: null,
      evidenceType: 'Provider attestation only'
    }));
  const evidenceRows = [...synapseRows, ...providerOnlyRows];

  return (
    <section className="table-wrap" aria-label="Indications">
      <div className="table-row table-head">
        <span>Evidence</span>
        <span>Attested</span>
        <span>Precision</span>
        <span>Confidence</span>
        <span>Pathway</span>
      </div>
      {evidenceRows.map((row) => (
        <div className={row.evidenceType === 'Provider attestation only' ? 'table-row provider-only-row' : 'table-row'} key={row.id}>
          <div>
            <strong>{row.name}</strong>
            <small>{row.evidenceType} - {row.category} - {row.sourceDocument}</small>
          </div>
          <Badge variant={row.attested ? 'positive-subtle' : 'neutral'}>
            {row.attested ? 'Yes' : 'No'}
          </Badge>
          {row.precision == null ? (
            <Badge variant="neutral">N/A</Badge>
          ) : (
            <div className="confidence-cell">
              <div
                className="confidence-track"
                role="progressbar"
                aria-label={`${row.name} precision`}
                aria-valuenow={Number(row.precision)}
                aria-valuemin="0"
                aria-valuemax="100"
              >
                <span style={{ width: `${row.precision}%` }} />
              </div>
              <strong>{formatPercent(row.precision)}</strong>
            </div>
          )}
          {row.confidence == null ? (
            <Badge variant="neutral">N/A</Badge>
          ) : (
            <div className="confidence-cell">
              <div
                className="confidence-track"
                role="progressbar"
                aria-label={`${row.name} Synapse confidence`}
                aria-valuenow={Number(row.confidence)}
                aria-valuemin="0"
                aria-valuemax="100"
              >
                <span style={{ width: `${row.confidence}%` }} />
              </div>
              <strong>{formatPercent(row.confidence)}</strong>
            </div>
          )}
          {row.pathwayMet == null ? (
            <Badge variant="info-subtle" className="pill">Attestation</Badge>
          ) : (
            <Badge variant={row.pathwayMet ? 'positive' : 'neutral'} className="pill">
              {row.pathwayMet ? 'Met' : 'Not met'}
            </Badge>
          )}
        </div>
      ))}
    </section>
  );
}

function EvaluationPanel({ evaluation }) {
  const autoApproved = isAutoApproved(evaluation.decision);

  return (
    <section className="evaluation-panel">
      <div className={`decision-banner ${autoApproved ? 'approved' : 'pended'}`}>
        <Badge variant={autoApproved ? 'positive' : 'warning'}>
          {formatDecision(evaluation.decision)}
        </Badge>
        <strong>{evaluation.decisionSummary}</strong>
      </div>

      <div className="bucket-panel">
        <h3>Medically necessary bucket</h3>
        {evaluation.medicallyNecessaryBucket.length === 0 ? (
          <p>No indications are currently in the bucket.</p>
        ) : (
          <div className="chip-row">
            {evaluation.medicallyNecessaryBucket.map((item) => (
              <Badge variant="info-subtle" className="static-chip" key={item}>{item}</Badge>
            ))}
          </div>
        )}
      </div>

      <section className="trace-list" aria-label="Rule execution trace">
        {evaluation.ruleExecutions.map((execution) => (
          <article className={execution.fired ? 'trace-card fired' : 'trace-card'} key={execution.ruleId}>
            <div className="trace-header">
              <div>
                <span>Priority {execution.priority}</span>
                <h3>{execution.ruleName}</h3>
              </div>
              <Badge variant={execution.fired ? 'positive-subtle' : 'neutral'}>
                {execution.fired ? formatAction(execution.actionTaken) : 'Did not fire'}
              </Badge>
            </div>
            <div className="condition-list">
              {execution.conditions.map((condition) => (
                <div className="condition-row" key={`${execution.ruleId}-${condition.label}`}>
                  <span className={condition.passed ? 'dot passed' : 'dot failed'} aria-label={condition.passed ? 'Passed' : 'Failed'} />
                  <div>
                    <strong>{condition.label}</strong>
                    <p>{condition.detail}</p>
                  </div>
                  <small>{condition.actual}</small>
                </div>
              ))}
            </div>
          </article>
        ))}
      </section>
    </section>
  );
}

function AuditView({ evaluations }) {
  if (evaluations.length === 0) {
    return <div className="empty-state">No local evaluations have been run.</div>;
  }

  return (
    <section className="audit-list" aria-label="Evaluation audit trail">
      {evaluations.map((evaluation) => (
        <article className="audit-card" key={evaluation.id}>
          <div>
            <p className="eyebrow">{new Date(evaluation.evaluatedAt).toLocaleString()}</p>
            <h2>{evaluation.request.id} - {formatDecision(evaluation.decision)}</h2>
            <p>{evaluation.decisionSummary}</p>
          </div>
          <div className="audit-meta">
            <Badge>{evaluation.request.memberSegment}</Badge>
            <Badge>{evaluation.request.serviceLine}</Badge>
            <Badge variant="info-subtle">{evaluation.phiRetentionStatement}</Badge>
          </div>
        </article>
      ))}
    </section>
  );
}

function normalizeRuleForSave(rule) {
  return {
    name: rule.name,
    description: rule.description,
    mode: getRuleModeIndex(rule.mode),
    action: getRuleActionIndex(rule.action),
    priority: Number(rule.priority),
    enabled: rule.enabled,
    memberSegments: rule.memberSegments,
    serviceLines: rule.serviceLines,
    guidelineIds: rule.guidelineIds,
    eligibleIndicationIds: rule.eligibleIndicationIds,
    medicalNecessityBucket: rule.medicalNecessityBucket ?? [],
    precisionThreshold: Number(rule.precisionThreshold ?? rule.confidenceThreshold),
    useConfidenceThreshold: Boolean(rule.useConfidenceThreshold),
    confidenceThreshold: Number(rule.confidenceThreshold),
    requireProviderAttestation: rule.requireProviderAttestation,
    minimumPathways: Number(rule.minimumPathways),
    updatedBy: 'Local prototype user'
  };
}

function flattenPreviewBucketItems(preview) {
  const items = [];

  (preview?.guidelines ?? []).forEach((guideline) => {
    const visit = (node, ancestorSatisfiedAll = false) => {
      if (isPreviewNodeSelectable(node, ancestorSatisfiedAll)) {
        items.push(bucketItemFromPreviewNode(guideline, node));
      }

      const nextAncestorSatisfiedAll = ancestorSatisfiedAll || isSatisfiedAllNode(node);
      (node.items ?? []).forEach((child) => visit(child, nextAncestorSatisfiedAll));
    };

    (guideline.nodes ?? []).forEach((node) => visit(node));
  });

  return items;
}

function buildCompletedMixedAllBucketItems(preview, evidenceSelections) {
  return flattenMixedAllSpecs(preview)
    .map((spec) => mixedAllBucketItemFromPreviewNode(
      preview,
      spec.guideline,
      spec.node,
      spec.children,
      evidenceSelections[spec.key] ?? {}
    ))
    .filter(Boolean);
}

function flattenMixedAllSpecs(preview) {
  const specs = [];

  (preview?.guidelines ?? []).forEach((guideline) => {
    const visit = (node, ancestorSatisfiedAll = false) => {
      if (isMixedAllCandidate(node, ancestorSatisfiedAll)) {
        specs.push({
          key: bucketKey(bucketItemFromPreviewNode(guideline, node)),
          guideline,
          node,
          children: requiredEvidenceChildren(node)
        });
      }

      const nextAncestorSatisfiedAll = ancestorSatisfiedAll || isSatisfiedAllNode(node);
      (node.items ?? []).forEach((child) => visit(child, nextAncestorSatisfiedAll));
    };

    (guideline.nodes ?? []).forEach((node) => visit(node));
  });

  return specs;
}

function buildInitialEvidenceSelections(preview) {
  const selections = {};

  flattenMixedAllSpecs(preview).forEach((spec) => {
    selections[spec.key] = {};
    spec.children.forEach((child) => {
      if (child.isTriggerable) {
        selections[spec.key][child.id] = evidenceSources.synapse;
      }
    });
  });

  return selections;
}

function bucketItemFromPreviewNode(guideline, node) {
  return {
    hsim: guideline.hsim,
    guidelineCode: guideline.code,
    guidelineTitle: guideline.title,
    pathwayId: node.id,
    pathwayText: node.text,
    logicType: node.logicType ?? null,
    logicText: node.logicText ?? null,
    precision: node.precision ?? null,
    confidence: node.confidence ?? null,
    addedAt: new Date().toISOString()
  };
}

function mixedAllBucketItemFromPreviewNode(preview, guideline, node, children, selections) {
  if (!isMixedAllComplete(children, selections)) {
    return null;
  }

  return {
    ...bucketItemFromPreviewNode(guideline, node),
    childEvidence: children.map((child) => ({
      pathwayId: child.id,
      pathwayText: child.text,
      evidenceSource: selections[child.id],
      logicType: child.logicType ?? null,
      logicText: child.logicText ?? null,
      precision: child.precision ?? null,
      confidence: child.confidence ?? null,
      precisionThreshold: Number(preview?.precisionThreshold ?? 0),
      useConfidenceThreshold: Boolean(preview?.useConfidenceThreshold),
      confidenceThreshold: Number(preview?.confidenceThreshold ?? 0),
      addedAt: new Date().toISOString()
    }))
  };
}

function isPreviewNodeSelectable(node, ancestorSatisfiedAll = false) {
  const hasChildren = node.items?.length > 0;
  return Boolean(node.isTriggerable && !ancestorSatisfiedAll && (isAllLogicNode(node) || !hasChildren));
}

function isMixedAllCandidate(node, ancestorSatisfiedAll = false) {
  return Boolean(
    !ancestorSatisfiedAll
    && isAllLogicNode(node)
    && node.items?.length > 0
    && !node.isTriggerable
    && requiredEvidenceChildren(node).length > 0
  );
}

function isSatisfiedAllNode(node) {
  return Boolean(node.isTriggerable && isAllLogicNode(node));
}

function isAllLogicNode(node) {
  return node.logicType === 'A' || /all of/i.test(node.logicText ?? '');
}

function isExampleLogicNode(node) {
  return node.logicType === 'E' || /examples include/i.test(node.logicText ?? '');
}

function requiredEvidenceChildren(node) {
  const children = [];

  const visit = (candidate) => {
    if (isExampleLogicNode(candidate)) {
      return;
    }

    if (!candidate.items?.length) {
      children.push(candidate);
      return;
    }

    candidate.items.forEach(visit);
  };

  (node.items ?? []).forEach(visit);
  return children;
}

function isMixedAllComplete(children, selections = {}) {
  return children.length > 0 && children.every((child) => Boolean(selections[child.id]));
}

function canUseSynapseException(node, preview) {
  return node.precision != null && Number(node.precision) < Number(preview?.precisionThreshold ?? 0);
}

function bucketKey(item) {
  return `${item.hsim ?? ''}::${item.pathwayId ?? ''}`;
}

function mergeBucketItems(existingItems, newItems) {
  const byKey = new Map();

  existingItems.forEach((item) => {
    byKey.set(bucketKey(item), item);
  });

  newItems.forEach((item) => {
    const key = bucketKey(item);
    if (!byKey.has(key)) {
      byKey.set(key, {
        ...item,
        addedAt: item.addedAt ?? new Date().toISOString()
      });
    }
  });

  return [...byKey.values()];
}

function groupBucketByGuideline(bucket) {
  const groups = new Map();

  bucket.forEach((item) => {
    const hsim = item.hsim ?? '';
    if (!groups.has(hsim)) {
      groups.set(hsim, {
        hsim,
        code: item.guidelineCode ?? hsim,
        title: item.guidelineTitle ?? 'Guideline',
        items: []
      });
    }

    groups.get(hsim).items.push(item);
  });

  return [...groups.values()].sort((left, right) => {
    const leftLabel = `${left.code} ${left.title}`;
    const rightLabel = `${right.code} ${right.title}`;
    return leftLabel.localeCompare(rightLabel);
  });
}

function bucketFilterStatus(item, preview, selectableKeySet) {
  if (!preview) {
    return null;
  }

  if (item.childEvidence?.length > 0) {
    return null;
  }

  const precision = item.precision == null ? null : Number(item.precision);
  const confidence = item.confidence == null ? null : Number(item.confidence);
  const precisionThreshold = Number(preview.precisionThreshold ?? 0);
  const confidenceThreshold = Number(preview.confidenceThreshold ?? 0);
  const belowPrecision = precision == null || precision < precisionThreshold;
  const belowConfidence = Boolean(preview.useConfidenceThreshold) && (confidence == null || confidence < confidenceThreshold);

  if (belowPrecision || belowConfidence) {
    return { label: 'Below current filter', variant: 'warning' };
  }

  if (!selectableKeySet.has(bucketKey(item))) {
    return { label: 'Not currently addable', variant: 'neutral' };
  }

  return null;
}

function mapPreviewNodes(preview) {
  const nodes = new Map();

  (preview?.guidelines ?? []).forEach((guideline) => {
    const visit = (node) => {
      nodes.set(`${guideline.hsim}::${node.id}`, node);
      (node.items ?? []).forEach(visit);
    };

    (guideline.nodes ?? []).forEach(visit);
  });

  return nodes;
}

function currentChildPrecision(child, bucketItem, previewNodeMap) {
  return previewNodeMap.get(`${bucketItem.hsim}::${child.pathwayId}`)?.precision ?? child.precision;
}

function savedExceptionStatus(child, bucketItem, previewNodeMap) {
  if (child.evidenceSource !== evidenceSources.synapseException) {
    return null;
  }

  const currentPrecision = currentChildPrecision(child, bucketItem, previewNodeMap);
  if (currentPrecision != null && Number(currentPrecision) >= Number(child.precisionThreshold ?? 0)) {
    return { label: 'Previous exception: now meets threshold', variant: 'positive-subtle' };
  }

  return { label: 'Saved exception: below threshold', variant: 'warning' };
}

function formatDecision(decision) {
  return isAutoApproved(decision) ? 'Auto-approved' : 'Pended for review';
}

function formatAction(action) {
  return actionLabels[getRuleAction(action)] ?? 'Unknown action';
}

function formatNumber(value) {
  return percentFormatter.format(Number(value));
}

function formatPercent(value) {
  return value == null ? '-' : `${formatNumber(value)}%`;
}

function guidelineLabel(guideline) {
  const title = guideline?.title || guideline?.rawTitle || guideline?.fileName || guideline?.hsim || 'Untitled guideline';
  return guideline?.code ? `${title} (${guideline.code})` : title;
}

function metricTone(value) {
  if (value == null) {
    return 'neutral';
  }

  if (value >= 95) {
    return 'positive';
  }

  if (value >= 80) {
    return 'warning';
  }

  return 'negative';
}

function agreementTone(value) {
  if (value == null) {
    return 'neutral';
  }

  if (value >= 95) {
    return 'positive';
  }

  if (value >= 80) {
    return 'warning';
  }

  return 'negative';
}

function badgeVariant(tone) {
  return {
    positive: 'positive-subtle',
    warning: 'warning',
    negative: 'negative-subtle',
    info: 'info-subtle',
    neutral: 'neutral'
  }[tone] ?? 'neutral';
}

function isAutoApproved(decision) {
  return getEnumKey(decision, decisionKeys) === 'AutoApproved';
}

function getRuleMode(mode) {
  return getEnumKey(mode, modeKeys);
}

function getRuleAction(action) {
  return getEnumKey(action, actionKeys);
}

function getRuleModeIndex(mode) {
  return getEnumIndex(mode, modeKeys);
}

function getRuleActionIndex(action) {
  return getEnumIndex(action, actionKeys);
}

function getEnumKey(value, keys) {
  if (typeof value === 'number') {
    return keys[value] ?? '';
  }

  if (typeof value === 'string' && /^\d+$/.test(value)) {
    return keys[Number(value)] ?? '';
  }

  return value;
}

function getEnumIndex(value, keys) {
  if (typeof value === 'number') {
    return value;
  }

  if (typeof value === 'string' && /^\d+$/.test(value)) {
    return Number(value);
  }

  const index = keys.indexOf(value);
  return index >= 0 ? index : 0;
}

export default App;
