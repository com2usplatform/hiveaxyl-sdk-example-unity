# Recipe usage examples

UI-free C# guides showing Recipe calls and the work your application supplies at `APP:` comments.
Choose the example you need and implement its application steps; these are not runnable scenes.

## Reading order

1. Complete [Initialization](Initialization.md).
2. Choose a source in [Adapters](Adapters.md).
3. Read the example below and its [Recipe](../Recipes/README.md).

| Scenario | Source | Application work |
| --- | --- | --- |
| Guest login | [GuestLoginExample.cs](Authentication/GuestLoginExample.cs) | Create or restore a guest; save returned credentials even on failure |
| Auto login | [AutoLoginExample.cs](Authentication/AutoLoginExample.cs) | Restore a saved session; delete it only when StoredCredentialIsStale is true |
| Username login/signup | [UsernameLoginExample.cs](Authentication/UsernameLoginExample.cs) | Collect input; obtain optional signup grant; save session |
| Custom login | [CustomLoginExample.cs](Authentication/CustomLoginExample.cs) | Authenticate with your server and obtain a one-time grant key |
| Provider login | [ProviderLoginExample.cs](Authentication/ProviderLoginExample.cs) | Prepare one credential adapter; save session |
| Link provider | [LinkProviderExample.cs](Authentication/LinkProviderExample.cs) | Sign in first; resolve conflicts; discard only the linked player's guest credentials |
| Logout | [LogoutExample.cs](Authentication/LogoutExample.cs) | Clear the matching saved session only when SessionCleared says so |
| Consumable purchase | [ConsumablePurchaseExample.cs](Payments/ConsumablePurchaseExample.cs) | Catalog/order, external payment, durable pending record, server delivery |
| Undelivered recovery | [UndeliveredRecoveryExample.cs](Payments/UndeliveredRecoveryExample.cs) | Discover entries, reconcile metadata, grant once, close each entry |
| Subscription start | [SubscriptionExample.cs](Payments/SubscriptionExample.cs) | Subscription product/offer, server entitlement grant, confirmation |
| Push preparation | [PushPreparationExample.cs](Notifications/PushPreparationExample.cs) | Native setup, consent, event lifetime, serialized refresh registration |
| Notification permission | [LocalNotificationExamples.cs](Notifications/LocalNotification/LocalNotificationExamples.cs) / RequestPermissionAsync | Requires `com.unity.mobile.notifications`; Permission timing and iOS delegate restoration |
| Notification scheduling | Same file / ScheduleAsync | Requires `com.unity.mobile.notifications`; Future time and saved notification ID |
| Notification cancellation | Same file / Cancel or CancelAll | Requires `com.unity.mobile.notifications`; Saved ID removal only after successful cancellation |

After login, check `IsBlocked` and save the session securely. A null `AutoLoginOutcome.IsBlocked`
is not confirmation that the player is unblocked. For username signup, check `IsNewAccount`;
for custom login, do not automatically reuse a grant key whose redemption status is uncertain.

For `BusinessOutcome`, handle the named outcome. Preserve `UnknownOutcomeCode` and `RawJson` for
unrecognized responses; do not parse human-readable messages to determine the result.

## Payment server integration

Implement [IPurchaseApplication](Payments/PurchaseApplication.cs) against your authenticated game server.
It is an example integration contract with no default implementation.

- Persist pending receipts and order context before proceeding. Remove them only after close succeeds.
- Subscribe to store approval before initiating payment; buffer early events and match them to the order.
  PG opens the returned URL and waits for return/resume. Returning is not proof of payment.
- The server verifies the receipt/token, account, product and price, and grants once per transaction.
  For subscriptions, also check current expiry and refund eligibility.
- `VerifyAndGrantOnceAsync` returns `PurchaseDeliveryConfirmation`;
  `VerifyAndGrantSubscriptionOnceAsync` returns `SubscriptionDeliveryConfirmation`.
  Return null if delivery is unconfirmed and propagate cancellation. Never fabricate client-side success.
- Confirm an eligible transaction already granted as well as a newly granted one. Correlate the response
  with the pending transaction and include any updated closing receipt or verified subscription product ID.
  If that field is absent, the Recipe keeps the pending receipt/product.

The Recipes prepare data; the game server owns verification and delivery.

Payment examples return a tuple of stage outcomes. A null later stage means **not attempted**.
For example, `Prepared.Status == Success` with `Closed == null` does not mean the purchase is complete.
Application callback exceptions propagate; retain the pending purchase for reconciliation.

For recovery, call `FindAsync`, persist the entries, then call `RecoverOneAsync` for each.
An empty successful list is normal; a failed query is not an empty list. Do not restart purchase
initiation to resume an existing order. Subscription renewal, refund and restart reconciliation
require a separate server/store flow, not consumable recovery.

## Lifetime and cancellation

Pass a lifetime cancellation token and await each task. Cancel and await pending work before disposing
its token source, event listener, source or SDK. Serialize account changes and payment operations per order.

Recipes may return `Failure` with `HiveErrorCode.Cancelled`; application callbacks may throw
`OperationCanceledException`. Cancellation is not a refund. Persist returned receipts even after cancellation,
stop subsequent delivery/close work, and reconcile later.

## Scope and compilation

Common examples reference Recipe contracts. Local notification examples have a separate assembly guarded
by `com.unity.mobile.notifications`. Copy the examples and dependencies you need; the application owns
credential storage, navigation, events and server integration.
