# Job State Machine

Core states:
QUEUED -> ANALYSING -> PLAN_READY -> AWAITING_APPROVAL -> EXECUTING -> TESTING -> PREVIEW_READY -> AWAITING_RELEASE_APPROVAL -> DEPLOYING -> VERIFYING -> SUCCEEDED

Failure states:
FAILED, BLOCKED, CANCELLED, ROLLED_BACK.

Rules:
- Every transition is persisted and audited.
- A failed stage can be retried without restarting unrelated completed stages.
- Only one production deployment per project/environment may execute at once.
- High-risk plans cannot transition to execution without approval.
- Production release cannot be marked succeeded until verification evidence is stored.