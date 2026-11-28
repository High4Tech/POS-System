# ORMA and Retail functional completion

Extend the existing shared application in both editions. Keep the main edition's business onboarding/licensing and all existing store data. Main branding becomes ORMA using the supplied logo; standalone Retail remains Wijgandu with its existing icon, local setup and separate data directory.

1. Customer credits: extend the existing returns service with auditable, idempotent credit refunds/applications. Update Customers balance/ledger/actions and Reports. Reuse existing sale balance credit fields; add a credit-movement table through an additive migration.
2. Cost accuracy: snapshot serial receiving costs, retain batch receiving costs, and use the selected device/batch cost at checkout. Unknown historical costs remain unknown rather than guessing from a later purchase. Extend trade ledger, purchase/sale services, domain entities, migration and tests.
3. Device returns: add administrator inspection/release of fully returned serial devices; preserve original sale and refund records. Make current availability authoritative while preserving duplicate-sale protection and receiving cost. Update return UI and tests.
4. Digital outcomes: expose Pending/Failed alongside normal completed payment without mandatory verification checkboxes. Reuse existing status changes and Reports history, keep pending/failed entries out of successful postings, and test recovery/idempotency.
5. Purchase retries: compare the complete original receiving request, including identifiers, prices, dates and initial payment. Reject changed retries; test unchanged retries and conflicts.
6. Retail updates: reuse Velopack and signed release verification with an independent signed Retail feed, separate package identity and version comparison. Retain license enforcement in the full edition. Keep publisher private keys outside Git and store data.
7. Branding: condition build resources, product metadata, window titles, installer icon and desktop shortcut by edition. Preserve executable/package/data identities for upgrade compatibility.

Verify focused regression cases, additive migration/backup restore and both editions' UI/resources. Build Release payloads using the existing scripts, update the closed main installation with backup/hash/shortcut checks, refresh the separately requested Retail installer, and publish source/versioned release assets to the existing repositories. Do not create piled application folders or a new main commercial distribution installer.

## Verification before packaging

- First backend regression: 251/253 passed. The pharmacy fixture now explicitly declares its opening-batch cost; the Windows host recovery retest passed (the sandbox could not open its native SQLite backup). Final focused financial run: 13 passed, including located-device resale, unknown batch cost, legacy migration/backfill, credit report totals, and Pending/Failed validation/posting dates.
- Initial Windows completion checks: 47 passed, covering real credit apply/refund UI, enlarged English/Urdu forms, PDF generation, direct unsettled recording and signed Retail policy/package/feed checks.
- Direct-checkout regression exposed the extra date column squeezing Urdu/200 history rows. History columns now size to their content; final published-payload checks must pass before installation.
- Main shortcut helper self-check passed: rename, repeat, preserve unrelated Wijgandu shortcut, and reject an unrelated existing ORMA target.

Approved devices return to unassigned stock; existing shelf balances are preserved and location assignment remains in Products & Stock. Historical costs and original invoices are never reconstructed from current prices. Credit refunds appear separately from original sales in X/Z/range PDFs and Reports. Retail uses its own signed public testing feed; main commercial licensing and Authenticode policy remain intact.

Published ORMA payload passed 52 completion/update checks and 269 direct-checkout/receipt/keyboard assertions. Retail candidate verification caught WPF reusing the full edition's cached .g.resources when switching flags. The build script now cleans generated outputs before publishing, and ModelTools verifies the embedded icon against the already-verified edition icon. The actual ORMA payload passed this check; the mixed Retail candidate was rejected. Rebuild Retail and re-sign its final bytes before publication.
