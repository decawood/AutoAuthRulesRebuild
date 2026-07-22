const api = async (path, options = {}) => {
  const response = await fetch(path, {
    headers: {
      'Content-Type': 'application/json',
      ...options.headers
    },
    ...options
  });

  if (!response.ok) {
    const fallback = `${response.status} ${response.statusText}`;
    let message = fallback;

    try {
      const body = await response.json();
      message = body.message || fallback;
    } catch {
      message = fallback;
    }

    throw new Error(message);
  }

  return response.json();
};

export const getPrototype = () => api('/api/prototype');

const metricModeQuery = (metricMode) => {
  const params = new URLSearchParams();
  if (metricMode) {
    params.set('metricMode', metricMode);
  }

  const query = params.toString();
  return query ? `?${query}` : '';
};

export const getObjectiveGuidelines = (metricMode) => api(`/api/objective-guidelines${metricModeQuery(metricMode)}`);

export const getObjectiveGuideline = (hsim, metricMode) => api(`/api/objective-guidelines/${encodeURIComponent(hsim)}${metricModeQuery(metricMode)}`);

export const getPrecisionPreview = ({
  precisionThreshold,
  useConfidenceThreshold,
  confidenceThreshold,
  useSynapseUtilizationRateFilter,
  utilizationReferenceSource,
  synapseUtilizationDelta,
  metricMode
}) => {
  const params = new URLSearchParams({
    precisionThreshold: String(precisionThreshold),
    useConfidenceThreshold: String(Boolean(useConfidenceThreshold)),
    confidenceThreshold: String(confidenceThreshold),
    useSynapseUtilizationRateFilter: String(Boolean(useSynapseUtilizationRateFilter)),
    utilizationReferenceSource: utilizationReferenceSource || 'payer',
    synapseUtilizationDelta: String(synapseUtilizationDelta ?? 0),
    metricMode: metricMode || 'sample'
  });

  return api(`/api/objective-guidelines/precision-preview?${params.toString()}`);
};

export const updateRule = (id, rule) =>
  api(`/api/rules/${id}`, {
    method: 'PUT',
    body: JSON.stringify(rule)
  });

export const evaluateRequest = (requestId) =>
  api('/api/evaluate', {
    method: 'POST',
    body: JSON.stringify({ requestId })
  });

export const shutdownPrototype = () =>
  api('/api/shutdown', {
    method: 'POST',
    body: JSON.stringify({ confirm: true })
  });
