# AutoAuth Rules Engine Prototype

This repository contains a fully local prototype for the AutoAuth / MCG Path rules engine rebuild.

The prototype has two local apps:

- `backend/AutoAuth.Api`: ASP.NET Core API with an in-memory rules engine, demo authorization requests, mock Synapse indication results, and evaluation audit history.
- `frontend`: React app for configuring rules, running demo authorizations, and reviewing the rule execution trail.

## What The Prototype Shows

- Confidence threshold mode: Synapse confidence places qualifying indications into a medically necessary bucket.
- Data point combination mode: provider attestation and Synapse agreement must line up on the same indication.
- Pathway threshold mode: customers can require more met pathways than the base guideline threshold.
- Auditability: every evaluation shows which rules fired, which conditions passed or failed, and what action was taken.
- Local-only operation: demo data and audit entries are in memory and disappear when the backend stops.

## Run Locally

Open two terminal windows from this folder.

Backend:

```bash
dotnet run --project backend/AutoAuth.Api
```

Frontend:

```bash
cd frontend
npm install
npm run dev
```

Then open:

```text
http://127.0.0.1:5173
```

The React dev server proxies API calls to the local .NET API at `http://localhost:5178`.
