import { ErrorPage, NotFound } from "@hackcove/ui";
import type { LinkRender } from "@hackcove/ui";
import { Link, createRouter, useRouter } from "@tanstack/react-router";
import type { ErrorComponentProps } from "@tanstack/react-router";
import { routeTree } from "./routeTree.gen";

export function getRouter() {
  return createRouter({
    routeTree,
    scrollRestoration: true,
    defaultNotFoundComponent: AppNotFound,
    defaultErrorComponent: AppError,
  });
}

const renderHome: LinkRender = (props) => (
  <Link to="/quotes" search={{ status: undefined }} {...props} />
);

function AppNotFound() {
  return <NotFound renderHome={renderHome} />;
}

/** Retry re-runs the loaders, which also clears the error; the message is shown, a stack never is. */
function AppError({ error }: ErrorComponentProps) {
  const router = useRouter();
  return (
    <ErrorPage
      detail={error instanceof Error ? error.message : String(error)}
      onRetry={() => void router.invalidate()}
      renderHome={renderHome}
    />
  );
}

declare module "@tanstack/react-router" {
  interface Register {
    router: ReturnType<typeof getRouter>;
  }
}
