# Contract Response to Document Delete and Restore Events

When Paperbase publishes `DocumentDeletedEto`, the Contracts module archives the contract linked by `DocumentId`.

This keeps the contract out of normal active workflows while preserving the extracted fields and audit trail. The module does not delete the contract because a document delete is recoverable and the contract record is the business projection of that recoverable document.

When Paperbase publishes `DocumentRestoredEto`, an archived contract is moved back to `Draft` and marked as needing review.

The handler deliberately avoids restoring the previous active state automatically. A restored document can return after user intent or permissions have changed, and the core restore flow does not rerun classification or extraction. Returning to draft makes the projection visible again without silently reactivating a business record.
