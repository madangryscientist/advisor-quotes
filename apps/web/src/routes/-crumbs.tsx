import type { LinkRenderProps } from "@hackcove/ui";
import { Link } from "@tanstack/react-router";

/** The first crumb of every quote page: back to the list. */
export const quotesCrumb = {
  label: "Quotes",
  render: (props: LinkRenderProps) => (
    <Link to="/quotes" search={{ status: undefined }} {...props} />
  ),
};
