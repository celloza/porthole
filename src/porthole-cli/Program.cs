using System.CommandLine;
using porthole_cli.Commands;

var root = new RootCommand("Porthole DevContainers compatibility CLI shim.")
{
    TreatUnmatchedTokensAsErrors = false,
};

root.Add(new InspectCommand());
root.Add(new RunCommand());
root.Add(new VersionCommand());
root.Add(new PsCommand());
root.Add(new ExecCommand());

root.SetAction((ParseResult parseResult) =>
{
    if (parseResult.Errors.Count > 0)
    {
        foreach (var error in parseResult.Errors)
        {
            Console.Error.WriteLine(error.Message);
        }

        return 1;
    }

    Console.Error.WriteLine("Unsupported command. Use --help for available commands.");
    return 1;
});

try
{
    ParseResult parseResult = root.Parse(args);
    return await parseResult.InvokeAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
