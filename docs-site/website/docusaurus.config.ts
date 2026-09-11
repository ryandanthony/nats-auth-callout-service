import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'nats-auth-callout-service',
  tagline: 'A NATS auth callout service backed by OIDC, config-driven routing, and JetStream KV identity propagation',
  favicon: 'img/favicon.ico',

  url: 'https://ryandanthony.github.io',
  baseUrl: '/nats-auth-callout-service/',
  organizationName: 'ryandanthony',
  projectName: 'nats-auth-callout-service',
  trailingSlash: false,

  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',
  onBrokenMarkdownLinks: 'throw',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  markdown: {
    mermaid: true,
  },

  themes: ['@docusaurus/theme-mermaid'],

  presets: [
    [
      'classic',
      {
        docs: {
          path: '../docs',
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/ryandanthony/nats-auth-callout-service/edit/main/docs-site/docs/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/social-card.png',
    colorMode: {
      defaultMode: 'dark',
      disableSwitch: false,
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'nats-auth-callout-service',
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docs',
          position: 'left',
          label: 'Docs',
        },
        {
          href: 'https://github.com/ryandanthony/nats-auth-callout-service',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Documentation',
          items: [
            {
              label: 'Getting Started',
              to: '/docs/getting-started/overview',
            },
            {
              label: 'Configuration',
              to: '/docs/configuration/overview',
            },
            {
              label: 'Operations',
              to: '/docs/operations/health-and-probes',
            },
          ],
        },
        {
          title: 'Community',
          items: [
            {
              label: 'GitHub',
              href: 'https://github.com/ryandanthony/nats-auth-callout-service',
            },
            {
              label: 'Issues',
              href: 'https://github.com/ryandanthony/nats-auth-callout-service/issues',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Ryan Anthony. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
