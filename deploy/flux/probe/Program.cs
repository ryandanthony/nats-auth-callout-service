// NACS callout probe for one NATS instance. Usage: dotnet run -- apps-test|apps [natsUrl]
// Token-free checks (6): anonymous grant, in/out-of-grant publish, shared vs broad inbox
// subscribe, malformed bearer fails closed. With NACS_PROBE_TOKEN set to a household user's
// token (never logged), 5 more: connect, own household allowed, other household denied, own
// scoped inbox allowed, shared mise._INBOX.> denied. Calibrated against a healthy callout
// (all pass), no callout (connect denied) and an unreachable server (connect error).
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

// 7-10 (optional): an authenticated household user, token from NACS_PROBE_TOKEN (never logged).
// Proves the profile grant (own household), tenant isolation (another household denied), and
// that the scoped inbox works while the broad mise._INBOX.> root is denied to users.
var token = Environment.GetEnvironmentVariable("NACS_PROBE_TOKEN");
if (!string.IsNullOrEmpty(token))
{
    var payload = System.Text.Json.JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(token.Split('.')[1])).RootElement;
    var house = payload.GetProperty("household_id").GetString()!;
    var sub = payload.GetProperty("sub").GetString()!;
    var inbox = AuthCallout.Contracts.Config.AppSlug.From("mise").ClientInboxPrefix(sub);
    await using var user = new NatsConnection(Opts(token) with { InboxPrefix = inbox });
    var userViolations = new System.Collections.Concurrent.ConcurrentQueue<string>();
    user.ServerError += (_, e) => { userViolations.Enqueue(e.Error); return ValueTask.CompletedTask; };
    try { await user.ConnectAsync(); Report("household token connect granted", true); }
    catch (Exception ex) { Report("household token connect granted", false, ex.GetBaseException().Message); goto done; }

    async Task<string?> UserViolationAfter(Func<Task> act)
    {
        while (userViolations.TryDequeue(out _)) { }
        await act(); await user.PingAsync(); await Task.Delay(300);
        return userViolations.TryDequeue(out var uv) ? uv : null;
    }
    async Task<string?> UserSub(string subject) => await UserViolationAfter(async () =>
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var s = await user.SubscribeCoreAsync<byte[]>(subject, cancellationToken: cts.Token); await Task.Delay(200); await s.DisposeAsync();
    });

    var other = house == "ProbeOther" ? "ProbeOther2" : "ProbeOther";
    v = await UserViolationAfter(() => user.PublishAsync($"mise.house.{house}.commands", "probe"u8.ToArray()).AsTask());
    Report($"user pub own mise.house.{house}.commands allowed", v is null, v ?? "");
    v = await UserViolationAfter(() => user.PublishAsync($"mise.house.{other}.commands", "probe"u8.ToArray()).AsTask());
    Report("user pub other household denied", v is not null && v.Contains("Permissions Violation", StringComparison.OrdinalIgnoreCase), v ?? "no violation");
    v = await UserSub($"{inbox}.>");
    Report("user sub own scoped inbox allowed", v is null, v ?? "");
    v = await UserSub("mise._INBOX.>");
    Report("user sub shared mise._INBOX.> denied", v is not null && v.Contains("Permissions Violation", StringComparison.OrdinalIgnoreCase), v ?? "no violation");
}
done:
Console.WriteLine($"{inst}: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
