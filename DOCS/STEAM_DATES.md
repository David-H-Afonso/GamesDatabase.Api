# Reviewed Steam date references

[Español](STEAM_DATES.es.md) · [API README](../README.md) · [Steam progress](STEAM_ACHIEVEMENT_SETS.md)

## What these dates mean

Steam playtime is accumulated time, not a calendar of sessions. A first achievement
is evidence that the game was played by that point, not necessarily its first play.
Last played and the last saved achievement do **not** prove when the story ended.

The API offers **references for empty fields**. The user chooses each Started/Finished
value explicitly. Reads, opening a detail, reloading references and Steam synchronization
never fill or move these personal dates automatically. An existing date—including one
accepted by an older Steam workflow—is retained. Deliberately changing it belongs in
the normal personal-data editor.

Steam can omit `rtime_last_played` for another account even when its games or achievements
are visible. Do not request someone else's key or scrape around privacy restrictions.
Review version 2 can offer the latest valid cached achievement as a clearly labelled
alternative reference; otherwise the date stays unknown.

## Read references

Authenticated, owner-scoped:

```http
GET /api/steam/date-suggestions?gameId=17&includeStarted=true&reviewVersion=2&timeZone=Europe%2FMadrid
```

Omit `gameId` for the account-wide tool. The current web UI uses 40-game review pages,
searches collection/Steam names and selects only visible fields.

| Field | Meaning |
| --- | --- |
| `reviewVersion: 2` / `reviewTimeZone` | Client capability and the disclosed timezone used for this review |
| `gameId`, `gameName`, `steamAppId`, `steamName` | Owned game and linked Steam identity |
| `updatedAt` | Reviewed game timestamp, returned as UTC |
| `currentStarted`, `currentFinished` | Saved personal dates, never replaced by lookup |
| `proposedStarted`, `proposedFinished` | Optional references for missing fields |
| `startedSource`, `finishedSource` | `firstAchievement`, `lastPlayed`, `lastAchievement` or `none` |
| `observedLastPlayed`, `firstAchievementDate`, `lastAchievementDate` | Available activity evidence, not completion declarations |
| `notes` | Stable informational codes; clients translate known codes rather than rendering raw server text |

References reject Unix 0/1, out-of-range/future timestamps and dates before Steam existed.
Invalid old cache timestamps are filtered **before** choosing the first or last
achievement; one bad timestamp must not discard the whole owned-games list. A reference
that contradicts an already saved counterpart date is not offered.

The browser sends its timezone. The API converts original UTC event timestamps once into
calendar references. Accepted date-only values are not reconverted on later reads. This
matters at midnight, year changes and daylight-saving transitions. Invalid timezone IDs
or review versions return `400`. Older clients default to review 1 and do not receive
the new last-achievement fallback they cannot explain correctly.

An empty result means **no new reliable references**, not that all dates are filled or
that the user never played. Without a linked Steam account, the read returns `400`.
Missing/foreign scoped games never expose another account's records.

## Save exactly the reviewed fields

```http
POST /api/steam/date-suggestions/apply
Content-Type: application/json

{
  "timeZone": "Europe/Madrid",
  "suggestions": [
    {
      "gameId": 17,
      "finished": "2026-06-02",
      "expectedUpdatedAt": "2026-11-26T17:00:00Z"
    }
  ]
}
```

Omitting `started` leaves it unchanged. At most 1,000 distinct games are accepted per
request. Values must be real ISO calendar dates, not future references or a reversed
start/finish pair. Ownership and the supplied reviewed timestamp are checked in a short
transaction. Review 2 clients always send `expectedUpdatedAt`. Occupied fields remain
occupied; there is no implicit overwrite, including for the former `steam` finish source.

The response contains `updated`, `errors`, exact per-field `applied` receipts (`gameId`,
accepted `started`/`finished`, new UTC `updatedAt`) and `skipped` reasons: `changed`,
`unavailable`, `invalidDate`, `hasDate`. A `200` with zero updated games is not a successful
date save. Clients retain the review and require a reload after stale/partial/uncertain
results; they do not automatically repeat writes.

Real date changes create normal field-history entries. A reviewed finish uses the
informational `steam-reviewed` source; subsequent play/sync cannot move it into a
different year. Ratings, estimates, time, status and other personal fields are not
recalculated as a side effect. No schema migration is required for this workflow.

Web selections are bound to their exact candidate and timestamp. An unrelated refresh
cannot silently transfer a check to a newer proposed date.

## Ignore references

`POST /api/steam/date-suggestions/dismiss` takes the same timezone and selected
game/date/timestamp shape. It stores owner-scoped rejection markers, not date values.
An unchanged rejected reference is not offered again. Reload after a partial/uncertain
acknowledgement; never assume every requested marker was saved.

## Why yearly counts differ from a library total

Statistics uses saved Started or Finished dates for games and Replays. The selected
basis is explicit in Overview, Month/year and period rankings, and remains selected
when opening a month. Games with no relevant stored date are not assigned to a year
by guessing from lifetime playtime. Dated achievement activity has its own view.

A user can legitimately have many games/dated unlocks and zero saved finish records.
Review references or enter known personal dates; do not inflate the counter to make
it equal the collection size.

## Concurrent Steam import settings

Steam import and its following `PATCH /api/games/bulk` are separate acknowledged steps.
Bulk settings reserve a short SQLite transaction before reading, so competing cache
writes finish before settings are applied. Catalog IDs must belong to the same owner.
Reapplying identical settings acknowledges the row without a fake edit timestamp;
relationship-only changes still mark a real edit.

SQLite busy/locked errors are sanitized `503 databaseBusy` with `Retry-After: 1`, not
an invalid-input `400`. Do not hide contention by retrying an entire successful import:
retain its receipt and review/retry only unfinished settings when necessary.

References: [Steam IPlayerService](https://partner.steamgames.com/doc/webapi/IPlayerService),
[Microsoft SQLite transactions](https://learn.microsoft.com/dotnet/standard/data/sqlite/transactions).
