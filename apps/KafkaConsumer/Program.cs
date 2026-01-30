using Confluent.Kafka;
using Spectre.Console;

var bootstrapServers = args.Length > 0 ? args[0] : "localhost:31094";
var topic = "demo-messages";
var groupId = "demo-consumer-group";

AnsiConsole.Write(new FigletText("Kafka Consumer").Color(Color.Cyan1));
AnsiConsole.MarkupLine($"[grey]Bootstrap: {bootstrapServers}[/]");
AnsiConsole.MarkupLine($"[grey]Topic:     {topic}[/]");
AnsiConsole.MarkupLine($"[grey]Group:     {groupId}[/]");
AnsiConsole.MarkupLine("[grey]Nasluchiwanie... Ctrl+C aby zakonczyc.[/]");
AnsiConsole.WriteLine();

var config = new ConsumerConfig
{
    BootstrapServers = bootstrapServers,
    GroupId = groupId,
    AutoOffsetReset = AutoOffsetReset.Earliest,
    EnableAutoCommit = true,
    SessionTimeoutMs = 10000
};

using var consumer = new ConsumerBuilder<string, string>(config)
    .SetErrorHandler((_, e) => AnsiConsole.MarkupLine($"[red]Blad: {e.Reason}[/]"))
    .Build();

consumer.Subscribe(topic);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var table = new Table()
    .Border(TableBorder.Rounded)
    .AddColumn("[cyan]Czas[/]")
    .AddColumn("[green]Klucz[/]")
    .AddColumn("[white]Wiadomosc[/]");

AnsiConsole.Write(table);

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var result = consumer.Consume(cts.Token);

            var timestamp = result.Message.Timestamp.UtcDateTime.ToString("HH:mm:ss");
            var key = result.Message.Key ?? "(brak)";
            var value = result.Message.Value;

            AnsiConsole.MarkupLine(
                $"  [cyan]{timestamp}[/]  [green]{key}[/]  [white]{value}[/]");
        }
        catch (ConsumeException ex)
        {
            AnsiConsole.MarkupLine($"[red]Blad odbioru: {ex.Error.Reason}[/]");
        }
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C
}

consumer.Close();
AnsiConsole.MarkupLine("[yellow]Consumer zakonczony.[/]");
