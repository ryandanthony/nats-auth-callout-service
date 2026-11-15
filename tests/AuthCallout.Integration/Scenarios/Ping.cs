// -----------------------------------------------------------------------
// <copyright file="Ping.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Integration.Scenarios;

// P20 task 4: the message types shared by every WolverineIntegrationTests-style class in this
// file group (one type per file, per MA0048; every scenario file references the exact same CLR
// type, since Wolverine's default FullTypeNaming message-type-name strategy keys
// discovery/handler-chain wiring off the type's full name — two differently-named classes
// shaped alike would be two different message types on the wire).

/// <summary>A request the "client" host sends to the "service" host (scenario 1: round trip with verified identity).</summary>
/// <param name="Payload">An arbitrary payload echoed back in <see cref="Pong"/>.</param>
public sealed record Ping(string Payload);
