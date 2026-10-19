using Foundation.Models;
using MS.Promocoes.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Invalid(InteressePromocao interesse)
{
    try { interesse.Normalizar(); }
    catch (ArgumentException) { return; }
    throw new Exception("Invalid interest was accepted");
}

var normalized = new InteressePromocao { Email = "  CLIENTE@example.com ", Categorias = [" b ", "A", "a"] }.Normalizar();
Check(normalized.Email == "cliente@example.com" && normalized.Categorias.SequenceEqual(new[] { "A", "B" }), "Normalization failed");
foreach (string email in new[] { "", "invalido", "user@localhost", "Nome <user@example.com>" })
    Invalid(new() { Email = email, Categorias = ["A"] });
Invalid(new() { Email = "user@example.com", Categorias = [] });
Invalid(new() { Email = "user@example.com", Categorias = ["D"] });
Invalid(new() { Email = "user@example.com", Categorias = [null!] });
Invalid(new() { Email = "user@example.com", Categorias = null! });
Invalid(new() { Email = "user@example.com", Cancelado = true, Categorias = ["A"] });
Console.WriteLine("PASS: email/category validation and normalization");

string scratchRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "scratch"));
string directory = Path.Combine(scratchRoot, "interesses-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    string path = Path.Combine(directory, "interesses.json");
    var store = new InteressesStore(path);
    Check(store.Aplicar(normalized, "register-1"), "First event must apply");
    Check(!store.Aplicar(normalized, "register-1"), "Duplicate event must be ignored");
    store.Aplicar(new() { Email = "CLIENTE@example.com", Categorias = ["C"] }, "update-1");
    Check(store.ListarDestinatarios("A").Count == 0 && store.ListarDestinatarios("C").Count == 1, "Update must replace categories");
    var restarted = new InteressesStore(path);
    Check(restarted.ListarDestinatarios("C").SequenceEqual(new[] { "cliente@example.com" }), "Interests must survive restart");
    Check(!restarted.Aplicar(normalized, "register-1"), "Processed message IDs must survive restart");
    restarted.Aplicar(new() { Email = "cliente@example.com", Cancelado = true }, "cancel-1");
    Check(new InteressesStore(path).ListarDestinatarios("C").Count == 0, "Cancellation must persist");
    Check(!restarted.Aplicar(normalized, "register-1"), "Old redelivery must not reactivate a cancelled subscription");
    restarted.Aplicar(new() { Email = "cliente@example.com", Cancelado = true }, "cancel-2");
    restarted.Aplicar(new() { Email = "cliente@example.com", Categorias = ["B"] }, "register-2");
    Check(restarted.ListarDestinatarios("B").Count == 1, "Fresh subscription after cancellation must succeed");
    Console.WriteLine("PASS: upsert, deduplication, persistence, cancellation, redelivery and resubscription");

    // Concurrent registrations for one address must produce one valid durable record.
    await Task.WhenAll(Enumerable.Range(0, 10).Select(n => Task.Run(() =>
        restarted.Aplicar(new() { Email = "outro@example.com", Categorias = ["A"] }, "parallel-" + n))));
    Check(new InteressesStore(path).ListarDestinatarios("A").Count == 1, "Concurrent writes must not duplicate the email or corrupt the file");

    string blocked = Path.Combine(directory, "blocked");
    File.WriteAllText(blocked, "not a directory");
    var failed = new InteressesStore(Path.Combine(blocked, "interesses.json"));
    try
    {
        failed.Aplicar(normalized, "retry");
        throw new Exception("Persistence failure must propagate");
    }
    catch (IOException) { }
    Check(failed.ListarDestinatarios("A").Count == 0, "A failed write must not change in-memory state");
    File.Delete(blocked);
    Check(failed.Aplicar(normalized, "retry"), "Failed write must allow retrying the same message");
    Console.WriteLine("PASS: concurrent writes and retry after persistence failure");
}
finally
{
    if (Path.GetDirectoryName(Path.GetFullPath(directory)) != scratchRoot)
        throw new InvalidOperationException("Refusing cleanup outside the temporary directory.");
    Directory.Delete(directory, recursive: true);
}
