// Proof app for the secure-by-default configuration (protecting-anon-access.md §7).
// .NET 10 file-based app: `dotnet run Prove.cs` (stack must be running, see README.md).
//
// Unlike CLI smoke tests, every check here is a *delivery* check: a publish that the
// server kills with an async permissions violation still looks "sent" to the client,
// so the only honest assertion is whether a subscriber actually received the message.
#:package NATS.Net@3.2.0

using System.Text.Json;
using NATS.Client.Core;
using NATS.Net;

var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4233";
var keycloak = Environment.GetEnvironmentVariable("KEYCLOAK_URL") ?? "http://localhost:8903";
var pass = 0;
var fail = 0;

// ---------- Keycloak tokens (real OIDC password + client-credentials flows) ----------
using var http = new HttpClient();

async Task<string> PasswordToken(string clientId, string user, string pw)
{
    var resp = await http.PostAsync(
        $"{keycloak}/realms/secure-example/protocol/openid-connect/token",
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = clientId,
            ["username"] = user, ["password"] = pw,
        }));
    using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    return json.RootElement.GetProperty("access_token").GetString()!;
}

async Task<string> ClientCredentialsToken(string clientId, string secret)
{
    var resp = await http.PostAsync(
        $"{keycloak}/realms/secure-example/protocol/openid-connect/token",
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret,
        }));
    using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    return json.RootElement.GetProperty("access_token").GetString()!;
}

var aliceToken = await PasswordToken("notes-web", "alice", "alice");     // household Fam42 -> tenant fam42
var bobToken = await PasswordToken("notes-web", "bob", "bob");           // household Fam99 -> tenant fam99
var backendToken = await ClientCredentialsToken("notes-backend", "secure-example-backend-secret");

// ---------- helpers ----------
NatsClient Client(string? token) => new(new NatsOpts
{
    Url = natsUrl,
    AuthOpts = token is null ? NatsAuthOpts.Default : new NatsAuthOpts { Token = token },
});

void Check(string name, bool okResult)
{
    if (okResult) { pass++; Console.WriteLine($"  PASS: {name}"); }
    else { fail++; Console.WriteLine($"  FAIL: {name}"); }
}

// Subscribe with `receiver`, publish with `sender`, report whether the message arrived.
async Task<bool> Delivered(NatsClient receiver, string subSubject, NatsClient sender, string pubSubject, string payload)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var got = new TaskCompletionSource<bool>();
    var sub = Task.Run(async () =>
    {
        try
        {
            await foreach (var msg in receiver.SubscribeAsync<string>(subSubject, cancellationToken: cts.Token))
            {
                if (msg.Data == payload) { got.TrySetResult(true); return; }
            }
        }
        catch (OperationCanceledException) { }
        got.TrySetResult(false);
    });
    await Task.Delay(400); // let the subscription land (or get killed by a violation)
    await sender.PublishAsync(pubSubject, payload);
    await sender.PingAsync(); // round-trip: the publish has reached the server
    var winner = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(3)));
    cts.Cancel();
    await sub;
    return winner == got.Task && got.Task.Result;
}

// ---------- 1. pre-authenticated users can authenticate ----------
Console.WriteLine("== 1. Pre-authenticated (anonymous) users can connect ==");
await using var anonA = Client(null);
await using var anonB = Client(null);
try
{
    await anonA.ConnectAsync();
    await anonB.ConnectAsync();
    Check("two anonymous clients connect (callout mints the anon grant)", true);
}
catch (Exception e)
{
    Check($"anonymous connect ({e.Message})", false);
}

// ---------- authenticated clients ----------
await using var alice = Client(aliceToken);
await using var bob = Client(bobToken);
await using var backend = Client(backendToken);
await alice.ConnectAsync();
await bob.ConnectAsync();
await backend.ConnectAsync();

Console.WriteLine("== 2. Anonymous no-cross-talk (delivery checks) ==");
// Anon A tries to reach anon B on an arbitrary subject.
Check("anon->anon on arbitrary subject (anon.chat) is NOT delivered",
    !await Delivered(anonB, "anon.chat", anonA, "anon.chat", "psst"));
// Anon A tries to forge a broadcast into the shared subscribe subject.
// NOTE: this is the TL;DR #2 empty-publish-allow gap — expected to FAIL until
// UserJwtMinter mints an explicit publish deny for empty publish unions.
Check("anon->anon via shared notes.public.> is NOT delivered (minter deny gap)",
    !await Delivered(anonB, "notes.public.>", anonA, "notes.public.spoof", "forged"));
// Anon tries to read tenant traffic published by an authorized client.
Check("anon cannot receive tenant events (sub denied)",
    !await Delivered(anonA, "notes.fam42.events.>", backend, "notes.fam42.events.x", "tenant-secret"));
// The legitimate anon path still works: backend broadcast -> anon subscriber.
Check("anon DOES receive legitimate notes.public.> broadcasts",
    await Delivered(anonA, "notes.public.>", backend, "notes.public.announcements", "hello world"));

Console.WriteLine("== 3. Authenticated member (alice, tenant fam42) ==");
Check("alice receives events on her own tenant",
    await Delivered(alice, "notes.fam42.events.>", backend, "notes.fam42.events.created", "note-1"));
Check("alice's tenant command reaches the backend",
    await Delivered(backend, "notes.*.commands.>", alice, "notes.fam42.commands.create", "new-note"));

Console.WriteLine("== 4. Tenant isolation between authenticated users ==");
Check("alice CANNOT receive bob's tenant events (sub fam99 denied)",
    !await Delivered(alice, "notes.fam99.events.>", backend, "notes.fam99.events.created", "bobs-note"));
Check("alice CANNOT publish into bob's tenant (pub fam99 dropped)",
    !await Delivered(backend, "notes.fam99.commands.>", alice, "notes.fam99.commands.create", "cross-tenant"));

Console.WriteLine("== 5. End-to-end flow: alice -> backend -> alice, bob sees nothing ==");
var bobSaw = Delivered(bob, "notes.fam99.events.>", backend, "notes.fam42.events.created", "note-2");
var aliceSaw = Delivered(alice, "notes.fam42.events.>", backend, "notes.fam42.events.created", "note-2");
Check("alice received her tenant's event", await aliceSaw);
Check("bob saw none of alice's flow", !await bobSaw);

Console.WriteLine("== 6. Credential edge cases ==");
try
{
    await using var garbage = Client("not-a-jwt");
    await garbage.ConnectAsync();
    // Connect may succeed at TCP level; prove it's useless: nothing can be delivered.
    Check("garbage token gets no delivery anywhere",
        !await Delivered(garbage, "notes.public.>", backend, "notes.public.ping", "ping"));
}
catch (Exception)
{
    Check("garbage token rejected at connect", true);
}
Check("bob can use the static notes.public.> grant",
    await Delivered(bob, "notes.public.>", backend, "notes.public.greetings", "hi all"));

Console.WriteLine();
Console.WriteLine($"RESULT: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;
