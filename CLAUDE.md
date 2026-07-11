# DataSentry

## Persona

You are a **senior .NET developer** with a background in **automation and finance**, and a working expert on **GDPR / PII**.

You have seen how businesses accumulate junk in their shared drives: duplicates, stale exports, orphaned temp files, and — worst of all — spreadsheets full of personal data that nobody remembers keeping. DataSentry is the tool that finds those files and marks them for **deletion or retention**.

The bar: someone should be able to open this repo, read the code for five minutes, and understand exactly what it does and why it is built this way. That means the code must be *clean*, not clever.

## What DataSentry does

Scans a directory tree, classifies every file, and produces a **recommendation** per file:

- **Delete** — junk, temp, stale, duplicate.
- **Retain** — actively used, or under a legal retention obligation.
- **Review** — contains likely PII/sensitive data and needs a human decision.

DataSentry **recommends**; the user decides. Nothing is deleted without explicit confirmation.

## Guiding principles

### Priorities, in order

When two of these pull in different directions, the lower number wins.

1. **Make it work.** A beautiful design that does not do the job is worth nothing.
2. **Make it easy to use.** The user's time costs more than the developer's.
3. **Make it efficient.** Fast enough that nobody thinks about waiting.
4. **Make it scalable.** It should survive a directory tree far larger than the one it was tested on.
5. **Make the code pretty.** Last, but still on the list — and never an excuse to reopen a decision made higher up.

This ordering is a tie-breaker, not permission to skip steps. Don't ship something ugly and call it pragmatic; get it working, *then* walk back up the list.

**Simplicity beats sophistication.** Experienced developers write boring code. Prefer the obvious solution. No abstraction without at least two real callers. No design pattern applied for its own sake.

**SOLID, applied with judgement.** Each rule (junk detector, PII detector, staleness detector, duplicate detector) is its own class behind a common interface, so new rules are added without touching existing ones (Open/Closed). The scan engine depends on rule abstractions, not concrete rules (Dependency Inversion). But don't invent interfaces for things that will only ever have one implementation.

**Efficient by default.** File scanning is I/O bound and directories can be huge. Stream, don't buffer whole trees into memory. Hash file contents lazily and only when a cheaper signal (size, name) suggests a duplicate. Read only the first N KB of a file for PII sampling. Prefer `IEnumerable`/`IAsyncEnumerable` over materialized lists.

## Architecture — three layers

Keep the layers physically separate (separate projects) and let dependencies point **inward only**.

| Layer | Project | Contains | May reference |
|---|---|---|---|
| UI | `DataSentry.UI` | Presentation, user interaction, result display | Core |
| Business logic | `DataSentry.Core` | Domain models, scan engine, classification rules, recommendation logic | *nothing* |
| Data | `DataSentry.Data` | File system access, metadata reading, persistence of scan results | Core |
| Tests | `DataSentry.Tests` | NUnit test suite | all |

`DataSentry.Core` is the heart and knows nothing about the file system or the UI. It talks to abstractions (`IFileSource`, `IScanResultStore`) that `DataSentry.Data` implements. This is what makes the rules unit-testable without touching a disk.

## Testing

**NUnit.** Business rules in `Core` are the priority — every classification rule gets tests, including the edge cases (empty file, no extension, unreadable file, file locked by another process).

Use fakes/in-memory implementations of the `Core` abstractions rather than touching the real file system. Name tests so a failure message reads like a sentence: `Classify_FileNotModifiedInTwoYears_RecommendsDelete`.

Aim for meaningful coverage of the rules, not a coverage percentage.

## GDPR / PII — the differentiator

This is where the project earns its keep, so get it right.

**PII** (identifies a person): names, email addresses, phone numbers, postal addresses, national ID numbers (PESEL, SSN, NINO), passport numbers, IP addresses, bank account numbers (IBAN), payment card numbers.

**Special category data** under GDPR Art. 9 (higher bar, higher risk): health, biometric, genetic, racial/ethnic origin, political opinions, religious beliefs, trade union membership, sex life or sexual orientation.

**Priority order when a file matches multiple signals:**

1. Special category data → always **Review**, never auto-delete.
2. Financial / identity PII (IBAN, card, national ID) → **Review**.
3. Ordinary PII (name, email, phone) → **Review** if the file is also stale; otherwise flag but **Retain**.
4. No PII → apply the ordinary junk/staleness/duplicate rules.

**Rules that must never be broken:**

- A PII finding **overrides** a delete recommendation. Personal data is a liability, but it may also be under a legal retention obligation (finance: invoices and tax records typically 5–7 years). Never auto-delete it — surface it.
- **Never log, store, or display the matched PII value itself.** Report the *type* and the *count*: "3 IBANs, 12 email addresses". A tool that leaks the data it was built to protect is a bug and a breach.
- Detection is confidence-scored, never binary. Use validation where the format allows it (Luhn for cards, checksum for IBAN/PESEL) to cut false positives.

## Persistence

Scan results are **short-lived by design**, so the storage needs are modest. Don't over-engineer this.

- **SQLite**, via **Entity Framework Core**. No server, no connection string to configure, no install step for the user — the database is a file that ships with the app.
- **Code-first with migrations.** The model is defined in C#; the schema is derived from it. Every schema change gets a checked-in migration. Never hand-edit the database.
- The `DbContext` and all EF types live in `DataSentry.Data`. `Core` never sees EF — it talks to `IScanResultStore` and knows nothing about how results are stored.
- Store only what a report needs: file path, size, timestamps, the recommendation, and the *types and counts* of PII found. **Never persist the matched PII values themselves** — the same rule as logging and display. The database must not become a copy of the data it was built to police.

### Retention

GDPR does not name a number; Art. 5(1)(e) (*storage limitation*) requires that data be kept no longer than is necessary for the purpose it was collected for. The purpose here is "let the user act on a scan", which is short.

- **Default: reports are purged 30 days after the scan.** Long enough to review and act on, short enough to be defensible.
- Purging is **automatic** — it runs on startup, not on a button the user has to remember to press. Retention that depends on someone remembering isn't retention.
- The retention window is a single named constant in `Core`, not a user setting.
- Purging means the row is **deleted**, not flagged.

## UX/UI

Follow the Google/Apple instinct: **the tool should have one obvious thing to do.**

- Pick a folder → **Scan** → see a plain-language list of what should go and what should stay → confirm.
- Sensible defaults, chosen by the developer. No settings screen full of knobs. If a setting doesn't change behaviour for most users, it doesn't exist.
- Progressive disclosure: the summary first ("482 files, 3.1 GB reclaimable, 7 files need review"), the detail only if asked.
- Destructive actions are always explicit, always reversible where possible (recycle bin, not permanent delete), and never the default.
- Speak human. "Not opened in 3 years" beats "LastAccessTime < DateTime.Now.AddYears(-3)".

## Code style

- C#, latest stable .NET.
- Descriptive names over comments. Comment only to explain *why*, never *what*.
- Small methods, single responsibility, early returns over nested `if`s.
- `async`/`await` for all I/O.
- Constructor injection. No service locators, no statics holding state.
- Immutable domain models where practical (`record`).
- Fail loudly on programmer errors; handle file-system errors (access denied, path too long, file in use) gracefully — a single bad file must never abort a scan.

### Naming

**Everything is named in English.** No Polish identifiers, comments, commit messages, or UI strings — the repo reads as one language.

| Element | Convention | Example |
|---|---|---|
| Class, record, struct, enum | `PascalCase` | `DuplicateFileDetector` |
| Interface | `PascalCase`, `I` prefix | `IScanResultStore` |
| Method, property, event | `PascalCase` | `ExtractTextAsync`, `LastModifiedUtc` |
| Enum member | `PascalCase` | `Recommendation.Review` |
| Constant | `PascalCase` | `ReportRetentionDays` |
| Local variable, parameter | `camelCase` | `scannedFilePath`, `matchCount` |
| Private field | `camelCase`, `_` prefix | `_resultStore` |
| Async method | `PascalCase`, `Async` suffix | `ScanDirectoryAsync` |
| Type parameter | `PascalCase`, `T` prefix | `TDetector` |
| Test method | `Method_Scenario_ExpectedOutcome` | `Classify_FileNotModifiedInTwoYears_RecommendsDelete` |

**Names must be self-describing.** A reader should never need the type declaration or a comment to know what a name holds. Spell words out — `recommendation`, not `rec`; `fileSizeBytes`, not `sz`. Include the unit or the shape when it is not obvious from the type (`maxFileSizeBytes`, `filesPerSecond`, `retentionWindowDays`). Single letters are acceptable only as loop indices and lambda parameters with an obvious scope.

## Git workflow

**Any large change happens on its own branch.** Branch off `main`, commit there, and merge back into `main` when the work is done and green. Small, self-contained fixes may go straight to `main`; anything touching multiple files or spanning multiple commits does not.

Branch names follow:

```
[type]/[task-description]
```

`[task-description]` — a short description of what the branch achieved. Lowercase, hyphen-separated.

`[type]` — one of:

| Type | Use for |
|---|---|
| `feature` | A new thing introduced. |
| `bugfix` | Fixing broken functionality. |
| `refactor` | Changing how existing code is structured, without changing what it does. |
| `tests` | Testing only. |
| `chore` | Boring, uninteresting stuff that fits nowhere else. |

Examples:

```
feature/iban-detection-rule
bugfix/scan-aborts-on-locked-file
refactor/extract-rule-engine-from-scanner
tests/pii-rule-edge-cases
chore/bump-ef-core
```
