# Project instructions

Simplicity is a prerequisite for reliability. Keep logic small and canonical,
and reuse existing code, components and designs unless there is a clear reason
to introduce a new approach.

## Releases

Publish a GitHub release only when the user explicitly requests it. Requests to
commit, push, build, package or create a tag alone do not authorize publishing.
Keep release publishing manual-only; do not add automatic tag, branch, schedule
or workflow-completion triggers.

Keep GitHub release notes focused on user-facing changes and installation.
Keep test counts, validation/audit summaries and QA testing limitations in
`docs/QA-REPORT.md`, rather than release notes.
