-- Step 2. FK target for Accounts.CategoryIdForPrincipal / CategoryIdForInterest.
-- Minimal for the same reason as step 1.
CREATE TABLE IF NOT EXISTS Categories (
    Id      INTEGER NOT NULL PRIMARY KEY,
    Name    TEXT    NOT NULL,
    Version INTEGER NOT NULL DEFAULT 1
) STRICT;
