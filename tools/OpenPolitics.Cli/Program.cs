using OpenPolitics.Cli.Commands;
using OpenPolitics.Core;

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine($"OpenPolitics {CoreInfo.Version}");
    return 0;
}

if (args.Length >= 1 && args[0] == "validate")
    return ValidateCommand.Execute(args[1..], Console.Out);

if (args.Length >= 1 && args[0] == "run")
    return RunCommand.Execute(args[1..], Console.Out);

if (args.Length >= 1 && args[0] == "play")
    return PlayCommand.Execute(args[1..], Console.In, Console.Out);

if (args.Length >= 1 && args[0] == "batch")
    return BatchCommand.Execute(args[1..], Console.Out);

Console.Error.WriteLine("Usage: openpolitics <play|validate|run|batch> ... | --version");
return 2;
