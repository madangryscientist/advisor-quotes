import {
  AppShell,
  Avatar,
  Body,
  BodyStrong,
  Caption,
  IconButton,
  Select,
  appShellRootClass,
  navTabClass,
} from "@hackcove/ui";
import {
  HeadContent,
  Link,
  Outlet,
  Scripts,
  createRootRoute,
  useRouter,
  useRouterState,
} from "@tanstack/react-router";
import { useEffect } from "react";
import type { ChangeEvent, ReactNode } from "react";
import {
  getAdviserId,
  getDemoLinks,
  getTheme,
  listAdvisers,
  setAdviserId,
  setTheme,
} from "../server/fns";
import type { Theme } from "../server/fns";
import appCss from "../styles.css?url";

/** The class each Hack Cove theme scopes itself to, on `<html>`. Swapped, never stacked: `dark` over `thrifty-paper` leaks its light link and ink colours. */
const THEME_CLASS: Record<Theme, string> = { light: "thrifty-paper", dark: "dark" };

export const Route = createRootRoute({
  head: () => ({
    meta: [
      { charSet: "utf-8" },
      { name: "viewport", content: "width=device-width, initial-scale=1" },
      { title: "Advisor Quotes" },
    ],
    links: [
      { rel: "stylesheet", href: appCss },
      { rel: "icon", href: "/favicon.svg", type: "image/svg+xml" },
    ],
  }),
  loader: async () => {
    const [adviserId, advisers, theme, demo] = await Promise.all([
      getAdviserId(),
      listAdvisers(),
      getTheme(),
      getDemoLinks(),
    ]);
    return { adviserId, advisers, theme, tracesUrl: demo.tracesUrl };
  },
  shellComponent: RootDocument,
  component: RootLayout,
});

function RootDocument({ children }: { children: ReactNode }) {
  const theme = Route.useLoaderData({ select: (data) => data.theme }) ?? "light";
  return (
    <html lang="en" className={`${THEME_CLASS[theme]} ${appShellRootClass}`}>
      <head>
        <HeadContent />
      </head>
      <body>
        {children}
        <Scripts />
      </body>
    </html>
  );
}

function RootLayout() {
  // Input typed into the server-rendered page before React hydrates it is lost, so the e2e tests wait for this.
  useEffect(() => {
    document.documentElement.dataset["hydrated"] = "true";
  }, []);
  return (
    <AppShell
      header={<AppHeader />}
      footer={
        <Caption>A demo: authentication is out of scope, so anyone can act as any adviser.</Caption>
      }
    >
      <Outlet />
    </AppShell>
  );
}

function AppHeader() {
  const path = useRouterState({ select: (state) => state.location.pathname });
  const tracesUrl = Route.useLoaderData({ select: (data) => data.tracesUrl });
  return (
    <>
      <Link to="/quotes" search={{ status: undefined }} className="flex items-center gap-2">
        <Avatar name="AQ" />
        <BodyStrong>Advisor Quotes</BodyStrong>
      </Link>
      <nav aria-label="Main" className="flex gap-1 text-sm">
        <Link
          to="/quotes"
          search={{ status: undefined }}
          className={navTabClass(path.startsWith("/quotes"))}
        >
          Quotes
        </Link>
        <Link to="/about" className={navTabClass(path === "/about")}>
          About this demo
        </Link>
        {tracesUrl && (
          // A new tab, so the demo stays open beside the trace of what was just done in it.
          <a
            href={tracesUrl}
            target="_blank"
            rel="noopener noreferrer"
            title="Every create and status change is traced: open the newest trace to see it"
            className={navTabClass(false)}
          >
            Traces ↗
          </a>
        )}
      </nav>
      <AdviserPicker />
      <ThemeToggle />
    </>
  );
}

/** The "acting as" picker. Picking sets the cookie, then reloads every loader: the list is the adviser filter. */
function AdviserPicker() {
  const router = useRouter();
  const { adviserId, advisers } = Route.useLoaderData();
  // A failed list still leaves the current adviser selectable, so the header never goes blank.
  const options = advisers.ok ? advisers.value : [{ adviserId, name: adviserId }];
  const current = options.find((adviser) => adviser.adviserId === adviserId);

  return (
    <div className="ml-auto flex items-center gap-2">
      <Avatar name={current?.name ?? adviserId} />
      <Body as="label" htmlFor="acting-as" color="muted">
        Acting as
      </Body>
      <Select
        id="acting-as"
        value={adviserId}
        onChange={async (event: ChangeEvent<HTMLSelectElement>) => {
          await setAdviserId({ data: event.target.value });
          await router.invalidate();
        }}
      >
        {options.map((adviser) => (
          <option key={adviser.adviserId} value={adviser.adviserId}>
            {adviser.name} ({adviser.adviserId})
          </option>
        ))}
      </Select>
    </div>
  );
}

function ThemeToggle() {
  const router = useRouter();
  const theme = Route.useLoaderData({ select: (data) => data.theme });
  const next: Theme = theme === "dark" ? "light" : "dark";

  return (
    <IconButton
      label={next === "dark" ? "Use the dark theme" : "Use the light theme"}
      onClick={async () => {
        // classList, not className, so the app shell's own root class survives until the reload below.
        document.documentElement.classList.remove(THEME_CLASS[theme ?? "light"]);
        document.documentElement.classList.add(THEME_CLASS[next]);
        await setTheme({ data: next });
        await router.invalidate();
      }}
    >
      {next === "dark" ? <MoonIcon /> : <SunIcon />}
    </IconButton>
  );
}

const iconProps = {
  "aria-hidden": true,
  width: 16,
  height: 16,
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 2,
  strokeLinecap: "round",
  strokeLinejoin: "round",
} as const;

function MoonIcon() {
  return (
    <svg {...iconProps}>
      <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z" />
    </svg>
  );
}

function SunIcon() {
  return (
    <svg {...iconProps}>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
    </svg>
  );
}
