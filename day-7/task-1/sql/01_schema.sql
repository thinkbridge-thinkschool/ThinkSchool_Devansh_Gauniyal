-- Day 7 / Task 1 -- fresh, self-contained SQLite schema (independent of Day 5's quotesapi.db).
-- Run PRAGMA foreign_keys = ON in every connection before using these tables --
-- SQLite parses FK constraints but does not enforce them unless this is set per-session.
PRAGMA foreign_keys = ON;

CREATE TABLE Authors (
    Id                   INTEGER PRIMARY KEY,
    Name                 TEXT NOT NULL,
    -- Nullable self-reference: NULL means "no known influence" (a root author).
    InfluencedByAuthorId INTEGER NULL,
    FOREIGN KEY (InfluencedByAuthorId) REFERENCES Authors (Id)
);

CREATE TABLE Quotes (
    Id        INTEGER PRIMARY KEY,
    AuthorId  INTEGER NOT NULL,
    Text      TEXT NOT NULL,
    -- Stored as ISO-8601 text (YYYY-MM-DDTHH:MM:SS) so that ordering by CreatedAt as a
    -- plain TEXT column is lexicographically identical to ordering chronologically.
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (AuthorId) REFERENCES Authors (Id)
);

CREATE TABLE Tags (
    Id   INTEGER PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE
);

CREATE TABLE QuoteTags (
    QuoteId INTEGER NOT NULL,
    TagId   INTEGER NOT NULL,
    PRIMARY KEY (QuoteId, TagId),
    FOREIGN KEY (QuoteId) REFERENCES Quotes (Id),
    FOREIGN KEY (TagId) REFERENCES Tags (Id)
);
