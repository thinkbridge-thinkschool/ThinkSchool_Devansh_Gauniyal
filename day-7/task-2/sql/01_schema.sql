-- Day 7 / Task 2 -- fresh, self-contained SQLite schema. Independent of Day 5's
-- quotesapi.db and of Day 7 Task 1's own database; nothing here reads either at runtime.
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
    -- Stored as 'YYYY-MM-DD HH:MM:SS' text: sorts lexicographically in chronological
    -- order, and julianday() parses this format directly.
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (AuthorId) REFERENCES Authors (Id)
);
