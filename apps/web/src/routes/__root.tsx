import {
  AppShell,
  AppShellBrand,
  Avatar,
  Body,
  Caption,
  ColourSchemeToggle,
  NavTabs,
  Select,
  appShellRootClassFor,
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
import appCss from "../styles.css?url";

/** The class each Hack Cove theme scopes itself to, on `<html>`. Swapped, never stacked: `dark` over `thrifty-paper` leaks its light link and ink colours. */
const SCHEMES = { light: "thrifty-paper", dark: "dark" };

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
    <html lang="en" className={appShellRootClassFor(theme, SCHEMES)}>
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
      brand={<AppBrand />}
      nav={<AppNav />}
      controls={<AppControls />}
      footer={
        <Caption>A demo: authentication is out of scope, so anyone can act as any adviser.</Caption>
      }
    >
      <Outlet />
    </AppShell>
  );
}

function AppBrand() {
  return (
    <AppShellBrand
      logo={<Avatar name="AQ" />}
      logoSize="lg"
      label="Advisor Quotes"
      render={(props) => <Link to="/" {...props} />}
    />
  );
}

function AppNav() {
  const path = useRouterState({ select: (state) => state.location.pathname });
  const tracesUrl = Route.useLoaderData({ select: (data) => data.tracesUrl });
  return (
    <NavTabs
      label="Main"
      items={[
        {
          label: "Quotes",
          active: path.startsWith("/quotes"),
          render: (props) => <Link to="/quotes" search={{ status: undefined }} {...props} />,
        },
        {
          label: "About this demo",
          active: path === "/about",
          render: (props) => <Link to="/about" {...props} />,
        },
        // A new tab, so the demo stays open beside the trace of what was just done in it.
        ...(tracesUrl ? [{ label: "Traces ↗", href: tracesUrl, external: true }] : []),
      ]}
    />
  );
}

function AppControls() {
  const router = useRouter();
  const theme = Route.useLoaderData({ select: (data) => data.theme }) ?? "light";
  return (
    <>
      <AdviserPicker />
      <ColourSchemeToggle
        schemes={SCHEMES}
        initial={theme}
        onChange={(scheme) => {
          void setTheme({ data: scheme }).then(() => router.invalidate());
        }}
      />
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
    <>
      <Avatar name={current?.name ?? adviserId} />
      <Body as="label" htmlFor="acting-as" color="muted">
        Acting as
      </Body>
      <Select
        id="acting-as"
        value={adviserId}
        onChange={(event: ChangeEvent<HTMLSelectElement>) => {
          void setAdviserId({ data: event.target.value }).then(() => router.invalidate());
        }}
      >
        {options.map((adviser) => (
          <option key={adviser.adviserId} value={adviser.adviserId}>
            {adviser.name} ({adviser.adviserId})
          </option>
        ))}
      </Select>
    </>
  );
}
