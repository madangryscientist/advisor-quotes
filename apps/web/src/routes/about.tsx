import {
  Body,
  BulletList,
  Callout,
  Card,
  Code,
  PageHeader,
  Stack,
  SubsectionHeading,
  TextLink,
  Video,
} from "@hackcove/ui";
import { createFileRoute } from "@tanstack/react-router";
import { getDemoLinks } from "../server/fns";

const REPO = "https://github.com/madangryscientist/advisor-quotes";

export const Route = createFileRoute("/about")({
  loader: () => getDemoLinks(),
  component: AboutPage,
});

function AboutPage() {
  const { videoUrl, tracesUrl, resetsNightly } = Route.useLoaderData();

  return (
    <>
      <PageHeader
        title="About this demo"
        subtitle="An adviser quote service: domain modelling, idempotent commands, optimistic concurrency, tracing and tests at every layer"
      />
      <Stack width="narrow">
        {resetsNightly && (
          <Callout tone="info">
            A public demo with made-up clients. Anyone can create and change quotes, and the data
            resets every night.
          </Callout>
        )}
        {videoUrl && (
          <Card>
            <Stack gap="sm">
              <SubsectionHeading>Walkthrough</SubsectionHeading>
              <Video src={videoUrl}>
                <TextLink href={videoUrl}>Download the walkthrough</TextLink>
              </Video>
            </Stack>
          </Card>
        )}
        <Card>
          <Stack gap="sm">
            <SubsectionHeading>Read the code</SubsectionHeading>
            <BulletList>
              <LinkItem href={REPO}>The repository</LinkItem>
              <LinkItem href={`${REPO}/blob/main/SOLUTION.md`}>
                SOLUTION.md: design, trade-offs and the AWS plan
              </LinkItem>
              <LinkItem href={`${REPO}/blob/main/libs/pricing/README.md`}>
                How the premium is calculated
              </LinkItem>
            </BulletList>
          </Stack>
        </Card>
        {tracesUrl && (
          <Card>
            <Stack gap="sm">
              <SubsectionHeading>Follow a request</SubsectionHeading>
              <Body as="p">
                Every API response carries an <Code>X-Trace-Id</Code> header. Create or move a
                quote, then open the <TextLink href={tracesUrl}>trace viewer</TextLink> and find the{" "}
                <Code>command SaveQuote</Code> span, its <Code>price</Code> span and the SQL beneath
                it.
              </Body>
            </Stack>
          </Card>
        )}
      </Stack>
    </>
  );
}

function LinkItem({ href, children }: { href: string; children: string }) {
  return (
    <Body as="li">
      <TextLink href={href}>{children}</TextLink>
    </Body>
  );
}
