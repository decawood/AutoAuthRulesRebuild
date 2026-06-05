# AutoAuth Rules Engine Prototype

This repository contains a fully local prototype for the AutoAuth / MCG Path rules engine rebuild.

The prototype has two local apps plus local data inputs:

- `backend/AutoAuth.Api`: ASP.NET Core API with an in-memory rules engine, demo authorization requests, mock Synapse indication results, and evaluation audit history.
- `frontend`: React app for configuring rules, running demo authorizations, reviewing the rule execution trail, and inspecting objective indication criteria.
- `Guideline XMLs`: local guideline XML exports used by the Objective indications screen.
- `Precision Recall Data`: local performance workbook used to populate precision, recall, and agreement metrics when indication IDs match.

## What The Prototype Shows

- Precision-first staging: precision filters candidate pathways, then selected pathways are added to a rule-specific Medical Necessity Bucket.
- Secondary confidence filtering: Synapse confidence can narrow the staging view without becoming the primary automation rule.
- Bucket-based evaluation: saved bucket pathways, not slider matches alone, drive simulated auto-approval.
- Mixed-evidence ALL pathways: required children can be explicitly covered by Synapse, provider attestation, or a saved Synapse exception.
- Pathway threshold mode: customers can require more met saved bucket pathways than the base guideline threshold.
- Objective indications: guideline XML sections marked `isautoauthorization="true"` render as nested, guideline-style indication rows.
- Precision and recall context: matched workbook rows populate metric columns; guidelines with no matched metric rows use clearly labeled sample metrics.
- Projected reviewer usage: objective indication rows show deterministic provider, payer, and provider-plus-payer selection-rate context for demos.
- Auditability: every evaluation shows which rules fired, which conditions passed or failed, and what action was taken.
- Local-only operation: demo data and audit entries are in memory and disappear when the backend stops.

## Documentation

The project uses the same three-document split as ExecAdmin:

- `requirements/requirements.md`: product intent, workflows, business rules, metric definitions, constraints, and non-goals.
- `requirements/ui-spec.md`: screen-by-screen frontend behavior, layout, states, and accessibility notes.
- `README.md`: technical orientation, run instructions, project structure, backend mechanics, and local data files.

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

## Project Structure

```text
backend/AutoAuth.Api/
  Program.cs                         ASP.NET Core API endpoints and static frontend hosting
  Models/
    PrototypeModels.cs               Rule, request, evaluation, and dashboard records
    ObjectiveGuidelineModels.cs      Guideline summary/detail/node/metric records
  Services/
    PrototypeStore.cs                In-memory rules, demo requests, and evaluation history
    RulesEvaluator.cs                Local rule execution and decision logic
    ObjectiveGuidelineService.cs     Guideline XML parsing and performance workbook matching

frontend/
  src/
    App.jsx                          React app, tabs, rule cards, simulator, indication viewer
    api.js                           Fetch helpers for local API endpoints
    styles.css                       MUCL-inspired visual system and responsive layout
  package.json                       Vite/React scripts

Guideline XMLs/                      Local guideline XML exports
Precision Recall Data/               Local precision/recall workbook
requirements/
  requirements.md                    Product requirements
  ui-spec.md                         Frontend UI specification
scripts/
  launch.sh                          Production-style local launcher
  launch-dev.sh                      Dev launcher for backend plus Vite hot refresh
  build-app.sh                       Builds the macOS Dock wrapper
  build-dev-app.sh                   Builds the macOS dev Dock wrapper
```

## How The Backend Works

The API is intentionally small and local. `Program.cs` registers three singleton services:

- `PrototypeStore` keeps rules, demo authorization requests, and evaluation history in memory.
- `RulesEvaluator` evaluates one demo request against the current rule set and returns a decision trace.
- `ObjectiveGuidelineService` reads guideline XML files and the performance workbook from local folders.

Key endpoints:

| Endpoint | Purpose |
|---|---|
| `GET /api/health` | Local health check |
| `POST /api/shutdown` | Stops the local API after confirmation |
| `GET /api/prototype` | Full dashboard/rules/requests/evaluations snapshot |
| `PUT /api/rules/{id}` | Update one in-memory rule |
| `POST /api/evaluate` | Run a demo request through the rules evaluator |
| `GET /api/objective-guidelines` | List parsed guideline summaries |
| `GET /api/objective-guidelines/{hsim}` | Get one parsed guideline tree with metrics |
| `GET /api/objective-guidelines/precision-preview` | Preview matching guideline pathways for the current staging filters |

When `frontend/dist` exists, the ASP.NET Core app serves the built React frontend from the same process. During development, Vite serves the frontend on `127.0.0.1:5173` and proxies API calls to the backend on `localhost:5178`.
