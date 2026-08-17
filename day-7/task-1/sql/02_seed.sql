-- Deterministic seed data: fixed literal Ids and timestamps only. No CURRENT_TIMESTAMP,
-- no random values -- every run of this script produces byte-identical data.
--
-- Deliberately included so the join/CTE queries have real cases to demonstrate:
--   * Author 7 (Confucius)        -- zero quotes (tests LEFT JOIN / INNER JOIN drop behaviour)
--   * Author 3's Quotes 9 and 10  -- identical CreatedAt (a genuine most-recent tie)
--   * Authors 4 -> 3 -> 2 -> 1    -- an influence chain three levels deep
--   * Tag 6 ('unused-tag')        -- never referenced in QuoteTags, by any author

INSERT INTO Authors (Id, Name, InfluencedByAuthorId) VALUES
    (1,  'Seneca',                 NULL),
    (2,  'Epictetus',              1),
    (3,  'Marcus Aurelius',        2),
    (4,  'Ryan Holiday',           3),
    (5,  'Zeno of Citium',         NULL),
    (6,  'Chrysippus',             5),
    (7,  'Confucius',              NULL),
    (8,  'Laozi',                  NULL),
    (9,  'Friedrich Nietzsche',    NULL),
    (10, 'Simone de Beauvoir',     9);

INSERT INTO Quotes (Id, AuthorId, Text, CreatedAt) VALUES
    -- Seneca (1)
    (1,  1, 'We suffer more often in imagination than in reality.',                         '2023-01-10T08:00:00'),
    (2,  1, 'Luck is what happens when preparation meets opportunity.',                      '2023-03-22T09:15:00'),
    (3,  1, 'It is not that we have a short time to live, but that we waste a lot of it.',    '2023-06-05T14:20:00'),
    -- Epictetus (2)
    (4,  2, 'It is not what happens to you, but how you react to it that matters.',          '2023-02-01T11:00:00'),
    (5,  2, 'No man is free who is not master of himself.',                                  '2023-04-18T16:45:00'),
    (6,  2, 'First say to yourself what you would be; and then do what you have to do.',     '2023-07-30T10:00:00'),
    -- Marcus Aurelius (3) -- Quotes 9 and 10 are a genuine CreatedAt tie for most-recent.
    (7,  3, 'You have power over your mind, not outside events.',                            '2023-01-05T07:30:00'),
    (8,  3, 'The best revenge is to be unlike him who performed the injury.',                 '2023-05-12T12:00:00'),
    (9,  3, 'Waste no more time arguing about what a good man should be. Be one.',            '2023-08-01T09:00:00'),
    (10, 3, 'Very little is needed to make a happy life.',                                    '2023-08-01T09:00:00'),
    -- Ryan Holiday (4)
    (11, 4, 'The obstacle is the way.',                                                       '2023-09-10T08:00:00'),
    (12, 4, 'Focus on what is in your control, let go of what is not.',                       '2023-10-01T13:30:00'),
    -- Zeno of Citium (5)
    (13, 5, 'Well-being is realized by small steps, but is truly no small thing.',            '2023-02-14T10:00:00'),
    (14, 5, 'Man conquers the world by conquering himself.',                                  '2023-06-19T15:00:00'),
    -- Chrysippus (6)
    (15, 6, 'Live in agreement with nature.',                                                 '2023-03-01T09:00:00'),
    (16, 6, 'The wise man is free from passion.',                                             '2023-07-04T11:00:00'),
    -- Confucius (7) has zero quotes -- deliberately.
    -- Laozi (8)
    (17, 8, 'The journey of a thousand miles begins with a single step.',                     '2023-01-20T08:00:00'),
    (18, 8, 'Nature does not hurry, yet everything is accomplished.',                         '2023-05-05T09:30:00'),
    (19, 8, 'When I let go of what I am, I become what I might be.',                          '2023-09-25T17:00:00'),
    -- Friedrich Nietzsche (9)
    (20, 9, 'He who has a why to live can bear almost any how.',                              '2023-02-28T12:00:00'),
    (21, 9, 'That which does not kill us makes us stronger.',                                 '2023-06-11T14:00:00'),
    (22, 9, 'Without music, life would be a mistake.',                                        '2023-11-02T10:00:00'),
    -- Simone de Beauvoir (10)
    (23, 10, 'One is not born, but rather becomes, a woman.',                                 '2023-03-08T09:00:00'),
    (24, 10, 'Change your life today. Do not gamble on the future, act now.',                 '2023-07-15T16:00:00'),
    (25, 10, 'It is up to each of us to invent our own path.',                                '2023-12-01T11:00:00');

INSERT INTO Tags (Id, Name) VALUES
    (1, 'stoicism'),
    (2, 'ethics'),
    (3, 'existentialism'),
    (4, 'taoism'),
    (5, 'nihilism'),
    (6, 'unused-tag');

INSERT INTO QuoteTags (QuoteId, TagId) VALUES
    (1, 1), (2, 1), (3, 2),
    (4, 1), (5, 2), (6, 1),
    (7, 1), (8, 2), (9, 1), (10, 1),
    (11, 1), (12, 2),
    (13, 1), (14, 1),
    (15, 1), (16, 1),
    (17, 4), (18, 4), (19, 4),
    (20, 5), (21, 5),
    (22, 3),
    (23, 3), (24, 3), (25, 3);
    -- Tag 6 ('unused-tag') intentionally has no rows here.
