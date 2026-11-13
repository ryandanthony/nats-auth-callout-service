# AuthCallout.Core

The decision core of [nats-auth-callout-service](https://github.com/ryandanthony/nats-auth-callout-service): claims access (`ClaimsAccessor`), claim matching, and grant template expansion.

You normally get this as a dependency of `AuthCallout.Identity` rather than referencing it directly. It has no transport or storage dependencies.

Docs: https://ryandanthony.github.io/nats-auth-callout-service/
