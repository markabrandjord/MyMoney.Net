-- Step 1. FK target for Accounts.OnlineAccount.
-- Deliberately minimal: this table's full column set arrives as its own numbered step in the
-- slice that brings the OnlineAccount root. A table created by step N and extended by step M is
-- exactly what the step mechanism is for - spec section 1.5, S-3 rule 4.
CREATE TABLE IF NOT EXISTS OnlineAccounts (
    Id      INTEGER NOT NULL PRIMARY KEY,
    Name    TEXT    NOT NULL,
    Version INTEGER NOT NULL DEFAULT 1
) STRICT;
