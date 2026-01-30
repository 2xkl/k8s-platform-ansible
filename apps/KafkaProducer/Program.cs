using Confluent.Kafka;
using Spectre.Console;

var bootstrapServers = args.Length > 0 ? args[0] : "localhost:31094";
var topic = "demo-messages";

AnsiConsole.Write(new FigletText("Kafka Producer").Color(Color.Green));
AnsiConsole.MarkupLine($"[grey]Bootstrap: {bootstrapServers}[/]");
AnsiConsole.MarkupLine($"[grey]Topic:     {topic}[/]");
AnsiConsole.MarkupLine("[grey]Wpisz wiadomosc i nacisnij Enter. 'exit' konczy program.[/]");
AnsiConsole.WriteLine();

var config = new ProducerConfig
{
    BootstrapServers = bootstrapServers,
    Acks = Acks.All,
    MessageTimeoutMs = 5000
};

using var producer = new ProducerBuilder<string, string>(config)
    .SetErrorHandler((_, e) => AnsiConsole.MarkupLine($"[red]Blad: {e.Reason}[/]"))
    .Build();

var messageCount = 0;

while (true)
{
    AnsiConsole.Markup("[blue]> [/]");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    try
    {
        messageCount++;
        var key = $"msg-{messageCount}";

        var result = await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = input
        });

        AnsiConsole.MarkupLine(
            $"[green]OK[/] [grey]partition={result.Partition.Value} " +
            $"offset={result.Offset.Value} " +
            $"timestamp={result.Timestamp.UtcDateTime:HH:mm:ss}[/]");
    }
    catch (ProduceException<string, string> ex)
    {
        AnsiConsole.MarkupLine($"[red]Blad wysylania: {ex.Error.Reason}[/]");
    }
}

producer.Flush(TimeSpan.FromSeconds(5));
AnsiConsole.MarkupLine("[yellow]Producer zakonczony.[/]");
