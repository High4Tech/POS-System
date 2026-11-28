ORMA / Wijgandu Retail 1.2.0-beta.8 testing update

The full edition is now ORMA and uses the supplied blue logo. Its business onboarding, licensing, profiles and existing modules remain intact. Standalone Wijgandu Retail keeps its original branding, simple local setup and separate data folder.

- Customers shows available return credit. An administrator can apply it to the same customer's unpaid Udhar invoice or record a completed cash/card refund. The customer ledger and exported reports preserve the audit trail.
- New device and batch receipts retain their own costs. Device history is backfilled only from an unambiguous original receipt; unknown historical costs remain unknown.
- Administrators can inspect and release fully returned tracked devices for resale. Original invoices remain intact, duplicate-sale protection remains active, and released devices enter unassigned stock.
- Digital forms can record Pending or Failed provider outcomes without taking payment or posting income. A pending operation can be completed from digital Reports after the provider succeeds. Attempt and posting dates are shown separately.
- Purchase retries require the same complete receipt/payment payload; altered retries are rejected.
- Standalone Retail gains a public signed update feed, package hash and identity checks, downgrade/schema checks, and draft/backup safeguards without commercial activation. Existing beta.7 Retail users need this installer once to gain the new updater.

The Retail Windows x64 testing installer is unsigned. Actual telecom/bank operations still use the authorized provider; physical printer and live-provider integration are not certified by these automated checks. No private signing key or store database is distributed.
