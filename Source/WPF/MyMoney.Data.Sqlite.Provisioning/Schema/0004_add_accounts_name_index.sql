-- Step 4. An index added to a table created by an EARLIER step - the exact shape of issue #34,
-- where CreateOrUpdateTable only emitted index DDL inside its "table doesn't exist yet" branch
-- and therefore silently never created this. Under the step mechanism there is no such branch:
-- a fresh database at version 0 applies steps 1..4 and gets this index because step 4 ran; a
-- database at version 3 applies step 4 and gets this index because step 4 ran. Same step, same
-- executor, same reason. Spec section 1.5, S-4.
CREATE INDEX IF NOT EXISTS IX_Accounts_Name ON Accounts (Name);
