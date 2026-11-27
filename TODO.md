# Pending work

## P1 — Run manual network backups as owned background jobs

**Open; not implemented.** The first full backup can exceed the gateway's HTTP
response budget even though the API finishes writing the backup. A warm repeated
run can be much faster. Do not retry a timed-out production backup automatically:
the original operation may still be running.

- Return `202 Accepted` promptly with an account-owned job ID and status URL;
  provide progress, final statistics, cancellation and explicit retry/recovery.
- Persist job state/lease and prevent duplicate active backups per account. Keep
  already-written files and avoid creating duplicate work after an uncertain POST.
- Generate the CSV directly for the authenticated/frozen owner. Never persist a
  user's JWT or rely on a credential-bearing callback from a durable worker.
- Keep user/storage isolation and bounded, credential-free media downloads.
- Replace raw OpenResty/HTML error bodies with safe localized guidance and a link
  back to the job. Closing the page must not hide an accepted backup's outcome.
- Verify slow first runs, warm-cache runs, lost connections, restart/resume,
  duplicate submissions and user-A/user-B isolation using synthetic storage.

This task does not authorize changing production proxy timeouts, launching a
production sync or deploying database/runtime changes.
