---
name: execute-plan
description: Use when the user points to an existing implementation plan or when recording the outcome of planned repository work.
---

# Execute an existing plan

Read the whole plan, its status, referenced contracts and applicable folder instructions before
editing. Existing plans live in `Docs/Superpowers/Plans`; no workflow plugin is required.

Confirm the requested work is still pending and its paths match current code. A completed plan is
historical context, not an automatic instruction to repeat the work. Follow the user's current scope.

Perform ordered steps, run each meaningful verification, and update progress/outcome in the plan.
Absorb factual path corrections. If an assumption changes behavior or scope, explain the mismatch
and options, ask the necessary question, and continue independent work.

Use [testing guidance](../../../Docs/TESTING.md). Report observed results and unavailable checks;
never substitute old evidence for a new run. Keep one owner for live Unity. Delegation requires
authorization and support in the current runtime.

Work and commit on `master`, staging exact owned changes. A plan does not authorize external
publication or broad cleanup. Close it with explicit completed, failed or cancelled status and
what actually landed; keep the record instead of deleting it or inventing a filename lifecycle.
