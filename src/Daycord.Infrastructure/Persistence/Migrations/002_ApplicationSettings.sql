-- DAYCORD — DC-015
-- Application settings.
--
-- Language codes are not restricted to a fixed list.
-- Supported languages are defined by AppLanguage in C#.
--
-- Existing user data is not modified.

CREATE TABLE AppSettings (
                             SingletonId INTEGER NOT NULL PRIMARY KEY
                                 CHECK (SingletonId = 1),

                             Language TEXT NOT NULL
                                 CHECK (
                                     length(Language) BETWEEN 2 AND 35
                                         AND Language = trim(Language)
),

    Theme TEXT NOT NULL
        CHECK (Theme IN ('System', 'Light', 'Dark')),

    MinutesPerDay INTEGER NOT NULL
        CHECK (
            typeof(MinutesPerDay) = 'integer'
            AND MinutesPerDay BETWEEN 1 AND 1440
        )
);

INSERT INTO AppSettings (
    SingletonId,
    Language,
    Theme,
    MinutesPerDay
)
VALUES (
           1,
           'en',
           'System',
           1440
       );