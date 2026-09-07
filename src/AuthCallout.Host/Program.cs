// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

// Skeleton entry point. Host wiring (DI, config, NATS connection, health
// endpoints, hosted services) is added by P13 (host, health, lifecycle).
var builder = Host.CreateApplicationBuilder(args);

var host = builder.Build();
await host.RunAsync();
