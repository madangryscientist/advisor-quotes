/**
 * The anti-slop rules Oxlint has no rule for: a TODO/FIXME with no issue ref, and
 * commented-out code. Run by `bun run lint` after oxlint; any hit fails the gate.
 */
import { globSync, readFileSync } from "node:fs";

const TODO_WITHOUT_REF = /\b(TODO|FIXME|XXX|HACK)\b(?!\s*\(#?[\w-]*\d+\))/;
// Two signals, so prose does not trip it: a statement keyword AND a trailing ; { } or ).
const CODE_IN_COMMENT =
  /^\s*\/\/\s*(?:const|let|var|import|export|return|await|if|for|while|function|throw)\b.*[;{})]\s*$/;

const hits: string[] = [];
for (const path of globSync("{src,scripts}/**/*.{ts,tsx}")) {
  if (path === "scripts/check-slop.ts") continue;
  readFileSync(path, "utf8")
    .split("\n")
    .forEach((line, i) => {
      if (TODO_WITHOUT_REF.test(line))
        hits.push(`${path}:${i + 1}: TODO/FIXME needs an issue ref, e.g. TODO(#123)`);
      if (CODE_IN_COMMENT.test(line))
        hits.push(`${path}:${i + 1}: commented-out code; delete it (git keeps it)`);
    });
}
if (hits.length > 0) {
  console.error(hits.join("\n"));
  process.exit(1);
}
