-- Step 3. The Plan A vertical's one fully-populated table.
--
-- Id is INTEGER (64-bit) because IAggregateRoot.Id is long - do NOT let a fresh schema narrow it
-- to INT because that is what the old tables said (spec section 5).
--
-- OpeningBalance is INTEGER ten-thousandths, which is exactly SQL Server's money representation
-- (see MoneyScale). LastSync/LastBalance are TEXT 'yyyy-MM-dd HH:mm:ss.fff' (lexicographically
-- sortable). SyncGuid is TEXT "D".
--
-- OnlineAccount / CategoryIdForPrincipal / CategoryIdForInterest exist as real, nullable foreign
-- keys even though Plan A can never set them non-null: no slice in 1-7 produces an OnlineAccount
-- or Category root, so the null round trip is lossless HERE. The slice that brings those roots
-- must revisit AccountRowCodec at the same time.
CREATE TABLE IF NOT EXISTS Accounts (
    Id                     INTEGER NOT NULL PRIMARY KEY,
    AccountId              TEXT,
    OfxAccountId           TEXT,
    Name                   TEXT    NOT NULL,
    Type                   INTEGER NOT NULL,
    Description            TEXT,
    OnlineAccount          INTEGER REFERENCES OnlineAccounts (Id),
    OpeningBalance         INTEGER NOT NULL,
    LastSync               TEXT,
    LastBalance            TEXT,
    SyncGuid               TEXT,
    Flags                  INTEGER,
    Currency               TEXT,
    WebSite                TEXT,
    ReconcileWarning       INTEGER,
    CategoryIdForPrincipal INTEGER REFERENCES Categories (Id),
    CategoryIdForInterest  INTEGER REFERENCES Categories (Id),
    Version                INTEGER NOT NULL DEFAULT 1
) STRICT;
