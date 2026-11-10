# Player operations features

## Accounts & access

The account workspace lists all registered users with handle, role, rating, and disabled state.

### Moderator capabilities

- List accounts.
- Enable or disable eligible standard users.
- Review current role and state.

### Administrator capabilities

- Perform moderator actions.
- Assign `user`, `moderator`, or `admin` roles subject to server validation.
- Manage privileged-account state.

### Safeguards

- Moderators cannot change privileged accounts beyond their authority.
- Users cannot use a disabled account session.
- Operators should record a support or moderation reason outside the account field when organizational policy requires case tracking.
- Account changes are server-authorized, not merely hidden by the UI.

## Player intelligence

Player intelligence opens the complete server record for any registered player.

### Player summary

- Handle and user ID.
- Role and active/disabled state.
- Rating, global rank, total ranked players, and percentile.

### Inventory

- Owned card instances grouped by card ID.
- Achievement IDs.
- Total card and achievement counts.

### Currency wallet

- Current balances by currency.
- Administrative balance adjustment.
- Required audit reason.
- Complete in-memory transaction ledger with amount, resulting balance, actor, reason, and time.

Wallet adjustment guidance:

1. Confirm the correct player and currency.
2. Use a positive amount for a grant and a negative amount for a deduction.
3. Enter a specific support, refund, or correction reason.
4. Review the resulting ledger entry and balance.

### Direct messages

- Messages sent or received by the player.
- Sender, recipient, timestamp, body, flagged state, and removed state.
- Flag/unflag and remove/restore controls.

### Decks and deck statistics

- Deck ID, name, rule pack, and card count.
- Unique cards, average card cost, and card-type distribution.
- Suspended state.
- Suspend or reinstate selected decks.

### Friends

- Friend connection IDs.
- Both participating user IDs.
- Connection status and creation time.

### Match history and replays

- Game ID, rule pack, turn, finished state, and players.
- Recorded server events with actor, type, message, data, and timestamp.
- Recorded game chat.

The replay viewer reflects server-held recordings. Export required history before restarting the in-memory server.

## Live chat center

The live chat center refreshes automatically and combines player presence, direct-message moderation, game-channel review, and announcements.

### Live metrics

- Online players active during the last two minutes.
- Direct messages available for moderation.
- Active and recorded game channels.

### Online players

- Handle.
- Rating.
- Last activity time.

### Direct-message moderation

- Review all direct messages in reverse chronological order.
- Flag or unflag a selected message.
- Remove or restore a selected message.

Removed messages remain represented for audit and restoration workflows instead of being silently destroyed.

### Game-channel transcripts

- Open active or completed game channels.
- Review participants and status.
- Read the full recorded transcript, including server announcements delivered to that channel.

### Server announcements

Administrators can broadcast a message of 1 to 500 characters.

Delivery behavior:

- Sent immediately to all connected game clients as `serverAnnouncement`.
- Added to each current game transcript with a `SERVER:` prefix.
- Recorded in the audit log.

Use announcements for maintenance, incident communication, event reminders, or service notices. Do not include secrets, private player information, or internal investigation details.

## Presence behavior

A player becomes present when a valid player session is resolved by an authenticated request or hub call. Presence remains active for two minutes after the latest activity. The local management session is excluded from player totals.

If a known connected player is absent:

1. Confirm the client made an authenticated request.
2. Verify its bearer token remains valid.
3. Check that the account is not disabled.
4. Compare client and server connectivity.

## Moderation workflow

1. Open **Live chat center** for current activity or **Player intelligence** for a specific account.
2. Capture the player, message, deck, game, and timestamp identifiers needed by policy.
3. Apply the smallest appropriate moderation action.
4. Review the resulting state.
5. Open **Audit logs** to confirm the operation was recorded where applicable.
6. Export a report or archive if the case must be retained beyond process memory.

