-- A payment/top-up callback may be retried by the caller. For a non-empty customer-scoped
-- reference, accept only the first credit and return it on later duplicate calls.

CREATE UNIQUE NONCLUSTERED INDEX UX_BalanceTransaction_TopUpReference
    ON dbo.BalanceTransaction (CustomerId, Reference)
    WHERE Type = 1 AND Reference IS NOT NULL;
