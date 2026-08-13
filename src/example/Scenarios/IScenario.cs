namespace example;

internal interface IScenario {

    string Name { get; }

    string Description { get; }

    string BrowserPath => "/";

    string Usage { get; }

    Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken);

}
