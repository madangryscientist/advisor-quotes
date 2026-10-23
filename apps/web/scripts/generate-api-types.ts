/**
 * Regenerates src/server/api.gen.ts from the API's OpenAPI document. Run `bun run generate:api`
 * after changing an endpoint and commit the result.
 *
 * It builds the API and starts it in Development with migrations off and a placeholder connection
 * string (the document needs no database), reads /openapi/v1.json, then stops it. Pass a URL or
 * file path as the first argument to read an already published document instead.
 */
import { spawn, spawnSync } from "node:child_process";
import { createServer } from "node:net";

const output = "src/server/api.gen.ts";
const apiProject = "../api/Api.csproj";
const apiDll = "../api/bin/Debug/net10.0/Api.dll";

async function freePort(): Promise<number> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  server.close();
  if (address === null || typeof address === "string") throw new Error("no free port");
  return address.port;
}

async function waitFor(url: string): Promise<void> {
  for (let attempt = 0; attempt < 120; attempt++) {
    try {
      if ((await fetch(url)).ok) return;
    } catch {
      // not listening yet
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${url} did not answer in 60 seconds`);
}

// openapi-typescript drives the TypeScript 5 compiler API, which the app's TypeScript 7 does not
// have. So the generator and its own TypeScript are fetched pinned, outside the app's dependencies.
const generatorPackages = ["typescript@5.9.3", "openapi-typescript@7.13.0"];

function generate(source: string): void {
  const result = spawnSync(
    "bunx",
    [...generatorPackages.flatMap((p) => ["-p", p]), "openapi-typescript", source, "-o", output],
    { stdio: "inherit" },
  );
  if (result.status !== 0) process.exit(result.status ?? 1);
}

const given = process.argv[2];
if (given !== undefined) {
  generate(given);
} else {
  const port = await freePort();
  const url = `http://127.0.0.1:${port}/openapi/v1.json`;
  // Run the built dll itself, not `dotnet run`: that wrapper starts the API as a child, and killing
  // the wrapper would leave the API running on the port.
  const build = spawnSync("dotnet", ["build", apiProject, "--nologo", "-v", "q"], {
    stdio: "inherit",
  });
  if (build.status !== 0) process.exit(build.status ?? 1);
  const api = spawn("dotnet", [apiDll], {
    stdio: "ignore",
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "Development",
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      Database__MigrateOnStartup: "false",
      ConnectionStrings__Quotes: "Host=localhost;Database=openapi-only",
    },
  });
  try {
    await waitFor(url);
    generate(url);
  } finally {
    api.kill();
  }
}
