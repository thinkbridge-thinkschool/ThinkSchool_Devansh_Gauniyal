-- Deterministic seed data: fixed literal Ids and timestamps only. No CURRENT_TIMESTAMP,
-- no random values -- every run of this script produces byte-identical data.
-- Author names and quote text are entirely invented, for exercise purposes only.
--
-- Deliberately included so the three set-operator questions have every case they can hit:
--   * Wilder Voss (AuthorId 1)      -- has quotes, NONE tagged            (the Q1 answer)
--   * Marguerite Holt (AuthorId 2)  -- has quotes, ALL tagged             (excluded from Q1)
--   * Otis Bramwell (AuthorId 3)    -- SOME tagged, SOME untagged -- the trap: he has at
--                                       least one tag, so under the documented "no tags at
--                                       all" reading he does NOT qualify for Q1.
--   * Freya Lindqvist (AuthorId 4)  -- ZERO quotes -- cannot appear in Q1 (no quotes to have
--                                       "no tags" on).
--   * Callista Wren (AuthorId 5)    -- classic-only  (Q2: excluded, not in both)
--   * Percival Doyle, Solomon Vance -- modern-only    (Q2: excluded, not in both)
--   * Anouk Fenn (AuthorId 7)       -- BOTH classic and modern            (the Q2 answer)
--   * Tag 'wisdom' exists as TWO rows (Id 4, classic; Id 7, modern) -- same name, different
--     category -- so Q3's UNION visibly collapses them to one, unlike UNION ALL.
--   * Tag 'antiquity' (Id 5, classic) is never referenced in QuoteTags by any quote.

INSERT INTO Authors (Id, Name) VALUES
    (1, 'Wilder Voss'),
    (2, 'Marguerite Holt'),
    (3, 'Otis Bramwell'),
    (4, 'Freya Lindqvist'),
    (5, 'Callista Wren'),
    (6, 'Percival Doyle'),
    (7, 'Anouk Fenn'),
    (8, 'Solomon Vance');

INSERT INTO Quotes (Id, AuthorId, Text, CreatedAt) VALUES
    -- Wilder Voss (1) -- quotes, none tagged
    (1,  1, 'A ledger kept by candlelight still balances by morning.',            '2023-01-10T09:00:00'),
    (2,  1, 'The quiet room remembers every argument it never had.',              '2023-02-14T10:00:00'),
    (3,  1, 'No map survives contact with the actual coastline.',                 '2023-03-20T11:00:00'),

    -- Marguerite Holt (2) -- quotes, all tagged
    (4,  2, 'Virtue is a habit mistaken for a mood.',                             '2023-01-15T09:00:00'),
    (5,  2, 'The forum rewards the loud, not the right.',                         '2023-02-20T10:00:00'),
    (6,  2, 'A promise unwritten is still a debt.',                               '2023-03-22T11:00:00'),

    -- Otis Bramwell (3) -- some tagged, some untagged (the trap)
    (7,  3, 'Half of every plan survives the first meeting.',                     '2023-01-25T09:00:00'),
    (8,  3, 'A borrowed opinion still costs full price.',                         '2023-02-25T10:00:00'),
    (9,  3, 'The second guess is rarely wiser than the first.',                   '2023-03-25T11:00:00'),
    (10, 3, 'Nobody drowns in water they admit is over their head.',              '2023-04-25T12:00:00'),

    -- Freya Lindqvist (4) has zero quotes -- deliberately.

    -- Callista Wren (5) -- classic only
    (11, 5, 'The old road still teaches the fastest lesson.',                     '2023-01-05T08:00:00'),
    (12, 5, 'Patience is just courage that learned to wait.',                     '2023-02-05T09:00:00'),
    (13, 5, 'A monument says less than the ruin beside it.',                      '2023-03-05T10:00:00'),
    (14, 5, 'The first draft of history is always self-serving.',                 '2023-04-05T11:00:00'),

    -- Percival Doyle (6) -- modern only
    (15, 6, 'Delete twice, then decide if it mattered.',                         '2023-01-08T08:00:00'),
    (16, 6, 'A calendar is just a to-do list wearing a disguise.',                '2023-02-08T09:00:00'),
    (17, 6, 'The notification is rarely as urgent as its sound.',                 '2023-03-08T10:00:00'),
    (18, 6, 'Good design disappears; bad design apologizes.',                     '2023-04-08T11:00:00'),

    -- Anouk Fenn (7) -- both classic and modern
    (19, 7, 'An old argument in new software is still an old argument.',          '2023-01-12T08:30:00'),
    (20, 7, 'Wisdom ages; the packaging just gets simpler.',                      '2023-02-12T09:30:00'),
    (21, 7, 'Wisdom now ships as a notification, not a scroll.',                  '2023-03-12T10:30:00'),
    (22, 7, 'Speed is not the same as arriving somewhere real.',                  '2023-04-12T11:30:00'),
    (23, 7, 'The cleanest desk still hides an opinion somewhere.',                '2023-05-12T12:30:00'),

    -- Solomon Vance (8) -- modern only
    (24, 8, 'A sprint is just a marathon that lied about its length.',            '2023-01-18T08:00:00'),
    (25, 8, 'Focus is the only luxury that costs nothing to keep.',               '2023-02-18T09:00:00'),
    (26, 8, 'The backlog forgives nothing and forgets even less.',                '2023-03-18T10:00:00'),
    (27, 8, 'Less furniture, more room to think.',                                '2023-04-18T11:00:00');

INSERT INTO Tags (Id, Name, Category) VALUES
    (1,  'stoicism',     'classic'),
    (2,  'virtue',       'classic'),
    (3,  'rhetoric',     'classic'),
    (4,  'wisdom',       'classic'),
    (5,  'antiquity',    'classic'),   -- never used by any quote
    (6,  'asceticism',   'classic'),
    (7,  'wisdom',       'modern'),    -- same name as Tag 4, different category
    (8,  'minimalism',   'modern'),
    (9,  'productivity', 'modern'),
    (10, 'mindfulness',  'modern'),
    (11, 'design',       'modern'),
    (12, 'agility',      'modern');

INSERT INTO QuoteTags (QuoteId, TagId) VALUES
    -- Marguerite Holt -- every quote tagged
    (4, 1), (5, 2), (6, 3),
    -- Otis Bramwell -- only Quotes 8 and 9 tagged; 7 and 10 stay untagged
    (8, 1), (9, 2),
    -- Callista Wren -- classic tags only
    (11, 3), (12, 4), (13, 6), (14, 1),
    -- Percival Doyle -- modern tags only
    (15, 8), (16, 9), (17, 10), (18, 11),
    -- Anouk Fenn -- classic (19, 20) and modern (21, 22, 23)
    (19, 2), (20, 4), (21, 7), (22, 12), (23, 11),
    -- Solomon Vance -- modern tags only
    (24, 9), (25, 10), (26, 12), (27, 8);
    -- Tag 5 ('antiquity') intentionally has no rows here.
