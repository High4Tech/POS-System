# Search and panel height fix — beta 3

Installed locally on 21 November 2026 at `D:\SupermarketPos\App`, version `1.1.0-beta.3`. No final installer, cloud deployment or APK change was made.

- The shared medicine autocomplete now uses a non-modal suggestion popup. Typing retains focus; choosing a result, Escape, focus moving elsewhere, an outside click or leaving the window dismisses suggestions. Canceled/stale queries cannot be selected or reopen a dismissed popup. This applies to Register and the existing other callers of `MedicineSearchBox`.
- Register category labels, including All, are bold and centered vertically and horizontally.
- Register, Sales History, Purchase History, Customers and Supplier Balances have horizontal height dividers. Drag vertically or focus the divider and use Up/Down. Widths stay unchanged. Double-click restores the original split. Relative heights are saved in `panel-heights.json` next to the database and therefore stay on the chosen data drive and are included by existing backups.

Verification: 744 full desktop checks passed; the final double-click correction additionally passed the 271-check focused run. Checks exercise repeated searches with suggestions visible, keyboard selection, dismissal/focus, stale query replacement, height changes without width changes, preference reload at another window height, and reset. English/Urdu 100%/200% screen layouts were rendered. No live checkout was used for testing.

Backup: `D:\SupermarketPos\Backups\Before-search-panels-20261121.zip`. Previous app: `D:\SupermarketPos\App-before-search-panels-20261121`. Installed DLL SHA-256: `5064ED763F57626454A3F61B75320D5CFB45CD3197BD938883496CDCDBCB51F4`, matching release output. SQLite integrity and preservation of every original column/row in 31 tables passed; schema remains 11.
