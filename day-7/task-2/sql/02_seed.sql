-- Deterministic seed data: fixed literal Ids and timestamps only. No CURRENT_TIMESTAMP,
-- no random values -- every run of this script produces byte-identical data.
-- Author names and quote text are entirely invented, for exercise purposes only.
--
-- Deliberately included so the window-function queries have real cases to demonstrate:
--   * Callum Reyes (AuthorId 5)     -- exactly ONE quote: LAG gap is NULL, running count
--                                      never exceeds 1.
--   * Nadia Kestrel (AuthorId 6)    -- ZERO quotes.
--   * Talia Marsh Quotes 6 and 7    -- identical CreatedAt (a genuine tie): gap is 0,
--                                      Id is the deterministic tie-break.
--   * Wren Ashby (AuthorId 1)       -- gaps of clearly different sizes: same day (Q1->Q2,
--                                      ~0 days), a few days (Q2->Q3, ~4 days), and several
--                                      months across a year boundary (Q3->Q4, ~219 days,
--                                      2023 -> 2024) -- naive day-of-year subtraction would
--                                      compute Jan-10 (day 10) minus Jun-05 (day ~156) and
--                                      get a negative number instead of +219.

INSERT INTO Authors (Id, Name) VALUES
    (1, 'Wren Ashby'),
    (2, 'Talia Marsh'),
    (3, 'Dorian Fenwick'),
    (4, 'Priya Novak'),
    (5, 'Callum Reyes'),
    (6, 'Nadia Kestrel');

INSERT INTO Quotes (Id, AuthorId, Text, CreatedAt) VALUES
    -- Wren Ashby (1) -- same-day, few-days, and year-boundary gaps
    (1,  1, 'The map is never the mountain, only a promise about the mountain.',   '2023-06-01 09:00:00'),
    (2,  1, 'Patience is a room you build one plank at a time.',                  '2023-06-01 15:00:00'),
    (3,  1, 'A held breath teaches more than a shouted answer.',                  '2023-06-05 10:00:00'),
    (4,  1, 'Winter asks the same questions summer avoided.',                     '2024-01-10 08:00:00'),
    (5,  1, 'Small repairs, done early, prevent large collapses.',                '2024-01-15 08:00:00'),

    -- Talia Marsh (2) -- Quotes 6 and 7 are a genuine CreatedAt tie
    (6,  2, 'Attention paid freely is the only gift that costs everything.',      '2023-04-10 12:00:00'),
    (7,  2, 'Two clocks can strike the same hour and still disagree about time.', '2023-04-10 12:00:00'),
    (8,  2, 'Light borrowed is still light.',                                     '2023-08-20 09:00:00'),

    -- Dorian Fenwick (3) -- ordinary spread across the year
    (9,  3, 'Every ledger eventually asks to be read aloud.',                     '2023-01-05 08:00:00'),
    (10, 3, 'A door left ajar is not the same as an invitation.',                 '2023-01-20 10:00:00'),
    (11, 3, 'The quiet ones keep the loudest records.',                          '2023-02-10 09:00:00'),
    (12, 3, 'Debt is just memory with interest.',                                '2023-03-01 11:00:00'),
    (13, 3, 'No harvest forgives a skipped planting.',                           '2023-03-25 09:00:00'),
    (14, 3, 'The bridge remembers every crossing, even the ones that turned back.', '2023-04-15 10:00:00'),
    (15, 3, 'Ambition without a map is just motion.',                            '2023-05-10 09:00:00'),
    (16, 3, 'What the fire spares, the flood usually finds.',                    '2023-06-15 11:00:00'),
    (17, 3, 'A promise deferred is still a promise owed.',                       '2023-07-20 09:00:00'),
    (18, 3, 'The archive does not forgive; it only files.',                      '2023-09-01 10:00:00'),
    (19, 3, 'Last words are rarely the important ones.',                        '2023-11-15 09:00:00'),

    -- Priya Novak (4) -- ordinary spread across the year
    (20, 4, 'A garden untended still keeps its own schedule.',                   '2023-01-12 08:30:00'),
    (21, 4, 'The tide does not negotiate with the shoreline.',                   '2023-02-02 09:15:00'),
    (22, 4, 'Every apprenticeship ends the day you stop asking why.',            '2023-02-28 10:00:00'),
    (23, 4, 'Silence is a language most people never study.',                    '2023-03-19 09:45:00'),
    (24, 4, 'The recipe survives; the cook is optional.',                        '2023-04-22 08:00:00'),
    (25, 4, 'A compass is only honest when nobody is watching.',                 '2023-05-30 10:30:00'),
    (26, 4, 'Grief and gratitude often share a doorway.',                        '2023-07-04 09:00:00'),
    (27, 4, 'The second draft is where the honesty begins.',                     '2023-08-11 09:20:00'),
    (28, 4, 'No harbor was ever built for calm weather.',                        '2023-09-29 10:00:00'),
    (29, 4, 'A rumor is a fact that got tired of waiting.',                      '2023-10-31 09:00:00'),
    (30, 4, 'The last mile costs more than the first ten.',                      '2023-12-05 10:00:00'),

    -- Callum Reyes (5) has exactly ONE quote -- deliberately.
    (31, 5, 'A single note, played well, outlasts a careless symphony.',         '2023-07-02 09:00:00');

    -- Nadia Kestrel (6) has zero quotes -- deliberately.
