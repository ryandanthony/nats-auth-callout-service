import type {ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './index.module.css';

type FeatureItem = {
  title: string;
  description: ReactNode;
  link: string;
};

const FeatureList: FeatureItem[] = [
  {
    title: 'OIDC-backed auth callout',
    description: (
      <>
        Clients present standard OIDC access tokens; the service validates
        them against per-issuer JWKS and mints scoped NATS user JWTs. No
        static NATS credentials, no provider lock-in.
      </>
    ),
    link: '/docs/getting-started/overview',
  },
  {
    title: 'Config-driven routing',
    description: (
      <>
        Applications, permission profiles, and claim matching live in{' '}
        <code>files</code>, a Kubernetes <code>CRD</code>, or{' '}
        <code>PostgreSQL</code> — hot-reloaded, atomic, and fail-closed on bad
        config.
      </>
    ),
    link: '/docs/configuration/overview',
  },
  {
    title: 'Templated subject grants',
    description: (
      <>
        Build per-tenant, per-user subject patterns from token claims with
        named template variables and a transform pipeline (lowercase,
        sanitize, hash, and more).
      </>
    ),
    link: '/docs/guides/authorization-model',
  },
  {
    title: 'Verifiable identity propagation',
    description: (
      <>
        A callout-signed <code>Callout-Identity</code> header plus JetStream
        KV lets any subscriber verify a publisher's identity without ever
        seeing the raw NATS message path carry a token.
      </>
    ),
    link: '/docs/guides/identity-propagation',
  },
];

function Feature({title, description, link}: FeatureItem) {
  return (
    <div className={clsx('col col--6', styles.feature)}>
      <div className="padding-horiz--md padding-vert--md">
        <Heading as="h3">
          <Link to={link}>{title}</Link>
        </Heading>
        <p>{description}</p>
      </div>
    </div>
  );
}

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={clsx('hero hero--primary', styles.heroBanner)}>
      <div className="container">
        <Heading as="h1" className="hero__title">
          {siteConfig.title}
        </Heading>
        <p className="hero__subtitle">{siteConfig.tagline}</p>
        <div className={styles.buttons}>
          <Link
            className="button button--primary button--lg"
            to="/docs/getting-started/overview">
            Get Started
          </Link>
          <Link
            className="button button--secondary button--lg"
            to="/docs/configuration/overview">
            Configuration reference
          </Link>
        </div>
      </div>
    </header>
  );
}

function HomepageFeatures() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout
      title={siteConfig.title}
      description="A standalone NATS auth callout service: OIDC token validation, config-driven authorization, and verifiable identity propagation.">
      <HomepageHeader />
      <main>
        <HomepageFeatures />
      </main>
    </Layout>
  );
}
