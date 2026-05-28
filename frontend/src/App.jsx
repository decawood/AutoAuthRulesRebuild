import { useEffect, useMemo, useState } from 'react';
import { evaluateRequest, getPrototype, shutdownPrototype, updateRule } from './api.js';

const memberSegments = ['Medicare', 'Commercial', 'Medicaid'];
const serviceLines = ['Inpatient admission', 'Elective procedure'];

const modeLabels = {
  ConfidenceThreshold: 'Confidence threshold',
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

const viewLabels = {
  configure: 'Rule configuration',
  simulator: 'Simulator',
  audit: 'Audit trail'
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
          requests={requests}
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

function ConfigurationView({ rules, modeFilter, setModeFilter, onSaveRule, requests }) {
  const modes = ['All', ...Object.keys(modeLabels)];
  const filteredRules = modeFilter === 'All' ? rules : rules.filter((rule) => getRuleMode(rule.mode) === modeFilter);
  const indications = uniqueIndications(requests);

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
            indications={indications}
            onSaveRule={onSaveRule}
          />
        ))}
      </section>
    </section>
  );
}

function RuleCard({ rule, indications, onSaveRule }) {
  const [draft, setDraft] = useState(rule);

  useEffect(() => {
    setDraft(rule);
  }, [rule]);

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
    ConfidenceThreshold: 'Builds a medically necessary bucket from Synapse confidence.',
    DataPointCombination: 'Requires provider attestation and Synapse support for the same indication.',
    PathwayThreshold: 'Requires more met pathways than the base guideline threshold.'
  }[getRuleMode(draft.mode)];

  return (
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
          <span>Synapse confidence threshold: {draft.confidenceThreshold}%</span>
          <input
            type="range"
            min="50"
            max="100"
            value={draft.confidenceThreshold}
            onChange={(event) => updateDraft('confidenceThreshold', Number(event.target.value))}
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

      <OptionGroup
        title="Eligible indications"
        options={indications.map((indication) => indication.id)}
        selected={draft.eligibleIndicationIds}
        labels={Object.fromEntries(indications.map((indication) => [indication.id, indication.name]))}
        onToggle={(value) => toggleArrayValue('eligibleIndicationIds', value)}
        allowEmptyLabel="All indications"
      />

      <div className="card-actions">
        <span>Last updated by {draft.updatedBy}</span>
        <Button variant="filled" onClick={() => onSaveRule(draft)}>Save rule</Button>
      </div>
    </article>
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
              <span>{request.memberSegment} - {request.caseType}</span>
            </Button>
          ))}
        </div>
      </aside>

      {selectedRequest && (
        <section className="simulator-main">
          <article className="request-summary">
            <div>
              <p className="eyebrow">Authorization request</p>
              <h2>{selectedRequest.guidelineName}</h2>
            </div>
            <Button variant="filled" className="primary-action" onClick={onRunEvaluation}>Run evaluation</Button>
          </article>

          <div className="summary-grid">
            <Fact label="Request ID" value={selectedRequest.id} />
            <Fact label="Member segment" value={selectedRequest.memberSegment} />
            <Fact label="Service line" value={selectedRequest.serviceLine} />
            <Fact label="Case type" value={selectedRequest.caseType} />
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

function IndicationTable({ request }) {
  return (
    <section className="table-wrap" aria-label="Indications">
      <div className="table-row table-head">
        <span>Indication</span>
        <span>Attested</span>
        <span>Synapse</span>
        <span>Pathway</span>
      </div>
      {request.synapseResults.map((result) => (
        <div className="table-row" key={result.indicationId}>
          <div>
            <strong>{result.indicationName}</strong>
            <small>{result.category} - {result.sourceDocument}</small>
          </div>
          <Badge variant={request.providerAttestations[result.indicationId] ? 'positive-subtle' : 'neutral'}>
            {request.providerAttestations[result.indicationId] ? 'Yes' : 'No'}
          </Badge>
          <div className="confidence-cell">
            <div
              className="confidence-track"
              role="progressbar"
              aria-label={`${result.indicationName} Synapse confidence`}
              aria-valuenow={Number(result.confidence)}
              aria-valuemin="0"
              aria-valuemax="100"
            >
              <span style={{ width: `${result.confidence}%` }} />
            </div>
            <strong>{result.confidence}%</strong>
          </div>
          <Badge variant={result.pathwayMet ? 'positive' : 'neutral'} className="pill">
            {result.pathwayMet ? 'Met' : 'Not met'}
          </Badge>
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
    confidenceThreshold: Number(rule.confidenceThreshold),
    requireProviderAttestation: rule.requireProviderAttestation,
    minimumPathways: Number(rule.minimumPathways),
    updatedBy: 'Local prototype user'
  };
}

function uniqueIndications(requests) {
  const byId = new Map();

  requests.forEach((request) => {
    request.synapseResults.forEach((result) => {
      byId.set(result.indicationId, {
        id: result.indicationId,
        name: result.indicationName
      });
    });
  });

  return [...byId.values()].sort((a, b) => a.name.localeCompare(b.name));
}

function formatDecision(decision) {
  return isAutoApproved(decision) ? 'Auto-approved' : 'Pended for review';
}

function formatAction(action) {
  return actionLabels[getRuleAction(action)] ?? 'Unknown action';
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
