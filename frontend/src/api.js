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
