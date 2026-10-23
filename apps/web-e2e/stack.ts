// Its own ports, so a dev stack on 5180/3000 is never reused against the wrong database.
export const apiUrl = "http://localhost:5181";
export const webUrl = "http://localhost:3001";

// The compose Postgres, in its own database: EF's Migrate() creates quotes_e2e on the first run.
export const connectionString =
  process.env["ConnectionStrings__QuotesE2E"] ??
  "Host=localhost;Port=5432;Database=quotes_e2e;Username=quotes;Password=quotes";
