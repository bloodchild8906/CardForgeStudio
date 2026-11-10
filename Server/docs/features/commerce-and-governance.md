# Commerce and governance features

## Store & economy

The commerce workspace manages multiple products and global currency guardrails.

### Product fields

- Stable product ID.
- Name and description.
- Decimal price.
- Currency ID.
- Optional artwork asset ID.
- Optional delivered content-pack ID.
- Active state.

### Product operations

- Create or replace products.
- Edit product price, currency, content, and availability.
- Select and remove multiple products from the catalog.
- Activate or deactivate products individually or in bulk.
- Link products to addressable artwork and content-pack delivery metadata.

### Multi-product workflow

1. Upload artwork or content packs first.
2. Create products with stable IDs.
3. Keep new products inactive during review.
4. Verify price and currency against economy settings.
5. Select all products in a promotion and activate them together.
6. Monitor early warnings and transaction records.
7. Deactivate products before removing them from an active campaign.

## Economy configuration

Global configuration includes:

- Soft-currency ID.
- Premium-currency ID.
- Daily soft-currency cap.
- Store tax percentage.

Changing currency IDs can affect rewards, products, wallets, and client presentation. Treat these IDs as stable contracts.

## Wallet adjustments and transaction ledger

Wallet changes are made from **Player intelligence**.

Every adjustment records:

- Player ID.
- Currency.
- Positive or negative amount.
- Resulting balance.
- Reason.
- Administrator actor ID.
- Creation timestamp.

Use wallet adjustments for support grants, refunds, and corrections, not as a replacement for normal game reward processing.

## Early warning

The early-warning workspace evaluates current operations configuration and surfaces potential risk signals.

Current warning domains include:

- Bot fleet availability and configuration.
- LiveOps campaign coverage.
- Rewards without distribution limits.
- Store-product availability.
- Moderation volume and related operational conditions.

Warnings are advisory. Operators must evaluate context before changing production state.

Recommended response:

1. Review severity, category, title, and detail.
2. Open the owning workspace.
3. Confirm the condition is current and not an intentional exception.
4. Apply the smallest safe correction.
5. Refresh warnings.
6. Record or export evidence when policy requires it.

## Audit logs

The audit workspace displays bounded recent structured entries.

Fields can include:

- Timestamp.
- Level.
- Category.
- Message.
- Actor ID.
- Request path.
- HTTP status code.

Use filters and bounded refreshes to investigate:

- Authentication and authorization failures.
- Management changes.
- Content uploads or deletion.
- Game termination.
- Matchmaking runs.
- Wallet adjustments.
- Chat announcements.
- Server errors.

The configured JSONL file is the durable source when `TCG_AUDIT_LOG_PATH` is set.

## Reports

Reports can be downloaded as JSON for structured integrations or CSV for spreadsheet analysis.

Report domains include:

- Players and account state.
- Games and recordings.
- Content and rule packs.
- AI bot profiles and simulations.
- LiveOps campaigns, rewards, and achievements.
- Economy configuration, products, wallets, and transactions.
- Audit activity.

Use JSON when preserving nested structures and CSV for flat review or analysis.

## Archives

Archives create a ZIP snapshot in the current server session.

Capabilities:

- Create an archive by scope, including `full`.
- List generated archive descriptors.
- Download an archive.
- Review byte length and SHA-256 hash.
- Verify the current in-session bytes against the descriptor.

Archive workflow:

1. Generate the archive.
2. Run integrity verification.
3. Download the ZIP.
4. Store it in an approved durable location.
5. Record the SHA-256 hash in the archive register when required.
6. Apply retention and access-control policy outside the process.

Generated archives are held in memory until downloaded and are lost when the process stops.

## Governance roadmap

The management map tracks scheduled reports, automated retention policies, full backup restore, plugin management, expanded system settings, and investigation/appeal workflows as roadmap capabilities.

