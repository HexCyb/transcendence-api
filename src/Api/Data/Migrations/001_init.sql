-- Players. Usernames are unique ignoring case: "Taha" and "taha" are the same player.
CREATE TABLE users (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username      varchar(16) NOT NULL,
    password_hash text        NOT NULL,
    rating        integer     NOT NULL DEFAULT 1000,
    wins          integer     NOT NULL DEFAULT 0 CHECK (wins >= 0),
    losses        integer     NOT NULL DEFAULT 0 CHECK (losses >= 0),
    created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX users_username_lower_key ON users (lower(username));
CREATE INDEX users_rating_idx ON users (rating DESC);

-- A match is reported by one player and only counts once the opponent confirms it.
CREATE TABLE matches (
    id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    reporter_id    bigint      NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    opponent_id    bigint      NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    reporter_score integer     NOT NULL CHECK (reporter_score BETWEEN 0 AND 21),
    opponent_score integer     NOT NULL CHECK (opponent_score BETWEEN 0 AND 21),
    status         text        NOT NULL DEFAULT 'pending'
                               CHECK (status IN ('pending', 'confirmed', 'rejected')),
    rating_change  integer,
    created_at     timestamptz NOT NULL DEFAULT now(),
    decided_at     timestamptz,
    CHECK (reporter_id <> opponent_id),
    CHECK (reporter_score <> opponent_score)
);
CREATE INDEX matches_pending_idx   ON matches (opponent_id) WHERE status = 'pending';
CREATE INDEX matches_confirmed_idx ON matches (decided_at DESC) WHERE status = 'confirmed';
