import { crumbLinkClass } from "@hackcove/ui";
import { Link } from "@tanstack/react-router";

/** The first crumb of every quote page: back to the list. */
export function QuotesCrumb() {
  return (
    <Link to="/quotes" search={{ status: undefined }} className={crumbLinkClass}>
      Quotes
    </Link>
  );
}
