# Ralph records

The files in this directory are legacy execution snapshots. They preserve useful
history, decisions, and completed-story evidence, but they are not an active
backlog.

GitHub Issues is the canonical source for planned and in-progress work:

- parent issue: desired outcome, context, constraints, final Definition of Done;
- sub-issue: independently deliverable and testable work;
- pull request: linked to the sub-issue it delivers.

Do not add stories to `prd.json` or `backlog-prd.json`, change `passes` to track
current status, or generate a new PRD for execution. Search and update GitHub
Issues instead. Historical archive files should remain unchanged unless a
separate archival cleanup explicitly migrates or removes them.
