# Account backup tools

[Español](ACCOUNT_BACKUP_ACCESS.es.md)

Authenticated Standard and Admin accounts can export/import their own CSV library,
run a full or incremental backup to the configured server storage, inspect their
duplicates and backup folders, repair their own image URLs, and explicitly remove
their own orphan folders. Backup storage must already be configured by the server
administrator. CSV export does not require image URL repair or network sync first.

The account comes from authentication. A `userId` query or `X-User-Id` header cannot
choose another library or storage directory. Standard image-cache clearing affects
only that account; global cache clearing, server configuration and scheduled-backup
administration remain Admin-only. Two backups of the same account cannot run together.

An unavailable image leaves its existing URL/file intact. Network media requests
are credential-free and bounded; private destinations require an administrator's
configured artwork-path exception. Missing server storage/configuration is a backup
availability problem, not a reason to grant users administrator roles.
