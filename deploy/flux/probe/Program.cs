// Token-free NACS callout probe for one NATS instance. Usage: dotnet run -- apps-test|apps [natsUrl]
// Checks the anonymous grant, in/out-of-grant publish, shared vs broad inbox subscribe, and that a
// malformed bearer fails closed at connect. Calibrated: passes 6/6 against a healthy callout, fails
// when no callout answers or the server is unreachable.
// NACS callout probe (anonymous + malformed-token). Uses a real NATS.Net connection so
// server-side permission violations surface via the connection's error stream.
using NATS.Client.Core;
var inst = args[0];
var url = args.Length > 1 ? args[1] : $"tls://{inst}.ants.zone:4222";
int pass = 0, fail = 0;
void Report(string name, bool ok, string detail = "") { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-52} {detail}"); if (ok) pass++; else fail++; }

NatsOpts Opts(string? token = null) => NatsOpts.Default with
{
    Url = url, TlsOpts = new NatsTlsOpts { Mode = TlsMode.Implicit },
    InboxPrefix = "mise._INBOX", MaxReconnectRetry = 0, ConnectTimeout = TimeSpan.FromSeconds(8),
    AuthOpts = token is null ? NatsAuthOpts.Default : new NatsAuthOpts { Token = token },
};

// 1. anonymous connect granted
await using var conn = new NatsConnection(Opts());
var violations = new System.Collections.Concurrent.ConcurrentQueue<string>();
conn.MessageDropped += (_, _) => ValueTask.CompletedTask;
conn.ServerError += (_, e) => { violations.Enqueue(e.Error); return ValueTask.CompletedTask; };
try { await conn.ConnectAsync(); Report("anonymous connect granted", conn.ConnectionState == NatsConnectionState.Open); }
catch (Exception ex) { Report("anonymous connect granted", false, ex.GetBaseException().Message); return 1; }

async Task<string?> ViolationAfter(Func<Task> act)
{
    while (violations.TryDequeue(out _)) { }
    await act();
    await conn.PingAsync();               // flush: any violation for act() arrives before PONG
    await Task.Delay(300);
    return violations.TryDequeue(out var v) ? v : null;
}

// 2/3. publish inside / outside the anonymous grant
var v = await ViolationAfter(() => conn.PublishAsync("mise.discovery.queries", "probe"u8.ToArray()).AsTask());
Report("anon pub mise.discovery.queries allowed", v is null, v ?? "");
v = await ViolationAfter(() => conn.PublishAsync("mise.house.ProbeX.commands", "probe"u8.ToArray()).AsTask());
Report("anon pub mise.house.ProbeX.commands denied", v is not null && v.Contains("Permissions Violation", StringComparison.OrdinalIgnoreCase), v ?? "no violation");

// 4/5. subscribe to the shared anonymous inbox / the broad inbox root
async Task<string?> SubViolation(string subject)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    return await ViolationAfter(async () => { var sub = await conn.SubscribeCoreAsync<byte[]>(subject, cancellationToken: cts.Token); await Task.Delay(200); await sub.DisposeAsync(); });
}
v = await SubViolation("mise._INBOX.>");
Report("anon sub mise._INBOX.> allowed", v is null, v ?? "");
v = await SubViolation("_INBOX.>");
Report("anon sub _INBOX.> denied", v is not null && v.Contains("Permissions Violation", StringComparison.OrdinalIgnoreCase), v ?? "no violation");

// 6. malformed bearer fails closed at connect (never downgraded to anonymous)
await using (var bad = new NatsConnection(Opts("not-a-jwt")))
{
    try { await bad.ConnectAsync(); Report("malformed bearer denied at connect", false, "connected!"); }
    catch (Exception ex) { var m = ex.GetBaseException().Message; Report("malformed bearer denied at connect", m.Contains("Authorization Violation", StringComparison.OrdinalIgnoreCase), m); }
}
Console.WriteLine($"{inst}: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
