# Account language

[Español](ACCOUNT_LANGUAGE.es.md)

`preferredLanguage` is an optional account preference: `en`, `es`, or `null` when
no account choice has been saved. Profile, user-list and login responses include
the field, including a JSON null. Older APIs omit it.

- An administrator can set it in `POST /api/users` or change it with
  `PUT /api/users/{id}`.
- A user can change their own preference through the same PUT route. Standard
  users cannot edit another account.
- Omitting the field, or sending null in an update, preserves the current choice.
  Other values are rejected. The last explicit user/admin choice takes effect.

```json
{ "preferredLanguage": "es" }
```

The web client loads the signed-in account before opening the tutorial. A saved
account language takes priority over the device's previous language. Appearance
saves an explicit new choice; opening the app never writes a cached choice back.
Changes made elsewhere are read again on sign-in, reload or window focus.

Creating an account in Spanish also gives its initial catalogs Spanish labels.
Changing language later does not rename existing or customized catalog entries.

`20261127141118_AddUserPreferredLanguage` adds one nullable column to `user`.
It does not assign a language to existing accounts. Back up SQLite before startup
with the new schema. For binary rollback, retain the column and current data;
the previous binary simply does not use it.
