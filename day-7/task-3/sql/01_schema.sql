-- Day 7 / Task 3 -- fresh, self-contained SQLite schema. Independent of Day 5's
-- quotesapi.db and of Day 7 Tasks 1 and 2's own databases; nothing here reads any of
-- them at runtime.
-- Run PRAGMA foreign_keys = ON in every connection before using these tables --
-- SQLite parses FK constraints but does not enforce them unless this is set per-session.
PRAGMA foreign_keys = ON;

CREATE TABLE Authors (
    Id   INTEGER PRIMARY KEY,
    Name TEXT NOT NULL
);

CREATE TABLE Quotes (
    Id        INTEGER PRIMARY KEY,
    AuthorId  INTEGER NOT NULL,
    Text      TEXT NOT NULL,
    -- ISO-8601 text (YYYY-MM-DDTHH:MM:SS), consistent with Day 7 Task 1's format.
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (AuthorId) REFERENCES Authors (Id)
);

CREATE TABLE Tags (
    Id       INTEGER PRIMARY KEY,
    Name     TEXT NOT NULL,
    -- Two different Tag rows may share the same Name across categories (deliberately
    -- seeded below) -- Name is not unique on its own, (Category is what distinguishes them.
    Category TEXT NOT NULL CHECK (Category IN ('classic', 'modern'))
);

CREATE TABLE QuoteTags (
    QuoteId INTEGER NOT NULL,
    TagId   INTEGER NOT NULL,
    PRIMARY KEY (QuoteId, TagId),
    FOREIGN KEY (QuoteId) REFERENCES Quotes (Id),
    FOREIGN KEY (TagId) REFERENCES Tags (Id)
);
