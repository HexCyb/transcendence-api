# transcendence-api

[![CI](https://github.com/HexCyb/transcendence-api/actions/workflows/ci.yml/badge.svg)](https://github.com/HexCyb/transcendence-api/actions/workflows/ci.yml)

REST API for the Pong game of [ft_transcendence](https://github.com/HexCyb/ft_transcendence): player accounts,
match results that both players have to agree on, and an Elo leaderboard.

**C# · ASP.NET Core 10 (minimal APIs) · PostgreSQL 17 · Dapper · JWT · xUnit · Docker · GitHub Actions**

## Endpoints

| Method | Path | Auth | What it does |
|---|---|---|---|
| `POST` | `/api/auth/register` | — | create a player |
| `POST` | `/api/auth/login` | — | get a JWT (HS256, 60 minutes) |
| `GET` | `/api/users/me` | Bearer | own profile: rating, wins, losses, rank |
| `GET` | `/api/users/{username}` | — | public profile |
| `GET` | `/api/users/{username}/matches` | — | confirmed matches, newest first |
| `GET` | `/api/leaderboard?limit=20` | — | players by rating |
| `POST` | `/api/matches` | Bearer | report a result: `{"opponent":"bob","myScore":5,"opponentScore":3}` |
| `GET` | `/api/matches/pending` | Bearer | results other players reported against me |
| `POST` | `/api/matches/{id}/confirm` | Bearer | the opponent agrees: ratings change |
| `POST` | `/api/matches/{id}/reject` | Bearer | the opponent disagrees: nothing changes |
| `GET` | `/health` | — | `Healthy` when PostgreSQL answers |
| `GET` | `/openapi/v1.json` | — | OpenAPI document |

## Design decisions

- **A result only counts when the opponent confirms it**, so nobody can farm rating by reporting fake wins.
  The reporter cannot confirm their own match (403), players outside the match get 404 (matches cannot be
  enumerated), and a decided match cannot be decided again (409).
- **Elo, K = 32.** The winner takes `round(32 × (1 − E))` points from the loser (at least 1), where `E` is the
  winner's expected score, so the total rating never changes. Equal players: 16 points. A 1400 player beating
  a 1000 player: 3. The upset: 29.
- **No lost updates.** Confirming is one transaction: the match row is locked with `SELECT … FOR UPDATE`, then
  both players, always in id order, so two confirmations at the same moment cannot deadlock or overwrite each
  other's rating change.
- **Login does not reveal which usernames exist.** Unknown user and wrong password get the same 401 and cost
  the same hashing time. Passwords are hashed with ASP.NET Core Identity's `PasswordHasher` (PBKDF2) and
  re-hashed on login when its parameters get stronger.
- **Brute-force limit:** `/api/auth/*` is rate limited per client IP (fixed window, 10 per minute by default) → 429.
- **Plain SQL, versioned.** Dapper over Npgsql. The schema lives in `src/Api/Data/Migrations/*.sql`, embedded in
  the assembly and applied once each at startup under a PostgreSQL advisory lock, so several instances can
  start together. Usernames are unique ignoring case (unique index on `lower(username)`), and `CHECK`
  constraints keep impossible rows out (draws, scores above 21, playing yourself) even if the API had a bug.
- **Errors are Problem Details** (`application/problem+json`), with validation messages per field.
- **Secrets only come from the environment:** `Jwt__Key` (at least 32 bytes, checked at startup) and the
  connection string. Nothing secret is committed; the container runs as a non-root user.

## Run it

```bash
cp .env.example .env          # set POSTGRES_PASSWORD, and JWT_KEY=$(openssl rand -hex 32)
docker compose up -d --build
curl localhost:8080/health    # Healthy
```

```bash
curl -s -X POST localhost:8080/api/auth/register -H 'Content-Type: application/json' \
     -d '{"username":"taha","password":"pong-player-42"}'
TOKEN=$(curl -s -X POST localhost:8080/api/auth/login -H 'Content-Type: application/json' \
     -d '{"username":"taha","password":"pong-player-42"}' | jq -r .accessToken)
curl -s localhost:8080/api/users/me -H "Authorization: Bearer $TOKEN"
```

## Tests

25 xUnit tests. Elo unit tests, plus integration tests that start the real application with
`WebApplicationFactory` against a throwaway PostgreSQL database (created before the first test, dropped after
the last):

- registration rules, usernames unique ignoring case, login answers, token checks, the rate limit, `/health`
- report → pending → confirm → ratings, wins/losses, history and leaderboard rank
- reject changes nothing; only the opponent can decide, only once; impossible scores and opponents are refused

```bash
docker run -d --name pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:17
dotnet test               # TEST_POSTGRES=<connection string> to use another server
```

[CI](.github/workflows/ci.yml) builds with warnings as errors and runs the tests against a PostgreSQL 17
service, then builds the Docker image, starts the compose stack and checks register → login → profile with curl.

## Layout

```
src/Api/
  Program.cs          services, middleware, endpoint groups
  Auth/               register and login, JWT, credential rules
  Users/              profiles, match history, leaderboard
  Matches/            report, confirm, reject, Elo
  Data/               migrator, health check, Migrations/*.sql
tests/Api.Tests/      xUnit unit and integration tests
```

## Status

The ft_transcendence single-page app still keeps its tournaments in the browser. Connecting it to this API
(accounts and a leaderboard page) is the next step.
